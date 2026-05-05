// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EyeLean.Skeleton
{
    /// <summary>
    /// ScriptableObject describing block/trial structure for a Skeleton experiment.
    /// At session start the active configuration is snapshotted into the events
    /// sidecar as a <c>ConfigTrials</c> JSON event so deterministic replay sees the
    /// same layout regardless of post-hoc inspector tweaks.
    /// </summary>
    [CreateAssetMenu(fileName = "ExperimentConfig", menuName = "Eye_lean/Skeleton Trial Configuration")]
    public class TrialConfiguration : ScriptableObject
    {
        [Header("Experiment Metadata")]
        public string experimentName = "Skeleton Experiment";

        [TextArea(3, 6)]
        public string description = "Configure experiment-specific description here";

        public string version = "1.0";

        [Header("Trial Block Configuration")]
        public List<TrialBlock> blocks = new List<TrialBlock>();

        [Header("Global Settings")]
        public bool randomizeTrialOrder = true;

        public bool randomizeBlockOrder = false;

        [Header("Validation & Debug")]
        public bool showValidationDetails = true;

        public int TotalTrials => blocks.Sum(b => b.trialsInBlock);
        public int TotalBlocks => blocks.Count;

        public bool IsValid(out string validationMessage)
        {
            validationMessage = "";
            if (blocks.Count == 0)
            {
                validationMessage = "No trial blocks defined. Add at least one block.";
                return false;
            }
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (string.IsNullOrEmpty(block.blockName))
                {
                    validationMessage = $"Block {i + 1}: Block name cannot be empty.";
                    return false;
                }
                if (block.trialsInBlock <= 0)
                {
                    validationMessage = $"Block '{block.blockName}': Must have at least 1 trial.";
                    return false;
                }
                int duplicates = blocks.Count(b => b.blockName == block.blockName);
                if (duplicates > 1)
                {
                    validationMessage = $"Block '{block.blockName}': Duplicate block names not allowed.";
                    return false;
                }
            }
            validationMessage = $"Configuration valid: {TotalTrials} trials across {TotalBlocks} blocks";
            return true;
        }

        [ContextMenu("Create Default Configuration")]
        public void CreateDefaultConfiguration()
        {
            blocks.Clear();
            blocks.Add(new TrialBlock { blockName = "Training Block", trialsInBlock = 5, description = "Initial training trials" });
            blocks.Add(new TrialBlock { blockName = "Main Block", trialsInBlock = 20, description = "Main experimental trials" });
            blocks.Add(new TrialBlock { blockName = "Test Block", trialsInBlock = 10, description = "Final test trials" });
            if (showValidationDetails)
                Debug.Log($"[TrialConfiguration] Created default configuration: {TotalTrials} trials across {TotalBlocks} blocks");
        }

        private void OnValidate()
        {
            if (Application.isPlaying || !showValidationDetails) return;
            if (!IsValid(out string message))
                Debug.LogWarning($"[TrialConfiguration] {name}: {message}", this);
        }
    }

    /// <summary>
    /// Single block of trials. Extend with experiment-specific fields
    /// or use as-is for simple designs.
    /// </summary>
    [System.Serializable]
    public class TrialBlock
    {
        [Header("Block Identity")]
        public string blockName = "Block 1";

        [TextArea(2, 4)]
        public string description = "";

        [Header("Trial Parameters")]
        [Range(1, 100)]
        public int trialsInBlock = 20;

        [Header("Agent Configuration")]
        [Range(0.5f, 2.0f)]
        public float agentMeanSpeed = 1.0f;

        [Range(0.0f, 0.5f)]
        public float agentSpeedVariation = 0.2f;

        public bool restrictToWalkingState = true;

        public string GetSummary() =>
            $"{blockName}: {trialsInBlock} trials, speed={agentMeanSpeed:F2}m/s";
    }
}
