using UnityEngine;
using System.Collections.Generic;
using EyeLean.Data;

/// <summary>
/// Per-frame research-grade sample. Captures raw eye-tracking data without
/// runtime calculations so analysis can derive metrics post-hoc.
/// </summary>
[System.Serializable]
public struct ResearchDataSample
{
    // ============= TIMING DATA =============
    public float UnityTimestamp;           // Time.time
    public float RealTimeSinceStartup;     // Time.realtimeSinceStartup
    public long SystemTimestamp;           // System.DateTime.UtcNow.Ticks
    public int FrameNumber;                // Frame count since start
    public float DeltaTime;                // Time since last sample

    // ============= SESSION CONTEXT =============
    // Participant identity lives at the session level (filenames, header
    // metadata) rather than being duplicated on every row.
    public int TrialNumber;
    public string CurrentPhase;            // Main experimental phase: "CalibrationCheck", "FreeExploration", "VisualSearch", etc.
    public string SubTask;                 // Specific sub-task within phase: "calibration_point_0", "visual_search_trial_1", etc.
    public string SessionConfig;           // General session configuration string
    public bool IsDebugMode;               // TRUE = debug mode with visual feedback, FALSE = production mode

    // ============= HEAD TRACKING DATA =============
    public Vector3 HeadPosition;           // Camera world position
    public Quaternion HeadRotation;        // Camera world rotation
    public Vector3 HeadForward;            // Camera forward vector
    public Vector3 HeadRight;              // Camera right vector
    public Vector3 HeadUp;                 // Camera up vector

    // ============= EYE TRACKING DATA =============
    // Combined Eye Data (most reliable)
    public Vector3 CombinedGazeOrigin;
    public Vector3 CombinedGazeDirection;
    public bool HasCombinedOrigin;
    public bool HasCombinedDirection;

    // Left Eye - All Available Fields
    public Vector3 LeftEyeOrigin;
    public Vector3 LeftEyeDirection;
    public float LeftEyeOpenness;
    public float LeftPupilDiameter;
    public Vector2 LeftPupilPosition;      // Position in sensor area
    public bool HasLeftOrigin;
    public bool HasLeftDirection;
    public bool HasLeftOpenness;
    public bool HasLeftPupilDiameter;
    public bool HasLeftPupilPosition;

    // Right Eye - All Available Fields
    public Vector3 RightEyeOrigin;
    public Vector3 RightEyeDirection;
    public float RightEyeOpenness;
    public float RightPupilDiameter;
    public Vector2 RightPupilPosition;     // Position in sensor area
    public bool HasRightOrigin;
    public bool HasRightDirection;
    public bool HasRightOpenness;
    public bool HasRightPupilDiameter;
    public bool HasRightPupilPosition;

    // Eye Tracking System Status
    public bool IsEyeTrackingAvailable;
    public bool IsTrackingValid;

    // ============= VERGENCE DATA (Raw Only) =============
    public Vector3 VergencePoint;          // Simple intersection result
    public float VergenceQuality;          // Distance between ray intersections
    public bool HasValidVergence;

    // ============= CURRENT GAZED OBJECT =============
    // Name of the GameObject the EyeTracker has identified as the current
    // gaze target this frame (empty string when none). Pulled from
    // EyeFrameSample.GazedObjectName, which sources EyeTracker.currentGazedObject.
    public string GazedObjectName;

    // ============= DYNAMIC OBJECT GAZE INTERSECTIONS =============
    // This will be populated dynamically based on scene objects with colliders
    // Format: ObjectName -> true/false for gaze intersection
    // Example: "Door_01" -> true, "Table_02" -> false, etc.
    public Dictionary<string, bool> ObjectGazeIntersections;

    // ============= CUSTOM EXPERIMENT METADATA =============
    // Researcher-defined metadata fields (Condition, Block, Stimulus, etc.)
    // Set via eyeTracker.SetMetadata("fieldName", value)
    public List<string> CustomMetadataFieldOrder;
    public Dictionary<string, MetadataValue> CustomMetadata;

    // ============= PERFORMANCE DATA =============
    public float CurrentFPS;
    public float FrameTimeMs;              // Milliseconds for this frame
    public int DataSampleCount;            // Total samples collected so far
}

/// <summary>
/// Auto-discovers GameObjects with colliders and tracks per-frame gaze
/// intersection state for each.
/// </summary>
public class ObjectTrackingRegistry
{
    private static List<GameObject> trackedObjects = new List<GameObject>();
    private static List<string> objectNames = new List<string>();
    private static bool isInitialized = false;

    // Pre-allocated raycast buffer to avoid per-frame allocations.
    private static RaycastHit[] reusedRaycastHits = new RaycastHit[50];

    /// <summary>
    /// Discover all GameObjects with non-trigger colliders. Call once per scene.
    /// </summary>
    public static void Initialize()
    {
        if (isInitialized) return;

        trackedObjects.Clear();
        objectNames.Clear();

        Collider[] allColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);

        foreach (Collider col in allColliders)
        {
            if (col.isTrigger) continue;

            trackedObjects.Add(col.gameObject);
            objectNames.Add(col.gameObject.name);
        }

        isInitialized = true;
        UnityEngine.Debug.Log($"[ObjectTrackingRegistry] Initialized tracking for {trackedObjects.Count} objects with colliders");

        for (int i = 0; i < objectNames.Count; i++)
        {
            UnityEngine.Debug.Log($"[ObjectTrackingRegistry] Tracking object: '{objectNames[i]}'");
        }
    }

    /// <summary>
    /// Reinitialize tracking (useful after scene changes)
    /// </summary>
    public static void Reinitialize()
    {
        isInitialized = false;
        Initialize();
    }

    /// <summary>
    /// Get all tracked object names for CSV header generation
    /// </summary>
    public static List<string> GetTrackedObjectNames()
    {
        if (!isInitialized) Initialize();
        return new List<string>(objectNames);
    }

    /// <summary>
    /// Perform efficient raycast against all tracked objects
    /// Uses provided dictionary to avoid GC allocations
    /// </summary>
    public static void CheckGazeIntersections(Vector3 rayOrigin, Vector3 rayDirection, Dictionary<string, bool> reusedDict, float maxDistance = 50f)
    {
        if (!isInitialized) Initialize();

        reusedDict.Clear();

        int hitCount = Physics.RaycastNonAlloc(rayOrigin, rayDirection, reusedRaycastHits, maxDistance);

        for (int i = 0; i < objectNames.Count; i++)
        {
            reusedDict[objectNames[i]] = false;
        }

        for (int i = 0; i < hitCount; i++)
        {
            string hitObjectName = reusedRaycastHits[i].collider.gameObject.name;
            if (reusedDict.ContainsKey(hitObjectName))
            {
                reusedDict[hitObjectName] = true;
            }
        }
    }

    /// <summary>
    /// Legacy method for backward compatibility - allocates new dictionary
    /// </summary>
    public static Dictionary<string, bool> CheckGazeIntersections(Vector3 rayOrigin, Vector3 rayDirection, float maxDistance = 50f)
    {
        Dictionary<string, bool> intersections = new Dictionary<string, bool>();
        CheckGazeIntersections(rayOrigin, rayDirection, intersections, maxDistance);
        return intersections;
    }

    /// <summary>
    /// Add a new object to tracking (for dynamically spawned objects)
    /// </summary>
    public static void RegisterNewObject(GameObject obj)
    {
        if (obj.GetComponent<Collider>() != null && !trackedObjects.Contains(obj))
        {
            trackedObjects.Add(obj);
            objectNames.Add(obj.name);
            UnityEngine.Debug.Log($"[ObjectTrackingRegistry] Added new tracked object: '{obj.name}'");
        }
    }
}

/// <summary>
/// CSV Header Generator - creates dynamic headers based on tracked objects and custom metadata
/// </summary>
public static class CSVHeaderGenerator
{
    // Static reference to custom metadata for header generation
    private static ExperimentMetadata _customMetadata;
    private static List<string> _cachedMetadataFields = new List<string>();

    /// <summary>
    /// Register custom metadata container for header generation.
    /// Call this before generating the first CSV header.
    /// </summary>
    public static void SetCustomMetadata(ExperimentMetadata metadata)
    {
        _customMetadata = metadata;
        if (metadata != null)
        {
            _cachedMetadataFields = metadata.FieldNames;
        }
        else
        {
            _cachedMetadataFields.Clear();
        }
    }

    /// <summary>
    /// Update the cached metadata field list.
    /// Call this if you add new metadata fields after initial registration.
    /// </summary>
    public static void UpdateCustomMetadataFields()
    {
        if (_customMetadata != null)
        {
            _cachedMetadataFields = _customMetadata.FieldNames;
        }
    }

    /// <summary>
    /// Get the ordered list of custom metadata field names.
    /// </summary>
    public static List<string> GetCustomMetadataFieldNames()
    {
        return new List<string>(_cachedMetadataFields);
    }

    /// <summary>
    /// Generate complete CSV header including all object columns and custom metadata.
    /// Registered metric column names (passed in by SessionRecorder) are
    /// inserted between DataSampleCount and GazedObjectName, in registration order.
    /// </summary>
    public static string GenerateHeader(IList<string> registeredMetricNames = null)
    {
        System.Text.StringBuilder header = new System.Text.StringBuilder();

        // Timing columns
        header.Append("UnityTimestamp,RealTimeSinceStartup,SystemTimestamp,FrameNumber,DeltaTime,");

        // Session context (ParticipantID intentionally omitted; tracked at session level)
        header.Append("TrialNumber,CurrentPhase,SubTask,SessionConfig,IsDebugMode,");

        // Custom experiment metadata (inserted after built-in session context)
        if (_cachedMetadataFields.Count > 0)
        {
            foreach (string fieldName in _cachedMetadataFields)
            {
                header.Append($"{fieldName},");
            }
        }

        // Head tracking
        header.Append("HeadPos_X,HeadPos_Y,HeadPos_Z,HeadRot_X,HeadRot_Y,HeadRot_Z,HeadRot_W,");
        header.Append("HeadForward_X,HeadForward_Y,HeadForward_Z,HeadRight_X,HeadRight_Y,HeadRight_Z,HeadUp_X,HeadUp_Y,HeadUp_Z,");

        // Combined eye data
        header.Append("CombinedOrigin_X,CombinedOrigin_Y,CombinedOrigin_Z,CombinedDir_X,CombinedDir_Y,CombinedDir_Z,");
        header.Append("HasCombinedOrigin,HasCombinedDirection,");

        // Left eye data
        header.Append("LeftOrigin_X,LeftOrigin_Y,LeftOrigin_Z,LeftDir_X,LeftDir_Y,LeftDir_Z,");
        header.Append("LeftOpenness,LeftPupilDiameter,LeftPupilPos_X,LeftPupilPos_Y,");
        header.Append("HasLeftOrigin,HasLeftDirection,HasLeftOpenness,HasLeftPupilDiameter,HasLeftPupilPosition,");

        // Right eye data
        header.Append("RightOrigin_X,RightOrigin_Y,RightOrigin_Z,RightDir_X,RightDir_Y,RightDir_Z,");
        header.Append("RightOpenness,RightPupilDiameter,RightPupilPos_X,RightPupilPos_Y,");
        header.Append("HasRightOrigin,HasRightDirection,HasRightOpenness,HasRightPupilDiameter,HasRightPupilPosition,");

        // System status
        header.Append("IsEyeTrackingAvailable,IsTrackingValid,");

        // Vergence data (raw only)
        header.Append("VergencePoint_X,VergencePoint_Y,VergencePoint_Z,VergenceQuality,HasValidVergence,");

        // Dynamic object gaze intersections
        List<string> objectNames = ObjectTrackingRegistry.GetTrackedObjectNames();
        foreach (string objName in objectNames)
        {
            header.Append($"Gaze_{objName},");
        }

        // Performance data
        header.Append("CurrentFPS,FrameTimeMs,DataSampleCount,");

        // Registered metric columns. Order matches SessionRecorder.RegisterMetric
        // call order. The default scene bootstrap registers `LiveLoadIndex`
        // (RIPA2 from RIPAMonitor).
        if (registeredMetricNames != null)
        {
            foreach (string metricName in registeredMetricNames)
            {
                if (string.IsNullOrEmpty(metricName)) continue;
                header.Append(metricName);
                header.Append(',');
            }
        }

        // Current gazed object (name only; per-object boolean Gaze_<obj>
        // columns above already cover the full registered set). Appended
        // last so existing positional-readers don't shift.
        header.Append("GazedObjectName");

        return header.ToString();
    }
}
