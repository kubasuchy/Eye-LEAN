using System;

namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Global flag indicating that the Replay system is currently driving
    /// the scene. Researcher experiment scripts should early-out on this
    /// so they don't fight the replayer.
    /// Set by <c>ReplayController</c> on transitions into
    /// <c>Playing/Paused/Loading</c>; cleared on
    /// <c>Ready/Complete/Uninitialized</c> and on destroy.
    /// </summary>
    public static class ReplayMode
    {
        public static bool IsActive { get; private set; }
        public static event Action<bool> Changed;

        public static void Begin()
        {
            if (IsActive) return;
            IsActive = true;
            Changed?.Invoke(true);
        }

        public static void End()
        {
            if (!IsActive) return;
            IsActive = false;
            Changed?.Invoke(false);
        }

        /// <summary>Test seam — clears state and listeners between cases.</summary>
        internal static void ResetForTests()
        {
            IsActive = false;
            Changed = null;
        }
    }
}
