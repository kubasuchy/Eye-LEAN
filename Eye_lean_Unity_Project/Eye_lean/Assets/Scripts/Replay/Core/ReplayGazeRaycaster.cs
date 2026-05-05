using UnityEngine;

namespace EyeLean.Replay
{
    /// <summary>
    /// Drives <see cref="GazeTarget"/> notifications globally during
    /// deterministic replay by raycasting recorded gaze against the live
    /// scene each frame. The live experiment's gaze-driven gameplay
    /// consumes <c>GazeTarget.IsBeingGazedAt</c>, which is normally set by
    /// the live <c>EyeTracker</c> MonoBehaviour. In editor replay that
    /// MonoBehaviour is often dormant, so this component takes its place.
    /// Auto-attaches to any <see cref="ReplayController"/> at scene load.
    /// </summary>
    public sealed class ReplayGazeRaycaster : MonoBehaviour
    {
        [Tooltip("Maximum raycast distance for gaze hit detection.")]
        [SerializeField] private float maxRaycastDistance = 20f;

        [Tooltip("Print one line per gaze-target transition to logcat.")]
        [SerializeField] private bool debugLogTransitions = false;

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
                if (rc.GetComponent<ReplayGazeRaycaster>() == null)
                {
                    rc.gameObject.AddComponent<ReplayGazeRaycaster>();
                    Debug.Log($"[ReplayGazeRaycaster] Auto-attached to '{rc.name}' (sibling of ReplayController).");
                }
            }
        }

        private GazeTarget currentGazeTarget; // last frame's hit, for enter/exit edge detection

        private void LateUpdate()
        {
            // Only run during replay, and only when the controller has data
            // loaded (otherwise the override may not yet be installed).
            if (!EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                ClearGazeTarget();
                return;
            }

            var tracker = EyeTracking.Core.EyeTrackerFactory.GetEyeTracker();
            if (tracker == null || !tracker.IsAvailable
                || !tracker.GetCombinedGazeOrigin(out Vector3 origin)
                || !tracker.GetCombinedGazeDirection(out Vector3 direction))
            {
                ClearGazeTarget();
                return;
            }

            if (Physics.Raycast(origin, direction, out RaycastHit hit, maxRaycastDistance, -1))
            {
                if (hit.collider == null || hit.collider.isTrigger)
                {
                    ClearGazeTarget();
                    return;
                }
                var target = hit.collider.gameObject.GetComponent<GazeTarget>();
                if (target == null)
                {
                    // Hit something but not a gaze target — exit any prior.
                    ClearGazeTarget();
                    return;
                }
                if (target == currentGazeTarget)
                {
                    return; // continuous gaze — no-op
                }
                // Transition: exit the previous target, enter the new one.
                if (currentGazeTarget != null)
                {
                    currentGazeTarget.OnGazeExit();
                }
                currentGazeTarget = target;
                target.OnGazeEnter(hit.point, hit.distance);
                if (debugLogTransitions) Debug.Log($"[ReplayGazeRaycaster] Gaze entered: {target.gameObject.name}");
            }
            else
            {
                ClearGazeTarget();
            }
        }

        private void ClearGazeTarget()
        {
            if (currentGazeTarget != null)
            {
                if (debugLogTransitions) Debug.Log($"[ReplayGazeRaycaster] Gaze exited: {currentGazeTarget.gameObject.name}");
                currentGazeTarget.OnGazeExit();
                currentGazeTarget = null;
            }
        }

        private void OnDisable()
        {
            ClearGazeTarget();
        }
    }
}
