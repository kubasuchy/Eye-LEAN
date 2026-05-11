using System.Collections.Generic;
using UnityEngine;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Fits a yaw/pitch offset (and optionally gain) that, when applied to
    /// the measured combined gaze direction, minimizes summed angular error
    /// to fixation targets. Pure C#, no MonoBehaviour — fully unit-testable.
    ///
    /// The fit operates in head-local space because the correction the
    /// eye-tracking pipeline applies is also head-local: an anatomical
    /// eyeball-aim bias rotates with the head, not with the world.
    ///
    /// Algorithm: per settled fixation sample, decompose the intended and
    /// measured gaze directions into (yaw, pitch) tuples in head-local
    /// space. Fit per-axis with Theil-Sen — robust median-of-pairwise-slopes
    /// regression — to recover both gain and offset jointly. Theil-Sen is
    /// asymptotically as efficient as least squares on Gaussian data while
    /// remaining robust to up to ~29 % outliers (Sen 1968; Theil 1950),
    /// which matters because real eye-tracker fixation data contains
    /// catastrophic outliers (blinks, brief mid-window re-fixations) — the
    /// rationale is the classical M-estimator / robust-statistics
    /// literature (Huber 1964; Rousseeuw &amp; Leroy 1987). For methodology
    /// context on fixation-only fitting and the settled-vs-transition
    /// sample discipline, see Holmqvist et al. 2011 (Eye Tracking: A
    /// Comprehensive Guide to Methods and Measures, OUP). All listed in
    /// `ACKNOWLEDGMENTS.md` at the project root.
    ///
    /// Gain is only applied when the per-axis slope's deviation from 1
    /// exceeds <see cref="Options.minGainDeviation"/> AND the input data
    /// spans at least <see cref="Options.minEccentricityDeg"/> in that
    /// axis — otherwise the slope estimate has no statistical leverage and
    /// would amplify noise. When suppressed, gain stays at 1 and only the
    /// offset is reported.
    /// </summary>
    public static class OffsetEstimator
    {
        public class Options
        {
            /// <summary>Skip samples within this many seconds of target onset.</summary>
            public float settleSeconds = 0.5f;
            /// <summary>Reject samples whose residual exceeds this magnitude (degrees).</summary>
            public float maxResidualDeg = 30f;
            /// <summary>Reject samples whose inter-sample angular gaze velocity exceeds this (deg/s).</summary>
            public float maxGazeVelocityDegPerSec = 30f;
            /// <summary>Apply a per-axis gain in addition to offset. Leave true unless tests need to isolate offset.</summary>
            public bool fitGain = true;
            /// <summary>Suppress gain when |slope - 1| is below this — protects against noise-driven inflation.</summary>
            public float minGainDeviation = 0.05f;
            /// <summary>Suppress gain when the data does not span this many degrees in that axis.</summary>
            public float minEccentricityDeg = 10f;
            /// <summary>
            /// Suppress gain when the per-axis interquartile range (Q3-Q1) of
            /// measured-axis values is below this many degrees. Total span
            /// alone is not enough to trust the slope: with a tight central
            /// cluster and one outlier target at each extreme, the slope is
            /// dominated by ~2 leverage points and Theil-Sen has no robustness
            /// budget. Requiring the bulk (middle 50%) of samples to span at
            /// least this much guarantees the slope is constrained by
            /// distributed evidence.
            /// </summary>
            public float minInterquartileEccentricityDeg = 4f;
        }

        public class Result
        {
            public float yawOffsetDeg;
            public float pitchOffsetDeg;
            public float yawGain = 1f;
            public float pitchGain = 1f;
            public int samplesUsed;
            public float preFitMedianErrorDeg;
            public float postFitMedianErrorDeg;
            public bool converged;
        }

        /// <summary>
        /// Fit a combined yaw/pitch offset (and optionally gain) from
        /// settled fixation samples.
        /// </summary>
        /// <param name="samples">All collected calibration samples.</param>
        /// <param name="headTransform">
        /// Transform whose local frame is used for the yaw/pitch
        /// decomposition. Typically Camera.main.transform.
        /// </param>
        /// <param name="options">Tuning knobs; nulls fall back to defaults.</param>
        public static Result FitCombinedOffset(
            IReadOnlyList<GroundTruthSample> samples,
            Transform headTransform,
            Options options = null)
        {
            options ??= new Options();
            var result = new Result();

            if (samples == null || samples.Count == 0 || headTransform == null)
            {
                return result;
            }

            // Per settled fixation sample: decompose to head-local yaw/pitch
            // for both intended and measured directions. Apply settle filter,
            // residual-magnitude filter, and (when populated) the I-VT
            // velocity filter. The velocity check is gated on > 0 so that
            // legacy tests with synthesized samples (gazeAngularVelocityDegPerSec
            // defaults to 0) are not affected.
            var fitSamples = new List<(float intendedYaw, float intendedPitch,
                                       float measuredYaw, float measuredPitch,
                                       float angularErr)>();
            foreach (var s in samples)
            {
                if (!s.testType.Contains("FIXATION")) continue;
                if (s.targetSettleSeconds < options.settleSeconds) continue;
                if (s.gazeAngularVelocityDegPerSec > options.maxGazeVelocityDegPerSec) continue;

                Vector3 intendedWorld = (s.targetPosition - s.actualGazeOrigin);
                if (intendedWorld.sqrMagnitude < 1e-6f) continue;
                intendedWorld.Normalize();

                Vector3 intendedLocal = headTransform.InverseTransformDirection(intendedWorld);
                Vector3 measuredLocal = headTransform.InverseTransformDirection(s.actualGazeDirection);
                if (measuredLocal.sqrMagnitude < 1e-6f) continue;

                float intendedYaw = Mathf.Atan2(intendedLocal.x, intendedLocal.z) * Mathf.Rad2Deg;
                float intendedPitch = -Mathf.Asin(Mathf.Clamp(intendedLocal.y, -1f, 1f)) * Mathf.Rad2Deg;
                float measuredYaw = Mathf.Atan2(measuredLocal.x, measuredLocal.z) * Mathf.Rad2Deg;
                float measuredPitch = -Mathf.Asin(Mathf.Clamp(measuredLocal.y, -1f, 1f)) * Mathf.Rad2Deg;

                float dyaw = intendedYaw - measuredYaw;
                float dpitch = intendedPitch - measuredPitch;
                float magnitude = Mathf.Sqrt(dyaw * dyaw + dpitch * dpitch);
                if (magnitude > options.maxResidualDeg) continue;

                fitSamples.Add((intendedYaw, intendedPitch, measuredYaw, measuredPitch, s.gazeError));
            }

            result.samplesUsed = fitSamples.Count;
            if (fitSamples.Count == 0)
            {
                result.converged = false;
                return result;
            }

            // Pre-fit median angular error (against the actual measured gaze).
            var preErrors = new List<float>(fitSamples.Count);
            foreach (var fs in fitSamples) preErrors.Add(fs.angularErr);
            preErrors.Sort();
            result.preFitMedianErrorDeg = preErrors[preErrors.Count / 2];

            // Per-axis fit: y = gain * x + offset, where x = measured, y = intended.
            // Gain & offset by Theil-Sen, with gain suppressed when the data
            // doesn't have the eccentricity span / slope deviation to justify it.
            var yawXs = new List<float>(fitSamples.Count);
            var yawYs = new List<float>(fitSamples.Count);
            var pitchXs = new List<float>(fitSamples.Count);
            var pitchYs = new List<float>(fitSamples.Count);
            foreach (var fs in fitSamples)
            {
                yawXs.Add(fs.measuredYaw);
                yawYs.Add(fs.intendedYaw);
                pitchXs.Add(fs.measuredPitch);
                pitchYs.Add(fs.intendedPitch);
            }

            FitAxis(yawXs, yawYs, options, out result.yawGain, out result.yawOffsetDeg);
            FitAxis(pitchXs, pitchYs, options, out result.pitchGain, out result.pitchOffsetDeg);

            // Post-fit error: apply the fitted gain + offset to each measured
            // direction and recompute angular error to the intended one.
            // CRITICAL: the live runtime correction in
            // ActiveProfile.ApplyCombinedCorrection (and its Python port
            // in eyelean_analysis.calibration.posthoc_correction) computes
            // localYaw_corrected = gain * localYaw + offset
            // in the head-local (yaw, pitch) decomposition, NOT a
            // Quaternion.Euler rotation. The two agree only to first order
            // and diverge for non-trivial offsets / non-unit gains, so use
            // the same yaw/pitch composition the runtime uses to keep
            // pre-fit and post-fit error metrics directly comparable.
            var postErrors = new List<float>(samples.Count);
            foreach (var s in samples)
            {
                if (!s.testType.Contains("FIXATION")) continue;
                if (s.targetSettleSeconds < options.settleSeconds) continue;
                if (s.gazeAngularVelocityDegPerSec > options.maxGazeVelocityDegPerSec) continue;

                Vector3 intendedWorld = (s.targetPosition - s.actualGazeOrigin);
                if (intendedWorld.sqrMagnitude < 1e-6f) continue;
                intendedWorld.Normalize();

                Vector3 measuredLocal = headTransform.InverseTransformDirection(s.actualGazeDirection);
                if (measuredLocal.sqrMagnitude < 1e-6f) continue;

                float measuredYaw   = Mathf.Atan2(measuredLocal.x, measuredLocal.z) * Mathf.Rad2Deg;
                float measuredPitch = -Mathf.Asin(Mathf.Clamp(measuredLocal.y, -1f, 1f)) * Mathf.Rad2Deg;
                float correctedYaw   = result.yawGain   * measuredYaw   + result.yawOffsetDeg;
                float correctedPitch = result.pitchGain * measuredPitch + result.pitchOffsetDeg;

                float yawRad   = correctedYaw   * Mathf.Deg2Rad;
                float pitchRad = -correctedPitch * Mathf.Deg2Rad;
                float cosP = Mathf.Cos(pitchRad);
                Vector3 correctedLocal = new Vector3(
                    Mathf.Sin(yawRad) * cosP,
                    Mathf.Sin(pitchRad),
                    Mathf.Cos(yawRad) * cosP
                );
                float clNorm = correctedLocal.magnitude;
                if (clNorm > 1e-9f) correctedLocal /= clNorm;
                Vector3 correctedWorld = headTransform.TransformDirection(correctedLocal);

                float dot = Vector3.Dot(correctedWorld, intendedWorld);
                float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
                postErrors.Add(angle);
            }
            if (postErrors.Count > 0)
            {
                postErrors.Sort();
                result.postFitMedianErrorDeg = postErrors[postErrors.Count / 2];
            }

            result.converged = true;
            return result;
        }

        // ----------------------------------------------------------------

        // Per-axis fit: regress y = gain * x + offset by Theil-Sen.
        // Gain is suppressed (held at 1) when the fit lacks statistical
        // leverage — small eccentricity span or near-unit slope. In that
        // regime, the offset is fit as the median of (y - x), which is
        // exactly the constant-bias estimator the prior algorithm used.
        private static void FitAxis(
            List<float> xs, List<float> ys, Options options,
            out float gain, out float offset)
        {
            int n = xs.Count;
            if (n == 0)
            {
                gain = 1f;
                offset = 0f;
                return;
            }

            // Eccentricity span check: if the participant only fixated a
            // narrow band on this axis, slope is unidentifiable.
            float minX = xs[0], maxX = xs[0];
            for (int i = 1; i < n; i++)
            {
                if (xs[i] < minX) minX = xs[i];
                if (xs[i] > maxX) maxX = xs[i];
            }
            float span = maxX - minX;

            // Interquartile-range leverage check: total span alone is met
            // even when 95% of samples sit in a tight cluster with one or
            // two outlier targets at the extremes. In that regime
            // Theil-Sen's slope is dominated by the few cross-cluster pairs
            // and lands wildly off 1.0 (observed in-headset: pitch gain of
            // 0.68 from a 7-target run with one target at +4° and one at
            // -8° pitch, the rest clustered near -2°). Requiring the middle
            // 50% of measurements to span minInterquartileEccentricityDeg
            // gates against that failure mode.
            float iqr;
            {
                var sortedXs = new List<float>(xs);
                sortedXs.Sort();
                int q1Idx = sortedXs.Count / 4;
                int q3Idx = (3 * sortedXs.Count) / 4;
                iqr = sortedXs[q3Idx] - sortedXs[q1Idx];
            }

            bool tryGain = options.fitGain
                && span >= options.minEccentricityDeg
                && iqr >= options.minInterquartileEccentricityDeg
                && n >= 30;

            if (tryGain)
            {
                // Theil-Sen slope: median of pairwise (yi - yj)/(xi - xj).
                // O(n²) — fine for n in the hundreds.
                var slopes = new List<float>(n * (n - 1) / 2);
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        float dx = xs[i] - xs[j];
                        if (Mathf.Abs(dx) < 1e-3f) continue;
                        slopes.Add((ys[i] - ys[j]) / dx);
                    }
                }
                if (slopes.Count > 0)
                {
                    slopes.Sort();
                    float slope = slopes[slopes.Count / 2];
                    if (Mathf.Abs(slope - 1f) >= options.minGainDeviation)
                    {
                        gain = slope;
                        var residuals = new List<float>(n);
                        for (int i = 0; i < n; i++) residuals.Add(ys[i] - gain * xs[i]);
                        residuals.Sort();
                        offset = residuals[residuals.Count / 2];
                        return;
                    }
                }
            }

            // Offset-only fallback: gain = 1, offset = median(y - x).
            gain = 1f;
            var diffs = new List<float>(n);
            for (int i = 0; i < n; i++) diffs.Add(ys[i] - xs[i]);
            diffs.Sort();
            offset = diffs[diffs.Count / 2];
        }
    }
}
