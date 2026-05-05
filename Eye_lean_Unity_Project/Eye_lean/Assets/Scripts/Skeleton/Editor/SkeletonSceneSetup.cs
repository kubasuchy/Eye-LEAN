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
    /// materializes a Skeleton scene with the manager trio + Eye_lean recorder
    /// trio pre-bootstrapped. The Skeleton is a developer-side template, NOT
    /// part of the Eye_lean APK build flow; the wizard does not add the scene
    /// to EditorBuildSettings. Re-running overwrites the scene.
    /// </summary>
    public static class SkeletonSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/Skeleton.unity";

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

            var managers = new GameObject("Managers");
            managers.AddComponent<TrialManager>();
            managers.AddComponent<ExperimentManager>();
            managers.AddComponent<AgentManager>();
            managers.AddComponent<EnvironmentManager>();

            var eyeRig = new GameObject("EyeTrackingSystem");
            eyeRig.AddComponent<EyeTracker>();
            eyeRig.AddComponent<HMDDataCollector>();
            eyeRig.AddComponent<SessionRecorder>();
            // RIPAMonitor + RIPACSVColumn are auto-attached by RIPAMonitorBootstrap at AfterSceneLoad

            var demoHandler = new GameObject("DemoPhaseHandler");
            demoHandler.AddComponent<EyeLean.Skeleton.Examples.EyeleanDemoPhaseHandler>();

            // Skeleton is developer-side: deliberately not added to EditorBuildSettings
            Directory.CreateDirectory(Path.GetDirectoryName(SCENE_PATH));
            EditorSceneManager.SaveScene(scene, SCENE_PATH);

            EditorUtility.DisplayDialog(
                "Skeleton scene created",
                $"Skeleton scene saved to:\n{SCENE_PATH}\n\n" +
                "The Skeleton is a researcher-side template — not part\n" +
                "of the Eye_lean APK build flow. It's NOT added to\n" +
                "Build Settings. Open the scene in the editor to\n" +
                "iterate on your experiment.\n\n" +
                "Next steps:\n" +
                "1. Create a TrialConfiguration asset (Assets > Create > Eye_lean > Skeleton Trial Configuration)\n" +
                "2. Drag it into TrialManager's 'Experiment Configuration' slot\n" +
                "3. Replace EyeleanDemoPhaseHandler with your own IExperimentPhaseHandler\n" +
                "4. Press Play in editor — RIPA + recording auto-bootstrap, no extra wiring needed\n" +
                "5. When ready to ship, add the scene to YOUR build settings.\n\n" +
                "See docs/SKELETON.md for the full walkthrough.",
                "OK");
        }
    }
}
