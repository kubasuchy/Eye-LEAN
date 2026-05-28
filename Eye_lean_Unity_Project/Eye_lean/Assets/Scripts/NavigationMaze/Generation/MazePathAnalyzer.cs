// SPDX-License-Identifier: MIT
using System.Collections.Generic;

namespace EyeLean.NavigationMaze.Generation
{
    public struct MazeTrialMetrics
    {
        public float optimalPathLength;
        public int optimalPathCellCount;
        public int decisionPointsOnOptimalPath;
        public List<(int r, int c)> optimalPath;
    }

    public static class MazePathAnalyzer
    {
        public static MazeTrialMetrics ComputeOptimalPath(
            MazeGrid grid, int startR, int startC, int goalR, int goalC)
        {
            var path = BFS(grid, startR, startC, goalR, goalC);
            if (path == null)
            {
                return new MazeTrialMetrics
                {
                    optimalPathLength = -1f,
                    optimalPathCellCount = 0,
                    decisionPointsOnOptimalPath = 0,
                    optimalPath = new List<(int, int)>()
                };
            }

            int decisionPoints = 0;
            foreach (var (r, c) in path)
            {
                var jt = MazeCellClassifier.Classify(grid, r, c);
                if (MazeCellClassifier.IsDecisionPoint(jt)) decisionPoints++;
            }

            return new MazeTrialMetrics
            {
                optimalPathLength = (path.Count - 1) * grid.CellSize,
                optimalPathCellCount = path.Count,
                decisionPointsOnOptimalPath = decisionPoints,
                optimalPath = path
            };
        }

        public static List<(int r, int c)> BFS(
            MazeGrid grid, int startR, int startC, int goalR, int goalC)
        {
            if (startR == goalR && startC == goalC)
                return new List<(int, int)> { (startR, startC) };

            var visited = new bool[grid.Size, grid.Size];
            var parent = new (int r, int c)[grid.Size, grid.Size];
            for (int r = 0; r < grid.Size; r++)
                for (int c = 0; c < grid.Size; c++)
                    parent[r, c] = (-1, -1);

            var queue = new Queue<(int r, int c)>();
            visited[startR, startC] = true;
            queue.Enqueue((startR, startC));

            while (queue.Count > 0)
            {
                var (cr, cc) = queue.Dequeue();
                foreach (var (nr, nc) in grid.OpenNeighbors(cr, cc))
                {
                    if (visited[nr, nc]) continue;
                    visited[nr, nc] = true;
                    parent[nr, nc] = (cr, cc);
                    if (nr == goalR && nc == goalC)
                        return ReconstructPath(parent, startR, startC, goalR, goalC);
                    queue.Enqueue((nr, nc));
                }
            }
            return null;
        }

        private static List<(int r, int c)> ReconstructPath(
            (int r, int c)[,] parent, int sr, int sc, int gr, int gc)
        {
            var path = new List<(int, int)>();
            int cr = gr, cc = gc;
            while (!(cr == sr && cc == sc))
            {
                path.Add((cr, cc));
                var (pr, pc) = parent[cr, cc];
                cr = pr; cc = pc;
            }
            path.Add((sr, sc));
            path.Reverse();
            return path;
        }
    }
}
