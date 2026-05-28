// SPDX-License-Identifier: MIT
using UnityEngine;
using Navlab.ExperimentRuntime;

namespace EyeLean.NavigationMaze.Generation
{
    [CreateAssetMenu(menuName = "Eye_lean/Maze/Procedural Maze Delegate", fileName = "ProceduralMazeDelegate")]
    public class ProceduralMazeDelegate : ProceduralEnvironmentDelegate
    {
        [System.NonSerialized] public int gridSize = 5;
        [System.NonSerialized] public float cellSize = 2f;
        [System.NonSerialized] public int wallRemovalCount;
        [System.NonSerialized] public float wallThickness = 0.1f;
        [System.NonSerialized] public SpawnGoalPlacement placement = SpawnGoalPlacement.DiagonalCorners;

        public MazeGrid LastGrid { get; private set; }

        public override string DelegateId => "eyelean_procedural_maze";

        public override void Generate(EnvironmentConfig env, int randomSeed)
        {
            LastGrid = MazeGenerator.Generate(gridSize, cellSize, randomSeed, wallRemovalCount);
            MazeGenerator.WriteToEnvironmentConfig(LastGrid, env, placement, randomSeed, wallThickness);
        }

        public void Configure(MazeConfig config, MazeBlockConfig block)
        {
            gridSize = config.gridSize;
            cellSize = config.corridorWidth;
            wallThickness = config.wallThickness;
            wallRemovalCount = block.difficulty;
            placement = block.spawnGoalPlacement;
        }
    }
}
