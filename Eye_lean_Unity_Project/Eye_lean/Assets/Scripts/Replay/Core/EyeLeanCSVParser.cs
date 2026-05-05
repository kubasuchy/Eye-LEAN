using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;

namespace EyeLean.Replay
{
    /// <summary>
    /// Parser for Eye_lean CSV format (ResearchDataStructure output).
    /// Maps column names to ReplayFrame fields with flexible column detection.
    /// </summary>
    public class EyeLeanCSVParser
    {
        // Column index mappings
        private Dictionary<string, int> columnMap = new Dictionary<string, int>();

        // Standard Eye_lean column names
        private static readonly Dictionary<string, string[]> COLUMN_ALIASES = new Dictionary<string, string[]>
        {
            // Timing
            { "timestamp", new[] { "UnityTimestamp", "timestamp", "time" } },
            { "realtime", new[] { "RealTimeSinceStartup", "realtime" } },
            { "frame", new[] { "FrameNumber", "frame", "frameNumber" } },
            { "delta", new[] { "DeltaTime", "delta", "deltaTime" } },

            // Session context
            { "participant", new[] { "ParticipantID", "participant_id", "participant" } },
            { "trial", new[] { "TrialNumber", "trial", "trialNumber" } },
            { "phase", new[] { "CurrentPhase", "phase", "current_phase" } },
            { "subtask", new[] { "SubTask", "sub_task", "subtask" } },

            // Head position
            { "head_x", new[] { "HeadPos_X", "head_x", "pos_x" } },
            { "head_y", new[] { "HeadPos_Y", "head_y", "pos_y" } },
            { "head_z", new[] { "HeadPos_Z", "head_z", "pos_z" } },

            // Head rotation
            { "head_rot_x", new[] { "HeadRot_X", "head_rot_x" } },
            { "head_rot_y", new[] { "HeadRot_Y", "head_rot_y" } },
            { "head_rot_z", new[] { "HeadRot_Z", "head_rot_z" } },
            { "head_rot_w", new[] { "HeadRot_W", "head_rot_w" } },

            // Head forward
            { "head_fwd_x", new[] { "HeadForward_X", "head_forward_x" } },
            { "head_fwd_y", new[] { "HeadForward_Y", "head_forward_y" } },
            { "head_fwd_z", new[] { "HeadForward_Z", "head_forward_z" } },

            // Combined gaze
            { "combined_origin_x", new[] { "CombinedOrigin_X", "combined_origin_x" } },
            { "combined_origin_y", new[] { "CombinedOrigin_Y", "combined_origin_y" } },
            { "combined_origin_z", new[] { "CombinedOrigin_Z", "combined_origin_z" } },
            { "combined_dir_x", new[] { "CombinedDir_X", "combined_dir_x" } },
            { "combined_dir_y", new[] { "CombinedDir_Y", "combined_dir_y" } },
            { "combined_dir_z", new[] { "CombinedDir_Z", "combined_dir_z" } },
            { "has_combined", new[] { "HasCombinedDirection", "has_combined_direction" } },

            // Left eye
            { "left_origin_x", new[] { "LeftOrigin_X", "left_origin_x", "LeftEyeOriginX" } },
            { "left_origin_y", new[] { "LeftOrigin_Y", "left_origin_y", "LeftEyeOriginY" } },
            { "left_origin_z", new[] { "LeftOrigin_Z", "left_origin_z", "LeftEyeOriginZ" } },
            { "left_dir_x", new[] { "LeftDir_X", "left_dir_x", "LeftEyeDirectionX" } },
            { "left_dir_y", new[] { "LeftDir_Y", "left_dir_y", "LeftEyeDirectionY" } },
            { "left_dir_z", new[] { "LeftDir_Z", "left_dir_z", "LeftEyeDirectionZ" } },
            { "left_openness", new[] { "LeftOpenness", "left_openness", "LeftEyeOpenness" } },
            { "left_pupil", new[] { "LeftPupilDiameter", "left_pupil_diameter" } },
            { "has_left", new[] { "HasLeftDirection", "has_left_direction" } },

            // Right eye
            { "right_origin_x", new[] { "RightOrigin_X", "right_origin_x", "RightEyeOriginX" } },
            { "right_origin_y", new[] { "RightOrigin_Y", "right_origin_y", "RightEyeOriginY" } },
            { "right_origin_z", new[] { "RightOrigin_Z", "right_origin_z", "RightEyeOriginZ" } },
            { "right_dir_x", new[] { "RightDir_X", "right_dir_x", "RightEyeDirectionX" } },
            { "right_dir_y", new[] { "RightDir_Y", "right_dir_y", "RightEyeDirectionY" } },
            { "right_dir_z", new[] { "RightDir_Z", "right_dir_z", "RightEyeDirectionZ" } },
            { "right_openness", new[] { "RightOpenness", "right_openness", "RightEyeOpenness" } },
            { "right_pupil", new[] { "RightPupilDiameter", "right_pupil_diameter" } },
            { "has_right", new[] { "HasRightDirection", "has_right_direction" } },

            // Vergence
            { "vergence_x", new[] { "VergencePoint_X", "vergence_x" } },
            { "vergence_y", new[] { "VergencePoint_Y", "vergence_y" } },
            { "vergence_z", new[] { "VergencePoint_Z", "vergence_z" } },
            { "vergence_quality", new[] { "VergenceQuality", "vergence_quality" } },
            { "has_vergence", new[] { "HasValidVergence", "has_valid_vergence" } },

            // Status
            { "tracking_valid", new[] { "IsTrackingValid", "is_tracking_valid", "tracking_valid" } },
            { "eye_available", new[] { "IsEyeTrackingAvailable", "is_eye_tracking_available" } },
        };

        private bool debugMode = false;

        /// <summary>
        /// Threshold for blink detection. Both eyes must be below this openness value to count as blinking.
        /// Default is 0.2 (20% open). Can be adjusted based on hardware or individual differences.
        /// </summary>
        public static float BlinkThreshold { get; set; } = 0.2f;

        /// <summary>
        /// Parse a CSV line properly handling quoted fields.
        /// Handles cases like: value1,"value with, comma",value3
        /// </summary>
        private string[] ParseCSVLine(string line)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;
            int fieldStart = 0;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    // End of field
                    string field = line.Substring(fieldStart, i - fieldStart).Trim();
                    // Remove surrounding quotes if present
                    if (field.Length >= 2 && field.StartsWith("\"") && field.EndsWith("\""))
                    {
                        field = field.Substring(1, field.Length - 2);
                    }
                    result.Add(field);
                    fieldStart = i + 1;
                }
            }

            // Don't forget the last field
            if (fieldStart <= line.Length)
            {
                string field = line.Substring(fieldStart).Trim();
                if (field.Length >= 2 && field.StartsWith("\"") && field.EndsWith("\""))
                {
                    field = field.Substring(1, field.Length - 2);
                }
                result.Add(field);
            }

            return result.ToArray();
        }

        /// <summary>
        /// Parse a complete CSV file into a ReplaySession
        /// </summary>
        public ReplaySession ParseFile(string filePath, bool debug = false)
        {
            debugMode = debug;

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[EyeLeanCSVParser] File not found: {filePath}");
                return null;
            }

            ReplaySession session = new ReplaySession();
            session.filePath = filePath;

            try
            {
                string[] lines = File.ReadAllLines(filePath);

                if (lines.Length < 2)
                {
                    Debug.LogError("[EyeLeanCSVParser] CSV file is empty or has only header");
                    return null;
                }

                // Strip UTF-8 BOM from the first line if present. Unity's
                // IL2CPP/Mono runtime has shipped inconsistent BOM-handling
                // across versions; without this guard, the first header
                // field carries a leading BOM and column-name lookup misses.
                if (lines[0].Length > 0 && lines[0][0] == '\uFEFF')
                {
                    lines[0] = lines[0].Substring(1);
                }

                // Capture `# Key: value` metadata lines. The load-bearing
                // keys are CoordinateOrigin (trial-start world position)
                // and CoordinateOriginSet (whether HeadPos_*, *Origin_*,
                // and VergencePoint_* columns are stored as offsets from
                // that origin or as raw world coordinates).
                int headerLineIndex = 0;
                while (headerLineIndex < lines.Length && lines[headerLineIndex].StartsWith("#"))
                {
                    ParseMetadataLine(lines[headerLineIndex], session);
                    headerLineIndex++;
                }

                if (headerLineIndex >= lines.Length)
                {
                    Debug.LogError("[EyeLeanCSVParser] No data lines found in CSV");
                    return null;
                }

                if (session.coordinateOriginSet)
                {
                    Debug.Log($"[EyeLeanCSVParser] CSV is normalized: positions are offsets from {session.coordinateOrigin}; will de-normalize on parse");
                }

                // Parse header
                ParseHeader(lines[headerLineIndex]);

                // Parse data lines
                List<ReplayFrame> frames = new List<ReplayFrame>();
                int validFrames = 0;
                int invalidFrames = 0;

                for (int i = headerLineIndex + 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;

                    ReplayFrame frame = ParseLine(lines[i], i);
                    if (frame != null)
                    {
                        if (session.coordinateOriginSet)
                        {
                            // CSV columns are offsets from session.coordinateOrigin;
                            // shift back to world space. Direction vectors and
                            // rotations are unaffected.
                            frame.headPosition += session.coordinateOrigin;
                            frame.combinedOrigin += session.coordinateOrigin;
                            frame.leftEyeOrigin += session.coordinateOrigin;
                            frame.rightEyeOrigin += session.coordinateOrigin;
                            frame.vergencePoint += session.coordinateOrigin;
                        }
                        frames.Add(frame);
                        validFrames++;
                    }
                    else
                    {
                        invalidFrames++;
                    }
                }

                session.frames = frames;
                session.totalFrames = frames.Count;

                // Calculate timing info
                if (frames.Count > 0)
                {
                    session.recordingStartTime = frames[0].timestamp;
                    session.recordingEndTime = frames[frames.Count - 1].timestamp;
                    session.totalDuration = session.recordingEndTime - session.recordingStartTime;
                    session.averageFrameRate = frames.Count / Mathf.Max(0.001f, session.totalDuration);

                    // Calculate frame durations
                    for (int i = 0; i < frames.Count - 1; i++)
                    {
                        frames[i].frameDuration = frames[i + 1].timestamp - frames[i].timestamp;
                    }
                    if (frames.Count > 1)
                    {
                        frames[frames.Count - 1].frameDuration = frames[frames.Count - 2].frameDuration;
                    }

                    // Get participant ID
                    session.participantId = frames[0].participantId ?? "Unknown";

                    // Build phase markers
                    BuildPhaseMarkers(session);

                    // Calculate quality stats
                    CalculateQualityStats(session);
                }

                if (debugMode)
                {
                    Debug.Log($"[EyeLeanCSVParser] Parsed {validFrames} frames, {invalidFrames} invalid lines");
                    Debug.Log($"[EyeLeanCSVParser] Duration: {session.totalDuration:F2}s, FPS: {session.averageFrameRate:F1}");
                    Debug.Log($"[EyeLeanCSVParser] Phases: {string.Join(", ", session.GetUniquePhases())}");
                }

                return session;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EyeLeanCSVParser] Error parsing file: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Parse a `# Key: value` metadata header line. Recognizes
        /// CoordinateOrigin (`x,y,z`) and CoordinateOriginSet (`true|false`);
        /// other keys are logged in debug mode and otherwise ignored.
        /// </summary>
        private void ParseMetadataLine(string line, ReplaySession session)
        {
            // Splitting is delegated to the shared helper so the sidecar
            // parser shares the same parsing rules.
            if (!EyeLean.Replay.SceneState.EyeLeanCsvHeaderReader.TryParse(line, out string key, out string value)) return;

            if (key.Equals("CoordinateOrigin", System.StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(',');
                if (parts.Length == 3 &&
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    session.coordinateOrigin = new Vector3(x, y, z);
                }
                else if (debugMode)
                {
                    Debug.LogWarning($"[EyeLeanCSVParser] Could not parse CoordinateOrigin value: '{value}'");
                }
            }
            else if (key.Equals("CoordinateOriginSet", System.StringComparison.OrdinalIgnoreCase))
            {
                session.coordinateOriginSet = value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
            }
            else if (key.Equals("Profile", System.StringComparison.OrdinalIgnoreCase))
            {
                // "none" sentinel means no profile was active. Anything else is
                // the profile name that was applied during the live recording.
                session.activeProfileName = value.Equals("none", System.StringComparison.OrdinalIgnoreCase) ? "" : value;
            }
            else if (debugMode)
            {
                Debug.Log($"[EyeLeanCSVParser] Metadata: {key}={value}");
            }
        }

        /// <summary>
        /// Parse CSV header and build column mapping
        /// </summary>
        private void ParseHeader(string headerLine)
        {
            columnMap.Clear();
            string[] headers = ParseCSVLine(headerLine);

            for (int i = 0; i < headers.Length; i++)
            {
                string colName = headers[i].Trim();
                columnMap[colName] = i;

                if (debugMode && i < 20)
                {
                    Debug.Log($"[EyeLeanCSVParser] Column {i}: '{colName}'");
                }
            }

            if (debugMode)
            {
                Debug.Log($"[EyeLeanCSVParser] Mapped {columnMap.Count} columns");
            }
        }

        /// <summary>
        /// Get column index by trying multiple aliases
        /// </summary>
        private int GetColumnIndex(string key)
        {
            // Direct lookup first
            if (columnMap.TryGetValue(key, out int idx))
                return idx;

            // Try aliases
            if (COLUMN_ALIASES.TryGetValue(key, out string[] aliases))
            {
                foreach (string alias in aliases)
                {
                    if (columnMap.TryGetValue(alias, out idx))
                        return idx;
                }
            }

            return -1;
        }

        /// <summary>
        /// Parse a single data line into a ReplayFrame
        /// </summary>
        private ReplayFrame ParseLine(string line, int lineNumber)
        {
            try
            {
                string[] values = ParseCSVLine(line);
                ReplayFrame frame = new ReplayFrame();

                // Timing
                frame.timestamp = GetFloat(values, "timestamp");
                frame.frameNumber = GetInt(values, "frame");

                // Session context
                frame.participantId = GetString(values, "participant");
                frame.trialNumber = GetInt(values, "trial");
                frame.phase = GetString(values, "phase");
                frame.subTask = GetString(values, "subtask");

                // Head tracking
                frame.headPosition = new Vector3(
                    GetFloat(values, "head_x"),
                    GetFloat(values, "head_y"),
                    GetFloat(values, "head_z")
                );

                // Head rotation
                float rotX = GetFloat(values, "head_rot_x");
                float rotY = GetFloat(values, "head_rot_y");
                float rotZ = GetFloat(values, "head_rot_z");
                float rotW = GetFloat(values, "head_rot_w", 1f);
                frame.headRotation = new Quaternion(rotX, rotY, rotZ, rotW);

                // Head forward
                frame.headForward = new Vector3(
                    GetFloat(values, "head_fwd_x"),
                    GetFloat(values, "head_fwd_y"),
                    GetFloat(values, "head_fwd_z", 1f)
                );

                // Combined gaze
                frame.combinedOrigin = new Vector3(
                    GetFloat(values, "combined_origin_x"),
                    GetFloat(values, "combined_origin_y"),
                    GetFloat(values, "combined_origin_z")
                );
                frame.combinedDirection = new Vector3(
                    GetFloat(values, "combined_dir_x"),
                    GetFloat(values, "combined_dir_y"),
                    GetFloat(values, "combined_dir_z")
                ).normalized;
                frame.hasCombinedGaze = GetBool(values, "has_combined");

                // Left eye
                frame.leftEyeOrigin = new Vector3(
                    GetFloat(values, "left_origin_x"),
                    GetFloat(values, "left_origin_y"),
                    GetFloat(values, "left_origin_z")
                );
                frame.leftEyeDirection = new Vector3(
                    GetFloat(values, "left_dir_x"),
                    GetFloat(values, "left_dir_y"),
                    GetFloat(values, "left_dir_z")
                ).normalized;
                frame.leftEyeOpenness = GetFloat(values, "left_openness", 1f);
                frame.leftPupilDiameter = GetFloat(values, "left_pupil");
                frame.hasLeftEye = GetBool(values, "has_left", true);

                // Right eye
                frame.rightEyeOrigin = new Vector3(
                    GetFloat(values, "right_origin_x"),
                    GetFloat(values, "right_origin_y"),
                    GetFloat(values, "right_origin_z")
                );
                frame.rightEyeDirection = new Vector3(
                    GetFloat(values, "right_dir_x"),
                    GetFloat(values, "right_dir_y"),
                    GetFloat(values, "right_dir_z")
                ).normalized;
                frame.rightEyeOpenness = GetFloat(values, "right_openness", 1f);
                frame.rightPupilDiameter = GetFloat(values, "right_pupil");
                frame.hasRightEye = GetBool(values, "has_right", true);

                // Vergence
                frame.vergencePoint = new Vector3(
                    GetFloat(values, "vergence_x"),
                    GetFloat(values, "vergence_y"),
                    GetFloat(values, "vergence_z")
                );
                frame.vergenceQuality = GetFloat(values, "vergence_quality");
                frame.hasValidVergence = GetBool(values, "has_vergence");

                // Status
                frame.isTrackingValid = GetBool(values, "tracking_valid", true);
                frame.isBlinking = frame.leftEyeOpenness < BlinkThreshold && frame.rightEyeOpenness < BlinkThreshold;

                return frame;
            }
            catch (Exception e)
            {
                if (debugMode)
                {
                    Debug.LogWarning($"[EyeLeanCSVParser] Error parsing line {lineNumber}: {e.Message}");
                }
                return null;
            }
        }

        // Helper methods for parsing values
        private float GetFloat(string[] values, string key, float defaultValue = 0f)
        {
            int idx = GetColumnIndex(key);
            if (idx < 0 || idx >= values.Length)
                return defaultValue;

            string val = values[idx].Trim().Trim('"');
            if (string.IsNullOrEmpty(val))
                return defaultValue;

            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;

            return defaultValue;
        }

        private int GetInt(string[] values, string key, int defaultValue = 0)
        {
            int idx = GetColumnIndex(key);
            if (idx < 0 || idx >= values.Length)
                return defaultValue;

            string val = values[idx].Trim().Trim('"');
            if (string.IsNullOrEmpty(val))
                return defaultValue;

            if (int.TryParse(val, out int result))
                return result;

            return defaultValue;
        }

        private string GetString(string[] values, string key, string defaultValue = "")
        {
            int idx = GetColumnIndex(key);
            if (idx < 0 || idx >= values.Length)
                return defaultValue;

            return values[idx].Trim().Trim('"');
        }

        private bool GetBool(string[] values, string key, bool defaultValue = false)
        {
            int idx = GetColumnIndex(key);
            if (idx < 0 || idx >= values.Length)
                return defaultValue;

            string val = values[idx].Trim().Trim('"').ToLower();
            return val == "true" || val == "1";
        }

        /// <summary>
        /// Build phase markers from frame data
        /// </summary>
        private void BuildPhaseMarkers(ReplaySession session)
        {
            if (session.frames.Count == 0)
                return;

            session.phaseMarkers.Clear();
            string currentPhase = null;
            PhaseMarker currentMarker = null;

            for (int i = 0; i < session.frames.Count; i++)
            {
                string phase = session.frames[i].phase;

                if (phase != currentPhase)
                {
                    // End previous marker
                    if (currentMarker != null)
                    {
                        currentMarker.endFrameIndex = i - 1;
                        currentMarker.endTime = session.frames[i - 1].timestamp;
                        currentMarker.frameCount = currentMarker.endFrameIndex - currentMarker.startFrameIndex + 1;
                        session.phaseMarkers.Add(currentMarker);
                    }

                    // Start new marker
                    currentPhase = phase;
                    currentMarker = new PhaseMarker
                    {
                        phaseName = phase ?? "Unknown",
                        startFrameIndex = i,
                        startTime = session.frames[i].timestamp
                    };
                }
            }

            // Close final marker
            if (currentMarker != null)
            {
                currentMarker.endFrameIndex = session.frames.Count - 1;
                currentMarker.endTime = session.frames[session.frames.Count - 1].timestamp;
                currentMarker.frameCount = currentMarker.endFrameIndex - currentMarker.startFrameIndex + 1;
                session.phaseMarkers.Add(currentMarker);
            }

            if (debugMode)
            {
                foreach (var marker in session.phaseMarkers)
                {
                    Debug.Log($"[EyeLeanCSVParser] Phase '{marker.phaseName}': frames {marker.startFrameIndex}-{marker.endFrameIndex} ({marker.frameCount} frames)");
                }
            }
        }

        /// <summary>
        /// Calculate quality statistics for the session
        /// </summary>
        private void CalculateQualityStats(ReplaySession session)
        {
            int validSamples = 0;
            int blinkSamples = 0;
            int trackingLossSamples = 0;

            foreach (var frame in session.frames)
            {
                if (frame.isTrackingValid)
                    validSamples++;
                else
                    trackingLossSamples++;

                if (frame.isBlinking)
                    blinkSamples++;
            }

            session.validSamplePercentage = session.frames.Count > 0
                ? (float)validSamples / session.frames.Count * 100f
                : 0f;
            session.blinkCount = blinkSamples;
            session.trackingLossCount = trackingLossSamples;

            if (debugMode)
            {
                Debug.Log($"[EyeLeanCSVParser] Quality: {session.validSamplePercentage:F1}% valid, {blinkSamples} blinks, {trackingLossSamples} tracking loss");
            }
        }
    }
}
