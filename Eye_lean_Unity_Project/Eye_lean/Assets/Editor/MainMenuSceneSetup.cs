#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using EyeLean.MainMenu;
using EyeTracking.Calibration.UI;

namespace EyeLean.Editor
{
    /// <summary>
    /// Editor utility that creates and registers the MainMenu launcher scene.
    /// Mirrors the ReplaySceneSetup pattern: scene authoring lives in code so
    /// a fresh clone of the repo can build the scene with one menu click and
    /// we don't have to hand-edit Unity YAML for camera/lighting/UI rigs.
    ///
    /// Menu: Eye_lean > Create Main Menu Scene
    /// </summary>
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
                "EventSystem, MainMenuController, and a CalibrationWorldUI " +
                "that displays the launcher with two dwell-buttons " +
                "(Calibrator / Sample Experiment).",
                "Create",
                "Cancel"))
            {
                return;
            }

            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateLighting();
            CreateEventSystem();
            CreateMenuController();

            if (!System.IO.Directory.Exists("Assets/Scenes"))
            {
                System.IO.Directory.CreateDirectory("Assets/Scenes");
            }
            EditorSceneManager.SaveScene(newScene, SCENE_PATH);
            AssetDatabase.Refresh();

            InsertIntoBuildSettings();

            Debug.Log($"[MainMenuSceneSetup] MainMenu created at {SCENE_PATH} and inserted as build index 0.");
            EditorUtility.DisplayDialog(
                "Main Menu created",
                $"MainMenu.unity has been created at:\n{SCENE_PATH}\n\n" +
                "Build Settings now starts with:\n" +
                "  0  Assets/Scenes/MainMenu.unity\n" +
                "  1  Assets/Scenes/CalibrationScene.unity\n" +
                "  2  Assets/Scenes/SampleExperiment.unity\n\n" +
                "Open MainMenu, enter Play mode to verify the dwell-buttons " +
                "appear, then deploy.",
                "OK");
        }

        private static void CreateCamera()
        {
            var cameraObj = new GameObject("Main Camera");
            cameraObj.tag = "MainCamera";
            var cam = cameraObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
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
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        private static void CreateEventSystem()
        {
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static void CreateMenuController()
        {
            // The MainMenu uses HUD-locked UI (parentToCamera = true) so the
            // panel stays directly in front of the user with no follow lag.
            // The empty scene has no spatial landmarks, so a static
            // world-space placement makes the panel feel mis-anchored — the
            // calibrator and experiment scenes don't have this problem
            // because their environments provide reference. Hard-parenting
            // the canvas to the camera is the standard VR menu pattern.
            var uiObj = new GameObject("MenuWorldUI");
            var ui = uiObj.AddComponent<CalibrationWorldUI>();
            // CalibrationWorldUI.parentToCamera is private [SerializeField];
            // can't set it from here without reflection, so we set it via
            // SerializedObject + ApplyModifiedProperties so the saved scene
            // ships with HUD-locked behavior out of the box.
            var so = new UnityEditor.SerializedObject(ui);
            SetBool(so, "parentToCamera", true);
            // Compact panel: gaze threshold is 15°, calibrator's FOV-based
            // 85%-of-screen sizing pushes buttons to ~30° off-center.
            // Vertical offset is positive to put the panel center above
            // gaze, so the bottom-of-canvas button row lands near eye level.
            SetBool(so, "useFixedPanelSize", true);
            SetFloat(so, "panelWidth", 1100f);
            SetFloat(so, "panelHeight", 700f);
            SetFloat(so, "uiScale", 0.0012f);
            SetFloat(so, "verticalOffset", 0.25f);
            // Two-second arming delay: prevents the user's straight-ahead
            // gaze at scene load from triggering whichever button is at
            // panel center.
            SetFloat(so, "gazeArmingDelaySeconds", 2f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(UnityEditor.SerializedObject so, string name, bool value)
        {
            var prop = so.FindProperty(name);
            if (prop != null) prop.boolValue = value;
        }

        private static void SetFloat(UnityEditor.SerializedObject so, string name, float value)
        {
            var prop = so.FindProperty(name);
            if (prop != null) prop.floatValue = value;

            var controllerObj = new GameObject("MainMenuController");
            controllerObj.AddComponent<MainMenuController>();
        }

        /// <summary>
        /// Write the canonical Build Settings scene list:
        ///   0  MainMenu          (enabled)
        ///   1  SampleScene       (enabled)  -- the calibrator
        ///   2  SampleExperiment  (enabled)
        /// Then preserve any other entries that happen to be present
        /// (preserving their enabled-flag) at the end. Deterministic so
        /// re-running the setup always produces the same ordering, and
        /// re-enables SampleScene / SampleExperiment if a previous test
        /// run had toggled them off — the menu needs both loadable.
        /// </summary>
        private static void InsertIntoBuildSettings()
        {
            const string CALIBRATOR_PATH = "Assets/Scenes/CalibrationScene.unity";
            const string EXPERIMENT_PATH = "Assets/Scenes/SampleExperiment.unity";
            var canonical = new[] { SCENE_PATH, CALIBRATOR_PATH, EXPERIMENT_PATH };
            var canonicalSet = new System.Collections.Generic.HashSet<string>(canonical);

            var newList = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (var p in canonical)
            {
                if (System.IO.File.Exists(p))
                {
                    newList.Add(new EditorBuildSettingsScene(p, true));
                }
                else
                {
                    Debug.LogWarning($"[MainMenuSceneSetup] Canonical scene missing on disk, skipping: {p}");
                }
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
