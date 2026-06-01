// unity-component/Runtime/Primitives.cs
using System;
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    [Serializable]
    public struct WallSegment
    {
        public Vector2 startXZ;
        public Vector2 endXZ;
        public float thickness;
    }

    [Serializable]
    public struct CircularObstacle
    {
        public Vector2 centerXZ;
        public float radius;
    }

    [Serializable]
    public struct NamedPoint
    {
        public string name;
        public Vector2 positionXZ;
        public float headingDeg;
    }
}
