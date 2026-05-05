namespace EyeTracking.Vergence
{
    /// <summary>
    /// Unified configuration for vergence calculation system
    /// </summary>
    [System.Serializable]
    public class VergenceCalculationSettings
    {
        public VergencePreset preset = VergencePreset.Balanced;
        public VergenceCalculationMethod method = VergenceCalculationMethod.Simple; // Simple uses ray-ray intersection
        public VergenceDistanceRange distanceRange = new VergenceDistanceRange { minDistance = 0.3f, maxDistance = 100f, wallMargin = 0.5f };
        public VergenceValidationCriteria validation = new VergenceValidationCriteria { maxVergenceDistance = 5.0f, maxConvergenceAngle = 60f, minConvergenceAngle = 0.001f, requireBothEyes = true };
        public VergenceSmoothingSettings smoothing = VergenceSmoothingSettings.Default;

        /// <summary>
        /// Get optimized preset configurations
        /// </summary>
        public static VergenceCalculationSettings GetPreset(VergencePreset presetType)
        {
            var settings = new VergenceCalculationSettings();
            settings.preset = presetType;

            switch (presetType)
            {
                case VergencePreset.Precise:
                    settings.method = VergenceCalculationMethod.PaperAlgorithm;
                    settings.validation.maxVergenceDistance = 1.0f;
                    settings.validation.maxConvergenceAngle = 45f;
                    settings.smoothing.enableSmoothing = false;
                    settings.smoothing.method = VergenceSmoothingMethod.WeightedEMA;
                    settings.smoothing.weightedEMA.smoothingFactor = 0.3f;
                    break;

                case VergencePreset.Balanced:
                    settings.method = VergenceCalculationMethod.Simple;
                    settings.validation.maxVergenceDistance = 2.0f;
                    settings.validation.maxConvergenceAngle = 60f;
                    settings.smoothing.enableSmoothing = true;
                    settings.smoothing.method = VergenceSmoothingMethod.WeightedEMA;
                    settings.smoothing.weightedEMA.smoothingFactor = 0.5f;
                    break;

                case VergencePreset.Stable:
                    settings.method = VergenceCalculationMethod.Simple;
                    settings.validation.maxVergenceDistance = 3.0f;
                    settings.validation.maxConvergenceAngle = 75f;
                    settings.smoothing.enableSmoothing = true;
                    settings.smoothing.method = VergenceSmoothingMethod.WeightedEMA;
                    settings.smoothing.weightedEMA.smoothingFactor = 0.7f;
                    settings.smoothing.weightedEMA.bufferSize = 4;
                    break;

                case VergencePreset.Custom:
                    // Use default values, user will customize
                    break;
            }

            return settings;
        }
    }
}
