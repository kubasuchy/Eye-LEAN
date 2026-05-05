using UnityEngine;

namespace EyeTracking.Data
{
    /// <summary>
    /// Tracks aggregate quality metrics for eye tracking data.
    /// Attach to same GameObject as EyeTracker for automatic integration.
    /// </summary>
    public class DataQualityMetrics : MonoBehaviour
    {
        [Header("Thresholds")]
        [Tooltip("Eye openness below this is considered a blink")]
        [Range(0f, 0.5f)]
        [SerializeField] private float blinkThreshold = 0.2f;

        [Tooltip("Direction change below this suggests stuck ray")]
        [Range(0.0001f, 0.01f)]
        [SerializeField] private float stuckRayThreshold = 0.001f;

        [Tooltip("Frames without movement before stuck")]
        [Range(30, 120)]
        [SerializeField] private int stuckRayFrames = 60;

        [Header("Current Metrics (Read-Only)")]
        [SerializeField] private int totalSamples;
        [SerializeField] private int validSamples;
        [SerializeField] private int blinkSamples;
        [SerializeField] private int trackingLossSamples;
        [SerializeField] private int stuckRayEvents;
        [SerializeField] private float validPercentage;

        // Properties
        public int TotalSamples => totalSamples;
        public int ValidSamples => validSamples;
        public int BlinkSamples => blinkSamples;
        public int TrackingLossSamples => trackingLossSamples;
        public int StuckRayEvents => stuckRayEvents;
        public float ValidPercentage => validPercentage;

        // Stuck ray detection state
        private Vector3 _lastDirection;
        private int _stuckFrameCount;

        /// <summary>
        /// Reset all metrics to zero.
        /// </summary>
        public void Reset()
        {
            totalSamples = 0;
            validSamples = 0;
            blinkSamples = 0;
            trackingLossSamples = 0;
            stuckRayEvents = 0;
            validPercentage = 0f;
            _lastDirection = Vector3.zero;
            _stuckFrameCount = 0;
        }

        /// <summary>
        /// Record a sample. Call from the eye-tracker pipeline after each data collection.
        /// </summary>
        /// <param name="isValid">Whether tracking data was valid this frame</param>
        /// <param name="gazeDirection">Current gaze direction (for stuck ray detection)</param>
        /// <param name="leftOpenness">Left eye openness (0-1), -1 if unavailable</param>
        /// <param name="rightOpenness">Right eye openness (0-1), -1 if unavailable</param>
        public void RecordSample(bool isValid, Vector3 gazeDirection, float leftOpenness = -1f, float rightOpenness = -1f)
        {
            totalSamples++;

            if (isValid)
            {
                validSamples++;
            }
            else
            {
                trackingLossSamples++;
            }

            // Blink detection (both eyes closed)
            if (leftOpenness >= 0f && rightOpenness >= 0f)
            {
                if (leftOpenness < blinkThreshold && rightOpenness < blinkThreshold)
                {
                    blinkSamples++;
                }
            }

            // Stuck ray detection
            if (isValid && gazeDirection != Vector3.zero)
            {
                CheckStuckRay(gazeDirection);
            }
            else
            {
                _stuckFrameCount = 0;
            }

            // Update percentage
            validPercentage = totalSamples > 0 ? (validSamples / (float)totalSamples) * 100f : 0f;
        }

        private void CheckStuckRay(Vector3 currentDirection)
        {
            if (_lastDirection != Vector3.zero)
            {
                float movement = Vector3.Distance(currentDirection.normalized, _lastDirection.normalized);

                if (movement < stuckRayThreshold)
                {
                    _stuckFrameCount++;
                    if (_stuckFrameCount == stuckRayFrames)
                    {
                        stuckRayEvents++;
                        Debug.LogWarning($"[DataQualityMetrics] Stuck ray detected! Event #{stuckRayEvents}");
                    }
                }
                else
                {
                    _stuckFrameCount = 0;
                }
            }

            _lastDirection = currentDirection.normalized;
        }

        /// <summary>
        /// Get overall quality rating.
        /// </summary>
        public string GetQualityRating()
        {
            if (validPercentage >= 95f && stuckRayEvents == 0) return "Excellent";
            if (validPercentage >= 85f && stuckRayEvents <= 2) return "Good";
            if (validPercentage >= 70f) return "Acceptable";
            if (validPercentage >= 50f) return "Poor";
            return "Unusable";
        }

        /// <summary>
        /// Get summary string for logging.
        /// </summary>
        public string GetSummary()
        {
            return $"Quality: {GetQualityRating()} | Valid: {validPercentage:F1}% ({validSamples}/{totalSamples}) | Blinks: {blinkSamples} | Tracking Loss: {trackingLossSamples} | Stuck Events: {stuckRayEvents}";
        }
    }
}
