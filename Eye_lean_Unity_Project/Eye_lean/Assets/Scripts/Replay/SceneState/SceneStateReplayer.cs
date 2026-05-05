using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using EyeLean.SceneState;

namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Drives transforms on live <see cref="Recordable"/> instances from a
    /// scene-state sidecar produced by <c>SceneStateRecorder</c>. Subscribes
    /// to <c>ReplayController.OnFrameDisplayed</c> and writes pose +
    /// active-state per frame. Object resolution is by stable id
    /// (<c>Recordable.UniqueId</c>); missing recorded ids are logged once
    /// per id then ignored, extra live objects are left untouched (replay
    /// is an overlay, not a takeover). When interpolation is on and
    /// <c>SampleEveryNthFrame &gt; 1</c>, adjacent keys are blended per
    /// object using the recorded frame number as the t-source.
    /// </summary>
    public class SceneStateReplayer : MonoBehaviour
    {
        [Header("References (auto-found if null)")]
        [SerializeField] private ReplayController controller;

        [Header("Sidecar")]
        [Tooltip("Override sidecar path. Empty = derive from ReplayController.dataFilePath by appending '_SceneState.csv'.")]
        [SerializeField] private string sidecarPathOverride = "";

        [Header("Resolution")]
        [Tooltip("If a recorded id has no live Recordable, fall back to GameObject.Find(name) once. Useful for scenes that haven't been migrated to Recordable yet.")]
        [SerializeField] private bool useGameObjectFindFallback = false;

        [Tooltip("De-normalize (add CoordinateOrigin to recorded positions) before applying. Match this to your scene's coordinate convention; default true.")]
        [SerializeField] private bool denormalizeOnApply = true;

        [Tooltip("Smooth between sidecar samples when sample rate is below the eye-CSV rate.")]
        [SerializeField] private bool interpolateBetweenSamples = true;

        [Header("Placeholder Spawning")]
        [Tooltip("ON: when a recorded id has no live Recordable, instantiate a sphere primitive and drive it from the sidecar. Useful for debugging legacy recordings. OFF (default): missing ids are silently skipped — pair with SceneEventReplayer + researcher handlers so recorded events spawn live Recordables before resolution.")]
        [SerializeField] private bool spawnPlaceholdersForMissingIds = false;

        [Tooltip("Diameter (m) of placeholder primitives. Should approximate the recorded objects' size for visual fidelity.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float placeholderSize = 0.22f;

        [Tooltip("Color of placeholder primitives. Distinct from typical experiment object colors so it's clear they're stand-ins.")]
        [SerializeField] private Color placeholderColor = new Color(0.85f, 0.85f, 0.85f, 1f);

        [Tooltip("Despawn placeholders that have no recorded sample within this many frames of the current playback frame. Catches the despawn boundary in _SceneEvents.csv without parsing it.")]
        [Range(30, 600)]
        [SerializeField] private int placeholderStaleFrameWindow = 90;

        [Header("Debug")]
        [Tooltip("Print parser + match stats to logcat on load.")]
        [SerializeField] private bool debugMode = false;

        private SceneStateTimeline timeline;
        private string resolvedSidecarPath;
        private readonly Dictionary<string, Recordable> liveById = new Dictionary<string, Recordable>();
        private readonly HashSet<string> warnedMissingIds = new HashSet<string>();

        // Placeholder spawning state.
        private readonly Dictionary<string, GameObject> placeholdersById = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, int> placeholderLastAppliedFrame = new Dictionary<string, int>();
        private GameObject placeholderRoot;
        // Shared material instance for placeholders so Renderer.material's
        // implicit clone doesn't leak one material per placeholder.
        private Material placeholderSharedMaterial;
        // Cache the authoritative ReplaySession so de-normalization can
        // fall back to the main CSV's coord origin when the sidecar header
        // doesn't carry one.
        private EyeLean.Replay.ReplaySession cachedSession;
        // Deferred-resolve coroutine handle so it can be cancelled on reload.
        private Coroutine deferredResolveCoroutine;

        // Diagnostic counters for the debug panel.
        public bool TimelineLoaded => timeline != null;
        public string SidecarPath => resolvedSidecarPath;
        public int RecordedObjectCount => timeline != null ? timeline.ObjectCount : 0;
        public int MatchedObjectCount { get; private set; }
        public int MissingObjectCount => warnedMissingIds.Count;
        public int LastFrameAppliedCount { get; private set; }
        public int LastFrameActiveCount { get; private set; }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<ReplayController>() ?? FindFirstObjectByType<ReplayController>();
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.OnLoadComplete += HandleLoadComplete;
                controller.OnFrameDisplayed += HandleFrameDisplayed;
            }
            // Subscribe to RecordableRegistry so Recordables that spawn
            // after the initial resolution scan still get matched to
            // recorded ids — replay-driven controllers may spawn phase
            // objects after ResolveLiveObjects has already run.
            EyeLean.SceneState.RecordableRegistry.Changed += HandleRecordableRegistryChanged;
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnLoadComplete -= HandleLoadComplete;
                controller.OnFrameDisplayed -= HandleFrameDisplayed;
            }
            EyeLean.SceneState.RecordableRegistry.Changed -= HandleRecordableRegistryChanged;
            // Stop pending deferred resolve, clear placeholder state.
            if (deferredResolveCoroutine != null)
            {
                StopCoroutine(deferredResolveCoroutine);
                deferredResolveCoroutine = null;
            }
            ClearPlaceholdersAndState();
        }

        /// <summary>
        /// When a Recordable enables (e.g. a phase-change spawn fires after
        /// the initial resolution pass), add it to liveById so subsequent
        /// ApplyKey calls drive it directly. If a placeholder exists for
        /// this id, destroy it so the live object replaces it cleanly.
        /// </summary>
        private void HandleRecordableRegistryChanged(EyeLean.SceneState.RecordableRegistry.RegistryEvent ev, string id, EyeLean.SceneState.Recordable rec)
        {
            if (string.IsNullOrEmpty(id)) return;
            switch (ev)
            {
                case EyeLean.SceneState.RecordableRegistry.RegistryEvent.Enabled:
                case EyeLean.SceneState.RecordableRegistry.RegistryEvent.IdCollisionRegenerated:
                    if (rec != null)
                    {
                        liveById[id] = rec;
                        if (timeline != null && timeline.ByObject != null && timeline.ByObject.ContainsKey(id))
                        {
                            // May slightly over-count if the same Recordable
                            // re-enables; this stat is a debug aid only.
                            MatchedObjectCount++;
                        }
                        // If a placeholder existed for this id, retire it.
                        if (placeholdersById.TryGetValue(id, out var ph))
                        {
                            if (ph != null) Destroy(ph);
                            placeholdersById.Remove(id);
                            placeholderLastAppliedFrame.Remove(id);
                        }
                    }
                    break;
                case EyeLean.SceneState.RecordableRegistry.RegistryEvent.Disabled:
                    if (liveById.TryGetValue(id, out var existing) && existing == rec)
                    {
                        liveById.Remove(id);
                    }
                    break;
            }
        }

        /// <summary>
        /// Tear down placeholder GameObjects + per-id dictionaries. Called
        /// before each (re)load and on disable so a second load doesn't
        /// inherit ghost placeholders from the first.
        /// </summary>
        private void ClearPlaceholdersAndState()
        {
            foreach (var kv in placeholdersById)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            placeholdersById.Clear();
            placeholderLastAppliedFrame.Clear();
            if (placeholderRoot != null)
            {
                Destroy(placeholderRoot);
                placeholderRoot = null;
            }
            // The shared material outlives placeholders by design (created
            // once, reused across the whole session). Clean up only on
            // OnDestroy so re-loads don't re-allocate it.
        }

        private void OnDestroy()
        {
            if (placeholderSharedMaterial != null)
            {
                Destroy(placeholderSharedMaterial);
                placeholderSharedMaterial = null;
            }
        }

        private void HandleLoadComplete(EyeLean.Replay.ReplaySession session)
        {
            // Reset state from any prior load so re-Load on the same
            // controller doesn't inherit stale ghosts.
            ClearPlaceholdersAndState();

            // Cache the authoritative coord origin from the main session.
            // The sidecar header may have been written before the
            // SetCoordinateOrigin event fired, so its CoordinateOriginSet
            // can be false even when the main CSV's is true; the main
            // session is authoritative.
            cachedSession = session;

            // Derive sidecar path from the ReplayController's loaded file.
            string mainPath = !string.IsNullOrEmpty(sidecarPathOverride)
                ? sidecarPathOverride
                : DeriveSidecarPath(controller != null ? controller.dataFilePath : null);
            resolvedSidecarPath = mainPath;

            // Distinguish unconfigured from configured-but-missing.
            if (string.IsNullOrEmpty(mainPath))
            {
                if (debugMode) Debug.Log("[SceneStateReplayer] No sidecar path configured; skipping object replay.");
                timeline = null;
                return;
            }
            if (!File.Exists(mainPath))
            {
                Debug.LogWarning($"[SceneStateReplayer] Sidecar path resolved to '{mainPath}' but the file does not exist; skipping object replay.");
                timeline = null;
                return;
            }

            var parser = new SceneStateCSVParser { DebugMode = debugMode };
            timeline = parser.ParseFile(mainPath);
            if (timeline == null)
            {
                Debug.LogWarning($"[SceneStateReplayer] Failed to parse sidecar at '{mainPath}'.");
                return;
            }

            // Warn if the sidecar disagrees with the main session about
            // coord origin. Mismatch suggests the recorder wrote the
            // sidecar header before the origin event fired; symptoms
            // (placeholders at world origin or 1.5m too high) are subtle.
            if (session != null && session.coordinateOriginSet && !timeline.CoordinateOriginSet)
            {
                Debug.LogWarning($"[SceneStateReplayer] Sidecar header missing CoordinateOrigin but main session has one set ({session.coordinateOrigin}). Falling back to the main session's origin for de-normalization.");
            }
            else if (session != null && session.coordinateOriginSet && timeline.CoordinateOriginSet
                     && (session.coordinateOrigin - timeline.CoordinateOrigin).sqrMagnitude > 1e-6f)
            {
                Debug.LogWarning($"[SceneStateReplayer] Coord-origin mismatch: sidecar={timeline.CoordinateOrigin} vs main session={session.coordinateOrigin}. Using sidecar's value.");
            }

            // Defer object resolution by one frame so OnLoadComplete
            // subscribers that run after this one (e.g. DemoReplayBootstrapper,
            // which spawns the Recordables to match against) have their work
            // captured. Without the defer, every recorded id routes to the
            // placeholder path on first load.
            if (deferredResolveCoroutine != null) StopCoroutine(deferredResolveCoroutine);
            deferredResolveCoroutine = StartCoroutine(ResolveAfterOneFrame());
        }

        private IEnumerator ResolveAfterOneFrame()
        {
            yield return null;
            ResolveLiveObjects();
            WarnIfRecordablesAreFightingPhysicsOrAnimation();
            deferredResolveCoroutine = null;
        }

        private void ResolveLiveObjects()
        {
            liveById.Clear();
            warnedMissingIds.Clear();
            MatchedObjectCount = 0;

            var allRec = FindObjectsByType<Recordable>(FindObjectsSortMode.None);
            for (int i = 0; i < allRec.Length; i++)
            {
                var rec = allRec[i];
                if (rec == null || string.IsNullOrEmpty(rec.UniqueId)) continue;
                liveById[rec.UniqueId] = rec;
            }

            int missing = 0;
            int extras = liveById.Count;
            foreach (var kv in timeline.ByObject)
            {
                if (liveById.ContainsKey(kv.Key))
                {
                    MatchedObjectCount++;
                    extras--;
                }
                else
                {
                    missing++;
                }
            }

            // Always log the resolution summary — main signal for "did
            // this replay scene have the same Recordables as the recording?"
            Debug.Log($"[SceneStateReplayer] Resolution: matched={MatchedObjectCount}, missing={missing}, extras (live, not recorded)={extras}");
        }

        private void WarnIfRecordablesAreFightingPhysicsOrAnimation()
        {
            foreach (var rec in liveById.Values)
            {
                if (rec == null) continue;
                var rb = rec.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    Debug.LogWarning($"[SceneStateReplayer] Recordable '{rec.gameObject.name}' has a non-kinematic Rigidbody — physics will fight the replayer. Mark kinematic during replay.");
                }
                var anim = rec.GetComponent<Animator>();
                if (anim != null && anim.enabled && anim.runtimeAnimatorController != null)
                {
                    Debug.LogWarning($"[SceneStateReplayer] Recordable '{rec.gameObject.name}' has an active Animator — disable it during replay or its writes will overwrite the replayed pose.");
                }
            }
        }

        private void HandleFrameDisplayed(EyeLean.Replay.ReplayFrame frame)
        {
            if (timeline == null || frame == null) return;

            // Direct frame hit.
            if (timeline.ByFrame.TryGetValue(frame.frameNumber, out var rows))
            {
                ApplyRows(rows);
            }
            else
            {
                // No exact frame entry — apply per-object hold-or-interpolate.
                ApplyHoldOrInterpolate(frame.frameNumber);
            }

            // Despawn placeholders that haven't been refreshed within the
            // stale window — that's how object-despawn boundaries get honored
            // (the recorder stops emitting rows for an id after its OnDisable
            // fires, so its lastAppliedFrame stops advancing).
            if (spawnPlaceholdersForMissingIds) RetirePlaceholdersStaleAt(frame.frameNumber);
        }

        private void ApplyRows(List<SceneStateKey> rows)
        {
            int applied = 0, active = 0;
            for (int i = 0; i < rows.Count; i++) { ApplyKey(rows[i], ref applied, ref active); }
            LastFrameAppliedCount = applied;
            LastFrameActiveCount = active;
        }

        private void ApplyHoldOrInterpolate(int targetFrame)
        {
            int applied = 0, active = 0;
            foreach (var kv in timeline.ByObject)
            {
                var keys = kv.Value;
                if (keys.Count == 0) continue;

                int idx = BinarySearchForFrame(keys, targetFrame);
                if (idx < 0)
                {
                    // No prior key — object hadn't been recorded yet by this frame; skip.
                    continue;
                }
                if (!interpolateBetweenSamples || idx >= keys.Count - 1 || keys[idx].Frame == targetFrame)
                {
                    ApplyKey(keys[idx], ref applied, ref active);
                    continue;
                }

                var a = keys[idx];
                var b = keys[idx + 1];
                if (b.Frame <= a.Frame)
                {
                    ApplyKey(a, ref applied, ref active);
                    continue;
                }
                float t = Mathf.Clamp01((targetFrame - a.Frame) / (float)(b.Frame - a.Frame));
                var blended = new SceneStateKey
                {
                    Frame = targetFrame,
                    T = Mathf.Lerp(a.T, b.T, t),
                    ObjectId = a.ObjectId,
                    Position = Vector3.Lerp(a.Position, b.Position, t),
                    Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t),
                    Active = (t < 0.5f) ? a.Active : b.Active,
                    ParentId = a.ParentId,
                };
                ApplyKey(blended, ref applied, ref active);
            }
            LastFrameAppliedCount = applied;
            LastFrameActiveCount = active;
        }

        private void ApplyKey(SceneStateKey key, ref int applied, ref int active)
        {
            Vector3 worldPos = denormalizeOnApply
                ? key.Position + ResolveCoordinateOrigin()
                : key.Position;

            if (liveById.TryGetValue(key.ObjectId, out var rec) && rec != null)
            {
                rec.transform.SetPositionAndRotation(worldPos, key.Rotation);
                if (rec.gameObject.activeSelf != key.Active) rec.gameObject.SetActive(key.Active);
                applied++;
                if (key.Active) active++;
                return;
            }

            // No live Recordable for this id.
            //
            // Under deterministic replay, NEVER spawn placeholders. The
            // live experiment's coroutines run at recorded cadence and
            // will spawn the matching Recordable within a frame or two of
            // the recorded id appearing in the sidecar. A placeholder
            // spawned in that brief gap flickers visibly before
            // RecordableRegistry.Changed retires it. Researchers who want
            // placeholders for legacy non-deterministic recordings can
            // disable deterministic replay on the controller.
            if (controller != null && controller.deterministicReplay)
            {
                return;
            }
            if (spawnPlaceholdersForMissingIds)
            {
                if (!placeholdersById.TryGetValue(key.ObjectId, out var ph) || ph == null)
                {
                    ph = SpawnPlaceholder(key.ObjectId);
                    placeholdersById[key.ObjectId] = ph;
                }
                if (ph != null)
                {
                    ph.transform.SetPositionAndRotation(worldPos, key.Rotation);
                    if (ph.activeSelf != key.Active) ph.SetActive(key.Active);
                    placeholderLastAppliedFrame[key.ObjectId] = key.Frame;
                    applied++;
                    if (key.Active) active++;
                }
                return;
            }

            if (warnedMissingIds.Add(key.ObjectId))
            {
                if (debugMode) Debug.Log($"[SceneStateReplayer] Recorded id '{key.ObjectId}' has no live Recordable; suppressing further warnings for this id.");
            }
        }

        private GameObject SpawnPlaceholder(string objectId)
        {
            if (placeholderRoot == null)
            {
                placeholderRoot = new GameObject("ReplayPlaceholders");
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Placeholder_{objectId.Substring(0, System.Math.Min(8, objectId.Length))}";
            go.transform.SetParent(placeholderRoot.transform, false);
            go.transform.localScale = Vector3.one * placeholderSize;
            // Drop colliders so placeholders don't interfere with any
            // gaze raycasts.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Single shared material across placeholders — Renderer.material
                // would clone per-instance otherwise.
                if (placeholderSharedMaterial == null)
                {
                    placeholderSharedMaterial = new Material(renderer.sharedMaterial);
                    placeholderSharedMaterial.color = placeholderColor;
                    placeholderSharedMaterial.name = "ReplayPlaceholderShared";
                }
                renderer.sharedMaterial = placeholderSharedMaterial;
            }
            return go;
        }

        // Despawn placeholders that haven't received a sample within the
        // stale-frame window. Scaled by the recorder's SampleEveryNthFrame
        // so a profile with sampleEveryNthFrame=10 doesn't despawn live
        // objects after 9 missed cadences.
        private void RetirePlaceholdersStaleAt(int currentFrame)
        {
            if (placeholdersById.Count == 0) return;
            int effectiveWindow = placeholderStaleFrameWindow
                * (timeline != null ? Mathf.Max(1, timeline.SampleEveryNthFrame) : 1);
            // Collect dead ids first to avoid modifying the dict during iteration.
            List<string> dead = null;
            foreach (var kv in placeholderLastAppliedFrame)
            {
                if (currentFrame - kv.Value > effectiveWindow)
                {
                    if (dead == null) dead = new List<string>();
                    dead.Add(kv.Key);
                }
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++)
            {
                if (placeholdersById.TryGetValue(dead[i], out var go) && go != null) Destroy(go);
                placeholdersById.Remove(dead[i]);
                placeholderLastAppliedFrame.Remove(dead[i]);
            }
        }

        /// <summary>
        /// Choose the coordinate origin to de-normalize against. Prefer
        /// the sidecar's value, but fall back to the main session's value
        /// when the sidecar header was written before the
        /// SetCoordinateOrigin event fired. If neither has one set, return
        /// zero (positions land in normalized space).
        /// </summary>
        private Vector3 ResolveCoordinateOrigin()
        {
            if (timeline != null && timeline.CoordinateOriginSet) return timeline.CoordinateOrigin;
            if (cachedSession != null && cachedSession.coordinateOriginSet) return cachedSession.coordinateOrigin;
            return Vector3.zero;
        }

        private static int BinarySearchForFrame(List<SceneStateKey> sortedKeys, int targetFrame)
        {
            // Returns the index of the largest key whose Frame <= targetFrame,
            // or -1 if targetFrame precedes the first key.
            int lo = 0, hi = sortedKeys.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (sortedKeys[mid].Frame <= targetFrame)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return best;
        }

        public static string DeriveSidecarPath(string mainCsvPath)
        {
            if (string.IsNullOrEmpty(mainCsvPath)) return string.Empty;
            return Path.ChangeExtension(mainCsvPath, null) + "_SceneState.csv";
        }
    }
}
