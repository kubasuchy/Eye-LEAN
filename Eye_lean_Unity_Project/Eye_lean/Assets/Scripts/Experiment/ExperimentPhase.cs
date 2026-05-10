using System;
using UnityEngine;

namespace EyeLean.Experiment
{
    /// <summary>
    /// Phases of the sample experiment for demonstrating eye tracking analysis.
    /// </summary>
    public enum ExperimentPhase
    {
        Idle,
        Instructions,
        CalibrationCheck,
        FreeExploration,
        VisualSearch,
        SmoothPursuit,
        CountingTask,
        ChangeDetection,
        Complete
    }

    /// <summary>
    /// Configuration for the calibration check phase (4x4 crosshair grid).
    /// </summary>
    [Serializable]
    public struct CalibrationCheckConfig
    {
        [Tooltip("Duration to display each fixation target (seconds)")]
        public float targetDuration;

        [Tooltip("Duration of highlight when target activates (seconds)")]
        public float highlightDuration;

        [Tooltip("Distance from viewer to targets (meters)")]
        public float targetDistance;

        [Tooltip("Horizontal spread of the grid (meters)")]
        public float gridWidth;

        [Tooltip("Vertical spread of the grid (meters)")]
        public float gridHeight;

        [Tooltip("Number of grid rows")]
        public int gridRows;

        [Tooltip("Number of grid columns")]
        public int gridColumns;

        [Tooltip("Size of crosshair arms (meters)")]
        public float crosshairSize;

        [Tooltip("Line thickness for crosshairs (meters)")]
        public float lineThickness;

        [Tooltip("Default crosshair color (when not highlighted)")]
        public Color defaultColor;

        [Tooltip("Highlighted crosshair color (when target is active)")]
        public Color highlightColor;

        public static CalibrationCheckConfig Default => new CalibrationCheckConfig
        {
            targetDuration = 2.0f,
            highlightDuration = 0.5f,
            targetDistance = 3.0f,
            gridWidth = 2.4f,
            gridHeight = 1.6f,
            gridRows = 4,
            gridColumns = 4,
            crosshairSize = 0.2f,   // LARGER crosshairs for visibility
            lineThickness = 0.035f, // THICKER lines for better rendering
            defaultColor = Color.white,  // White crosses on black background
            highlightColor = Color.yellow
        };

        /// <summary>
        /// Get the target positions for the calibration grid (4x4 = 16 points).
        /// </summary>
        public Vector3[] GetTargetPositions(float eyeHeight)
        {
            Vector3[] positions = new Vector3[gridRows * gridColumns];

            // Calculate cell spacing (distribute evenly across grid)
            float cellWidth = gridColumns > 1 ? gridWidth / (gridColumns - 1) : 0;
            float cellHeight = gridRows > 1 ? gridHeight / (gridRows - 1) : 0;

            int index = 0;
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridColumns; col++)
                {
                    float x = -gridWidth / 2f + col * cellWidth;
                    float y = eyeHeight + gridHeight / 2f - row * cellHeight;
                    positions[index++] = new Vector3(x, y, targetDistance);
                }
            }
            return positions;
        }
    }

    /// <summary>
    /// Configuration for the free exploration phase.
    /// </summary>
    [Serializable]
    public struct FreeExplorationConfig
    {
        [Tooltip("Duration of free exploration (seconds)")]
        public float duration;

        [Tooltip("Number of static objects to generate")]
        public int staticObjectCount;

        [Tooltip("Number of dynamic (moving) objects")]
        public int dynamicObjectCount;

        public static FreeExplorationConfig Default => new FreeExplorationConfig
        {
            duration = 30.0f,
            staticObjectCount = 12,
            dynamicObjectCount = 4
        };
    }

    /// <summary>
    /// Visual-search difficulty modes (Treisman & Gelade 1980).
    /// ColorPopOut and ShapePopOut are pre-attentive single-feature searches
    /// (target detected in O(1) regardless of set size). Conjunction is
    /// serial — distractors split between the two feature-distractor types
    /// so the target's color+shape combination is unique.
    /// </summary>
    public enum VisualSearchCondition
    {
        ColorPopOut,
        ShapePopOut,
        Conjunction,
    }

    /// <summary>
    /// Per-trial visual-search difficulty spec. A schedule of these makes
    /// each trial vary in condition and set size (researcher-tunable in
    /// the Inspector).
    /// </summary>
    [Serializable]
    public struct VisualSearchTrialSpec
    {
        [Tooltip("Search condition for this trial")]
        public VisualSearchCondition condition;

        [Tooltip("Number of distractors on this trial")]
        public int distractorCount;
    }

    /// <summary>
    /// Configuration for the visual search phase.
    /// </summary>
    [Serializable]
    public struct VisualSearchConfig
    {
        [Tooltip("Number of search trials. Ignored if 'trials' below is non-empty (the schedule's length wins).")]
        public int trialCount;

        [Tooltip("Number of distractor objects per trial. Used as a fallback when no per-trial schedule is set.")]
        public int distractorCount;

        [Tooltip("Per-trial difficulty schedule. When non-empty, drives both trial count and per-trial condition + set size. Empty = uniform conjunction trials at distractorCount.")]
        public VisualSearchTrialSpec[] trials;

        [Tooltip("Maximum time per trial (seconds)")]
        public float maxTrialDuration;

        [Tooltip("Gaze time on target to count as found (seconds)")]
        public float targetAcquisitionTime;

        [Tooltip("Minimum depth for objects (meters)")]
        public float minDepth;

        [Tooltip("Maximum depth for objects (meters)")]
        public float maxDepth;

        [Tooltip("Size of search objects (meters)")]
        public float objectSize;

        public static VisualSearchConfig Default => new VisualSearchConfig
        {
            trialCount = 5,
            // Conjunction search is serial — set size 24 keeps the slope
            // measurable (~50 ms/item on hardware) without exceeding the
            // 20 s per-trial budget.
            distractorCount = 24,
            // Mixed schedule: easy pop-outs interleaved with conjunction
            // trials of growing set size. Pop-outs give the participant
            // quick wins to stay engaged; the conjunction set-size sweep
            // is the meaningful psychometric data.
            trials = new VisualSearchTrialSpec[]
            {
                new VisualSearchTrialSpec { condition = VisualSearchCondition.ColorPopOut, distractorCount = 8 },
                new VisualSearchTrialSpec { condition = VisualSearchCondition.Conjunction, distractorCount = 12 },
                new VisualSearchTrialSpec { condition = VisualSearchCondition.ShapePopOut, distractorCount = 16 },
                new VisualSearchTrialSpec { condition = VisualSearchCondition.Conjunction, distractorCount = 20 },
                new VisualSearchTrialSpec { condition = VisualSearchCondition.Conjunction, distractorCount = 28 },
            },
            maxTrialDuration = 20.0f,
            targetAcquisitionTime = 1.5f,
            minDepth = 2.0f,
            maxDepth = 5.0f,
            objectSize = 0.2f
        };
    }

    /// <summary>
    /// Configuration for the smooth pursuit phase.
    /// </summary>
    [Serializable]
    public struct SmoothPursuitConfig
    {
        [Tooltip("Duration of pursuit task (seconds)")]
        public float duration;

        [Tooltip("Horizontal radius of Figure-8 path (meters)")]
        public float horizontalRadius;

        [Tooltip("Vertical radius of Figure-8 path (meters)")]
        public float verticalRadius;

        [Tooltip("Speed of target (cycles per second)")]
        public float speed;

        [Tooltip("Distance from viewer (meters)")]
        public float distance;

        [Tooltip("Target size (meters)")]
        public float targetSize;

        [Tooltip("Target color (should contrast with room walls)")]
        public Color targetColor;

        public static SmoothPursuitConfig Default => new SmoothPursuitConfig
        {
            duration = 20.0f,
            horizontalRadius = 1.5f,
            verticalRadius = 1.0f,
            speed = 0.1f,           // SLOWER for easier tracking
            distance = 3.0f,
            targetSize = 0.6f,      // LARGER for better visibility
            targetColor = Color.cyan  // Contrasting color
        };
    }

    /// <summary>
    /// Configuration for the counting task phase.
    /// </summary>
    [Serializable]
    public struct CountingTaskConfig
    {
        [Tooltip("Duration of counting task (seconds)")]
        public float duration;

        [Tooltip("Minimum count of target color objects")]
        public int minTargetCount;

        [Tooltip("Maximum count of target color objects")]
        public int maxTargetCount;

        [Tooltip("Minimum count of distractor objects per color")]
        public int minDistractorCount;

        [Tooltip("Maximum count of distractor objects per color")]
        public int maxDistractorCount;

        [Tooltip("Size of counting objects (meters)")]
        public float objectSize;

        [Tooltip("Enable answer input after counting")]
        public bool collectAnswer;

        [Tooltip("Maximum time to input answer (seconds)")]
        public float answerInputTimeout;

        [Tooltip("Answer options range (show actual +/- this value)")]
        public int answerOptionsRange;

        [Tooltip("Dwell time to select an answer (seconds)")]
        public float answerDwellTime;

        public static CountingTaskConfig Default => new CountingTaskConfig
        {
            duration = 10.0f,       // 10 seconds is sufficient for counting
            minTargetCount = 8,
            maxTargetCount = 12,
            minDistractorCount = 8,
            maxDistractorCount = 12,
            objectSize = 0.15f,
            collectAnswer = true,
            answerInputTimeout = 10.0f,
            answerOptionsRange = 4,
            answerDwellTime = 1.5f
        };
    }

    /// <summary>
    /// Configuration for the change detection phase.
    /// </summary>
    [Serializable]
    public struct ChangeDetectionConfig
    {
        [Tooltip("Number of change detection trials")]
        public int trialCount;

        [Tooltip("Number of objects in the scene")]
        public int objectCount;

        [Tooltip("Duration to display initial scene (seconds)")]
        public float studyDuration;

        [Tooltip("Duration of blank screen (seconds)")]
        public float blankDuration;

        [Tooltip("Maximum time to find change (seconds)")]
        public float maxSearchDuration;

        [Tooltip("Gaze time on object to register selection (seconds)")]
        public float detectionAcquisitionTime;

        [Tooltip("Position shift amount for position changes (meters)")]
        public float positionShiftAmount;

        [Tooltip("Show feedback when participant settles on an object")]
        public bool showFeedback;

        [Tooltip("Duration to display feedback (seconds)")]
        public float feedbackDuration;

        [Tooltip("Feedback color for correct detection")]
        public Color correctFeedbackColor;

        [Tooltip("Feedback color for incorrect detection")]
        public Color incorrectFeedbackColor;

        public static ChangeDetectionConfig Default => new ChangeDetectionConfig
        {
            trialCount = 3,
            objectCount = 12,
            studyDuration = 8.0f,   // long enough for participants to memorize the scene before the change
            blankDuration = 0.5f,
            maxSearchDuration = 15.0f,
            detectionAcquisitionTime = 1.0f,
            positionShiftAmount = 0.5f,
            showFeedback = true,
            feedbackDuration = 1.5f,
            correctFeedbackColor = Color.green,
            incorrectFeedbackColor = Color.red
        };
    }

    /// <summary>
    /// Results from a visual search trial.
    /// </summary>
    [Serializable]
    public struct VisualSearchResult
    {
        public int trialNumber;
        public bool targetFound;
        public float searchTimeSeconds;
        public Vector3 targetPosition;
        public VisualSearchCondition condition;
        public int distractorCount;
    }

    /// <summary>
    /// Results from a change detection trial.
    /// </summary>
    [Serializable]
    public struct ChangeDetectionResult
    {
        public int trialNumber;
        public bool changeDetected;
        public float detectionTimeSeconds;
        public string changeType; // "color" or "position"
        public string changedObjectName;
        public bool wasIncorrectSelection;  // True if participant selected wrong object
    }

    /// <summary>
    /// Results from the counting task.
    /// </summary>
    [Serializable]
    public struct CountingResult
    {
        public int actualCount;
        public int reportedCount;
        public bool isCorrect;
        public float responseTimeSeconds;
        public bool timedOut;
    }

    /// <summary>
    /// Complete session results for the experiment.
    /// </summary>
    [Serializable]
    public class ExperimentResults
    {
        public string participantID;
        public string sessionId;
        public DateTime startTime;
        public DateTime endTime;
        public float totalDurationSeconds;

        public VisualSearchResult[] visualSearchResults;
        public ChangeDetectionResult[] changeDetectionResults;
        public CountingResult countingResult;

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }
    }
}
