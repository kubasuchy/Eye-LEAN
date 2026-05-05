using System.Collections.Generic;
using System.IO;
using UnityEngine;
using EyeTracking.Components;

namespace EyeLean.SceneState
{
    /// <summary>
    /// Per-frame transform recorder for objects opted-in via <see cref="Recordable"/>.
    /// Sibling MonoBehaviour to <see cref="SessionRecorder"/> sharing the
    /// session id, coordinate-origin convention, and frame counter. Writes
    /// the long-format sidecar <c>&lt;mainCsv&gt;_SceneState.csv</c>, joinable
    /// to the main CSV by <c>FrameNumber</c>.
    ///
    /// Header is deferred until the coord-origin lands or a grace window
    /// expires — buffered rows are re-normalized on flush so every row
    /// honours the file's <c>CoordinateOriginSet:True</c> claim. The
    /// recordable set schema-locks at <c>SessionRecorder.OnHeaderWritten</c>;
    /// late-spawned Recordables are still recorded with a SceneEvents row.
    /// Execution order is one slot after SessionRecorder so they read a
    /// consistent frame counter (LateUpdate order is otherwise nondeterministic).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SceneStateRecorder : MonoBehaviour
    {
        [Header("Component References (auto-found if null)")]
        [SerializeField] private SessionRecorder sessionRecorder;
        [SerializeField] private HMDDataCollector hmdCollector;

        [Header("Recording Profile")]
        [Tooltip("Required. ScriptableObject describing which objects to record and at what rate.")]
        [SerializeField] private SceneRecordingProfile profile;

        [Tooltip("Master enable. If off, no sidecar is opened and Recordable registrations are not consumed for recording.")]
        [SerializeField] private bool enableRecording = true;

        [Tooltip("Match SessionRecorder's grace window — the number of seconds we'll buffer rows in memory waiting for SetCoordinateOrigin before flushing world-space.")]
        [SerializeField] private float coordinateOriginGraceSeconds = 2f;

        // Spawn/Despawn/IdRegenerated events go through SceneEventRecorder
        // alongside researcher-defined events.
        private string sidecarPath;
        private StreamWriter sidecarWriter;
        private SceneStateCSVWriter csvWriter;
        private bool initialized;
        private bool headerWritten;
        private float openedAt = -1f;
        private readonly Queue<SceneStateRow> pendingRows = new Queue<SceneStateRow>();

        // Schema (recordable set captured at header-write time)
        private readonly HashSet<string> lockedIds = new HashSet<string>();
        private bool schemaLocked;

        private int lastSampledFrame = -1;

        public bool RecordingEnabled => enableRecording && initialized;
        public string SidecarPath => sidecarPath;

        private void Awake()
        {
            if (sessionRecorder == null) sessionRecorder = GetComponent<SessionRecorder>() ?? FindFirstObjectByType<SessionRecorder>();
            if (hmdCollector == null) hmdCollector = GetComponent<HMDDataCollector>() ?? FindFirstObjectByType<HMDDataCollector>();
        }

        private void OnEnable()
        {
            RecordableRegistry.Changed += HandleRegistryChanged;
            if (hmdCollector != null) hmdCollector.OnCoordinateOriginSet += HandleCoordinateOriginSet;
            if (sessionRecorder != null) sessionRecorder.OnHeaderWritten += HandleSessionHeaderWritten;
        }

        private void OnDisable()
        {
            UnsubscribeAll();
        }

        // Idempotent so it's safe to call from both OnDisable and OnDestroy.
        // Unity's shutdown ordering (domain reload mid-play, editor shutdown
        // with the scene unloaded) can leave RecordableRegistry.Changed
        // holding a dead delegate if only OnDisable runs.
        private void UnsubscribeAll()
        {
            RecordableRegistry.Changed -= HandleRegistryChanged;
            if (hmdCollector != null) hmdCollector.OnCoordinateOriginSet -= HandleCoordinateOriginSet;
            if (sessionRecorder != null) sessionRecorder.OnHeaderWritten -= HandleSessionHeaderWritten;
        }

        private void Start()
        {
            if (!enableRecording) return;
            // Suppress recording during deterministic replay playback.
            if (EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                Debug.Log("[SceneStateRecorder] ReplayMode active — sidecar recording suppressed.");
                enableRecording = false;
                return;
            }
            if (sessionRecorder == null)
            {
                Debug.LogError("[SceneStateRecorder] No SessionRecorder sibling found. Sidecar will not be written.");
                enableRecording = false;
                return;
            }
            if (profile == null)
            {
                Debug.LogWarning("[SceneStateRecorder] No SceneRecordingProfile assigned. Defaulting to Recordable-only discovery at 1x sample rate.");
                profile = ScriptableObject.CreateInstance<SceneRecordingProfile>();
            }
            InitializeSidecar();
            BootstrapDiscoveredObjects();
            if (profile.runtimeRescanIntervalSeconds > 0f)
            {
                StartCoroutine(RuntimeRescanLoop());
            }
        }

        private System.Collections.IEnumerator RuntimeRescanLoop()
        {
            var wait = new WaitForSeconds(profile.runtimeRescanIntervalSeconds);
            while (enableRecording)
            {
                yield return wait;
                BootstrapDiscoveredObjects();
            }
        }

        /// <summary>
        /// Researcher-facing helper: drop into your spawn code right after
        /// <c>Instantiate</c> so the spawned object joins the recording
        /// without having to wait for the periodic rescan, and without
        /// needing a tag/layer match.
        /// <code>
        /// var go = Instantiate(prefab, ...);
        /// SceneStateRecorder.MarkRecordable(go);
        /// </code>
        /// Idempotent: a no-op if the GameObject already has a Recordable.
        /// Always returns the (existing or new) Recordable.
        /// </summary>
        public static Recordable MarkRecordable(GameObject go)
        {
            if (go == null) return null;
            var existing = go.GetComponent<Recordable>();
            return existing != null ? existing : go.AddComponent<Recordable>();
        }

        /// <summary>
        /// Like <see cref="MarkRecordable"/>, but seeds the UniqueId with a
        /// deterministic string. Same seed always produces the same id,
        /// across Unity sessions and across machines, so a recording's
        /// scene-state sidecar will line up against a replay scene that
        /// uses the same seed scheme.
        /// <para>
        /// Use case: runtime-spawned gaze targets in a procedural scene.
        /// Without seeded ids, fresh GUIDs are minted every session and
        /// SceneStateReplayer falls through to placeholders for everything.
        /// </para>
        /// <para>
        /// Seed conventions: short, stable, unique-per-spawn-slot. Examples:
        /// <c>"Static_Near_0"</c>, <c>"Dynamic_3"</c>,
        /// <c>"CountingCube_phase=Counting_index=5"</c>. NEVER include
        /// values that change run-to-run (timestamps, instance IDs, random
        /// numbers).
        /// </para>
        /// IMPORTANT: must be called BEFORE the GameObject becomes active
        /// (i.e. immediately after AddComponent on a still-deactivated
        /// GameObject, or before SetActive(true)). After OnEnable runs,
        /// the registry has already keyed off the random GUID.
        /// </summary>
        public static Recordable MarkRecordableSeeded(GameObject go, string seed)
        {
            if (go == null) return null;
            // Bracket the AddComponent in deactivate/reactivate so the
            // seeded id is the one that gets keyed into RecordableRegistry.
            // On an active GameObject, OnEnable fires synchronously and
            // EnsureUniqueId would mint a random GUID before SetUniqueId
            // could overwrite it, leaving a stale registry key.
            bool wasActive = go.activeSelf;
            if (wasActive) go.SetActive(false);

            var rec = go.GetComponent<Recordable>();
            if (rec == null) rec = go.AddComponent<Recordable>();
            if (!string.IsNullOrEmpty(seed)) rec.SetUniqueId(seed);

            if (wasActive) go.SetActive(true);
            return rec;
        }

        // --- Initialization ---

        private void InitializeSidecar()
        {
            // Match SessionRecorder's lazy-write pattern: open the file now,
            // write the header at first valid sample (or coord-origin lands).
            if (string.IsNullOrEmpty(sessionRecorder.CsvFilePath))
            {
                Debug.LogWarning("[SceneStateRecorder] SessionRecorder.CsvFilePath is empty — main recorder may not be initialized yet. Sidecar deferred.");
                return;
            }
            sidecarPath = Path.ChangeExtension(sessionRecorder.CsvFilePath, null) + "_SceneState.csv";
            try
            {
                sidecarWriter = new StreamWriter(sidecarPath, append: false);
                csvWriter = new SceneStateCSVWriter(sidecarWriter, profile.recordParentId);
                openedAt = Time.realtimeSinceStartup;
                pendingRows.Clear();
                initialized = true;
                Debug.Log($"[SceneStateRecorder] Sidecar initialized: {sidecarPath} (header deferred)");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SceneStateRecorder] Failed to open sidecar at {sidecarPath}: {e.Message}");
                initialized = false;
            }
        }

        /// <summary>
        /// Walk the scene at Start and seed any non-Recordable GameObjects
        /// that match the discovery rules with a runtime-only Recordable so
        /// they get GUIDs and participate in the registry. Recordables that
        /// already exist are picked up via OnEnable → registry callback.
        /// </summary>
        private void BootstrapDiscoveredObjects()
        {
            if (profile.discovery == SceneRecordingProfile.DiscoveryMode.RecordableComponentOnly)
                return; // OnEnable in Recordable does the registration.

            if (profile.discovery == SceneRecordingProfile.DiscoveryMode.ExplicitListOnly)
            {
                if (profile.explicitObjects == null) return;
                foreach (var go in profile.explicitObjects)
                {
                    if (go == null) continue;
                    if (go.GetComponent<Recordable>() == null) go.AddComponent<Recordable>();
                }
                return;
            }

            // OrTag / OrLayer: scan the scene for matches and attach Recordable.
            var all = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var go = all[i].gameObject;
                if (go.GetComponent<Recordable>() != null) continue;
                if (profile.ShouldRecord(go, null))
                {
                    go.AddComponent<Recordable>();
                }
            }
        }

        // --- Per-frame collection ---

        private void LateUpdate()
        {
            if (!initialized) return;

            int frame = sessionRecorder != null ? sessionRecorder.FrameNumber : 0;
            if (frame == lastSampledFrame) return; // dedupe within the same Unity frame
            int divider = Mathf.Max(1, profile.sampleEveryNthFrame);
            if (frame % divider != 0) return;
            lastSampledFrame = frame;

            float t = Time.time;
            Vector3 origin = (hmdCollector != null && hmdCollector.HasTrialStartPosition)
                ? hmdCollector.CurrentTrialStartPosition
                : Vector3.zero;
            bool originSet = hmdCollector != null && hmdCollector.HasTrialStartPosition;

            foreach (var rec in RecordableRegistry.All())
            {
                if (rec == null) continue;
                if (schemaLocked && !lockedIds.Contains(rec.UniqueId)) continue; // late-spawn handled separately
                if (profile != null && profile.discovery != SceneRecordingProfile.DiscoveryMode.ExplicitListOnly &&
                    !profile.ShouldRecord(rec.gameObject, rec))
                    continue;

                var tr = rec.transform;
                Vector3 pos = originSet ? tr.position - origin : tr.position;

                var row = new SceneStateRow
                {
                    Frame = frame,
                    T = t,
                    ObjectId = rec.UniqueId,
                    Position = pos,
                    Rotation = tr.rotation,
                    Active = rec.gameObject.activeInHierarchy,
                    ParentId = profile.recordParentId ? ResolveParentId(rec) : string.Empty,
                };

                if (!headerWritten)
                {
                    // Buffer until the header lands. Header writes either when
                    // SessionRecorder fires OnHeaderWritten OR when the grace
                    // window expires.
                    if (Time.realtimeSinceStartup - openedAt < coordinateOriginGraceSeconds)
                    {
                        pendingRows.Enqueue(row);
                        continue;
                    }
                    // Grace expired: write header world-space + flush buffered rows.
                    WriteHeaderInternal(originSet, origin);
                }

                csvWriter.WriteRow(row);
            }

            if (sessionRecorder != null && sessionRecorder.FrameNumber % 60 == 0)
            {
                csvWriter?.Flush();
            }
        }

        private string ResolveParentId(Recordable rec)
        {
            if (rec == null || rec.transform.parent == null) return string.Empty;
            var parentRec = rec.transform.parent.GetComponentInParent<Recordable>();
            return parentRec != null ? parentRec.UniqueId : string.Empty;
        }

        // --- Lifecycle hooks ---

        private void HandleSessionHeaderWritten()
        {
            if (!initialized || headerWritten) return;
            // Use the now-known origin from HMDDataCollector — same source
            // SessionRecorder used for its own header.
            Vector3 origin = (hmdCollector != null && hmdCollector.HasTrialStartPosition)
                ? hmdCollector.CurrentTrialStartPosition
                : Vector3.zero;
            bool originSet = hmdCollector != null && hmdCollector.HasTrialStartPosition;
            WriteHeaderInternal(originSet, origin);
        }

        private void HandleCoordinateOriginSet(Vector3 origin)
        {
            // No-op: the SessionRecorder.OnHeaderWritten handler is
            // authoritative for header timing. Subscription kept to register intent.
        }

        private void WriteHeaderInternal(bool originSet, Vector3 origin)
        {
            if (headerWritten) return;
            string profileName = profile != null ? profile.name : null;
            string sessionId = sessionRecorder != null ? sessionRecorder.SessionId : "unknown";
            csvWriter.WriteHeader(sessionId, origin, originSet, profile != null ? profile.sampleEveryNthFrame : 1, profileName);
            // Re-normalize buffered rows now that the origin is known.
            while (pendingRows.Count > 0)
            {
                var r = pendingRows.Dequeue();
                if (originSet) r.Position -= origin;
                csvWriter.WriteRow(r);
            }
            headerWritten = true;

            // Schema-lock: the set of ids we've seen so far is the recorded
            // schema. Late additions still record but emit a SceneEvents row.
            lockedIds.Clear();
            foreach (var rec in RecordableRegistry.All())
            {
                if (rec != null && profile.ShouldRecord(rec.gameObject, rec))
                    lockedIds.Add(rec.UniqueId);
            }
            schemaLocked = true;
            Debug.Log($"[SceneStateRecorder] Sidecar header written; schema locked at {lockedIds.Count} objects.");
        }

        private void HandleRegistryChanged(RecordableRegistry.RegistryEvent ev, string id, Recordable rec)
        {
            if (!initialized) return;
            // Internal lifecycle events route through the same recorder
            // researchers use for their own events. Reserved type names:
            // "Spawn", "Despawn", "IdRegenerated".
            string objectName = rec != null ? rec.gameObject.name : string.Empty;
            switch (ev)
            {
                case RecordableRegistry.RegistryEvent.Enabled:
                    if (schemaLocked && !lockedIds.Contains(id))
                    {
                        // Late spawn — record the event AND admit to the live set
                        // so per-frame collection picks it up. Schema lock is
                        // about the FILE'S column set; long-format sidecars don't
                        // have per-id columns, so admitting late ids is safe.
                        lockedIds.Add(id);
                        SceneEventRecorder.Record("Spawn", id, objectName);
                    }
                    break;
                case RecordableRegistry.RegistryEvent.Disabled:
                    if (schemaLocked)
                        SceneEventRecorder.Record("Despawn", id, objectName);
                    break;
                case RecordableRegistry.RegistryEvent.IdCollisionRegenerated:
                    SceneEventRecorder.Record("IdRegenerated", id, objectName);
                    break;
            }
        }

        private void OnApplicationQuit() => CloseSidecar();
        private void OnDestroy()
        {
            UnsubscribeAll();
            CloseSidecar();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                csvWriter?.Flush();
            }
        }

        private void CloseSidecar()
        {
            try
            {
                if (sidecarWriter != null)
                {
                    csvWriter?.Flush();
                    sidecarWriter.Close();
                    sidecarWriter.Dispose();
                    sidecarWriter = null;
                }
            }
            catch (System.Exception e) { Debug.LogError($"[SceneStateRecorder] CloseSidecar error: {e.Message}"); }
        }
    }
}
