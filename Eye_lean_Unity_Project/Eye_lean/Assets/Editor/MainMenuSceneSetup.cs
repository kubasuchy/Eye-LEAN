#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using EyeLean.MainMenu;

namespace EyeLean.Editor
{
    public static class MainMenuSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/MainMenu.unity";

        [MenuItem("Eye_lean/Create Main Menu Scene")]
        public static void CreateMainMenuScene()
        {
            if (!EditorUtility.DisplayDialog(
                "Create Main Menu Scene",
                "This will create a new MainMenu.unity in Assets/Scenes and " +
                "insert it as build index 0.\n\n" +
                "The scene contains a camera, directional light, " +
                "EventSystem, and MainMenuController. The menu panel " +
                "is built programmatically at runtime.",
                "Create",
                "Cancel"))
            {
                return;
            }

            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateLighting();
            CreateEventSystem();

            var controllerObj = new GameObject("MainMenuController");
            controllerObj.AddComponent<MainMenuController>();

            if (!System.IO.Directory.Exists("Assets/Scenes"))
                System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(newScene, SCENE_PATH);
            AssetDatabase.Refresh();

            InsertIntoBuildSettings();

            Debug.Log($"[MainMenuSceneSetup] MainMenu created at {SCENE_PATH} and inserted as build index 0.");
        }

        private static void CreateCamera()
        {
            var cameraObj = new GameObject("Main Camera");
            cameraObj.tag = "MainCamera";
            var cam = cameraObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.05f, 1f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.fieldOfView = 60f;
            cameraObj.AddComponent<UniversalAdditionalCameraData>();
            cameraObj.AddComponent<AudioListener>();
            cameraObj.transform.position = new Vector3(0f, 1.6f, 0f);
            cameraObj.transform.rotation = Quaternion.identity;
        }

        private static void CreateLighting()
        {
            var lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 0.3f;
            light.shadows = LightShadows.None;
            lightObj.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        private static void CreateEventSystem()
        {
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static void InsertIntoBuildSettings()
        {
            string[] canonical = {
                SCENE_PATH,
                "Assets/Scenes/CalibrationScene.unity",
                "Assets/Scenes/SampleExperiment.unity",
                "Assets/Scenes/NBackScene.unity",
                "Assets/Scenes/MazeScene.unity",
            };
            var canonicalSet = new System.Collections.Generic.HashSet<string>(canonical);

            var newList = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (var p in canonical)
            {
                if (System.IO.File.Exists(p))
                    newList.Add(new EditorBuildSettingsScene(p, true));
                else
                    Debug.LogWarning($"[MainMenuSceneSetup] Scene missing: {p}");
            }
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (canonicalSet.Contains(s.path)) continue;
                newList.Add(s);
            }
            EditorBuildSettings.scenes = newList.ToArray();
        }
    }
}
#endif
