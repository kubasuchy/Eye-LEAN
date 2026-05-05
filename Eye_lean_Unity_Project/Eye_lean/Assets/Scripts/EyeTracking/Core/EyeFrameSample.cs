using UnityEngine;

namespace EyeTracking.Core
{
    /// <summary>
    /// Per-frame snapshot of eye-tracking data, produced by
    /// <see cref="Components.EyeTracker"/>.SampleSnapshot() and consumed by
    /// <see cref="Components.SessionRecorder"/> in LateUpdate. Pass by
    /// <c>in</c> to avoid the ~120-byte copy.
    ///
    /// This struct is intentionally NOT pushed down into <see cref="IEyeTracker"/>
    /// — providers (VIVE / Varjo / HoloLens) keep their existing per-channel
    /// `out`-parameter shape so the multi-vendor abstraction stays intact.
    /// The snapshot is built inside EyeTracker from individual IEyeTracker
    /// calls, then handed off to SessionRecorder as a frozen view.
    /// </summary>
    public readonly struct EyeFrameSample
    {
        public readonly bool HasLeftValid;
        public readonly Vector3 LeftOrigin;
        public readonly Vector3 LeftDirection;
        public readonly float LeftOpenness;
        public readonly float LeftPupilDiameter;
        public readonly Vector2 LeftPupilPosition;

        public readonly bool HasRightValid;
        public readonly Vector3 RightOrigin;
        public readonly Vector3 RightDirection;
        public readonly float RightOpenness;
        public readonly float RightPupilDiameter;
        public readonly Vector2 RightPupilPosition;

        public readonly bool HasCombinedValid;
        public readonly Vector3 CombinedOrigin;
        public readonly Vector3 CombinedDirection;

        public readonly bool HasValidVergence;
        public readonly Vector3 VergencePoint;
        public readonly float VergenceQuality;

        public readonly string GazedObjectName;

        public EyeFrameSample(
            bool hasLeftValid, Vector3 leftOrigin, Vector3 leftDirection,
            float leftOpenness, float leftPupilDiameter, Vector2 leftPupilPosition,
            bool hasRightValid, Vector3 rightOrigin, Vector3 rightDirection,
            float rightOpenness, float rightPupilDiameter, Vector2 rightPupilPosition,
            bool hasCombinedValid, Vector3 combinedOrigin, Vector3 combinedDirection,
            bool hasValidVergence, Vector3 vergencePoint, float vergenceQuality,
            string gazedObjectName)
        {
            HasLeftValid = hasLeftValid;
            LeftOrigin = leftOrigin;
            LeftDirection = leftDirection;
            LeftOpenness = leftOpenness;
            LeftPupilDiameter = leftPupilDiameter;
            LeftPupilPosition = leftPupilPosition;

            HasRightValid = hasRightValid;
            RightOrigin = rightOrigin;
            RightDirection = rightDirection;
            RightOpenness = rightOpenness;
            RightPupilDiameter = rightPupilDiameter;
            RightPupilPosition = rightPupilPosition;

            HasCombinedValid = hasCombinedValid;
            CombinedOrigin = combinedOrigin;
            CombinedDirection = combinedDirection;

            HasValidVergence = hasValidVergence;
            VergencePoint = vergencePoint;
            VergenceQuality = vergenceQuality;

            GazedObjectName = gazedObjectName;
        }

        public static readonly EyeFrameSample Invalid = new EyeFrameSample(
            false, Vector3.zero, Vector3.forward, 0f, 0f, Vector2.zero,
            false, Vector3.zero, Vector3.forward, 0f, 0f, Vector2.zero,
            false, Vector3.zero, Vector3.forward,
            false, Vector3.zero, 0f,
            string.Empty);
    }
}
