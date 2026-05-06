using System.Collections.Generic;
using UnityEngine;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Fits a constant yaw/pitch offset that, when applied to the measured
    /// combined gaze direction, minimizes summed angular error to fixation
    /// targets. Pure C#, no MonoBehaviour — fully unit-testable.
    ///
    /// The fit operates in head-local space because the correction the
    /// eye-tracking pipeline applies is also head-local: an anatomical
    /// eyeball-aim bias rotates with the head, not with the world.
    ///
    /// Algorithm: per settled fixation sample, decompose the intended and
    /// measured gaze directions into (yaw, pitch) tuples in head-local
    /// space, take the median of the per-sample residuals. Median is more
    /// robust than mean given real-world calibration data is noisy and may
    /// include occasional catastrophic outliers (blinks, brief target
    /// re-fixations after a mid-window saccade) — the rationale is the
    /// classical M-estimator / robust-statistics literature (Huber 1964;
    /// Rousseeuw & Leroy 1987). For methodology context on
    /// fixation-only fitting and the settled-vs-transition sample
    /// discipline, see Holmqvist et al. 2011 (Eye Tracking: A
    /// Comprehensive Guide to Methods and Measures, OUP). Both are
    /// listed in `ACKNOWLEDGMENTS.md` at the project root.
    /// </summary>
    public static class OffsetEstimator
    {
        public class Options
        {
            /// <summary>Skip samples within this many seconds of target onset.</summary>
            public float settleSeconds = 0.5f;
            /// <summary>Reject samples whose residual exceeds this magnitude (degrees).</summary>
            public float maxResidualDeg = 30f;
        }

        public class Result
        {
            public float yawOffsetDeg;
            public float pitchOffsetDeg;
            public int samplesUsed;
            public float preFitMedianErrorDeg;
            public float postFitMedianErrorDeg;
            public bool converged;
        }

        /// <summary>
        /// Fit a combined yaw/pitch offset from settled fixation samples.
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

            // Group fixation samples by target so we can apply the per-target
            // settle filter. (sessionTime - targetSettleSeconds is the onset.)
            var residuals = new List<(float dyaw, float dpitch, float angularErr)>();
            foreach (var s in samples)
            {
                if (!s.testType.Contains("FIXATION")) continue;
                if (s.targetSettleSeconds < options.settleSeconds) continue;

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

                residuals.Add((dyaw, dpitch, s.gazeError));
            }

            result.samplesUsed = residuals.Count;
            if (residuals.Count == 0)
            {
                result.converged = false;
                return result;
            }

            result.preFitMedianErrorDeg = Median(residuals, r => r.angularErr);
            result.yawOffsetDeg = Median(residuals, r => r.dyaw);
            result.pitchOffsetDeg = Median(residuals, r => r.dpitch);

            // Post-fit error: apply the fitted offset to each measured
            // direction and recompute angular error to the intended one.
            // The live runtime correction in
            // ActiveProfile.ApplyCombinedCorrection (and its Python port
            // in eyelean_analysis.calibration.posthoc_correction) is
            // additive in the head-local (yaw, pitch) decomposition, so
            // we use the same decomposition here to keep pre-fit and
            // post-fit error metrics directly comparable to the
            // correction the runtime applies.
            var postErrors = new List<float>(samples.Count);
            foreach (var s in samples)
            {
                if (!s.testType.Contains("FIXATION")) continue;
                if (s.targetSettleSeconds < options.settleSeconds) continue;

                Vector3 intendedWorld = (s.targetPosition - s.actualGazeOrigin);
                if (intendedWorld.sqrMagnitude < 1e-6f) continue;
                intendedWorld.Normalize();

                Vector3 measuredLocal = headTransform.InverseTransformDirection(s.actualGazeDirection);
                if (measuredLocal.sqrMagnitude < 1e-6f) continue;

                float measuredYaw   = Mathf.Atan2(measuredLocal.x, measuredLocal.z) * Mathf.Rad2Deg;
                float measuredPitch = -Mathf.Asin(Mathf.Clamp(measuredLocal.y, -1f, 1f)) * Mathf.Rad2Deg;
                float correctedYaw   = measuredYaw   + result.yawOffsetDeg;
                float correctedPitch = measuredPitch + result.pitchOffsetDeg;

                // Recompose to a unit local vector using the same
                // convention as the runtime (yaw=atan2(x,z), pitch=−asin(y)),
                // then take to world space for the angular-error compare.
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

        private static float Median<T>(List<T> items, System.Func<T, float> selector)
        {
            var values = new List<float>(items.Count);
            foreach (var item in items) values.Add(selector(item));
            values.Sort();
            return values[values.Count / 2];
        }
    }
}
