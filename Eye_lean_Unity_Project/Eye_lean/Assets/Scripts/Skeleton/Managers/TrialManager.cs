// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EyeTracking.Components;
using EyeLean.SceneState;

namespace EyeLean.Skeleton
{
    /// <summary>
    /// Trial-level state machine: ITI -> WaitingOnPlatform -> FixationCross ->
    /// ExperimentalPhase -> TrialComplete. ExperimentalPhase delegates to a
    /// researcher-provided <see cref="IExperimentPhaseHandler"/>. Auto-wires into
    /// <c>SessionRecorder</c> (per-frame CSV phase/trial context) and
    /// <c>SceneEventRecorder</c> (sidecar trial events). Seeds
    /// <c>UnityEngine.Random</c> from <see cref="randomSeed"/> for deterministic
    /// block / trial shuffles. Recordable objects spawned per-trial should call
    /// <c>SceneStateRecorder.MarkRecordableSeeded</c> for deterministic replay.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class TrialManager : MonoBehaviour
    {
        [Header("Trial Configuration")]
        [Tooltip("ScriptableObject containing the complete experimental design")]
        [SerializeField] private TrialConfiguration experimentConfiguration;

        [Header("Fallback Configuration (used if no ScriptableObject assigned)")]
        [SerializeField] private int fallbackTotalTrials = 20;
        [SerializeField] private int fallbackNumberOfBlocks = 2;
        [SerializeField] private bool fallbackRandomizeTrialOrder = true;

        [Header("Phase Timing")]
        [Tooltip("Duration of fixation cross phase in seconds (0 = use FixationCross component setting)")]
        [SerializeField] private float fixationCrossDuration = 0.0f;

        [Header("Determinism")]
        [Tooltip("UnityEngine.Random.InitState seed. Use the same value for byte-identical re-runs.")]
        [SerializeField] private int randomSeed = 12345;

        [Header("Eye_lean wiring (auto-found if null)")]
        [Tooltip("SessionRecorder writes per-trial context into the per-frame CSV. Auto-found at Start.")]
        [SerializeField] private SessionRecorder sessionRecorder;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        public enum TrialPhase
        {
            InterTrialInterval,
            WaitingOnPlatform,
            FixationCross,
            ExperimentalPhase,
            TrialComplete
        }

        [System.Serializable]
        public class TrialData
        {
            public int trialNumber;
            public int blockNumber;
            public int indexInBlock;
            public string blockName = "";
            public float trialStartTime;
            public float trialEndTime;
            public bool isCompleted;
            public bool isFailed;
            public string failureReason = "";
            public Dictionary<string, object> experimentData = new Dictionary<string, object>();
            public float TrialDuration => trialEndTime - trialStartTime;
        }

        private TrialPhase currentPhase = TrialPhase.InterTrialInterval;
        private int currentTrialIndex = 0;
        private readonly List<TrialData> allTrials = new List<TrialData>();
        private TrialData currentTrial;
        private TrialBlock currentBlock;
        private float phaseStartTime;
        private bool phaseTimerActive;
        private IExperimentPhaseHandler experimentPhaseHandler;
        private bool randomSeedDeclared;

        public System.Action<TrialPhase> OnPhaseChanged;
        public System.Action<TrialData> OnTrialStarted;
        public System.Action<TrialData> OnTrialCompleted;
        public System.Action OnAllTrialsCompleted;

        private void Awake()
        {
            if (sessionRecorder == null) sessionRecorder = FindFirstObjectByType<SessionRecorder>();
            // Declare in Awake so fields land before SessionRecorder.Start writes the
            // CSV header. Researcher-defined fields from GetPhaseData are declared
            // lazily on first SetMetadata.
            if (sessionRecorder != null)
            {
                sessionRecorder.DeclareMetadataField("TrialBlockName", EyeLean.Data.MetadataValueType.String);
                sessionRecorder.DeclareMetadataField("TrialIndexInBlock", EyeLean.Data.MetadataValueType.Int);
                sessionRecorder.DeclareMetadataField("TrialFailed", EyeLean.Data.MetadataValueType.Bool);
                sessionRecorder.DeclareMetadataField("TrialFailureReason", EyeLean.Data.MetadataValueType.String);
            }
        }

        private void Start()
        {
            // Deterministic seeding for block/trial shuffles.
            UnityEngine.Random.InitState(randomSeed);
            EmitConfigSnapshot();
            EmitRandomSeedRecord();
            InitializeTrialSequence();
        }

        private void Update()
        {
            if (phaseTimerActive) UpdatePhaseTimer();

            if (currentPhase == TrialPhase.ExperimentalPhase && experimentPhaseHandler != null)
            {
                if (experimentPhaseHandler.IsPhaseComplete()) CompleteExperimentalPhase();
            }
        }

        // -------- Initialization --------

        private void InitializeTrialSequence()
        {
            allTrials.Clear();
            if (experimentConfiguration != null) InitializeFromConfiguration();
            else InitializeFromFallback();

            if (allTrials.Count > 0)
            {
                currentTrial = allTrials[0];
                UpdateCurrentBlock();
                SetPhase(TrialPhase.InterTrialInterval);
            }
        }

        private void InitializeFromConfiguration()
        {
            if (!experimentConfiguration.IsValid(out string msg))
            {
                Debug.LogError($"[TrialManager] Invalid TrialConfiguration: {msg}");
                InitializeFromFallback();
                return;
            }
            if (showDebugInfo) Debug.Log($"[TrialManager] Initializing from TrialConfiguration: {experimentConfiguration.experimentName}");

            var blocks = experimentConfiguration.blocks;
            if (experimentConfiguration.randomizeBlockOrder)
                blocks = blocks.OrderBy(_ => UnityEngine.Random.value).ToList();

            int trialNumber = 1;
            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                var blockTrials = new List<TrialData>();
                for (int i = 0; i < block.trialsInBlock; i++)
                {
                    blockTrials.Add(new TrialData
                    {
                        trialNumber = trialNumber++,
                        blockNumber = b + 1,
                        indexInBlock = i + 1,
                        blockName = block.blockName,
                    });
                }
                if (experimentConfiguration.randomizeTrialOrder)
                    blockTrials = blockTrials.OrderBy(_ => UnityEngine.Random.value).ToList();
                allTrials.AddRange(blockTrials);
            }
            if (showDebugInfo) Debug.Log($"[TrialManager] Initialized {allTrials.Count} trials across {blocks.Count} blocks");
        }

        private void InitializeFromFallback()
        {
            Debug.LogWarning("[TrialManager] No TrialConfiguration assigned, using fallback settings");
            int trialsPerBlock = Mathf.CeilToInt((float)fallbackTotalTrials / fallbackNumberOfBlocks);
            int trialNumber = 1;
            for (int b = 0; b < fallbackNumberOfBlocks; b++)
            {
                var blockTrials = new List<TrialData>();
                int trialsInThisBlock = Mathf.Min(trialsPerBlock, fallbackTotalTrials - b * trialsPerBlock);
                for (int i = 0; i < trialsInThisBlock; i++)
                {
                    blockTrials.Add(new TrialData
                    {
                        trialNumber = trialNumber++,
                        blockNumber = b + 1,
                        indexInBlock = i + 1,
                        blockName = $"Block {b + 1}",
                    });
                }
                if (fallbackRandomizeTrialOrder)
                    blockTrials = blockTrials.OrderBy(_ => UnityEngine.Random.value).ToList();
                allTrials.AddRange(blockTrials);
            }
        }

        private void UpdateCurrentBlock()
        {
            currentBlock = currentTrial != null && experimentConfiguration != null
                ? experimentConfiguration.blocks.FirstOrDefault(b => b.blockName == currentTrial.blockName)
                : null;
        }

        // -------- Phase machine --------

        public void SetPhase(TrialPhase newPhase)
        {
            if (currentPhase == newPhase) return;
            var prev = currentPhase;
            currentPhase = newPhase;
            if (showDebugInfo) Debug.Log($"[TrialManager] Phase: {prev} → {newPhase} (Trial {currentTrialIndex + 1}/{allTrials.Count})");
            StopPhaseTimer();

            // Eye_lean: write the live phase to the per-frame CSV's SubTask
            // column AND emit a TrialPhaseChanged event row.
            if (sessionRecorder != null) sessionRecorder.SetSubTask(newPhase.ToString());
            SceneEventRecorder.RecordKV(
                "TrialPhaseChanged",
                "",
                ("phase", newPhase.ToString()),
                ("trial", (currentTrial?.trialNumber ?? 0).ToString())
            );

            switch (newPhase)
            {
                case TrialPhase.InterTrialInterval:
                    break;
                case TrialPhase.WaitingOnPlatform:
                    break;
                case TrialPhase.FixationCross:
                    if (currentTrial != null)
                    {
                        currentTrial.trialStartTime = Time.time;
                        // Eye_lean: bind trial-level context to the recorder
                        // BEFORE the first sample of the trial lands.
                        if (sessionRecorder != null)
                        {
                            string config = experimentConfiguration != null ? experimentConfiguration.experimentName : "Skeleton";
                            sessionRecorder.SetSessionContext(
                                trialNumber: currentTrial.trialNumber,
                                phase: "ExperimentalPhase",
                                config: config,
                                subTask: newPhase.ToString());
                            sessionRecorder.SetMetadata("TrialBlockName", currentTrial.blockName);
                            sessionRecorder.SetMetadata("TrialIndexInBlock", currentTrial.indexInBlock);
                        }
                        OnTrialStarted?.Invoke(currentTrial);
                        SceneEventRecorder.RecordKV(
                            "TrialStarted",
                            "",
                            ("trial", currentTrial.trialNumber.ToString()),
                            ("block", currentTrial.blockNumber.ToString()),
                            ("blockName", currentTrial.blockName),
                            ("indexInBlock", currentTrial.indexInBlock.ToString())
                        );
                    }
                    if (fixationCrossDuration > 0) StartPhaseTimer();
                    break;
                case TrialPhase.ExperimentalPhase:
                    experimentPhaseHandler?.OnPhaseStart();
                    break;
                case TrialPhase.TrialComplete:
                    CompleteCurrentTrial();
                    break;
            }

            // Notify UI listeners (except for transitional TrialComplete state).
            if (newPhase != TrialPhase.TrialComplete) OnPhaseChanged?.Invoke(newPhase);
        }

        private void UpdatePhaseTimer()
        {
            float elapsed = Time.time - phaseStartTime;
            switch (currentPhase)
            {
                case TrialPhase.FixationCross:
                    if (fixationCrossDuration > 0 && elapsed >= fixationCrossDuration)
                        SetPhase(TrialPhase.ExperimentalPhase);
                    break;
            }
        }

        private void StartPhaseTimer() { phaseStartTime = Time.time; phaseTimerActive = true; }
        private void StopPhaseTimer() { phaseTimerActive = false; }

        // -------- Phase handler --------

        public void SetPhaseHandler(IExperimentPhaseHandler handler)
        {
            experimentPhaseHandler = handler;
            if (showDebugInfo) Debug.Log($"[TrialManager] Phase handler registered: {handler.GetType().Name}");
        }

        public void CompleteExperimentalPhase()
        {
            if (currentPhase != TrialPhase.ExperimentalPhase) return;
            if (experimentPhaseHandler != null)
            {
                experimentPhaseHandler.OnPhaseEnd();
                var data = experimentPhaseHandler.GetPhaseData();
                if (data != null && currentTrial != null)
                {
                    foreach (var kvp in data) currentTrial.experimentData[kvp.Key] = kvp.Value;
                }
            }
            SetPhase(TrialPhase.TrialComplete);
        }

        // -------- Completion --------

        private void CompleteCurrentTrial()
        {
            if (currentTrial == null) return;
            currentTrial.isCompleted = true;
            currentTrial.trialEndTime = Time.time;
            if (showDebugInfo) Debug.Log($"[TrialManager] Trial {currentTrial.trialNumber} completed in {currentTrial.TrialDuration:F2}s");

            // Eye_lean: flush experimentData into custom metadata + emit
            // TrialCompleted KV row.
            FlushExperimentDataToRecorders(currentTrial);
            OnTrialCompleted?.Invoke(currentTrial);
            AdvanceToNextTrial();
        }

        public void FailCurrentTrial(string reason)
        {
            if (currentTrial == null) return;
            currentTrial.isFailed = true;
            currentTrial.failureReason = reason;
            currentTrial.trialEndTime = Time.time;
            Debug.LogWarning($"[TrialManager] Trial {currentTrial.trialNumber} failed: {reason}");
            FlushExperimentDataToRecorders(currentTrial);
            OnTrialCompleted?.Invoke(currentTrial);
            AdvanceToNextTrial();
        }

        private void FlushExperimentDataToRecorders(TrialData trial)
        {
            if (sessionRecorder != null)
            {
                sessionRecorder.SetMetadata("TrialFailed", trial.isFailed);
                sessionRecorder.SetMetadata("TrialFailureReason", trial.failureReason ?? "");
                if (trial.experimentData != null)
                {
                    foreach (var kvp in trial.experimentData)
                    {
                        WriteMetadataDynamically(kvp.Key, kvp.Value);
                    }
                }
            }
            // Sidecar event row mirrors the per-frame CSV write so analysts
            // can join trials by event timestamp without column lookups.
            var kvParts = new List<(string, string)>
            {
                ("trial", trial.trialNumber.ToString()),
                ("duration", trial.TrialDuration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)),
                ("failed", trial.isFailed.ToString()),
            };
            if (trial.experimentData != null)
            {
                foreach (var kvp in trial.experimentData)
                {
                    kvParts.Add((kvp.Key, kvp.Value?.ToString() ?? ""));
                }
            }
            SceneEventRecorder.RecordKV("TrialCompleted", "", kvParts.ToArray());
        }

        // SetMetadata has typed overloads. Pick one based on the actual
        // runtime value type so the CSV gets the right serialization
        // (and the schema-lock check fires correctly).
        private void WriteMetadataDynamically(string fieldName, object value)
        {
            if (sessionRecorder == null) return;
            switch (value)
            {
                case bool b:   sessionRecorder.SetMetadata(fieldName, b); break;
                case int i:    sessionRecorder.SetMetadata(fieldName, i); break;
                case long l:   sessionRecorder.SetMetadata(fieldName, (int)l); break;
                case float f:  sessionRecorder.SetMetadata(fieldName, f); break;
                case double d: sessionRecorder.SetMetadata(fieldName, (float)d); break;
                case null:     sessionRecorder.SetMetadata(fieldName, ""); break;
                default:       sessionRecorder.SetMetadata(fieldName, value.ToString()); break;
            }
        }

        private void AdvanceToNextTrial()
        {
            currentTrialIndex++;
            if (currentTrialIndex >= allTrials.Count)
            {
                if (showDebugInfo) Debug.Log("[TrialManager] All trials completed!");
                SceneEventRecorder.Record("AllTrialsCompleted", "", $"total={allTrials.Count}");
                OnAllTrialsCompleted?.Invoke();
                return;
            }
            currentTrial = allTrials[currentTrialIndex];
            UpdateCurrentBlock();
            SetPhase(TrialPhase.InterTrialInterval);
        }

        public void OnPlatformActivated()
        {
            if (currentPhase == TrialPhase.WaitingOnPlatform) SetPhase(TrialPhase.FixationCross);
        }

        // -------- Eye_lean snapshots --------

        private void EmitConfigSnapshot()
        {
            if (experimentConfiguration == null) return;
            // RecordJson Base64-encodes the payload so commas / newlines in
            // descriptions don't break CSV column alignment.
            SceneEventRecorder.RecordJson("ConfigTrials", "", experimentConfiguration);
        }

        private void EmitRandomSeedRecord()
        {
            if (randomSeedDeclared) return;
            SceneEventRecorder.RecordKV("RandomSeed", "",
                ("seed", randomSeed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            randomSeedDeclared = true;
        }

        // -------- Public read-only API --------

        public TrialPhase GetCurrentPhase() => currentPhase;
        public TrialData GetCurrentTrial() => currentTrial;
        public TrialBlock GetCurrentBlock() => currentBlock;
        public int GetCurrentTrialIndex() => currentTrialIndex;
        public bool IsUsingConfiguration() => experimentConfiguration != null;
        public bool AreAllTrialsCompleted() => currentTrialIndex >= allTrials.Count;
        public TrialConfiguration GetExperimentConfiguration() => experimentConfiguration;
        public float GetFixationCrossDuration() => fixationCrossDuration;
        public string GetCurrentPhaseString() => currentPhase.ToString();

        public string GetProgressInfo()
        {
            if (allTrials.Count == 0) return "No trials configured";
            int done = allTrials.Count(t => t.isCompleted || t.isFailed);
            int totalBlocks = experimentConfiguration != null ? experimentConfiguration.TotalBlocks : fallbackNumberOfBlocks;
            return $"Trial {currentTrialIndex + 1}/{allTrials.Count} (Block {currentTrial?.blockNumber}/{totalBlocks}) - {done} completed";
        }

        public float GetPhaseRemainingTime()
        {
            if (!phaseTimerActive) return 0f;
            float elapsed = Time.time - phaseStartTime;
            return currentPhase == TrialPhase.FixationCross ? Mathf.Max(0f, fixationCrossDuration - elapsed) : 0f;
        }

        // -------- Debug HUD --------

        private void OnGUI()
        {
            if (!showDebugInfo || !Application.isPlaying) return;
            GUILayout.BeginArea(new Rect(10, 10, 320, 160));
            GUILayout.Label("<b>TRIAL MANAGER</b>");
            GUILayout.Label($"Phase: {currentPhase}");
            GUILayout.Label($"Progress: {GetProgressInfo()}");
            if (currentTrial != null)
                GUILayout.Label($"Trial: {currentTrial.trialNumber}, Block: {currentTrial.blockNumber}");
            GUILayout.EndArea();
        }
    }
}
