using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Parser for the events sidecar produced by
    /// <see cref="EyeLean.SceneState.SceneEventRecorder"/>. Sibling to
    /// <see cref="SceneStateCSVParser"/>; reuses
    /// <see cref="EyeLeanCsvHeaderReader"/> for the metadata header block.
    /// </summary>
    public class SceneEventCSVParser
    {
        public bool DebugMode { get; set; } = false;

        public SceneEventTimeline ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                if (DebugMode) Debug.Log($"[SceneEventCSVParser] No events sidecar at {filePath}");
                return null;
            }

            var timeline = new SceneEventTimeline();
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length < 2) return timeline;

                // Strip BOM if present (matches main + state parsers).
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

                // Column header is fixed and unused for parsing — column
                // order is locked by the recorder. Skip it.
                int dataStart = headerIdx + 1;

                for (int i = dataStart; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cols = line.Split(',');
                    if (cols.Length < 5) continue; // malformed

                    if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame)) continue;
                    if (!float.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float t)) t = 0f;
                    var row = new SceneEventRow
                    {
                        Frame = frame,
                        T = t,
                        EventType = cols[2],
                        ObjectId = cols[3],
                        // Re-join cols 4..n in case a Detail value contained
                        // a comma that sanitization missed (defensive — the
                        // recorder replaces commas with underscores).
                        Detail = cols.Length > 5 ? string.Join(",", cols, 4, cols.Length - 4) : cols[4],
                    };

                    if (!timeline.ByFrame.TryGetValue(frame, out var bucket))
                    {
                        bucket = new List<SceneEventRow>();
                        timeline.ByFrame[frame] = bucket;
                    }
                    bucket.Add(row);
                    timeline.TotalEventCount++;
                }

                if (DebugMode)
                {
                    Debug.Log($"[SceneEventCSVParser] Parsed {timeline.TotalEventCount} events across {timeline.FrameCount} frames from {filePath}");
                }
                return timeline;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneEventCSVParser] Error parsing {filePath}: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        private static void ApplyMetadata(SceneEventTimeline t, string key, string value)
        {
            if (key.Equals("SessionID", StringComparison.OrdinalIgnoreCase))
            {
                t.SessionId = value;
            }
            // FileVersion / banner lines are recognized but not stored.
        }
    }
}
