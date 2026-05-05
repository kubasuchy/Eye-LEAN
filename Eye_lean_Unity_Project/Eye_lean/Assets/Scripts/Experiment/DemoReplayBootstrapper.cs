using UnityEngine;
using EyeLean.Replay;

namespace EyeLean.Experiment
{
    /// <summary>
    /// Sets the recorded coordinate origin + first-frame head yaw on the
    /// scene's <see cref="EnvironmentGenerator"/> before its own
    /// <c>InitializeWithDelay</c> coroutine reads them. Recorded eye-direction
    /// vectors are in hardware-world-space, so the replay scene's environment
    /// must sit in the same frame; otherwise recorded gaze rays don't intersect
    /// the freshly-spawned targets. Drop on the same GameObject as
    /// <see cref="ReplayController"/>.
    /// </summary>
    [RequireComponent(typeof(ReplayController))]
    public class DemoReplayBootstrapper : MonoBehaviour
    {
        [Tooltip("Optional override. If null we find one in the scene on Start.")]
        [SerializeField] private EnvironmentGenerator environmentGenerator;

        [Tooltip("Anchor the EnvironmentGenerator transform to the recording's coord-origin + first-frame head yaw before generating the room. ON: replay environment lines up with recorded gaze directions. OFF: room generates against editor camera (use only when troubleshooting).")]
        [SerializeField] private bool anchorToRecording = true;

        [Tooltip("Editor convenience: call ReplayController.Play() once the environment is bootstrapped. Without this the replay sits at State=Ready and waits for the user to press Space.")]
        [SerializeField] private bool autoPlayAfterBootstrap = true;

        private ReplayController controller;

        private void Awake()
        {
            controller = GetComponent<ReplayController>();
            if (environmentGenerator == null) environmentGenerator = FindFirstObjectByType<EnvironmentGenerator>();
        }

        private void OnEnable()
        {
            if (controller != null) controller.OnLoadComplete += HandleLoadComplete;
        }

        private void OnDisable()
        {
            if (controller != null) controller.OnLoadComplete -= HandleLoadComplete;
        }

        private void HandleLoadComplete(ReplaySession session)
        {
            if (environmentGenerator == null)
            {
                Debug.LogWarning("[DemoReplayBootstrapper] No EnvironmentGenerator in scene — nothing to anchor.");
                return;
            }

            if (anchorToRecording && session != null)
            {
                AnchorEnvironmentToRecording(session);
            }

            // Re-generate the basic room AFTER anchoring. The auto-Start
            // coroutine in EnvironmentGenerator already built the room against
            // the default spawn (0,0,0,yaw=0) before HandleLoadComplete fires.
            // Without this rebuild, the room stays mis-anchored and recorded
            // gaze rays don't intersect the live scene's geometry.
            // GenerateBasicRoom calls ClearTestObjects first so the rebuild is
            // clean.
            environmentGenerator.GenerateBasicRoom();
            Debug.Log($"[DemoReplayBootstrapper] Room rebuilt at recorded anchor (anchored={anchorToRecording}). Live SampleExperimentController will handle gaze-target spawns.");

            if (autoPlayAfterBootstrap && controller != null)
            {
                controller.Play();
                Debug.Log("[DemoReplayBootstrapper] Auto-started playback (autoPlayAfterBootstrap=true).");
            }
        }

        private void AnchorEnvironmentToRecording(ReplaySession session)
        {
            // Position: use the recorded CoordinateOrigin (the trial-start
            // head position in hardware world space). Falls back to the first
            // frame's headPosition if the origin wasn't set in the recording.
            Vector3 anchorPos = session.coordinateOriginSet
                ? session.coordinateOrigin
                : (session.frames != null && session.frames.Count > 0
                    ? session.frames[0].headPosition
                    : Vector3.zero);

            // Yaw: project the first frame's head forward onto the horizontal
            // plane and take atan2. Matches EnvironmentGenerator's own yaw
            // computation so record/replay agree.
            float anchorYaw = 0f;
            if (session.frames != null && session.frames.Count > 0)
            {
                Quaternion headRot = session.frames[0].headRotation;
                Vector3 headForward = headRot * Vector3.forward;
                headForward.y = 0f;
                if (headForward.sqrMagnitude > 1e-4f)
                {
                    headForward.Normalize();
                    anchorYaw = Mathf.Atan2(headForward.x, headForward.z) * Mathf.Rad2Deg;
                }
            }

            // GenerateBasicRoom reads the generator's internal user-spawn
            // fields, not its own transform — drive them via the override seam.
            environmentGenerator.SetUserSpawnOverride(anchorPos, anchorYaw);

            Debug.Log($"[DemoReplayBootstrapper] Anchored env to recording: pos={anchorPos}, yaw={anchorYaw:F2}° (originSet={session.coordinateOriginSet}).");
        }
    }
}
