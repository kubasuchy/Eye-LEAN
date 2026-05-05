using UnityEngine;

namespace EyeTracking.Configuration
{
    /// <summary>
    /// Configuration settings for data collection and export.
    /// Create via Assets > Create > Eye Tracking > Data Export Settings
    /// </summary>
    [CreateAssetMenu(fileName = "DataExportSettings", menuName = "Eye Tracking/Data Export Settings")]
    public class DataExportSettings : ScriptableObject
    {
        [Header("Data Collection")]
        [Tooltip("Enable data collection")]
        public bool EnableDataCollection = true;

        [Tooltip("Collect data every N frames (1 = every frame)")]
        [Range(1, 10)]
        public int CollectionInterval = 1;

        [Header("Blink Detection")]
        [Tooltip("Eye openness threshold below which is considered a blink")]
        [Range(0f, 0.5f)]
        public float BlinkThreshold = 0.2f;

        [Tooltip("Minimum blink duration in seconds")]
        [Range(0.05f, 0.5f)]
        public float MinBlinkDuration = 0.1f;

        [Header("Stuck Ray Detection")]
        [Tooltip("Direction change threshold below which ray is considered stuck")]
        [Range(0.0001f, 0.01f)]
        public float StuckRayThreshold = 0.001f;

        [Tooltip("Frames without movement before ray is considered stuck")]
        [Range(30, 120)]
        public int StuckRayFrameThreshold = 60;

        [Header("CSV Export")]
        [Tooltip("Enable CSV file export")]
        public bool EnableCSVExport = true;

        [Tooltip("Flush CSV to disk every N frames (for crash protection)")]
        [Range(30, 300)]
        public int CSVFlushInterval = 60;

        [Tooltip("Include header comments with session metadata")]
        public bool IncludeHeaderComments = true;

        [Tooltip("Use high precision timestamps (more decimal places)")]
        public bool HighPrecisionTimestamps = true;

        [Header("JSON Export")]
        [Tooltip("Export session metadata as JSON")]
        public bool ExportSessionMetadata = true;

        [Tooltip("Export quality summary as JSON")]
        public bool ExportQualitySummary = true;

        [Header("File Paths")]
        [Tooltip("Subdirectory for data files (relative to persistent data path)")]
        public string DataSubdirectory = "EyeTrackingData";

        [Tooltip("File name prefix for CSV files")]
        public string CSVFilePrefix = "GazeData";

        [Tooltip("File name prefix for JSON files")]
        public string JSONFilePrefix = "SessionInfo";

        [Header("Quality Thresholds")]
        [Tooltip("Minimum valid sample percentage for 'Good' rating")]
        [Range(70f, 95f)]
        public float GoodQualityThreshold = 85f;

        [Tooltip("Minimum valid sample percentage for 'Acceptable' rating")]
        [Range(50f, 80f)]
        public float AcceptableQualityThreshold = 70f;

        [Header("Debug")]
        [Tooltip("Log data collection statistics periodically")]
        public bool LogStatistics = false;

        [Tooltip("Statistics logging interval in seconds")]
        [Range(5f, 60f)]
        public float StatisticsLogInterval = 10f;

        /// <summary>
        /// Get the full path for data files.
        /// </summary>
        public string GetDataPath()
        {
            string basePath = Application.persistentDataPath;

            #if UNITY_ANDROID && !UNITY_EDITOR
            // Use app-specific external storage on Android
            basePath = Application.persistentDataPath;
            #endif

            return System.IO.Path.Combine(basePath, DataSubdirectory);
        }

        /// <summary>
        /// Generate a timestamped filename for CSV export.
        /// </summary>
        public string GenerateCSVFilename(string participantID = null)
        {
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string prefix = string.IsNullOrEmpty(participantID)
                ? CSVFilePrefix
                : $"{participantID}_{CSVFilePrefix}";

            return $"{prefix}_{timestamp}.csv";
        }

        /// <summary>
        /// Generate a timestamped filename for JSON export.
        /// </summary>
        public string GenerateJSONFilename(string participantID = null, string suffix = "")
        {
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string prefix = string.IsNullOrEmpty(participantID)
                ? JSONFilePrefix
                : $"{participantID}_{JSONFilePrefix}";

            string name = string.IsNullOrEmpty(suffix)
                ? $"{prefix}_{timestamp}"
                : $"{prefix}_{suffix}_{timestamp}";

            return $"{name}.json";
        }

        /// <summary>
        /// Validate settings and log any issues.
        /// </summary>
        public bool Validate()
        {
            bool valid = true;

            if (CollectionInterval < 1)
            {
                Debug.LogWarning("[DataExportSettings] CollectionInterval must be >= 1");
                CollectionInterval = 1;
                valid = false;
            }

            if (BlinkThreshold < 0f || BlinkThreshold > 1f)
            {
                Debug.LogWarning("[DataExportSettings] BlinkThreshold must be between 0 and 1");
                BlinkThreshold = Mathf.Clamp01(BlinkThreshold);
                valid = false;
            }

            if (string.IsNullOrEmpty(DataSubdirectory))
            {
                Debug.LogWarning("[DataExportSettings] DataSubdirectory cannot be empty");
                DataSubdirectory = "EyeTrackingData";
                valid = false;
            }

            return valid;
        }
    }
}
