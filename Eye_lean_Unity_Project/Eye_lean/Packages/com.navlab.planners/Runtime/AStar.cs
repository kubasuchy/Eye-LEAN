using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Navlab.Planners
{
    public sealed class AStar : IPlanner
    {
        public string Name => "astar";
        public string Version => "1.0.0";

        private const double SQRT2 = 1.4142135623730951;
        private static readonly double SQRT2_MINUS_ONE = SQRT2 - 1.0;

        // Neighbor offsets: (dCol, dRow, cost), counter-clockwise from East.
        private static readonly (int dc, int dr, double cost)[] Neighbors = new[]
        {
            (+1,  0, 1.0),       // E
            (+1, +1, SQRT2),     // NE
            ( 0, +1, 1.0),       // N
            (-1, +1, SQRT2),     // NW
            (-1,  0, 1.0),       // W
            (-1, -1, SQRT2),     // SW
            ( 0, -1, 1.0),       // S
            (+1, -1, SQRT2),     // SE
        };

        private static double Octile(int r1, int c1, int r2, int c2)
        {
            int dx = Math.Abs(c1 - c2);
            int dy = Math.Abs(r1 - r2);
            return Math.Max(dx, dy) + SQRT2_MINUS_ONE * Math.Min(dx, dy);
        }

        public PlanResult Plan(PlanRequest request)
        {
            var sw = Stopwatch.StartNew();
            bool[,] inflated = request.Grid.InflateObstacles(request.AgentRadiusM);
            int h = request.Grid.Height, w = request.Grid.Width;

            int sr, sc, gr, gc;
            try
            {
                (sr, sc) = request.Grid.WorldToCell(request.StartWorld.x, request.StartWorld.y);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, 0, "start in obstacle");
            }
            try
            {
                (gr, gc) = request.Grid.WorldToCell(request.GoalWorld.x, request.GoalWorld.y);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, 0, "goal in obstacle");
            }

            if (inflated[sr, sc])
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, 0, "start in obstacle");
            if (inflated[gr, gc])
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, 0, "goal in obstacle");
            if (sr == gr && sc == gc)
            {
                var (wx, wy) = request.Grid.CellToWorld(sr, sc);
                return new PlanResult(true, new[] { (wx, wy) },
                    sw.Elapsed.TotalMilliseconds, 0);
            }

            // Priority queue: SortedSet ordered by (f, h, insertion, row, col).
            var open = new SortedSet<(double f, double h, int ord, int r, int c)>();
            var gScore = new Dictionary<(int, int), double> { [(sr, sc)] = 0.0 };
            var parent = new Dictionary<(int, int), (int, int)>();
            var closed = new HashSet<(int, int)>();
            int insertion = 0;
            double h0 = Octile(sr, sc, gr, gc);
            open.Add((h0, h0, insertion++, sr, sc));

            int nodesExpanded = 0;
            double timeoutMs = request.MaxPlanningTimeMs;

            while (open.Count > 0)
            {
                if (sw.Elapsed.TotalMilliseconds > timeoutMs)
                    return new PlanResult(false, Array.Empty<(float, float)>(),
                        sw.Elapsed.TotalMilliseconds, nodesExpanded, "timeout");
                var top = open.Min;
                open.Remove(top);
                var (cr, cc) = (top.r, top.c);
                if (closed.Contains((cr, cc))) continue;
                closed.Add((cr, cc));
                nodesExpanded++;

                if (cr == gr && cc == gc)
                {
                    var path = new List<(int, int)> { (cr, cc) };
                    while (parent.TryGetValue(path[path.Count - 1], out var p))
                        path.Add(p);
                    path.Reverse();
                    var waypoints = new (float, float)[path.Count];
                    for (int i = 0; i < path.Count; i++)
                    {
                        var (wx, wy) = request.Grid.CellToWorld(path[i].Item1, path[i].Item2);
                        waypoints[i] = (wx, wy);
                    }
                    return new PlanResult(true, waypoints,
                        sw.Elapsed.TotalMilliseconds, nodesExpanded);
                }

                foreach (var (dc, dr, stepCost) in Neighbors)
                {
                    int nr = cr + dr, nc = cc + dc;
                    if (nr < 0 || nr >= h || nc < 0 || nc >= w) continue;
                    if (inflated[nr, nc]) continue;
                    if (closed.Contains((nr, nc))) continue;
                    double tentative = gScore[(cr, cc)] + stepCost;
                    if (!gScore.TryGetValue((nr, nc), out var existing) || tentative < existing)
                    {
                        gScore[(nr, nc)] = tentative;
                        parent[(nr, nc)] = (cr, cc);
                        double hsc = Octile(nr, nc, gr, gc);
                        double f = tentative + hsc;
                        open.Add((f, hsc, insertion++, nr, nc));
                    }
                }
            }

            return new PlanResult(false, Array.Empty<(float, float)>(),
                sw.Elapsed.TotalMilliseconds, nodesExpanded, "no path");
        }
    }
}
