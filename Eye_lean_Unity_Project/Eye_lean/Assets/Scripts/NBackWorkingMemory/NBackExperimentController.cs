// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using EyeLean.NBack.UI;
using EyeTracking.Components;
using EyeTracking.Core;

namespace EyeLean.NBack
{
    /// <summary>
    /// Orchestrates the N-back working-memory session. Mirrors the
    /// coroutine-sequencer pattern from <c>SampleExperimentController</c>:
    /// the controller owns phase state + CSV metadata declaration and
    /// delegates per-block stimulus presentation to <see cref="NBackTaskManager"/>.
    ///
    /// The scene is deterministic-replay safe: stimulus randomness uses
    /// UnityEngine.Random.Range, and the session-level Random.state snapshot
    /// captured by SceneEventRecorder at header-write is restored on replay.
    /// One InitState call lives in <c>Start</c> — never per block.
    /// </summary>
    public class NBackExperimentController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SessionRecorder sessionRecorder;
        [SerializeField] private NBackTaskManager taskManager;
        [SerializeField] private EyeTracking.UI.WorldInstructionPanel instructionsPanel;
        [SerializeField] private NBackHUDController hud;

        [Header("Configuration")]
        [Tooltip("N-back configuration ScriptableObject. Default asset values mirror Jayawardena 2025 (RIPA2). See the asset's sourceCitation field for the parameter provenance. Must be assigned — the scene refuses to start with a null config.")]
        public NBackConfig config;

        [Header("Pacing")]
        [SerializeField] private float instructionDisplayTime = 6f;
        [SerializeField] private bool autoStart = false;

        // State
        private NBackPhase currentPhase = NBackPhase.Idle;
        private bool isRunning;
        private List<NBackBlockResult> blockResults = new List<NBackBlockResult>();
        private List<NBackPhase> resolvedBlockOrder = new List<NBackPhase>();
        private InputAction startAction;

        public event Action<NBackPhase> OnPhaseChanged;
        public event Action<List<NBackBlockResult>> OnSessionComplete;

        public NBackPhase CurrentPhase => currentPhase;
        public bool IsRunning => isRunning;

        private void Awake()
        {
            if (sessionRecorder == null) sessionRecorder = FindFirstObjectByType<SessionRecorder>();
            if (taskManager == null) taskManager = GetComponent<NBackTaskManager>();
            if (instructionsPanel == null) instructionsPanel = FindFirstObjectByType<EyeTracking.UI.WorldInstructionPanel>();
            if (instructionsPanel == null)
            {
                instructionsPanel = EyeTracking.UI.WorldInstructionPanel.Create(null);
                Debug.Log("[NBack] Auto-spawned WorldInstructionPanel — none found in scene.");
            }
            if (hud == null) hud = FindFirstObjectByType<NBackHUDController>();

            if (config == null)
            {
                Debug.LogError("[NBack] No NBackConfig assigned on this controller. The scene cannot record valid data without a config asset because the SceneEvents 'ConfigNBack' snapshot — which the analysis side reads as authoritative — would otherwise carry anonymous defaults that don't match any researcher-edited asset. Create an asset via Assets > Create > Eye_lean > N-back Config, assign it in the Inspector, and re-enter Play.");
                instructionsPanel?.Show("Configuration error", "NBackConfig is not assigned on the controller. Stop Play, assign the config asset in the Inspector, and try again.");
                enabled = false;
                return;
            }
            if (sessionRecorder == null)
            {
                Debug.LogError("[NBack] CRITICAL: SessionRecorder not found in scene. CSV columns will not register.");
            }

            // Declare CSV columns BEFORE recording starts (header lock fires
            // at first row write). The block-level NBackLevel column is the
            // ground-truth load level the analysis side joins against the
            // per-detector LiveLoadIndex_* columns.
            if (sessionRecorder != null)
            {
                DeclareMetadataFields();
            }

            taskManager?.Configure(config);
            if (taskManager != null)
            {
                taskManager.OnTrialResolved += HandleTrialResolved;
            }

            if (!EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                startAction = new InputAction("NBackStart", binding: "<Keyboard>/space");
                startAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
                startAction.AddBinding("<XRController>{RightHand}/triggerPressed");
                startAction.performed += OnStartPerformed;
                startAction.Enable();
            }
        }

        private void OnDestroy()
        {
            if (taskManager != null)
            {
                taskManager.OnTrialResolved -= HandleTrialResolved;
            }
            if (startAction != null)
            {
                startAction.performed -= OnStartPerformed;
                startAction.Disable();
                startAction.Dispose();
            }
        }

        private void DeclareMetadataFields()
        {
            sessionRecorder.DeclareMetadataField("SessionType", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("ExperimentVersion", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("NBackBlock", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("NBackLevel", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("NBackTrial", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("NBackStimulus", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("NBackIsTarget", EyeLean.Data.MetadataValueType.Bool);
            sessionRecorder.DeclareMetadataField("NBackResponse", EyeLean.Data.MetadataValueType.Bool);
            sessionRecorder.DeclareMetadataField("NBackResponseTimeMs", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("NBackBlockResultJSON", EyeLean.Data.MetadataValueType.String);
        }

        private void Start()
        {
            if (config != null)
            {
                UnityEngine.Random.InitState(config.stimulusSeed);
            }

            EyeLean.SceneState.SceneEventRecorder.RecordJson("ConfigNBack", "", config);

            StartCoroutine(PlacePanelsThenShow());
        }

        private IEnumerator PlacePanelsThenShow()
        {
            if (EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                var rc = FindFirstObjectByType<EyeLean.Replay.ReplayController>();
                if (rc != null)
                {
                    float timeout = Time.realtimeSinceStartup + 30f;
                    while (!rc.IsPlaying && Time.realtimeSinceStartup < timeout)
                        yield return null;
                    // Skip past the first ~20 frames which may have stale
                    // scene-default camera position from before XR tracking
                    // kicked in during the original recording.
                    for (int i = 0; i < 30; i++) yield return null;
                    Debug.Log($"[NBack] ReplayController playing, placing panels.");
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
                instructionsPanel?.PlaceInFrontOf(cam.transform);
                taskManager?.PlaceStimulusPanel(cam.transform);
                hud?.PlaceInFrontOf(cam.transform);
                Debug.Log($"[NBack] Panels placed at camera pos={cam.transform.position}");
            }

            if (EyeLean.Replay.SceneState.ReplayMode.IsActive || autoStart)
                StartExperiment();
            else
                ShowIdleMessage();
        }

        private void ShowIdleMessage()
        {
            instructionsPanel?.Show("Working Memory (N-back)",
                "You will see a stream of letters.\n\n" +
                "Press the trigger when the current letter matches the rule\n" +
                "for the current block.\n\n" +
                "Press the trigger to begin.");
            hud?.SetMessage("Press trigger to begin");
            SetPhase(NBackPhase.Idle);
        }

        public void StartExperiment()
        {
            if (isRunning) return;
            if (config == null) { Debug.LogError("[NBack] Cannot start: no config."); return; }
            isRunning = true;
            blockResults.Clear();

            sessionRecorder?.SetMetadata("SessionType", "NBack");
            sessionRecorder?.SetMetadata("ExperimentVersion", "1.0");

            StartCoroutine(RunSession());
        }


        public void StopExperiment()
        {
            if (!isRunning) return;
            StopAllCoroutines();
            taskManager?.Cleanup();
            isRunning = false;
            SetPhase(NBackPhase.Idle);
        }

        private IEnumerator RunSession()
        {
            ResolveBlockOrder();
            if (resolvedBlockOrder.Count == 0)
            {
                Debug.LogError("[NBack] Session aborted: resolvedBlockOrder is empty. Check config.blocks in the assigned NBackConfig asset.");
                instructionsPanel?.Show("Configuration error",
                    "No blocks to run.\n\nThe assigned NBackConfig asset has an empty 'blocks' array. Fix the config and re-enter Play.");
                isRunning = false;
                yield break;
            }
            EyeLean.SceneState.SceneEventRecorder.RecordKV("NBackBlockOrder", "",
                ("order", string.Join(",", resolvedBlockOrder.ConvertAll(p => p.ToString()))));

            instructionsPanel?.Hide();

            for (int b = 0; b < resolvedBlockOrder.Count; b++)
            {
                NBackPhase phase = resolvedBlockOrder[b];
                int level = phase.LoadLevel();
                hud?.SetStatus(b, resolvedBlockOrder.Count, level, 0, config.TrialsForLevel(level));

                yield return RunBlockWithInstructions(b, phase);

                if (b < resolvedBlockOrder.Count - 1)
                {
                    hud?.SetMessage("Rest…");
                    float restEnd = Time.time + config.interBlockRestSec;
                    while (Time.time < restEnd)
                    {
                        int remaining = Mathf.CeilToInt(restEnd - Time.time);
                        instructionsPanel?.Show("Rest", $"Take a short break.\n\nNext block in {remaining} s");
                        yield return null;
                    }
                    instructionsPanel?.Hide();
                }
            }

            CompleteSession();
        }

        private void ResolveBlockOrder()
        {
            resolvedBlockOrder.Clear();
            if (config.blocks == null || config.blocks.Length == 0)
            {
                Debug.LogError("[NBack] config.blocks is empty; nothing to run.");
                return;
            }

            if (!config.randomizeBlockOrder)
            {
                resolvedBlockOrder.AddRange(config.blocks);
                return;
            }

            // PassiveBaseline phases are pinned to the start of the
            // sequence; an uncontaminated pre-task baseline would otherwise
            // be polluted by carryover from previous task blocks (residual
            // arousal / cognitive load decays over tens of seconds). The
            // n-back task levels among themselves are shuffled.
            // Researchers wanting baseline elsewhere (post-task, or
            // pre+post) should set randomizeBlockOrder = false and
            // declare the desired order explicitly in config.blocks.
            var pinnedBaselines = new List<NBackPhase>();
            var shuffleable = new List<NBackPhase>();
            foreach (var phase in config.blocks)
            {
                if (phase == NBackPhase.PassiveBaseline) pinnedBaselines.Add(phase);
                else shuffleable.Add(phase);
            }

            // Fisher–Yates using a System.Random keyed off the block-order
            // seed. Kept separate from UnityEngine.Random so changes to
            // stimulus RNG don't shift block ordering and vice versa.
            var rng = new System.Random(config.blockOrderSeed);
            for (int i = shuffleable.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (shuffleable[i], shuffleable[j]) = (shuffleable[j], shuffleable[i]);
            }

            resolvedBlockOrder.AddRange(pinnedBaselines);
            resolvedBlockOrder.AddRange(shuffleable);
        }

        private IEnumerator RunBlockWithInstructions(int blockIndex, NBackPhase phase)
        {
            sessionRecorder?.SetMetadata("NBackBlockResultJSON", "");
            SetPhase(NBackPhase.Instructions);
            int level = phase.LoadLevel();

            string title;
            string body;
            switch (phase)
            {
                case NBackPhase.PassiveBaseline:
                    title = "Baseline";
                    body = "Letters will appear, just like the task blocks.\n\n" +
                           "Do not respond to anything — just watch the screen.";
                    break;
                case NBackPhase.ZeroBack:
                    title = "0-back";
                    body = $"Press the trigger when the letter is\n\n<size=200%><b>{config.zeroBackTargetLetter}</b></size>";
                    break;
                default:
                    title = level + "-back";
                    body = $"Press the trigger when the current letter\nmatches the letter shown <b>{level}</b> step{(level == 1 ? "" : "s")} ago.";
                    break;
            }
            instructionsPanel?.Show(title, body);
            yield return new WaitForSeconds(instructionDisplayTime);

            for (int i = 3; i >= 1; i--)
            {
                instructionsPanel?.SetBodyOnly($"Starting in\n\n<size=300%>{i}</size>");
                yield return new WaitForSeconds(1f);
            }
            instructionsPanel?.SetBodyOnly("<size=250%>Go!</size>");
            yield return new WaitForSeconds(0.4f);
            instructionsPanel?.Hide();

            SetPhase(phase);
            sessionRecorder?.SetMetadata("NBackBlock", blockIndex);
            sessionRecorder?.SetMetadata("NBackLevel", level);

            NBackBlockResult result = default;
            yield return taskManager.RunBlock(blockIndex, level, r => result = r);
            blockResults.Add(result);

            // Stamp the block's aggregate JSON onto the final row. The
            // analysis side joins on block boundaries via the NBackBlockEnd
            // event in SceneEvents.csv.
            sessionRecorder?.SetMetadata("NBackBlockResultJSON", JsonUtility.ToJson(result));
        }

        private void HandleTrialResolved(int blockIndex, int loadLevel, int trialIndex, string stim, bool isTarget, bool responded, float rtMs)
        {
            if (sessionRecorder == null) return;
            sessionRecorder.SetMetadata("NBackBlock", blockIndex);
            sessionRecorder.SetMetadata("NBackLevel", loadLevel);
            sessionRecorder.SetMetadata("NBackTrial", trialIndex);
            sessionRecorder.SetMetadata("NBackStimulus", stim ?? "");
            sessionRecorder.SetMetadata("NBackIsTarget", isTarget);
            sessionRecorder.SetMetadata("NBackResponse", responded);
            sessionRecorder.SetMetadata("NBackResponseTimeMs", float.IsNaN(rtMs) ? -1f : rtMs);
            hud?.SetStatus(blockIndex, resolvedBlockOrder.Count, loadLevel, trialIndex + 1, config.TrialsForLevel(loadLevel));
        }

        private void SetPhase(NBackPhase phase)
        {
            currentPhase = phase;
            if (sessionRecorder != null)
            {
                sessionRecorder.SetSessionContext(0, phase.ToString(), "NBack", DefaultSubTask(phase));
            }
            OnPhaseChanged?.Invoke(phase);
        }

        private static string DefaultSubTask(NBackPhase phase)
        {
            return phase switch
            {
                NBackPhase.PassiveBaseline => "Baseline",
                NBackPhase.ZeroBack => "0back",
                NBackPhase.OneBack => "1back",
                NBackPhase.TwoBack => "2back",
                NBackPhase.ThreeBack => "3back",
                NBackPhase.Instructions => "Instructions",
                _ => phase.ToString(),
            };
        }

        private void CompleteSession()
        {
            SetPhase(NBackPhase.Complete);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Session complete. Results:\n");
            foreach (var r in blockResults)
            {
                if (r.loadLevel == -1) continue;
                string levelLabel = r.loadLevel == 0 ? "0-back" : $"{r.loadLevel}-back";
                int correct = r.hits + r.correctRejections;
                sb.AppendLine($"<b>{levelLabel}</b>:  {correct}/{r.totalTrials} correct  ({r.hits} hits, {r.misses} miss, {r.falseAlarms} FA)");
            }
            sb.AppendLine("\nThank you!");

            instructionsPanel?.Show("Done", sb.ToString());
            hud?.SetMessage("Complete");
            isRunning = false;
            OnSessionComplete?.Invoke(blockResults);
        }

        private void OnStartPerformed(InputAction.CallbackContext _)
        {
            if (currentPhase == NBackPhase.Idle && !isRunning)
            {
                StartExperiment();
            }
        }
    }
}
