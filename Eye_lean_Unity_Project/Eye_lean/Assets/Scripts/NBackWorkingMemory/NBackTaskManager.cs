// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using EyeLean.NBack.UI;

namespace EyeLean.NBack
{
    /// <summary>
    /// Drives one N-back block: generates the stimulus stream with the
    /// requested target rate, presents stimuli via <see cref="NBackStimulusPanel"/>,
    /// listens for response presses inside the response window, and reports
    /// per-trial outcomes back to the controller via the per-trial callback
    /// plus a per-block result on completion. Mirrors the manager-shape
    /// contract used by ChangeDetectionManager / VisualSearchManager: ad-hoc
    /// MonoBehaviour with Configure / RunBlock / Cleanup methods, no shared
    /// base class — the same pattern SampleExperimentController consumes.
    ///
    /// Stimulus randomness uses UnityEngine.Random.Range; the session-level
    /// Random.state snapshot captured by SceneEventRecorder gives the replay
    /// path deterministic reproduction. The controller seeds Random.InitState
    /// once at scene load — this manager does NOT reseed per block.
    /// </summary>
    public class NBackTaskManager : MonoBehaviour
    {
        [Tooltip("Stimulus panel under the main camera. Auto-found in Awake if null.")]
        [SerializeField] private NBackStimulusPanel stimulusPanel;

        private NBackConfig config;
        private InputAction respondAction;
        private bool respondedThisTrial;
        private float responseTimeSec;
        private bool acceptingResponses;

        // Per-trial event delegate signature:
        //   blockIndex, loadLevel, trialIndex, stimulus, isTarget, responded, rtMs
        public event Action<int, int, int, string, bool, bool, float> OnTrialResolved;

        private void Awake()
        {
            if (stimulusPanel == null) stimulusPanel = FindFirstObjectByType<NBackStimulusPanel>();

            // The participant responds with any of: keyboard Space (editor),
            // either controller trigger (HMD), or either primary button (HMD
            // fallback for grip-only setups). Constructing the action in
            // code avoids requiring an .inputactions asset in the scene.
            respondAction = new InputAction("NBackRespond");
            respondAction.AddBinding("<Keyboard>/space");
            respondAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
            respondAction.AddBinding("<XRController>{RightHand}/triggerPressed");
            respondAction.AddBinding("<XRController>{LeftHand}/primaryButton");
            respondAction.AddBinding("<XRController>{RightHand}/primaryButton");
            respondAction.performed += OnRespondPerformed;
            respondAction.Enable();
        }

        private void OnDestroy()
        {
            if (respondAction != null)
            {
                respondAction.performed -= OnRespondPerformed;
                respondAction.Disable();
                respondAction.Dispose();
            }
        }

        public void Configure(NBackConfig config)
        {
            this.config = config ?? throw new System.ArgumentNullException(nameof(config),
                "NBackTaskManager.Configure called with null config. The controller is responsible for refusing to start without a config; this exception indicates a wiring regression.");
            if (config.stimulusAlphabet == null || config.stimulusAlphabet.Length < 2)
            {
                Debug.LogError($"[NBack] stimulusAlphabet must have at least 2 entries (has {config.stimulusAlphabet?.Length ?? 0}). Target/non-target distinction requires distinct letters.");
            }
        }

        /// <summary>
        /// Run one block of length <c>trialsPerBlock</c> at the given load
        /// level. <paramref name="loadLevel"/> follows NBackPhaseExtensions:
        /// -1 = passive fixation (single-row fixation, no stream), 0/1/2/3 =
        /// memory-task levels. <paramref name="onBlockComplete"/> receives the
        /// aggregated NBackBlockResult.
        /// </summary>
        public IEnumerator RunBlock(int blockIndex, int loadLevel, Action<NBackBlockResult> onBlockComplete)
        {
            if (config == null)
            {
                Debug.LogError("[NBack] RunBlock called without Configure; aborting.");
                onBlockComplete?.Invoke(default);
                yield break;
            }

            // Snapshot the block boundary so a replay handler can re-align
            // to it before the trial events stream in.
            EyeLean.SceneState.SceneEventRecorder.RecordKV("NBackBlockStart", "",
                ("idx", blockIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("level", loadLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (loadLevel == -1)
            {
                // Passive baseline: single fixation cross for the full block
                // duration. No stimulus stream, no responses scored. The
                // RIPA detectors continue ticking, providing a baseline
                // load value for the analysis side.
                yield return RunPassiveBaseline(blockIndex, onBlockComplete);
                yield break;
            }

            yield return RunStimulusBlock(blockIndex, loadLevel, onBlockComplete);
        }

        private IEnumerator RunPassiveBaseline(int blockIndex, Action<NBackBlockResult> onBlockComplete)
        {
            // Passive-viewing baseline: same stimulus rhythm as a real
            // n-back block (letters at stimulusDurationSec on, fixation
            // for isiSec off) but no target enforcement and no response
            // window. Frequency-matches the rhythmic pupillary light-
            // reflex / novelty-response component so the analysis side
            // can subtract it from task-block pupil signals — a fixation-
            // only baseline misses this control entirely.
            int n = config.passiveBaselineTrials;
            int alphabetLen = config.stimulusAlphabet != null ? config.stimulusAlphabet.Length : 0;
            if (n <= 0 || alphabetLen == 0)
            {
                Debug.LogWarning("[NBack] Passive baseline skipped: passiveBaselineTrials=" + n + ", alphabet length=" + alphabetLen + ".");
                onBlockComplete?.Invoke(default);
                yield break;
            }

            string previous = null;

            for (int i = 0; i < n; i++)
            {
                // Avoid back-to-back identical letters so the participant
                // never sees a trivial "match" pattern during baseline.
                // No n-back constraint beyond that — this stream carries
                // zero working-memory load by design.
                string letter = PickNonTargetLetter(previous);

                EyeLean.SceneState.SceneEventRecorder.RecordKV("NBackStimulus", "",
                    ("idx", i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("char", letter),
                    ("isTarget", "0"));

                stimulusPanel?.ShowStimulus(letter);
                float trialStarted = Time.time;
                float stimEndsAt = trialStarted + config.stimulusDurationSec;
                while (Time.time < stimEndsAt) yield return null;

                stimulusPanel?.ShowFixation();
                float isiEndsAt = trialStarted + config.stimulusDurationSec + config.isiSec;
                while (Time.time < isiEndsAt) yield return null;

                // Drive the same per-trial CSV row + HUD update path as
                // task blocks (loadLevel = -1, isTarget = false, responded
                // = false, RT = NaN). Keeps the CSV row schema uniform
                // across baseline and task blocks.
                OnTrialResolved?.Invoke(blockIndex, -1, i, letter, false, false, float.NaN);

                previous = letter;
            }

            stimulusPanel?.ShowBlank();

            EyeLean.SceneState.SceneEventRecorder.RecordKV("NBackBlockEnd", "",
                ("idx", blockIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("level", "-1"));

            var result = NBackBlockResult.Compute(blockIndex, -1, 0, 0, 0, 0, 0f);
            result.totalTrials = n;
            onBlockComplete?.Invoke(result);
        }

        private IEnumerator RunStimulusBlock(int blockIndex, int loadLevel, Action<NBackBlockResult> onBlockComplete)
        {
            string[] stream = BuildStimulusStream(loadLevel, out bool[] isTargetFlags);
            int hits = 0, misses = 0, falseAlarms = 0, correctRejections = 0;
            float rtSumMs = 0f;
            int rtCount = 0;

            for (int i = 0; i < stream.Length; i++)
            {
                string letter = stream[i];
                bool isTarget = isTargetFlags[i];

                respondedThisTrial = false;
                responseTimeSec = float.NaN;
                acceptingResponses = true;

                EyeLean.SceneState.SceneEventRecorder.RecordKV("NBackStimulus", "",
                    ("idx", i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("char", letter),
                    ("isTarget", isTarget ? "1" : "0"));

                stimulusPanel?.ShowStimulus(letter);

                float trialStarted = Time.time;
                float endsAt = trialStarted + config.stimulusDurationSec;
                while (Time.time < endsAt)
                {
                    yield return null;
                }

                stimulusPanel?.ShowFixation();

                float windowEndsAt = trialStarted + Mathf.Max(config.responseWindowSec, config.stimulusDurationSec);
                while (Time.time < windowEndsAt && !respondedThisTrial)
                {
                    yield return null;
                }

                acceptingResponses = false;
                bool responded = respondedThisTrial;
                float rtMs = float.IsNaN(responseTimeSec) ? float.NaN : (responseTimeSec - trialStarted) * 1000f;

                // i < loadLevel for true N-back is a "warm-up" — there's no
                // preceding stimulus to compare against, so by design those
                // trials cannot be targets. We still score lure trials in
                // 0-back (the fixed-target task is well-defined from i=0).
                if (responded && isTarget) { hits++; rtSumMs += rtMs; rtCount++; }
                else if (!responded && isTarget) { misses++; }
                else if (responded && !isTarget) { falseAlarms++; rtSumMs += rtMs; rtCount++; }
                else { correctRejections++; }

                OnTrialResolved?.Invoke(blockIndex, loadLevel, i, letter, isTarget, responded, rtMs);

                // ISI: held in fixation until the next stimulus. ISI is
                // measured from stimulus offset (=trialStarted + stimulusDuration).
                float isiEndsAt = trialStarted + config.stimulusDurationSec + config.isiSec;
                while (Time.time < isiEndsAt)
                {
                    yield return null;
                }
            }

            stimulusPanel?.ShowBlank();

            float meanRTms = rtCount > 0 ? rtSumMs / rtCount : 0f;
            var result = NBackBlockResult.Compute(blockIndex, loadLevel, hits, misses, falseAlarms, correctRejections, meanRTms);

            EyeLean.SceneState.SceneEventRecorder.RecordKV("NBackBlockEnd", "",
                ("idx", blockIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("level", loadLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("hits", hits.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("fa", falseAlarms.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            onBlockComplete?.Invoke(result);
        }

        /// <summary>
        /// Generate the stimulus stream for a block. Target slots are
        /// chosen exactly (Fisher-Yates partial shuffle, never under-fills)
        /// and, for N >= 1, spaced so no two targets sit within loadLevel of
        /// each other — otherwise a chain copy (stream[i]=stream[i-N]) would
        /// produce accidental secondary matches at intermediate positions
        /// and inflate the empirical target rate above targetRatio.
        /// Non-target slots avoid the i-N letter to prevent the same.
        /// </summary>
        private string[] BuildStimulusStream(int loadLevel, out bool[] isTargetFlags)
        {
            int n = config.TrialsForLevel(loadLevel);
            string[] stream = new string[n];
            isTargetFlags = new bool[n];

            if (loadLevel == 0)
            {
                string target = string.IsNullOrEmpty(config.zeroBackTargetLetter)
                    ? config.stimulusAlphabet[0]
                    : config.zeroBackTargetLetter;
                int desiredTargets = Mathf.RoundToInt(n * config.zeroBackTargetRatio);
                HashSet<int> targetIdx = PickIndices(n, desiredTargets, 0);
                for (int i = 0; i < n; i++)
                {
                    if (targetIdx.Contains(i))
                    {
                        stream[i] = target;
                        isTargetFlags[i] = true;
                    }
                    else
                    {
                        stream[i] = PickNonTargetLetter(target);
                        isTargetFlags[i] = false;
                    }
                }
                return stream;
            }

            int eligibleStart = loadLevel;
            int eligibleCount = n - eligibleStart;
            int desired = Mathf.RoundToInt(eligibleCount * config.targetRatio);
            HashSet<int> targetSlots = PickSpacedIndices(eligibleCount, desired, eligibleStart, loadLevel);

            for (int i = 0; i < n; i++)
            {
                if (targetSlots.Contains(i))
                {
                    stream[i] = stream[i - loadLevel];
                    isTargetFlags[i] = true;
                }
                else
                {
                    string forbidden = i >= loadLevel ? stream[i - loadLevel] : null;
                    stream[i] = PickNonTargetLetter(forbidden);
                    isTargetFlags[i] = false;
                }
            }
            return stream;
        }

        /// <summary>
        /// Pick exactly <paramref name="howMany"/> indices from
        /// [offset, offset + count) via a Fisher-Yates partial shuffle.
        /// Deterministic and exact; never under-fills.
        /// </summary>
        private HashSet<int> PickIndices(int count, int howMany, int offset)
        {
            howMany = Mathf.Clamp(howMany, 0, count);
            var picked = new HashSet<int>();
            if (howMany == 0 || count == 0) return picked;
            int[] pool = new int[count];
            for (int i = 0; i < count; i++) pool[i] = i;
            for (int i = count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            for (int k = 0; k < howMany; k++) picked.Add(pool[k] + offset);
            return picked;
        }

        /// <summary>
        /// Pick indices from [offset, offset + count) with the constraint
        /// that no two picks are within <paramref name="minSpacing"/> of
        /// each other (i.e., consecutive picks differ by &gt; minSpacing).
        /// Used for N-back target slot selection so the chain-copy step
        /// cannot create accidental secondary matches. Uses an iterative
        /// greedy pick from a shuffled pool; if the requested density is
        /// not satisfiable under spacing, logs a warning and returns the
        /// best-effort set so the experiment still proceeds.
        /// </summary>
        private HashSet<int> PickSpacedIndices(int count, int howMany, int offset, int minSpacing)
        {
            howMany = Mathf.Clamp(howMany, 0, count);
            var picked = new HashSet<int>();
            if (howMany == 0 || count == 0) return picked;
            int[] pool = new int[count];
            for (int i = 0; i < count; i++) pool[i] = i;
            for (int i = count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            foreach (int candidate in pool)
            {
                bool ok = true;
                foreach (int p in picked)
                {
                    if (Mathf.Abs(p - (candidate + offset)) < minSpacing) { ok = false; break; }
                }
                if (ok) picked.Add(candidate + offset);
                if (picked.Count >= howMany) break;
            }
            if (picked.Count < howMany)
            {
                Debug.LogWarning($"[NBack] PickSpacedIndices: requested {howMany} targets in {count} slots with min spacing {minSpacing}; only {picked.Count} fit. Empirical target rate will be {(float)picked.Count / count:F3}, configured was {(float)howMany / count:F3}.");
            }
            return picked;
        }

        private string PickNonTargetLetter(string forbidden)
        {
            int len = config.stimulusAlphabet.Length;
            if (len == 0) return "?";
            for (int attempt = 0; attempt < 16; attempt++)
            {
                string pick = config.stimulusAlphabet[UnityEngine.Random.Range(0, len)];
                if (forbidden == null || pick != forbidden) return pick;
            }
            // Fall back to any letter (only fires if alphabet has 1 entry).
            return config.stimulusAlphabet[0];
        }

        private void OnRespondPerformed(InputAction.CallbackContext _)
        {
            if (EyeLean.Replay.SceneState.ReplayMode.IsActive) return;
            if (!acceptingResponses) return;
            if (respondedThisTrial) return;
            respondedThisTrial = true;
            responseTimeSec = Time.time;
        }

        public void PlaceStimulusPanel(Transform camT)
        {
            stimulusPanel?.PlaceInFrontOf(camT);
        }

        public void Cleanup()
        {
            acceptingResponses = false;
            stimulusPanel?.ShowBlank();
        }
    }
}
