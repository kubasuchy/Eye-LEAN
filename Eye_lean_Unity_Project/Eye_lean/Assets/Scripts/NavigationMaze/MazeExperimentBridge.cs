// SPDX-License-Identifier: MIT
using System.IO;
using UnityEngine;
using Navlab.ExperimentRuntime;
using EyeTracking.Components;
using EyeLean.NavigationMaze.UI;
using EyeLean.NavigationMaze.Generation;

namespace EyeLean.NavigationMaze
{
    /// <summary>
    /// Eye_lean ↔ navlab bridge for the maze scene. Owns the
    /// <see cref="ExperimentRunner"/> lifecycle hooks so navlab's sidecars
    /// land next to Eye_lean's <c>EyeTracking_&lt;session&gt;.csv</c>, and
    /// proxies navlab's trial-boundary events into Eye_lean's
    /// <see cref="EyeLean.SceneState.SceneEventRecorder"/> so the replay
    /// path can re-anchor on them.
    ///
    /// Execution order is -50 so SetSessionContext fires before
    /// ExperimentRunner.Start() (default order 0) writes its sidecar files.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MazeExperimentBridge : MonoBehaviour
    {
        [Header("Bridge wiring")]
        [Tooltip("navlab ExperimentRunner in the scene. Auto-found if null.")]
        [SerializeField] private ExperimentRunner runner;
        [Tooltip("navlab HumanObstacleTracker. Auto-found if null. Its headCamera is wired to Camera.main at runtime so the participant becomes a dynamic obstacle for NPC replanners.")]
        [SerializeField] private HumanObstacleTracker humanTracker;

        [Header("Eye_lean")]
        [SerializeField] private SessionRecorder sessionRecorder;
        [SerializeField] private MazeHUDController hud;

        [Header("Defaults")]
        [Tooltip("Default participant ID when launched outside the MainMenu flow.")]
        [SerializeField] private string participantID = "P001";
        [Tooltip("Directory for sidecar output when SessionRecorder doesn't expose a path (editor fallback). Production runs read SessionRecorder.CsvFilePath instead.")]
        [SerializeField] private string fallbackLogDirectory = "Logs";

        [Header("Spawn")]
        [Tooltip("Name of the spawn point (from EnvironmentConfig.spawnPoints) the participant rig is teleported to at scene start. Defaults to 'S_human'. Set blank to disable teleport (rig stays at its scene-default position).")]
        [SerializeField] private string humanSpawnPointName = "S_human";
        [Tooltip("Participant eye height in meters, applied at spawn. Typical adult standing eye height is ~1.6m.")]
        [SerializeField] private float eyeHeightMeters = 1.6f;

        [Header("Navigation Suite")]
        [SerializeField] private MazeConfig mazeConfig;
        [SerializeField] private ProceduralMazeDelegate mazeDelegate;
        [SerializeField] private MazeEnvironmentRenderer environmentRenderer;
        [SerializeField] private MazeLandmarkPlacer landmarkPlacer;
        [SerializeField] private MazeCeilingBuilder ceilingBuilder;
        [SerializeField] private MazeDecisionPointTracker decisionPointTracker;
        [SerializeField] private EyeTracking.UI.WorldInstructionPanel instructionsPanel;

        [Header("Instructions")]
        [Tooltip("Seconds to show block instructions before auto-hiding.")]
        [SerializeField] private float instructionsDurationSeconds = 5f;
        [Tooltip("Distance in meters to goal marker that counts as 'reached'.")]
        [SerializeField] private float goalReachRadius = 1.5f;
        [Tooltip("Angular threshold in degrees for counting gaze as 'fixating on a landmark'. Typical foveal + parafoveal attention spans ~10-15°.")]
        [SerializeField] private float landmarkGazeAngleThreshold = 15f;

        private int activeTrialIndex = -1;
        private int replanCount;
        private System.Action<string, string, string> installedSink;
        private int currentBlockIndex = -1;
        private MazeBlockConfig currentBlock;
        private MazeGrid currentGrid;
        private JunctionType[,] currentJunctions;
        private MazeTrialMetrics currentTrialMetrics;
        private int trialIndexInBlock;
        private float trialStartTime;
        private float currentTrialDuration;
        private bool goalReached;
        private Vector3 goalWorldPos;
        private int sequentialGoalsReached;
        private int landmarkGazeFrames;
        private int totalTrialFrames;
        private bool goalResolved;

        private void Awake()
        {
            if (runner == null) runner = FindFirstObjectByType<ExperimentRunner>();
            // Hold the runner until world-space UI is placed against the
            // actual HMD pose (replay) or live VR-ready pose. Without this,
            // ExperimentRunner.Start fires RunTrials → OnTrialStart in the
            // same frame as our Start, racing the placement coroutine.
            // PlaceHudWhenCameraReady re-enables the runner after placement.
            if (runner != null) runner.enabled = false;
            if (humanTracker == null) humanTracker = FindFirstObjectByType<HumanObstacleTracker>();
            if (sessionRecorder == null) sessionRecorder = FindFirstObjectByType<SessionRecorder>();
            if (hud == null) hud = FindFirstObjectByType<MazeHUDController>();
            if (environmentRenderer == null) environmentRenderer = FindFirstObjectByType<MazeEnvironmentRenderer>();
            if (landmarkPlacer == null) landmarkPlacer = FindFirstObjectByType<MazeLandmarkPlacer>();
            if (ceilingBuilder == null) ceilingBuilder = FindFirstObjectByType<MazeCeilingBuilder>();
            if (decisionPointTracker == null) decisionPointTracker = FindFirstObjectByType<MazeDecisionPointTracker>();
            if (instructionsPanel == null) instructionsPanel = FindFirstObjectByType<EyeTracking.UI.WorldInstructionPanel>();
            if (instructionsPanel == null)
            {
                instructionsPanel = EyeTracking.UI.WorldInstructionPanel.Create(null);
                Debug.Log("[Maze] Auto-spawned WorldInstructionPanel — none found in scene.");
            }

            if (runner == null)
            {
                Debug.LogError("[Maze] No ExperimentRunner in scene. The maze cannot run without one.");
                return;
            }
            if (sessionRecorder == null)
            {
                Debug.LogError("[Maze] No SessionRecorder in scene. Sidecar files will land in the fallback directory instead of beside the main CSV.");
            }

            DeclareMetadataFields();

            if (mazeConfig != null && mazeDelegate != null)
            {
                EyeLean.SceneState.SceneEventRecorder.RecordJson("ConfigMazeSuite", "", new
                {
                    summary = mazeConfig.ToSummary(),
                    gridSize = mazeConfig.gridSize,
                    corridorWidth = mazeConfig.corridorWidth,
                    blockCount = mazeConfig.blocks.Length,
                    totalTrials = mazeConfig.TotalTrials
                });
            }

            WireHumanTracker();
            InjectSessionContext();
            WireRunnerEvents();
        }

        private void Start()
        {
            sessionRecorder?.SetParticipantID(participantID);
            sessionRecorder?.SetMetadata("SessionType", "NavigationMaze");
            sessionRecorder?.SetMetadata("ExperimentVersion", "1.0");

            if (runner != null && runner.experiment != null)
            {
                if (mazeConfig != null && mazeConfig.blocks != null && mazeConfig.blocks.Length > 0)
                    BuildTrialListFromConfig();

                EyeLean.SceneState.SceneEventRecorder.RecordJson("ConfigMaze", "", new MazeConfigSnapshot
                {
                    experimentName = runner.experiment.experimentName,
                    version = runner.experiment.version,
                    randomSeed = runner.experiment.randomSeed,
                    trialCount = runner.experiment.trials?.Count ?? 0,
                });

                UnityEngine.Random.InitState(runner.experiment.randomSeed);

                if (!EyeLean.Replay.SceneState.ReplayMode.IsActive)
                {
                    TeleportToSpawn(runner.experiment.environment);
                }
            }

            StartCoroutine(PlaceHudWhenCameraReady());
        }

        // Universal replay contract Rule 6: defer world-space UI placement
        // until the camera reflects the recorded HMD pose (replay) or live
        // HMD pose (recording). Without this, the HUD anchors to the scene-
        // default camera position before XR tracking engages.
        private System.Collections.IEnumerator PlaceHudWhenCameraReady()
        {
            if (EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                var rc = FindFirstObjectByType<EyeLean.Replay.ReplayController>();
                if (rc != null)
                {
                    float timeout = Time.realtimeSinceStartup + 30f;
                    while (!rc.IsPlaying && Time.realtimeSinceStartup < timeout)
                        yield return null;
                    for (int i = 0; i < 30; i++) yield return null;
                }
            }
            else
            {
                var readiness = EyeTracking.Core.VRReadinessService.Instance;
                if (readiness != null) yield return readiness.WaitForCameraReady(8f);
                yield return null;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                hud?.PlaceInFrontOf(cam.transform);
                instructionsPanel?.PlaceInFrontOf(cam.transform);
            }
            hud?.SetMessage("Maze ready");

            // Release the runner — its Start() will fire RunTrials, which
            // begins firing OnTrialStart. By now both panels are anchored
            // at the correct camera-relative positions.
            if (runner != null) runner.enabled = true;
        }

        private void BuildTrialListFromConfig()
        {
            runner.experiment.trials.Clear();
            int trialIdx = 0;
            for (int bi = 0; bi < mazeConfig.blocks.Length; bi++)
            {
                var block = mazeConfig.blocks[bi];
                for (int ti = 0; ti < block.trialsInBlock; ti++)
                {
                    var agents = new System.Collections.Generic.List<string> { "P_human" };
                    if (block.npcEnabled) agents.Add("NPC_1");

                    runner.experiment.trials.Add(new TrialSpec
                    {
                        trialId = $"b{bi}_{block.mode}_{ti}",
                        activeAgentNames = agents,
                        durationSeconds = block.trialDurationSeconds,
                        trialSeed = runner.experiment.randomSeed * 100 + trialIdx,
                    });
                    trialIdx++;
                }
            }
            Debug.Log($"[Maze] Built {runner.experiment.trials.Count} trials from {mazeConfig.blocks.Length} MazeConfig blocks.");
        }

        private void TeleportToSpawn(Navlab.ExperimentRuntime.EnvironmentConfig env)
        {
            if (env == null || env.spawnPoints == null || env.spawnPoints.Count == 0) return;
            if (string.IsNullOrEmpty(humanSpawnPointName)) return;

            Navlab.ExperimentRuntime.NamedPoint spawn = default;
            bool found = false;
            foreach (var p in env.spawnPoints)
            {
                if (string.Equals(p.name, humanSpawnPointName, System.StringComparison.OrdinalIgnoreCase))
                {
                    spawn = p;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Debug.LogWarning($"[Maze] Spawn point '{humanSpawnPointName}' not found in EnvironmentConfig.spawnPoints. Camera stays at scene-default position.");
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[Maze] No Camera.main resolved at bridge Start; spawn teleport skipped.");
                return;
            }
            // Prefer the rig root (cam.parent) over the camera itself so
            // XR's per-frame head-pose update doesn't fight the move. On
            // editor without XR, parent is typically null so we fall back
            // to the camera transform.
            Transform rig = cam.transform.parent != null ? cam.transform.parent : cam.transform;
            rig.position = new Vector3(spawn.positionXZ.x, eyeHeightMeters, spawn.positionXZ.y);
            rig.rotation = Quaternion.Euler(0f, spawn.headingDeg, 0f);

            Debug.Log($"[Maze] Spawned rig at '{spawn.name}' = ({spawn.positionXZ.x:F2}, {eyeHeightMeters:F2}, {spawn.positionXZ.y:F2}) facing {spawn.headingDeg:F0}°.");
        }

        private void DeclareMetadataFields()
        {
            if (sessionRecorder == null) return;
            sessionRecorder.DeclareMetadataField("SessionType", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("ExperimentVersion", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("MazeTrialId", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("MazeCondition", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("MazeActiveAgents", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("MazeReplanCount", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("MazeGoalReached", EyeLean.Data.MetadataValueType.Bool);
            sessionRecorder.DeclareMetadataField("MazeDifficulty", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("MazeBlockIndex", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("MazePhase", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("MazeCeiling", EyeLean.Data.MetadataValueType.Bool);
            sessionRecorder.DeclareMetadataField("MazeLandmarkCondition", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("MazeOptimalPathLength", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("MazeActualPathLength", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("MazePathEfficiency", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("MazeDecisionPointsOnPath", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("MazeWrongTurns", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("MazeDeadEndEntries", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("MazeBacktrackCount", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("MazeTimeToCompletion", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("MazeLandmarkFixationRatio", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("MazeSequentialGoalsCompleted", EyeLean.Data.MetadataValueType.Int);
        }

        private void WireHumanTracker()
        {
            if (humanTracker == null) return;
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[Maze] Camera.main is null at Awake — humanTracker.headCamera will resolve via its own Start-time lookup, which may pick an inactive camera under XR. If NPCs appear to ignore the participant, check XR init order.");
                return;
            }
            // HumanObstacleTracker.headCamera is a public field; assigning
            // it here removes the race with XR camera assignment order.
            humanTracker.headCamera = cam;
        }

        private void InjectSessionContext()
        {
            if (runner == null) return;
            string sessionId = sessionRecorder != null ? sessionRecorder.SessionId : null;
            string logDir = ResolveLogDirectory();
            string baseName = string.IsNullOrEmpty(sessionId)
                ? "EyeTracking_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss")
                : "EyeTracking_" + sessionId;
            runner.SetSessionContext(baseName, logDir);
        }

        private string ResolveLogDirectory()
        {
            if (sessionRecorder == null || string.IsNullOrEmpty(sessionRecorder.CsvFilePath))
            {
                Directory.CreateDirectory(fallbackLogDirectory);
                return fallbackLogDirectory;
            }
            string dir = Path.GetDirectoryName(sessionRecorder.CsvFilePath);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private void WireRunnerEvents()
        {
            if (runner == null) return;

            // Proxy navlab's trial-boundary events into Eye_lean's
            // SceneEvents stream so the replay system sees them in one
            // unified timeline. The runner's per-trial defaults stay
            // intact: it still spawns / despawns agents and runs the
            // duration loop; we just hook the boundaries.
            installedSink = (eventType, objectId, detail) =>
            {
                EyeLean.SceneState.SceneEventRecorder.Record(
                    "Maze" + eventType,
                    objectId ?? "",
                    detail ?? "");
            };
            runner.sceneEventSink = installedSink;

            runner.OnTrialStart += HandleTrialStart;
            runner.OnTrialEnd += HandleTrialEnd;
        }

        private void Update()
        {
            if (activeTrialIndex < 0 || currentBlock == null) return;

            float elapsed = Time.time - trialStartTime;
            float remaining = currentTrialDuration - elapsed;
            if (remaining > 0f) hud?.SetTimer(remaining);

            var cam = Camera.main;
            if (cam != null)
            {
                if (!goalReached && goalResolved && currentBlock.mode != MazeTrialMode.Exploration)
                {
                    float dist = Vector3.Distance(cam.transform.position, goalWorldPos);
                    if (dist < goalReachRadius)
                    {
                        goalReached = true;
                        sequentialGoalsReached++;
                        float reachTime = elapsed;
                        sessionRecorder?.SetMetadata("MazeGoalReached", true);
                        sessionRecorder?.SetMetadata("MazeTimeToCompletion", reachTime);
                        sessionRecorder?.SetMetadata("MazeSequentialGoalsCompleted", sequentialGoalsReached);
                        hud?.SetStatus("<color=#33DD55>Goal reached!</color>");
                        EyeLean.SceneState.SceneEventRecorder.RecordKV("MazeGoalReached", "G_human",
                            ("timeSeconds", reachTime.ToString("F2")),
                            ("sequentialIndex", sequentialGoalsReached.ToString()));
                        runner?.EndCurrentTrial();
                    }
                }

                totalTrialFrames++;
                if (landmarkPlacer != null && landmarkPlacer.ActiveLandmarks.Count > 0)
                {
                    // Use EyeTrackerFactory so replay's ReplayingEyeTracker
                    // transparently supplies recorded gaze. Falls back to
                    // head pose only if no tracker is available.
                    Vector3 gazeDir = cam.transform.forward;
                    Vector3 origin = cam.transform.position;
                    var tracker = EyeTracking.Core.EyeTrackerFactory.GetEyeTracker();
                    if (tracker != null && tracker.IsAvailable
                        && tracker.GetCombinedGazeOrigin(out Vector3 trackerOrigin)
                        && tracker.GetCombinedGazeDirection(out Vector3 trackerDir))
                    {
                        origin = trackerOrigin;
                        gazeDir = trackerDir;
                    }
                    foreach (var lm in landmarkPlacer.ActiveLandmarks)
                    {
                        if (lm == null) continue;
                        Vector3 toLandmark = (lm.transform.position - origin).normalized;
                        if (Vector3.Angle(gazeDir, toLandmark) < landmarkGazeAngleThreshold)
                        {
                            landmarkGazeFrames++;
                            break;
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (runner == null) return;
            runner.OnTrialStart -= HandleTrialStart;
            runner.OnTrialEnd -= HandleTrialEnd;
            // Only clear the sink if it's still the one we installed —
            // another component may have installed its own after us.
            if (runner.sceneEventSink == installedSink)
            {
                runner.sceneEventSink = null;
            }
        }

        private void TransitionToBlock(int blockIndex)
        {
            if (mazeConfig == null || mazeDelegate == null) return;
            currentBlockIndex = blockIndex;
            currentBlock = mazeConfig.blocks[blockIndex];
            trialIndexInBlock = 0;

            if (!currentBlock.reusesPreviousBlockMaze || currentGrid == null)
            {
                int blockSeed = currentBlock.mazeSeed >= 0
                    ? currentBlock.mazeSeed
                    : (runner.experiment.randomSeed * 31 + blockIndex);
                mazeDelegate.Configure(mazeConfig, currentBlock);
                runner.experiment.environment.ResolveProcedural(blockSeed);
                currentGrid = mazeDelegate.LastGrid;
                currentJunctions = MazeCellClassifier.ClassifyAll(currentGrid);

                if (environmentRenderer != null) environmentRenderer.Rebuild();
            }

            if (ceilingBuilder != null)
            {
                if (currentBlock.ceilingEnabled)
                    ceilingBuilder.Build(mazeConfig.gridSize * mazeConfig.corridorWidth, mazeConfig.wallHeight);
                else
                    ceilingBuilder.Destroy();
            }

            if (landmarkPlacer != null)
            {
                float mazeSize = mazeConfig.gridSize * mazeConfig.corridorWidth;
                switch (currentBlock.landmarkCondition)
                {
                    case LandmarkCondition.Distal:
                        landmarkPlacer.PlaceDistalLandmarks(mazeConfig, mazeSize);
                        break;
                    case LandmarkCondition.Proximal:
                        landmarkPlacer.PlaceProximalLandmarks(mazeConfig, currentGrid, currentJunctions);
                        break;
                    default:
                        landmarkPlacer.ClearAll();
                        break;
                }
            }

            if (decisionPointTracker != null)
            {
                var cam = Camera.main;
                var rig = cam != null
                    ? (cam.transform.parent != null ? cam.transform.parent : cam.transform)
                    : null;
                decisionPointTracker.Initialize(currentGrid, currentJunctions, rig);
            }

            EyeLean.SceneState.SceneEventRecorder.RecordKV("MazeBlockStart", "",
                ("blockIndex", blockIndex.ToString()),
                ("mode", currentBlock.mode.ToString()),
                ("difficulty", currentBlock.difficulty.ToString()),
                ("ceiling", currentBlock.ceilingEnabled.ToString()),
                ("landmarks", currentBlock.landmarkCondition.ToString()));

            if (instructionsPanel != null)
            {
                string title = $"Block {blockIndex + 1} — {currentBlock.mode}";
                string body = InstructionsForMode(currentBlock.mode, currentBlock);
                instructionsPanel.Show(title, body);
                StartCoroutine(HideInstructionsAfterDelay());
            }
        }

        private System.Collections.IEnumerator HideInstructionsAfterDelay()
        {
            yield return new WaitForSeconds(instructionsDurationSeconds);
            if (instructionsPanel != null) instructionsPanel.Hide();
        }

        private static string InstructionsForMode(MazeTrialMode mode, MazeBlockConfig block)
        {
            switch (mode)
            {
                case MazeTrialMode.Exploration:
                    return "Explore the maze freely.\nLook around and learn the layout.";
                case MazeTrialMode.Wayfinding:
                    return "Navigate to the green goal marker.\nFind the most direct route you can.";
                case MazeTrialMode.Sequential:
                    return $"Visit {block.sequentialGoalCount} goal markers in order.\nEach will light up when reached.";
                case MazeTrialMode.Competitive:
                    return "Race to the goal!\nAnother agent is also navigating the maze.";
                case MazeTrialMode.Probe:
                    return "Navigate to the goal marker.\nSome cues may have changed.";
                default:
                    return "Navigate the maze.";
            }
        }

        private int ResolveBlockForTrial(int globalTrialIndex)
        {
            int cumulative = 0;
            for (int i = 0; i < mazeConfig.blocks.Length; i++)
            {
                cumulative += mazeConfig.blocks[i].trialsInBlock;
                if (globalTrialIndex < cumulative) return i;
            }
            return mazeConfig.blocks.Length - 1;
        }

        private void HandleTrialStart(string trialId, TrialSpec trial)
        {
            activeTrialIndex++;
            replanCount = 0;
            trialStartTime = Time.time;
            sequentialGoalsReached = 0;
            goalReached = false;
            goalResolved = false;
            landmarkGazeFrames = 0;
            totalTrialFrames = 0;
            currentTrialDuration = trial.durationSeconds;

            if (mazeConfig != null && mazeConfig.blocks.Length > 0)
            {
                int targetBlock = ResolveBlockForTrial(activeTrialIndex);
                if (targetBlock != currentBlockIndex)
                    TransitionToBlock(targetBlock);
                trialIndexInBlock++;

                if (currentBlock != null && currentBlock.mode == MazeTrialMode.Sequential && currentBlock.sequentialGoalCount > 1)
                    Debug.LogWarning($"[Maze] Sequential mode with {currentBlock.sequentialGoalCount} goals requested, but multi-goal placement is not yet implemented. Only the single G_human goal will be tracked.");
            }

            int npcCount = 0;
            string activeAgents = "";
            if (trial.activeAgentNames != null)
            {
                activeAgents = string.Join(",", trial.activeAgentNames);
                foreach (var name in trial.activeAgentNames)
                    if (!string.Equals(name, "P_human", System.StringComparison.OrdinalIgnoreCase))
                        npcCount++;
            }
            string condition = npcCount > 0 ? "competitive" : "solo";

            if (!EyeLean.Replay.SceneState.ReplayMode.IsActive)
                TeleportToSpawn(runner.experiment.environment);

            if (currentGrid != null)
            {
                var spawnPt = runner.experiment.environment.FindSpawn("S_human");
                var goalPt = runner.experiment.environment.FindGoal("G_human");
                if (spawnPt.HasValue && goalPt.HasValue)
                {
                    goalWorldPos = new Vector3(goalPt.Value.positionXZ.x, eyeHeightMeters, goalPt.Value.positionXZ.y);
                    goalResolved = true;
                    var (sr, sc) = currentGrid.WorldToCell(spawnPt.Value.positionXZ.x, spawnPt.Value.positionXZ.y);
                    var (gr, gc) = currentGrid.WorldToCell(goalPt.Value.positionXZ.x, goalPt.Value.positionXZ.y);
                    currentTrialMetrics = MazePathAnalyzer.ComputeOptimalPath(currentGrid, sr, sc, gr, gc);
                    decisionPointTracker?.BeginTrial(currentTrialMetrics.optimalPath);
                }
                else
                {
                    Debug.LogWarning("[Maze] Spawn or goal point not resolved from EnvironmentConfig. Goal-reach detection disabled for this trial.");
                }
            }

            sessionRecorder?.SetSessionContext(activeTrialIndex, "MazeTrial", "Maze", condition);
            sessionRecorder?.SetMetadata("MazeTrialId", trialId ?? "");
            sessionRecorder?.SetMetadata("MazeCondition", condition);
            sessionRecorder?.SetMetadata("MazeActiveAgents", activeAgents);
            sessionRecorder?.SetMetadata("MazeReplanCount", 0);
            sessionRecorder?.SetMetadata("MazeGoalReached", false);

            if (currentBlock != null)
            {
                sessionRecorder?.SetMetadata("MazeDifficulty", currentBlock.difficulty);
                sessionRecorder?.SetMetadata("MazeBlockIndex", currentBlockIndex);
                sessionRecorder?.SetMetadata("MazePhase", currentBlock.mode.ToString());
                sessionRecorder?.SetMetadata("MazeCeiling", currentBlock.ceilingEnabled);
                sessionRecorder?.SetMetadata("MazeLandmarkCondition", currentBlock.landmarkCondition.ToString());
                sessionRecorder?.SetMetadata("MazeOptimalPathLength", currentTrialMetrics.optimalPathLength);
                sessionRecorder?.SetMetadata("MazeDecisionPointsOnPath", currentTrialMetrics.decisionPointsOnOptimalPath);
            }

            if (mazeConfig != null && currentBlock != null)
                hud?.SetBlockInfo(currentBlockIndex, mazeConfig.blocks.Length, currentBlock.mode.ToString());

            hud?.SetTrial(activeTrialIndex, mazeConfig != null ? mazeConfig.TotalTrials : (runner.experiment.trials?.Count ?? 0), trialId, condition);
        }

        private void HandleTrialEnd(string trialId, TrialSpec trial)
        {
            float elapsed = Time.time - trialStartTime;
            if (!goalReached)
            {
                sessionRecorder?.SetMetadata("MazeGoalReached", false);
                sessionRecorder?.SetMetadata("MazeTimeToCompletion", elapsed);
            }
            sessionRecorder?.SetMetadata("MazeReplanCount", replanCount);

            if (decisionPointTracker != null)
            {
                sessionRecorder?.SetMetadata("MazeActualPathLength", decisionPointTracker.ActualPathLength);
                sessionRecorder?.SetMetadata("MazeWrongTurns", decisionPointTracker.WrongTurns);
                sessionRecorder?.SetMetadata("MazeDeadEndEntries", decisionPointTracker.DeadEndEntries);
                sessionRecorder?.SetMetadata("MazeBacktrackCount", decisionPointTracker.BacktrackCount);

                float efficiency = currentTrialMetrics.optimalPathLength > 0
                    ? currentTrialMetrics.optimalPathLength / Mathf.Max(decisionPointTracker.ActualPathLength, 0.01f)
                    : 0f;
                sessionRecorder?.SetMetadata("MazePathEfficiency", efficiency);
            }

            float landmarkRatio = totalTrialFrames > 0
                ? (float)landmarkGazeFrames / totalTrialFrames
                : 0f;
            sessionRecorder?.SetMetadata("MazeLandmarkFixationRatio", landmarkRatio);
            sessionRecorder?.SetMetadata("MazeSequentialGoalsCompleted",
                currentBlock != null && currentBlock.mode == MazeTrialMode.Sequential
                    ? sequentialGoalsReached : 0);
        }

        [System.Serializable]
        private struct MazeConfigSnapshot
        {
            public string experimentName;
            public string version;
            public int randomSeed;
            public int trialCount;
        }
    }
}
