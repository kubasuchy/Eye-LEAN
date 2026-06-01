// unity-component/Runtime/SceneAdapter.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using Navlab.Planners;

namespace Navlab.ExperimentRuntime
{
    public static class SceneAdapter
    {
        /// <summary>Rasterize an EnvironmentConfig's static geometry into an
        /// OccupancyGrid. Mirrors the workbench's EnvironmentConfig.rasterize().</summary>
        public static OccupancyGrid Rasterize(EnvironmentConfig env, float resolutionM)
        {
            float wx0 = env.boundsMin.x, wy0 = env.boundsMin.y;
            float wx1 = env.boundsMax.x, wy1 = env.boundsMax.y;
            int width = Math.Max(1, Mathf.RoundToInt((wx1 - wx0) / resolutionM));
            int height = Math.Max(1, Mathf.RoundToInt((wy1 - wy0) / resolutionM));
            var data = new bool[height, width];

            (int row, int col) WorldToCell(float wx, float wy)
            {
                int col = Mathf.RoundToInt((wx - wx0) / resolutionM);
                int row = Mathf.RoundToInt((wy - wy0) / resolutionM);
                return (row, col);
            }

            // Walls: sampled along the segment with thickness inflation
            foreach (var w in env.walls)
            {
                float length = Vector2.Distance(w.startXZ, w.endXZ);
                int nSteps = Math.Max(2, (int)(length / (resolutionM * 0.5f)) + 1);
                int halfThickCells = Math.Max(1,
                    Mathf.CeilToInt(w.thickness / 2.0f / resolutionM));
                for (int i = 0; i < nSteps; i++)
                {
                    float t = (float)i / (nSteps - 1);
                    float wx = Mathf.Lerp(w.startXZ.x, w.endXZ.x, t);
                    float wy = Mathf.Lerp(w.startXZ.y, w.endXZ.y, t);
                    var (r, c) = WorldToCell(wx, wy);
                    int r0 = Math.Max(0, r - halfThickCells);
                    int r1 = Math.Min(height - 1, r + halfThickCells);
                    int c0 = Math.Max(0, c - halfThickCells);
                    int c1 = Math.Min(width - 1, c + halfThickCells);
                    for (int rr = r0; rr <= r1; rr++)
                        for (int cc = c0; cc <= c1; cc++)
                            data[rr, cc] = true;
                }
            }

            // Circular obstacles
            foreach (var o in env.obstacles)
            {
                var (cr, cc) = WorldToCell(o.centerXZ.x, o.centerXZ.y);
                int radiusCells = Math.Max(1, Mathf.CeilToInt(o.radius / resolutionM));
                for (int dr = -radiusCells; dr <= radiusCells; dr++)
                {
                    for (int dc = -radiusCells; dc <= radiusCells; dc++)
                    {
                        if (dr * dr + dc * dc > radiusCells * radiusCells) continue;
                        int r = cr + dr, c = cc + dc;
                        if (r >= 0 && r < height && c >= 0 && c < width)
                            data[r, c] = true;
                    }
                }
            }

            return new OccupancyGrid(width, height, resolutionM, wx0, wy0, data);
        }

        /// <summary>Build the live DynamicObstacle list from tracked Transforms.
        /// poses is a single-frame snapshot at currentTime.</summary>
        public static IReadOnlyList<DynamicObstacle> BuildDynamicObstacles(
            IEnumerable<TrackedObject> tracked, float currentTime)
        {
            var result = new List<DynamicObstacle>();
            foreach (var t in tracked)
            {
                if (t.transform == null) continue;
                var pos = t.transform.position;
                result.Add(new DynamicObstacle(
                    t.objectId,
                    t.footprintRadiusM,
                    new[] { (currentTime, pos.x, pos.z) }
                ));
            }
            return result;
        }
    }

    /// <summary>Lightweight handle for a dynamically tracked object.</summary>
    public sealed class TrackedObject
    {
        public string objectId;
        public Transform transform;
        public float footprintRadiusM;
    }
}
