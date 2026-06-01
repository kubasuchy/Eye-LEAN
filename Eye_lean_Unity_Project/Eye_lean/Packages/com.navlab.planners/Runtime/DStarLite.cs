using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Navlab.Planners
{
    public sealed class DStarLite : IPlanner
    {
        public string Name => "dstar_lite";
        public string Version => "1.0.0";

        private const double SQRT2 = 1.4142135623730951;
        private static readonly double SQRT2_MINUS_ONE = SQRT2 - 1.0;

        private static readonly (int dc, int dr, double cost)[] Neighbors = new[]
        {
            (+1,  0, 1.0),
            (+1, +1, SQRT2),
            ( 0, +1, 1.0),
            (-1, +1, SQRT2),
            (-1,  0, 1.0),
            (-1, -1, SQRT2),
            ( 0, -1, 1.0),
            (+1, -1, SQRT2),
        };

        private static double H(int r1, int c1, int r2, int c2)
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

            (int r, int c) sStart;
            (int r, int c) sGoal;
            try
            {
                sStart = request.Grid.WorldToCell(request.StartWorld.x, request.StartWorld.y);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, 0, "start in obstacle");
            }
            try
            {
                sGoal = request.Grid.WorldToCell(request.GoalWorld.x, request.GoalWorld.y);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, 0, "goal in obstacle");
            }

            if (inflated[sStart.r, sStart.c])
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, 0, "start in obstacle");
            if (inflated[sGoal.r, sGoal.c])
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, 0, "goal in obstacle");

            var gScore = new Dictionary<(int, int), double>();
            var rhs = new Dictionary<(int, int), double>();
            rhs[sGoal] = 0.0;

            double G((int r, int c) s) =>
                gScore.TryGetValue(s, out var v) ? v : double.PositiveInfinity;
            double RHS((int r, int c) s) =>
                rhs.TryGetValue(s, out var v) ? v : double.PositiveInfinity;

            double km = 0.0;
            int insertion = 0;

            // Open: SortedSet keyed on (k1, k2, insertion, r, c)
            var open = new SortedSet<(double k1, double k2, int ord, int r, int c)>();
            var inOpen = new Dictionary<(int, int), (double k1, double k2, int ord)>();

            (double k1, double k2) CalcKey((int r, int c) s)
            {
                double mn = Math.Min(G(s), RHS(s));
                return (mn + H(sStart.r, sStart.c, s.r, s.c) + km, mn);
            }

            void UpdateVertex((int r, int c) s)
            {
                if (s != sGoal)
                {
                    double best = double.PositiveInfinity;
                    foreach (var (dc, dr, cost) in Neighbors)
                    {
                        int nr = s.r + dr, nc = s.c + dc;
                        if (nr < 0 || nr >= h || nc < 0 || nc >= w) continue;
                        if (inflated[nr, nc]) continue;
                        double candidate = cost + G((nr, nc));
                        if (candidate < best) best = candidate;
                    }
                    if (double.IsPositiveInfinity(best)) rhs.Remove(s);
                    else rhs[s] = best;
                }
                // Remove any stale open-set entry before (re)inserting.
                if (inOpen.TryGetValue(s, out var prevEntry))
                {
                    open.Remove((prevEntry.k1, prevEntry.k2, prevEntry.ord, s.r, s.c));
                    inOpen.Remove(s);
                }
                if (G(s) != RHS(s))
                {
                    var (k1, k2) = CalcKey(s);
                    int ord = insertion++;
                    open.Add((k1, k2, ord, s.r, s.c));
                    inOpen[s] = (k1, k2, ord);
                }
            }

            // Initialize: insert sGoal
            {
                var (k1, k2) = CalcKey(sGoal);
                int ord = insertion++;
                open.Add((k1, k2, ord, sGoal.r, sGoal.c));
                inOpen[sGoal] = (k1, k2, ord);
            }

            int nodesExpanded = 0;
            double timeoutMs = request.MaxPlanningTimeMs;

            // ComputeShortestPath
            while (open.Count > 0)
            {
                if (sw.Elapsed.TotalMilliseconds > timeoutMs)
                    return new PlanResult(false, Array.Empty<(float, float)>(),
                        sw.Elapsed.TotalMilliseconds, nodesExpanded, "timeout");

                var top = open.Min;
                var startKey = CalcKey(sStart);
                int topVsStart = (top.k1, top.k2).CompareTo((startKey.k1, startKey.k2));
                if (topVsStart >= 0 && RHS(sStart) == G(sStart))
                    break;

                open.Remove(top);
                var u = (top.r, top.c);
                inOpen.Remove(u);
                var kOld = (top.k1, top.k2);
                var kNew = CalcKey(u);
                if (kOld.CompareTo(kNew) < 0)
                {
                    int ord = insertion++;
                    open.Add((kNew.k1, kNew.k2, ord, u.r, u.c));
                    inOpen[u] = (kNew.k1, kNew.k2, ord);
                }
                else if (G(u) > RHS(u))
                {
                    gScore[u] = RHS(u);
                    nodesExpanded++;
                    foreach (var (dc, dr, _) in Neighbors)
                    {
                        int pr = u.r + dr, pc = u.c + dc;
                        if (pr < 0 || pr >= h || pc < 0 || pc >= w) continue;
                        if (inflated[pr, pc]) continue;
                        UpdateVertex((pr, pc));
                    }
                }
                else
                {
                    gScore[u] = double.PositiveInfinity;
                    nodesExpanded++;
                    UpdateVertex(u);
                    foreach (var (dc, dr, _) in Neighbors)
                    {
                        int pr = u.r + dr, pc = u.c + dc;
                        if (pr < 0 || pr >= h || pc < 0 || pc >= w) continue;
                        if (inflated[pr, pc]) continue;
                        UpdateVertex((pr, pc));
                    }
                }
            }

            if (double.IsPositiveInfinity(G(sStart)))
                return new PlanResult(false, Array.Empty<(float, float)>(),
                    sw.Elapsed.TotalMilliseconds, nodesExpanded, "no path");

            // Walk min-cost successors from sStart to sGoal
            var path = new List<(int, int)> { sStart };
            var cur = sStart;
            int guard = 0;
            int maxSteps = h * w + 1;
            while (cur != sGoal)
            {
                guard++;
                if (guard > maxSteps)
                    return new PlanResult(false, Array.Empty<(float, float)>(),
                        sw.Elapsed.TotalMilliseconds, nodesExpanded, "path walk overflow");

                (int r, int c)? best = null;
                double bestCost = double.PositiveInfinity;
                foreach (var (dc, dr, cost) in Neighbors)
                {
                    int nr = cur.r + dr, nc = cur.c + dc;
                    if (nr < 0 || nr >= h || nc < 0 || nc >= w) continue;
                    if (inflated[nr, nc]) continue;
                    double candidate = cost + G((nr, nc));
                    if (candidate < bestCost) { bestCost = candidate; best = (nr, nc); }
                }
                if (best == null)
                    return new PlanResult(false, Array.Empty<(float, float)>(),
                        sw.Elapsed.TotalMilliseconds, nodesExpanded, "no path");
                path.Add(best.Value);
                cur = best.Value;
            }

            var waypoints = new (float, float)[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                var (wx, wy) = request.Grid.CellToWorld(path[i].Item1, path[i].Item2);
                waypoints[i] = (wx, wy);
            }
            return new PlanResult(true, waypoints,
                sw.Elapsed.TotalMilliseconds, nodesExpanded);
        }
    }
}
