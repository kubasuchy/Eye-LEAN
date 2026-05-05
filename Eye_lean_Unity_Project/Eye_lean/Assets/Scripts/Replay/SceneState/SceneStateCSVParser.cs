using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Parser for the long-format scene-state sidecar CSV produced by
    /// <c>SceneStateRecorder</c>. Sibling to <c>EyeLeanCSVParser</c>.
    /// Reuses <see cref="EyeLeanCsvHeaderReader"/> for the
    /// <c># Key: value</c> metadata block; the column header is fixed, so
    /// no alias-based mapping is needed.
    /// </summary>
    public class SceneStateCSVParser
    {
        public bool DebugMode { get; set; } = false;

        public SceneStateTimeline ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                if (DebugMode) Debug.Log($"[SceneStateCSVParser] No sidecar at {filePath}");
                return null;
            }

            var timeline = new SceneStateTimeline();
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length < 2) return null;

                // Strip UTF-8 BOM if present (matches EyeLeanCSVParser).
                if (lines[0].Length > 0 && lines[0][0] == '\uFEFF') lines[0] = lines[0].Substring(1);

                int headerIdx = 0;
                while (headerIdx < lines.Length && lines[headerIdx].StartsWith("#"))
                {
                    if (EyeLeanCsvHeaderReader.TryParse(lines[headerIdx], out string key, out string value))
                    {
                        ApplyMetadata(timeline, key, value);
                    }
                    headerIdx++;
                }
                if (headerIdx >= lines.Length) return timeline;

                // Column header — fixed column order. We tolerate the optional
                // trailing ParentId column, but require the first 11.
                string[] headerCols = lines[headerIdx].Split(',');
                int parentIdIdx = -1;
                for (int i = 0; i < headerCols.Length; i++)
                {
                    if (headerCols[i].Trim().Equals("ParentId", StringComparison.OrdinalIgnoreCase))
                    { parentIdIdx = i; break; }
                }

                for (int i = headerIdx + 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cols = line.Split(',');
                    if (cols.Length < 11) continue; // malformed

                    if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame)) continue;
                    if (!float.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float t)) t = 0f;
                    string objectId = cols[2];

                    var key = new SceneStateKey
                    {
                        Frame = frame,
                        T = t,
                        ObjectId = objectId,
                        Position = ParseVec3(cols, 3),
                        Rotation = ParseQuat(cols, 6),
                        Active = ParseBool(cols[10]),
                        ParentId = (parentIdIdx >= 0 && parentIdIdx < cols.Length) ? cols[parentIdIdx] : string.Empty,
                    };

                    if (!timeline.ByFrame.TryGetValue(frame, out var frameList))
                    {
                        frameList = new List<SceneStateKey>();
                        timeline.ByFrame[frame] = frameList;
                    }
                    frameList.Add(key);

                    if (!timeline.ByObject.TryGetValue(objectId, out var objList))
                    {
                        objList = new List<SceneStateKey>();
                        timeline.ByObject[objectId] = objList;
                    }
                    objList.Add(key);
                }

                // ByObject lists already arrive in per-frame ascending
                // order; defensive sort guards against future out-of-order
                // rows from the recorder.
                foreach (var kv in timeline.ByObject) kv.Value.Sort((a, b) => a.Frame.CompareTo(b.Frame));

                if (DebugMode)
                {
                    Debug.Log($"[SceneStateCSVParser] Parsed {timeline.FrameCount} frames, {timeline.ObjectCount} unique ids from {filePath}");
                }
                return timeline;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneStateCSVParser] Error parsing {filePath}: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        private static void ApplyMetadata(SceneStateTimeline t, string key, string value)
        {
            if (key.Equals("CoordinateOrigin", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(',');
                if (parts.Length == 3
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    t.CoordinateOrigin = new Vector3(x, y, z);
                }
            }
            else if (key.Equals("CoordinateOriginSet", StringComparison.OrdinalIgnoreCase))
            {
                t.CoordinateOriginSet = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            else if (key.Equals("SampleEveryNthFrame", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    t.SampleEveryNthFrame = Mathf.Max(1, n);
            }
            else if (key.Equals("Profile", StringComparison.OrdinalIgnoreCase))
            {
                t.ProfileName = value.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : value;
            }
            else if (key.Equals("SessionID", StringComparison.OrdinalIgnoreCase))
            {
                t.SessionId = value;
            }
        }

        private static Vector3 ParseVec3(string[] cols, int start)
        {
            float.TryParse(cols[start    ], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
            float.TryParse(cols[start + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
            float.TryParse(cols[start + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
            return new Vector3(x, y, z);
        }

        private static Quaternion ParseQuat(string[] cols, int start)
        {
            float.TryParse(cols[start    ], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
            float.TryParse(cols[start + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
            float.TryParse(cols[start + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
            float.TryParse(cols[start + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out float w);
            return new Quaternion(x, y, z, w);
        }

        private static bool ParseBool(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim();
            return t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1";
        }
    }
}
