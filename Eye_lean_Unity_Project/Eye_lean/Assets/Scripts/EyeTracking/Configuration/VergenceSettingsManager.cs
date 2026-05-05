using UnityEngine;
using EyeTracking.Vergence;

namespace EyeTracking.Configuration
{
    /// <summary>
    /// Wrapper class for JSON serialization of vergence settings. Unity's
    /// JsonUtility requires a class wrapper for nested structs.
    /// </summary>
    [System.Serializable]
    public class VergenceSettingsFile
    {
        public string fileVersion = "1.1";
        public string savedAt;
        public string vergenceDepthMode;
        public VergenceCalculationSettings vergenceSettings;
        public VergenceConstraintSettings constraintSettings;
    }
}
