// unity-component/Runtime/TrialSpec.cs
using System;
using System.Collections.Generic;

namespace Navlab.ExperimentRuntime
{
    [Serializable]
    public class TrialSpec
    {
        public string trialId;
        public List<string> activeAgentNames = new List<string>();
        [Serializable]
        public class GoalOverride
        {
            public string agentName;
            public string goalRef;
        }
        public List<GoalOverride> goalOverrides = new List<GoalOverride>();
        public float durationSeconds = 30.0f;
        public int trialSeed = 0;
    }
}
