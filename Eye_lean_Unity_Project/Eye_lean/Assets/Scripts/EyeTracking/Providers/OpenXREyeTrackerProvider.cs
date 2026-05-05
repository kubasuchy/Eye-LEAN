#if USE_OPENXR

using UnityEngine;
using EyeTracking.Core;
using VRNavigation.Tracking;

namespace EyeTracking.Providers
{
    /// <summary>
    /// IEyeTracker implementation that wraps the existing OpenXREyeTracker.
    /// Provides a clean interface while maintaining backward compatibility.
    /// Thread-safe singleton pattern.
    /// </summary>
    public class OpenXREyeTrackerProvider : IEyeTrackerExtended
    {
        private static OpenXREyeTrackerProvider _instance;
        private static readonly object _lock = new object();

        public static OpenXREyeTrackerProvider Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new OpenXREyeTrackerProvider();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Reference to the underlying OpenXREyeTracker singleton.
        /// Uses safe access to handle initialization timing.
        /// </summary>
        private OpenXREyeTracker Tracker
        {
            get
            {
                try
                {
                    return OpenXREyeTracker.Instance;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[OpenXREyeTrackerProvider] Failed to access OpenXREyeTracker: {e.Message}");
                    return null;
                }
            }
        }

        // ============= IEyeTracker Implementation =============

        public bool IsAvailable
        {
            get
            {
                try
                {
                    var tracker = Tracker;
                    return tracker != null && tracker.IsEyeTrackingAvailable();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[OpenXREyeTrackerProvider] Error checking availability: {e.Message}");
                    return false;
                }
            }
        }

        public string DeviceName => "VIVE Focus Vision (OpenXR)";

        /// <summary>
        /// Eye tracking sampling rate in Hz.
        /// Default is 120Hz for VIVE Focus Vision.
        /// Other devices: Quest Pro = 90Hz, Varjo XR-3 = 200Hz, HoloLens 2 = 30Hz.
        /// Set via SetSamplingRate() if using a different device.
        /// </summary>
        private static float _samplingRateHz = 120f;
        public float SamplingRateHz => _samplingRateHz;

        /// <summary>
        /// Set the sampling rate for the eye tracker.
        /// Call this if you're using a device other than VIVE Focus Vision.
        /// </summary>
        public static void SetSamplingRate(float rateHz)
        {
            _samplingRateHz = rateHz;
            Debug.Log($"[OpenXREyeTrackerProvider] Sampling rate set to {rateHz} Hz");
        }

        public EyeTrackerFeatures SupportedFeatures => EyeTrackerFeatures.All;

        public bool SupportsFeature(EyeTrackerFeatures feature)
        {
            return (SupportedFeatures & feature) == feature;
        }

        // ============= Combined Gaze =============

        public bool GetCombinedGazeOrigin(out Vector3 origin)
        {
            if (Tracker == null)
            {
                origin = Vector3.zero;
                return false;
            }
            return Tracker.GetCombinedEyeOrigin(out origin);
        }

        public bool GetCombinedGazeDirection(out Vector3 direction)
        {
            if (Tracker == null)
            {
                direction = Vector3.forward;
                return false;
            }
            return Tracker.GetCombinedEyeDirectionNormalized(out direction);
        }

        // ============= Left Eye =============

        public bool GetLeftEyeOrigin(out Vector3 origin)
        {
            if (Tracker == null)
            {
                origin = Vector3.zero;
                return false;
            }
            return Tracker.GetLeftEyeOrigin(out origin);
        }

        public bool GetLeftEyeDirection(out Vector3 direction)
        {
            if (Tracker == null)
            {
                direction = Vector3.forward;
                return false;
            }
            return Tracker.GetLeftEyeDirectionNormalized(out direction);
        }

        public bool GetLeftEyeOpenness(out float openness)
        {
            if (Tracker == null)
            {
                openness = 0f;
                return false;
            }
            return Tracker.GetLeftEyeOpenness(out openness);
        }

        public bool GetLeftPupilDiameter(out float diameterMm)
        {
            if (Tracker == null)
            {
                diameterMm = 0f;
                return false;
            }
            return Tracker.GetLeftEyePupilDiameter(out diameterMm);
        }

        public bool GetLeftPupilPosition(out Vector2 position)
        {
            if (Tracker == null)
            {
                position = Vector2.zero;
                return false;
            }
            return Tracker.GetLeftEyePupilPositionInSensorArea(out position);
        }

        // ============= Right Eye =============

        public bool GetRightEyeOrigin(out Vector3 origin)
        {
            if (Tracker == null)
            {
                origin = Vector3.zero;
                return false;
            }
            return Tracker.GetRightEyeOrigin(out origin);
        }

        public bool GetRightEyeDirection(out Vector3 direction)
        {
            if (Tracker == null)
            {
                direction = Vector3.forward;
                return false;
            }
            return Tracker.GetRightEyeDirectionNormalized(out direction);
        }

        public bool GetRightEyeOpenness(out float openness)
        {
            if (Tracker == null)
            {
                openness = 0f;
                return false;
            }
            return Tracker.GetRightEyeOpenness(out openness);
        }

        public bool GetRightPupilDiameter(out float diameterMm)
        {
            if (Tracker == null)
            {
                diameterMm = 0f;
                return false;
            }
            return Tracker.GetRightEyePupilDiameter(out diameterMm);
        }

        public bool GetRightPupilPosition(out Vector2 position)
        {
            if (Tracker == null)
            {
                position = Vector2.zero;
                return false;
            }
            return Tracker.GetRightEyePupilPositionInSensorArea(out position);
        }
    }
}

#endif // USE_OPENXR
