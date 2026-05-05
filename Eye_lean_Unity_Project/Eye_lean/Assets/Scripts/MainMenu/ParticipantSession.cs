using UnityEngine;

namespace EyeLean.MainMenu
{
    /// <summary>
    /// Cross-scene participant identity. Set once at the Main Menu (or
    /// auto-generated on first access) and read by the calibrator and
    /// experiment scenes so a single research session produces
    /// consistently-tagged CSVs across multiple scene loads. Static state
    /// survives <c>SceneManager.LoadScene</c> calls.
    /// </summary>
    public static class ParticipantSession
    {
        private static string _id;

        /// <summary>
        /// The participant ID for the current research session. First read
        /// auto-generates `P_yyyyMMdd_HHmmss` if not yet set. Setters from
        /// scene-specific UI take precedence.
        /// </summary>
        public static string Id
        {
            get
            {
                if (string.IsNullOrEmpty(_id))
                {
                    _id = $"P_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                    Debug.Log($"[ParticipantSession] Auto-generated participant ID: {_id}");
                }
                return _id;
            }
            set
            {
                _id = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                Debug.Log($"[ParticipantSession] Participant ID set: {_id ?? "<auto on next read>"}");
            }
        }

        /// <summary>Reset the participant ID so the next read auto-generates a fresh one.</summary>
        public static void Clear() => _id = null;
    }
}
