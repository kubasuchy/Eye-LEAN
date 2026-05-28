// SPDX-License-Identifier: MIT
using UnityEngine;

namespace EyeLean.NavigationMaze.Generation
{
    public sealed class MazeGrid
    {
        public readonly int Size;
        public readonly float CellSize;

        // horizontalWalls[r, c] = wall between cell (r,c) and cell (r+1,c)
        private readonly bool[,] _hWalls;
        // verticalWalls[r, c] = wall between cell (r,c) and cell (r,c+1)
        private readonly bool[,] _vWalls;

        public MazeGrid(int size, float cellSize)
        {
            Size = size;
            CellSize = cellSize;
            _hWalls = new bool[size - 1, size];
            _vWalls = new bool[size, size - 1];

            for (int r = 0; r < size - 1; r++)
                for (int c = 0; c < size; c++)
                    _hWalls[r, c] = true;
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size - 1; c++)
                    _vWalls[r, c] = true;
        }

        public bool HasHorizontalWall(int r, int c) => _hWalls[r, c];
        public bool HasVerticalWall(int r, int c) => _vWalls[r, c];
        public void RemoveHorizontalWall(int r, int c) => _hWalls[r, c] = false;
        public void RemoveVerticalWall(int r, int c) => _vWalls[r, c] = false;

        public bool IsOpenNorth(int r, int c) => r < Size - 1 && !_hWalls[r, c];
        public bool IsOpenSouth(int r, int c) => r > 0 && !_hWalls[r - 1, c];
        public bool IsOpenEast(int r, int c) => c < Size - 1 && !_vWalls[r, c];
        public bool IsOpenWest(int r, int c) => c > 0 && !_vWalls[r, c - 1];

        public int OpeningCount(int r, int c)
        {
            int n = 0;
            if (IsOpenNorth(r, c)) n++;
            if (IsOpenSouth(r, c)) n++;
            if (IsOpenEast(r, c)) n++;
            if (IsOpenWest(r, c)) n++;
            return n;
        }

        public Vector2 CellCenter(int r, int c) =>
            new Vector2((c + 0.5f) * CellSize, (r + 0.5f) * CellSize);

        public (int r, int c) WorldToCell(float x, float z)
        {
            int col = Mathf.Clamp(Mathf.FloorToInt(x / CellSize), 0, Size - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(z / CellSize), 0, Size - 1);
            return (row, col);
        }

        public int InternalWallCount()
        {
            int n = 0;
            for (int r = 0; r < Size - 1; r++)
                for (int c = 0; c < Size; c++)
                    if (_hWalls[r, c]) n++;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size - 1; c++)
                    if (_vWalls[r, c]) n++;
            return n;
        }

        public bool CanReach(int r1, int c1, int r2, int c2)
        {
            if (r1 == r2 && c1 == c2) return true;
            var visited = new bool[Size, Size];
            var stack = new System.Collections.Generic.Stack<(int, int)>();
            visited[r1, c1] = true;
            stack.Push((r1, c1));
            while (stack.Count > 0)
            {
                var (r, c) = stack.Pop();
                foreach (var (nr, nc) in OpenNeighbors(r, c))
                {
                    if (nr == r2 && nc == c2) return true;
                    if (!visited[nr, nc])
                    {
                        visited[nr, nc] = true;
                        stack.Push((nr, nc));
                    }
                }
            }
            return false;
        }

        public System.Collections.Generic.List<(int r, int c)> OpenNeighbors(int r, int c)
        {
            var list = new System.Collections.Generic.List<(int, int)>(4);
            if (IsOpenNorth(r, c)) list.Add((r + 1, c));
            if (IsOpenSouth(r, c)) list.Add((r - 1, c));
            if (IsOpenEast(r, c)) list.Add((r, c + 1));
            if (IsOpenWest(r, c)) list.Add((r, c - 1));
            return list;
        }
    }
}
