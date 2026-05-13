// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// LF/HF cognitive-load detector via periodogram (FFT-based PSD), per
    /// Duchowski 2026 §2 / Listing 1 (PupilFrequencyRatioDetector).
    ///
    /// Pipeline (when the buffer fills, on each pushed sample):
    ///   1. Copy the rolling pupil buffer into a working array.
    ///   2. Linear-detrend (matches <c>scipy.signal.detrend</c>).
    ///   3. Forward FFT (Cooley–Tukey radix-2; <see cref="Fft"/>).
    ///   4. Periodogram: P(f) = |X(f)|² / (fs · N).
    ///   5. Sum P(f) in LF (0–1.6 Hz) and HF (1.6–4 Hz) bands.
    ///   6. Emit ratio = LF / HF (capped at 20 per paper §7).
    ///
    /// Buffer length is a power of two so the FFT runs in-place without
    /// zero-padding. <see cref="BufferSamples"/> = 2048 (34.13 s at 60 Hz)
    /// comfortably exceeds the paper's 10 s minimum and matches the paper's
    /// 30 s default window. Δf = fs / N (0.0293 Hz at 60 Hz, 2048).
    ///
    /// Pure C# — no Unity types; constructable in EditMode tests and runnable
    /// in a background thread.
    /// </summary>
    public sealed class FftLfHfAnalyzer
    {
        public const double DefaultMaxRatio = 20.0;
        public const double DefaultSmoothedScale = 0.5;
        public const double SmoothedClipMax = 1.5;
        public const double VarianceNoiseFloor = 1e-12;

        public double SampleRateHz { get; }
        public int BufferSamples { get; }
        public double LowBandHz { get; }
        public double HighBandHz { get; }
        public double BufferSeconds => BufferSamples / SampleRateHz;
        public double MaxRatio { get; }
        public double SmoothedScale { get; }

        public double CurrentRawRatio { get; private set; }
        public double CurrentSmoothed { get; private set; }
        public bool IsValid => filled >= BufferSamples;
        public int SamplesPushed { get; private set; }

        private readonly double[] ring;
        private int head;
        private int filled;

        // Working arrays reused across updates to avoid GC pressure.
        private readonly double[] workReal;
        private readonly double[] workImag;

        public FftLfHfAnalyzer(
            double sampleRateHz,
            int bufferSamples = 2048,
            double lowBandHz = 1.6,
            double highBandHz = 4.0,
            double maxRatio = DefaultMaxRatio,
            double smoothedScale = DefaultSmoothedScale)
        {
            if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (bufferSamples <= 0 || (bufferSamples & (bufferSamples - 1)) != 0)
                throw new ArgumentException("bufferSamples must be a power of two.", nameof(bufferSamples));
            if (lowBandHz <= 0 || highBandHz <= lowBandHz || highBandHz >= sampleRateHz / 2)
                throw new ArgumentOutOfRangeException(nameof(highBandHz), $"Need 0 < low < high < fs/2; got low={lowBandHz}, high={highBandHz}, fs={sampleRateHz}.");
            if (maxRatio <= 0) throw new ArgumentOutOfRangeException(nameof(maxRatio));
            if (smoothedScale <= 0) throw new ArgumentOutOfRangeException(nameof(smoothedScale));
            SampleRateHz = sampleRateHz;
            BufferSamples = bufferSamples;
            LowBandHz = lowBandHz;
            HighBandHz = highBandHz;
            MaxRatio = maxRatio;
            SmoothedScale = smoothedScale;
            ring = new double[bufferSamples];
            workReal = new double[bufferSamples];
            workImag = new double[bufferSamples];
        }

        public void Reset()
        {
            for (int i = 0; i < BufferSamples; i++) ring[i] = 0;
            head = filled = 0;
            CurrentRawRatio = 0;
            CurrentSmoothed = 0;
            SamplesPushed = 0;
        }

        public void PushSample(double pupilMm)
        {
            if (double.IsNaN(pupilMm) || double.IsInfinity(pupilMm)) return;
            SamplesPushed++;

            ring[head] = pupilMm;
            head++; if (head == BufferSamples) head = 0;
            if (filled < BufferSamples) filled++;
            if (filled < BufferSamples) return;

            Recompute();
        }

        private void Recompute()
        {
            // Copy ring into work array in chronological order (oldest first).
            // The ring's oldest sample is at index `head` (since head is the
            // next write position once the ring is full).
            int n = BufferSamples;
            for (int i = 0; i < n; i++)
            {
                int src = head + i; if (src >= n) src -= n;
                workReal[i] = ring[src];
                workImag[i] = 0.0;
            }
            Fft.LinearDetrendInPlace(workReal);
            Fft.TransformInPlace(workReal, workImag);

            // Periodogram (single-sided): P(f_k) = |X_k|² / (fs · N) for DC
            // and Nyquist; multiplied by 2 elsewhere. We sum bands strictly
            // inside (0, fs/2), so the 2× factor cancels in the LF/HF ratio.
            double dfHz = SampleRateHz / n;
            int kLfHi = (int)Math.Floor(LowBandHz / dfHz);            // inclusive
            int kHfLo = kLfHi + 1;                                     // strictly > LowBandHz
            int kHfHi = (int)Math.Floor(HighBandHz / dfHz);
            int kNyq = n / 2;
            if (kHfHi > kNyq) kHfHi = kNyq;

            double lfPower = 0.0;
            // DC bin is in LF band (paper's band starts at 0 Hz inclusive).
            lfPower += (workReal[0] * workReal[0] + workImag[0] * workImag[0]) / (SampleRateHz * n);
            for (int k = 1; k <= kLfHi && k < kNyq; k++)
            {
                lfPower += 2.0 * (workReal[k] * workReal[k] + workImag[k] * workImag[k]) / (SampleRateHz * n);
            }

            double hfPower = 0.0;
            for (int k = kHfLo; k <= kHfHi && k < kNyq; k++)
            {
                hfPower += 2.0 * (workReal[k] * workReal[k] + workImag[k] * workImag[k]) / (SampleRateHz * n);
            }
            // Nyquist bin (if it lands in the HF band): no 2× factor.
            if (kHfHi == kNyq)
            {
                hfPower += (workReal[kNyq] * workReal[kNyq] + workImag[kNyq] * workImag[kNyq]) / (SampleRateHz * n);
            }

            if (lfPower < VarianceNoiseFloor) lfPower = 0;
            if (hfPower < VarianceNoiseFloor) hfPower = 0;

            double ratio;
            if (hfPower <= 0) ratio = (lfPower > 0) ? MaxRatio : 0.0;
            else { ratio = lfPower / hfPower; if (ratio > MaxRatio) ratio = MaxRatio; }
            CurrentRawRatio = ratio;

            double smoothed = Math.Log(1.0 + ratio) * SmoothedScale;
            if (smoothed > SmoothedClipMax) smoothed = SmoothedClipMax;
            CurrentSmoothed = smoothed;
        }
    }
}
