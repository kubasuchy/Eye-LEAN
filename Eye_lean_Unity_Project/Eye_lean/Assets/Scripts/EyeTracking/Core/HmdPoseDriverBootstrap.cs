using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace EyeTracking.Core
{
    /// <summary>
    /// Auto-attach a TrackedPoseDriver (Input System) to Camera.main on
    /// scene load if one isn't already present. Required because the
    /// project's scene assets ship a bare Camera with no driver; without
    /// one, OpenXR doesn't push HMD pose into Camera.main and the camera
    /// sits at its serialized pose. Runtime attachment lets researcher-
    /// added scenes inherit the fix automatically. Bootstraps
    /// BeforeSceneLoad so the first scene's Camera.main gets the driver
    /// before VRReadinessService starts polling.
    /// </summary>
    public sealed class HmdPoseDriverBootstrap : MonoBehaviour, ISceneTransitionAware
    {
        private static HmdPoseDriverBootstrap _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("[HmdPoseDriverBootstrap]");
            _instance = go.AddComponent<HmdPoseDriverBootstrap>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            if (SceneTransitionCoordinator.Instance != null)
            {
                SceneTransitionCoordinator.Instance.Register(this);
            }
            // Also try immediately for the bootstrap-time scene (we may have
            // landed after Camera.main exists but before SceneLoaded fires).
            StartCoroutine(EnsureDriverNextFrame());
        }

        private System.Collections.IEnumerator EnsureDriverNextFrame()
        {
            // One frame yield so Camera.main resolves after scene root objects
            // finish their own Awake.
            yield return null;
            EnsurePoseDriverOnMainCamera();
            LevelCameraRigChain();
        }

        // The project's scene assets sometimes serialize a ViveRig parent
        // transform with a non-identity local rotation. HMD pose composes
        // ON TOP of that rig, so the world frame the user sees ends up
        // permanently tilted relative to gravity.
        //
        // Defensive fix: walk Camera.main's parent chain at every scene
        // load and zero any non-identity local rotation. Do NOT touch
        // local position — some scenes legitimately offset rig height for
        // a seated start, and stripping that would put the camera on the
        // floor. Yaw / pitch / roll on a rig parent are never desirable
        // because the rig is a passive mount under TrackedPoseDriver.
        private static void LevelCameraRigChain()
        {
            var cam = Camera.main;
            if (cam == null) return;
            int correctionsApplied = 0;
            Transform t = cam.transform.parent;
            while (t != null)
            {
                if (t.localRotation != Quaternion.identity)
                {
                    Debug.Log($"[HmdPoseDriverBootstrap] Leveling rig parent '{t.name}': " +
                              $"localRotation was {t.localRotation.eulerAngles:F2}, resetting to identity.");
                    t.localRotation = Quaternion.identity;
                    correctionsApplied++;
                }
                t = t.parent;
            }
            if (correctionsApplied > 0)
            {
                Debug.Log($"[HmdPoseDriverBootstrap] Camera rig chain leveled ({correctionsApplied} parent transform(s) corrected). World up now matches gravity regardless of how the headset was oriented at scene load.");
            }
        }

        public void OnSceneWillUnload(Scene from) { }

        public void OnSceneDidLoad(Scene to)
        {
            // Re-check on every scene load — Camera.main may differ across
            // scenes (MainMenu -> CalibrationScene -> SampleExperiment).
            StartCoroutine(EnsureDriverNextFrame());
        }

        private static void EnsurePoseDriverOnMainCamera()
        {
            // During deterministic replay, do NOT attach a TrackedPoseDriver.
            // The replay system writes Camera.main.position/rotation directly
            // from recorded frames; a live driver would race those writes
            // and snap the camera back to live HMD pose between updates.
            if (EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[HmdPoseDriverBootstrap] Camera.main not found yet; skipping pose-driver attach for this frame.");
                return;
            }
            if (cam.GetComponent<TrackedPoseDriver>() != null)
            {
                // Scene already shipped one — leave it alone.
                return;
            }
            var driver = cam.gameObject.AddComponent<TrackedPoseDriver>();
            // Bind to HMD center-eye pose. The Input System ships an
            // Input Action Asset under com.unity.inputsystem with the
            // standard XR HMD bindings; we wire the driver up to read
            // from the live device directly without needing asset paths.
            var positionAction = new InputAction(name: "HmdPosition", type: InputActionType.Value, binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            var rotationAction = new InputAction(name: "HmdRotation", type: InputActionType.Value, binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            positionAction.Enable();
            rotationAction.Enable();
            driver.positionInput = new InputActionProperty(positionAction);
            driver.rotationInput = new InputActionProperty(rotationAction);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            Debug.Log($"[HmdPoseDriverBootstrap] Auto-attached TrackedPoseDriver to '{cam.name}'. HMD pose now drives Camera.main.");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                if (SceneTransitionCoordinator.Instance != null)
                {
                    SceneTransitionCoordinator.Instance.Unregister(this);
                }
                _instance = null;
            }
        }
    }
}
