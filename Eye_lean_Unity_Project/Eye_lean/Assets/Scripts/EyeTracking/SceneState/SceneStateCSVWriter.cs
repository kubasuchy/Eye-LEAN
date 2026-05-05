using System.Globalization;
using System.IO;
using System.Text;

namespace EyeLean.SceneState
{
    /// <summary>
    /// Long-format sidecar formatter. Mirrors <c>SessionRecorder.FormatDataSampleAsCSV</c>
    /// conventions: reused StringBuilder, F6 floats, culture-invariant formatting.
    /// Column header:
    /// <code>
    /// Frame,T,ObjectId,Pos_X,Pos_Y,Pos_Z,Rot_X,Rot_Y,Rot_Z,Rot_W,Active[,ParentId]
    /// </code>
    /// ObjectId / ParentId are sanitized of stray commas to keep CSV alignment.
    /// </summary>
    public sealed class SceneStateCSVWriter
    {
        private readonly StreamWriter writer;
        private readonly StringBuilder builder = new StringBuilder(256);
        private readonly bool includeParentId;

        public const string FileVersion = "1.0";

        public SceneStateCSVWriter(StreamWriter writer, bool includeParentId)
        {
            this.writer = writer;
            this.includeParentId = includeParentId;
        }

        /// <summary>
        /// Write the metadata block + column header. Caller passes the
        /// CoordinateOrigin known at flush time so frame-1 buffered rows and
        /// every subsequent row share one consistent normalization frame.
        /// </summary>
        public void WriteHeader(string sessionId, UnityEngine.Vector3 coordinateOrigin, bool coordinateOriginSet,
                                int sampleEveryNthFrame, string profileName)
        {
            writer.WriteLine("# Eye_lean Scene State Sidecar");
            writer.WriteLine($"# FileVersion: {FileVersion}");
            writer.WriteLine($"# SessionID: {sessionId}");
            writer.WriteLine($"# CoordinateOrigin: {coordinateOrigin.x.ToString("F4", CultureInfo.InvariantCulture)},{coordinateOrigin.y.ToString("F4", CultureInfo.InvariantCulture)},{coordinateOrigin.z.ToString("F4", CultureInfo.InvariantCulture)}");
            writer.WriteLine($"# CoordinateOriginSet: {coordinateOriginSet}");
            writer.WriteLine($"# SampleEveryNthFrame: {sampleEveryNthFrame}");
            writer.WriteLine($"# Profile: {(string.IsNullOrEmpty(profileName) ? "none" : profileName)}");

            if (includeParentId)
                writer.WriteLine("Frame,T,ObjectId,Pos_X,Pos_Y,Pos_Z,Rot_X,Rot_Y,Rot_Z,Rot_W,Active,ParentId");
            else
                writer.WriteLine("Frame,T,ObjectId,Pos_X,Pos_Y,Pos_Z,Rot_X,Rot_Y,Rot_Z,Rot_W,Active");

            writer.Flush();
        }

        public void WriteRow(in SceneStateRow row)
        {
            builder.Clear();
            builder.Append(row.Frame).Append(',');
            builder.Append(row.T.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(SanitizeId(row.ObjectId)).Append(',');
            builder.Append(row.Position.x.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(row.Position.y.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(row.Position.z.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(row.Rotation.x.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(row.Rotation.y.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(row.Rotation.z.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(row.Rotation.w.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(row.Active ? "True" : "False");
            if (includeParentId)
            {
                builder.Append(',');
                builder.Append(SanitizeId(row.ParentId ?? string.Empty));
            }
            writer.WriteLine(builder.ToString());
        }

        public void Flush() => writer.Flush();

        private static string SanitizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            // GUIDs (the canonical case) never contain commas. Fallback paths
            // (gameObject.name) might. One stray comma silently corrupts CSV
            // alignment, so swap to underscore.
            return id.IndexOf(',') >= 0 ? id.Replace(',', '_') : id;
        }
    }
}
