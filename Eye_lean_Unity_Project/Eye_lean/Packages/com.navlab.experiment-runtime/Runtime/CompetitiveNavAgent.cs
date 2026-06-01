// unity-component/Runtime/CompetitiveNavAgent.cs
using System.Collections.Generic;
using UnityEngine;
using Navlab.Planners;

namespace Navlab.ExperimentRuntime
{
    /// <summary>Drives one NPC along planner-computed waypoints, replanning
    /// at a fixed rate. Goal and planner are set per-trial by the ExperimentRunner.</summary>
    public class CompetitiveNavAgent : MonoBehaviour
    {
        [Header("Configuration")]
        public PlannerType plannerType = PlannerType.DStarLite;
        public EnvironmentConfig environment;
        public float replanRateHz = 5.0f;
        public float gridResolutionM = 0.05f;
        public float agentRadiusM = 0.25f;

        [Header("Locomotion")]
        public float walkSpeedMps = 1.4f;
        public float turnRateDegPerSec = 360f;
        public float arrivalToleranceM = 0.3f;

        [Header("Optional integrations")]
        public HumanObstacleTracker humanTracker;
        [System.NonSerialized]
        public PlannerLogger logger;

        private PlannerBridge _bridge;
        private (float x, float y)[] _waypoints = System.Array.Empty<(float, float)>();
        private int _waypointIndex;
        private float _nextReplanTime;
        private Vector2 _goalXZ;
        private bool _hasGoal;
        private readonly List<TrackedObject> _trackedDynamics = new List<TrackedObject>();

        public void SetGoal(Vector2 goalXZ)
        {
            _goalXZ = goalXZ;
            _hasGoal = true;
            _nextReplanTime = 0f;  // force replan
        }

        public void RegisterTrackedObstacle(TrackedObject obj)
        {
            if (obj != null && obj.transform != null)
                _trackedDynamics.Add(obj);
        }

        private void Start()
        {
            _bridge = new PlannerBridge(plannerType);
            if (humanTracker != null)
                RegisterTrackedObstacle(humanTracker.AsTrackedObject());
        }

        private void Update()
        {
            if (!_hasGoal || environment == null) return;

            // Replan tick
            if (Time.time >= _nextReplanTime)
            {
                Replan();
                _nextReplanTime = Time.time + 1.0f / replanRateHz;
            }

            // Drive along waypoints
            if (_waypointIndex < _waypoints.Length)
            {
                var (tx, ty) = _waypoints[_waypointIndex];
                var target = new Vector3(tx, transform.position.y, ty);
                var toTarget = target - transform.position;
                toTarget.y = 0;
                if (toTarget.magnitude < arrivalToleranceM)
                {
                    _waypointIndex++;
                    return;
                }
                // Steering
                var lookRot = Quaternion.LookRotation(toTarget.normalized);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, lookRot, turnRateDegPerSec * Time.deltaTime);
                transform.position += transform.forward * walkSpeedMps * Time.deltaTime;
            }
        }

        private void Replan()
        {
            var grid = SceneAdapter.Rasterize(environment, gridResolutionM);
            var dynamics = SceneAdapter.BuildDynamicObstacles(
                _trackedDynamics, Time.time);

            var start = (transform.position.x, transform.position.z);
            var goal = (_goalXZ.x, _goalXZ.y);

            var request = new PlanRequest(
                grid, start, goal, agentRadiusM,
                dynamicObstacles: dynamics
            );

            float startMs = Time.realtimeSinceStartup * 1000f;
            var result = _bridge.Plan(request);
            double runtimeMs = (Time.realtimeSinceStartup * 1000f) - startMs;

            if (result.Success && result.WaypointsWorld.Length > 1)
            {
                _waypoints = result.WaypointsWorld;
                _waypointIndex = 1;  // skip the start position
            }

            logger?.LogReplan(
                tSeconds: Time.time,
                plannerName: _bridge.Name,
                plannerVersion: _bridge.Version,
                runtimeMs: runtimeMs,
                waypoints: result.WaypointsWorld
            );
        }
    }
}
