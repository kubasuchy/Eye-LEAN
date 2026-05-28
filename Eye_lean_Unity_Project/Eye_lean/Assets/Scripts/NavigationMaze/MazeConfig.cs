// SPDX-License-Identifier: MIT
using UnityEngine;

namespace EyeLean.NavigationMaze
{
    public enum MazeTrialMode
    {
        Exploration,
        Wayfinding,
        Sequential,
        Competitive,
        Probe
    }

    public enum SpawnGoalPlacement
    {
        DiagonalCorners,
        AdjacentCorners,
        RandomCells,
        CenterPerimeter
    }

    public enum LandmarkCondition
    {
        Distal,
        Proximal,
        None
    }

    [System.Serializable]
    public class DistalLandmarkConfig
    {
        public string name = "North";
        public PrimitiveType shape = PrimitiveType.Sphere;
        public Color color = Color.red;
    }

    [System.Serializable]
    public class MazeBlockConfig
    {
        public MazeTrialMode mode = MazeTrialMode.Wayfinding;
        [Tooltip("Wall-removal count k. 0 = perfect maze (hardest), higher = more shortcuts.")]
        public int difficulty;
        public bool ceilingEnabled;
        public LandmarkCondition landmarkCondition = LandmarkCondition.Distal;
        public bool npcEnabled;
        public int trialsInBlock = 4;
        public SpawnGoalPlacement spawnGoalPlacement = SpawnGoalPlacement.DiagonalCorners;
        public bool goalVisible = true;
        public int sequentialGoalCount = 1;
        public bool reusesPreviousBlockMaze;
        [Tooltip("-1 = derive from experiment randomSeed + block index.")]
        public int mazeSeed = -1;
        public float trialDurationSeconds = 60f;
    }

    [CreateAssetMenu(menuName = "Eye_lean/Maze/Maze Config", fileName = "MazeConfig")]
    public class MazeConfig : ScriptableObject
    {
        [Header("Grid")]
        public int gridSize = 5;
        public float corridorWidth = 2.0f;
        public float wallHeight = 2.4f;
        public float wallThickness = 0.1f;

        [Header("Landmarks")]
        public DistalLandmarkConfig[] distalLandmarks = new[]
        {
            new DistalLandmarkConfig { name = "North", shape = PrimitiveType.Sphere, color = Color.red },
            new DistalLandmarkConfig { name = "East", shape = PrimitiveType.Cube, color = Color.blue },
            new DistalLandmarkConfig { name = "South", shape = PrimitiveType.Capsule, color = Color.green },
            new DistalLandmarkConfig { name = "West", shape = PrimitiveType.Cylinder, color = Color.yellow },
        };
        public string[] proximalLandmarkPool = new[] { "Clock", "Painting", "Plant", "Sign", "Trophy", "Globe" };
        public int maxProximalLandmarks = 6;
        public float distalLandmarkHeight = 5.0f;
        public float distalLandmarkRadius = 0.4f;
        public float proximalLandmarkHeight = 1.6f;

        [Header("Blocks")]
        public MazeBlockConfig[] blocks = new[] { new MazeBlockConfig() };

        [Header("Provenance")]
        public string sourceCitation;

        public int TotalTrials
        {
            get
            {
                if (blocks == null) return 0;
                int total = 0;
                foreach (var b in blocks) total += b.trialsInBlock;
                return total;
            }
        }

        public string ToSummary() =>
            $"Maze {gridSize}x{gridSize}, {blocks.Length} blocks, {TotalTrials} trials, corridorWidth={corridorWidth}m";
    }
}
