using UnityEngine;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Types of calibration/validation tests available.
    /// </summary>
    public enum CalibrationTestType
    {
        /// <summary>Look at static targets for specified duration.</summary>
        Fixation,

        /// <summary>Track smoothly moving objects with eyes.</summary>
        SmoothPursuit,

        /// <summary>Rapid gaze shifts between targets.</summary>
        Saccade,

        /// <summary>Natural viewing behavior in environment.</summary>
        FreeExploration
    }

    /// <summary>
    /// Phases of a calibration session.
    /// </summary>
    public enum CalibrationPhase
    {
        /// <summary>Initial setup, participant ID entry.</summary>
        Setup,

        /// <summary>Showing instructions to participant.</summary>
        Instructions,

        /// <summary>Running calibration tests.</summary>
        Testing,

        /// <summary>
        /// Auto-fit a per-user EyeTrackingProfile from collected samples
        /// and prompt the user to save it. Sits between Testing and
        /// Completion so the saved profile, if any, is reflected in the
        /// completion-screen metrics.
        /// </summary>
        Tuning,

        /// <summary>
        /// Brief post-fit fixation re-test that runs after the user accepts a
        /// saved profile. Lets the Completion screen show metrics that
        /// reflect the corrected eye-tracker state, not the pre-correction
        /// state. Skipped if the user declines to save a profile.
        /// </summary>
        Verification,

        /// <summary>Session complete, showing results.</summary>
        Completion,

        /// <summary>Validating collected data quality.</summary>
        DataValidation
    }

    /// <summary>
    /// Configuration for a single calibration test scenario.
    /// </summary>
    [System.Serializable]
    public struct CalibrationScenario
    {
        /// <summary>Display name of the test.</summary>
        public string name;

        /// <summary>Brief description for logs.</summary>
        public string description;

        /// <summary>Duration of the test in seconds.</summary>
        public float duration;

        /// <summary>Type of test.</summary>
        public CalibrationTestType type;

        /// <summary>Instructions shown to participant.</summary>
        public string instructions;

        public CalibrationScenario(string name, CalibrationTestType type, float duration, string description, string instructions)
        {
            this.name = name;
            this.type = type;
            this.duration = duration;
            this.description = description;
            this.instructions = instructions;
        }
    }

    /// <summary>
    /// Which path produced a gaze sample. Lets the fit and verification
    /// reject mid-test source switches (e.g., combined-gaze becoming
    /// invalid for one frame and the runner falling through to a per-eye
    /// average) that would otherwise mix data with different noise
    /// characteristics into the same median.
    /// </summary>
    public enum GazeSource
    {
        Unknown = 0,
        Combined = 1,
        BinocularAverage = 2,
        LeftMonocular = 3,
        RightMonocular = 4
    }

    /// <summary>
    /// Ground truth data sample for accuracy validation.
    /// </summary>
    [System.Serializable]
    public struct GroundTruthSample
    {
        /// <summary>Time when sample was recorded.</summary>
        public float timestamp;

        /// <summary>Session-relative time.</summary>
        public float sessionTime;

        /// <summary>Type of test being performed.</summary>
        public string testType;

        /// <summary>Identifier of the target object.</summary>
        public string targetID;

        /// <summary>World position of target center.</summary>
        public Vector3 targetPosition;

        /// <summary>Point where gaze ray intersects target surface.</summary>
        public Vector3 surfaceIntersectionPoint;

        /// <summary>Direction participant should be looking.</summary>
        public Vector3 intendedGazeDirection;

        /// <summary>Actual gaze ray origin from eye tracker.</summary>
        public Vector3 actualGazeOrigin;

        /// <summary>Actual gaze ray direction from eye tracker.</summary>
        public Vector3 actualGazeDirection;

        /// <summary>Angular error to target center (degrees).</summary>
        public float gazeError;

        /// <summary>Angular error to surface intersection (degrees).</summary>
        public float surfaceError;

        /// <summary>Whether participant was in fixation state.</summary>
        public bool isFixating;

        /// <summary>Whether sample meets accuracy threshold.</summary>
        public bool isValid;

        /// <summary>Whether sample was valid specifically due to low angular error.</summary>
        public bool isValidByAngularError;

        /// <summary>Whether gaze ray hit a surface.</summary>
        public bool hasSurfaceIntersection;

        /// <summary>
        /// Seconds since the current target was activated. Used to filter
        /// out target-to-target transition samples when scoring fixation
        /// (the user is saccading toward the new target during the first
        /// few hundred ms; including those samples depresses the score).
        /// 0 if the runner did not call MarkTargetOnset.
        /// </summary>
        public float targetSettleSeconds;

        /// <summary>
        /// Instantaneous target velocity in world units / second. Zero for
        /// stationary fixation/saccade targets, non-zero during smooth
        /// pursuit. Used to compute pursuit gain.
        /// </summary>
        public Vector3 targetVelocity;

        /// <summary>
        /// Which gaze pipeline produced this sample (combined, per-eye
        /// average, or monocular). Used downstream to filter out source
        /// changes mid-fixation that would otherwise mix differently-noisy
        /// signals into the same aggregate.
        /// </summary>
        public GazeSource gazeSource;

        /// <summary>
        /// Inter-sample angular gaze velocity (degrees/second) computed
        /// from the previous sample on the same target. Used by the I-VT
        /// fixation gate (Salvucci & Goldberg 2000): samples with velocity
        /// above ~30°/s are saccades or microsaccade bursts, not settled
        /// fixation, and inflate the median if included. Zero on the first
        /// sample of each target window (no previous to differentiate from).
        /// </summary>
        public float gazeAngularVelocityDegPerSec;
    }

    /// <summary>
    /// Results from a validation session.
    ///
    /// The metric design splits per test type rather than reporting a single
    /// "% accuracy" number — that single number was misleading because it
    /// applied a binary "&lt; 2°" rule to fundamentally different signals
    /// (steady-state fixation, saccade flight paths, and naturally-lagging
    /// smooth pursuit). The per-test fields below report what each test is
    /// actually capable of measuring.
    /// </summary>
    [System.Serializable]
    public struct CalibrationResults
    {
        /// <summary>Number of scenarios completed.</summary>
        public int completedScenarios;

        /// <summary>Session duration in seconds.</summary>
        public float sessionDuration;

        // ===================================================================
        // Fixation metrics (steady-state accuracy on a stationary target).
        // Computed from "settled" samples only — those recorded after the
        // user has had time to saccade onto the new target. Including the
        // transition samples masks otherwise-clean tracking.
        // ===================================================================

        /// <summary>% of settled fixation samples within the angular threshold.</summary>
        public float fixationSettledAccuracyPct;
        /// <summary>Median angular error (deg) during settled fixation samples.</summary>
        public float fixationMedianErrorDeg;
        /// <summary>95th-percentile angular error (deg) during settled fixation samples.</summary>
        public float fixationP95ErrorDeg;
        /// <summary>Number of fixation samples used for the settled metrics.</summary>
        public int fixationSettledSampleCount;
        /// <summary>Total fixation samples recorded (settled + transition).</summary>
        public int fixationTotalSampleCount;

        // ===================================================================
        // Saccade metrics. The flight phase is by definition off-target;
        // what's measured is whether the eye lands on the new target after
        // each shift. We use the last fraction of each target window as the
        // "landing" period and score angular error there.
        // ===================================================================

        /// <summary>% of saccade landing samples within the angular threshold.</summary>
        public float saccadeLandingAccuracyPct;
        /// <summary>Median angular error (deg) during saccade landing samples.</summary>
        public float saccadeLandingMedianErrorDeg;
        /// <summary>95th-percentile angular error (deg) during saccade landing samples.</summary>
        public float saccadeLandingP95ErrorDeg;
        /// <summary>Number of saccade samples used for the landing metrics.</summary>
        public int saccadeLandingSampleCount;
        /// <summary>Total saccade samples recorded.</summary>
        public int saccadeTotalSampleCount;

        // ===================================================================
        // Smooth-pursuit metrics. Human pursuit naturally lags target motion
        // by ~100-200 ms, producing 1-5° instantaneous error at typical
        // target speeds. A binary &lt;2° threshold mis-reports this as failure.
        // We instead report the lag distribution and the velocity gain
        // (eye speed / target speed); a healthy pursuit has gain ≈ 0.9–1.0.
        // ===================================================================

        /// <summary>Median instantaneous angular distance (deg) from gaze to target during pursuit.</summary>
        public float pursuitMedianLagDeg;
        /// <summary>95th-percentile angular distance (deg) from gaze to target during pursuit.</summary>
        public float pursuitP95LagDeg;
        /// <summary>Eye angular speed / target angular speed; ≈1.0 is ideal pursuit.</summary>
        public float pursuitGainEstimate;
        /// <summary>Number of pursuit samples used for the metrics.</summary>
        public int pursuitSampleCount;

        // ===================================================================
        // Legacy fields — kept for backwards-compat with any callers reading
        // the old API. These mirror the *Settled* / *Landing* / median-lag
        // values where appropriate.
        // ===================================================================

        /// <summary>Legacy: same as fixationSettledAccuracyPct.</summary>
        public float fixationAccuracy;
        /// <summary>Legacy: same as saccadeLandingAccuracyPct.</summary>
        public float saccadeAccuracy;
        /// <summary>Legacy: 100 - normalized pursuitMedianLagDeg (rough conversion).</summary>
        public float pursuitAccuracy;
        /// <summary>Legacy: average of the three test-type headline numbers.</summary>
        public float accuracy;
        /// <summary>Legacy: same as accuracy (kept for binary-rendering callers).</summary>
        public float dataCompleteness;
        /// <summary>Legacy: total raw samples across all tests.</summary>
        public int totalSamples;
        /// <summary>Legacy: count of legacy "isValid" samples.</summary>
        public int validSamples;

        /// <summary>
        /// Quality rating based on the per-test metrics rather than a single
        /// aggregate %. The bar is a real-world VR eye-tracker target:
        ///   - Excellent: settled fixation median error &lt; 1° AND ≥ 90% pass
        ///   - Good:      median &lt; 1.5° AND ≥ 75% pass
        ///   - Acceptable: median &lt; 2.5° AND ≥ 60% pass
        ///   - Poor:      median &lt; 5°
        ///   - Unusable:  worse
        /// Pursuit gain &lt; 0.5 forces "Poor" at most (clear pursuit failure).
        /// </summary>
        public string GetQualityRating()
        {
            float med = fixationMedianErrorDeg;
            float pass = fixationSettledAccuracyPct;
            string rating;
            if (med < 1f && pass >= 90f) rating = "Excellent";
            else if (med < 1.5f && pass >= 75f) rating = "Good";
            else if (med < 2.5f && pass >= 60f) rating = "Acceptable";
            else if (med < 5f) rating = "Poor";
            else rating = "Unusable";

            // Pursuit gain is a separate failure mode — a person with broken
            // pursuit but clean fixation should not be rated above "Poor".
            if (pursuitSampleCount > 0 && pursuitGainEstimate < 0.5f)
            {
                if (rating == "Excellent" || rating == "Good" || rating == "Acceptable")
                    rating = "Poor";
            }

            return rating;
        }

        /// <summary>
        /// Compact human-readable summary covering all three test types.
        /// </summary>
        public string GetSummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"Calibration: {GetQualityRating()} | ");
            if (fixationSettledSampleCount > 0)
                sb.Append($"Fixation: {fixationSettledAccuracyPct:F1}% within ({fixationMedianErrorDeg:F1}° med, {fixationP95ErrorDeg:F1}° p95) | ");
            if (saccadeLandingSampleCount > 0)
                sb.Append($"Saccade landing: {saccadeLandingAccuracyPct:F1}% ({saccadeLandingMedianErrorDeg:F1}° med) | ");
            if (pursuitSampleCount > 0)
                sb.Append($"Pursuit lag: {pursuitMedianLagDeg:F1}° med, gain {pursuitGainEstimate:F2}");
            return sb.ToString().TrimEnd(' ', '|');
        }
    }

    /// <summary>
    /// Configuration settings for calibration tests.
    /// </summary>
    [System.Serializable]
    public class CalibrationSettings
    {
        [Header("Fixation Test")]
        [Tooltip("Number of fixation targets to show")]
        public int fixationTargetCount = 7;

        [Tooltip("Time to fixate on each target (seconds)")]
        public float fixationDwellTime = 2f;

        [Tooltip("Size of fixation target spheres (meters)")]
        public float fixationTargetSize = 0.2f;

        [Header("Saccade Test")]
        [Tooltip("Number of saccade targets")]
        public int saccadeTargetCount = 12;

        [Tooltip("Interval between saccade targets (seconds)")]
        public float saccadeInterval = 1f;

        [Header("Smooth Pursuit Test")]
        [Tooltip("Duration of smooth pursuit test (seconds)")]
        public float pursuitDuration = 30f;

        [Tooltip("Speed of pursuit target (m/s)")]
        public float pursuitSpeed = 0.5f;

        [Header("Free Exploration")]
        [Tooltip("Duration of free exploration (seconds)")]
        public float explorationDuration = 60f;

        [Header("Validation")]
        [Tooltip("Accuracy threshold in degrees")]
        public float accuracyThreshold = 2f;

        [Tooltip("Sampling rate for ground truth recording (Hz)")]
        public float samplingRate = 60f;

        [Tooltip("Seconds the user has after each fixation target appears before samples count toward the score. Filters out the saccade-to-new-target transition window.")]
        public float fixationSettleSeconds = 0.5f;

        [Tooltip("Fraction of each saccade target's display window used as the 'landing' period. The first (1 - fraction) of the window is the saccade flight; only the trailing fraction is scored.")]
        [Range(0.1f, 0.9f)]
        public float saccadeLandingFraction = 0.4f;

        [Header("Post-Fit Verification")]
        [Tooltip("Number of fixation targets shown in the Verification phase that runs after the user saves a profile. Smaller than fixationTargetCount so the re-test stays brief, but large enough that the median is statistically stable.")]
        public int verificationTargetCount = 6;

        [Tooltip("Dwell time per target during the Verification phase (seconds). Approaches fixationDwellTime so per-target settled-sample count is comparable; without this the pre-fit vs post-fit medians are computed at very different sample sizes.")]
        public float verificationDwellTime = 1.5f;

        [Header("Target Distances")]
        [Tooltip("Base distance for targets from camera (meters)")]
        public float baseTargetDistance = 3f;

        [Tooltip("Maximum horizontal spread for targets (meters)")]
        public float maxHorizontalSpread = 1.5f;

        [Tooltip("Maximum vertical spread for targets (meters)")]
        public float maxVerticalSpread = 0.6f;

        /// <summary>
        /// Create default calibration scenarios.
        /// </summary>
        public CalibrationScenario[] CreateDefaultScenarios()
        {
            return new CalibrationScenario[]
            {
                new CalibrationScenario(
                    "Fixation Test",
                    CalibrationTestType.Fixation,
                    fixationTargetCount * (fixationDwellTime + 1f),
                    "Look at highlighted objects for 2 seconds each",
                    $"{fixationTargetCount} targets will appear one by one in yellow.\n\n" +
                    $"Look at each target and hold your gaze for {fixationDwellTime} seconds.\n" +
                    "The target will turn green when completed.\n\n" +
                    "Keep your head still and use only your eyes to look at each target."
                ),
                new CalibrationScenario(
                    "Smooth Pursuit Test",
                    CalibrationTestType.SmoothPursuit,
                    pursuitDuration,
                    "Follow a single moving object with your eyes",
                    "A single bright orange object will move smoothly across your field of view.\n\n" +
                    "Follow it with your eyes as smoothly as possible.\n" +
                    "Try to keep your gaze centered on the object at all times.\n\n" +
                    "Keep your head still and use only your eyes to follow the movement."
                ),
                new CalibrationScenario(
                    "Saccade Test",
                    CalibrationTestType.Saccade,
                    saccadeTargetCount * (saccadeInterval + 0.5f),
                    "Look quickly between highlighted objects",
                    "Look quickly between objects as they light up.\n" +
                    "Move your eyes as fast as possible.\n\n" +
                    "Keep your head still and use only your eyes."
                ),
                new CalibrationScenario(
                    "Free Exploration",
                    CalibrationTestType.FreeExploration,
                    explorationDuration,
                    "Walk around and explore the environment naturally",
                    "Now you can walk around freely in the virtual environment!\n\n" +
                    "Move around the room using your headset's tracking.\n" +
                    "Look at objects that interest you as you navigate.\n" +
                    "This tests how eye tracking performs during movement.\n\n" +
                    "Feel free to explore the entire space naturally."
                )
            };
        }
    }
}
