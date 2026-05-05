using UnityEngine;

namespace EyeLean.Replay
{
    /// <summary>
    /// Pins <c>Time.captureDeltaTime</c> to the recorded per-frame delta
    /// during deterministic replay. With this active, every coroutine that
    /// uses <c>WaitForSeconds</c>, <c>Time.deltaTime</c>, or <c>Time.time</c>
    /// advances on the recording's clock, so the live experiment's phase
    /// coroutines re-execute at the cadence the participant experienced.
    /// <see cref="ReplayController"/> calls <see cref="BeginCapture"/> on
    /// entering Playing, <see cref="ApplyFrameDelta"/> each frame after
    /// advancing playback, and <see cref="EndCapture"/> on Stop / Complete /
    /// Pause; disabled outside replay so the live experiment runs at native
    /// frame rate.
    /// </summary>
    public static class ReplayClock
    {
        private static bool capturing;
        private static float originalCaptureDeltaTime;

        /// <summary>True iff the replay clock is currently driving Unity time.</summary>
        public static bool IsCapturing => capturing;

        public static void BeginCapture()
        {
            if (capturing) return;
            originalCaptureDeltaTime = Time.captureDeltaTime;
            capturing = true;
        }

        /// <summary>
        /// Set Unity's frame delta to the recorded value. Clamped to
        /// <c>[0.001, 0.5]</c> so a corrupt CSV row can't freeze or
        /// catastrophically advance the clock.
        /// </summary>
        public static void ApplyFrameDelta(float recordedDelta)
        {
            if (!capturing) return;
            if (float.IsNaN(recordedDelta) || float.IsInfinity(recordedDelta) || recordedDelta <= 0f)
            {
                return; // Leave previous value in place.
            }
            Time.captureDeltaTime = Mathf.Clamp(recordedDelta, 0.001f, 0.5f);
        }

        public static void EndCapture()
        {
            if (!capturing) return;
            Time.captureDeltaTime = originalCaptureDeltaTime;
            capturing = false;
        }
    }
}
