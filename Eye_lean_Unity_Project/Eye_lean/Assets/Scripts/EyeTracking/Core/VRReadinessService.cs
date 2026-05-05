using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EyeTracking.Core
{
    /// <summary>
    /// Single source of truth for "is the VR rig actually tracking the
    /// user yet". Multiple scenes (MainMenu, calibrator, experiment,
    /// environment generator) need to gate behavior on this and were
    /// each answering it inconsistently — UI rendered at floor level,
    /// SetCoordinateOrigin fired before standing height, status panels
    /// reported no eye tracker because OpenXR hadn't initialized yet.
    ///
    /// Tracking criteria: Camera.main present AND
    ///   <c>camPos.y &gt; MinCameraHeightMeters</c> (filters out
    ///   floor-level readings before height tracking kicks in) AND
    ///   <c>camPos.magnitude &gt; MinCameraOriginEpsilon</c> (filters
    ///   out the identity (0,0,0) reading from before OpenXR pose data
    ///   arrives).
    ///
    /// Persistent (DontDestroyOnLoad). Auto-instantiates BeforeSceneLoad.
    /// Implements <see cref="ISceneTransitionAware"/> so the per-scene
    /// ready state resets cleanly across scene transitions.
    /// </summary>
    public sealed class VRReadinessService : MonoBehaviour, ISceneTransitionAware
    {
        public const float MinCameraHeightMeters = 0.5f;
        public const float MinCameraOriginEpsilon = 0.1f;

        private static VRReadinessService _instance;
        private static bool _quitting;

        private bool _isTracking;
        private Transform _cachedCamera;

        public static VRReadinessService Instance
        {
            get
            {
                if (_quitting) return null;
                if (_instance == null) Bootstrap();
                return _instance;
            }
        }

        /// <summary>
        /// True once the camera has been observed at standing height with a
        /// non-zero in-plane position. Resets to false on each scene change.
        /// </summary>
        public bool IsCameraTracking => _isTracking;

        /// <summary>
        /// Fires once per scene the first time <see cref="IsCameraTracking"/>
        /// transitions to true. Subscribers may unsubscribe inside the handler;
        /// we re-subscribe when the next scene loads.
        /// </summary>
        public event Action OnReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("[VRReadinessService]");
            _instance = go.AddComponent<VRReadinessService>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            SceneTransitionCoordinator.Instance.Register(this);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                if (!_quitting && SceneTransitionCoordinator.Instance != null)
                {
                    SceneTransitionCoordinator.Instance.Unregister(this);
                }
                _instance = null;
            }
        }

        private void OnApplicationQuit() => _quitting = true;

        private void Update()
        {
            if (_isTracking) return;
            if (_cachedCamera == null) _cachedCamera = Camera.main?.transform;
            if (_cachedCamera == null) return;
            Vector3 p = _cachedCamera.position;
            if (p.y > MinCameraHeightMeters && p.magnitude > MinCameraOriginEpsilon)
            {
                _isTracking = true;
                Debug.Log($"[VRReadinessService] Camera ready in scene '{gameObject.scene.name}': pos={p}");
                try { OnReady?.Invoke(); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        public void OnSceneWillUnload(Scene from)
        {
            // Drop cached camera; it's about to be destroyed.
            _cachedCamera = null;
        }

        public void OnSceneDidLoad(Scene to)
        {
            // Reset readiness for the new scene. The next Update will re-resolve.
            _isTracking = false;
            _cachedCamera = null;
        }

        /// <summary>
        /// Coroutine-friendly wait. Yields each frame until
        /// <see cref="IsCameraTracking"/> becomes true or
        /// <paramref name="timeoutSeconds"/> elapses (whichever comes first).
        /// Logs a warning on timeout but completes normally so callers can
        /// proceed with whatever they have. Use the readiness flag to decide
        /// whether to position UI confidently or with degraded fallback.
        /// </summary>
        public IEnumerator WaitForCameraReady(float timeoutSeconds = 5f)
        {
            float started = Time.time;
            while (!_isTracking && (Time.time - started) < timeoutSeconds)
            {
                yield return null;
            }
            if (!_isTracking)
            {
                Debug.LogWarning($"[VRReadinessService] Timed out after {timeoutSeconds:F1}s waiting for VR camera to begin tracking; downstream consumer will proceed with degraded fallback.");
            }
        }
    }
}
