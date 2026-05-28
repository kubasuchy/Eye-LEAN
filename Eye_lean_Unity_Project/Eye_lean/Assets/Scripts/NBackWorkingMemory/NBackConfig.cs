// SPDX-License-Identifier: MIT
using System;
using UnityEngine;

namespace EyeLean.NBack
{
    /// <summary>
    /// ScriptableObject configuration for the N-back working-memory scene.
    /// Default values reproduce the protocol of Jayawardena, Jayawardana &amp;
    /// Gwizdka (2025), <i>Measuring Mental Effort in Real Time Using
    /// Pupillometry</i>, J. Eye Movement Research 18(6):70, §5.1 — the
    /// paper RIPA2 was validated against. Stimulus timing, alphabet, and
    /// per-level trial counts are paper-exact; the passive-fixation block,
    /// target rate, and response window are Eye_lean choices documented
    /// inline below.
    ///
    /// Researchers tuning the paradigm clone the asset and edit fields;
    /// the per-field <c>sourceCitation</c> string is written into the
    /// SceneEvents stream so the CSV is self-describing.
    /// </summary>
    [CreateAssetMenu(fileName = "NBackConfig", menuName = "Eye_lean/N-back Config", order = 100)]
    public class NBackConfig : ScriptableObject
    {
        [Header("Source citation")]
        [Tooltip("Citation for the parameter set. Written into the SceneEvents config snapshot for traceability.")]
        [TextArea(2, 4)]
        public string sourceCitation = "Jayawardena, Jayawardana & Gwizdka 2025, J. Eye Movement Research 18(6):70, §5.1. Passive baseline, target rate, and response window are Eye_lean additions.";

        [Header("Blocks")]
        [Tooltip("Block sequence to run. Selected from the NBackPhase enum (PassiveBaseline / ZeroBack / OneBack / TwoBack / ThreeBack). The paper runs 4 reps × 4 levels = 16 sessions; the default below is a shorter Eye_lean demo (1 of each + passive baseline). Repeat phases to approximate the paper's 16-session design.")]
        public NBackPhase[] blocks = new NBackPhase[]
        {
            NBackPhase.PassiveBaseline,
            NBackPhase.ZeroBack,
            NBackPhase.OneBack,
            NBackPhase.TwoBack,
            NBackPhase.ThreeBack,
        };

        [Tooltip("Randomize block order using blockOrderSeed. Off = run blocks in declared order. The paper uses randomized order with no more than two consecutive sessions of the same difficulty; the no-consecutive constraint is not enforced here yet.")]
        public bool randomizeBlockOrder = true;

        [Tooltip("Seed for block-order randomization. Recorded into the SceneEvents stream so replay reproduces the same order.")]
        public int blockOrderSeed = 17;

        [Header("Trial structure")]
        [Tooltip("Stimuli per block for passive / 0-back / 1-back / 2-back. Paper §5.1: 45 trials for levels n0–n2.")]
        public int trialsPerBlock = 45;

        [Tooltip("Stimuli per block for 3-back. Paper §5.1: 30 trials for level n3.")]
        public int trialsPerBlockN3 = 30;

        [Tooltip("Fraction of 1/2/3-back trials that are true N-back matches. Eye_lean choice — Jayawardena 2025 does not specify a target rate; 0.30 follows the broader N-back literature (e.g., Owen et al. 2005 meta-analysis).")]
        [Range(0.05f, 0.5f)]
        public float targetRatio = 0.30f;

        [Tooltip("Fraction of 0-back trials that match the fixed 0-back target letter. Independent of targetRatio because 0-back semantics differ.")]
        [Range(0.05f, 0.7f)]
        public float zeroBackTargetRatio = 0.30f;

        [Header("Timing")]
        [Tooltip("Stimulus on-screen duration in seconds. Paper §5.1: 0.5 s.")]
        public float stimulusDurationSec = 0.5f;

        [Tooltip("Inter-stimulus interval in seconds (blank between stimuli). Paper §5.1: 1.5 s, giving a 2 s total trial duration.")]
        public float isiSec = 1.5f;

        [Tooltip("Response window per stimulus in seconds, measured from stimulus onset. A press inside this window counts; a press outside it is ignored. Eye_lean choice — paper does not specify; 2 s covers the full trial duration so any response during the trial counts.")]
        public float responseWindowSec = 2.0f;

        [Header("Stimuli")]
        [Tooltip("Letter set used as stimuli. Paper §5.1: {C, F, H, S}. Researchers extending to a larger alphabet should add a sourceCitation note.")]
        public string[] stimulusAlphabet = new string[] { "C", "F", "H", "S" };

        [Tooltip("Letter the 0-back block treats as the always-target. Must be a member of stimulusAlphabet. Paper §5.1 uses the same alphabet for all blocks; choose any letter — \"C\" is the default.")]
        public string zeroBackTargetLetter = "C";

        [Header("Passive baseline (Eye_lean addition — not in paper)")]
        [Tooltip("Number of stimulus trials in the passive-viewing baseline. Letters change at the same cadence as task blocks (stimulusDurationSec on, isiSec off) but the participant is asked not to respond. Frequency-matching the stimulus stream lets analysis subtract the stimulus-driven pupillary component from task-block load signals. Default 45 matches a single n0/n1/n2 block so baseline-vs-load comparisons hold time-on-task constant; total duration = passiveBaselineTrials × (stimulusDurationSec + isiSec).")]
        public int passiveBaselineTrials = 45;

        [Header("Instructions / pacing")]
        [Tooltip("Seconds the per-block instruction text is displayed before the 3-2-1 countdown.")]
        public float instructionDisplaySec = 6f;

        [Tooltip("Seconds between blocks. Lets the participant rest and the cognitive-load detectors settle.")]
        public float interBlockRestSec = 8f;

        [Header("Determinism")]
        [Tooltip("Seed for stimulus-stream RNG. The session-level Random.state snapshot captures this; replay restores it. Per-block isolation is achieved by stride-mixing this seed with block index, not by re-seeding.")]
        public int stimulusSeed = 0xE7E1EA1;

        /// <summary>
        /// Returns the trial count for a given N-back level. The paper uses
        /// 30 trials for n3 and 45 trials for n0–n2. Passive baseline
        /// (loadLevel = -1) returns <c>passiveBaselineTrials</c> so HUD
        /// trial-counter readouts match the actual stream length.
        /// </summary>
        public int TrialsForLevel(int loadLevel)
        {
            if (loadLevel == -1) return passiveBaselineTrials;
            if (loadLevel < 0) return 0;
            return loadLevel == 3 ? trialsPerBlockN3 : trialsPerBlock;
        }

        /// <summary>
        /// One-line summary written into the SceneEvents config snapshot so
        /// the analysis side can identify the config without parsing JSON.
        /// </summary>
        public string ToSummary()
        {
            return string.Format(
                "trials/blk={0}(n3={1}) targetRatio={2:F2} stim={3:F2}s isi={4:F2}s resp={5:F2}s seed={6}",
                trialsPerBlock, trialsPerBlockN3, targetRatio, stimulusDurationSec, isiSec, responseWindowSec, stimulusSeed);
        }
    }
}
