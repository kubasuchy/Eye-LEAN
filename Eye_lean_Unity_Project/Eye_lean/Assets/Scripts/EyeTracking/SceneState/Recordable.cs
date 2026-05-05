using UnityEngine;

namespace EyeLean.SceneState
{
    /// <summary>
    /// Opt-in marker component for any GameObject whose pose should be
    /// recorded into the scene-state sidecar and replayed by SceneStateReplayer.
    ///
    /// Identity is a serialized GUID. Three failure modes are handled:
    /// <list type="bullet">
    /// <item>Editor: AddComponent / Reset / Paste-Component-Values produce a
    /// fresh GUID via OnValidate.</item>
    /// <item>Editor: dropping a prefab twice in the same scene gets caught by
    /// the duplicate-id scan in OnValidate (RecordableEditor surfaces the
    /// warning to the inspector).</item>
    /// <item>Runtime: Instantiate-d prefabs all carry the prefab's serialized
    /// id. RecordableRegistry detects collisions on register and asks the new
    /// instance to RegenerateId() — a SceneEventRecorder row records the swap so
    /// post-hoc analysis can untangle which clone got which id.</item>
    /// </list>
    /// Cross-session GUID stability is not guaranteed for runtime-spawned
    /// objects. Each recording is paired with its own sidecar where the same
    /// id is used consistently — the join contract is intra-session only.
    /// </summary>
    public class Recordable : MonoBehaviour
    {
        [SerializeField] private string uniqueId;
        public string UniqueId => uniqueId;

        private void Reset()
        {
            EnsureUniqueId();
        }

        private void OnValidate()
        {
            EnsureUniqueId();
        }

        private void OnEnable()
        {
            EnsureUniqueId();
            RecordableRegistry.Register(this);
        }

        private void OnDisable()
        {
            RecordableRegistry.Unregister(this);
        }

        /// <summary>
        /// Forcibly mint a new GUID. Called by RecordableRegistry when a
        /// runtime collision is detected on Register.
        /// </summary>
        public void RegenerateId()
        {
            uniqueId = System.Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Override the UniqueId with a deterministic value derived from
        /// <paramref name="seed"/>. Used by runtime spawn code that needs
        /// cross-session id stability so a recording's ids match a replay
        /// scene's ids.
        ///
        /// MUST be called BEFORE OnEnable, i.e. on the same frame as
        /// AddComponent — once OnEnable has registered the Recordable, the
        /// id is locked into the registry and changing it would leave a
        /// stale dictionary key.
        /// </summary>
        public void SetUniqueId(string seed)
        {
            if (string.IsNullOrEmpty(seed)) return;
            // MD5 is used purely for stable 128-bit distribution (not security).
            // Same seed yields the same id across editor restarts and machines.
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
                uniqueId = System.BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private void EnsureUniqueId()
        {
            if (string.IsNullOrEmpty(uniqueId))
            {
                uniqueId = System.Guid.NewGuid().ToString("N");
            }
        }
    }
}
