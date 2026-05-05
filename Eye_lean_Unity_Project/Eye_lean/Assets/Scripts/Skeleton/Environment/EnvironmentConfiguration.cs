// SPDX-License-Identifier: MIT
using UnityEngine;

/// <summary>
/// Configuration data for procedural environment generation: dimensions,
/// materials, and lighting. Extend for experiment-specific parameters.
/// </summary>

namespace EyeLean.Skeleton
{
    [System.Serializable]
    public class EnvironmentConfiguration
    {
        #region Dimension Limits

        /// <summary>Minimum supported environment length</summary>
        public const float MIN_LENGTH = 3.0f;

        /// <summary>Maximum supported environment length</summary>
        public const float MAX_LENGTH = 50.0f;

        /// <summary>Minimum supported environment width</summary>
        public const float MIN_WIDTH = 1.5f;

        /// <summary>Maximum supported environment width</summary>
        public const float MAX_WIDTH = 20.0f;

        #endregion

        #region Environment Dimensions

        [Header("Environment Dimensions")]
        [Tooltip("Length of the environment (Z-axis extent)")]
        [Range(MIN_LENGTH, MAX_LENGTH)]
        public float environmentLength = 10.0f;

        [Tooltip("Width of the environment (X-axis extent)")]
        [Range(MIN_WIDTH, MAX_WIDTH)]
        public float environmentWidth = 3.0f;

        [Tooltip("Height of walls/boundaries")]
        [Range(2.0f, 10.0f)]
        public float wallHeight = 3.0f;

        [Tooltip("Thickness of wall elements")]
        public float wallThickness = 0.1f;

        #endregion

        #region Spawn Points

        [Header("Spawn Points")]
        [Tooltip("Where the participant starts")]
        public Vector3 participantSpawnPoint = Vector3.zero;

        [Tooltip("Where agents spawn (if applicable)")]
        public Vector3 agentSpawnPoint = new Vector3(0, 0, 15);

        [Tooltip("Exit/completion trigger position")]
        public Vector3 exitPoint = new Vector3(0, 0, 10);

        #endregion

        #region Lighting

        [Header("Lighting")]
        [Tooltip("Spacing between ceiling lamps in meters")]
        [Range(1.0f, 10.0f)]
        public float lampSpacing = 3.0f;

        [Tooltip("Height of ceiling lamps")]
        [Range(2.0f, 10.0f)]
        public float lampHeight = 3.5f;

        [Tooltip("Scale for ceiling lamp prefabs")]
        [Range(0.1f, 3.0f)]
        public float lampScale = 1.0f;

        [Tooltip("Enable real-time lights on lamps (performance cost)")]
        public bool enableLampLights = false;

        [Tooltip("Ambient light color")]
        public Color ambientLightColor = new Color(0.5f, 0.5f, 0.55f);

        [Tooltip("Ambient light intensity")]
        [Range(0.5f, 2.0f)]
        public float ambientIntensity = 1.0f;

        #endregion

        #region Materials

        [Header("Materials (Optional - uses defaults if null)")]
        [Tooltip("Floor material")]
        public Material floorMaterial;

        [Tooltip("Wall/boundary material")]
        public Material wallMaterial;

        [Tooltip("Ceiling material")]
        public Material ceilingMaterial;

        #endregion

        #region Validation

        /// <summary>Clamp configuration values to valid ranges.</summary>
        public virtual void Validate()
        {
            environmentLength = Mathf.Clamp(environmentLength, MIN_LENGTH, MAX_LENGTH);
            environmentWidth = Mathf.Clamp(environmentWidth, MIN_WIDTH, MAX_WIDTH);
            wallHeight = Mathf.Clamp(wallHeight, 2.0f, 10.0f);
            lampSpacing = Mathf.Clamp(lampSpacing, 1.0f, 10.0f);
            ambientIntensity = Mathf.Clamp(ambientIntensity, 0.5f, 2.0f);
        }

        /// <summary>Create a default configuration with standard values.</summary>
        public static EnvironmentConfiguration CreateDefault()
        {
            return new EnvironmentConfiguration
            {
                environmentLength = 10.0f,
                environmentWidth = 3.0f,
                wallHeight = 3.0f,
                wallThickness = 0.1f,
                participantSpawnPoint = Vector3.zero,
                agentSpawnPoint = new Vector3(0, 0, 15),
                exitPoint = new Vector3(0, 0, 10),
                lampSpacing = 3.0f,
                lampHeight = 3.5f,
                lampScale = 1.0f,
                enableLampLights = false,
                ambientLightColor = new Color(0.5f, 0.5f, 0.55f),
                ambientIntensity = 1.0f
            };
        }

        #endregion
    }
}
