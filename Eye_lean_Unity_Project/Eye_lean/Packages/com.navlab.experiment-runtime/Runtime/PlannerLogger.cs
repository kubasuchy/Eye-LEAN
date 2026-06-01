// unity-component/Runtime/PlannerLogger.cs
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Navlab.ExperimentRuntime
{
    /// <summary>Writes the _PlannerLog.csv sidecar. One row per replan call.</summary>
    public sealed class PlannerLogger
    {
        private readonly StreamWriter _writer;

        public PlannerLogger(string path)
        {
            _writer = new StreamWriter(path);
            _writer.WriteLine("t,planner,version,runtime_ms,waypoint_count,waypoint_hash");
        }

        public void LogReplan(float tSeconds, string plannerName, string plannerVersion,
                               double runtimeMs, (float x, float y)[] waypoints)
        {
            string hash = HashWaypoints(waypoints);
            _writer.WriteLine(
                $"{tSeconds:F4},{plannerName},{plannerVersion}," +
                $"{runtimeMs:F3},{waypoints.Length},{hash}");
        }

        public void Flush() => _writer.Flush();

        public void Close() => _writer.Close();

        private static string HashWaypoints((float x, float y)[] waypoints)
        {
            using var sha = SHA1.Create();
            var sb = new StringBuilder();
            foreach (var (x, y) in waypoints) sb.Append($"{x:F6},{y:F6};");
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return System.BitConverter.ToString(hash).Replace("-", "").Substring(0, 12);
        }
    }
}
