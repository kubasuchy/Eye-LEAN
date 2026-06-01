// unity-component/Runtime/ExperimentConfig.cs
using System.Collections.Generic;
using UnityEngine;

namespace Navlab.ExperimentRuntime
{
    [CreateAssetMenu(menuName = "Navlab/Experiment Config")]
    public class ExperimentConfig : ScriptableObject
    {
        [Header("Identification")]
        public string experimentName = "untitled";
        public string version = "0.1.0";

        [Header("Environment")]
        public EnvironmentConfig environment;

        [Header("Agents")]
        public List<AgentSpec> agents = new List<AgentSpec>();

        [Header("Trial sequence")]
        public List<TrialSpec> trials = new List<TrialSpec>();

        [Header("Reproducibility")]
        [Tooltip("Top-level seed. Each TrialSpec.trialSeed is derived from this.")]
        public int randomSeed = 42;
    }
}
