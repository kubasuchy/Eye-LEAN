// SPDX-License-Identifier: MIT
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EyeLean.NBack;
using EyeLean.NBack.UI;
using EyeTracking.Components;

namespace EyeLean.NBack.EditorTools
{
    /// <summary>
    /// Editor wizard (menu: <c>VR Experiment &gt; New N-back Scene</c>) that
    /// materializes <c>Assets/Scenes/NBackScene.unity</c> with the controller,
    /// task manager, UI panels, and the EyeTracking recorder trio pre-wired.
    /// Adds the scene to Build Settings (after SampleExperiment), since the
    /// N-back scene IS a shippable experiment, not a developer-side template.
    /// </summary>
    public static class NBackSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/NBackScene.unity";
        private const string CONFIG_DIR = "Assets/Settings";
        private const string CONFIG_PATH = "Assets/Settings/NBackConfig_PaperDefault.asset";

        [MenuItem("VR Experiment/New N-back Scene")]
        public static void CreateNBackScene()
        {
            if (File.Exists(SCENE_PATH))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "N-back scene exists",
                    SCENE_PATH + " already exists. Overwrite?",
                    "Overwrite",
                    "Cancel");
                if (!overwrite) return;
            }

            NBackConfig config = EnsureDefaultConfigAsset();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // EyeTracking rig: same components as CalibrationScene / SampleExperiment.
            var eyeRig = new GameObject("EyeTrackingSystem");
            var eyeTracker = eyeRig.AddComponent<EyeTracker>();
            eyeRig.AddComponent<HMDDataCollector>();
            eyeRig.AddComponent<SessionRecorder>();
            // Opt in to the on-screen cognitive-load gauge; N-back's whole
            // purpose is validating the load detectors against ground-truth
            // load, so the researcher should always see the live readout.
            // Toggling visibility at runtime is via RIPAMonitor.ShowOverlay.
            var serEt = new UnityEditor.SerializedObject(eyeTracker);
            serEt.FindProperty("spawnCognitiveLoadOverlay").boolValue = true;
            serEt.ApplyModifiedPropertiesWithoutUndo();
            // RIPAMonitor + RIPACSVColumn are auto-attached at AfterSceneLoad
            // by RIPAMonitorBootstrap, so the per-detector LiveLoadIndex_*
            // columns appear in the CSV without manual wiring.

            // Managers: controller + task manager on the same GameObject so
            // the controller's GetComponent<NBackTaskManager>() auto-resolves.
            var managers = new GameObject("Managers");
            var taskManager = managers.AddComponent<NBackTaskManager>();
            var controller = managers.AddComponent<NBackExperimentController>();
            controller.config = config;

            // UI: three world-space panels under their own root, all
            // camera-anchored at LateUpdate so head pose tracks them.
            var uiRoot = new GameObject("NBackUI");
            var stimulusGO = new GameObject("StimulusPanel");
            stimulusGO.transform.SetParent(uiRoot.transform, false);
            var stimulusPanel = stimulusGO.AddComponent<NBackStimulusPanel>();

            var instructionsGO = new GameObject("InstructionsPanel");
            instructionsGO.transform.SetParent(uiRoot.transform, false);
            var instructionsPanel = instructionsGO.AddComponent<EyeTracking.UI.WorldInstructionPanel>();

            var hudGO = new GameObject("HUDController");
            hudGO.transform.SetParent(uiRoot.transform, false);
            var hud = hudGO.AddComponent<NBackHUDController>();

            // Wire inspector references on the manager components so the
            // scene works without per-component auto-find at runtime.
            var taskSerialized = new SerializedObject(taskManager);
            taskSerialized.FindProperty("stimulusPanel").objectReferenceValue = stimulusPanel;
            taskSerialized.ApplyModifiedPropertiesWithoutUndo();

            var ctrlSerialized = new SerializedObject(controller);
            ctrlSerialized.FindProperty("taskManager").objectReferenceValue = taskManager;
            ctrlSerialized.FindProperty("instructionsPanel").objectReferenceValue = instructionsPanel;
            ctrlSerialized.FindProperty("hud").objectReferenceValue = hud;
            ctrlSerialized.FindProperty("config").objectReferenceValue = config;
            ctrlSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Re-resolve the config SO from disk before marking it dirty.
            // Unity's NewScene() can trigger an internal asset re-import
            // that destroys SO instances loaded before the scene was
            // created, so the C# reference held since EnsureDefaultConfigAsset
            // may already point to a destroyed object — SetDirty on which
            // throws MissingReferenceException.
            config = AssetDatabase.LoadAssetAtPath<NBackConfig>(CONFIG_PATH);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(taskManager);
            if (config != null) EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // AssetDatabase.Refresh may have re-imported the config asset,
            // invalidating the in-memory reference we set above. Re-resolve
            // by path and re-assign on the SerializedObject — this is the
            // canonical pattern for wizard-driven scene/asset wiring.
            var refreshedConfig = AssetDatabase.LoadAssetAtPath<NBackConfig>(CONFIG_PATH);
            var ctrlReassign = new SerializedObject(controller);
            ctrlReassign.FindProperty("config").objectReferenceValue = refreshedConfig;
            ctrlReassign.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);

            Directory.CreateDirectory(Path.GetDirectoryName(SCENE_PATH));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();

            AddSceneToBuildSettings(SCENE_PATH);

            EditorUtility.DisplayDialog(
                "N-back scene created",
                "N-back scene saved to:\n" + SCENE_PATH + "\n\n" +
                "Build Settings: added the scene (also confirm position in\n" +
                "Build Settings is after SampleExperiment so MainMenu's\n" +
                "scene-name lookup resolves it).\n\n" +
                "Next steps:\n" +
                "1. Confirm the active config is " + CONFIG_PATH + ".\n" +
                "2. Edit the config's parameter values to match\n" +
                "   Jayawardena 2025 (RIPA2 paper) — currently set to\n" +
                "   reasonable defaults pending the paper-exact pull.\n" +
                "3. Add the scene to your APK build flow.",
                "OK");
        }

        private static NBackConfig EnsureDefaultConfigAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<NBackConfig>(CONFIG_PATH);
            if (existing != null) return existing;

            Directory.CreateDirectory(CONFIG_DIR);
            var config = ScriptableObject.CreateInstance<NBackConfig>();
            AssetDatabase.CreateAsset(config, CONFIG_PATH);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var current = EditorBuildSettings.scenes;
            foreach (var s in current)
            {
                if (s.path == scenePath) return; // already present
            }
            var updated = new EditorBuildSettingsScene[current.Length + 1];
            for (int i = 0; i < current.Length; i++) updated[i] = current[i];
            updated[current.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}
