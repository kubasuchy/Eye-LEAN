using UnityEngine;
using EyeTracking.Calibration.UI;

namespace EyeLean.MainMenu
{
    /// <summary>
    /// Smooth-follow head anchor for the Main Menu's CalibrationWorldUI
    /// canvas. CalibrationWorldUI's default world-space placement positions
    /// the canvas once at startup and never moves it; in the empty MainMenu
    /// scene the lack of landmarks makes that placement feel like drift.
    /// This anchor lerps the canvas in LateUpdate toward the user's current
    /// head pose at the configured distance and height — gentle damping for
    /// small movements, quick catch-up on head turns. Instances are added
    /// only by MainMenuSceneSetup; no effect on calibrator or experiment.
    /// </summary>
    public class MenuHeadAnchor : MonoBehaviour
    {
        [Tooltip("Reference to the CalibrationWorldUI whose canvas this anchor follows. Auto-found if null.")]
        [SerializeField] private CalibrationWorldUI worldUI;

        [Tooltip("Target distance in meters between the camera and the canvas.")]
        [SerializeField] private float distance = 2.0f;

        [Tooltip("Vertical offset (m) below eye level so the canvas sits comfortably in the lower-center of the FOV.")]
        [SerializeField] private float verticalOffset = -0.15f;

        [Tooltip("Position smoothing time in seconds. Smaller = snappier follow, larger = more relaxed.")]
        [SerializeField] private float positionSmoothTime = 0.35f;

        [Tooltip("Rotation smoothing time in seconds.")]
        [SerializeField] private float rotationSmoothTime = 0.35f;

        [Tooltip("Distance threshold (m) below which the canvas is considered settled and no movement happens. Prevents micro-jitter.")]
        [SerializeField] private float settleThreshold = 0.02f;

        private Transform cameraTransform;
        private Transform canvasTransform;
        private Vector3 positionVelocity;

        void Start()
        {
            if (worldUI == null)
            {
                worldUI = FindObjectOfType<CalibrationWorldUI>();
            }
            if (worldUI == null)
            {
                Debug.LogError("[MenuHeadAnchor] CalibrationWorldUI not found; head-anchor disabled.");
                enabled = false;
                return;
            }

            cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (cameraTransform == null)
            {
                Debug.LogError("[MenuHeadAnchor] Camera.main not found; head-anchor disabled.");
                enabled = false;
            }
        }

        void LateUpdate()
        {
            if (cameraTransform == null) return;

            if (canvasTransform == null)
            {
                var canvasObj = GameObject.Find("CalibrationCanvas");
                if (canvasObj == null) return;
                canvasTransform = canvasObj.transform;
                // Snap on first frame so the user doesn't see a fly-in.
                canvasTransform.position = ComputeTargetPosition();
                canvasTransform.rotation = ComputeTargetRotation();
                return;
            }

            Vector3 targetPos = ComputeTargetPosition();
            if ((canvasTransform.position - targetPos).sqrMagnitude > settleThreshold * settleThreshold)
            {
                canvasTransform.position = Vector3.SmoothDamp(
                    canvasTransform.position, targetPos, ref positionVelocity, positionSmoothTime);
            }

            Quaternion targetRot = ComputeTargetRotation();
            // Slerp with a per-frame factor derived from rotationSmoothTime
            // (Quaternion.SmoothDamp doesn't exist in UnityEngine).
            float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, rotationSmoothTime));
            canvasTransform.rotation = Quaternion.Slerp(canvasTransform.rotation, targetRot, t);
        }

        private Vector3 ComputeTargetPosition()
        {
            Vector3 forward = cameraTransform.forward;
            // Project to horizontal plane so vertical look doesn't lift or
            // drop the canvas — keeps it at consistent eye-line height.
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            else forward.Normalize();

            Vector3 pos = cameraTransform.position + forward * distance;
            pos.y = cameraTransform.position.y + verticalOffset;
            return pos;
        }

        private Quaternion ComputeTargetRotation()
        {
            Vector3 toCanvas = ComputeTargetPosition() - cameraTransform.position;
            toCanvas.y = 0f;
            if (toCanvas.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(toCanvas);
        }
    }
}
