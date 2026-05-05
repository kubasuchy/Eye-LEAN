using UnityEngine;
using UnityEngine.SceneManagement;
using EyeTracking.Core;

namespace EyeTracking.Components
{
    /// <summary>
    /// Per-frame head-pose sampler. Owns the camera reference, the
    /// trial-start coordinate origin (used by SessionRecorder for the
    /// `# CoordinateOrigin` CSV header and the per-row position
    /// normalization), and FPS tracking. Exposes
    /// <see cref="SampleSnapshot"/> for SessionRecorder's LateUpdate to
    /// pull. Per-scene MonoBehaviour — do not mark DontDestroyOnLoad.
    /// Implements <see cref="ISceneTransitionAware"/> so the camera ref
    /// invalidates cleanly if this component somehow outlives a scene
    /// change.
    /// </summary>
    public class HMDDataCollector : MonoBehaviour, ISceneTransitionAware
    {
        [Header("Camera")]
        [Tooltip("Optional override; if null we resolve from Camera.main on Start and re-resolve lazily if it ever goes Unity-null.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Coordinate Origin")]
        [Tooltip("If true, snapshots include the trial-start origin set via SetCoordinateOrigin so SessionRecorder can normalize positions for analysis. Default true.")]
        [SerializeField] private bool reportTrialStartPosition = true;

        // Coord-origin state (canonical source — SessionRecorder reads via SampleSnapshot).
        private Vector3 _trialStartWorldPosition = Vector3.zero;
        private bool _hasTrialStartPosition = false;

        // FPS tracking
        private float _smoothedFps;

        public bool HasTrialStartPosition => _hasTrialStartPosition;
        public Vector3 CurrentTrialStartPosition => _trialStartWorldPosition;
        public Transform CameraTransform => GetCameraTransformOrResolve();

        /// <summary>
        /// Fires whenever <see cref="SetCoordinateOrigin()"/> or the
        /// explicit-position overload successfully sets the trial-start
        /// origin. SceneStateRecorder subscribes to flush its grace-window
        /// pending-rows in lockstep with SessionRecorder. Fires only on the
        /// success branch — never from <see cref="ResetCoordinateOrigin"/>.
        /// </summary>
        public event System.Action<Vector3> OnCoordinateOriginSet;

        private void Start()
        {
            ResolveCameraTransformIfNeeded();
        }

        private void Awake()
        {
            Debug.Log($"[HMDDataCollector] Awake on '{gameObject.name}' (instance {GetInstanceID()})");
            // Self-register with the scene-transition coordinator. Defensive
            // — handles the edge case where someone marks this
            // DontDestroyOnLoad upstream and the camera dies on scene change.
            if (SceneTransitionCoordinator.Instance != null)
            {
                SceneTransitionCoordinator.Instance.Register(this);
            }
        }

        private void OnDestroy()
        {
            if (SceneTransitionCoordinator.Instance != null)
            {
                SceneTransitionCoordinator.Instance.Unregister(this);
            }
        }

        public void OnSceneWillUnload(Scene from)
        {
            // Drop the cached camera if it belonged to the unloading scene.
            cameraTransform = null;
        }

        public void OnSceneDidLoad(Scene to)
        {
            // Re-resolved lazily by GetCameraTransformOrResolve.
        }

        private void Update()
        {
            // Smoothed FPS via single-pole low-pass over instantaneous frame rate.
            float instantFps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            _smoothedFps = _smoothedFps <= 0f ? instantFps : Mathf.Lerp(_smoothedFps, instantFps, 0.1f);
        }

        /// <summary>
        /// Re-resolve cameraTransform if it's null (real or Unity-overloaded).
        /// The cached transform from Start can become Unity-null before a
        /// deferred SetCoordinateOrigin call fires; any public method that
        /// touches the camera must go through this resolver.
        /// </summary>
        private Transform GetCameraTransformOrResolve()
        {
            if (!IsTransformAlive(cameraTransform))
            {
                cameraTransform = Camera.main != null ? Camera.main.transform : null;
            }
            return cameraTransform;
        }

        private void ResolveCameraTransformIfNeeded()
        {
            if (!IsTransformAlive(cameraTransform))
            {
                cameraTransform = Camera.main != null ? Camera.main.transform : transform;
                Debug.Log($"[HMDDataCollector] Camera resolved to: {cameraTransform.name}");
            }
        }

        private static bool IsTransformAlive(Transform t)
        {
            return (object)t != null && t != null;
        }

        /// <summary>
        /// Set the trial-start origin to the current camera position. Used
        /// by EnvironmentGenerator at scene start so that all subsequent
        /// CSV rows record positions relative to (0,0,0) — the origin
        /// being the user's position when the trial began. Re-resolves
        /// Camera.main if the cached cameraTransform went Unity-null
        /// since Start. Returns true if the origin was set, false if no
        /// camera was available even after re-resolution.
        /// </summary>
        public bool SetCoordinateOrigin()
        {
            var t = GetCameraTransformOrResolve();
            if (!IsTransformAlive(t))
            {
                Debug.LogError("[HMDDataCollector] SetCoordinateOrigin: no live camera available. Origin NOT set.");
                return false;
            }
            _trialStartWorldPosition = t.position;
            _hasTrialStartPosition = true;
            Debug.Log($"[HMDDataCollector] Coordinate origin set: {_trialStartWorldPosition} (positions will be relative to this point)");
            OnCoordinateOriginSet?.Invoke(_trialStartWorldPosition);
            return true;
        }

        public void SetCoordinateOrigin(Vector3 worldPosition)
        {
            _trialStartWorldPosition = worldPosition;
            _hasTrialStartPosition = true;
            Debug.Log($"[HMDDataCollector] Coordinate origin set: {_trialStartWorldPosition}");
            OnCoordinateOriginSet?.Invoke(_trialStartWorldPosition);
        }

        public void ResetCoordinateOrigin()
        {
            _hasTrialStartPosition = false;
            _trialStartWorldPosition = Vector3.zero;
            Debug.Log("[HMDDataCollector] Coordinate origin reset (positions will be in world coordinates)");
        }

        /// <summary>
        /// Build the per-frame HMD snapshot. Called by SessionRecorder in
        /// LateUpdate so the head pose reflects this frame's final
        /// transform, not the start-of-frame value.
        /// </summary>
        public HmdFrameSample SampleSnapshot()
        {
            var t = GetCameraTransformOrResolve();
            if (!IsTransformAlive(t))
            {
                return HmdFrameSample.Invalid;
            }
            Vector3 trialOrigin = reportTrialStartPosition && _hasTrialStartPosition
                ? _trialStartWorldPosition
                : Vector3.zero;
            bool hasTrialOrigin = reportTrialStartPosition && _hasTrialStartPosition;
            return new HmdFrameSample(
                isValid: true,
                headPosition: t.position,
                headRotation: t.rotation,
                headForward: t.forward,
                headUp: t.up,
                headRight: t.right,
                fps: _smoothedFps,
                hasTrialStartPosition: hasTrialOrigin,
                trialStartPosition: trialOrigin);
        }
    }
}
