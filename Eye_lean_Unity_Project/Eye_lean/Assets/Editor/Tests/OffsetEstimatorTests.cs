using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using EyeTracking.Calibration;
using EyeTracking.Configuration;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage for the median-residual offset fitter. Builds
    /// synthetic GroundTruthSample lists with known biases and verifies
    /// the fitter recovers them within float epsilon.
    ///
    /// The fit is purely combined-yaw/pitch in head-local space; per-eye
    /// is identity in v1.0. Tests use an identity-rotation head so
    /// `headTransform.InverseTransformDirection` and `TransformDirection`
    /// are pass-throughs and the math collapses to plain Euler-angle
    /// arithmetic.
    /// </summary>
    public class OffsetEstimatorTests
    {
        private GameObject _headGo;
        private Transform _head;

        [SetUp]
        public void SetUp()
        {
            _headGo = new GameObject("FakeHead");
            _head = _headGo.transform;
            _head.position = Vector3.zero;
            _head.rotation = Quaternion.identity;
        }

        [TearDown]
        public void TearDown()
        {
            if (_headGo != null) Object.DestroyImmediate(_headGo);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Build a fixation sample where the participant is reportedly
        /// looking in `measuredDirection` while the target sits along
        /// `intendedDirection` from the eye origin. `settle` defaults
        /// above the 0.5s filter so the sample contributes to the fit.
        /// </summary>
        private static GroundTruthSample MakeFixationSample(
            Vector3 measuredDirection,
            Vector3 intendedDirection,
            float distance = 4f,
            float settle = 1.0f)
        {
            Vector3 origin = Vector3.zero;
            Vector3 targetPos = origin + intendedDirection.normalized * distance;
            return new GroundTruthSample
            {
                testType = "FIXATION",
                targetSettleSeconds = settle,
                targetPosition = targetPos,
                actualGazeOrigin = origin,
                actualGazeDirection = measuredDirection.normalized,
                gazeError = Vector3.Angle(measuredDirection, intendedDirection),
                isValid = true,
                isFixating = true,
            };
        }

        /// <summary>
        /// Build measured/intended directions from a yaw/pitch pair (Unity
        /// convention: yaw around Y, pitch around X with positive=down).
        /// </summary>
        private static Vector3 DirFromYawPitch(float yawDeg, float pitchDeg)
        {
            // Recompose using Unity's convention from ActiveProfile.ApplyCombinedCorrection:
            //   x = sin(yaw) * cos(pitch_signed),  pitch_signed = -pitch
            //   y = sin(pitch_signed)
            //   z = cos(yaw) * cos(pitch_signed)
            float yawRad = yawDeg * Mathf.Deg2Rad;
            float pitchRad = -pitchDeg * Mathf.Deg2Rad;
            float cosP = Mathf.Cos(pitchRad);
            return new Vector3(
                Mathf.Sin(yawRad) * cosP,
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * cosP).normalized;
        }

        // ----------------------------------------------------------------
        // Empty / degenerate inputs
        // ----------------------------------------------------------------

        [Test]
        public void FitCombinedOffset_returns_default_for_empty_samples()
        {
            var result = OffsetEstimator.FitCombinedOffset(new List<GroundTruthSample>(), _head);
            Assert.AreEqual(0, result.samplesUsed);
            Assert.IsFalse(result.converged);
        }

        [Test]
        public void FitCombinedOffset_returns_default_for_null_samples()
        {
            var result = OffsetEstimator.FitCombinedOffset(null, _head);
            Assert.AreEqual(0, result.samplesUsed);
            Assert.IsFalse(result.converged);
        }

        [Test]
        public void FitCombinedOffset_returns_default_for_null_head()
        {
            var samples = new List<GroundTruthSample> { MakeFixationSample(Vector3.forward, Vector3.forward) };
            var result = OffsetEstimator.FitCombinedOffset(samples, null);
            Assert.AreEqual(0, result.samplesUsed);
            Assert.IsFalse(result.converged);
        }

        // ----------------------------------------------------------------
        // Correctness
        // ----------------------------------------------------------------

        [Test]
        public void FitCombinedOffset_recovers_constant_yaw_bias()
        {
            // Participant is "looking 5° to the left of intended" on every sample.
            const float yawBiasDeg = -5f;
            var samples = new List<GroundTruthSample>();
            // Vary intended yaw across the calibration grid so the fit isn't
            // trivially confounded with a single direction.
            float[] intendedYaws = { -10f, -5f, 0f, 5f, 10f };
            foreach (var iy in intendedYaws)
            {
                Vector3 intended = DirFromYawPitch(iy, 0f);
                Vector3 measured = DirFromYawPitch(iy + yawBiasDeg, 0f);
                samples.Add(MakeFixationSample(measured, intended));
            }

            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.IsTrue(result.converged);
            Assert.AreEqual(intendedYaws.Length, result.samplesUsed);
            // Fit should recover -yawBias (the offset that, when added to the
            // measured direction, brings it back to the intended).
            Assert.AreEqual(-yawBiasDeg, result.yawOffsetDeg, 1e-3f);
            Assert.AreEqual(0f, result.pitchOffsetDeg, 1e-3f);
            // Post-fit error should be near zero (no noise in synthetic data).
            Assert.Less(result.postFitMedianErrorDeg, 0.1f);
        }

        [Test]
        public void FitCombinedOffset_recovers_constant_pitch_bias()
        {
            const float pitchBiasDeg = 3f;
            var samples = new List<GroundTruthSample>();
            float[] intendedPitches = { -8f, -4f, 0f, 4f, 8f };
            foreach (var ip in intendedPitches)
            {
                Vector3 intended = DirFromYawPitch(0f, ip);
                Vector3 measured = DirFromYawPitch(0f, ip + pitchBiasDeg);
                samples.Add(MakeFixationSample(measured, intended));
            }

            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.IsTrue(result.converged);
            Assert.AreEqual(0f, result.yawOffsetDeg, 1e-3f);
            Assert.AreEqual(-pitchBiasDeg, result.pitchOffsetDeg, 1e-3f);
            Assert.Less(result.postFitMedianErrorDeg, 0.1f);
        }

        [Test]
        public void FitCombinedOffset_recovers_combined_yaw_pitch_bias()
        {
            const float yawBias = -2f;
            const float pitchBias = 1.5f;
            var samples = new List<GroundTruthSample>();
            for (int i = -2; i <= 2; i++)
            {
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 intended = DirFromYawPitch(i * 5f, j * 5f);
                    Vector3 measured = DirFromYawPitch(i * 5f + yawBias, j * 5f + pitchBias);
                    samples.Add(MakeFixationSample(measured, intended));
                }
            }
            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.IsTrue(result.converged);
            Assert.AreEqual(-yawBias, result.yawOffsetDeg, 0.05f);
            Assert.AreEqual(-pitchBias, result.pitchOffsetDeg, 0.05f);
        }

        // ----------------------------------------------------------------
        // Robustness: settle filter, outlier rejection, non-fixation rows
        // ----------------------------------------------------------------

        [Test]
        public void FitCombinedOffset_filters_unsettled_samples()
        {
            const float yawBias = -5f;
            // 5 settled samples consistent with the bias + 5 unsettled
            // garbage samples that would skew the fit if included.
            var samples = new List<GroundTruthSample>();
            float[] intendedYaws = { -10f, -5f, 0f, 5f, 10f };
            foreach (var iy in intendedYaws)
            {
                Vector3 intended = DirFromYawPitch(iy, 0f);
                Vector3 measured = DirFromYawPitch(iy + yawBias, 0f);
                samples.Add(MakeFixationSample(measured, intended, settle: 1.0f));
            }
            // Garbage during the saccade transition: measured pointing 30° off-target.
            foreach (var iy in intendedYaws)
            {
                Vector3 intended = DirFromYawPitch(iy, 0f);
                Vector3 measured = DirFromYawPitch(iy + 30f, 0f);
                samples.Add(MakeFixationSample(measured, intended, settle: 0.1f));
            }

            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.IsTrue(result.converged);
            // Only the 5 settled samples should contribute.
            Assert.AreEqual(5, result.samplesUsed);
            Assert.AreEqual(-yawBias, result.yawOffsetDeg, 1e-3f);
        }

        [Test]
        public void FitCombinedOffset_ignores_non_fixation_samples()
        {
            // Saccade and pursuit samples should be silently dropped — only
            // FIXATION rows feed the fit.
            const float yawBias = -2f;
            var samples = new List<GroundTruthSample>();
            for (int i = 0; i < 5; i++)
            {
                Vector3 intended = DirFromYawPitch(i * 4f, 0f);
                Vector3 measured = DirFromYawPitch(i * 4f + yawBias, 0f);
                var s = MakeFixationSample(measured, intended);
                samples.Add(s);
            }
            // Non-fixation noise.
            for (int i = 0; i < 10; i++)
            {
                var s = MakeFixationSample(
                    DirFromYawPitch(i * 7f - 30f, 0f),
                    DirFromYawPitch(0f, 0f));
                s.testType = "SACCADE_TARGET_LANDING";
                samples.Add(s);
            }

            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.AreEqual(5, result.samplesUsed);
            Assert.AreEqual(-yawBias, result.yawOffsetDeg, 1e-3f);
        }

        [Test]
        public void FitCombinedOffset_rejects_outliers_above_max_residual()
        {
            // 5 clean samples + 1 huge outlier above the 30° default cap.
            var samples = new List<GroundTruthSample>();
            const float yawBias = -1f;
            for (int i = 0; i < 5; i++)
            {
                Vector3 intended = DirFromYawPitch(i * 4f, 0f);
                Vector3 measured = DirFromYawPitch(i * 4f + yawBias, 0f);
                samples.Add(MakeFixationSample(measured, intended));
            }
            // Outlier: gaze pointing 60° off target.
            samples.Add(MakeFixationSample(
                DirFromYawPitch(60f, 0f),
                DirFromYawPitch(0f, 0f)));

            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.AreEqual(5, result.samplesUsed);
            Assert.AreEqual(-yawBias, result.yawOffsetDeg, 1e-3f);
        }

        [Test]
        public void FitCombinedOffset_post_fit_error_drops_below_pre_fit()
        {
            // Sanity check that the post-fit error is meaningfully smaller
            // than the pre-fit error when there's a real bias to remove.
            const float yawBias = -8f;
            const float pitchBias = 4f;
            var samples = new List<GroundTruthSample>();
            for (int i = -2; i <= 2; i++)
            {
                Vector3 intended = DirFromYawPitch(i * 5f, i * 3f);
                Vector3 measured = DirFromYawPitch(i * 5f + yawBias, i * 3f + pitchBias);
                samples.Add(MakeFixationSample(measured, intended));
            }
            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.IsTrue(result.converged);
            Assert.Greater(result.preFitMedianErrorDeg, 5f, "synthetic bias should give large pre-fit error");
            Assert.Less(result.postFitMedianErrorDeg, 0.5f, "fitted offset should bring residual near zero");
            Assert.Less(result.postFitMedianErrorDeg, result.preFitMedianErrorDeg);
        }

        [Test]
        public void PostFitError_UsesAdditiveYawPitch_MatchesRuntimeCorrection()
        {
            // Regression guard for the v1.0.0 Quaternion.Euler-based post-fit
            // verification: the live runtime correction
            // (ActiveProfile.ApplyCombinedCorrection / Python's
            // posthoc_correction.apply_combined_correction) is ADDITIVE in
            // the head-local (yaw, pitch) decomposition. Quaternion.Euler-
            // based rotation only agrees to first order, so for non-tiny
            // offsets the post-fit error reported by the fitter would
            // diverge from what the runtime actually applies. Assert that
            // a constructed perfect-fit case gives near-zero post-fit
            // error (which it WOULDN'T under Quaternion.Euler for this
            // offset magnitude).
            const float yawBias = -7f;     // large enough that Quaternion.Euler
            const float pitchBias = 5f;    // and additive yaw/pitch differ noticeably
            var samples = new List<GroundTruthSample>();
            // Construct using the SAME yaw/pitch convention the runtime
            // uses, so the fitted offset should perfectly cancel the bias.
            float[] iyaws   = { -8f, -4f, 0f, 4f, 8f };
            float[] ipitches = { -6f, -3f, 0f, 3f, 6f };
            foreach (var iy in iyaws)
            foreach (var ip in ipitches)
            {
                Vector3 intended = DirFromYawPitch(iy, ip);
                Vector3 measured = DirFromYawPitch(iy + yawBias, ip + pitchBias);
                samples.Add(MakeFixationSample(measured, intended));
            }

            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.IsTrue(result.converged);
            Assert.AreEqual(-yawBias,   result.yawOffsetDeg,   1e-3f);
            Assert.AreEqual(-pitchBias, result.pitchOffsetDeg, 1e-3f);

            // The post-fit error should be at most floating-point noise.
            // Under v1.0.0's Quaternion.Euler post-fit math, the residual
            // for a (-7°, 5°) offset across this grid would have been
            // visibly non-zero (~0.4° median); the additive math makes it
            // exact.
            Assert.Less(result.postFitMedianErrorDeg, 1e-3f,
                "Additive yaw/pitch post-fit math should give exact recovery on synthetic data; " +
                "non-trivial post-fit error here would suggest the v1.0.0 Quaternion.Euler path was reintroduced.");
        }

        // ----------------------------------------------------------------
        // Gain-fit gate
        // ----------------------------------------------------------------

        [Test]
        public void FitCombinedOffset_suppresses_gain_when_eccentricity_clustered()
        {
            // Reproduces the in-headset failure that motivated the IQR gate.
            // 7-target geometry: 5 targets clustered near pitch -2°, one
            // at pitch -8°, one at pitch +4°. Total pitch span ~12° (passes
            // the span gate by a hair), but the IQR is ~0° because the
            // bulk of measurements sits in the central cluster.
            //
            // Inject a true unit gain on pitch (no gain error to recover).
            // With insufficient leverage Theil-Sen lands at a noise-driven
            // off-1.0 slope; the IQR gate must catch this and fall back
            // to offset-only with gain held at 1.0.
            const int samplesPerTarget = 70;
            // (intendedPitchDeg) per target — same distribution as the
            // standard 7-target fixation set when sampled at a 1.65 m eye
            // height against the world-fixed predefined positions.
            float[] targetPitches = { -2f, -2f, -2f, -2f, -2f, -8f, +4f };
            var samples = new List<GroundTruthSample>();
            foreach (float p in targetPitches)
            {
                for (int k = 0; k < samplesPerTarget; k++)
                {
                    // Small per-sample jitter so Theil-Sen has the same
                    // many-pairs structure as a real run (settled fixations
                    // are not bit-identical).
                    float jitter = (k * 0.0173f) % 0.5f - 0.25f;   // ~ ±0.25°
                    Vector3 intended = DirFromYawPitch(0f, p);
                    Vector3 measured = DirFromYawPitch(0f, p + jitter);
                    samples.Add(MakeFixationSample(measured, intended));
                }
            }

            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.IsTrue(result.converged);
            // Gate must reject gain fitting on this distribution: a noise-
            // driven gain (e.g. 0.68 observed in headset) applied to off-
            // axis verification targets produces several degrees of fresh
            // error, which is exactly the post-fit regression the gate
            // exists to prevent.
            Assert.AreEqual(1f, result.pitchGain, 1e-6f,
                "Pitch gain must be suppressed when only 2 leverage targets are present; " +
                "IQR of intended-pitch values is < minInterquartileEccentricityDeg.");
            // Yaw axis is single-valued (0°), so yaw gain must also stay 1.
            Assert.AreEqual(1f, result.yawGain, 1e-6f);
        }

        [Test]
        public void FitCombinedOffset_recovers_gain_when_eccentricity_well_distributed()
        {
            // Counterpart to the suppression test: when eccentricity coverage
            // IS well-distributed (the IQR gate is satisfied), a real gain
            // error must still be recoverable. Inject pitch gain = 1.25
            // across an 11-pitch evenly-sampled grid and confirm the fit
            // returns gain ≈ 1.25 rather than falling through to offset-only.
            const float trueGain = 1.25f;
            var samples = new List<GroundTruthSample>();
            // Pitches -10..+10 in 11 steps × ~10 samples each → 110 samples,
            // central 50% (IQR) spans -5..+5 = 10° > 4° threshold.
            for (int p = -10; p <= 10; p += 2)
            {
                for (int k = 0; k < 10; k++)
                {
                    float jitter = (k * 0.137f) % 0.3f - 0.15f;
                    Vector3 intended = DirFromYawPitch(0f, p);
                    // Pretend the eye tracker reports pitch/gain → applying
                    // gain on intended yields measured.
                    float measuredPitch = p / trueGain + jitter;
                    Vector3 measured = DirFromYawPitch(0f, measuredPitch);
                    samples.Add(MakeFixationSample(measured, intended));
                }
            }

            var result = OffsetEstimator.FitCombinedOffset(samples, _head);
            Assert.IsTrue(result.converged);
            Assert.AreEqual(trueGain, result.pitchGain, 0.05f,
                "Pitch gain should be recovered when the IQR gate is satisfied.");
        }
    }
}
