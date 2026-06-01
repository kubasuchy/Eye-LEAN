// unity-component/Runtime/ExperimentRunner.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    /// <summary>Orchestrates a sequence of TrialSpecs from an ExperimentConfig.
    /// Resolves the environment (including procedural delegates), spawns agents,
    /// sets goals, writes the _ExperimentConfig.json + _PlannerLog.csv sidecars
    /// at session paths matching Eye-LEAN's SessionRecorder filename.</summary>
    public class ExperimentRunner : MonoBehaviour
    {
        [Header("Experiment")]
        public ExperimentConfig experiment;

        [Header("Session path")]
        [Tooltip("Directory where Eye-LEAN's SessionRecorder writes its CSV. " +
                 "Sidecars land here too.")]
        public string sessionLogDirectory = "Logs";
        [Tooltip("Base name (without extension). If empty, derived from current " +
                 "Eye-LEAN session at start.")]
        public string sessionBaseName = "";

        [Header("Integrations")]
        public HumanObstacleTracker humanTracker;

        /// <summary>Per-trial lifecycle event: (trialId, trial). Subscribers
        /// receive notifications even when sceneEventSink is overridden.
        /// Eye-LEAN's bridge uses these to set SessionRecorder context per
        /// trial.</summary>
        public event Action<string, TrialSpec> OnTrialStart;
        public event Action<string, TrialSpec> OnTrialEnd;

        /// <summary>Sink for trial-boundary scene events. Defaults to
        /// writing a navlab-owned `_SceneEvents.csv` next to the other
        /// sidecars. Override (Eye-LEAN bridge) with a closure that calls
        /// SceneEventRecorder.RecordKV so the events join the main session's
        /// SceneEvents stream and the replay path can re-anchor on them.
        /// Signature: (eventType, objectId, detail).</summary>
        public Action<string, string, string> sceneEventSink;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private PlannerLogger _logger;
        private string _basePath;
        private bool _requestEndTrial;

        /// <summary>Request immediate termination of the current trial.
        /// The normal OnTrialEnd event still fires. Safe to call when no
        /// trial is running (the flag resets at next trial start).</summary>
        public void EndCurrentTrial() => _requestEndTrial = true;

        /// <summary>Inject session context before Start() runs. Eye-LEAN's
        /// bridge calls this in Awake() so the sidecars land beside the main
        /// `EyeTracking_<session>.csv`. Must be called before Start() (i.e.
        /// from an earlier execution order or via [DefaultExecutionOrder] on
        /// the bridge).</summary>
        public void SetSessionContext(string sessionId, string logDirectory)
        {
            if (!string.IsNullOrEmpty(sessionId)) sessionBaseName = sessionId;
            if (!string.IsNullOrEmpty(logDirectory)) sessionLogDirectory = logDirectory;
        }

        private void Start()
        {
            if (experiment == null)
            {
                Debug.LogError("ExperimentRunner: experiment is not assigned.");
                return;
            }
            // Resolve procedural environment first
            experiment.environment.ResolveProcedural(experiment.randomSeed);
            _basePath = BasePath();
            WriteSidecars();
            StartCoroutine(RunTrials());
        }

        private void OnDestroy()
        {
            _logger?.Close();
        }

        private string BasePath()
        {
            string name = string.IsNullOrEmpty(sessionBaseName)
                ? $"EyeTracking_{System.DateTime.Now:yyyyMMdd_HHmmss}"
                : sessionBaseName;
            Directory.CreateDirectory(sessionLogDirectory);
            return Path.Combine(sessionLogDirectory, name);
        }

        private void WriteSidecars()
        {
            ExperimentConfigExporter.WriteToFile(experiment, _basePath + "_ExperimentConfig.json");
            _logger = new PlannerLogger(_basePath + "_PlannerLog.csv");
        }

        private IEnumerator RunTrials()
        {
            foreach (var trial in experiment.trials)
                yield return RunTrial(trial);
        }

        private IEnumerator RunTrial(TrialSpec trial)
        {
            DespawnAll();
            _requestEndTrial = false;
            EmitSceneEvent("TrialStart", trial.trialId);
            OnTrialStart?.Invoke(trial.trialId, trial);
            var agents = SpawnAgents(trial);
            float startTime = Time.time;
            while (Time.time - startTime < trial.durationSeconds && !_requestEndTrial)
                yield return null;
            EmitSceneEvent("TrialEnd", trial.trialId);
            OnTrialEnd?.Invoke(trial.trialId, trial);
            DespawnAll();
        }

        /// <summary>Emit one scene-boundary event. Routes through
        /// `sceneEventSink` when set (Eye-LEAN bridges this to
        /// SceneEventRecorder.RecordKV so the event lands in the main
        /// session's stream). Falls back to the navlab-owned
        /// `_SceneEvents.csv` so standalone navlab use is unchanged.</summary>
        private void EmitSceneEvent(string eventType, string detail)
        {
            if (sceneEventSink != null)
            {
                sceneEventSink(eventType, "", detail);
                return;
            }
            string ourSidecar = _basePath + "_SceneEvents.csv";
            bool isNew = !File.Exists(ourSidecar);
            using var writer = new StreamWriter(ourSidecar, append: true);
            if (isNew) writer.WriteLine("Frame,T,EventType,ObjectId,Detail");
            int frame = Time.frameCount;
            writer.WriteLine($"{frame},{Time.time:F4},{eventType},,{detail}");
        }

        private List<CompetitiveNavAgent> SpawnAgents(TrialSpec trial)
        {
            var result = new List<CompetitiveNavAgent>();
            foreach (var agentName in trial.activeAgentNames)
            {
                var spec = FindSpec(agentName);
                if (spec == null) continue;
                var spawn = experiment.environment.FindSpawn(spec.spawnRef);
                if (spawn == null) continue;
                var pos = new Vector3(spawn.Value.positionXZ.x, 0,
                                       spawn.Value.positionXZ.y);
                var rot = Quaternion.Euler(0, spawn.Value.headingDeg, 0);

                GameObject go;
                if (spec.agentType == AgentType.Human)
                {
                    // Human is the participant; no spawn — represented by XR camera.
                    continue;
                }
                if (spec.skin != null && spec.skin.prefab != null)
                {
                    go = Instantiate(spec.skin.prefab, pos, rot);
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.transform.position = pos;
                    go.transform.rotation = rot;
                }
                _spawned.Add(go);

                var agent = go.AddComponent<CompetitiveNavAgent>();
                agent.plannerType = spec.planner;
                agent.environment = experiment.environment;
                agent.humanTracker = humanTracker;
                agent.logger = _logger;

                // Resolve goal (with per-trial override)
                string goalRef = spec.goalRef;
                foreach (var ov in trial.goalOverrides)
                    if (ov.agentName == spec.name) goalRef = ov.goalRef;
                var goal = experiment.environment.FindGoal(goalRef);
                if (goal.HasValue) agent.SetGoal(goal.Value.positionXZ);

                result.Add(agent);
            }
            return result;
        }

        private AgentSpec FindSpec(string name)
        {
            foreach (var a in experiment.agents)
                if (a.name == name) return a;
            return null;
        }

        private void DespawnAll()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
        }
    }
}
