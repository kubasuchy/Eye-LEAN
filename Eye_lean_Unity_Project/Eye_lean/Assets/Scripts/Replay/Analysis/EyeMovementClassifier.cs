using UnityEngine;
using System.Collections.Generic;

namespace EyeLean.Replay.Analysis
{
    /// <summary>
    /// Classifies eye movements into fixations and saccades, and computes
    /// the K-coefficient for attention-type analysis (focal vs ambient).
    /// Uses the velocity-threshold identification (I-VT) algorithm.
    /// </summary>
    public class EyeMovementClassifier : MonoBehaviour
    {
        #region Types

        public enum MovementType
        {
            Unknown,
            Fixation,
            Saccade
        }

        public enum AttentionType
        {
            Unknown,
            Focal,      // Detailed inspection (long fixations, short saccades)
            Ambient,    // Scanning behavior (short fixations, long saccades)
            Neutral     // Balanced attention
        }

        /// <summary>
        /// Result of classifying a single frame
        /// </summary>
        [System.Serializable]
        public class ClassificationResult
        {
            public MovementType movementType;
            public AttentionType attentionType;
            public float velocity;           // Degrees per second
            public float kCoefficient;       // K-coefficient value (-1.5 to +1.5)
            public bool isValid;
        }

        /// <summary>
        /// Summary statistics for a session or segment
        /// </summary>
        [System.Serializable]
        public class ClassificationSummary
        {
            public int totalFrames;
            public int fixationFrames;
            public int saccadeFrames;
            public int unknownFrames;

            public float fixationPercentage;
            public float saccadePercentage;

            public float averageVelocity;
            public float maxVelocity;

            public float averageKCoefficient;
            public AttentionType dominantAttention;

            public int fixationCount;
            public int saccadeCount;
            public float averageFixationDuration;
            public float averageSaccadeAmplitude;
        }

        #endregion

        #region Settings

        [Header("Velocity Classification")]
        [Tooltip("Velocity threshold for saccade detection (degrees/second)")]
        [Range(20f, 150f)]
        public float saccadeThreshold = AnalysisConstants.SACCADE_VELOCITY_THRESHOLD;

        [Tooltip("Minimum fixation duration in seconds")]
        [Range(0.05f, 0.5f)]
        public float minFixationDuration = AnalysisConstants.MIN_FIXATION_DURATION;

        [Tooltip("Minimum saccade duration in seconds")]
        [Range(0.01f, 0.1f)]
        public float minSaccadeDuration = AnalysisConstants.MIN_SACCADE_DURATION;

        [Header("K-Coefficient Settings")]
        [Tooltip("Number of fixation/saccade pairs to keep in the K-coefficient ring buffer.")]
        [Range(10, 100)]
        public int kCoefficientWindow = AnalysisConstants.K_COEFFICIENT_WINDOW_SIZE;

        [Tooltip("Visualisation-only neutral band: |K| < deadZone is reported as Neutral. Krejtz 2016 classifies by sign (deadZone = 0).")]
        [Range(0f, 1.0f)]
        public float kDeadZone = AnalysisConstants.K_COEFFICIENT_DEAD_ZONE;

        [Header("Smoothing")]
        [Tooltip("Enable velocity smoothing")]
        public bool enableSmoothing = true;

        [Tooltip("Velocity smoothing factor (lower = smoother)")]
        [Range(0.1f, 1.0f)]
        public float smoothingFactor = AnalysisConstants.DEFAULT_VELOCITY_SMOOTHING_FACTOR;

        #endregion

        #region Private Fields

        private float lastSmoothedVelocity = 0f;
        private Vector3 lastGazeDirection = Vector3.forward;
        private float lastTimestamp = 0f;

        // K-coefficient calculation buffers
        private Queue<float> fixationDurations = new Queue<float>();
        private Queue<float> saccadeAmplitudes = new Queue<float>();

        // Current fixation tracking
        private bool inFixation = false;
        private float fixationStartTime = 0f;
        private Vector3 fixationStartDirection = Vector3.forward;

        #endregion

        #region Public Methods

        /// <summary>
        /// Classify a single replay frame
        /// </summary>
        public ClassificationResult ClassifyFrame(ReplayFrame frame, ReplayFrame previousFrame = null)
        {
            var result = new ClassificationResult
            {
                isValid = false,
                movementType = MovementType.Unknown,
                attentionType = AttentionType.Unknown
            };

            if (frame == null)
                return result;

            // Prefer combined gaze; fall back to per-eye average if absent.
            Vector3 gazeDirection;
            if (frame.hasCombinedGaze && frame.combinedDirection.magnitude > 0.1f)
            {
                gazeDirection = frame.combinedDirection.normalized;
            }
            else if (frame.hasLeftEye && frame.hasRightEye)
            {
                gazeDirection = ((frame.leftEyeDirection + frame.rightEyeDirection) * 0.5f).normalized;
            }
            else if (frame.hasLeftEye)
            {
                gazeDirection = frame.leftEyeDirection.normalized;
            }
            else if (frame.hasRightEye)
            {
                gazeDirection = frame.rightEyeDirection.normalized;
            }
            else
            {
                return result;
            }

            // Calculate velocity (degrees per second)
            float velocity = 0f;
            float deltaTime = frame.frameDuration;

            if (previousFrame != null && deltaTime > 0.001f)
            {
                // Angular velocity in degrees per second
                float angleDelta = Vector3.Angle(lastGazeDirection, gazeDirection);
                velocity = angleDelta / deltaTime;
            }

            // Apply smoothing
            if (enableSmoothing)
            {
                velocity = Mathf.Lerp(lastSmoothedVelocity, velocity, smoothingFactor);
            }

            lastSmoothedVelocity = velocity;
            lastGazeDirection = gazeDirection;

            // Classify movement type
            result.velocity = velocity;
            result.isValid = true;

            if (velocity > saccadeThreshold)
            {
                result.movementType = MovementType.Saccade;
                HandleSaccadeDetected(gazeDirection, frame.timestamp);
            }
            else
            {
                result.movementType = MovementType.Fixation;
                HandleFixationDetected(gazeDirection, frame.timestamp);
            }

            // Calculate K-coefficient
            result.kCoefficient = CalculateKCoefficient();
            result.attentionType = ClassifyAttention(result.kCoefficient);

            // Store results in frame for later analysis
            frame.gazeVelocity = velocity;
            frame.isFixation = result.movementType == MovementType.Fixation;
            frame.isSaccade = result.movementType == MovementType.Saccade;
            frame.kCoefficient = result.kCoefficient;

            return result;
        }

        /// <summary>
        /// Process all frames in a session
        /// </summary>
        public ClassificationSummary ProcessSession(ReplaySession session)
        {
            if (session == null || session.frames == null || session.frames.Count == 0)
                return null;

            Reset();

            var summary = new ClassificationSummary();
            float totalVelocity = 0f;
            float maxVel = 0f;
            float kSum = 0f;
            int kCount = 0;

            ReplayFrame previousFrame = null;

            foreach (var frame in session.frames)
            {
                var result = ClassifyFrame(frame, previousFrame);

                if (result.isValid)
                {
                    summary.totalFrames++;
                    totalVelocity += result.velocity;
                    maxVel = Mathf.Max(maxVel, result.velocity);

                    switch (result.movementType)
                    {
                        case MovementType.Fixation:
                            summary.fixationFrames++;
                            break;
                        case MovementType.Saccade:
                            summary.saccadeFrames++;
                            break;
                        default:
                            summary.unknownFrames++;
                            break;
                    }

                    if (Mathf.Abs(result.kCoefficient) < 10f) // Valid K-coefficient
                    {
                        kSum += result.kCoefficient;
                        kCount++;
                    }
                }

                previousFrame = frame;
            }

            // Calculate summary statistics
            if (summary.totalFrames > 0)
            {
                summary.fixationPercentage = (float)summary.fixationFrames / summary.totalFrames * 100f;
                summary.saccadePercentage = (float)summary.saccadeFrames / summary.totalFrames * 100f;
                summary.averageVelocity = totalVelocity / summary.totalFrames;
                summary.maxVelocity = maxVel;

                if (kCount > 0)
                {
                    summary.averageKCoefficient = kSum / kCount;
                    summary.dominantAttention = ClassifyAttention(summary.averageKCoefficient);
                }

                // Count fixation/saccade events (transitions)
                summary.fixationCount = fixationDurations.Count;
                summary.saccadeCount = saccadeAmplitudes.Count;

                if (fixationDurations.Count > 0)
                {
                    float durSum = 0f;
                    foreach (float d in fixationDurations) durSum += d;
                    summary.averageFixationDuration = durSum / fixationDurations.Count;
                }

                if (saccadeAmplitudes.Count > 0)
                {
                    float ampSum = 0f;
                    foreach (float a in saccadeAmplitudes) ampSum += a;
                    summary.averageSaccadeAmplitude = ampSum / saccadeAmplitudes.Count;
                }
            }

            return summary;
        }

        /// <summary>
        /// Reset classifier state
        /// </summary>
        public void Reset()
        {
            lastSmoothedVelocity = 0f;
            lastGazeDirection = Vector3.forward;
            lastTimestamp = 0f;

            fixationDurations.Clear();
            saccadeAmplitudes.Clear();

            inFixation = false;
            fixationStartTime = 0f;
        }

        /// <summary>
        /// Get current K-coefficient value
        /// </summary>
        public float GetCurrentKCoefficient()
        {
            return CalculateKCoefficient();
        }

        /// <summary>
        /// Get attention type from K-coefficient.
        ///
        /// Per Krejtz 2016, classification is sign-based: K > 0 ⇒ focal,
        /// K < 0 ⇒ ambient. The optional <see cref="kDeadZone"/> band
        /// reports |K| &lt; deadZone as Neutral, purely for visualisation.
        /// </summary>
        public AttentionType ClassifyAttention(float kCoefficient)
        {
            if (float.IsNaN(kCoefficient)) return AttentionType.Unknown;
            if (kCoefficient >  kDeadZone) return AttentionType.Focal;
            if (kCoefficient < -kDeadZone) return AttentionType.Ambient;
            return AttentionType.Neutral;
        }

        /// <summary>
        /// Get color for attention type (for visualization)
        /// </summary>
        public Color GetAttentionColor(AttentionType attention)
        {
            switch (attention)
            {
                case AttentionType.Focal:
                    return Color.green;
                case AttentionType.Ambient:
                    return new Color(1f, 0.7f, 0f); // Orange/gold
                case AttentionType.Neutral:
                    return Color.cyan;
                default:
                    return Color.gray;
            }
        }

        /// <summary>
        /// Get color for K-coefficient value (gradient)
        /// </summary>
        public Color GetKCoefficientColor(float kCoefficient)
        {
            // Map K-coefficient to color gradient
            // Focal (positive) = Green
            // Neutral (zero) = Cyan
            // Ambient (negative) = Orange/Gold

            float t = Mathf.Clamp(kCoefficient, -1.5f, 1.5f);

            if (t >= 0)
            {
                // Neutral to Focal: Cyan to Green
                float blend = t / 1.5f;
                return Color.Lerp(Color.cyan, Color.green, blend);
            }
            else
            {
                // Neutral to Ambient: Cyan to Orange
                float blend = -t / 1.5f;
                return Color.Lerp(Color.cyan, new Color(1f, 0.7f, 0f), blend);
            }
        }

        #endregion

        #region Private Methods

        private void HandleFixationDetected(Vector3 gazeDirection, float timestamp)
        {
            if (!inFixation)
            {
                // Start of new fixation
                inFixation = true;
                fixationStartTime = timestamp;
                fixationStartDirection = gazeDirection;
            }
        }

        private void HandleSaccadeDetected(Vector3 gazeDirection, float timestamp)
        {
            if (inFixation)
            {
                // End of fixation, start of saccade.
                float fixationDuration = timestamp - fixationStartTime;

                // Record the (fixation_i, saccade_i) PAIR atomically: skip
                // fixations shorter than minFixationDuration AND the saccade
                // that follows them, so the two ring buffers remain index-
                // aligned for the canonical K-coefficient (z(a_i) − z(d_i)
                // requires paired data; advancing one queue without the
                // other quietly desynchronises the metric).
                if (fixationDuration >= minFixationDuration)
                {
                    float saccadeAmplitude = Vector3.Angle(fixationStartDirection, gazeDirection);

                    fixationDurations.Enqueue(fixationDuration);
                    saccadeAmplitudes.Enqueue(saccadeAmplitude);
                    while (fixationDurations.Count > kCoefficientWindow)  fixationDurations.Dequeue();
                    while (saccadeAmplitudes.Count  > kCoefficientWindow) saccadeAmplitudes.Dequeue();
                }

                inFixation = false;
            }
        }

        /// <summary>
        /// Calculate the K-coefficient per Krejtz et al. (2016, PLoS ONE Eq. 1):
        ///
        ///   K_i = (a_i − μ_a) / σ_a − (d_i − μ_d) / σ_d
        ///   K   = mean_i(K_i)
        ///
        /// where d_i is fixation i's duration, a_i is the amplitude of the
        /// saccade following fixation i, and the i-th entries of the two
        /// ring buffers are kept index-aligned by HandleSaccadeDetected.
        /// Magnitude is in standard-deviation units; classification is
        /// sign-based (see ClassifyAttention).
        /// </summary>
        private float CalculateKCoefficient()
        {
            int n = Mathf.Min(fixationDurations.Count, saccadeAmplitudes.Count);
            // Need >= 2 paired samples for std with Bessel's correction.
            if (n < 2) return 0f;

            // Snapshot the two queues (head-to-tail order) for indexed access.
            // Both queues are bounded to kCoefficientWindow so allocation is small.
            var d = new float[n];
            var a = new float[n];
            int i = 0;
            foreach (float v in fixationDurations) { if (i < n) d[i++] = v; }
            i = 0;
            foreach (float v in saccadeAmplitudes) { if (i < n) a[i++] = v; }

            // Means.
            float meanD = 0f, meanA = 0f;
            for (int k = 0; k < n; k++) { meanD += d[k]; meanA += a[k]; }
            meanD /= n; meanA /= n;

            // Sample standard deviations (ddof = 1, matching Krejtz / numpy default).
            float sumSqD = 0f, sumSqA = 0f;
            for (int k = 0; k < n; k++)
            {
                float dd = d[k] - meanD;
                float da = a[k] - meanA;
                sumSqD += dd * dd;
                sumSqA += da * da;
            }
            float stdD = Mathf.Sqrt(sumSqD / (n - 1));
            float stdA = Mathf.Sqrt(sumSqA / (n - 1));
            // Degenerate spread (constant duration or constant amplitude in
            // the window) leaves z-score undefined. Return 0 (not Inf/NaN);
            // ClassifyAttention will surface this as Neutral.
            if (stdD < 1e-6f || stdA < 1e-6f) return 0f;

            float kSum = 0f;
            for (int k = 0; k < n; k++)
            {
                float zD = (d[k] - meanD) / stdD;
                float zA = (a[k] - meanA) / stdA;
                kSum += zA - zD;
            }
            return kSum / n;
        }

        #endregion
    }
}
