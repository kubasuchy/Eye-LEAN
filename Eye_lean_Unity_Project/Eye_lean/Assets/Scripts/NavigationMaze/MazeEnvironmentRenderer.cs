// SPDX-License-Identifier: MIT
using System.Collections;
using UnityEngine;
using Navlab.ExperimentRuntime;
using EyeLean.NavigationMaze.UI;

namespace EyeLean.NavigationMaze
{
    /// <summary>
    /// Walks the maze <see cref="EnvironmentConfig"/> and instantiates
    /// visible GameObjects for the floor, walls, obstacles, and goal
    /// markers. navlab's <see cref="ExperimentRunner"/> consumes the
    /// EnvironmentConfig as planner-input *data* only — it does not render
    /// geometry. Without this component the maze appears as an empty
    /// scene with only the camera-locked HUD visible.
    ///
    /// Goals are rendered as bright upright posts so the participant can
    /// see where to navigate. Spawn points are not rendered by default
    /// (they're just where agents appear; the participant spawn is
    /// represented by the XR camera position at scene load).
    ///
    /// Execution order is -40 — after <see cref="MazeExperimentBridge"/>
    /// (-50) so the session context is already injected, but before the
    /// runner's default-order Start so the environment is visible the
    /// instant trial 1 begins.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class MazeEnvironmentRenderer : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Experiment config providing the environment to render. If null, the renderer looks for an ExperimentRunner in the scene and uses its assigned experiment.")]
        [SerializeField] private ExperimentConfig experiment;

        [Header("Geometry")]
        [Tooltip("Vertical height of all walls (meters). The paper-canonical 2.4 m matches a typical interior room ceiling and blocks an adult's line of sight through the wall.")]
        [SerializeField] private float wallHeight = 2.4f;

        [Tooltip("Floor thickness so the floor reads as a slab from below rather than an infinitely-thin plane (which can flicker against the room floor in URP).")]
        [SerializeField] private float floorThickness = 0.05f;

        [Tooltip("Vertical height of circular obstacle pillars (meters). Slightly shorter than walls so the participant can see over them when navigating.")]
        [SerializeField] private float obstacleHeight = 1.4f;

        [Tooltip("Vertical height of goal markers (meters). Taller than walls so they're visible from anywhere in the maze.")]
        [SerializeField] private float goalMarkerHeight = 4.5f;

        [Tooltip("Radius of goal-marker posts (meters).")]
        [SerializeField] private float goalMarkerRadius = 0.25f;

        [Header("Visibility")]
        [Tooltip("Render goal markers. Off = goals are invisible (planner still routes there). Useful for exploration trials where the participant must find the goal.")]
        [SerializeField] private bool renderGoals = true;

        [Tooltip("Render spawn-point markers. Off by default; spawn points are abstract positions, not geometry the participant should see.")]
        [SerializeField] private bool renderSpawns = false;

        [Header("Colors")]
        [SerializeField] private Color wallColor = new Color(0.55f, 0.55f, 0.55f);
        [SerializeField] private Color floorColor = new Color(0.30f, 0.32f, 0.35f);
        [SerializeField] private Color obstacleColor = new Color(0.40f, 0.30f, 0.20f);
        [SerializeField] private Color goalColor = new Color(0.20f, 0.85f, 0.30f);
        [SerializeField] private Color spawnColor = new Color(0.30f, 0.50f, 0.85f);

        private Transform rootContainer;

        private IEnumerator Start()
        {
            // Wait one frame so navlab's ExperimentRunner (default execution
            // order 0) has a chance to run its own Start and resolve any
            // procedural environment with its experiment seed. Calling
            // ResolveProcedural from here would risk producing a different
            // environment than the runner sees — render geometry that
            // doesn't match the planner's view of the world.
            yield return null;

            EnvironmentConfig env = ResolveEnvironment();
            if (env == null)
            {
                Debug.LogError("[MazeRenderer] No EnvironmentConfig resolved; the maze will appear empty. Assign the 'experiment' field on this component or add an ExperimentRunner with an assigned ExperimentConfig.");
                EyeLean.SceneState.SceneEventRecorder.RecordKV("MazeRenderFailed", "",
                    ("reason", "no_environment_config"));
                var hud = FindFirstObjectByType<MazeHUDController>();
                hud?.SetMessage("Configuration error:\nno EnvironmentConfig");
                yield break;
            }

            rootContainer = new GameObject("MazeGeometry").transform;
            rootContainer.SetParent(transform, false);

            BuildFloor(env);
            for (int i = 0; i < env.walls.Count; i++) BuildWall(env.walls[i], i);
            for (int i = 0; i < env.obstacles.Count; i++) BuildObstacle(env.obstacles[i], i);
            if (renderGoals) for (int i = 0; i < env.goals.Count; i++) BuildPostMarker(env.goals[i], goalColor, goalMarkerHeight, goalMarkerRadius, "Goal_" + env.goals[i].name);
            if (renderSpawns) for (int i = 0; i < env.spawnPoints.Count; i++) BuildPostMarker(env.spawnPoints[i], spawnColor, 1.2f, 0.15f, "Spawn_" + env.spawnPoints[i].name);
        }

        /// <summary>
        /// Tears down the current maze geometry and re-runs the rendering
        /// coroutine. Called by <see cref="MazeExperimentBridge"/> when the
        /// procedural delegate generates a new <see cref="EnvironmentConfig"/>
        /// for a subsequent block.
        /// </summary>
        public void Rebuild()
        {
            if (rootContainer != null)
            {
                DestroyImmediate(rootContainer.gameObject);
                rootContainer = null;
            }
            StartCoroutine(Start());
        }

        private EnvironmentConfig ResolveEnvironment()
        {
            if (experiment != null && experiment.environment != null) return experiment.environment;
            var runner = FindFirstObjectByType<ExperimentRunner>();
            if (runner != null && runner.experiment != null)
            {
                experiment = runner.experiment; // cache for re-resolves
                return runner.experiment.environment;
            }
            return null;
        }

        private void BuildFloor(EnvironmentConfig env)
        {
            float width = env.boundsMax.x - env.boundsMin.x;
            float depth = env.boundsMax.y - env.boundsMin.y;
            float cx = (env.boundsMax.x + env.boundsMin.x) * 0.5f;
            float cz = (env.boundsMax.y + env.boundsMin.y) * 0.5f;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(rootContainer, false);
            // Floor centered at the bounds midpoint, sitting just below y=0
            // so spawn points (placed at small +y like 0.6 in S_human) and
            // agents at y=0 read as standing on the slab rather than
            // floating inside it.
            floor.transform.localPosition = new Vector3(cx, -floorThickness * 0.5f, cz);
            floor.transform.localScale = new Vector3(width, floorThickness, depth);
            TintPrimitive(floor, floorColor);
            // The floor isn't gaze-relevant for behavioral analysis; strip
            // the collider so raycasts pass through it. (Walls + obstacles
            // keep their colliders so future locomotion systems block on
            // them.)
            var col = floor.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
        }

        private void BuildWall(WallSegment w, int idx)
        {
            float dx = w.endXZ.x - w.startXZ.x;
            float dz = w.endXZ.y - w.startXZ.y;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            if (length < 0.001f) return;

            float cx = (w.startXZ.x + w.endXZ.x) * 0.5f;
            float cz = (w.startXZ.y + w.endXZ.y) * 0.5f;
            // Yaw in degrees from the +Z axis (Unity forward) toward +X.
            // atan2(dx, dz) gives 0 for a wall pointing along +Z and 90
            // for one along +X — exactly what Quaternion.Euler(0, y, 0)
            // expects.
            float yawDeg = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall_" + idx;
            wall.transform.SetParent(rootContainer, false);
            wall.transform.localPosition = new Vector3(cx, wallHeight * 0.5f, cz);
            wall.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            // Local axes after rotation: X = thickness, Y = height, Z = length.
            wall.transform.localScale = new Vector3(Mathf.Max(w.thickness, 0.01f), wallHeight, length);
            TintPrimitive(wall, wallColor);
        }

        private void BuildObstacle(CircularObstacle o, int idx)
        {
            // Unity's Cylinder primitive has height = 2 at scale.y = 1, so
            // scale.y = height / 2 produces a pillar of the requested height,
            // centered at y = height / 2.
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Obstacle_" + idx;
            pillar.transform.SetParent(rootContainer, false);
            pillar.transform.localPosition = new Vector3(o.centerXZ.x, obstacleHeight * 0.5f, o.centerXZ.y);
            pillar.transform.localScale = new Vector3(o.radius * 2f, obstacleHeight * 0.5f, o.radius * 2f);
            TintPrimitive(pillar, obstacleColor);
        }

        private void BuildPostMarker(NamedPoint p, Color tint, float height, float radius, string objName)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = objName;
            post.transform.SetParent(rootContainer, false);
            post.transform.localPosition = new Vector3(p.positionXZ.x, height * 0.5f, p.positionXZ.y);
            post.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            TintPrimitive(post, tint);
        }

        private static void TintPrimitive(GameObject go, Color c)
        {
            // VRMaterialProvider handles URP / Android shader-stripping
            // safely. Fall back to the renderer's default material if the
            // provider isn't initialized yet (e.g., very early scene-load).
            // The fallback path uses Unity's default material, which on
            // Android URP can be the magenta "shader missing" material if
            // the unlit shader was stripped — visible to the researcher as
            // a pink maze. The Debug.LogWarning surfaces the underlying
            // cause so it's not silently masked.
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            try
            {
                renderer.material = VRMaterialProvider.GetMaterial(c);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MazeRenderer] VRMaterialProvider.GetMaterial failed for {go.name}; falling back to default material tint. {ex.GetType().Name}: {ex.Message}");
                renderer.material.color = c;
            }
        }
    }
}
