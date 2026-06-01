// unity-component/Runtime/AgentSkin.cs
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    [CreateAssetMenu(menuName = "Navlab/Agent Skin")]
    public class AgentSkin : ScriptableObject
    {
        [Tooltip("Prefab instantiated for this skin. Rocketbox humanoid, robot " +
                 "mesh, or any primitive. Must have a Transform at root.")]
        public GameObject prefab;

        [Tooltip("Indicative classification for logging/analysis. Does not " +
                 "affect runtime behavior.")]
        public AgentVisualType visualType = AgentVisualType.Humanoid;
    }

    public enum AgentVisualType
    {
        Humanoid,
        Robot,
        Marker,
        Other,
    }
}
