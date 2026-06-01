// unity-component/Runtime/EnvironmentConfig.cs
using System.Collections.Generic;
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    [CreateAssetMenu(menuName = "Navlab/Environment Config")]
    public class EnvironmentConfig : ScriptableObject
    {
        [Header("Room bounds (XZ world coords, meters)")]
        public Vector2 boundsMin;
        public Vector2 boundsMax;

        [Header("Static geometry")]
        public List<WallSegment> walls = new List<WallSegment>();
        public List<CircularObstacle> obstacles = new List<CircularObstacle>();

        [Header("Reference points")]
        public List<NamedPoint> spawnPoints = new List<NamedPoint>();
        public List<NamedPoint> goals = new List<NamedPoint>();

        [Header("Procedural override (optional)")]
        [Tooltip("If set, declarative fields are populated by this delegate at " +
                 "experiment start using the recorded random seed.")]
        public ProceduralEnvironmentDelegate proceduralDelegate;

        /// <summary>Resolves the procedural delegate (if any) by populating the
        /// declarative lists in place. Idempotent; safe to call multiple times.</summary>
        public void ResolveProcedural(int randomSeed)
        {
            if (proceduralDelegate != null)
                proceduralDelegate.Generate(this, randomSeed);
        }

        public NamedPoint? FindSpawn(string name)
        {
            foreach (var p in spawnPoints)
                if (p.name == name) return p;
            return null;
        }

        public NamedPoint? FindGoal(string name)
        {
            foreach (var p in goals)
                if (p.name == name) return p;
            return null;
        }
    }
}
