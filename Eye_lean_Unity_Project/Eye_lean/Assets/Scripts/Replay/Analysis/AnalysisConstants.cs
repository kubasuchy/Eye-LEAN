namespace EyeLean.Replay.Analysis
{
    /// <summary>
    /// Centralized analysis constants for eye tracking metrics.
    /// These values are synchronized with the Python eyelean_analysis package.
    ///
    /// References:
    ///   - Krejtz, K., Duchowski, A. T., Niber, T., Krejtz, I., & Kopacz, A. (2016).
    ///     Eye tracking cognitive load using pupil diameter and microsaccades with
    ///     fixed gaze. PLoS ONE, 11(9), e0163087.
    ///   - Salvucci, D. D., & Goldberg, J. H. (2000).
    ///     Identifying fixations and saccades in eye-tracking protocols.
    ///     Proceedings of the 2000 symposium on Eye tracking research & applications.
    /// </summary>
    public static class AnalysisConstants
    {
        #region K-Coefficient Thresholds

        /// <summary>
        /// Visualisation-only neutral band: |K| &lt; deadZone is reported as
        /// Neutral. Krejtz 2016 classifies by sign, so the default is 0.
        /// </summary>
        public const float K_COEFFICIENT_DEAD_ZONE = 0.0f;

        /// <summary>
        /// Window size for K-coefficient calculation (number of fixation-saccade pairs).
        /// </summary>
        public const int K_COEFFICIENT_WINDOW_SIZE = 30;

        #endregion

        #region Velocity Thresholds

        /// <summary>
        /// Velocity threshold for saccade detection (degrees/second).
        /// Values above this are classified as saccades.
        /// Based on I-VT (velocity threshold identification) algorithm.
        /// </summary>
        public const float SACCADE_VELOCITY_THRESHOLD = 50f;

        /// <summary>
        /// Minimum fixation duration in seconds.
        /// Eye movements shorter than this are not considered valid fixations.
        /// </summary>
        public const float MIN_FIXATION_DURATION = 0.1f;

        /// <summary>
        /// Minimum saccade duration in seconds.
        /// </summary>
        public const float MIN_SACCADE_DURATION = 0.02f;

        #endregion

        #region Pupil Metrics

        /// <summary>
        /// Minimum valid pupil diameter in millimeters.
        /// Values below this are considered invalid/tracking loss.
        /// </summary>
        public const float MIN_VALID_PUPIL_DIAMETER = 1.5f;

        /// <summary>
        /// Maximum valid pupil diameter in millimeters.
        /// Values above this are considered invalid/artifacts.
        /// </summary>
        public const float MAX_VALID_PUPIL_DIAMETER = 9.0f;

        #endregion

        #region Signal Quality

        /// <summary>
        /// Maximum allowed gap in data (seconds) before interpolation is not recommended.
        /// </summary>
        public const float MAX_INTERPOLATION_GAP = 0.075f;

        /// <summary>
        /// Minimum data quality ratio (valid samples / total samples) for reliable analysis.
        /// </summary>
        public const float MIN_DATA_QUALITY_RATIO = 0.7f;

        #endregion

        #region Smoothing Parameters

        /// <summary>
        /// Default smoothing factor for velocity calculations.
        /// Lower values = smoother output.
        /// </summary>
        public const float DEFAULT_VELOCITY_SMOOTHING_FACTOR = 0.3f;

        #endregion
    }
}
