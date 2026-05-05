using UnityEngine;

namespace EyeLean.SceneState
{
    /// <summary>
    /// One scene-state sidecar row. Value type so the buffered-sample queue
    /// can re-normalize positions in place when the coord-origin lands after
    /// frame collection (matches <c>SessionRecorder</c>'s grace-window flow
    /// at <c>SessionRecorder.cs:305</c>).
    /// </summary>
    public struct SceneStateRow
    {
        public int Frame;
        public float T;
        public string ObjectId;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Active;
        public string ParentId; // empty unless profile.recordParentId
    }
}
