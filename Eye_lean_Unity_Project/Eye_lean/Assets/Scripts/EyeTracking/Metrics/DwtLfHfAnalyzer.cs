// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// LF/HF cognitive-load detector via Daubechies-4 DWT, per Duchowski 2026
    /// §3 / Listing 2 (PupilDWTRatioDetector).
    ///
    /// Pipeline (when the buffer fills, on each pushed sample):
    ///   1. Copy the rolling pupil buffer into a working array.
    ///   2. Remove the mean (Listing 2 uses mean-subtraction, not linear detrend).
    ///   3. Multi-level db4 wavelet decomposition (<see cref="Db4DwtDecomposer"/>).
    ///   4. Sum squared coefficients per detail level into LF or HF band by
    ///      the band-frequency mapping from paper §5.2:
    ///        detail level d covers [fs / 2^(d+1), fs / 2^d].
    ///      The deepest-level approximation contributes to LF.
    ///   5. Emit ratio = LF_energy / HF_energy (capped at 20 per paper §7).
    ///
    /// Buffer length is a power of two so the cascade halves exactly each
    /// level. <see cref="BufferSamples"/> = 2048 (34.13 s at 60 Hz) covers
    /// the paper's 7.5 s minimum and 30 s default.
    ///
    /// Note on the paper's Listing 2: the published listing's loop indexes
    /// <c>coeffs[level]</c> while computing frequencies as <c>fs/2^level</c>,
    /// which inverts pywt's deepest-first ordering. This implementation uses
    /// the corrected mapping (paper §5.2 frequency table is authoritative).
    /// </summary>
    public sealed class DwtLfHfAnalyzer
    {
        public const double DefaultMaxRatio = 20.0;
        public const double DefaultSmoothedScale = 0.5;
        public const double SmoothedClipMax = 1.5;
        public const double EnergyNoiseFloor = 1e-12;

        public double SampleRateHz { get; }
        public int BufferSamples { get; }
        public int MaxLevel { get; }
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
        private readonly double[] work;

        public DwtLfHfAnalyzer(
            double sampleRateHz,
            int bufferSamples = 2048,
            int maxLevel = 8,
            double lowBandHz = 1.6,
            double highBandHz = 4.0,
            double maxRatio = DefaultMaxRatio,
            double smoothedScale = DefaultSmoothedScale)
        {
            if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (bufferSamples <= 0 || (bufferSamples & (bufferSamples - 1)) != 0)
                throw new ArgumentException("bufferSamples must be a power of two.", nameof(bufferSamples));
            if ((bufferSamples >> maxLevel) < 1) throw new ArgumentException("maxLevel too large for bufferSamples.", nameof(maxLevel));
            if (lowBandHz <= 0 || highBandHz <= lowBandHz || highBandHz >= sampleRateHz / 2)
                throw new ArgumentOutOfRangeException(nameof(highBandHz));
            if (maxRatio <= 0) throw new ArgumentOutOfRangeException(nameof(maxRatio));
            if (smoothedScale <= 0) throw new ArgumentOutOfRangeException(nameof(smoothedScale));
            SampleRateHz = sampleRateHz;
            BufferSamples = bufferSamples;
            MaxLevel = maxLevel;
            LowBandHz = lowBandHz;
            HighBandHz = highBandHz;
            MaxRatio = maxRatio;
            SmoothedScale = smoothedScale;
            ring = new double[bufferSamples];
            work = new double[bufferSamples];
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
            // Copy ring oldest-first into work.
            int n = BufferSamples;
            for (int i = 0; i < n; i++)
            {
                int src = head + i; if (src >= n) src -= n;
                work[i] = ring[src];
            }
            // Mean-subtract (Listing 2 uses np.mean, not linear detrend).
            double mean = 0;
            for (int i = 0; i < n; i++) mean += work[i];
            mean /= n;
            for (int i = 0; i < n; i++) work[i] -= mean;

            // Cascade db4 decomposition.
            double[][] coeffs = Db4DwtDecomposer.Decompose(work, MaxLevel);

            // Map detail levels to frequency bands and accumulate energy.
            // coeffs[i] for i in 1..MaxLevel corresponds to detail at decomp
            // level (MaxLevel - i + 1). That level covers
            // [fs / 2^(d+1), fs / 2^d].
            double lfEnergy = 0, hfEnergy = 0;
            for (int i = 1; i <= MaxLevel; i++)
            {
                int decompLevel = MaxLevel - i + 1;
                double fHigh = SampleRateHz / (1 << decompLevel);
                double fLow = SampleRateHz / (1 << (decompLevel + 1));
                double e = 0;
                var c = coeffs[i];
                for (int k = 0; k < c.Length; k++) e += c[k] * c[k];
                bool inLf = (fLow <= LowBandHz) && (fHigh >= 0);
                bool inHf = (fLow <= HighBandHz) && (fHigh >= LowBandHz);
                if (inHf) hfEnergy += e;
                else if (inLf) lfEnergy += e;
            }
            // Approximation at the deepest level: [0, fs / 2^(MaxLevel+1)],
            // which always falls inside the LF band for our parameters.
            double approxHigh = SampleRateHz / (1 << (MaxLevel + 1));
            if (approxHigh <= LowBandHz)
            {
                var ca = coeffs[0];
                for (int k = 0; k < ca.Length; k++) lfEnergy += ca[k] * ca[k];
            }

            if (lfEnergy < EnergyNoiseFloor) lfEnergy = 0;
            if (hfEnergy < EnergyNoiseFloor) hfEnergy = 0;

            double ratio;
            if (hfEnergy <= 0) ratio = (lfEnergy > 0) ? MaxRatio : 0.0;
            else { ratio = lfEnergy / hfEnergy; if (ratio > MaxRatio) ratio = MaxRatio; }
            CurrentRawRatio = ratio;

            double smoothed = Math.Log(1.0 + ratio) * SmoothedScale;
            if (smoothed > SmoothedClipMax) smoothed = SmoothedClipMax;
            CurrentSmoothed = smoothed;
        }
    }
}
