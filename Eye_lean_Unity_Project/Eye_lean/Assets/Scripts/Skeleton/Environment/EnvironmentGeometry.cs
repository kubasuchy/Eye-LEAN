// SPDX-License-Identifier: MIT
using UnityEngine;

/// <summary>
/// Pure-function helpers for procedural environment geometry (floor, ceiling,
/// walls, lamps, spawn points, bounds). World convention: floor at Y=0,
/// environment extends along +Z, centered on X=0.
/// </summary>

namespace EyeLean.Skeleton
{
    public static class EnvironmentGeometry
    {
        #region Floor and Ceiling

        /// <summary>Floor transform (position + scale). Floor sits at Y=0 with slight thickness.</summary>
        public static (Vector3 position, Vector3 scale) GetFloorTransform(float width, float length, float originZ = 0f)
        {
            float centerZ = originZ + (length / 2f);
            Vector3 position = new Vector3(0f, -0.05f, centerZ);
            Vector3 scale = new Vector3(width, 0.1f, length);
            return (position, scale);
        }

        /// <summary>Ceiling transform (position + scale).</summary>
        public static (Vector3 position, Vector3 scale) GetCeilingTransform(float width, float length, float height, float originZ = 0f)
        {
            float centerZ = originZ + (length / 2f);
            Vector3 position = new Vector3(0f, height, centerZ);
            Vector3 scale = new Vector3(width, 0.1f, length);
            return (position, scale);
        }

        #endregion

        #region Walls

        /// <summary>Left wall transform (negative X side).</summary>
        public static (Vector3 position, Vector3 scale) GetLeftWallTransform(
            float width, float length, float height, float thickness = 0.1f, float originZ = 0f)
        {
            float centerZ = originZ + (length / 2f);
            Vector3 position = new Vector3(-width / 2f, height / 2f, centerZ);
            Vector3 scale = new Vector3(thickness, height, length);
            return (position, scale);
        }

        /// <summary>Right wall transform (positive X side).</summary>
        public static (Vector3 position, Vector3 scale) GetRightWallTransform(
            float width, float length, float height, float thickness = 0.1f, float originZ = 0f)
        {
            float centerZ = originZ + (length / 2f);
            Vector3 position = new Vector3(width / 2f, height / 2f, centerZ);
            Vector3 scale = new Vector3(thickness, height, length);
            return (position, scale);
        }

        /// <summary>Back wall transform (at Z origin, facing forward).</summary>
        public static (Vector3 position, Vector3 scale) GetBackWallTransform(
            float width, float height, float thickness = 0.1f, float originZ = 0f)
        {
            Vector3 position = new Vector3(0f, height / 2f, originZ);
            Vector3 scale = new Vector3(width, height, thickness);
            return (position, scale);
        }

        /// <summary>Front wall transform (at Z = originZ + length, facing backward).</summary>
        public static (Vector3 position, Vector3 scale) GetFrontWallTransform(
            float width, float length, float height, float thickness = 0.1f, float originZ = 0f)
        {
            Vector3 position = new Vector3(0f, height / 2f, originZ + length);
            Vector3 scale = new Vector3(width, height, thickness);
            return (position, scale);
        }

        #endregion

        #region Lighting

        /// <summary>Number of lamps for the given length and spacing (minimum 1).</summary>
        public static int GetLampCount(float length, float spacing)
        {
            return Mathf.Max(1, Mathf.RoundToInt(length / spacing));
        }

        /// <summary>Position of a specific lamp; lamps are evenly distributed along the environment length.</summary>
        public static Vector3 GetLampPosition(int lampIndex, int totalLamps, float length, float height, float originZ = 0f)
        {
            float actualSpacing = length / (totalLamps + 1);
            float lampZ = originZ + actualSpacing * (lampIndex + 1);
            return new Vector3(0f, height, lampZ);
        }

        #endregion

        #region Spawn Points

        /// <summary>Spawn point at <paramref name="distanceFromOrigin"/> along Z, with optional X/Y offsets.</summary>
        public static Vector3 GetSpawnPoint(float distanceFromOrigin, float xOffset = 0f, float yOffset = 0f)
        {
            return new Vector3(xOffset, yOffset, distanceFromOrigin);
        }

        #endregion

        #region Bounds and Triggers

        /// <summary>Bounds encapsulating the entire environment.</summary>
        public static Bounds GetEnvironmentBounds(float width, float length, float height, float originZ = 0f)
        {
            Vector3 center = new Vector3(0f, height / 2f, originZ + length / 2f);
            Vector3 size = new Vector3(width, height, length);
            return new Bounds(center, size);
        }

        /// <summary>Exit-trigger position at the far end of the environment (positive offset moves it inside).</summary>
        public static Vector3 GetExitTriggerPosition(float length, float triggerOffset = 1f, float originZ = 0f)
        {
            return new Vector3(0f, 0f, originZ + length - triggerOffset);
        }

        #endregion

        #region Debug Helpers

        /// <summary>Log derived positions for debugging.</summary>
        public static void LogPositions(EnvironmentConfiguration config)
        {
            Debug.Log($"[EnvironmentGeometry] === Position Summary ===");
            Debug.Log($"  Dimensions: {config.environmentWidth}m x {config.environmentLength}m x {config.wallHeight}m");

            var floor = GetFloorTransform(config.environmentWidth, config.environmentLength);
            Debug.Log($"  Floor: pos={floor.position}, scale={floor.scale}");

            var ceiling = GetCeilingTransform(config.environmentWidth, config.environmentLength, config.wallHeight);
            Debug.Log($"  Ceiling: pos={ceiling.position}, scale={ceiling.scale}");

            int lampCount = GetLampCount(config.environmentLength, config.lampSpacing);
            Debug.Log($"  Lamps: {lampCount} lamps at {config.lampSpacing}m spacing");

            Debug.Log($"  Exit: {GetExitTriggerPosition(config.environmentLength)}");
        }

        #endregion
    }
}
