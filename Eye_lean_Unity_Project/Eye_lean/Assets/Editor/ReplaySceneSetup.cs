#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using EyeLean.Replay;
using EyeLean.Replay.UI;
using EyeLean.Replay.Analysis;
using EyeLean.Replay.Visualization;

namespace EyeLean.Editor
{
    /// <summary>
    /// Editor utility to create and configure the ReplayScene.
    /// Menu: Eye_lean > Create Replay Scene
    /// </summary>
    public static class ReplaySceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/ReplayScene.unity";

        [MenuItem("Eye_lean/Create Replay Scene")]
        public static void CreateReplayScene()
        {
            // Confirm with user
            if (!EditorUtility.DisplayDialog(
                "Create Replay Scene",
                "This will create a new ReplayScene.unity in Assets/Scenes.\n\n" +
                "The scene will contain all components needed for eye tracking data replay and visualization.",
                "Create",
                "Cancel"))
            {
                return;
            }

            // Create new scene
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Build the scene hierarchy
            CreateCamera();
            CreateLighting();
            CreateReplaySystem();
            CreateEnvironment();
            CreateCanvas();

            // Save the scene
            if (!System.IO.Directory.Exists("Assets/Scenes"))
            {
                System.IO.Directory.CreateDirectory("Assets/Scenes");
            }

            EditorSceneManager.SaveScene(newScene, SCENE_PATH);
            AssetDatabase.Refresh();

            Debug.Log($"[ReplaySceneSetup] ReplayScene created at: {SCENE_PATH}");
            EditorUtility.DisplayDialog(
                "Scene Created",
                $"ReplayScene.unity has been created at:\n{SCENE_PATH}\n\n" +
                "To use:\n" +
                "1. Open the scene\n" +
                "2. Select ReplayManager and set CSV file path\n" +
                "3. Enter Play mode\n" +
                "4. Use the UI to load and play data",
                "OK");
        }

        private static void CreateCamera()
        {
            // Create Main Camera with XR support
            var cameraObj = new GameObject("Main Camera");
            cameraObj.tag = "MainCamera";

            var camera = cameraObj.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.fieldOfView = 60f;

            // Add Universal Additional Camera Data for URP
            var cameraData = cameraObj.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;

            // Add AudioListener
            cameraObj.AddComponent<AudioListener>();

            // Position at origin (will be controlled by XR rig or replay)
            cameraObj.transform.position = new Vector3(0, 1.6f, 0);
            cameraObj.transform.rotation = Quaternion.identity;

            Debug.Log("[ReplaySceneSetup] Created Main Camera");
        }

        private static void CreateLighting()
        {
            // Create Directional Light
            var lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;

            lightObj.transform.position = new Vector3(0, 10, 0);
            lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);

            Debug.Log("[ReplaySceneSetup] Created Directional Light");
        }

        private static void CreateReplaySystem()
        {
            // Create ReplaySystem parent object
            var replaySystemObj = new GameObject("ReplaySystem");

            // Create ReplayManager (main coordinator)
            var replayManagerObj = new GameObject("ReplayManager");
            replayManagerObj.transform.SetParent(replaySystemObj.transform);

            var replayManager = replayManagerObj.AddComponent<ReplayManager>();
            replayManager.autoLoadOnStart = false;
            replayManager.autoCreateComponents = true;
            replayManager.debugMode = true;

            // The ReplayManager will auto-create ReplayController, EyeMovementClassifier,
            // GazeEntropyCalculator, and ReplayVisualizer components

            Debug.Log("[ReplaySceneSetup] Created ReplaySystem with ReplayManager");
        }

        private static void CreateEnvironment()
        {
            // Create Environment parent object
            var environmentObj = new GameObject("Environment");

            // Add EnvironmentGenerator for basic room and objects
            var envGenerator = environmentObj.AddComponent<EnvironmentGenerator>();

            // Set configurable defaults for replay visualization (only settable properties)
            envGenerator.StaticObjectCount = 8;
            envGenerator.DynamicObjectCount = 0; // No moving objects in replay mode

            // Note: RoomWidth/Height/Length use default values from EnvironmentGenerator
            // To change room dimensions, modify the serialized fields in the Inspector

            // Create floor as fallback (EnvironmentGenerator may create its own)
            var floorObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floorObj.name = "Floor";
            floorObj.transform.SetParent(environmentObj.transform);
            floorObj.transform.position = Vector3.zero;
            floorObj.transform.localScale = new Vector3(1f, 1f, 1f);

            // Create a material for the floor
            var floorRenderer = floorObj.GetComponent<Renderer>();
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(0.3f, 0.3f, 0.3f);
            floorRenderer.material = floorMaterial;

            Debug.Log("[ReplaySceneSetup] Created Environment with EnvironmentGenerator");
        }

        private static void CreateCanvas()
        {
            // Create UI Canvas
            var canvasObj = new GameObject("Canvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Create ReplayUI
            var replayUIObj = new GameObject("ReplayUI");
            replayUIObj.transform.SetParent(canvasObj.transform);

            var replayUI = replayUIObj.AddComponent<ReplayUI>();
            replayUI.autoCreateUI = true;
            replayUI.uiCanvas = canvas;

            // Create EventSystem for UI interaction
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var eventSystemObj = new GameObject("EventSystem");
                eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystemObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            Debug.Log("[ReplaySceneSetup] Created Canvas with ReplayUI");
        }

        [MenuItem("Eye_lean/Validate Replay Scene")]
        public static void ValidateReplayScene()
        {
            var issues = new System.Collections.Generic.List<string>();

            // Check for required components
            var replayManager = Object.FindFirstObjectByType<ReplayManager>();
            if (replayManager == null)
            {
                issues.Add("Missing ReplayManager component");
            }
            else
            {
                if (replayManager.replayController == null && !replayManager.autoCreateComponents)
                {
                    issues.Add("ReplayManager has no ReplayController reference and autoCreateComponents is disabled");
                }
            }

            var replayUI = Object.FindFirstObjectByType<ReplayUI>();
            if (replayUI == null)
            {
                issues.Add("Missing ReplayUI component");
            }

            var envGenerator = Object.FindFirstObjectByType<EnvironmentGenerator>();
            if (envGenerator == null)
            {
                issues.Add("Missing EnvironmentGenerator component");
            }

            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                issues.Add("No Main Camera found");
            }

            // Check for conflicting components
            var experimentControllers = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in experimentControllers)
            {
                if (mb != null && mb.GetType().Name == "SampleExperimentController" && mb.enabled)
                {
                    issues.Add($"SampleExperimentController is enabled on '{mb.gameObject.name}' - this conflicts with ReplayManager");
                }
            }

            // Report results
            if (issues.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Validation Passed",
                    "The scene has all required components for replay functionality.",
                    "OK");
            }
            else
            {
                string issueList = string.Join("\n- ", issues);
                EditorUtility.DisplayDialog(
                    "Validation Issues Found",
                    $"The following issues were found:\n\n- {issueList}",
                    "OK");
            }
        }
    }
}
#endif
