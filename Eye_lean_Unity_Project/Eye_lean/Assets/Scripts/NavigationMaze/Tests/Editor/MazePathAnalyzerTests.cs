// SPDX-License-Identifier: MIT
using NUnit.Framework;
using EyeLean.NavigationMaze.Generation;

namespace EyeLean.NavigationMaze.Tests
{
    public class MazePathAnalyzerTests
    {
        [Test]
        public void BFS_SameCell_ReturnsSingleCell()
        {
            var grid = MazeGenerator.Generate(5, 2f, seed: 42, wallRemovalCount: 0);
            var path = MazePathAnalyzer.BFS(grid, 0, 0, 0, 0);
            Assert.IsNotNull(path);
            Assert.AreEqual(1, path.Count);
        }

        [Test]
        public void BFS_ConnectedCells_FindsPath()
        {
            var grid = MazeGenerator.Generate(5, 2f, seed: 42, wallRemovalCount: 0);
            var path = MazePathAnalyzer.BFS(grid, 0, 0, 4, 4);
            Assert.IsNotNull(path, "BFS should find a path in a connected maze");
            Assert.AreEqual((0, 0), path[0]);
            Assert.AreEqual((4, 4), path[path.Count - 1]);
        }

        [Test]
        public void BFS_PathIsOptimal()
        {
            var grid = new MazeGrid(3, 2f);
            grid.RemoveHorizontalWall(0, 0);
            grid.RemoveVerticalWall(1, 0);
            grid.RemoveVerticalWall(1, 1);
            grid.RemoveVerticalWall(0, 0);
            grid.RemoveVerticalWall(0, 1);
            grid.RemoveHorizontalWall(0, 2);

            var path = MazePathAnalyzer.BFS(grid, 0, 0, 1, 2);
            Assert.AreEqual(4, path.Count);
        }

        [Test]
        public void ComputeOptimalPath_CorrectLength()
        {
            var grid = MazeGenerator.Generate(5, 2f, seed: 42, wallRemovalCount: 0);
            var metrics = MazePathAnalyzer.ComputeOptimalPath(grid, 0, 0, 4, 4);
            Assert.IsTrue(metrics.optimalPathLength > 0);
            Assert.AreEqual(
                (metrics.optimalPathCellCount - 1) * 2f,
                metrics.optimalPathLength, 0.001f);
        }

        [Test]
        public void ComputeOptimalPath_CountsDecisionPoints()
        {
            var grid = MazeGenerator.Generate(5, 2f, seed: 42, wallRemovalCount: 3);
            var metrics = MazePathAnalyzer.ComputeOptimalPath(grid, 0, 0, 4, 4);
            Assert.IsTrue(metrics.decisionPointsOnOptimalPath >= 0);
        }
    }
}
