// SPDX-License-Identifier: MIT
using NUnit.Framework;
using EyeLean.NavigationMaze.Generation;

namespace EyeLean.NavigationMaze.Tests
{
    public class MazeGeneratorTests
    {
        [Test]
        public void Generate_PerfectMaze_AllCellsReachable()
        {
            var grid = MazeGenerator.Generate(5, 2f, seed: 42, wallRemovalCount: 0);
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    Assert.IsTrue(grid.CanReach(0, 0, r, c),
                        $"Cell ({r},{c}) unreachable from (0,0)");
        }

        [Test]
        public void Generate_PerfectMaze_CorrectWallCount()
        {
            int n = 5;
            int initial = 2 * n * (n - 1); // 40
            int removed = n * n - 1;        // 24
            int expected = initial - removed; // 16

            var grid = MazeGenerator.Generate(n, 2f, seed: 42, wallRemovalCount: 0);
            Assert.AreEqual(expected, grid.InternalWallCount());
        }

        [Test]
        public void Generate_WallRemoval_ReducesWallCount()
        {
            var perfect = MazeGenerator.Generate(5, 2f, seed: 42, wallRemovalCount: 0);
            var easier = MazeGenerator.Generate(5, 2f, seed: 42, wallRemovalCount: 3);
            Assert.AreEqual(perfect.InternalWallCount() - 3, easier.InternalWallCount());
        }

        [Test]
        public void Generate_Deterministic_SameSeedSameResult()
        {
            var a = MazeGenerator.Generate(5, 2f, seed: 123, wallRemovalCount: 0);
            var b = MazeGenerator.Generate(5, 2f, seed: 123, wallRemovalCount: 0);
            Assert.AreEqual(a.InternalWallCount(), b.InternalWallCount());
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 5; c++)
                    Assert.AreEqual(a.HasHorizontalWall(r, c), b.HasHorizontalWall(r, c),
                        $"HWall mismatch at ({r},{c})");
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 4; c++)
                    Assert.AreEqual(a.HasVerticalWall(r, c), b.HasVerticalWall(r, c),
                        $"VWall mismatch at ({r},{c})");
        }

        [Test]
        public void Generate_DifferentSeeds_DifferentMazes()
        {
            var a = MazeGenerator.Generate(5, 2f, seed: 1, wallRemovalCount: 0);
            var b = MazeGenerator.Generate(5, 2f, seed: 2, wallRemovalCount: 0);
            bool anyDiff = false;
            for (int r = 0; r < 4 && !anyDiff; r++)
                for (int c = 0; c < 5 && !anyDiff; c++)
                    if (a.HasHorizontalWall(r, c) != b.HasHorizontalWall(r, c))
                        anyDiff = true;
            Assert.IsTrue(anyDiff, "Two different seeds produced identical mazes");
        }

        [Test]
        public void Generate_SmallGrid_3x3_Works()
        {
            var grid = MazeGenerator.Generate(3, 2f, seed: 7, wallRemovalCount: 0);
            Assert.AreEqual(3, grid.Size);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    Assert.IsTrue(grid.CanReach(0, 0, r, c));
        }

        [Test]
        public void CellCenter_ReturnsCorrectPosition()
        {
            var grid = new MazeGrid(5, 2f);
            var center = grid.CellCenter(0, 0);
            Assert.AreEqual(1f, center.x, 0.001f);
            Assert.AreEqual(1f, center.y, 0.001f);

            center = grid.CellCenter(4, 4);
            Assert.AreEqual(9f, center.x, 0.001f);
            Assert.AreEqual(9f, center.y, 0.001f);
        }

        [Test]
        public void WorldToCell_ReturnsCorrectCell()
        {
            var grid = new MazeGrid(5, 2f);
            var (r, c) = grid.WorldToCell(1f, 1f);
            Assert.AreEqual(0, r);
            Assert.AreEqual(0, c);

            (r, c) = grid.WorldToCell(9f, 9f);
            Assert.AreEqual(4, r);
            Assert.AreEqual(4, c);
        }
    }
}
