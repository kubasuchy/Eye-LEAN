using UnityEngine;

namespace EyeTracking.Components
{
    /// <summary>
    /// Researcher-friendly opt-in that calls
    /// <see cref="HMDDataCollector.SetCoordinateOrigin()"/> at scene start so
    /// the main CSV's <c>HeadPos_*</c> / <c>*Origin_*</c> columns and the
    /// scene-state sidecar's <c>Pos_*</c> columns are normalized to the
    /// trial-start camera position. Drop this on the rig instead of relying
    /// on EnvironmentGenerator, which only ships with the demo. Configurable
    /// delay so it lands AFTER the camera rig has resolved its world-space
    /// position (OpenXR rigs need ~1 frame for the XR Origin to apply
    /// tracking-space transforms).
    /// </summary>
    public class CoordinateOriginInitializer : MonoBehaviour
    {
        [Tooltip("Optional override. If null we use the sibling HMDDataCollector or find one in the scene.")]
        [SerializeField] private HMDDataCollector hmdCollector;

        [Tooltip("Frames to wait before calling SetCoordinateOrigin so the camera rig has settled. 1-2 is plenty for OpenXR.")]
        [Range(0, 10)]
        [SerializeField] private int delayFrames = 1;

        [Tooltip("Set the origin again whenever this scene is reloaded. Off by default — origin is a once-per-session anchor.")]
        [SerializeField] private bool setOnEveryEnable = false;

        private bool didSetThisSession;

        private void Awake()
        {
            if (hmdCollector == null) hmdCollector = GetComponent<HMDDataCollector>() ?? FindFirstObjectByType<HMDDataCollector>();
        }

        private void OnEnable()
        {
            if (didSetThisSession && !setOnEveryEnable) return;
            StartCoroutine(SetOriginAfterDelay());
        }

        private System.Collections.IEnumerator SetOriginAfterDelay()
        {
            for (int i = 0; i < delayFrames; i++) yield return null;
            if (hmdCollector == null)
            {
                Debug.LogError("[CoordinateOriginInitializer] No HMDDataCollector available — origin not set.");
                yield break;
            }
            if (hmdCollector.SetCoordinateOrigin())
                didSetThisSession = true;
        }
    }
}
