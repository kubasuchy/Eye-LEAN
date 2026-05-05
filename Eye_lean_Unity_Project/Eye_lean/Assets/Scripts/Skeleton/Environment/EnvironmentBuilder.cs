// SPDX-License-Identifier: MIT
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Procedural builder for floor, walls, fences, lamps, lighting, and exit
/// trigger. Mobile-VR optimized: simple materials, no shadows, ambient
/// lighting only. Override <see cref="CreateCustomElements"/> for
/// experiment-specific props.
/// </summary>

namespace EyeLean.Skeleton
{
    public class EnvironmentBuilder : MonoBehaviour
    {
        #region Configuration

        [Header("Environment Dimensions")]
        [Tooltip("Length of the environment in meters")]
        [Range(3f, 50f)]
        public float environmentLength = 10f;

        [Tooltip("Width of the environment in meters")]
        [Range(1.5f, 20f)]
        public float environmentWidth = 3f;

        [Tooltip("Height of walls/fences")]
        [Range(2f, 10f)]
        public float wallHeight = 3f;

        [Header("Fence Appearance")]
        [Tooltip("Width of fence posts")]
        public float postWidth = 0.08f;

        [Tooltip("Spacing between fence posts")]
        public float postSpacing = 0.25f;

        [Tooltip("Fence color")]
        public Color fenceColor = new Color(0.25f, 0.27f, 0.3f);

        [Header("Lamp Settings")]
        [Tooltip("Spacing between ceiling lamps")]
        public float lampSpacing = 3f;

        [Tooltip("Height of lamps (Y position)")]
        public float lampHeight = 3.5f;

        [Tooltip("Scale of lamp prefabs")]
        public float lampScale = 2.0f;

        [Tooltip("Add real lights to lamps (performance cost)")]
        public bool enableLampLights = false;

        [Tooltip("Light intensity for each lamp")]
        [Range(0.5f, 5f)]
        public float lampLightIntensity = 2.0f;

        [Tooltip("Light range for each lamp")]
        [Range(3f, 15f)]
        public float lampLightRange = 8f;

        [Tooltip("Light color for lamps")]
        public Color lampLightColor = new Color(1f, 0.95f, 0.85f);

        [Header("Ambient Lighting (Mobile VR Optimized)")]
        [Tooltip("Ambient light color")]
        public Color ambientLightColor = new Color(0.7f, 0.7f, 0.75f);

        [Tooltip("Ambient light intensity multiplier")]
        [Range(0.5f, 2.0f)]
        public float ambientIntensity = 1.2f;

        [Tooltip("Add a directional fill light (no shadows)")]
        public bool addFillLight = true;

        [Tooltip("Fill light intensity")]
        [Range(0.3f, 1.5f)]
        public float fillLightIntensity = 0.6f;

        [Header("Materials")]
        [Tooltip("Floor material (optional)")]
        public Material floorMaterial;

        [Tooltip("Wall material (optional)")]
        public Material wallMaterial;

        [Header("Prefabs")]
        [Tooltip("Ceiling lamp prefab (loaded from Resources if not assigned)")]
        public GameObject lampPrefab;

        [Header("Debug")]
        [Tooltip("Enable debug logging")]
        public bool showDebugLogs = false;

        #endregion

        #region Runtime State

        protected GameObject environmentRoot;
        protected GameObject exitTriggerObject;

        /// <summary>Event fired when participant reaches exit</summary>
        public System.Action OnExitReached;

        #endregion

        #region Public Methods

        /// <summary>Build the environment at <paramref name="position"/>. <paramref name="config"/> overrides inspector values.</summary>
        public virtual GameObject Build(Vector3 position, EnvironmentConfiguration config = null)
        {
            if (config != null)
            {
                ApplyConfiguration(config);
            }

            LoadPrefabs();

            if (environmentRoot != null)
            {
                Destroy(environmentRoot);
            }

            environmentRoot = new GameObject("Environment");
            environmentRoot.transform.position = position;

            CreateFloor();
            CreateWalls();
            CreateFences();
            CreateLamps();
            SetupLighting();
            CreateExitTrigger();

            CreateCustomElements();

            if (showDebugLogs)
            {
                Debug.Log($"[EnvironmentBuilder] Environment built at {position}");
                Debug.Log($"[EnvironmentBuilder] Dimensions: {environmentWidth}m x {environmentLength}m x {wallHeight}m");
            }

            return environmentRoot;
        }

        /// <summary>Destroy the environment root.</summary>
        public virtual void Cleanup()
        {
            if (environmentRoot != null)
            {
                Destroy(environmentRoot);
                environmentRoot = null;
            }
            exitTriggerObject = null;

            if (showDebugLogs) Debug.Log("[EnvironmentBuilder] Environment cleaned up");
        }

        #endregion

        #region Protected Methods - Override for Custom Environments

        /// <summary>Hook for experiment-specific elements. Called after the standard elements (floor, walls, fences, lamps) are created.</summary>
        protected virtual void CreateCustomElements()
        {
        }

        /// <summary>Apply configuration values from an <see cref="EnvironmentConfiguration"/>.</summary>
        protected virtual void ApplyConfiguration(EnvironmentConfiguration config)
        {
            environmentLength = config.environmentLength;
            environmentWidth = config.environmentWidth;
            wallHeight = config.wallHeight;
            lampSpacing = config.lampSpacing;
            lampHeight = config.lampHeight;
            lampScale = config.lampScale;
            enableLampLights = config.enableLampLights;
            ambientLightColor = config.ambientLightColor;
            ambientIntensity = config.ambientIntensity;

            if (config.floorMaterial != null) floorMaterial = config.floorMaterial;
            if (config.wallMaterial != null) wallMaterial = config.wallMaterial;
        }

        #endregion

        #region Environment Building

        protected virtual void LoadPrefabs()
        {
            if (lampPrefab == null)
            {
                lampPrefab = Resources.Load<GameObject>("Prefabs/CeilingLamp");
                if (lampPrefab == null && showDebugLogs)
                {
                    Debug.LogWarning("[EnvironmentBuilder] CeilingLamp prefab not found in Resources/Prefabs/");
                }
            }
        }

        protected virtual void CreateFloor()
        {
            var (position, scale) = EnvironmentGeometry.GetFloorTransform(environmentWidth, environmentLength);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(environmentRoot.transform);
            floor.transform.localPosition = position;
            floor.transform.localScale = scale;
            floor.isStatic = true;

            if (floorMaterial != null)
            {
                floor.GetComponent<Renderer>().material = floorMaterial;
            }

            if (showDebugLogs) Debug.Log($"[EnvironmentBuilder] Floor created at {position}, scale {scale}");
        }

        protected virtual void CreateWalls()
        {
            var (leftPos, leftScale) = EnvironmentGeometry.GetLeftWallTransform(
                environmentWidth, environmentLength, wallHeight);

            GameObject leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftWall.name = "LeftWall";
            leftWall.transform.SetParent(environmentRoot.transform);
            leftWall.transform.localPosition = leftPos;
            leftWall.transform.localScale = leftScale;
            leftWall.isStatic = true;

            if (wallMaterial != null)
            {
                leftWall.GetComponent<Renderer>().material = wallMaterial;
            }

            var (rightPos, rightScale) = EnvironmentGeometry.GetRightWallTransform(
                environmentWidth, environmentLength, wallHeight);

            GameObject rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightWall.name = "RightWall";
            rightWall.transform.SetParent(environmentRoot.transform);
            rightWall.transform.localPosition = rightPos;
            rightWall.transform.localScale = rightScale;
            rightWall.isStatic = true;

            if (wallMaterial != null)
            {
                rightWall.GetComponent<Renderer>().material = wallMaterial;
            }

            if (showDebugLogs) Debug.Log($"[EnvironmentBuilder] Walls created");
        }

        // Combined mesh fences keep draw-call count low for mobile VR
        protected virtual void CreateFences()
        {
            float fenceStartZ = 0f;
            float fenceEndZ = environmentLength;
            float fenceLength = fenceEndZ - fenceStartZ;
            float fenceCenterZ = (fenceStartZ + fenceEndZ) / 2f;

            CreateFence(
                new Vector3(-environmentWidth / 2f, wallHeight / 2f, fenceCenterZ),
                fenceLength,
                "LeftFence"
            );

            CreateFence(
                new Vector3(environmentWidth / 2f, wallHeight / 2f, fenceCenterZ),
                fenceLength,
                "RightFence"
            );

            if (showDebugLogs) Debug.Log($"[EnvironmentBuilder] Fences created: length={fenceLength}m");
        }

        protected void CreateFence(Vector3 position, float length, string name)
        {
            GameObject fence = new GameObject(name);
            fence.transform.SetParent(environmentRoot.transform);
            fence.transform.localPosition = position;

            int numPosts = Mathf.RoundToInt(length / postSpacing);
            float actualSpacing = length / numPosts;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int i = 0; i <= numPosts; i++)
            {
                float postZ = (i * actualSpacing) - (length / 2f);
                AddCubeToMesh(vertices, triangles,
                    new Vector3(0, 0, postZ),
                    new Vector3(postWidth, wallHeight, postWidth));
            }

            // Horizontal rails at 30% and 70% height
            float rail1Y = wallHeight * 0.3f - wallHeight / 2f;
            float rail2Y = wallHeight * 0.7f - wallHeight / 2f;

            AddCubeToMesh(vertices, triangles,
                new Vector3(0, rail1Y, 0),
                new Vector3(postWidth, postWidth, length));

            AddCubeToMesh(vertices, triangles,
                new Vector3(0, rail2Y, 0),
                new Vector3(postWidth, postWidth, length));

            Mesh mesh = new Mesh();
            mesh.name = name + "_Mesh";
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter mf = fence.AddComponent<MeshFilter>();
            MeshRenderer mr = fence.AddComponent<MeshRenderer>();
            mf.mesh = mesh;

            // Shader fallback chain: Unlit/Color -> Mobile/Diffuse -> URP Simple Lit
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Mobile/Diffuse");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");

            Material mat = new Material(shader);
            mat.name = name + "_Material";
            mat.color = fenceColor;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", fenceColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", fenceColor);

            mr.material = mat;
            fence.isStatic = true;
        }

        /// <summary>Append cube vertices/triangles to a combined mesh buffer.</summary>
        protected void AddCubeToMesh(List<Vector3> vertices, List<int> triangles, Vector3 center, Vector3 size)
        {
            int vOffset = vertices.Count;
            Vector3 half = size / 2f;

            vertices.Add(center + new Vector3(-half.x, -half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, -half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, half.y, -half.z));
            vertices.Add(center + new Vector3(-half.x, half.y, -half.z));
            vertices.Add(center + new Vector3(-half.x, -half.y, half.z));
            vertices.Add(center + new Vector3(half.x, -half.y, half.z));
            vertices.Add(center + new Vector3(half.x, half.y, half.z));
            vertices.Add(center + new Vector3(-half.x, half.y, half.z));

            int[] cubeTriangles = {
                0, 2, 1, 0, 3, 2,
                5, 6, 4, 6, 7, 4,
                4, 7, 0, 7, 3, 0,
                1, 6, 5, 1, 2, 6,
                3, 6, 2, 3, 7, 6,
                0, 1, 4, 1, 5, 4
            };

            foreach (int t in cubeTriangles)
                triangles.Add(t + vOffset);
        }

        protected virtual void CreateLamps()
        {
            if (lampPrefab == null)
            {
                if (showDebugLogs) Debug.LogWarning("[EnvironmentBuilder] Lamp prefab not assigned");
                return;
            }

            GameObject lampContainer = new GameObject("CeilingLamps");
            lampContainer.transform.SetParent(environmentRoot.transform);
            lampContainer.transform.localPosition = Vector3.zero;

            int lampCount = EnvironmentGeometry.GetLampCount(environmentLength, lampSpacing);

            for (int i = 0; i < lampCount; i++)
            {
                Vector3 lampPos = EnvironmentGeometry.GetLampPosition(i, lampCount, environmentLength, lampHeight);

                GameObject lamp = Instantiate(lampPrefab, lampContainer.transform);
                lamp.name = $"Lamp_{i + 1}";
                lamp.transform.localPosition = lampPos;
                lamp.transform.localRotation = Quaternion.Euler(90, 0, 0);
                lamp.transform.localScale = Vector3.one * lampScale;

                foreach (Collider col in lamp.GetComponentsInChildren<Collider>())
                    Destroy(col);

                if (enableLampLights)
                {
                    Light lampLight = lamp.AddComponent<Light>();
                    lampLight.type = LightType.Point;
                    lampLight.color = lampLightColor;
                    lampLight.intensity = lampLightIntensity;
                    lampLight.range = lampLightRange;
                    lampLight.shadows = LightShadows.None; // disabled for mobile VR
                    lampLight.renderMode = LightRenderMode.Auto;
                }
            }

            if (showDebugLogs) Debug.Log($"[EnvironmentBuilder] Created {lampCount} lamps");
        }

        // Mobile VR: prefer Unity's flat ambient over per-light calculations
        protected virtual void SetupLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            Color finalAmbient = ambientLightColor * ambientIntensity;
            RenderSettings.ambientLight = finalAmbient;
            RenderSettings.ambientSkyColor = finalAmbient;
            RenderSettings.ambientEquatorColor = finalAmbient;
            RenderSettings.ambientGroundColor = finalAmbient * 0.8f;

            if (showDebugLogs) Debug.Log($"[EnvironmentBuilder] Ambient light: color={ambientLightColor}, intensity={ambientIntensity}");

            if (addFillLight)
            {
                GameObject fillLightObj = new GameObject("FillLight");
                fillLightObj.transform.SetParent(environmentRoot.transform);
                fillLightObj.transform.localPosition = new Vector3(0, 5f, environmentLength / 2f);
                fillLightObj.transform.localRotation = Quaternion.Euler(50, 0, 0);

                Light fillLight = fillLightObj.AddComponent<Light>();
                fillLight.type = LightType.Directional;
                fillLight.color = new Color(1f, 0.98f, 0.95f);
                fillLight.intensity = fillLightIntensity;
                fillLight.shadows = LightShadows.None;
                fillLight.renderMode = LightRenderMode.ForceVertex;

                if (showDebugLogs) Debug.Log($"[EnvironmentBuilder] Fill light added: intensity={fillLightIntensity}");
            }

            // Fog can cause depth-perception artifacts in VR
            RenderSettings.fog = false;
        }

        protected virtual void CreateExitTrigger()
        {
            Vector3 exitPos = EnvironmentGeometry.GetExitTriggerPosition(environmentLength);

            exitTriggerObject = new GameObject("ExitTrigger");
            exitTriggerObject.transform.SetParent(environmentRoot.transform);
            exitTriggerObject.transform.localPosition = exitPos;

            Rigidbody rb = exitTriggerObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;

            BoxCollider trigger = exitTriggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(environmentWidth + 1f, 2.5f, 1.0f);
            trigger.center = new Vector3(0, 1.25f, 0);

            ExitTriggerDetector detector = exitTriggerObject.AddComponent<ExitTriggerDetector>();
            detector.Initialize(OnExitReached);

            if (showDebugLogs) Debug.Log($"[EnvironmentBuilder] Exit trigger created at {exitPos}");
        }

        #endregion
    }

    /// <summary>One-shot exit-trigger detector with a 1s arming debounce.</summary>
    public class ExitTriggerDetector : MonoBehaviour
    {
        private System.Action onExitCallback;
        private bool hasTriggered = false;
        private float activationTime;

        public void Initialize(System.Action callback)
        {
            onExitCallback = callback;
            activationTime = Time.time;
            hasTriggered = false;
        }

        void OnTriggerEnter(Collider other)
        {
            if (hasTriggered) return;
            if (Time.time - activationTime < 1.0f) return; // Debounce on spawn

            if (other.CompareTag("Player") || other.CompareTag("MainCamera") ||
                other.GetComponent<Camera>() != null)
            {
                hasTriggered = true;
                onExitCallback?.Invoke();
            }
        }
    }
}
