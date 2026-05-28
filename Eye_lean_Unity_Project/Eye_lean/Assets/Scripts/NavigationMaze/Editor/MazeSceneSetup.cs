// SPDX-License-Identifier: MIT
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Navlab.ExperimentRuntime;
using EyeTracking.Components;
using EyeLean.NavigationMaze;
using EyeLean.NavigationMaze.Generation;
using EyeLean.NavigationMaze.UI;
using EyeLean.NavigationMaze.DebugTools;

namespace EyeLean.NavigationMaze.EditorTools
{
    /// <summary>
    /// Editor wizard (menu: <c>VR Experiment &gt; New Maze Scene</c>) that
    /// materializes <c>Assets/Scenes/MazeScene.unity</c> with navlab's
    /// <see cref="ExperimentRunner"/> + <see cref="HumanObstacleTracker"/>,
    /// Eye_lean's recorder trio, and the bridge that joins them. Generates a
    /// hand-authored EnvironmentConfig + ExperimentConfig pair under
    /// <c>Assets/Settings/Navigation/</c> on first run (kept on subsequent
    /// re-runs). Adds the scene to Build Settings.
    /// </summary>
    public static class MazeSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/MazeScene.unity";
        private const string SETTINGS_DIR = "Assets/Settings/Navigation";
        private const string ENV_PATH = "Assets/Settings/Navigation/Maze_v1_Environment.asset";
        private const string EXP_PATH = "Assets/Settings/Navigation/Maze_v1_Experiment.asset";
        private const string SKIN_PATH = "Assets/Settings/Navigation/NPC_1_AgentSkin.asset";
        private const string DELEGATE_PATH = "Assets/Settings/Navigation/ProceduralMazeDelegate.asset";
        private const string CONFIG_PATH = "Assets/Settings/Navigation/MazeConfig_Default.asset";

        [MenuItem("VR Experiment/New Maze Scene")]
        public static void CreateMazeScene()
        {
            if (File.Exists(SCENE_PATH))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Maze scene exists",
                    SCENE_PATH + " already exists. Overwrite?",
                    "Overwrite",
                    "Cancel");
                if (!overwrite) return;
            }

            EnvironmentConfig env = EnsureEnvironment();
            AgentSkin npcSkin = EnsureAgentSkin();
            ExperimentConfig exp = EnsureExperiment(env, npcSkin);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // EyeTracking rig (same as N-back / SampleExperiment scenes).
            var eyeRig = new GameObject("EyeTrackingSystem");
            var eyeTracker = eyeRig.AddComponent<EyeTracker>();
            eyeRig.AddComponent<HMDDataCollector>();
            eyeRig.AddComponent<SessionRecorder>();
            // Opt in to the on-screen cognitive-load gauge for maze runs
            // so researchers can correlate locomotion / NPC encounters with
            // live load. RIPAMonitor.ShowOverlay toggles visibility at run.
            var serEt = new UnityEditor.SerializedObject(eyeTracker);
            serEt.FindProperty("spawnCognitiveLoadOverlay").boolValue = true;
            serEt.ApplyModifiedPropertiesWithoutUndo();

            // navlab runner + human-obstacle tracker. The bridge wires
            // headCamera = Camera.main at Awake.
            var navlabRoot = new GameObject("Navlab");
            var runner = navlabRoot.AddComponent<ExperimentRunner>();
            runner.experiment = exp;
            var humanTracker = navlabRoot.AddComponent<HumanObstacleTracker>();
            runner.humanTracker = humanTracker;

            // Eye_lean bridge — executes before runner.Start() so the
            // session context is injected in time.
            var bridgeRoot = new GameObject("MazeBridge");
            var bridge = bridgeRoot.AddComponent<MazeExperimentBridge>();
            var bridgeSerialized = new SerializedObject(bridge);
            bridgeSerialized.FindProperty("runner").objectReferenceValue = runner;
            bridgeSerialized.FindProperty("humanTracker").objectReferenceValue = humanTracker;
            bridgeSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Environment renderer — walks the EnvironmentConfig and
            // instantiates visible floor/walls/obstacles/goals at Start.
            // Without this the maze appears empty (navlab's runtime only
            // treats EnvironmentConfig as planner-input data).
            var rendererRoot = new GameObject("MazeEnvironment");
            var envRenderer = rendererRoot.AddComponent<MazeEnvironmentRenderer>();
            var rendererSerialized = new SerializedObject(envRenderer);
            rendererSerialized.FindProperty("experiment").objectReferenceValue = exp;
            rendererSerialized.ApplyModifiedPropertiesWithoutUndo();

            // UI: HUD + per-trial instructions panel.
            var uiRoot = new GameObject("MazeUI");
            var hudGO = new GameObject("HUD");
            hudGO.transform.SetParent(uiRoot.transform, false);
            var hud = hudGO.AddComponent<MazeHUDController>();
            var instructionsGO = new GameObject("Instructions");
            instructionsGO.transform.SetParent(uiRoot.transform, false);
            instructionsGO.AddComponent<EyeTracking.UI.WorldInstructionPanel>();

            // Re-wire the bridge HUD ref now that the HUD exists.
            bridgeSerialized.Update();
            bridgeSerialized.FindProperty("hud").objectReferenceValue = hud;
            bridgeSerialized.ApplyModifiedPropertiesWithoutUndo();

            // --- Navigation Suite: procedural delegate + config ---
            var mazeDelegate = AssetDatabase.LoadAssetAtPath<ProceduralMazeDelegate>(DELEGATE_PATH);
            if (mazeDelegate == null)
            {
                mazeDelegate = ScriptableObject.CreateInstance<ProceduralMazeDelegate>();
                AssetDatabase.CreateAsset(mazeDelegate, DELEGATE_PATH);
            }

            var mazeConfig = AssetDatabase.LoadAssetAtPath<MazeConfig>(CONFIG_PATH);
            if (mazeConfig == null)
            {
                mazeConfig = ScriptableObject.CreateInstance<MazeConfig>();
                mazeConfig.blocks = CreateDefaultBlocks();
                AssetDatabase.CreateAsset(mazeConfig, CONFIG_PATH);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            mazeDelegate = AssetDatabase.LoadAssetAtPath<ProceduralMazeDelegate>(DELEGATE_PATH);
            mazeConfig = AssetDatabase.LoadAssetAtPath<MazeConfig>(CONFIG_PATH);
            env = AssetDatabase.LoadAssetAtPath<EnvironmentConfig>(ENV_PATH);
            Debug.Log($"[MazeSetup] delegate={mazeDelegate != null}, config={mazeConfig != null}, env={env != null}");

            var envSO = new SerializedObject(env);
            var delegateProp = envSO.FindProperty("proceduralDelegate");
            Debug.Log($"[MazeSetup] env.proceduralDelegate prop found: {delegateProp != null}");
            delegateProp.objectReferenceValue = mazeDelegate;
            envSO.ApplyModifiedProperties();

            var lp = rendererRoot.AddComponent<MazeLandmarkPlacer>();
            var cb = rendererRoot.AddComponent<MazeCeilingBuilder>();

            var suiteSO = new SerializedObject(bridge);
            string[] fields = { "mazeConfig", "mazeDelegate", "environmentRenderer", "landmarkPlacer", "ceilingBuilder" };
            Object[] values = { mazeConfig, mazeDelegate, envRenderer, lp, cb };
            for (int i = 0; i < fields.Length; i++)
            {
                var prop = suiteSO.FindProperty(fields[i]);
                Debug.Log($"[MazeSetup] bridge.{fields[i]} prop found: {prop != null}, value: {values[i]}");
                if (prop != null) prop.objectReferenceValue = values[i];
            }
            suiteSO.ApplyModifiedProperties();

            // Flush SO + scene to disk in order so GUIDs resolve correctly
            // on next scene load. CreateAsset registers GUIDs immediately
            // but the .meta write is asynchronous — without SaveAssets()
            // before SaveScene(), the scene file can reference an asset
            // whose .meta hasn't reached disk yet, which deserializes as
            // null on next open.
            // Auto-install the editor-only WASD locomotion helper on the
            // camera (or its parent if a rig is detected). Same logic as
            // the menu installer — wrapped in #if UNITY_EDITOR via the
            // component's own file gate, so this never reaches a player
            // build. Researchers can delete the component in inspector
            // if they prefer the menu-driven workflow.
            var cam = Camera.main;
            if (cam != null)
            {
                Transform locoHost = cam.transform.parent != null ? cam.transform.parent : cam.transform;
                if (locoHost.GetComponent<EditorDebugLocomotion>() == null)
                {
                    locoHost.gameObject.AddComponent<EditorDebugLocomotion>();
                    EditorUtility.SetDirty(locoHost.gameObject);
                }
            }
            else
            {
                Debug.LogWarning("[MazeSceneSetup] No Camera.main in active scene; EditorDebugLocomotion was not auto-installed. Add it via 'Eye_lean > Debug > Add Editor Locomotion to Current Scene' after positioning your camera.");
            }

            // Re-resolve SO assets from disk before SetDirty. NewScene() +
            // intermediate AssetDatabase ops can destroy the C# instances
            // loaded earlier in the wizard; calling SetDirty on the stale
            // reference throws MissingReferenceException.
            exp = AssetDatabase.LoadAssetAtPath<ExperimentConfig>(EXP_PATH);
            env = AssetDatabase.LoadAssetAtPath<EnvironmentConfig>(ENV_PATH);
            EditorUtility.SetDirty(runner);
            EditorUtility.SetDirty(bridge);
            EditorUtility.SetDirty(envRenderer);
            if (exp != null) EditorUtility.SetDirty(exp);
            if (env != null) EditorUtility.SetDirty(env);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Refresh may have re-imported ScriptableObject assets,
            // invalidating the in-memory references we wired above. Re-load
            // from disk and re-assign on SerializedObject so the scene file
            // captures live GUIDs rather than fileID:0.
            mazeConfig = AssetDatabase.LoadAssetAtPath<MazeConfig>(CONFIG_PATH);
            mazeDelegate = AssetDatabase.LoadAssetAtPath<ProceduralMazeDelegate>(DELEGATE_PATH);
            env = AssetDatabase.LoadAssetAtPath<EnvironmentConfig>(ENV_PATH);
            var bridgeReassign = new SerializedObject(bridge);
            bridgeReassign.FindProperty("mazeConfig").objectReferenceValue = mazeConfig;
            bridgeReassign.FindProperty("mazeDelegate").objectReferenceValue = mazeDelegate;
            bridgeReassign.ApplyModifiedPropertiesWithoutUndo();
            var envReassign = new SerializedObject(env);
            envReassign.FindProperty("proceduralDelegate").objectReferenceValue = mazeDelegate;
            envReassign.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bridge);
            EditorUtility.SetDirty(env);

            Directory.CreateDirectory(Path.GetDirectoryName(SCENE_PATH));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();
            AddSceneToBuildSettings(SCENE_PATH);

            EditorUtility.DisplayDialog(
                "Maze scene created",
                "Maze scene saved to:\n" + SCENE_PATH + "\n\n" +
                "Assets created (kept on re-run if already present):\n" +
                "  • " + ENV_PATH + "\n" +
                "  • " + EXP_PATH + "\n" +
                "  • " + SKIN_PATH + "\n" +
                "  • " + DELEGATE_PATH + "\n" +
                "  • " + CONFIG_PATH + "\n\n" +
                "Navigation suite wired:\n" +
                "  ProceduralMazeDelegate, MazeConfig (6-block default),\n" +
                "  MazeLandmarkPlacer, MazeCeilingBuilder.\n\n" +
                "Next steps:\n" +
                "1. Open MazeConfig_Default to tune block sequence, grid size,\n" +
                "   and landmark settings.\n" +
                "2. Confirm com.navlab.experiment-runtime resolved in the\n" +
                "   Package Manager (Unity Console should be clear).\n" +
                "3. Add MazeScene to your APK build flow.\n\n" +
                "Editor-only testing without an HMD:\n" +
                "  Eye_lean > Debug > Add Editor Locomotion to Current Scene\n" +
                "  (WASD + right-click look, F1 toggle; compiled out of\n" +
                "   player builds, never visible to participants).",
                "OK");
        }

        private static EnvironmentConfig EnsureEnvironment()
        {
            var existing = AssetDatabase.LoadAssetAtPath<EnvironmentConfig>(ENV_PATH);
            if (existing != null) return existing;

            Directory.CreateDirectory(SETTINGS_DIR);
            var env = ScriptableObject.CreateInstance<EnvironmentConfig>();
            env.boundsMin = new Vector2(-3.5f, 0f);
            env.boundsMax = new Vector2(3.5f, 7f);

            // Outer walls (rectangular room) — 0.1 m thick.
            env.walls.Add(W(-3.5f, 0f, 3.5f, 0f));
            env.walls.Add(W(-3.5f, 7f, 3.5f, 7f));
            env.walls.Add(W(-3.5f, 0f, -3.5f, 7f));
            env.walls.Add(W(3.5f, 0f, 3.5f, 7f));

            // Two interior baffles forming a small maze.
            env.walls.Add(W(-1.5f, 2.0f, 1.5f, 2.0f));
            env.walls.Add(W(-1.5f, 5.0f, 1.5f, 5.0f));

            // Obstacle cluster between the baffles.
            env.obstacles.Add(new CircularObstacle { centerXZ = new Vector2(-0.8f, 3.5f), radius = 0.35f });
            env.obstacles.Add(new CircularObstacle { centerXZ = new Vector2(0.8f, 3.5f), radius = 0.35f });

            env.spawnPoints.Add(new NamedPoint { name = "S_human", positionXZ = new Vector2(-1.5f, 0.6f), headingDeg = 0f });
            env.spawnPoints.Add(new NamedPoint { name = "S_npc", positionXZ = new Vector2(1.5f, 0.6f), headingDeg = 0f });
            env.goals.Add(new NamedPoint { name = "G_human", positionXZ = new Vector2(-1.5f, 6.4f), headingDeg = 180f });
            env.goals.Add(new NamedPoint { name = "G_npc", positionXZ = new Vector2(1.5f, 6.4f), headingDeg = 180f });

            AssetDatabase.CreateAsset(env, ENV_PATH);
            AssetDatabase.SaveAssets();
            return env;
        }

        private static WallSegment W(float x1, float z1, float x2, float z2)
        {
            return new WallSegment
            {
                startXZ = new Vector2(x1, z1),
                endXZ = new Vector2(x2, z2),
                thickness = 0.10f,
            };
        }

        private static AgentSkin EnsureAgentSkin()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AgentSkin>(SKIN_PATH);
            if (existing != null) return existing;
            Directory.CreateDirectory(SETTINGS_DIR);
            var skin = ScriptableObject.CreateInstance<AgentSkin>();
            // Prefab left null — navlab's runner falls back to a primitive
            // capsule when AgentSkin.prefab is null. Researchers drag a
            // proper humanoid prefab in later.
            AssetDatabase.CreateAsset(skin, SKIN_PATH);
            AssetDatabase.SaveAssets();
            return skin;
        }

        private static ExperimentConfig EnsureExperiment(EnvironmentConfig env, AgentSkin npcSkin)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ExperimentConfig>(EXP_PATH);
            if (existing != null) return existing;

            Directory.CreateDirectory(SETTINGS_DIR);
            var exp = ScriptableObject.CreateInstance<ExperimentConfig>();
            exp.experimentName = "Maze_v1";
            exp.version = "1.0.0";
            exp.environment = env;
            exp.randomSeed = 0xBEEF;

            exp.agents.Add(new AgentSpec
            {
                name = "P_human",
                agentType = AgentType.Human,
                skin = null,
                planner = PlannerType.None,
                spawnRef = "S_human",
                goalRef = "G_human",
            });
            exp.agents.Add(new AgentSpec
            {
                name = "NPC_1",
                agentType = AgentType.Npc,
                skin = npcSkin,
                planner = PlannerType.DStarLite,
                spawnRef = "S_npc",
                goalRef = "G_npc",
            });

            // 4 solo trials (P_human only) + 4 competitive (P_human + NPC_1)
            // interleaved so participant fatigue doesn't load entirely onto
            // one condition.
            for (int i = 0; i < 4; i++)
            {
                exp.trials.Add(new TrialSpec
                {
                    trialId = "solo_" + (i + 1),
                    activeAgentNames = new System.Collections.Generic.List<string> { "P_human" },
                    durationSeconds = 45f,
                    trialSeed = 1000 + i,
                });
                exp.trials.Add(new TrialSpec
                {
                    trialId = "comp_" + (i + 1),
                    activeAgentNames = new System.Collections.Generic.List<string> { "P_human", "NPC_1" },
                    durationSeconds = 45f,
                    trialSeed = 2000 + i,
                });
            }

            AssetDatabase.CreateAsset(exp, EXP_PATH);
            AssetDatabase.SaveAssets();
            return exp;
        }

        private static MazeBlockConfig[] CreateDefaultBlocks()
        {
            return new[]
            {
                new MazeBlockConfig { mode = MazeTrialMode.Exploration, trialsInBlock = 1, landmarkCondition = LandmarkCondition.Distal },
                new MazeBlockConfig { mode = MazeTrialMode.Wayfinding, trialsInBlock = 4, difficulty = 0, landmarkCondition = LandmarkCondition.Distal },
                new MazeBlockConfig { mode = MazeTrialMode.Wayfinding, trialsInBlock = 4, difficulty = 0, ceilingEnabled = true, landmarkCondition = LandmarkCondition.Proximal },
                new MazeBlockConfig { mode = MazeTrialMode.Probe, trialsInBlock = 1, reusesPreviousBlockMaze = true, landmarkCondition = LandmarkCondition.None },
                new MazeBlockConfig { mode = MazeTrialMode.Sequential, trialsInBlock = 3, difficulty = 2, sequentialGoalCount = 3, landmarkCondition = LandmarkCondition.Distal },
                new MazeBlockConfig { mode = MazeTrialMode.Competitive, trialsInBlock = 3, difficulty = 2, npcEnabled = true, landmarkCondition = LandmarkCondition.Distal },
            };
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var current = EditorBuildSettings.scenes;
            foreach (var s in current)
            {
                if (s.path == scenePath) return;
            }
            var updated = new EditorBuildSettingsScene[current.Length + 1];
            for (int i = 0; i < current.Length; i++) updated[i] = current[i];
            updated[current.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}
