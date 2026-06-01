// unity-component/Runtime/AgentSpec.cs
using System;

namespace Navlab.ExperimentRuntime
{
    [Serializable]
    public class AgentSpec
    {
        public string name;
        public AgentType agentType;
        public AgentSkin skin;
        public PlannerType planner;
        public string spawnRef;
        public string goalRef;
    }

    public enum AgentType
    {
        Human,
        Npc,
        Robot,
        Algorithmic,
    }

    public enum PlannerType
    {
        None,
        AStar,
        DStarLite,
    }
}
