using System;
using System.Collections.Generic;

namespace Navlab.Planners
{
    public sealed class OccupancyGrid
    {
        public int Width { get; }
        public int Height { get; }
        public float ResolutionM { get; }
        public float OriginWorldX { get; }
        public float OriginWorldY { get; }
        public bool[,] Data { get; }  // Indexed as Data[row, col]

        public OccupancyGrid(int width, int height, float resolutionM,
            float originWorldX, float originWorldY, bool[,] data)
        {
            if (data.GetLength(0) != height || data.GetLength(1) != width)
                throw new ArgumentException(
                    $"data shape [{data.GetLength(0)},{data.GetLength(1)}] " +
                    $"!= (height={height}, width={width})");
            Width = width;
            Height = height;
            ResolutionM = resolutionM;
            OriginWorldX = originWorldX;
            OriginWorldY = originWorldY;
            Data = data;
        }
    }

    public sealed class DynamicObstacle
    {
        public string ObjectId { get; }
        public float FootprintRadiusM { get; }
        public IReadOnlyList<(float t, float x, float y)> Poses { get; }

        public DynamicObstacle(string objectId, float footprintRadiusM,
            IReadOnlyList<(float t, float x, float y)> poses)
        {
            for (int i = 1; i < poses.Count; i++)
                if (poses[i].t < poses[i - 1].t)
                    throw new ArgumentException("poses must have non-decreasing t");
            ObjectId = objectId;
            FootprintRadiusM = footprintRadiusM;
            Poses = poses;
        }
    }

    public sealed class PlanRequest
    {
        public OccupancyGrid Grid { get; }
        public (float x, float y) StartWorld { get; }
        public (float x, float y) GoalWorld { get; }
        public float AgentRadiusM { get; }
        public IReadOnlyList<DynamicObstacle> DynamicObstacles { get; }
        public double MaxPlanningTimeMs { get; }
        public IReadOnlyDictionary<string, object> Extra { get; }

        public PlanRequest(OccupancyGrid grid,
            (float, float) startWorld, (float, float) goalWorld,
            float agentRadiusM,
            IReadOnlyList<DynamicObstacle> dynamicObstacles = null,
            double maxPlanningTimeMs = 5000.0,
            IReadOnlyDictionary<string, object> extra = null)
        {
            Grid = grid;
            StartWorld = startWorld;
            GoalWorld = goalWorld;
            AgentRadiusM = agentRadiusM;
            DynamicObstacles = dynamicObstacles ?? Array.Empty<DynamicObstacle>();
            MaxPlanningTimeMs = maxPlanningTimeMs;
            Extra = extra ?? new Dictionary<string, object>();
        }
    }

    public sealed class PlanResult
    {
        public bool Success { get; }
        public (float x, float y)[] WaypointsWorld { get; }
        public double RuntimeMs { get; }
        public int NodesExpanded { get; }
        public string ErrorMessage { get; }
        public IReadOnlyDictionary<string, object> Extra { get; }

        public PlanResult(bool success, (float x, float y)[] waypointsWorld,
            double runtimeMs, int nodesExpanded,
            string errorMessage = null,
            IReadOnlyDictionary<string, object> extra = null)
        {
            Success = success;
            WaypointsWorld = waypointsWorld;
            RuntimeMs = runtimeMs;
            NodesExpanded = nodesExpanded;
            ErrorMessage = errorMessage;
            Extra = extra ?? new Dictionary<string, object>();
        }
    }

    public interface IPlanner
    {
        string Name { get; }
        string Version { get; }
        PlanResult Plan(PlanRequest request);
    }
}
