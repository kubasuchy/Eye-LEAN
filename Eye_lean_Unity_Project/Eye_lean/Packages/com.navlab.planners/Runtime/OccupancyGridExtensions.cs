using System;

namespace Navlab.Planners
{
    public static class OccupancyGridExtensions
    {
        public static (int row, int col) WorldToCell(this OccupancyGrid g, float wx, float wy)
        {
            int col = (int)Math.Round((wx - g.OriginWorldX) / g.ResolutionM, MidpointRounding.ToEven);
            int row = (int)Math.Round((wy - g.OriginWorldY) / g.ResolutionM, MidpointRounding.ToEven);
            if (row < 0 || row >= g.Height || col < 0 || col >= g.Width)
                throw new ArgumentOutOfRangeException(
                    $"world ({wx}, {wy}) -> cell ({row}, {col}) " +
                    $"is outside grid {g.Height}x{g.Width}");
            return (row, col);
        }

        public static (float x, float y) CellToWorld(this OccupancyGrid g, int row, int col)
        {
            float wx = g.OriginWorldX + col * g.ResolutionM;
            float wy = g.OriginWorldY + row * g.ResolutionM;
            return (wx, wy);
        }

        public static bool[,] InflateObstacles(this OccupancyGrid g, float agentRadiusM)
        {
            int h = g.Height, w = g.Width;
            var result = new bool[h, w];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    result[r, c] = g.Data[r, c];
            if (agentRadiusM <= 0f) return result;
            int inflateCells = (int)Math.Ceiling(agentRadiusM / g.ResolutionM);
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (!g.Data[r, c]) continue;
                    int r0 = Math.Max(0, r - inflateCells);
                    int r1 = Math.Min(h - 1, r + inflateCells);
                    int c0 = Math.Max(0, c - inflateCells);
                    int c1 = Math.Min(w - 1, c + inflateCells);
                    for (int rr = r0; rr <= r1; rr++)
                        for (int cc = c0; cc <= c1; cc++)
                            result[rr, cc] = true;
                }
            }
            return result;
        }
    }
}
