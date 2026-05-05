using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EyeTracking.Components;
using UnityEngine;

namespace EyeLean.SceneState
{
    /// <summary>
    /// Researcher-facing event recorder. Writes a sidecar
    /// <c>&lt;prefix&gt;_SceneEvents.csv</c> alongside the main eye-tracking
    /// CSV with one row per event:
    /// <code>Frame,T,EventType,ObjectId,Detail</code>
    /// <c>EventType</c> is free-form; researchers pair names with handlers
    /// registered via <c>SceneEventReplayer.RegisterHandler</c>. The
    /// recording stack reserves <c>Spawn</c>, <c>Despawn</c>, and
    /// <c>IdRegenerated</c> for internal use. Static <see cref="Record"/>
    /// calls become no-ops if no component is in the scene. The file opens
    /// lazily after <see cref="SessionRecorder.OnHeaderWritten"/>; events
    /// emitted earlier are buffered and flushed in order. Sessions with no
    /// events leave no file.
    /// </summary>
    [RequireComponent(typeof(SessionRecorder))]
    public sealed class SceneEventRecorder : MonoBehaviour
    {
        // Auto-attach a sibling SceneEventRecorder to every SessionRecorder
        // on scene load. Deterministic replay relies on the RandomStateSnapshot
        // event always being present, so this can't be left to the researcher.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            EnsureSiblingOnSessionRecorders();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => EnsureSiblingOnSessionRecorders();
        }

        private static void EnsureSiblingOnSessionRecorders()
        {
            var recorders = FindObjectsByType<SessionRecorder>(FindObjectsSortMode.None);
            for (int i = 0; i < recorders.Length; i++)
            {
                var sr = recorders[i];
                if (sr == null) continue;
                if (sr.GetComponent<SceneEventRecorder>() == null)
                {
                    sr.gameObject.AddComponent<SceneEventRecorder>();
                    Debug.Log($"[SceneEventRecorder] Auto-attached to '{sr.name}' (sibling of SessionRecorder).");
                }
            }
        }

        [Tooltip("Master switch. When false, Record() is a no-op and no sidecar file is created.")]
        [SerializeField] private bool enableRecording = true;

        [Tooltip("Print one-line debug log on every event recorded. Off in production — high-frequency events would spam logcat.")]
        [SerializeField] private bool debugLogEvents = false;

        private static SceneEventRecorder _instance;

        private SessionRecorder sessionRecorder;
        private string sidecarPath;
        private StreamWriter writer;
        private bool fileOpen;
        private bool headerWritten;
        // Pre-header buffer: any Record() call that fires before the main
        // CSV header lands is stashed here, then flushed in order on
        // OnHeaderWritten so the events sidecar always opens with rows that
        // can join the main CSV by Frame/T.
        private readonly Queue<BufferedEvent> pendingEvents = new Queue<BufferedEvent>();

        private struct BufferedEvent
        {
            public int frame;
            public float t;
            public string type;
            public string objectId;
            public string detail;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[SceneEventRecorder] Multiple instances detected — '{name}' will defer to '{_instance.name}'. Consider removing duplicates.");
                return;
            }
            _instance = this;
            sessionRecorder = GetComponent<SessionRecorder>();
            // Suppress recording during deterministic replay so the live
            // experiment's re-emitted events don't overwrite the original sidecar.
            if (EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                enableRecording = false;
                Debug.Log("[SceneEventRecorder] ReplayMode active — events sidecar suppressed for this session.");
            }
        }

        private void OnEnable()
        {
            if (sessionRecorder != null)
            {
                sessionRecorder.OnHeaderWritten += HandleSessionHeaderWritten;
            }
        }

        private void OnDisable()
        {
            if (sessionRecorder != null)
            {
                sessionRecorder.OnHeaderWritten -= HandleSessionHeaderWritten;
            }
            FlushSafely();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) FlushSafely();
        }

        private void OnApplicationQuit() => CloseFile();

        private void OnDestroy()
        {
            CloseFile();
            if (_instance == this) _instance = null;
        }

        // --- Public API ---

        /// <summary>
        /// Record a single event at the current frame and timestamp.
        /// <paramref name="type"/> is a researcher-defined string paired
        /// with a handler registered via <c>SceneEventReplayer.RegisterHandler</c>.
        /// Use <see cref="RecordKV"/> when the detail payload has structured
        /// fields. Unity main thread only.
        /// </summary>
        public static void Record(string type, string objectId = "", string detail = "")
        {
            if (_instance == null) return; // No recorder in scene → silently drop.
            _instance.RecordInternal(type, objectId, detail);
        }

        /// <summary>
        /// Convenience overload: serialize a small key/value list into the
        /// <c>Detail</c> column as <c>k1=v1;k2=v2</c>. Replay-side handlers
        /// can split on <c>;</c> and <c>=</c>. Avoid commas in values — the
        /// CSV writer replaces them with underscores.
        /// </summary>
        public static void RecordKV(string type, string objectId, params (string key, string val)[] kv)
        {
            if (_instance == null) return;
            string detail = string.Empty;
            if (kv != null && kv.Length > 0)
            {
                var sb = new System.Text.StringBuilder(64);
                for (int i = 0; i < kv.Length; i++)
                {
                    if (i > 0) sb.Append(';');
                    sb.Append(Sanitize(kv[i].key)).Append('=').Append(Sanitize(kv[i].val));
                }
                detail = sb.ToString();
            }
            _instance.RecordInternal(type, objectId, detail);
        }

        /// <summary>True iff a recorder is in the scene and its component is enabled.</summary>
        public static bool IsActive => _instance != null && _instance.enableRecording && _instance.isActiveAndEnabled;

        // --- Deterministic-seed helpers for spawn methods ---

        /// <summary>
        /// Pending seed override consumed by the next <see cref="PickAndInitSeed"/>
        /// call. Replay handlers set this just before invoking a spawn so
        /// the spawn picks the recorded seed instead of a fresh one.
        /// </summary>
        private static int? _pendingSeedOverride;

        /// <summary>
        /// Pick a seed, init <c>UnityEngine.Random</c> with it, and return
        /// the seed for the caller to record alongside its spawn event.
        /// <para>
        /// During recording, picks a fresh seed from the current global
        /// state. During replay, consumes the value previously set via
        /// <see cref="SetPendingSeedOverride"/>. Either way, the global
        /// RNG ends in a deterministic state derived from the returned
        /// seed — recorded scenes regenerate identically on replay.
        /// </para>
        /// </summary>
        public static int PickAndInitSeed()
        {
            int seed;
            if (_pendingSeedOverride.HasValue)
            {
                seed = _pendingSeedOverride.Value;
                _pendingSeedOverride = null;
            }
            else
            {
                seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            }
            UnityEngine.Random.InitState(seed);
            return seed;
        }

        /// <summary>
        /// Set the seed value that the next call to <see cref="PickAndInitSeed"/>
        /// will return + apply. Called by replay-side handlers immediately
        /// before invoking a spawn method that uses RNG.
        /// </summary>
        public static void SetPendingSeedOverride(int seed)
        {
            _pendingSeedOverride = seed;
        }

        // --- JSON-payload helpers (config snapshots, structured rows) ---

        /// <summary>
        /// Record an event whose Detail column holds a Base64-encoded JSON
        /// payload. Use for capturing serialized config structs / arbitrary
        /// objects whose comma-laden JSON would otherwise be mangled by
        /// the CSV sanitizer.
        /// </summary>
        public static void RecordJson(string type, string objectId, object payload)
        {
            if (payload == null) { Record(type, objectId, string.Empty); return; }
            string json = JsonUtility.ToJson(payload);
            string b64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            Record(type, objectId, b64);
        }

        /// <summary>
        /// Decode a payload written by <see cref="RecordJson"/> back into
        /// its original type. Returns the deserialized struct/class, or
        /// <c>default</c> if decoding fails.
        /// </summary>
        public static T DecodeJson<T>(string base64Detail)
        {
            if (string.IsNullOrEmpty(base64Detail)) return default;
            try
            {
                string json = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(base64Detail));
                return JsonUtility.FromJson<T>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SceneEventRecorder] DecodeJson<{typeof(T).Name}> failed: {e.Message}");
                return default;
            }
        }

        // --- Internals ---

        private void RecordInternal(string type, string objectId, string detail)
        {
            if (!enableRecording) return;
            if (string.IsNullOrEmpty(type)) return;

            int frame = sessionRecorder != null ? sessionRecorder.FrameNumber : 0;
            float t = Time.time;

            if (!headerWritten)
            {
                // Buffer until the main CSV header lands — sidecar path is
                // derived from sessionRecorder.CsvFilePath, which is only
                // guaranteed populated by then.
                pendingEvents.Enqueue(new BufferedEvent
                {
                    frame = frame, t = t, type = type, objectId = objectId ?? string.Empty, detail = detail ?? string.Empty,
                });
                return;
            }

            WriteRow(frame, t, type, objectId, detail);
        }

        private void HandleSessionHeaderWritten()
        {
            if (string.IsNullOrEmpty(sessionRecorder?.CsvFilePath)) return;
            sidecarPath = Path.ChangeExtension(sessionRecorder.CsvFilePath, null) + "_SceneEvents.csv";
            headerWritten = true;

            // Snapshot UnityEngine.Random.state at session start. Replay
            // restores it before live phase coroutines run so Random.Range /
            // Random.value calls reproduce the recording's sequence.
            string rngSnapshot = EncodeRandomState(UnityEngine.Random.state);
            if (!string.IsNullOrEmpty(rngSnapshot))
            {
                Record("RandomStateSnapshot", "", rngSnapshot);
            }

            // Drain pre-header buffer (EnsureOpen runs from the first WriteRow).
            while (pendingEvents.Count > 0)
            {
                var ev = pendingEvents.Dequeue();
                WriteRow(ev.frame, ev.t, ev.type, ev.objectId, ev.detail);
            }
        }

        /// <summary>
        /// Encode <c>UnityEngine.Random.State</c> as a fixed-format
        /// "s0;s1;s2;s3" string. Random.State's fields are internal so
        /// JsonUtility doesn't see them; reflection is the cleanest
        /// portable way to read them.
        /// </summary>
        public static string EncodeRandomState(UnityEngine.Random.State state)
        {
            try
            {
                var fields = typeof(UnityEngine.Random.State).GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (fields == null || fields.Length < 4) return string.Empty;
                object boxed = state;
                var sb = new System.Text.StringBuilder(64);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (i > 0) sb.Append(';');
                    sb.Append(System.Convert.ToInt32(fields[i].GetValue(boxed))
                        .ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SceneEventRecorder] EncodeRandomState failed: {e.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Inverse of <see cref="EncodeRandomState"/>. Sets
        /// <c>UnityEngine.Random.state</c> from a recorded snapshot string.
        /// Returns false on parse failure.
        /// </summary>
        public static bool TryDecodeAndApplyRandomState(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return false;
            try
            {
                var parts = encoded.Split(';');
                var fields = typeof(UnityEngine.Random.State).GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (fields == null || fields.Length != parts.Length) return false;
                object boxed = new UnityEngine.Random.State();
                for (int i = 0; i < fields.Length; i++)
                {
                    if (!int.TryParse(parts[i], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int v)) return false;
                    fields[i].SetValue(boxed, v);
                }
                UnityEngine.Random.state = (UnityEngine.Random.State)boxed;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SceneEventRecorder] TryDecodeAndApplyRandomState failed: {e.Message}");
                return false;
            }
        }

        private void WriteRow(int frame, float t, string type, string objectId, string detail)
        {
            EnsureOpen();
            if (writer == null) return;
            try
            {
                writer.WriteLine(string.Concat(
                    frame.ToString(CultureInfo.InvariantCulture), ",",
                    t.ToString("F6", CultureInfo.InvariantCulture), ",",
                    Sanitize(type), ",",
                    Sanitize(objectId ?? string.Empty), ",",
                    Sanitize(detail ?? string.Empty)));
                if (debugLogEvents)
                {
                    Debug.Log($"[SceneEventRecorder] {type} | id='{objectId}' | detail='{detail}'");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SceneEventRecorder] write failed: {e.Message}");
            }
        }

        private void EnsureOpen()
        {
            if (fileOpen) return;
            if (string.IsNullOrEmpty(sidecarPath)) return;
            try
            {
                writer = new StreamWriter(sidecarPath, append: false);
                writer.WriteLine("# Eye_lean Scene Events Sidecar");
                writer.WriteLine("# FileVersion: 1.0");
                if (sessionRecorder != null && !string.IsNullOrEmpty(sessionRecorder.SessionId))
                {
                    writer.WriteLine($"# SessionID: {sessionRecorder.SessionId}");
                }
                writer.WriteLine("Frame,T,EventType,ObjectId,Detail");
                writer.Flush();
                fileOpen = true;
                Debug.Log($"[SceneEventRecorder] Opened {sidecarPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SceneEventRecorder] open failed for {sidecarPath}: {e.Message}");
                writer = null;
                fileOpen = false;
            }
        }

        private void FlushSafely()
        {
            if (writer == null) return;
            try { writer.Flush(); }
            catch (System.Exception e) { Debug.LogError($"[SceneEventRecorder] flush failed: {e.Message}"); }
        }

        private void CloseFile()
        {
            if (writer == null) return;
            try { writer.Flush(); writer.Close(); writer.Dispose(); }
            catch (System.Exception e) { Debug.LogError($"[SceneEventRecorder] close failed: {e.Message}"); }
            writer = null;
            fileOpen = false;
        }

        private static string Sanitize(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty :
            (s.IndexOf(',') >= 0 ? s.Replace(',', '_') : s)
                .Replace('\n', ' ').Replace('\r', ' ');
    }
}
