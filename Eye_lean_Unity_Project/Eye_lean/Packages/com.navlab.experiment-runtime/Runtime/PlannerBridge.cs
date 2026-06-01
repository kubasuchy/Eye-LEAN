// unity-component/Runtime/PlannerBridge.cs
using System;
using Navlab.Planners;

namespace Navlab.ExperimentRuntime
{
    public class PlannerBridge
    {
        private readonly IPlanner _planner;
        public string Name => _planner.Name;
        public string Version => _planner.Version;

        public PlannerBridge(PlannerType type)
        {
            _planner = type switch
            {
                PlannerType.AStar => new AStar(),
                PlannerType.DStarLite => new DStarLite(),
                PlannerType.None => throw new ArgumentException(
                    "PlannerType.None cannot be used for active planning."),
                _ => throw new ArgumentException($"Unknown planner type: {type}")
            };
        }

        public PlanResult Plan(PlanRequest request) => _planner.Plan(request);
    }
}
