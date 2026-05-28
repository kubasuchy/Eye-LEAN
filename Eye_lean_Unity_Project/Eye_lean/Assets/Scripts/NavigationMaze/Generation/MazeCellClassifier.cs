// SPDX-License-Identifier: MIT
namespace EyeLean.NavigationMaze.Generation
{
    public enum JunctionType
    {
        DeadEnd,
        Corner,
        Straight,
        T_Junction,
        Crossroad
    }

    public static class MazeCellClassifier
    {
        public static JunctionType Classify(MazeGrid grid, int r, int c)
        {
            int openings = grid.OpeningCount(r, c);
            switch (openings)
            {
                case 0: return JunctionType.DeadEnd;
                case 1: return JunctionType.DeadEnd;
                case 3: return JunctionType.T_Junction;
                case 4: return JunctionType.Crossroad;
                case 2:
                    bool n = grid.IsOpenNorth(r, c);
                    bool s = grid.IsOpenSouth(r, c);
                    bool e = grid.IsOpenEast(r, c);
                    bool w = grid.IsOpenWest(r, c);
                    if ((n && s) || (e && w)) return JunctionType.Straight;
                    return JunctionType.Corner;
                default: return JunctionType.Crossroad;
            }
        }

        public static JunctionType[,] ClassifyAll(MazeGrid grid)
        {
            var result = new JunctionType[grid.Size, grid.Size];
            for (int r = 0; r < grid.Size; r++)
                for (int c = 0; c < grid.Size; c++)
                    result[r, c] = Classify(grid, r, c);
            return result;
        }

        public static System.Collections.Generic.List<(int r, int c)> FindJunctions(
            MazeGrid grid, JunctionType type)
        {
            var list = new System.Collections.Generic.List<(int, int)>();
            for (int r = 0; r < grid.Size; r++)
                for (int c = 0; c < grid.Size; c++)
                    if (Classify(grid, r, c) == type) list.Add((r, c));
            return list;
        }

        public static bool IsDecisionPoint(JunctionType type) =>
            type == JunctionType.T_Junction || type == JunctionType.Crossroad;
    }
}
