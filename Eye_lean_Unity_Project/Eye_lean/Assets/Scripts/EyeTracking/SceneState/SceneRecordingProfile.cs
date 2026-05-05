using System.Collections.Generic;
using UnityEngine;

namespace EyeLean.SceneState
{
    /// <summary>
    /// ScriptableObject configuration for <see cref="SceneStateRecorder"/>.
    /// Mirrors the <c>ExperimentMetadataSchema</c> pattern: declare upfront,
    /// reference from the rig, and the recorder picks up the rules at Start.
    /// </summary>
    [CreateAssetMenu(fileName = "SceneRecordingProfile", menuName = "Eye Tracking/Scene Recording Profile")]
    public class SceneRecordingProfile : ScriptableObject
    {
        public enum DiscoveryMode
        {
            /// <summary>Only objects carrying a Recordable component.</summary>
            RecordableComponentOnly,
            /// <summary>Recordables OR any GameObject matching one of <see cref="tags"/>.</summary>
            OrTag,
            /// <summary>Recordables OR any GameObject in <see cref="layerMask"/>.</summary>
            OrLayer,
            /// <summary>Only the GameObjects in <see cref="explicitObjects"/> — Recordables ignored.</summary>
            ExplicitListOnly
        }

        [Tooltip("How the recorder enumerates target objects each scene.")]
        public DiscoveryMode discovery = DiscoveryMode.RecordableComponentOnly;

        [Tooltip("Used when discovery is OrLayer.")]
        public LayerMask layerMask = ~0;

        [Tooltip("Used when discovery is OrTag.")]
        public string[] tags;

        [Tooltip("Used when discovery is ExplicitListOnly. Objects without a Recordable get one auto-attached at Start.")]
        public List<GameObject> explicitObjects = new List<GameObject>();

        [Tooltip("GameObject names to skip even if they match the discovery filter (e.g. static walls/floor that don't move).")]
        public string[] excludedNames;

        [Tooltip("Record transforms every Nth frame. 1 = every frame at the eye-CSV rate (~90Hz). 3 ≈ 30Hz, halves sidecar size.")]
        [Range(1, 10)]
        public int sampleEveryNthFrame = 1;

        [Tooltip("Include a parent_id column (Recordable parent's UniqueId) in the sidecar.")]
        public bool recordParentId = false;

        [Tooltip("Seconds between automatic re-scans for newly-spawned objects. 0 = scan only at Start. Recommended: 1.0 for runtime-heavy scenes with dynamic targets.")]
        [Range(0f, 5f)]
        public float runtimeRescanIntervalSeconds = 0f;

        /// <summary>
        /// Decide whether a candidate GameObject should be recorded under the
        /// current discovery mode. Used by <see cref="SceneStateRecorder"/> at
        /// startup and on late-spawn registration.
        /// </summary>
        public bool ShouldRecord(GameObject go, Recordable rec)
        {
            if (go == null) return false;
            if (excludedNames != null)
            {
                for (int i = 0; i < excludedNames.Length; i++)
                {
                    if (go.name == excludedNames[i]) return false;
                }
            }

            switch (discovery)
            {
                case DiscoveryMode.RecordableComponentOnly:
                    return rec != null;
                case DiscoveryMode.OrTag:
                    if (rec != null) return true;
                    if (tags != null)
                    {
                        for (int i = 0; i < tags.Length; i++)
                            if (!string.IsNullOrEmpty(tags[i]) && go.CompareTag(tags[i])) return true;
                    }
                    return false;
                case DiscoveryMode.OrLayer:
                    if (rec != null) return true;
                    return ((1 << go.layer) & layerMask.value) != 0;
                case DiscoveryMode.ExplicitListOnly:
                    return explicitObjects != null && explicitObjects.Contains(go);
                default:
                    return false;
            }
        }
    }
}
