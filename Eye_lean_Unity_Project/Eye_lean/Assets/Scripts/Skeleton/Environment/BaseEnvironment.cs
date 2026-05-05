// SPDX-License-Identifier: MIT
using UnityEngine;

/// <summary>
/// Lifecycle coordinator for procedural trial environments. Owns generate / clear /
/// exit-detection; delegates construction to <see cref="EnvironmentBuilder"/>.
/// Subclass or use the builder directly for experiment-specific environments.
/// </summary>

namespace EyeLean.Skeleton
{
    public class BaseEnvironment : MonoBehaviour
    {
        #region Configuration

        [Header("Environment Configuration")]
        [SerializeField]
        protected EnvironmentConfiguration configuration;

        [Header("Debug")]
        public bool showDebugGizmos = false;
        public bool logPositions = false;

        #endregion

        #region Public Properties

        /// <summary>World position of environment origin (participant spawn)</summary>
        public Vector3 EnvironmentOrigin { get; protected set; }

        /// <summary>Position where agents spawn</summary>
        public Vector3 AgentSpawnPosition { get; protected set; }

        /// <summary>Current environment length</summary>
        public float CurrentLength { get; protected set; }

        /// <summary>Current environment width</summary>
        public float CurrentWidth
        {
            get => configuration?.environmentWidth ?? 3f;
            set
            {
                if (configuration != null)
                    configuration.environmentWidth = value;
            }
        }

        /// <summary>Whether environment is currently active</summary>
        public bool IsEnvironmentActive { get; protected set; }

        /// <summary>Access to full configuration</summary>
        public EnvironmentConfiguration Configuration => configuration;

        #endregion

        #region Events

        /// <summary>Event fired when participant reaches the exit</summary>
        public System.Action OnExitReached;

        #endregion

        #region Private State

        protected GameObject environmentRoot;
        protected EnvironmentBuilder builder;

        #endregion

        #region Unity Lifecycle

        protected virtual void Awake()
        {
            if (logPositions) Debug.Log($"[BaseEnvironment] Awake on {gameObject.name}");

            if (configuration == null)
            {
                configuration = EnvironmentConfiguration.CreateDefault();
            }

            builder = GetComponent<EnvironmentBuilder>();
            if (builder == null)
            {
                builder = gameObject.AddComponent<EnvironmentBuilder>();
            }

            builder.OnExitReached = HandleExitReached;
        }

        protected virtual void OnDestroy()
        {
            if (logPositions) Debug.Log($"[BaseEnvironment] OnDestroy");
            ClearEnvironment();
        }

        #endregion

        #region Public API

        /// <summary>Generate the trial environment with the given <paramref name="length"/> and world <paramref name="originPosition"/>.</summary>
        public virtual void GenerateEnvironment(float length, Vector3 originPosition)
        {
            if (logPositions)
            {
                Debug.Log("========================================");
                Debug.Log("[BaseEnvironment] GENERATE ENVIRONMENT - START");
                Debug.Log($"Input: length={length}, origin={originPosition}");
                Debug.Log("========================================");
            }

            configuration.environmentLength = Mathf.Clamp(length,
                EnvironmentConfiguration.MIN_LENGTH,
                EnvironmentConfiguration.MAX_LENGTH);
            configuration.Validate();

            CurrentLength = configuration.environmentLength;
            EnvironmentOrigin = originPosition;

            ClearEnvironment();

            BuildEnvironment();

            IsEnvironmentActive = true;

            if (logPositions)
            {
                Debug.Log("========================================");
                Debug.Log("[BaseEnvironment] GENERATE ENVIRONMENT - COMPLETE");
                Debug.Log($"Environment: {CurrentLength}m at {originPosition}");
                Debug.Log("========================================");
            }
        }

        /// <summary>Destroy all environment objects.</summary>
        public virtual void ClearEnvironment()
        {
            if (logPositions) Debug.Log("[BaseEnvironment] ClearEnvironment");

            IsEnvironmentActive = false;

            if (builder != null)
            {
                builder.Cleanup();
            }

            if (environmentRoot != null)
            {
                Destroy(environmentRoot);
                environmentRoot = null;
            }
        }

        /// <summary>Container transform researchers can parent custom props onto.</summary>
        public Transform GetEnvironmentTransform()
        {
            return environmentRoot?.transform;
        }

        /// <summary>Environment bounds (in environment-local space) for agent constraints.</summary>
        public (float minX, float maxX, float minZ, float maxZ) GetEnvironmentBounds()
        {
            float halfWidth = configuration.environmentWidth / 2f;
            return (
                -halfWidth,
                halfWidth,
                0f,
                CurrentLength
            );
        }

        #endregion

        #region Environment Building

        protected virtual void BuildEnvironment()
        {
            if (logPositions) Debug.Log("[BaseEnvironment] Building environment...");

            builder.environmentLength = configuration.environmentLength;
            builder.environmentWidth = configuration.environmentWidth;
            builder.wallHeight = configuration.wallHeight;
            builder.lampSpacing = configuration.lampSpacing;
            builder.lampHeight = configuration.lampHeight;
            builder.lampScale = configuration.lampScale;
            builder.enableLampLights = configuration.enableLampLights;
            builder.ambientLightColor = configuration.ambientLightColor;
            builder.ambientIntensity = configuration.ambientIntensity;

            if (configuration.floorMaterial != null)
                builder.floorMaterial = configuration.floorMaterial;
            if (configuration.wallMaterial != null)
                builder.wallMaterial = configuration.wallMaterial;

            environmentRoot = builder.Build(EnvironmentOrigin, configuration);

            AgentSpawnPosition = configuration.agentSpawnPoint;

            if (logPositions)
            {
                Debug.Log($"[BaseEnvironment] Environment built: {configuration.environmentLength}m x {configuration.environmentWidth}m");
                Debug.Log($"[BaseEnvironment] Agent spawn: {AgentSpawnPosition}");
            }
        }

        #endregion

        #region Event Handlers

        protected virtual void HandleExitReached()
        {
            if (logPositions) Debug.Log("[BaseEnvironment] HandleExitReached called");

            if (!IsEnvironmentActive)
            {
                Debug.LogWarning("[BaseEnvironment] Environment not active - exit ignored");
                return;
            }

            if (logPositions) Debug.Log("[BaseEnvironment] Participant reached exit");
            IsEnvironmentActive = false;

            OnExitReached?.Invoke();
        }

        #endregion

        #region Debug Visualization

        protected virtual void OnDrawGizmos()
        {
            if (!showDebugGizmos || !IsEnvironmentActive) return;

            Gizmos.color = Color.cyan;
            float halfWidth = configuration?.environmentWidth ?? 3f;
            halfWidth /= 2f;

            Vector3 corner1 = EnvironmentOrigin + new Vector3(-halfWidth, 0, 0);
            Vector3 corner2 = EnvironmentOrigin + new Vector3(halfWidth, 0, 0);
            Vector3 corner3 = EnvironmentOrigin + new Vector3(halfWidth, 0, CurrentLength);
            Vector3 corner4 = EnvironmentOrigin + new Vector3(-halfWidth, 0, CurrentLength);

            Gizmos.DrawLine(corner1, corner2);
            Gizmos.DrawLine(corner2, corner3);
            Gizmos.DrawLine(corner3, corner4);
            Gizmos.DrawLine(corner4, corner1);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(EnvironmentOrigin + AgentSpawnPosition, 0.5f);

            Gizmos.color = Color.yellow;
            Vector3 exitPos = EnvironmentGeometry.GetExitTriggerPosition(CurrentLength);
            Gizmos.DrawWireSphere(EnvironmentOrigin + exitPos, 1.0f);
        }

        #endregion

        #region Editor Helpers

        protected virtual void Reset()
        {
            if (configuration == null)
            {
                configuration = EnvironmentConfiguration.CreateDefault();
            }
        }

        protected virtual void OnValidate()
        {
            configuration?.Validate();
        }

        #endregion
    }
}
