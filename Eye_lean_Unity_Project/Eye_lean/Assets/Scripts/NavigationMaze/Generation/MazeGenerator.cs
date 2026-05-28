// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using UnityEngine;
using Navlab.ExperimentRuntime;

namespace EyeLean.NavigationMaze.Generation
{
    public static class MazeGenerator
    {
        public static MazeGrid Generate(int gridSize, float cellSize, int seed, int wallRemovalCount)
        {
            var grid = new MazeGrid(gridSize, cellSize);
            var rng = new System.Random(seed);
            CarveDFS(grid, rng);
            RemoveRandomWalls(grid, wallRemovalCount, rng);
            return grid;
        }

        public static void WriteToEnvironmentConfig(
            MazeGrid grid, EnvironmentConfig env,
            SpawnGoalPlacement placement, int seed,
            float wallThickness = 0.1f)
        {
            float totalSize = grid.Size * grid.CellSize;
            env.boundsMin = Vector2.zero;
            env.boundsMax = new Vector2(totalSize, totalSize);
            env.walls.Clear();
            env.obstacles.Clear();
            env.spawnPoints.Clear();
            env.goals.Clear();

            AddPerimeterWalls(env, totalSize, wallThickness);
            AddInternalWalls(grid, env, wallThickness);
            PlaceSpawnAndGoal(grid, env, placement, seed);
        }

        private static void CarveDFS(MazeGrid grid, System.Random rng)
        {
            int size = grid.Size;
            var visited = new bool[size, size];
            var stack = new Stack<(int r, int c)>();

            int startR = rng.Next(size);
            int startC = rng.Next(size);
            visited[startR, startC] = true;
            stack.Push((startR, startC));

            while (stack.Count > 0)
            {
                var (r, c) = stack.Peek();
                var neighbors = GetUnvisitedNeighbors(r, c, size, visited);
                if (neighbors.Count == 0)
                {
                    stack.Pop();
                    continue;
                }
                var (nr, nc) = neighbors[rng.Next(neighbors.Count)];
                RemoveWallBetween(grid, r, c, nr, nc);
                visited[nr, nc] = true;
                stack.Push((nr, nc));
            }
        }

        private static List<(int r, int c)> GetUnvisitedNeighbors(
            int r, int c, int size, bool[,] visited)
        {
            var list = new List<(int, int)>(4);
            if (r > 0 && !visited[r - 1, c]) list.Add((r - 1, c));
            if (r < size - 1 && !visited[r + 1, c]) list.Add((r + 1, c));
            if (c > 0 && !visited[r, c - 1]) list.Add((r, c - 1));
            if (c < size - 1 && !visited[r, c + 1]) list.Add((r, c + 1));
            return list;
        }

        private static void RemoveWallBetween(MazeGrid grid, int r1, int c1, int r2, int c2)
        {
            if (r1 == r2)
            {
                int minC = System.Math.Min(c1, c2);
                grid.RemoveVerticalWall(r1, minC);
            }
            else
            {
                int minR = System.Math.Min(r1, r2);
                grid.RemoveHorizontalWall(minR, c1);
            }
        }

        private static void RemoveRandomWalls(MazeGrid grid, int count, System.Random rng)
        {
            if (count <= 0) return;
            var walls = new List<(bool isH, int r, int c)>();
            for (int r = 0; r < grid.Size - 1; r++)
                for (int c = 0; c < grid.Size; c++)
                    if (grid.HasHorizontalWall(r, c))
                        walls.Add((true, r, c));
            for (int r = 0; r < grid.Size; r++)
                for (int c = 0; c < grid.Size - 1; c++)
                    if (grid.HasVerticalWall(r, c))
                        walls.Add((false, r, c));

            int toRemove = System.Math.Min(count, walls.Count);
            for (int i = 0; i < toRemove; i++)
            {
                int j = rng.Next(i, walls.Count);
                (walls[i], walls[j]) = (walls[j], walls[i]);
                var (isH, r, c) = walls[i];
                if (isH) grid.RemoveHorizontalWall(r, c);
                else grid.RemoveVerticalWall(r, c);
            }
        }

        private static void AddPerimeterWalls(EnvironmentConfig env, float size, float t)
        {
            env.walls.Add(new WallSegment { startXZ = new Vector2(0, 0), endXZ = new Vector2(size, 0), thickness = t });
            env.walls.Add(new WallSegment { startXZ = new Vector2(0, size), endXZ = new Vector2(size, size), thickness = t });
            env.walls.Add(new WallSegment { startXZ = new Vector2(0, 0), endXZ = new Vector2(0, size), thickness = t });
            env.walls.Add(new WallSegment { startXZ = new Vector2(size, 0), endXZ = new Vector2(size, size), thickness = t });
        }

        private static void AddInternalWalls(MazeGrid grid, EnvironmentConfig env, float t)
        {
            float cs = grid.CellSize;
            for (int r = 0; r < grid.Size - 1; r++)
            {
                for (int c = 0; c < grid.Size; c++)
                {
                    if (grid.HasHorizontalWall(r, c))
                    {
                        float y = (r + 1) * cs;
                        env.walls.Add(new WallSegment
                        {
                            startXZ = new Vector2(c * cs, y),
                            endXZ = new Vector2((c + 1) * cs, y),
                            thickness = t
                        });
                    }
                }
            }
            for (int r = 0; r < grid.Size; r++)
            {
                for (int c = 0; c < grid.Size - 1; c++)
                {
                    if (grid.HasVerticalWall(r, c))
                    {
                        float x = (c + 1) * cs;
                        env.walls.Add(new WallSegment
                        {
                            startXZ = new Vector2(x, r * cs),
                            endXZ = new Vector2(x, (r + 1) * cs),
                            thickness = t
                        });
                    }
                }
            }
        }

        private static void PlaceSpawnAndGoal(
            MazeGrid grid, EnvironmentConfig env,
            SpawnGoalPlacement placement, int seed)
        {
            var rng = new System.Random(seed);
            int last = grid.Size - 1;
            int sr, sc, gr, gc;

            switch (placement)
            {
                case SpawnGoalPlacement.AdjacentCorners:
                    sr = 0; sc = 0; gr = 0; gc = last;
                    break;
                case SpawnGoalPlacement.RandomCells:
                    sr = rng.Next(grid.Size); sc = rng.Next(grid.Size);
                    do { gr = rng.Next(grid.Size); gc = rng.Next(grid.Size); }
                    while (gr == sr && gc == sc);
                    break;
                case SpawnGoalPlacement.CenterPerimeter:
                    sr = grid.Size / 2; sc = grid.Size / 2;
                    gr = 0; gc = 0;
                    break;
                default: // DiagonalCorners
                    sr = 0; sc = 0; gr = last; gc = last;
                    break;
            }

            env.spawnPoints.Add(new NamedPoint
            {
                name = "S_human",
                positionXZ = grid.CellCenter(sr, sc),
                headingDeg = 0f
            });
            env.spawnPoints.Add(new NamedPoint
            {
                name = "S_npc",
                positionXZ = grid.CellCenter(sr, last - sc),
                headingDeg = 0f
            });
            env.goals.Add(new NamedPoint
            {
                name = "G_human",
                positionXZ = grid.CellCenter(gr, gc),
                headingDeg = 180f
            });
            env.goals.Add(new NamedPoint
            {
                name = "G_npc",
                positionXZ = grid.CellCenter(gr, last - gc),
                headingDeg = 180f
            });
        }
    }
}
