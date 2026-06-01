// unity-component/Runtime/ProceduralEnvironmentDelegate.cs
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    /// <summary>
    /// Abstract base for procedural environment generators. Concrete impls (e.g.,
    /// wrapping Eye-LEAN's RoomGenerator) override Generate() to produce
    /// declarative primitives from a seed.
    /// </summary>
    public abstract class ProceduralEnvironmentDelegate : ScriptableObject
    {
        /// <summary>Unique ID written into the recorded sidecar for traceability.</summary>
        public abstract string DelegateId { get; }

        /// <summary>Populate the environment's declarative primitives lists
        /// (walls, obstacles, spawnPoints, goals) based on the recorded seed.
        /// Must be deterministic: identical seed → identical output.</summary>
        public abstract void Generate(EnvironmentConfig env, int randomSeed);
    }
}
