// unity-component/Runtime/HumanObstacleTracker.cs
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    /// <summary>Auto-finds the XR camera (Camera.main or tagged "MainCamera")
    /// and exposes its position to the SceneAdapter as a dynamic obstacle so
    /// NPCs avoid the participant.</summary>
    public class HumanObstacleTracker : MonoBehaviour
    {
        [Tooltip("Footprint radius used when treating the human as a dynamic " +
                 "obstacle. Default 0.3 m approximates a person's shoulder width.")]
        public float footprintRadiusM = 0.3f;

        [Tooltip("Camera that represents the participant's head. If null, " +
                 "Camera.main is used.")]
        public Camera headCamera;

        public TrackedObject AsTrackedObject()
        {
            var cam = headCamera != null ? headCamera : Camera.main;
            return new TrackedObject
            {
                objectId = "P_human",
                transform = cam != null ? cam.transform : null,
                footprintRadiusM = footprintRadiusM,
            };
        }
    }
}
