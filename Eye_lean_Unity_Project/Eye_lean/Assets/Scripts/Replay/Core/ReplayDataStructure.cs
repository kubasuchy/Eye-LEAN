using UnityEngine;
using System.Collections.Generic;

namespace EyeLean.Replay
{
    /// <summary>
    /// Data structure for a single preprocessed replay frame.
    /// All calculations are done during loading, not during playback.
    /// </summary>
    [System.Serializable]
    public class ReplayFrame
    {
        // Timing
        public float timestamp;
        public float frameDuration;
        public int frameNumber;

        // Session context
        public string participantId;
        public int trialNumber;
        public string phase;
        public string subTask;

        // Head position & orientation
        public Vector3 headPosition;
        public Quaternion headRotation;
        public Vector3 headForward;

        // Combined eye data
        public Vector3 combinedOrigin;
        public Vector3 combinedDirection;
        public bool hasCombinedGaze;

        // Left eye data
        public Vector3 leftEyeOrigin;
        public Vector3 leftEyeDirection;
        public float leftEyeOpenness;
        public float leftPupilDiameter;
        public bool hasLeftEye;

        // Right eye data
        public Vector3 rightEyeOrigin;
        public Vector3 rightEyeDirection;
        public float rightEyeOpenness;
        public float rightPupilDiameter;
        public bool hasRightEye;

        // Vergence data (pre-calculated)
        public Vector3 vergencePoint;
        public float vergenceQuality;
        public bool hasValidVergence;

        // Eye tracking validity
        public bool isTrackingValid;
        public bool isBlinking;

        // Analysis data (computed during preprocessing)
        public float gazeVelocity;           // Degrees per second
        public bool isFixation;              // True if velocity < threshold
        public bool isSaccade;               // True if velocity > threshold
        public float kCoefficient;           // Attention type indicator
    }

    /// <summary>
    /// Container for a complete replay session with all preprocessed frames.
    /// </summary>
    [System.Serializable]
    public class ReplaySession
    {
        // Session metadata
        public string filePath;
        public string participantId;
        public float recordingStartTime;
        public float recordingEndTime;
        public float totalDuration;
        public int totalFrames;
        public float averageFrameRate;

        // Coordinate-frame metadata from CSV `# CoordinateOrigin: x,y,z` /
        // `# CoordinateOriginSet: true|false` header lines. When
        // coordinateOriginSet is true, the CSV's HeadPos_*, *Origin_*, and
        // VergencePoint_* columns are stored as offsets from coordinateOrigin
        // (the trial-start world position); the parser de-normalizes them
        // back to world space before populating ReplayFrame.
        public Vector3 coordinateOrigin;
        public bool coordinateOriginSet;

        // Eye-tracking calibration profile that was active at recording time.
        // Empty / "none" / null all mean "no profile applied during recording".
        // Captured from the `# Profile: <name>` metadata header so analysis
        // and replay can decide whether a post-hoc correction is appropriate.
        public string activeProfileName;

        // Preprocessed frames
        public List<ReplayFrame> frames = new List<ReplayFrame>();

        // Phase information
        public List<PhaseMarker> phaseMarkers = new List<PhaseMarker>();

        // Quality summary
        public float validSamplePercentage;
        public int blinkCount;
        public int trackingLossCount;

        /// <summary>
        /// Get duration in seconds
        /// </summary>
        public float Duration => totalDuration;

        /// <summary>
        /// Get frame at specific index
        /// </summary>
        public ReplayFrame GetFrame(int index)
        {
            if (index < 0 || index >= frames.Count)
                return null;
            return frames[index];
        }

        /// <summary>
        /// Find frame index for a given timestamp
        /// </summary>
        public int FindFrameAtTime(float time)
        {
            if (frames.Count == 0) return -1;

            float targetTime = recordingStartTime + time;

            // Binary search for efficiency
            int left = 0;
            int right = frames.Count - 1;

            while (left < right)
            {
                int mid = (left + right) / 2;
                if (frames[mid].timestamp < targetTime)
                    left = mid + 1;
                else
                    right = mid;
            }

            return left;
        }

        /// <summary>
        /// Get all frames for a specific phase
        /// </summary>
        public List<ReplayFrame> GetFramesForPhase(string phaseName)
        {
            return frames.FindAll(f => f.phase == phaseName);
        }

        /// <summary>
        /// Get unique phases in the recording
        /// </summary>
        public List<string> GetUniquePhases()
        {
            HashSet<string> phases = new HashSet<string>();
            foreach (var frame in frames)
            {
                if (!string.IsNullOrEmpty(frame.phase))
                    phases.Add(frame.phase);
            }
            return new List<string>(phases);
        }
    }

    /// <summary>
    /// Marks the start of a phase in the recording
    /// </summary>
    [System.Serializable]
    public class PhaseMarker
    {
        public string phaseName;
        public int startFrameIndex;
        public int endFrameIndex;
        public float startTime;
        public float endTime;
        public int frameCount;
    }

    /// <summary>
    /// Processing state for the replay controller
    /// </summary>
    public enum ReplayState
    {
        Uninitialized,
        Loading,
        Processing,
        Ready,
        Playing,
        Paused,
        Complete
    }
}
