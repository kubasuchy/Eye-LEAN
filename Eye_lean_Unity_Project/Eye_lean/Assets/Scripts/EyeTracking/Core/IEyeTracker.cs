using UnityEngine;

namespace EyeTracking.Core
{
    /// <summary>
    /// Common interface for all eye tracking device providers.
    /// Implementations: OpenXREyeTracker (VIVE), VarjoEyeTracker, HoloLensEyeTracker, etc.
    /// </summary>
    public interface IEyeTracker
    {
        /// <summary>
        /// Whether eye tracking hardware is available and initialized.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Human-readable device name (e.g., "VIVE Focus Vision", "Varjo XR-3").
        /// </summary>
        string DeviceName { get; }

        /// <summary>
        /// Native sampling rate of the eye tracker in Hz.
        /// </summary>
        float SamplingRateHz { get; }

        // ============= COMBINED GAZE =============

        /// <summary>
        /// Get the combined (cyclops) gaze origin in tracking space.
        /// </summary>
        bool GetCombinedGazeOrigin(out Vector3 origin);

        /// <summary>
        /// Get the combined (cyclops) gaze direction in tracking space.
        /// </summary>
        bool GetCombinedGazeDirection(out Vector3 direction);

        // ============= LEFT EYE =============

        /// <summary>
        /// Get the left eye gaze origin in tracking space.
        /// </summary>
        bool GetLeftEyeOrigin(out Vector3 origin);

        /// <summary>
        /// Get the left eye gaze direction in tracking space.
        /// </summary>
        bool GetLeftEyeDirection(out Vector3 direction);

        /// <summary>
        /// Get the left eye openness (0 = closed, 1 = fully open).
        /// </summary>
        bool GetLeftEyeOpenness(out float openness);

        /// <summary>
        /// Get the left eye pupil diameter in millimeters.
        /// </summary>
        bool GetLeftPupilDiameter(out float diameterMm);

        /// <summary>
        /// Get the left eye pupil position in normalized coordinates (0-1).
        /// </summary>
        bool GetLeftPupilPosition(out Vector2 position);

        // ============= RIGHT EYE =============

        /// <summary>
        /// Get the right eye gaze origin in tracking space.
        /// </summary>
        bool GetRightEyeOrigin(out Vector3 origin);

        /// <summary>
        /// Get the right eye gaze direction in tracking space.
        /// </summary>
        bool GetRightEyeDirection(out Vector3 direction);

        /// <summary>
        /// Get the right eye openness (0 = closed, 1 = fully open).
        /// </summary>
        bool GetRightEyeOpenness(out float openness);

        /// <summary>
        /// Get the right eye pupil diameter in millimeters.
        /// </summary>
        bool GetRightPupilDiameter(out float diameterMm);

        /// <summary>
        /// Get the right eye pupil position in normalized coordinates (0-1).
        /// </summary>
        bool GetRightPupilPosition(out Vector2 position);
    }

    /// <summary>
    /// Feature flags indicating which eye tracking features a device supports.
    /// </summary>
    [System.Flags]
    public enum EyeTrackerFeatures
    {
        None = 0,
        CombinedGaze = 1 << 0,
        PerEyeGaze = 1 << 1,
        EyeOpenness = 1 << 2,
        PupilDiameter = 1 << 3,
        PupilPosition = 1 << 4,
        All = CombinedGaze | PerEyeGaze | EyeOpenness | PupilDiameter | PupilPosition
    }

    /// <summary>
    /// Extended interface for devices that support feature queries.
    /// </summary>
    public interface IEyeTrackerExtended : IEyeTracker
    {
        /// <summary>
        /// Get the features supported by this eye tracker.
        /// </summary>
        EyeTrackerFeatures SupportedFeatures { get; }

        /// <summary>
        /// Check if a specific feature is supported.
        /// </summary>
        bool SupportsFeature(EyeTrackerFeatures feature);
    }
}
