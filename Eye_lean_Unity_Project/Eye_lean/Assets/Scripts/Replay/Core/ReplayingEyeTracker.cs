using UnityEngine;
using EyeTracking.Core;

namespace EyeLean.Replay
{
    /// <summary>
    /// <see cref="IEyeTracker"/> implementation backed by a
    /// <see cref="ReplayController"/>'s currently-displayed
    /// <see cref="ReplayFrame"/>. During deterministic replay the controller
    /// installs an instance of this class as the active tracker via
    /// <see cref="EyeTrackerFactory.SetReplayOverride"/>, so the live
    /// experiment's gaze-driven gameplay consumes recorded gaze and
    /// reproduces the participant's behavior exactly.
    /// </summary>
    public sealed class ReplayingEyeTracker : IEyeTracker
    {
        private readonly ReplayController controller;
        private bool diagnosedFirstSample;

        public ReplayingEyeTracker(ReplayController controller)
        {
            this.controller = controller;
        }

        private ReplayFrame Frame => controller != null ? controller.CurrentFrame : null;

        /// <summary>
        /// One-shot diagnostic: the first time a consumer asks for an eye
        /// sample, log what's being served. Helps debug "EyeTracker says
        /// null" when the override is installed but downstream code doesn't
        /// see valid data.
        /// </summary>
        private void DiagnoseFirstSample(ReplayFrame f, string caller)
        {
            if (diagnosedFirstSample || f == null) return;
            diagnosedFirstSample = true;
            Debug.Log(
                $"[ReplayingEyeTracker] first sample via {caller}: " +
                $"hasCombined={f.hasCombinedGaze} combinedOrigin={f.combinedOrigin} combinedDir={f.combinedDirection} | " +
                $"hasLeft={f.hasLeftEye} leftOrigin={f.leftEyeOrigin} leftDir={f.leftEyeDirection} | " +
                $"hasRight={f.hasRightEye} rightOrigin={f.rightEyeOrigin} rightDir={f.rightEyeDirection}");
        }

        public bool IsAvailable => Frame != null;
        public string DeviceName => "Replaying (recorded)";
        public float SamplingRateHz =>
            controller != null && controller.Session != null && controller.Session.averageFrameRate > 0f
                ? controller.Session.averageFrameRate
                : 90f;

        public bool GetCombinedGazeOrigin(out Vector3 origin)
        {
            var f = Frame;
            if (f == null || !f.hasCombinedGaze) { origin = Vector3.zero; return false; }
            origin = f.combinedOrigin;
            return true;
        }

        public bool GetCombinedGazeDirection(out Vector3 direction)
        {
            var f = Frame;
            if (f == null || !f.hasCombinedGaze) { direction = Vector3.forward; return false; }
            direction = f.combinedDirection;
            return true;
        }

        public bool GetLeftEyeOrigin(out Vector3 origin)
        {
            var f = Frame;
            DiagnoseFirstSample(f, nameof(GetLeftEyeOrigin));
            if (f == null || !f.hasLeftEye) { origin = Vector3.zero; return false; }
            origin = f.leftEyeOrigin;
            return true;
        }

        public bool GetLeftEyeDirection(out Vector3 direction)
        {
            var f = Frame;
            if (f == null || !f.hasLeftEye) { direction = Vector3.forward; return false; }
            direction = f.leftEyeDirection;
            return true;
        }

        public bool GetLeftEyeOpenness(out float openness)
        {
            var f = Frame;
            if (f == null || !f.hasLeftEye) { openness = 0f; return false; }
            openness = f.leftEyeOpenness;
            return true;
        }

        public bool GetLeftPupilDiameter(out float diameterMm)
        {
            var f = Frame;
            if (f == null || !f.hasLeftEye) { diameterMm = 0f; return false; }
            diameterMm = f.leftPupilDiameter;
            return true;
        }

        public bool GetLeftPupilPosition(out Vector2 position)
        {
            // Pupil-position columns aren't recorded in the main CSV.
            position = Vector2.zero;
            return false;
        }

        public bool GetRightEyeOrigin(out Vector3 origin)
        {
            var f = Frame;
            if (f == null || !f.hasRightEye) { origin = Vector3.zero; return false; }
            origin = f.rightEyeOrigin;
            return true;
        }

        public bool GetRightEyeDirection(out Vector3 direction)
        {
            var f = Frame;
            if (f == null || !f.hasRightEye) { direction = Vector3.forward; return false; }
            direction = f.rightEyeDirection;
            return true;
        }

        public bool GetRightEyeOpenness(out float openness)
        {
            var f = Frame;
            if (f == null || !f.hasRightEye) { openness = 0f; return false; }
            openness = f.rightEyeOpenness;
            return true;
        }

        public bool GetRightPupilDiameter(out float diameterMm)
        {
            var f = Frame;
            if (f == null || !f.hasRightEye) { diameterMm = 0f; return false; }
            diameterMm = f.rightPupilDiameter;
            return true;
        }

        public bool GetRightPupilPosition(out Vector2 position)
        {
            position = Vector2.zero;
            return false;
        }
    }
}
