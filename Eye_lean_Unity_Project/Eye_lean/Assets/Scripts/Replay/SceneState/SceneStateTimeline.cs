using System.Collections.Generic;
using UnityEngine;

namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Parsed scene-state sidecar, indexed for both per-frame application
    /// (<see cref="ByFrame"/>) and resume-from-seek per-object lookup
    /// (<see cref="ByObject"/>, sorted by Frame).
    /// </summary>
    public class SceneStateTimeline
    {
        public Vector3 CoordinateOrigin;
        public bool CoordinateOriginSet;
        public int SampleEveryNthFrame = 1;
        public string ProfileName;
        public string SessionId;

        public Dictionary<int, List<SceneStateKey>> ByFrame = new Dictionary<int, List<SceneStateKey>>();
        public Dictionary<string, List<SceneStateKey>> ByObject = new Dictionary<string, List<SceneStateKey>>();

        public int FrameCount => ByFrame.Count;
        public int ObjectCount => ByObject.Count;
    }

    public struct SceneStateKey
    {
        public int Frame;
        public float T;
        public string ObjectId;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Active;
        public string ParentId;
    }
}
