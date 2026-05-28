// SPDX-License-Identifier: MIT
using NUnit.Framework;
using EyeLean.NavigationMaze.Generation;

namespace EyeLean.NavigationMaze.Tests
{
    public class MazeCellClassifierTests
    {
        [Test]
        public void DeadEnd_SingleOpening()
        {
            var grid = new MazeGrid(3, 2f);
            grid.RemoveHorizontalWall(0, 0);
            Assert.AreEqual(JunctionType.DeadEnd, MazeCellClassifier.Classify(grid, 0, 0));
        }

        [Test]
        public void Straight_OppositeOpenings()
        {
            var grid = new MazeGrid(3, 2f);
            grid.RemoveHorizontalWall(0, 1);
            grid.RemoveHorizontalWall(1, 1);
            Assert.AreEqual(JunctionType.Straight, MazeCellClassifier.Classify(grid, 1, 1));
        }

        [Test]
        public void Corner_AdjacentOpenings()
        {
            var grid = new MazeGrid(3, 2f);
            grid.RemoveHorizontalWall(0, 0);
            grid.RemoveVerticalWall(0, 0);
            Assert.AreEqual(JunctionType.Corner, MazeCellClassifier.Classify(grid, 0, 0));
        }

        [Test]
        public void TJunction_ThreeOpenings()
        {
            var grid = new MazeGrid(3, 2f);
            grid.RemoveHorizontalWall(0, 1);
            grid.RemoveVerticalWall(1, 0);
            grid.RemoveVerticalWall(1, 1);
            Assert.AreEqual(JunctionType.T_Junction, MazeCellClassifier.Classify(grid, 1, 1));
        }

        [Test]
        public void Crossroad_FourOpenings()
        {
            var grid = new MazeGrid(3, 2f);
            grid.RemoveHorizontalWall(0, 1);
            grid.RemoveHorizontalWall(1, 1);
            grid.RemoveVerticalWall(1, 0);
            grid.RemoveVerticalWall(1, 1);
            Assert.AreEqual(JunctionType.Crossroad, MazeCellClassifier.Classify(grid, 1, 1));
        }

        [Test]
        public void FindJunctions_ReturnsCorrectCells()
        {
            var grid = MazeGenerator.Generate(5, 2f, seed: 42, wallRemovalCount: 0);
            var deadEnds = MazeCellClassifier.FindJunctions(grid, JunctionType.DeadEnd);
            Assert.IsTrue(deadEnds.Count > 0, "A perfect 5x5 maze should have dead ends");
        }
    }
}
