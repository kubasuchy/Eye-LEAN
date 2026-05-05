using UnityEngine;

namespace EyeTracking.Core
{
    /// <summary>
    /// Per-frame snapshot of HMD / head-pose data, produced by
    /// <see cref="Components.HMDDataCollector"/>.SampleSnapshot() and consumed
    /// by <see cref="Components.SessionRecorder"/> in LateUpdate. Pass by
    /// <c>in</c> to avoid the ~80-byte copy.
    ///
    /// Positions reported here are world-space; the SessionRecorder may
    /// normalize to <see cref="TrialStartPosition"/> on the way to CSV
    /// (preserving the established CSV column convention) but the snapshot
    /// itself stays untransformed so that consumers other than the recorder
    /// see consistent world-space data.
    /// </summary>
    public readonly struct HmdFrameSample
    {
        public readonly bool IsValid;
        public readonly Vector3 HeadPosition;
        public readonly Quaternion HeadRotation;
        public readonly Vector3 HeadForward;
        public readonly Vector3 HeadUp;
        public readonly Vector3 HeadRight;
        public readonly float Fps;
        public readonly bool HasTrialStartPosition;
        public readonly Vector3 TrialStartPosition;

        public HmdFrameSample(
            bool isValid,
            Vector3 headPosition,
            Quaternion headRotation,
            Vector3 headForward,
            Vector3 headUp,
            Vector3 headRight,
            float fps,
            bool hasTrialStartPosition,
            Vector3 trialStartPosition)
        {
            IsValid = isValid;
            HeadPosition = headPosition;
            HeadRotation = headRotation;
            HeadForward = headForward;
            HeadUp = headUp;
            HeadRight = headRight;
            Fps = fps;
            HasTrialStartPosition = hasTrialStartPosition;
            TrialStartPosition = trialStartPosition;
        }

        public static readonly HmdFrameSample Invalid = new HmdFrameSample(
            false, Vector3.zero, Quaternion.identity,
            Vector3.forward, Vector3.up, Vector3.right,
            0f, false, Vector3.zero);
    }
}
