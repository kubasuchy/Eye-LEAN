using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using EyeTracking.Core;
using EyeTracking.Configuration;
using EyeTracking.Data;
using EyeLean.Data;

namespace EyeTracking.Components
{
    /// <summary>
    /// Per-frame CSV recorder. Pulls <see cref="EyeFrameSample"/> from
    /// <see cref="EyeTracker"/> and <see cref="HmdFrameSample"/> from
    /// <see cref="HMDDataCollector"/> in LateUpdate (so each row reflects
    /// this-frame state, not previous-frame), formats them into a single
    /// row, and writes to disk. Owns the CSV writer + lazy header, the
    /// always-on `# CoordinateOrigin` / `# CoordinateOriginSet` / `# Profile`
    /// metadata block, the coord-origin grace window (buffers rows until
    /// SetCoordinateOrigin lands so positions normalize correctly across
    /// the boundary), the session-context + custom-metadata APIs, and the
    /// OnApplicationPause / OnApplicationQuit flush hooks. Per-scene
    /// MonoBehaviour. CSV column layout is contract-stable — Python
    /// `eyelean_analysis` parses against the same fields.
    /// </summary>
    public class SessionRecorder : MonoBehaviour
    {
        [Header("Component References (auto-found if null)")]
        [Tooltip("EyeTracker that supplies the per-frame eye sample. Auto-resolved on the same GameObject if left empty.")]
        [SerializeField] private EyeTracker eyeTracker;
        [Tooltip("HMDDataCollector that supplies the per-frame head pose. Auto-resolved on the same GameObject if left empty.")]
        [SerializeField] private HMDDataCollector hmdCollector;

        [Header("Recording")]
        [Tooltip("Optional DataExportSettings asset. When assigned, overrides the two toggles below and the filename / output-path defaults with the asset's values.")]
        [SerializeField] private DataExportSettings exportSettings;
        [Tooltip("Master switch for the per-frame collection loop. Off = no rows are produced (no CSV, no in-memory buffer).")]
        [SerializeField] private bool enableDataCollection = true;
        [Tooltip("Write rows out to a CSV file. Off = collection still runs (the public RecordKV / SetMetadata API still works) but nothing lands on disk.")]
        [SerializeField] private bool enableCSVExport = true;

        [Header("Custom Experiment Metadata")]
        [Tooltip("Optional pre-defined metadata fields. Auto-initialized when recording starts.")]
        [SerializeField] private EyeLean.Configuration.ExperimentMetadataSchema metadataSchema;
        [Tooltip("Max seconds to defer the lazy CSV header (and buffer pending rows) waiting for SetCoordinateOrigin. The buffered rows are then flushed with normalized positions so the file's coord-origin metadata matches the data.")]
        [SerializeField] private float csvCoordinateOriginGraceSeconds = 2f;

        // Session identification + CSV state. CSV file version 1.1 carries
        // RIPA2 (Jayawardena et al. 2025) in the LiveLoadIndex column; the
        // column name is unchanged for tooling compatibility, but the value
        // semantics differ from earlier files.
        private string sessionId;
        private const string CSV_FILE_VERSION = "1.1";
        private StreamWriter csvWriter;
        private string csvFilePath;
        private bool csvInitialized = false;
        private bool csvHeaderWritten = false;
        private float csvWriterOpenedAt = -1f;
        // Buffer raw samples (struct value-copies) instead of pre-formatted strings:
        // when the coord-origin lands AFTER buffering, we re-normalize positions
        // before formatting. Keeps the file's `# CoordinateOriginSet: True` header
        // honest for every row, including frame-1 / pre-origin frames.
        private readonly Queue<ResearchDataSample> pendingSamples = new Queue<ResearchDataSample>();

        // Trial-start origin snapshot taken from HMDDataCollector at header-write time.
        private Vector3 trialStartWorldPositionAtHeader;
        private bool hasTrialStartPositionAtHeader;

        // Session context
        private string currentParticipantID = "";
        private int currentTrialNumber = 0;
        private string currentSessionPhase = "Recording";
        private string currentSubTask = "";
        private string currentSessionConfig = "Default";

        // Custom metadata
        private EyeLean.Data.ExperimentMetadata customMetadata = new EyeLean.Data.ExperimentMetadata();
        private List<string> cachedMetadataFieldOrder = new List<string>();

        // Per-frame format buffer (reused to avoid GC).
        private StringBuilder csvStringBuilder;
        private Dictionary<string, bool> reusedIntersectionDict;
        private ResearchDataSample currentDataSample;

        private int frameCount = 0;
        private float lastDataCollectionTime = 0f;
        private int totalSamplesCollected = 0;

        // Public API

        public bool IsMetadataSchemaLocked => customMetadata?.IsSchemaLocked ?? false;
        public string GetParticipantID() => currentParticipantID;
        public EyeLean.Data.ExperimentMetadata GetMetadata() => customMetadata;
        public bool RecordingEnabled => enableDataCollection && enableCSVExport && csvInitialized;

        // ---- Pluggable metric columns ----
        //
        // Any component (notably RIPACSVColumn) can call RegisterMetric to
        // contribute a CSV column without recorder-side changes. The
        // recorder evaluates each registered getter once per row and writes
        // its formatted output. Column names are written between
        // DataSampleCount and GazedObjectName, in registration order, so
        // existing positional readers see only an extension of the layout.
        // RegisterMetric calls made AFTER the CSV header is locked are
        // ignored (logged as a warning) — same constraint as DeclareMetadataField.
        public sealed class MetricColumn
        {
            public string Name;
            public Func<string> Getter; // returns the cell text for one row
        }
        private readonly List<MetricColumn> registeredMetrics = new List<MetricColumn>();
        public IReadOnlyList<MetricColumn> RegisteredMetrics => registeredMetrics;

        public void RegisterMetric(string name, Func<string> getter)
        {
            if (string.IsNullOrEmpty(name)) { Debug.LogError("[SessionRecorder] RegisterMetric: name cannot be empty."); return; }
            if (getter == null) { Debug.LogError($"[SessionRecorder] RegisterMetric('{name}'): getter cannot be null."); return; }
            if (csvHeaderWritten)
            {
                Debug.LogError($"[SessionRecorder] Cannot register metric '{name}' — CSV header already written. Register before recording starts (Awake or before SetCoordinateOrigin).");
                return;
            }
            for (int i = 0; i < registeredMetrics.Count; i++)
            {
                if (registeredMetrics[i].Name == name)
                {
                    Debug.LogWarning($"[SessionRecorder] RegisterMetric: replacing existing getter for '{name}'.");
                    registeredMetrics[i].Getter = getter;
                    return;
                }
            }
            registeredMetrics.Add(new MetricColumn { Name = name, Getter = getter });
        }

        public void RegisterMetric(string name, Func<float> getter, string format = "F4")
        {
            if (getter == null) { Debug.LogError($"[SessionRecorder] RegisterMetric('{name}'): getter cannot be null."); return; }
            string fmt = string.IsNullOrEmpty(format) ? "F4" : format;
            RegisterMetric(name, () => getter().ToString(fmt, System.Globalization.CultureInfo.InvariantCulture));
        }

        public bool UnregisterMetric(string name)
        {
            if (csvHeaderWritten)
            {
                Debug.LogError($"[SessionRecorder] Cannot unregister metric '{name}' — CSV header already written.");
                return false;
            }
            for (int i = 0; i < registeredMetrics.Count; i++)
            {
                if (registeredMetrics[i].Name == name) { registeredMetrics.RemoveAt(i); return true; }
            }
            return false;
        }

        // Surface for SceneStateRecorder integration. Path lets the sidecar
        // derive its filename from the main CSV. FrameNumber lets the
        // sidecar share an exact frame counter so rows join on a stable
        // key. OnHeaderWritten fires exactly once when the column header
        // lands; SceneStateRecorder uses it to schema-lock its recordable
        // set in lockstep.
        public string CsvFilePath => csvFilePath;
        public int FrameNumber => frameCount;
        public string SessionId => sessionId;
        public event System.Action OnHeaderWritten;

        private void Awake()
        {
            Debug.Log($"[SessionRecorder] Awake on '{gameObject.name}' (instance {GetInstanceID()})");
            if (eyeTracker == null) eyeTracker = GetComponent<EyeTracker>() ?? FindFirstObjectByType<EyeTracker>();
            if (hmdCollector == null) hmdCollector = GetComponent<HMDDataCollector>() ?? FindFirstObjectByType<HMDDataCollector>();
            if (eyeTracker == null) Debug.LogError("[SessionRecorder] EyeTracker sibling not found — recording will produce empty rows.");
            if (hmdCollector == null) Debug.LogError("[SessionRecorder] HMDDataCollector sibling not found — head pose will be zero.");
        }

        private void Start()
        {
            csvStringBuilder = new StringBuilder(2000);
            reusedIntersectionDict = new Dictionary<string, bool>();
            currentDataSample = new ResearchDataSample();

            if (!enableDataCollection)
            {
                Debug.LogWarning("[SessionRecorder] Data collection DISABLED in inspector");
                return;
            }
            // Deterministic replay: don't open a new CSV during playback —
            // it would write live samples over the original recording. The
            // live experiment still runs (UI, spawns, etc.) but its sample
            // stream goes nowhere.
            if (EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                Debug.Log("[SessionRecorder] ReplayMode active — recording suppressed for this session.");
                enableDataCollection = false;
                return;
            }
            InitializeDataCollection();
        }

        private void LateUpdate()
        {
            if (!enableDataCollection) return;
            frameCount++;

            if (eyeTracker == null || !eyeTracker.HasValidGazeData()) return;

            EyeFrameSample eye = eyeTracker.SampleSnapshot();
            HmdFrameSample hmd = hmdCollector != null ? hmdCollector.SampleSnapshot() : HmdFrameSample.Invalid;

            CollectResearchData(in eye, in hmd);
            ExportDataToCSV();
        }

        // --- Initialization ---

        private void InitializeDataCollection()
        {
            sessionId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" +
                        System.Guid.NewGuid().ToString("N").Substring(0, 8);

            if (exportSettings != null)
            {
                exportSettings.Validate();
                enableCSVExport = exportSettings.EnableCSVExport;
                enableDataCollection = exportSettings.EnableDataCollection;
                Debug.Log($"[SessionRecorder] Using DataExportSettings: CSV={enableCSVExport}, Collection={enableDataCollection}");
            }

            if (metadataSchema != null) InitializeMetadataFromSchema(metadataSchema);
            else CSVHeaderGenerator.SetCustomMetadata(customMetadata);
            cachedMetadataFieldOrder = customMetadata.FieldNames;

            ObjectTrackingRegistry.Initialize();

            if (enableCSVExport) InitializeCSVExport();
        }

        private void InitializeCSVExport()
        {
            string fileName, dataDirectory;
            if (exportSettings != null)
            {
                fileName = exportSettings.GenerateCSVFilename();
                dataDirectory = exportSettings.GetDataPath();
            }
            else
            {
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                fileName = $"EyeTracking_{timestamp}.csv";
                dataDirectory = GetExternalStoragePath();
            }

            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
                Debug.Log($"[SessionRecorder] Created data directory: {dataDirectory}");
            }

            csvFilePath = Path.Combine(dataDirectory, fileName);
            try
            {
                csvWriter = new StreamWriter(csvFilePath, false);
                csvHeaderWritten = false;
                csvWriterOpenedAt = Time.realtimeSinceStartup;
                pendingSamples.Clear();
                csvInitialized = true;
                Debug.Log($"[SessionRecorder] CSV initialized: {csvFilePath} (header deferred until first sample)");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SessionRecorder] Failed to initialize CSV: {e.Message}");
                csvInitialized = false;
            }
        }

        private string GetExternalStoragePath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity != null)
                    {
                        using (var ext = activity.Call<AndroidJavaObject>("getExternalFilesDir", (object)null))
                        {
                            if (ext != null) return ext.Call<string>("getAbsolutePath");
                        }
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[SessionRecorder] Failed Android external dir: {e.Message}"); }
#endif
            return Application.persistentDataPath;
        }

        // --- Headers ---

        private void WriteCSVMetadataHeader()
        {
            csvWriter.WriteLine("# Eye_lean Research Data Export");
            csvWriter.WriteLine($"# FileVersion: {CSV_FILE_VERSION}");
            csvWriter.WriteLine($"# SessionID: {sessionId}");
            csvWriter.WriteLine($"# Timestamp: {System.DateTime.UtcNow:o}");
            csvWriter.WriteLine($"# Device: {SystemInfo.deviceModel}");
            csvWriter.WriteLine("#");
        }

        /// <summary>
        /// Always-on metadata: CoordinateOrigin + CoordinateOriginSet so
        /// post-hoc analysis can de-normalize HeadPos_*/Origin_* columns.
        /// Plus the active EyeTrackingProfile name and combined yaw/pitch
        /// so analysis knows which (if any) software-side correction was
        /// applied. Snapshot the origin from HMDDataCollector at header-
        /// write time so it reflects whatever SetCoordinateOrigin landed
        /// during the grace window (or the unset fallback).
        /// </summary>
        private void WriteCoordinateOriginHeader()
        {
            if (hmdCollector != null)
            {
                trialStartWorldPositionAtHeader = hmdCollector.CurrentTrialStartPosition;
                hasTrialStartPositionAtHeader = hmdCollector.HasTrialStartPosition;
            }
            csvWriter.WriteLine($"# CoordinateOrigin: {trialStartWorldPositionAtHeader.x:F4},{trialStartWorldPositionAtHeader.y:F4},{trialStartWorldPositionAtHeader.z:F4}");
            csvWriter.WriteLine($"# CoordinateOriginSet: {hasTrialStartPositionAtHeader}");
            EyeTrackingProfile activeProfile = ActiveProfile.Current;
            if (activeProfile != null)
            {
                string profileName = string.IsNullOrEmpty(activeProfile.metadata.profileName) ? "(unnamed)" : activeProfile.metadata.profileName;
                var combined = activeProfile.combinedGaze;
                csvWriter.WriteLine($"# Profile: {profileName}");
                csvWriter.WriteLine($"# ProfileCombinedYawDeg: {combined.gazeYawOffsetDeg:F4}");
                csvWriter.WriteLine($"# ProfileCombinedPitchDeg: {combined.gazePitchOffsetDeg:F4}");
            }
            else csvWriter.WriteLine("# Profile: none");
        }

        private void WriteCSVHeaderIfNeeded()
        {
            if (csvHeaderWritten || csvWriter == null) return;
            // Default ON: write the descriptive metadata block whenever
            // there's no exportSettings asset, or the asset opts in.
            // Explicit opt-out is the only path to "no comments".
            if (exportSettings == null || exportSettings.IncludeHeaderComments) WriteCSVMetadataHeader();
            WriteCoordinateOriginHeader();
            // Re-init ObjectTrackingRegistry if it came up empty when SessionRecorder.Start
            // ran (e.g. the experiment scene's room/props spawn after Start). The dynamic
            // Gaze_<obj> column block is generated NOW, so this is the last chance to
            // populate it before the header is locked. Also covers the case where the
            // env generator only finishes spawning by the time SetCoordinateOrigin lands.
            if (ObjectTrackingRegistry.GetTrackedObjectNames().Count == 0)
            {
                Debug.LogWarning("[SessionRecorder] ObjectTrackingRegistry empty at header-write — reinitializing to pick up scene colliders that spawned after Start.");
                ObjectTrackingRegistry.Reinitialize();
                int reinitCount = ObjectTrackingRegistry.GetTrackedObjectNames().Count;
                if (reinitCount > 0) Debug.Log($"[SessionRecorder] Reinit registered {reinitCount} tracked objects.");
                else Debug.LogWarning("[SessionRecorder] Reinit still found 0 tracked objects — Gaze_<obj> columns will be absent.");
            }
            // Header generator emits registered metric column names between
            // DataSampleCount and GazedObjectName, in RegisterMetric order.
            var metricNames = new List<string>(registeredMetrics.Count);
            for (int i = 0; i < registeredMetrics.Count; i++) metricNames.Add(registeredMetrics[i].Name);
            csvWriter.WriteLine(CSVHeaderGenerator.GenerateHeader(metricNames));
            csvWriter.Flush();
            if (customMetadata != null) customMetadata.LockSchema();
            csvHeaderWritten = true;
            // Fire AFTER the flag flips so subscribers (SceneStateRecorder)
            // can call back into RecordingEnabled / CsvFilePath safely.
            OnHeaderWritten?.Invoke();
        }

        // Re-normalize positions on a sample collected before
        // SetCoordinateOrigin landed (NormalizePosition returned
        // world-space). Keeps CoordinateOriginSet:True honest across the
        // grace-window boundary. Mirrors the valid/invalid gating in
        // CollectResearchData: invalid combined / vergence are stored as
        // Vector3.zero and stay zero; per-eye origins are re-subtracted
        // unconditionally to match the live path.
        private static void RenormalizePositionsForFlush(ref ResearchDataSample s, Vector3 origin)
        {
            s.HeadPosition -= origin;
            if (s.HasCombinedOrigin) s.CombinedGazeOrigin -= origin;
            s.LeftEyeOrigin -= origin;
            s.RightEyeOrigin -= origin;
            if (s.HasValidVergence) s.VergencePoint -= origin;
        }

        // --- Per-frame collection + format ---

        private void CollectResearchData(in EyeFrameSample eye, in HmdFrameSample hmd)
        {
            float currentTime = Time.time;
            if (currentDataSample.ObjectGazeIntersections == null) currentDataSample = new ResearchDataSample();

            // Timing
            currentDataSample.UnityTimestamp = currentTime;
            currentDataSample.RealTimeSinceStartup = Time.realtimeSinceStartup;
            currentDataSample.SystemTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            currentDataSample.FrameNumber = frameCount;
            currentDataSample.DeltaTime = currentTime - lastDataCollectionTime;

            // Session context
            currentDataSample.TrialNumber = currentTrialNumber;
            currentDataSample.CurrentPhase = currentSessionPhase;
            currentDataSample.SubTask = currentSubTask;
            currentDataSample.SessionConfig = currentSessionConfig;
            currentDataSample.IsDebugMode = eyeTracker != null && eyeTracker.IsDebugMode();

            // Custom metadata snapshot
            var metadataSnapshot = customMetadata.CreateOrderedSnapshot();
            currentDataSample.CustomMetadataFieldOrder = metadataSnapshot.fieldOrder;
            currentDataSample.CustomMetadata = metadataSnapshot.values;

            // Head pose (normalized) — match pre-refactor semantics
            currentDataSample.HeadPosition = NormalizePosition(hmd.HeadPosition);
            currentDataSample.HeadRotation = hmd.HeadRotation;
            currentDataSample.HeadForward = hmd.HeadForward;
            currentDataSample.HeadRight = hmd.HeadRight;
            currentDataSample.HeadUp = hmd.HeadUp;

            // Eye data (combined + per-eye), normalized for positions
            if (eye.HasCombinedValid)
            {
                currentDataSample.CombinedGazeOrigin = NormalizePosition(eye.CombinedOrigin);
                currentDataSample.CombinedGazeDirection = eye.CombinedDirection;
                currentDataSample.HasCombinedOrigin = true;
                currentDataSample.HasCombinedDirection = true;
            }
            else
            {
                currentDataSample.CombinedGazeOrigin = Vector3.zero;
                currentDataSample.CombinedGazeDirection = Vector3.zero;
                currentDataSample.HasCombinedOrigin = false;
                currentDataSample.HasCombinedDirection = false;
            }

            currentDataSample.HasLeftOrigin = eye.HasLeftValid;
            currentDataSample.HasLeftDirection = eye.HasLeftValid;
            currentDataSample.HasLeftOpenness = eye.HasLeftValid;
            currentDataSample.HasLeftPupilDiameter = eye.HasLeftValid;
            currentDataSample.HasLeftPupilPosition = eye.HasLeftValid;
            currentDataSample.LeftEyeOrigin = NormalizePosition(eye.LeftOrigin);
            currentDataSample.LeftEyeDirection = eye.LeftDirection;
            currentDataSample.LeftEyeOpenness = eye.LeftOpenness;
            currentDataSample.LeftPupilDiameter = eye.LeftPupilDiameter;
            currentDataSample.LeftPupilPosition = eye.LeftPupilPosition;

            currentDataSample.HasRightOrigin = eye.HasRightValid;
            currentDataSample.HasRightDirection = eye.HasRightValid;
            currentDataSample.HasRightOpenness = eye.HasRightValid;
            currentDataSample.HasRightPupilDiameter = eye.HasRightValid;
            currentDataSample.HasRightPupilPosition = eye.HasRightValid;
            currentDataSample.RightEyeOrigin = NormalizePosition(eye.RightOrigin);
            currentDataSample.RightEyeDirection = eye.RightDirection;
            currentDataSample.RightEyeOpenness = eye.RightOpenness;
            currentDataSample.RightPupilDiameter = eye.RightPupilDiameter;
            currentDataSample.RightPupilPosition = eye.RightPupilPosition;

            currentDataSample.IsEyeTrackingAvailable = eye.HasLeftValid || eye.HasRightValid;
            currentDataSample.IsTrackingValid = eye.HasLeftValid || eye.HasRightValid;

            // Vergence
            if (eye.HasValidVergence)
            {
                currentDataSample.VergencePoint = NormalizePosition(eye.VergencePoint);
                currentDataSample.VergenceQuality = eye.VergenceQuality;
                currentDataSample.HasValidVergence = true;
            }
            else
            {
                currentDataSample.VergencePoint = Vector3.zero;
                currentDataSample.VergenceQuality = 0f;
                currentDataSample.HasValidVergence = false;
            }

            // Currently gazed object (name; boolean per-object columns are written separately).
            currentDataSample.GazedObjectName = eye.GazedObjectName ?? string.Empty;

            // Object gaze intersections — use the same gaze ray semantics as the monolith
            CollectObjectGazeIntersections(in eye, in hmd);

            currentDataSample.CurrentFPS = hmd.IsValid ? hmd.Fps : (Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f);
            currentDataSample.FrameTimeMs = Time.unscaledDeltaTime * 1000f;
            currentDataSample.DataSampleCount = totalSamplesCollected;

            lastDataCollectionTime = currentTime;
            totalSamplesCollected++;
        }

        private void CollectObjectGazeIntersections(in EyeFrameSample eye, in HmdFrameSample hmd)
        {
            Vector3 gazeOrigin, gazeDirection;
            if (currentDataSample.HasCombinedOrigin && currentDataSample.HasCombinedDirection)
            {
                gazeOrigin = currentDataSample.CombinedGazeOrigin;
                gazeDirection = currentDataSample.CombinedGazeDirection;
            }
            else if (eye.HasLeftValid)
            {
                // Match monolith fallback shape (camera position + lateral eye offset).
                gazeOrigin = NormalizePosition(hmd.HeadPosition + hmd.HeadRight * -0.08f);
                gazeDirection = eye.LeftDirection;
            }
            else if (eye.HasRightValid)
            {
                gazeOrigin = NormalizePosition(hmd.HeadPosition + hmd.HeadRight * 0.08f);
                gazeDirection = eye.RightDirection;
            }
            else
            {
                currentDataSample.ObjectGazeIntersections = new Dictionary<string, bool>();
                return;
            }

            ObjectTrackingRegistry.CheckGazeIntersections(gazeOrigin, gazeDirection, reusedIntersectionDict);
            currentDataSample.ObjectGazeIntersections = new Dictionary<string, bool>(reusedIntersectionDict);
        }

        private void ExportDataToCSV()
        {
            if (!enableCSVExport || !csvInitialized || csvWriter == null) return;
            try
            {
                // Coord-origin grace window: hold raw samples in memory until
                // the origin lands or the grace timeout expires. We buffer the
                // ResearchDataSample (struct value-copy) instead of a formatted
                // string so we can re-normalize positions at flush time once
                // the origin is known — keeping the file's
                // `# CoordinateOriginSet: True` header honest for every row.
                if (!csvHeaderWritten && (hmdCollector == null || !hmdCollector.HasTrialStartPosition))
                {
                    float elapsed = Time.realtimeSinceStartup - csvWriterOpenedAt;
                    if (elapsed < csvCoordinateOriginGraceSeconds)
                    {
                        // Struct value-copy. Reference fields on the sample
                        // (ObjectGazeIntersections dict, custom-metadata
                        // dict/list) are freshly allocated each frame in
                        // CollectResearchData, so sharing those refs is safe.
                        pendingSamples.Enqueue(currentDataSample);
                        return;
                    }
                    Debug.LogWarning($"[SessionRecorder] Coordinate-origin grace window ({csvCoordinateOriginGraceSeconds:F1}s) expired with no SetCoordinateOrigin call; CSV header will record CoordinateOriginSet:False and {pendingSamples.Count} buffered rows will be written as world-space.");
                }

                WriteCSVHeaderIfNeeded();

                // Flush buffered samples. If origin landed during the grace
                // window, re-normalize each buffered sample's positions before
                // formatting so the row matches the header's
                // CoordinateOriginSet:True claim. If the grace window expired
                // without origin set, originLanded is false and rows are
                // written world-space (matches the warning above).
                bool originLanded = hmdCollector != null && hmdCollector.HasTrialStartPosition;
                Vector3 origin = originLanded ? hmdCollector.CurrentTrialStartPosition : Vector3.zero;
                while (pendingSamples.Count > 0)
                {
                    ResearchDataSample buffered = pendingSamples.Dequeue();
                    if (originLanded) RenormalizePositionsForFlush(ref buffered, origin);
                    csvWriter.WriteLine(FormatDataSampleAsCSV(buffered));
                }
                csvWriter.WriteLine(FormatDataSampleAsCSV(currentDataSample));

                int flushInterval = exportSettings != null ? exportSettings.CSVFlushInterval : 60;
                if (frameCount % flushInterval == 0) csvWriter.Flush();
            }
            catch (System.Exception e) { Debug.LogError($"[SessionRecorder] CSV export error: {e.Message}"); }
        }

        // CSV row format is contract-stable. Any change here breaks the
        // Python eyelean_analysis loader's column expectations.
        private string FormatDataSampleAsCSV(ResearchDataSample s)
        {
            csvStringBuilder.Clear();
            csvStringBuilder.Append($"{s.UnityTimestamp:F6},{s.RealTimeSinceStartup:F6},{s.SystemTimestamp},{s.FrameNumber},{s.DeltaTime:F6},");
            csvStringBuilder.Append($"{s.TrialNumber},{s.CurrentPhase},{s.SubTask},{s.SessionConfig},{s.IsDebugMode},");
            if (s.CustomMetadataFieldOrder != null && s.CustomMetadataFieldOrder.Count > 0)
            {
                string md = EyeLean.Data.ExperimentMetadata.GetCSVRowFromSnapshot(s.CustomMetadataFieldOrder, s.CustomMetadata);
                if (!string.IsNullOrEmpty(md)) { csvStringBuilder.Append(md); csvStringBuilder.Append(","); }
            }
            Vector3 pos = s.HeadPosition; Quaternion rot = s.HeadRotation;
            Vector3 fwd = s.HeadForward; Vector3 right = s.HeadRight; Vector3 up = s.HeadUp;
            csvStringBuilder.Append($"{pos.x:F6},{pos.y:F6},{pos.z:F6},{rot.x:F6},{rot.y:F6},{rot.z:F6},{rot.w:F6},");
            csvStringBuilder.Append($"{fwd.x:F6},{fwd.y:F6},{fwd.z:F6},{right.x:F6},{right.y:F6},{right.z:F6},{up.x:F6},{up.y:F6},{up.z:F6},");
            Vector3 co = s.CombinedGazeOrigin, cd = s.CombinedGazeDirection;
            csvStringBuilder.Append($"{co.x:F6},{co.y:F6},{co.z:F6},{cd.x:F6},{cd.y:F6},{cd.z:F6},");
            csvStringBuilder.Append($"{s.HasCombinedOrigin},{s.HasCombinedDirection},");
            Vector3 lo = s.LeftEyeOrigin, ld = s.LeftEyeDirection; Vector2 lpp = s.LeftPupilPosition;
            csvStringBuilder.Append($"{lo.x:F6},{lo.y:F6},{lo.z:F6},{ld.x:F6},{ld.y:F6},{ld.z:F6},");
            csvStringBuilder.Append($"{s.LeftEyeOpenness:F6},{s.LeftPupilDiameter:F6},{lpp.x:F6},{lpp.y:F6},");
            csvStringBuilder.Append($"{s.HasLeftOrigin},{s.HasLeftDirection},{s.HasLeftOpenness},{s.HasLeftPupilDiameter},{s.HasLeftPupilPosition},");
            Vector3 ro = s.RightEyeOrigin, rd = s.RightEyeDirection; Vector2 rpp = s.RightPupilPosition;
            csvStringBuilder.Append($"{ro.x:F6},{ro.y:F6},{ro.z:F6},{rd.x:F6},{rd.y:F6},{rd.z:F6},");
            csvStringBuilder.Append($"{s.RightEyeOpenness:F6},{s.RightPupilDiameter:F6},{rpp.x:F6},{rpp.y:F6},");
            csvStringBuilder.Append($"{s.HasRightOrigin},{s.HasRightDirection},{s.HasRightOpenness},{s.HasRightPupilDiameter},{s.HasRightPupilPosition},");
            csvStringBuilder.Append($"{s.IsEyeTrackingAvailable},{s.IsTrackingValid},");
            Vector3 v = s.VergencePoint;
            csvStringBuilder.Append($"{v.x:F6},{v.y:F6},{v.z:F6},{s.VergenceQuality:F6},{s.HasValidVergence},");
            var objectNames = ObjectTrackingRegistry.GetTrackedObjectNames();
            foreach (string objName in objectNames)
            {
                bool isGazedAt = s.ObjectGazeIntersections.ContainsKey(objName) && s.ObjectGazeIntersections[objName];
                csvStringBuilder.Append($"{isGazedAt},");
            }
            csvStringBuilder.Append($"{s.CurrentFPS:F2},{s.FrameTimeMs:F2},{s.DataSampleCount},");
            // Registered metric columns. Each getter is invoked once per
            // row; null/exception -> empty cell so a buggy researcher
            // metric can't kill the recording stream.
            for (int i = 0; i < registeredMetrics.Count; i++)
            {
                string cell;
                try { cell = registeredMetrics[i].Getter?.Invoke() ?? string.Empty; }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    cell = string.Empty;
                }
                if (cell.IndexOf(',') >= 0) cell = cell.Replace(',', '_');
                csvStringBuilder.Append(cell);
                csvStringBuilder.Append(',');
            }
            // Sanitize gazed-object name so a stray comma in a GameObject name
            // can't break column alignment.
            string gazedName = s.GazedObjectName ?? string.Empty;
            if (gazedName.IndexOf(',') >= 0) gazedName = gazedName.Replace(',', '_');
            csvStringBuilder.Append(gazedName);
            return csvStringBuilder.ToString();
        }

        private Vector3 NormalizePosition(Vector3 worldPosition)
        {
            if (hmdCollector != null && hmdCollector.HasTrialStartPosition)
                return worldPosition - hmdCollector.CurrentTrialStartPosition;
            return worldPosition;
        }

        // --- Lifecycle hooks for crash protection ---

        private void OnApplicationQuit()
        {
            if (csvWriter != null)
            {
                try { csvWriter.Flush(); csvWriter.Close(); csvWriter.Dispose(); Debug.Log($"[SessionRecorder] CSV file saved: {csvFilePath}"); }
                catch (System.Exception e) { Debug.LogError($"[SessionRecorder] Error closing CSV: {e.Message}"); }
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // Android: user can take headset off, switch apps, or be killed by OS at
            // any time — OnApplicationQuit may never fire. Flush on pause guarantees
            // data buffered since last periodic flush survives. Don't close the writer;
            // the app may resume.
            if (!pauseStatus || csvWriter == null) return;
            try { csvWriter.Flush(); }
            catch (System.Exception e) { Debug.LogError($"[SessionRecorder] Error flushing on pause: {e.Message}"); }
        }

        // --- Session context API ---

        public void SetSessionContext(int trialNumber, string phase = "Recording", string config = "Default", string subTask = "")
        {
            currentTrialNumber = trialNumber;
            currentSessionPhase = phase;
            currentSubTask = subTask;
            currentSessionConfig = config;
            Debug.Log($"[SessionRecorder] Session context updated: Trial={trialNumber}, Phase={phase}, SubTask={subTask}, Config={config}");
        }

        public void SetSubTask(string subTask)
        {
            currentSubTask = subTask ?? "";
            Debug.Log($"[SessionRecorder] SubTask set: {currentSubTask}");
        }

        public void SetParticipantID(string participantID)
        {
            currentParticipantID = participantID ?? "";
            Debug.Log($"[SessionRecorder] Participant ID set: {currentParticipantID}");
        }

        // --- Custom metadata API ---

        public void SetMetadata(string fieldName, string value)
        {
            WarnIfNewFieldAfterCSVInit(fieldName);
            customMetadata.SetString(fieldName, value);
            CSVHeaderGenerator.UpdateCustomMetadataFields();
        }

        public void SetMetadata(string fieldName, int value)
        {
            WarnIfNewFieldAfterCSVInit(fieldName);
            customMetadata.SetInt(fieldName, value);
            CSVHeaderGenerator.UpdateCustomMetadataFields();
        }

        public void SetMetadata(string fieldName, float value)
        {
            WarnIfNewFieldAfterCSVInit(fieldName);
            customMetadata.SetFloat(fieldName, value);
            CSVHeaderGenerator.UpdateCustomMetadataFields();
        }

        public void SetMetadata(string fieldName, bool value)
        {
            WarnIfNewFieldAfterCSVInit(fieldName);
            customMetadata.SetBool(fieldName, value);
            CSVHeaderGenerator.UpdateCustomMetadataFields();
        }

        private void WarnIfNewFieldAfterCSVInit(string fieldName)
        {
            if (csvInitialized && !customMetadata.HasField(fieldName))
            {
                Debug.LogWarning($"[SessionRecorder] WARNING: Adding new metadata field '{fieldName}' after CSV export started will cause column count mismatches. Call DeclareMetadataField BEFORE recording.");
            }
        }

        public void DeclareMetadataField(string fieldName, EyeLean.Data.MetadataValueType type)
        {
            if (csvInitialized)
            {
                Debug.LogError($"[SessionRecorder] Cannot declare field '{fieldName}' — CSV already initialized.");
                return;
            }
            customMetadata.DeclareField(fieldName, type);
            CSVHeaderGenerator.UpdateCustomMetadataFields();
        }

        public bool RemoveMetadata(string fieldName)
        {
            bool removed = customMetadata.Remove(fieldName);
            if (removed) CSVHeaderGenerator.UpdateCustomMetadataFields();
            return removed;
        }

        public void ClearMetadata()
        {
            customMetadata.Clear();
            CSVHeaderGenerator.UpdateCustomMetadataFields();
        }

        public void InitializeMetadataFromSchema(EyeLean.Configuration.ExperimentMetadataSchema schema)
        {
            if (schema == null) return;
            schema.InitializeMetadata(customMetadata);
            CSVHeaderGenerator.SetCustomMetadata(customMetadata);
            cachedMetadataFieldOrder = customMetadata.FieldNames;
            Debug.Log($"[SessionRecorder] Initialized {cachedMetadataFieldOrder.Count} metadata fields from schema");
        }

        // --- Quality metrics passthrough (sourced from EyeTracker's DataQualityMetrics sibling) ---

        public DataQualityMetrics GetQualityMetrics() => eyeTracker != null ? eyeTracker.GetQualityMetrics() : null;

        public void LogQualitySummary()
        {
            var qm = GetQualityMetrics();
            if (qm != null) Debug.Log($"[SessionRecorder] {qm.GetSummary()}");
            else Debug.Log("[SessionRecorder] No DataQualityMetrics attached");
        }

        public void ResetQualityMetrics()
        {
            var qm = GetQualityMetrics();
            if (qm != null) { qm.Reset(); Debug.Log("[SessionRecorder] Quality metrics reset"); }
        }
    }
}
