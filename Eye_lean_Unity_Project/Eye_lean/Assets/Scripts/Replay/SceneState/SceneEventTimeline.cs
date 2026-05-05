using System.Collections.Generic;

namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Parsed events sidecar (<c>&lt;prefix&gt;_SceneEvents.csv</c>),
    /// indexed for per-frame dispatch.
    /// </summary>
    public class SceneEventTimeline
    {
        public string SessionId;

        /// <summary>One bucket per recorded frame, in insertion order.</summary>
        public Dictionary<int, List<SceneEventRow>> ByFrame = new Dictionary<int, List<SceneEventRow>>();

        public int TotalEventCount { get; set; }
        public int FrameCount => ByFrame.Count;
    }

    /// <summary>
    /// One recorded event. Mirrors the on-disk schema: <c>Frame, T, EventType,
    /// ObjectId, Detail</c>. Matches <see cref="EyeLean.SceneState.SceneEventRecorder"/>
    /// row format exactly.
    /// </summary>
    public struct SceneEventRow
    {
        public int Frame;
        public float T;
        public string EventType;
        public string ObjectId;
        public string Detail;
    }
}
