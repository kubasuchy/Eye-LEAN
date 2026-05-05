using System;
using System.Collections.Generic;
using UnityEngine;

namespace EyeLean.SceneState
{
    /// <summary>
    /// Static registry of live <see cref="Recordable"/> instances keyed by
    /// stable UniqueId. Distinct from <c>ObjectTrackingRegistry</c> in
    /// <c>ResearchDataStructure.cs</c>: that one keys by collision-prone
    /// GameObject.name and only stores collider-bearing objects, and the
    /// per-object Gaze_&lt;name&gt; columns continue to consult it.
    /// </summary>
    public static class RecordableRegistry
    {
        private static readonly Dictionary<string, Recordable> liveById = new Dictionary<string, Recordable>();

        /// <summary>
        /// Fired whenever a Recordable enters or leaves the registry, plus on
        /// id collisions. Subscribers (SceneStateRecorder) use this to write
        /// SceneEvents rows. The string parameter is the resolved id at the
        /// time of the event (post-collision regeneration if applicable).
        /// </summary>
        public static event Action<RegistryEvent, string, Recordable> Changed;

        public enum RegistryEvent { Enabled, Disabled, IdCollisionRegenerated }

        public static IEnumerable<Recordable> All() => liveById.Values;
        public static int Count => liveById.Count;

        public static bool TryGet(string id, out Recordable recordable) =>
            liveById.TryGetValue(id, out recordable);

        /// <summary>
        /// Register a Recordable. If its UniqueId is already taken (typical
        /// cause: <c>Instantiate</c> on a prefab whose serialized id is in
        /// use), force the newcomer to RegenerateId() and log via Changed.
        /// </summary>
        internal static void Register(Recordable r)
        {
            if (r == null || string.IsNullOrEmpty(r.UniqueId)) return;

            if (liveById.TryGetValue(r.UniqueId, out var existing))
            {
                if (existing == r)
                {
                    // Idempotent re-register of the same instance (pool reuse,
                    // OnEnable after a transient OnDisable). Skip the Enabled
                    // event so SceneEvents doesn't get a duplicate Spawn row.
                    return;
                }
                // Different instance with a colliding id — mint a new id on
                // the newcomer and preserve the original registration.
                r.RegenerateId();
                Changed?.Invoke(RegistryEvent.IdCollisionRegenerated, r.UniqueId, r);
            }

            liveById[r.UniqueId] = r;
            Changed?.Invoke(RegistryEvent.Enabled, r.UniqueId, r);
        }

        internal static void Unregister(Recordable r)
        {
            if (r == null || string.IsNullOrEmpty(r.UniqueId)) return;
            if (liveById.TryGetValue(r.UniqueId, out var stored) && stored == r)
            {
                liveById.Remove(r.UniqueId);
                Changed?.Invoke(RegistryEvent.Disabled, r.UniqueId, r);
            }
        }

        /// <summary>
        /// Test seam — clears the registry without invoking Changed callbacks.
        /// Production code should never call this; EditMode tests use it
        /// between cases to avoid bleed.
        /// </summary>
        internal static void ResetForTests()
        {
            liveById.Clear();
            Changed = null;
        }
    }
}
