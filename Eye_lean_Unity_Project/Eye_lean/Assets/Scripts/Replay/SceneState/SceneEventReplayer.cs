using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Plays back the events sidecar (<c>&lt;prefix&gt;_SceneEvents.csv</c>)
    /// produced by <c>SceneEventRecorder</c>. Researchers register a handler
    /// per event type, typically from <c>OnEnable</c>:
    /// <c>SceneEventReplayer.RegisterHandler("ShowInstruction", row =&gt; ...)</c>.
    /// Multiple handlers per event type fire in registration order. If
    /// playback skips frames (interpolation or seek), events whose frame
    /// number falls between the previously-fired frame and the current
    /// frame still fire — no event is silently dropped.
    /// </summary>
    public class SceneEventReplayer : MonoBehaviour
    {
        // Auto-bootstrap: on every scene load, attach a sibling
        // SceneEventReplayer to any ReplayController that lacks one, so
        // deterministic replay always picks up the events sidecar
        // (RandomStateSnapshot, ConfigSnapshot, etc.).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            EnsureSiblingOnReplayControllers();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => EnsureSiblingOnReplayControllers();
        }

        private static void EnsureSiblingOnReplayControllers()
        {
            var controllers = FindObjectsByType<ReplayController>(FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                var rc = controllers[i];
                if (rc == null) continue;
                if (rc.GetComponent<SceneEventReplayer>() == null)
                {
                    rc.gameObject.AddComponent<SceneEventReplayer>();
                    Debug.Log($"[SceneEventReplayer] Auto-attached to '{rc.name}' (sibling of ReplayController).");
                }
            }
        }

        [Header("References (auto-found if null)")]
        [SerializeField] private ReplayController controller;

        [Header("Sidecar")]
        [Tooltip("Override sidecar path. Empty = derive from ReplayController.dataFilePath by appending '_SceneEvents.csv'.")]
        [SerializeField] private string sidecarPathOverride = "";

        [Header("Debug")]
        [Tooltip("Print one line per fired event to logcat.")]
        [SerializeField] private bool debugLogEvents = false;

        [Tooltip("Print one summary line on load.")]
        [SerializeField] private bool debugMode = false;

        private SceneEventTimeline timeline;
        private string resolvedSidecarPath;
        private int lastFiredFrame = -1;

        // Handler dispatch table. Keyed by event type string. Multiple
        // handlers per type allowed; fired in registration order.
        private static readonly Dictionary<string, List<Action<SceneEventRow>>> _handlers
            = new Dictionary<string, List<Action<SceneEventRow>>>(StringComparer.Ordinal);

        // Diagnostic
        public int LoadedEventCount => timeline != null ? timeline.TotalEventCount : 0;
        public int LoadedFrameCount => timeline != null ? timeline.FrameCount : 0;
        public string SidecarPath => resolvedSidecarPath;

        // --- Static handler API ---

        /// <summary>
        /// Register a callback for a recorded event type. Fires whenever
        /// playback crosses a frame containing a row with this EventType.
        /// </summary>
        public static void RegisterHandler(string eventType, Action<SceneEventRow> handler)
        {
            if (string.IsNullOrEmpty(eventType) || handler == null) return;
            if (!_handlers.TryGetValue(eventType, out var list))
            {
                list = new List<Action<SceneEventRow>>(2);
                _handlers[eventType] = list;
            }
            list.Add(handler);
        }

        /// <summary>Remove a previously-registered handler. Idempotent.</summary>
        public static void UnregisterHandler(string eventType, Action<SceneEventRow> handler)
        {
            if (string.IsNullOrEmpty(eventType) || handler == null) return;
            if (_handlers.TryGetValue(eventType, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0) _handlers.Remove(eventType);
            }
        }

        /// <summary>Drop every handler. Useful for tests / scene reloads.</summary>
        public static void ClearHandlers() => _handlers.Clear();

        /// <summary>Snapshot of currently-registered event types (for diagnostics).</summary>
        public static IReadOnlyCollection<string> RegisteredEventTypes
        {
            get
            {
                var arr = new string[_handlers.Count];
                int i = 0;
                foreach (var k in _handlers.Keys) arr[i++] = k;
                return arr;
            }
        }

        // --- Lifecycle ---

        private void Awake()
        {
            if (controller == null) controller = GetComponent<ReplayController>() ?? FindFirstObjectByType<ReplayController>();
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.OnLoadComplete += HandleLoadComplete;
            controller.OnFrameDisplayed += HandleFrameDisplayed;
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnLoadComplete -= HandleLoadComplete;
                controller.OnFrameDisplayed -= HandleFrameDisplayed;
            }
            timeline = null;
            lastFiredFrame = -1;
        }

        private void HandleLoadComplete(EyeLean.Replay.ReplaySession session)
        {
            string mainPath = !string.IsNullOrEmpty(sidecarPathOverride)
                ? sidecarPathOverride
                : DeriveSidecarPath(controller != null ? controller.dataFilePath : null);
            resolvedSidecarPath = mainPath;
            lastFiredFrame = -1;

            if (string.IsNullOrEmpty(mainPath))
            {
                if (debugMode) Debug.Log("[SceneEventReplayer] No sidecar path configured; skipping event replay.");
                timeline = null;
                return;
            }
            if (!File.Exists(mainPath))
            {
                Debug.LogWarning($"[SceneEventReplayer] Events sidecar resolved to '{mainPath}' but file does not exist; skipping event replay.");
                timeline = null;
                return;
            }

            var parser = new SceneEventCSVParser { DebugMode = debugMode };
            timeline = parser.ParseFile(mainPath);
            if (timeline == null)
            {
                Debug.LogWarning($"[SceneEventReplayer] Failed to parse events sidecar at '{mainPath}'.");
                return;
            }

            // Always log the load summary — main signal that event replay
            // is wired up. Lists registered handler types so type-string
            // typos surface quickly.
            string handlerTypes = _handlers.Count == 0 ? "<none registered>" : string.Join(",", RegisteredEventTypes);
            Debug.Log($"[SceneEventReplayer] Loaded {timeline.TotalEventCount} events across {timeline.FrameCount} frames from {mainPath}. Registered handlers: [{handlerTypes}]");

            // Apply the recorded RandomStateSnapshot eagerly, before any
            // phase coroutine starts running. This event is emitted once
            // at session header-write; scan all frames for it rather than
            // relying on per-frame dispatch.
            if (TryFindRandomStateSnapshot(out string rngSnapshot))
            {
                if (EyeLean.SceneState.SceneEventRecorder.TryDecodeAndApplyRandomState(rngSnapshot))
                {
                    Debug.Log("[SceneEventReplayer] Applied recorded UnityEngine.Random.state snapshot.");
                }
            }
        }

        private bool TryFindRandomStateSnapshot(out string encoded)
        {
            encoded = null;
            if (timeline == null || timeline.ByFrame == null) return false;
            foreach (var bucket in timeline.ByFrame.Values)
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    if (bucket[i].EventType == "RandomStateSnapshot")
                    {
                        encoded = bucket[i].Detail;
                        return !string.IsNullOrEmpty(encoded);
                    }
                }
            }
            return false;
        }

        private void HandleFrameDisplayed(EyeLean.Replay.ReplayFrame frame)
        {
            if (timeline == null || frame == null) return;
            int curFrame = frame.frameNumber;

            // First call: just establish the baseline. Don't fire historical
            // events that happened before playback started.
            if (lastFiredFrame < 0)
            {
                FireFrame(curFrame);
                lastFiredFrame = curFrame;
                return;
            }

            if (curFrame == lastFiredFrame) return;

            if (curFrame > lastFiredFrame)
            {
                // Forward play — fire any frames crossed since the last call.
                for (int f = lastFiredFrame + 1; f <= curFrame; f++)
                {
                    FireFrame(f);
                }
            }
            else
            {
                // Backward seek — drop the lastFiredFrame anchor without
                // re-firing past events.
                if (debugLogEvents) Debug.Log($"[SceneEventReplayer] Seek backward to frame {curFrame}; not re-firing events.");
            }

            lastFiredFrame = curFrame;
        }

        private void FireFrame(int frame)
        {
            if (!timeline.ByFrame.TryGetValue(frame, out var rows)) return;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (debugLogEvents)
                {
                    Debug.Log($"[SceneEventReplayer] {row.EventType} | id='{row.ObjectId}' | detail='{row.Detail}'");
                }
                if (_handlers.TryGetValue(row.EventType, out var list))
                {
                    for (int h = 0; h < list.Count; h++)
                    {
                        try { list[h](row); }
                        catch (Exception e)
                        {
                            Debug.LogError($"[SceneEventReplayer] Handler for '{row.EventType}' threw: {e.Message}\n{e.StackTrace}");
                        }
                    }
                }
            }
        }

        public static string DeriveSidecarPath(string mainCsvPath)
        {
            if (string.IsNullOrEmpty(mainCsvPath)) return string.Empty;
            return Path.ChangeExtension(mainCsvPath, null) + "_SceneEvents.csv";
        }
    }
}
