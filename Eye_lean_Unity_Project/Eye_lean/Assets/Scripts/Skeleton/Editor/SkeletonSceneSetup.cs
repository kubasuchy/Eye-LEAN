// SPDX-License-Identifier: MIT
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EyeTracking.Components;

namespace EyeLean.Skeleton.Editor
{
    /// <summary>
    /// Editor wizard (menu: <c>VR Experiment &gt; New Skeleton Scene</c>) that
    /// materializes a COMPLETE, runnable Skeleton scene: the manager trio, the
    /// Eye_lean recorder trio, the loop drivers (StartingPlatform + FixationCross
    /// with its visual prefab), a demo phase handler, and a default
    /// TrialConfiguration — all wired so the ITI -> Platform -> Fixation -> Trial
    /// loop runs on Play with no manual setup. The Skeleton is a developer-side
    /// template, NOT part of the Eye_lean APK build flow; the wizard does not add
    /// the scene to EditorBuildSettings. Re-running overwrites the scene (an
    /// existing TrialConfig asset is reused, not clobbered).
    /// </summary>
    public static class SkeletonSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/Skeleton.unity";
        private const string CONFIG_PATH = "Assets/Scenes/SkeletonTrialConfig.asset";

        [MenuItem("VR Experiment/New Skeleton Scene")]
        public static void CreateSkeletonScene()
        {
            if (File.Exists(SCENE_PATH))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Skeleton scene exists",
                    $"{SCENE_PATH} already exists. Overwrite?",
                    "Overwrite",
                    "Cancel");
                if (!overwrite) return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // --- Desktop test locomotion on the participant camera (WASD walk, Q/E up-down,
            // hold right-mouse to look, Shift to sprint). Editor-only + inert whenever an HMD
            // is active, so it never touches VR sessions or builds. ---
            var mainCam = Camera.main != null ? Camera.main.gameObject : GameObject.Find("Main Camera");
            if (mainCam != null) mainCam.AddComponent<DesktopTestCameraController>();

            // --- Managers: state machine + session coordinator + agents + environment ---
            var managers = new GameObject("Managers");
            var trialManager = managers.AddComponent<TrialManager>();
            var experimentManager = managers.AddComponent<ExperimentManager>();
            managers.AddComponent<AgentManager>();
            managers.AddComponent<EnvironmentManager>();

            // --- Loop drivers. TrialManager is a passive state machine; nothing advances
            // ITI -> WaitingOnPlatform -> FixationCross on its own. StartingPlatform is the
            // driver (self-positions at the participant, renders its own bubble, calls
            // OnPlatformActivated when stood on); FixationCross shows the cross and advances
            // to ExperimentalPhase. Without these the scene sits at ITI forever. ---
            var startingPlatform = managers.AddComponent<StartingPlatform>();
            var fixation = managers.AddComponent<FixationCross>();
            WireFixationCrossVisual(managers, fixation);

            // --- Eye_lean recorder trio (per-frame CSV + sidecars). ---
            var eyeRig = new GameObject("EyeTrackingSystem");
            eyeRig.AddComponent<EyeTracker>();
            eyeRig.AddComponent<HMDDataCollector>();
            eyeRig.AddComponent<SessionRecorder>();
            // RIPAMonitor + RIPACSVColumn are auto-attached by RIPAMonitorBootstrap at AfterSceneLoad

            // --- Researcher hook: the demo handler exercises the ExperimentalPhase. ---
            var demoHandler = new GameObject("DemoPhaseHandler");
            demoHandler.AddComponent<EyeLean.Skeleton.Examples.EyeleanDemoPhaseHandler>();

            // --- Default trial design, created + assigned so the loop runs without manual
            // setup. An existing asset is reused so re-running the wizard never clobbers
            // edits. (TrialManager also has a 20-trial fallback, but a real asset is the
            // thing researchers edit + it's what gets snapshotted into the events sidecar.) ---
            var config = GetOrCreateDefaultConfig();
            SetObjectRef(trialManager, "experimentConfiguration", config);

            // --- Session lifecycle: auto-start so ExperimentManager enters Running (engages
            // the session-timeout safety net). The loop itself is driven by StartingPlatform. ---
            SetBool(experimentManager, "autoStartSession", true);

            // --- Developer-template observability: phase HUD (TrialManager, top-left) +
            // platform HUD/logs (StartingPlatform, top-right) on by default so a test run is
            // visible at a glance. Uncheck 'Show Debug Info' / 'Show Debug Logs' for real runs. ---
            SetBool(trialManager, "showDebugInfo", true);
            SetBool(startingPlatform, "showDebugLogs", true);

            // Skeleton is developer-side: deliberately not added to EditorBuildSettings
            Directory.CreateDirectory(Path.GetDirectoryName(SCENE_PATH));
            EditorSceneManager.SaveScene(scene, SCENE_PATH);

            EditorUtility.DisplayDialog(
                "Skeleton scene created",
                $"Skeleton scene saved to:\n{SCENE_PATH}\n\n" +
                "Fully wired and runnable out of the box:\n" +
                "• Managers + Eye_lean recorder trio\n" +
                "• StartingPlatform + FixationCross (+ visual) drive the\n" +
                "  ITI -> Platform -> Fixation -> Trial loop\n" +
                "• Default TrialConfig created + assigned\n" +
                "• Phase + platform debug HUDs ON (uncheck to disable)\n\n" +
                "Try it:\n" +
                "1. Press Play.\n" +
                "2. On a headset: stand on the green platform.\n" +
                "   In the editor: WASD to walk onto the platform (hold right\n" +
                "   mouse to look, Q/E down/up, Shift to sprint) — or use\n" +
                "   StartingPlatform's gear menu > 'Test Platform Activation'.\n" +
                "3. Watch the HUD step Platform -> FixationCross -> Experimental;\n" +
                "   a red DemoStimulus appears — press Space to respond.\n" +
                "4. CSV + _SceneEvents/_SceneState sidecars are written on quit.\n\n" +
                "Customize: edit SkeletonTrialConfig, then replace\n" +
                "EyeleanDemoPhaseHandler with your own IExperimentPhaseHandler.\n" +
                "The Skeleton is developer-side and is NOT added to Build\n" +
                "Settings — add it to YOUR build when ready to ship.\n\n" +
                "See docs/SKELETON.md for the full walkthrough.",
                "OK");
        }

        /// <summary>
        /// Loads the default TrialConfiguration if it already exists (so re-running the
        /// wizard never clobbers researcher edits), otherwise creates a minimal valid one
        /// (single 5-trial block) next to the scene.
        /// </summary>
        private static TrialConfiguration GetOrCreateDefaultConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TrialConfiguration>(CONFIG_PATH);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            var config = ScriptableObject.CreateInstance<TrialConfiguration>();
            config.experimentName = "Skeleton Demo";
            config.description = "Auto-created by 'New Skeleton Scene'. Edit blocks / trials here.";
            config.blocks.Add(new TrialBlock
            {
                blockName = "Demo Block",
                trialsInBlock = 5,
                description = "Demo trials generated by the Skeleton wizard.",
            });
            AssetDatabase.CreateAsset(config, CONFIG_PATH);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>
        /// Instantiates the reusable FixationCrossVisual prefab as a child of Managers and
        /// assigns it to FixationCross's private serialized 'fixationCross' field. Located by
        /// name+type so it survives the Skeleton folder being relocated. No-op (with a warning)
        /// if the prefab isn't in the project.
        /// </summary>
        private static void WireFixationCrossVisual(GameObject managers, FixationCross fixation)
        {
            string[] guids = AssetDatabase.FindAssets("FixationCrossVisual t:Prefab");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[SkeletonSceneSetup] FixationCrossVisual prefab not found in project. " +
                    "Add Skeleton/Prefabs/FixationCrossVisual.prefab or assign FixationCross.fixationCross manually.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (prefab == null) return;

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            visual.transform.SetParent(managers.transform, false);
            visual.SetActive(false); // FixationCross enables it when the participant fixates
            SetObjectRef(fixation, "fixationCross", visual);
        }

        // ----- SerializedObject helpers (write private [SerializeField] fields cleanly) -----

        private static void SetObjectRef(Object target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning($"[SkeletonSceneSetup] '{propertyName}' not found on {target.GetType().Name} — skipped.");
            }
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                prop.boolValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning($"[SkeletonSceneSetup] '{propertyName}' not found on {target.GetType().Name} — skipped.");
            }
        }
    }
}
