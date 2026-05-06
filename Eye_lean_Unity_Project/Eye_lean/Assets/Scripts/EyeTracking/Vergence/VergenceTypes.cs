using UnityEngine;

namespace EyeTracking.Vergence
{
    /// <summary>
    /// Preset configurations for different vergence calculation scenarios
    /// </summary>
    public enum VergencePreset
    {
        Precise,    // High accuracy, low smoothing, strict validation
        Balanced,   // Good balance of accuracy and stability
        Stable,     // Maximum stability, higher smoothing
        Custom      // User-defined settings
    }

    /// <summary>
    /// Methods for calculating vergence intersection
    /// </summary>
    public enum VergenceCalculationMethod
    {
        Simple,             // Ray-ray closest-point algorithm (Eberly form)
        PaperAlgorithm      // Vector-vector intersection per Duchowski et al. 2022. Note: paper indexes t_2 with the left ray (R_2) and t_1 with the right ray (R_4) — see EyeTracker.CalculateUsingPaperAlgorithm.
    }

    /// <summary>
    /// Constraint handling when vergence point is out of bounds
    /// </summary>
    public enum VergenceFallbackMethod
    {
        SurfaceRaycast,     // Use raycast intersection on surfaces
        CenterPointFixed,   // Use fixed point at room center
        LastValidPoint,     // Use last valid vergence point
        Disable             // Hide vergence point when invalid
    }

    /// <summary>
    /// Primary mode for determining vergence depth.
    /// TrueConvergence uses eye convergence math but is limited by hardware (~1.5-2.3m on VIVE Focus Vision).
    /// DepthExtension uses math vergence for near objects, then extends via raycast for far objects.
    /// </summary>
    public enum VergenceDepthMode
    {
        TrueConvergence,    // Use mathematical eye convergence (limited by hardware ~1.5-2.3m)
        DepthExtension      // Math vergence for near + raycast extension for far (requires colliders)
    }

    /// <summary>
    /// Result of vergence constraint validation
    /// </summary>
    public enum VergenceConstraintResult
    {
        Valid,              // Point is within room bounds
        OutOfBounds,        // Point is outside room - use fallback
        Unreliable,         // Calculation quality too low - use fallback
        FallbackUsed        // Currently using fallback method
    }

    /// <summary>
    /// Smoothing algorithm selection for vergence point filtering
    /// </summary>
    public enum VergenceSmoothingMethod
    {
        WeightedEMA,        // Weighted Exponential Moving Average (default, responsive)
        Butterworth,        // IIR low-pass filter (smooth, minimal lag)
        SavitzkyGolay       // Polynomial smoothing (preserves peaks, slight lag)
    }

    /// <summary>
    /// Distance range for valid vergence calculations
    /// </summary>
    [System.Serializable]
    public struct VergenceDistanceRange
    {
        public float minDistance;
        public float maxDistance;
        public float wallMargin;    // Distance from walls to start quality degradation

        public static VergenceDistanceRange Default => new VergenceDistanceRange
        {
            minDistance = 0.3f,
            maxDistance = 100f,  // Extended for far walls and outdoor use
            wallMargin = 0.5f
        };
    }

    /// <summary>
    /// Validation criteria for vergence calculations
    /// </summary>
    [System.Serializable]
    public struct VergenceValidationCriteria
    {
        public float maxVergenceDistance;           // Max distance between ray intersections
        public float maxConvergenceAngle;           // Max convergence angle in degrees
        public float minConvergenceAngle;           // Min convergence angle for distant objects
        public bool requireBothEyes;                // Require both eyes for calculation

        public static VergenceValidationCriteria Default => new VergenceValidationCriteria
        {
            maxVergenceDistance = 2.0f,
            maxConvergenceAngle = 60f,
            minConvergenceAngle = 0.001f,  // Very low for distant objects
            requireBothEyes = true
        };
    }

    /// <summary>
    /// Settings specific to Weighted EMA smoothing method
    /// </summary>
    [System.Serializable]
    public struct WeightedEMASettings
    {
        [Tooltip("Smoothing strength (0 = no smoothing, 1 = maximum smoothing)")]
        [Range(0f, 1f)]
        public float smoothingFactor;

        [Tooltip("Adjust smoothing based on distance and quality")]
        public bool adaptiveSmoothing;

        [Tooltip("Number of samples in history buffer")]
        [Range(2, 10)]
        public int bufferSize;

        public static WeightedEMASettings Default => new WeightedEMASettings
        {
            smoothingFactor = 0.5f,
            adaptiveSmoothing = true,
            bufferSize = 5
        };
    }

    /// <summary>
    /// Settings specific to Butterworth low-pass filter
    /// </summary>
    [System.Serializable]
    public struct ButterworthSettings
    {
        [Tooltip("Cutoff frequency (0.01-0.5). Lower = more smoothing, higher lag")]
        [Range(0.01f, 0.5f)]
        public float cutoffFrequency;

        public static ButterworthSettings Default => new ButterworthSettings
        {
            cutoffFrequency = 0.1f
        };
    }

    /// <summary>
    /// Settings specific to Savitzky-Golay polynomial smoothing
    /// </summary>
    [System.Serializable]
    public struct SavitzkyGolaySettings
    {
        [Tooltip("Window size (odd numbers only). Larger = smoother but more lag. 5-11 recommended.")]
        [Range(5, 11)]
        public int windowSize;

        public static SavitzkyGolaySettings Default => new SavitzkyGolaySettings
        {
            windowSize = 5  // 5-point is default (faster, less lag)
        };
    }

    /// <summary>
    /// Smoothing configuration for vergence calculations.
    /// Select a method, then configure its specific settings below.
    /// </summary>
    [System.Serializable]
    public struct VergenceSmoothingSettings
    {
        [Tooltip("Enable vergence point smoothing")]
        public bool enableSmoothing;

        [Tooltip("Smoothing algorithm to use")]
        public VergenceSmoothingMethod method;

        [Header("Method-Specific Settings")]
        [Tooltip("Settings for WeightedEMA method")]
        public WeightedEMASettings weightedEMA;

        [Tooltip("Settings for Butterworth method")]
        public ButterworthSettings butterworth;

        [Tooltip("Settings for Savitzky-Golay method")]
        public SavitzkyGolaySettings savitzkyGolay;

        public static VergenceSmoothingSettings Default => new VergenceSmoothingSettings
        {
            enableSmoothing = true,
            method = VergenceSmoothingMethod.WeightedEMA,
            weightedEMA = WeightedEMASettings.Default,
            butterworth = ButterworthSettings.Default,
            savitzkyGolay = SavitzkyGolaySettings.Default
        };
    }

    /// <summary>
    /// Constraint settings for room bounds validation
    /// </summary>
    [System.Serializable]
    public struct VergenceConstraintSettings
    {
        public VergenceFallbackMethod fallbackMethod;
        public float qualityThreshold;      // Minimum quality for valid calculation
        public bool enableConstraints;      // Enable/disable constraint checking
        public LayerMask raycastLayerMask;  // Layers for surface raycast fallback

        public static VergenceConstraintSettings Default => new VergenceConstraintSettings
        {
            fallbackMethod = VergenceFallbackMethod.LastValidPoint,
            qualityThreshold = 0.1f,
            enableConstraints = false,
            raycastLayerMask = -1
        };
    }

    /// <summary>
    /// Result structure for vergence calculations
    /// </summary>
    public struct VergenceCalculationResult
    {
        public Vector3 rawVergencePoint;        // Uncorrected calculation result
        public Vector3 finalVergencePoint;      // Final processed point
        public float quality;                   // Calculation quality metric
        public bool isValid;                    // Whether calculation is valid
        public VergenceCalculationMethod methodUsed;
        public string debugInfo;                // Debug information
    }
}
