// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Real-time IIR (Butterworth) cognitive-load detector that computes the
    /// LF/HF power ratio of the pupil diameter signal, per Duchowski 2026,
    /// "Real-Time Cognitive Load Measurement of Pupillary Oscillation",
    /// Proc. ACM CGIT 9(2) Article 23 §4 / Listing 3 (PupilFilterRatioDetector).
    ///
    /// Pipeline (per pushed sample x[n]):
    ///   1. Run x through a 4th-order Butterworth lowpass filter (cutoff
    ///      <see cref="LowBandHz"/>) → LF-band signal ℓ[n].
    ///   2. Run x through a 4th-order Butterworth bandpass filter (passband
    ///      <see cref="LowBandHz"/>–<see cref="HighBandHz"/>) → HF-band ℎ[n].
    ///   3. Maintain rolling variance buffers of length
    ///      <see cref="PowerWindowSeconds"/> on ℓ and ℎ.
    ///   4. Once both buffers are full, emit ratio = Var(ℓ) / Var(ℎ).
    ///
    /// The ratio is non-negative and unbounded above. Per §7 of the paper,
    /// practical range is [0, 20] (typically 1–10 at rest, 2–20 under
    /// cognitive load). For HUD use, prefer <see cref="CurrentSmoothed"/>
    /// which is log(1 + ratio) scaled into the same [0, 1.5] range as RIPA2.
    ///
    /// Sign convention: increasing LF/HF generally indicates cognitive load
    /// under arithmetic-type tasks; n-back tasks produce a *decrease*. See
    /// §7.2 of the paper for the task-specific interpretation.
    ///
    /// Device-bandwidth caveat: the paper's "practical [0, 20]" range
    /// assumes a high-bandwidth raw pupil stream (e.g. Eyelink 1000 at
    /// 1000 Hz). HMD eye trackers that pre-smooth their pupil output
    /// (observed on VIVE Focus Vision: 98 % of signal power lives below
    /// 1.6 Hz, leaving the HF band starved of energy) produce much larger
    /// raw ratios — typically 50–200. <see cref="MaxRatio"/> and
    /// <see cref="SmoothedScale"/> let callers retune for their device so
    /// the HUD doesn't pin at the clip ceiling.
    ///
    /// Pure C# — no Unity types; safe to construct in EditMode tests or in a
    /// background thread.
    /// </summary>
    public sealed class ButterworthLfHfAnalyzer
    {
        // Paper-recommended HUD scaling: log(1+ratio) compresses the raw
        // ratio into the [0, SmoothedClipMax] band. Default scale 0.5 maps
        // the paper's [0, 20] cap to ~1.52 (just above the 1.5 clip), so a
        // "typical baseline" ratio lands mid-gauge for paper-quality data.
        // For pre-smoothed HMD signals the scale needs to be smaller and
        // the cap larger — see the constructor params.
        public const double DefaultMaxRatio = 20.0;
        public const double DefaultSmoothedScale = 0.5;
        public const double SmoothedClipMax = 1.5;

        // Numerical-noise floor for band-variance values. Real pupil
        // oscillations under cognitive load have variances of ~0.01–1 mm²;
        // anything 12 orders of magnitude below that is roundoff from the
        // filter's steady-state representation, not signal. Variances below
        // this floor are treated as exactly zero, so a perfectly constant
        // pupil produces ratio = 0 instead of a nonsense ratio-of-roundoff.
        public const double VarianceNoiseFloor = 1e-12;

        public double SampleRateHz { get; }
        public double LowBandHz { get; }
        public double HighBandHz { get; }
        public int FilterOrder { get; }
        public int PowerWindowSamples { get; }
        public double PowerWindowSeconds => PowerWindowSamples / SampleRateHz;
        /// <summary>Raw LF/HF ratio is clipped to this ceiling (paper default 20).</summary>
        public double MaxRatio { get; }
        /// <summary>Smoothed = log(1 + raw) · SmoothedScale, clipped at <see cref="SmoothedClipMax"/>.</summary>
        public double SmoothedScale { get; }

        public double CurrentRawRatio { get; private set; }
        public double CurrentSmoothed { get; private set; }
        public bool IsValid => lfFilled >= PowerWindowSamples && hfFilled >= PowerWindowSamples;
        public int SamplesPushed { get; private set; }

        private readonly SosFilter lfFilter;
        private readonly SosFilter hfFilter;
        private bool filtersPrimed;

        // Running variance buffers using Welford-style incremental updates.
        // Storing the raw samples in a ring keeps the variance numerically
        // stable as old samples are evicted.
        private readonly double[] lfRing;
        private readonly double[] hfRing;
        private int lfHead, hfHead, lfFilled, hfFilled;
        private double lfSum, hfSum;
        private double lfSumSq, hfSumSq;

        /// <summary>
        /// Construct an analyzer with the paper's default band edges and
        /// 5-second power window. The lowpass filter handles the LF band when
        /// lowBandHz starts at 0 (a bandpass with lower cutoff 0 is ill-posed).
        /// </summary>
        public ButterworthLfHfAnalyzer(
            double sampleRateHz,
            double lowBandHz = 1.6,
            double highBandHz = 4.0,
            int filterOrder = 4,
            double powerWindowSeconds = 5.0,
            double maxRatio = DefaultMaxRatio,
            double smoothedScale = DefaultSmoothedScale)
        {
            if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (powerWindowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(powerWindowSeconds));
            if (maxRatio <= 0) throw new ArgumentOutOfRangeException(nameof(maxRatio));
            if (smoothedScale <= 0) throw new ArgumentOutOfRangeException(nameof(smoothedScale));
            SampleRateHz = sampleRateHz;
            LowBandHz = lowBandHz;
            HighBandHz = highBandHz;
            FilterOrder = filterOrder;
            PowerWindowSamples = (int)Math.Ceiling(powerWindowSeconds * sampleRateHz);
            MaxRatio = maxRatio;
            SmoothedScale = smoothedScale;

            // LF: lowpass at lowBandHz (paper's Listing 3 falls back to
            // lowpass when the bandpass would have a 0 Hz lower edge).
            double[,] lfSos = IirButterworthDesign.LowpassSos(filterOrder, lowBandHz, sampleRateHz);
            double[,] hfSos = IirButterworthDesign.BandpassSos(filterOrder, lowBandHz, highBandHz, sampleRateHz);
            lfFilter = new SosFilter(lfSos);
            hfFilter = new SosFilter(hfSos);

            lfRing = new double[PowerWindowSamples];
            hfRing = new double[PowerWindowSamples];
        }

        public void Reset()
        {
            lfFilter.Reset();
            hfFilter.Reset();
            filtersPrimed = false;
            for (int i = 0; i < PowerWindowSamples; i++) { lfRing[i] = 0; hfRing[i] = 0; }
            lfHead = hfHead = lfFilled = hfFilled = 0;
            lfSum = hfSum = lfSumSq = hfSumSq = 0;
            CurrentRawRatio = 0;
            CurrentSmoothed = 0;
            SamplesPushed = 0;
        }

        /// <summary>
        /// Push a single pupil-diameter sample (millimeters). Non-finite
        /// samples are ignored. Once both variance buffers fill,
        /// <see cref="CurrentRawRatio"/> and <see cref="CurrentSmoothed"/>
        /// are updated; before that they remain at their previous values.
        /// </summary>
        public void PushSample(double pupilMm)
        {
            if (double.IsNaN(pupilMm) || double.IsInfinity(pupilMm)) return;
            SamplesPushed++;

            // Match Listing 3: prime each filter's state to the steady-state
            // response for the first observed pupil sample so a constant-ish
            // baseline produces zero filter output transient. Without this
            // the BP filter rings for several seconds on startup and leaks
            // variance into the power buffer.
            if (!filtersPrimed)
            {
                lfFilter.InitializeForConstantInput(pupilMm);
                hfFilter.InitializeForConstantInput(pupilMm);
                filtersPrimed = true;
            }

            double lf = lfFilter.Push(pupilMm);
            double hf = hfFilter.Push(pupilMm);

            // Evict the oldest sample if the ring is full, then push the new.
            // Running sum + sum-of-squares lets us compute population variance
            // in O(1) per update: Var = E[X²] − E[X]².
            if (lfFilled == PowerWindowSamples)
            {
                double oldLf = lfRing[lfHead];
                lfSum -= oldLf;
                lfSumSq -= oldLf * oldLf;
            }
            lfRing[lfHead] = lf;
            lfHead++; if (lfHead == PowerWindowSamples) lfHead = 0;
            if (lfFilled < PowerWindowSamples) lfFilled++;
            lfSum += lf; lfSumSq += lf * lf;

            if (hfFilled == PowerWindowSamples)
            {
                double oldHf = hfRing[hfHead];
                hfSum -= oldHf;
                hfSumSq -= oldHf * oldHf;
            }
            hfRing[hfHead] = hf;
            hfHead++; if (hfHead == PowerWindowSamples) hfHead = 0;
            if (hfFilled < PowerWindowSamples) hfFilled++;
            hfSum += hf; hfSumSq += hf * hf;

            if (!IsValid) return;

            double meanLf = lfSum / PowerWindowSamples;
            double meanHf = hfSum / PowerWindowSamples;
            double varLf = lfSumSq / PowerWindowSamples - meanLf * meanLf;
            double varHf = hfSumSq / PowerWindowSamples - meanHf * meanHf;
            if (varLf < 0) varLf = 0; // floating-point drift guard
            if (varHf < 0) varHf = 0;
            // Floor sub-noise variances to zero so a quiescent (DC) pupil
            // does not produce a ratio dominated by floating-point roundoff.
            if (varLf < VarianceNoiseFloor) varLf = 0;
            if (varHf < VarianceNoiseFloor) varHf = 0;

            double ratio;
            if (varHf <= 0)
            {
                ratio = (varLf > 0) ? MaxRatio : 0.0;
            }
            else
            {
                ratio = varLf / varHf;
                if (ratio > MaxRatio) ratio = MaxRatio;
            }
            CurrentRawRatio = ratio;

            // log(1 + ratio) compresses dynamic range; scale matches the
            // RIPA2 [0, 1.5] clip range so the existing HUD gauge thresholds
            // (green/yellow/red bands) work without reconfiguration.
            double smoothed = Math.Log(1.0 + ratio) * SmoothedScale;
            if (smoothed > SmoothedClipMax) smoothed = SmoothedClipMax;
            CurrentSmoothed = smoothed;
        }
    }
}
