// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// RIPA2 — Real-time Index of Pupillary Activity (v2).
    ///
    /// Two Savitzky–Golay first-derivative filters are applied to a rolling
    /// pupil-diameter buffer at the same effective time index. The metric is
    ///   RIPA2[t] = (SG_LF · P[t±M_LF])^2 − (SG_VLF · P[t±M_VLF])^2
    /// clipped to [0, 1.5]. A trailing moving average then smooths the raw
    /// stream over a configurable window (typically 1–2 s).
    ///
    /// Reference: Jayawardena, Jayawardana, Gwizdka 2025, "Measuring Mental
    /// Effort in Real Time Using Pupillometry", J. Eye Movement Research
    /// 18(6) #70, https://doi.org/10.3390/jemr18060070
    ///
    /// Pure C# — no Unity types; safe to construct in EditMode tests, in a
    /// background thread, or from a Python parity harness via P/Invoke.
    ///
    /// The output value is in (mm/s)^2 — the SG outputs are rescaled by
    /// the configured sample rate before squaring so the difference fits
    /// the paper's [0, 1.5] clip range across the typical pupil-dynamics
    /// envelope. Higher values indicate more cognitive load.
    /// </summary>
    public sealed class RIPA2Analyzer
    {
        public const double ClipMin = 0.0;
        public const double ClipMax = 1.5;

        public SavitzkyGolayDerivative SgVlf { get; }
        public SavitzkyGolayDerivative SgLf { get; }
        public float SampleRateHz { get; }
        public int BufferSize { get; }
        public int SmoothingSize { get; }

        public double CurrentRaw { get; private set; }
        public double CurrentSmoothed { get; private set; }
        public bool IsValid => ringFilled >= SgVlf.WindowSize;
        public int SamplesBuffered => ringFilled;

        private readonly double[] ringBuffer;
        private int ringHead;
        private int ringFilled;

        private readonly double[] smoothRing;
        private int smoothHead;
        private int smoothFilled;
        private double smoothSum;

        public RIPA2Analyzer(
            float sampleRateHz,
            int vlfHalfWidth, int vlfPolyOrder,
            int lfHalfWidth, int lfPolyOrder,
            int bufferSize,
            int smoothingSize)
        {
            if (sampleRateHz <= 0f) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (lfHalfWidth > vlfHalfWidth) throw new ArgumentException("LF half-width must be <= VLF half-width (LF window must fit inside VLF window)");
            SampleRateHz = sampleRateHz;
            SgVlf = new SavitzkyGolayDerivative(vlfHalfWidth, vlfPolyOrder);
            SgLf = new SavitzkyGolayDerivative(lfHalfWidth, lfPolyOrder);
            BufferSize = Math.Max(SgVlf.WindowSize, bufferSize);
            SmoothingSize = Math.Max(1, smoothingSize);
            ringBuffer = new double[BufferSize];
            smoothRing = new double[SmoothingSize];
        }

        /// <summary>
        /// Convenience constructor that sizes the SG filters from target
        /// cutoff frequencies via Schäfer's approximation, sets the buffer
        /// to <paramref name="bufferSeconds"/> of data, and the smoothing
        /// window to <paramref name="smoothingSeconds"/>.
        /// </summary>
        public static RIPA2Analyzer FromCutoffs(
            float sampleRateHz,
            float vlfCutoffHz, int vlfPolyOrder,
            float lfCutoffHz, int lfPolyOrder,
            float bufferSeconds,
            float smoothingSeconds)
        {
            int mVlf = SavitzkyGolayDerivative.DeriveHalfWidth(sampleRateHz, vlfCutoffHz, vlfPolyOrder);
            int mLf = SavitzkyGolayDerivative.DeriveHalfWidth(sampleRateHz, lfCutoffHz, lfPolyOrder);
            int buffer = (int)Math.Ceiling(bufferSeconds * sampleRateHz);
            int smooth = (int)Math.Ceiling(smoothingSeconds * sampleRateHz);
            return new RIPA2Analyzer(sampleRateHz, mVlf, vlfPolyOrder, mLf, lfPolyOrder, buffer, smooth);
        }

        public void Reset()
        {
            ringHead = 0;
            ringFilled = 0;
            smoothHead = 0;
            smoothFilled = 0;
            smoothSum = 0.0;
            CurrentRaw = 0.0;
            CurrentSmoothed = 0.0;
        }

        /// <summary>
        /// Push a new pupil-diameter sample. Updates <see cref="CurrentRaw"/>
        /// and <see cref="CurrentSmoothed"/> once the buffer has filled to
        /// the VLF window size. Non-finite samples are ignored.
        /// </summary>
        public void PushSample(double pupilMm)
        {
            if (double.IsNaN(pupilMm) || double.IsInfinity(pupilMm)) return;
            ringBuffer[ringHead] = pupilMm;
            ringHead++;
            if (ringHead == BufferSize) ringHead = 0;
            if (ringFilled < BufferSize) ringFilled++;

            int W = SgVlf.WindowSize;
            if (ringFilled < W) return;

            // The most recent sample is at (ringHead - 1 + n) % n. The
            // VLF window's last sample is that one; its first sample is
            // 2*M_VLF samples earlier in the ring.
            int vlfStart = ringHead - W;
            if (vlfStart < 0) vlfStart += BufferSize;
            // LF window centered at the same time index: starts (M_VLF − M_LF)
            // samples after the VLF start.
            int lfStart = vlfStart + (SgVlf.HalfWidth - SgLf.HalfWidth);
            if (lfStart >= BufferSize) lfStart -= BufferSize;

            double valueVlf = SgVlf.ApplyRing(ringBuffer, vlfStart);
            double valueLf = SgLf.ApplyRing(ringBuffer, lfStart);

            // The Savitzky-Golay coefficients deliver a PER-SAMPLE derivative;
            // the paper's RIPA2 (and its [0, 1.5] clip range) assumes a
            // PER-SECOND derivative. Without this time-base conversion, the
            // squared derivatives are underscaled by sampleRateHz^2 (at
            // 120 Hz that's a 14,400× collapse into the noise floor).
            // Rescaling each SG output by SampleRateHz before squaring
            // restores the paper's published range and preserves the
            // constant-signal-to-zero invariant.
            valueVlf *= SampleRateHz;
            valueLf *= SampleRateHz;

            double raw = (valueLf * valueLf) - (valueVlf * valueVlf);
            if (raw < ClipMin) raw = ClipMin;
            else if (raw > ClipMax) raw = ClipMax;
            CurrentRaw = raw;

            // Trailing moving average over SmoothingSize raw outputs.
            if (smoothFilled == SmoothingSize) smoothSum -= smoothRing[smoothHead];
            smoothRing[smoothHead] = raw;
            smoothHead++;
            if (smoothHead == SmoothingSize) smoothHead = 0;
            if (smoothFilled < SmoothingSize) smoothFilled++;
            smoothSum += raw;
            CurrentSmoothed = smoothSum / smoothFilled;
        }

        /// <summary>
        /// One-shot computation: feed an already-acquired window of pupil
        /// samples and return the (clipped) raw RIPA2 value evaluated at
        /// the window center. Used for golden-output tests and Python parity.
        /// </summary>
        public double ComputeRawFromWindow(double[] pupil)
        {
            if (pupil == null || pupil.Length < SgVlf.WindowSize) return 0.0;
            int vlfStart = pupil.Length - SgVlf.WindowSize;
            int lfStart = vlfStart + (SgVlf.HalfWidth - SgLf.HalfWidth);
            double valueVlf = SgVlf.Apply(pupil, vlfStart);
            double valueLf = SgLf.Apply(pupil, lfStart);
            // Match the streaming path's per-second rescaling so a one-shot
            // window-based call returns the same value as the analyzer's
            // streaming CurrentRaw given the same data.
            valueVlf *= SampleRateHz;
            valueLf *= SampleRateHz;
            double raw = (valueLf * valueLf) - (valueVlf * valueVlf);
            if (raw < ClipMin) raw = ClipMin;
            else if (raw > ClipMax) raw = ClipMax;
            return raw;
        }
    }
}
