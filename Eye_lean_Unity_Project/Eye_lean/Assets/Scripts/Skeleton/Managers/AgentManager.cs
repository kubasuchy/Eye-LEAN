// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using UnityEngine;
using EyeLean.SceneState;

namespace EyeLean.Skeleton
{
    /// <summary>
    /// Object pool + spawn API for humanoid avatars. Designed around the Microsoft
    /// Rocketbox library; falls back to procedural cube agents when no Rocketbox
    /// prefabs are present so the scene runs without external assets. Each
    /// <see cref="SpawnAgent"/> registers the spawn with <c>SceneStateRecorder</c>
    /// for deterministic replay. See <c>docs/SKELETON_AGENTS.md</c> for Rocketbox
    /// install steps and citation guidance.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class AgentManager : MonoBehaviour
    {
        [Header("Debug & Visualization")]
        [Tooltip("Verbose console logging for the agent pool — spawn IDs, despawns, vision-cone events. Off by default; turn on while debugging avoidance behavior.")]
        [SerializeField] public bool showDebugLogs = false;
        [Tooltip("Draw vision cones and collision-avoidance gizmos in the Scene view.")]
        [SerializeField] private bool showGizmosInScene = false;

        [Header("Agent Pool Settings")]
        [Tooltip("Agents preallocated per gender at scene start. Higher = more visual variety, more memory.")]
        [Range(5, 50)] [SerializeField] private int poolSizePerGender = 20;
        [Tooltip("Cap on simultaneously active agents in the scene. The pool keeps the rest dormant.")]
        [Range(5, 50)] [SerializeField] private int maxTotalAgents = 20;

        [Header("Agent Appearance")]
        [Tooltip("Uniform scale multiplier applied to every agent. 1.0 = real-world height; raise for cartoonier scenes.")]
        [Range(0.1f, 2.0f)] [SerializeField] private float agentScale = 1.0f;

        [Header("Agent Animation")]
        [Tooltip("Force agents to use the Walking animation state only, locking out idle/run variants. Predictable for controlled studies.")]
        [SerializeField] private bool restrictToWalkingState = true;

        [Header("Vision Cone Collision Avoidance")]
        [Tooltip("Master switch for the vision-cone TTC avoidance behavior. Off = agents walk straight through each other and the participant.")]
        [SerializeField] private bool enableVisionConeAvoidance = true;
        [Tooltip("Half-angle of each agent's forward vision cone, in degrees.")]
        [Range(30f, 120f)] [SerializeField] private float visionConeAngle = 45f;
        [Tooltip("Inner radius of the vision cone, in meters. Anything closer triggers immediate stop.")]
        [Range(0.5f, 2.0f)] [SerializeField] private float minVisionRadius = 1.0f;
        [Tooltip("Outer radius of the vision cone, in meters. Beyond this, agents ignore each other.")]
        [Range(1.5f, 5.0f)] [SerializeField] private float maxVisionRadius = 2.5f;
        [Tooltip("Time-to-collision threshold in seconds. Below this predicted TTC, the agent stops.")]
        [Range(0.5f, 3.0f)] [SerializeField] private float ttcThreshold = 1.5f;
        [Tooltip("Seconds an agent waits when blocked before reattempting a path.")]
        [Range(0.2f, 2.0f)] [SerializeField] private float stopDuration = 0.8f;

        [Header("Eye_lean Integration")]
        [Tooltip("Register every spawned agent with SceneStateRecorder using a deterministic seed so deterministic replay reproduces them.")]
        [SerializeField] private bool registerSpawnsForReplay = true;

        public static AgentManager Instance { get; private set; }

        private GameObject[] maleAgentPrefabs;
        private GameObject[] femaleAgentPrefabs;
        private bool usingFallbackPrefabs;

        private readonly Queue<GameObject> maleAgentPool = new Queue<GameObject>();
        private readonly Queue<GameObject> femaleAgentPool = new Queue<GameObject>();
        private readonly List<ActiveAgent> activeAgents = new List<ActiveAgent>();

        private bool isAgentSystemActive;
        private bool nextAgentIsMale = true;
        private int nextAgentId = 1;
        private TrialManager cachedTrialManager;

        public int ActiveAgentCount => activeAgents.Count;
        public int MaxAgents => maxTotalAgents;
        public bool IsActive => isAgentSystemActive;
        /// <summary>True when no Rocketbox prefabs were found and the procedural cube fallback is in use.</summary>
        public bool IsUsingFallbackPrefabs => usingFallbackPrefabs;

        private void Awake() { Instance = this; }

        private void Start()
        {
            cachedTrialManager = FindFirstObjectByType<TrialManager>();
            InitializeAgentPrefabs();
            InitializePools();
        }

        private void Update()
        {
            if (!isAgentSystemActive) return;
            UpdateActiveAgents();
        }

        // ----- Public API -----

        public void InitializePool(int poolSize = 20)
        {
            poolSizePerGender = poolSize;
            InitializeAgentPrefabs();
            InitializePools();
        }

        public void StartAgentSystem()
        {
            isAgentSystemActive = true;
            nextAgentId = 1;
            if (showDebugLogs) Debug.Log("[AgentManager] Agent system started");
        }

        public void StopAgentSystem()
        {
            isAgentSystemActive = false;
            DespawnAllAgents();
        }

        public GameObject SpawnAgent(Vector3 position, Vector3 direction, float speed = 1.0f)
        {
            if (activeAgents.Count >= maxTotalAgents)
            {
                if (showDebugLogs) Debug.LogWarning("[AgentManager] Cannot spawn - at max capacity");
                return null;
            }

            GameObject agent = GetAgentFromPool(nextAgentIsMale);
            nextAgentIsMale = !nextAgentIsMale;
            if (agent == null)
            {
                if (showDebugLogs) Debug.LogWarning("[AgentManager] Cannot spawn - pool exhausted");
                return null;
            }

            agent.transform.position = position;
            // Look-rotation guard: a zero-direction vector throws.
            Vector3 lookDir = direction.sqrMagnitude > 1e-4f ? direction : Vector3.forward;
            agent.transform.rotation = Quaternion.LookRotation(lookDir);
            agent.transform.localScale = Vector3.one * agentScale;

            int agentId = nextAgentId++;
            var active = new ActiveAgent
            {
                agentId = agentId,
                gameObject = agent,
                targetSpeed = speed,
                direction = lookDir,
                spawnTime = Time.time,
            };
            active.CacheComponents();

            // Cube-fallback agents have no AgentBehavior — guard the call.
            var behavior = active.GetBehavior();
            if (behavior != null)
            {
                behavior.SetAnimatorRestriction(restrictToWalkingState);
                behavior.SetMovementParameters(lookDir, speed);
            }

            var rb = active.GetRigidbody();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            activeAgents.Add(active);
            agent.SetActive(true);

            // Seed uses the trial number so re-running with the same Random.InitState
            // produces an identical sequence of agent GUIDs (deterministic replay).
            if (registerSpawnsForReplay)
            {
                int trialNumber = cachedTrialManager?.GetCurrentTrial()?.trialNumber ?? 0;
                string seed = $"Agent_{trialNumber}_{agentId}";
                SceneStateRecorder.MarkRecordableSeeded(agent, seed);
                SceneEventRecorder.RecordKV("AgentSpawn", seed,
                    ("trial", trialNumber.ToString()),
                    ("position", $"{position.x:F3};{position.y:F3};{position.z:F3}"),
                    ("speed", speed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            }

            if (showDebugLogs) Debug.Log($"[AgentManager] Spawned agent #{agentId} at {position}");
            return agent;
        }

        public void DespawnAgent(GameObject agent)
        {
            if (agent == null) return;
            for (int i = activeAgents.Count - 1; i >= 0; i--)
            {
                if (activeAgents[i].gameObject == agent)
                {
                    ReturnAgentToPool(agent);
                    activeAgents.RemoveAt(i);
                    return;
                }
            }
        }

        public void DespawnAllAgents()
        {
            foreach (var a in activeAgents)
                if (a.gameObject != null) ReturnAgentToPool(a.gameObject);
            activeAgents.Clear();
            VisionConeScript.ClearCache();
        }

        public void PauseAllAgents()
        {
            foreach (var a in activeAgents)
                a.GetBehavior()?.SetMovementParameters(a.direction, 0f);
        }

        public void ResumeAllAgents()
        {
            foreach (var a in activeAgents)
                a.GetBehavior()?.SetMovementParameters(a.direction, a.targetSpeed);
        }

        public float GetNearestAgentDistance(Vector3 fromPosition)
        {
            float nearest = float.MaxValue;
            foreach (var a in activeAgents)
            {
                if (a.gameObject != null && a.gameObject.activeInHierarchy)
                {
                    float d = Vector3.Distance(a.gameObject.transform.position, fromPosition);
                    if (d < nearest) nearest = d;
                }
            }
            return nearest;
        }

        public List<Vector3> GetAgentPositions()
        {
            var list = new List<Vector3>();
            foreach (var a in activeAgents)
                if (a.gameObject != null && a.gameObject.activeInHierarchy)
                    list.Add(a.gameObject.transform.position);
            return list;
        }

        // ----- Prefab loading + cube fallback -----

        private void InitializeAgentPrefabs() { LoadRocketBoxAgentPrefabs(); }

        private void LoadRocketBoxAgentPrefabs()
        {
            var males = new List<GameObject>();
            for (int i = 1; i <= 3; i++)
            {
                GameObject p = Resources.Load<GameObject>($"Prefabs/Agents/Male_Adult_0{i}")
                            ?? Resources.Load<GameObject>($"Male_Adult_0{i}");
                if (p != null) males.Add(SetupAgentPrefab(p, $"MaleAgent_0{i}", true));
            }
            maleAgentPrefabs = males.ToArray();

            var females = new List<GameObject>();
            for (int i = 1; i <= 3; i++)
            {
                GameObject p = Resources.Load<GameObject>($"Prefabs/Agents/Female_Adult_0{i}")
                            ?? Resources.Load<GameObject>($"Female_Adult_0{i}");
                if (p != null) females.Add(SetupAgentPrefab(p, $"FemaleAgent_0{i}", false));
            }
            femaleAgentPrefabs = females.ToArray();

            // Cube fallback so the Skeleton scene works without Rocketbox installed
            if (maleAgentPrefabs.Length == 0 && femaleAgentPrefabs.Length == 0)
            {
                Debug.LogWarning("[AgentManager] No Rocketbox prefabs found in Resources/. Falling back to procedural cube agents. " +
                                 "See docs/SKELETON_AGENTS.md for instructions on installing the Microsoft Rocketbox library.");
                maleAgentPrefabs = new[] { CreateFallbackAgentPrefab("MaleFallback", new Color(0.30f, 0.55f, 0.85f)) };
                femaleAgentPrefabs = new[] { CreateFallbackAgentPrefab("FemaleFallback", new Color(0.85f, 0.50f, 0.55f)) };
                usingFallbackPrefabs = true;
            }
            else if (showDebugLogs)
            {
                Debug.Log($"[AgentManager] Loaded prefabs - Males: {maleAgentPrefabs.Length}, Females: {femaleAgentPrefabs.Length}");
            }
        }

        private GameObject SetupAgentPrefab(GameObject originalPrefab, string newName, bool isMale)
        {
            GameObject agent = Instantiate(originalPrefab);
            agent.name = newName;

            var rb = agent.GetComponent<Rigidbody>() ?? agent.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = 70f;
            rb.linearDamping = 5f;
            rb.angularDamping = 10f;
            rb.freezeRotation = true;
            rb.useGravity = true;

            var col = agent.GetComponent<CapsuleCollider>() ?? agent.AddComponent<CapsuleCollider>();
            col.isTrigger = false;
            col.height = 1.8f;
            col.radius = 0.3f;
            col.center = new Vector3(0, 0.9f, 0);

            var anim = agent.GetComponent<Animator>() ?? agent.AddComponent<Animator>();
            string controllerName = isMale ? "MaleAgentAnimatorController" : "FemaleAgentAnimatorController";
            var animController = Resources.Load<RuntimeAnimatorController>($"Animations/{controllerName}")
                              ?? Resources.Load<RuntimeAnimatorController>("Animations/AgentAnimatorController");
            if (animController != null) anim.runtimeAnimatorController = animController;

            if (agent.GetComponent<AgentBehavior>() == null) agent.AddComponent<AgentBehavior>();
            if (enableVisionConeAvoidance) AddVisionConeToAgent(agent);

            agent.SetActive(false);
            return agent;
        }

        // Cube fallback: tinted capsule + rigidbody but no Animator/AgentBehavior.
        // The agent stands still where spawned; useful before Rocketbox is installed.
        private GameObject CreateFallbackAgentPrefab(string name, Color tint)
        {
            var agent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            agent.name = name;
            var renderer = agent.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = new Material(renderer.sharedMaterial) { color = tint };

            var rb = agent.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = 70f;
            rb.linearDamping = 5f;
            rb.angularDamping = 10f;
            rb.freezeRotation = true;
            rb.useGravity = true;

            agent.SetActive(false);
            return agent;
        }

        private void AddVisionConeToAgent(GameObject agent)
        {
            var visionConeChild = new GameObject("VisionCone");
            visionConeChild.transform.SetParent(agent.transform);
            visionConeChild.transform.localPosition = Vector3.zero;
            var vc = visionConeChild.AddComponent<VisionConeScript>();
            vc.angle = visionConeAngle;
            vc.minVisionRadius = minVisionRadius;
            vc.maxVisionRadius = maxVisionRadius;
            vc.TTCThreshold = ttcThreshold;
            vc.stopDuration = stopDuration;
            vc.showDebugLine = showDebugLogs;
        }

        private void InitializePools()
        {
            maleAgentPool.Clear();
            femaleAgentPool.Clear();
            if (maleAgentPrefabs != null && maleAgentPrefabs.Length > 0)
            {
                for (int i = 0; i < poolSizePerGender; i++)
                    maleAgentPool.Enqueue(Instantiate(maleAgentPrefabs[i % maleAgentPrefabs.Length], transform));
            }
            if (femaleAgentPrefabs != null && femaleAgentPrefabs.Length > 0)
            {
                for (int i = 0; i < poolSizePerGender; i++)
                    femaleAgentPool.Enqueue(Instantiate(femaleAgentPrefabs[i % femaleAgentPrefabs.Length], transform));
            }
        }

        private GameObject GetAgentFromPool(bool isMale)
        {
            var pool = isMale ? maleAgentPool : femaleAgentPool;
            if (pool.Count > 0)
            {
                var agent = pool.Dequeue();
                agent.transform.SetParent(transform);
                return agent;
            }
            var prefabs = isMale ? maleAgentPrefabs : femaleAgentPrefabs;
            if (prefabs != null && prefabs.Length > 0)
                return Instantiate(prefabs[Random.Range(0, prefabs.Length)], transform);
            return null;
        }

        private void ReturnAgentToPool(GameObject agent)
        {
            if (agent == null) return;
            agent.GetComponent<AgentBehavior>()?.ResetForPool();
            agent.SetActive(false);
            if (agent.name.Contains("Male")) maleAgentPool.Enqueue(agent);
            else femaleAgentPool.Enqueue(agent);
        }

        private void UpdateActiveAgents()
        {
            for (int i = activeAgents.Count - 1; i >= 0; i--)
            {
                var a = activeAgents[i];
                if (a.gameObject == null) { activeAgents.RemoveAt(i); continue; }
                a.GetBehavior()?.UpdateMovement();
            }
        }

        private void OnDrawGizmos()
        {
            if (!showGizmosInScene || !Application.isPlaying) return;
            Gizmos.color = Color.green;
            foreach (var a in activeAgents)
                if (a.gameObject != null)
                    Gizmos.DrawWireSphere(a.gameObject.transform.position + Vector3.up, 0.3f);
        }
    }

    [System.Serializable]
    public class ActiveAgent
    {
        public int agentId;
        public GameObject gameObject;
        public float targetSpeed;
        public Vector3 direction;
        public float spawnTime;

        [System.NonSerialized] private AgentBehavior cachedBehavior;
        [System.NonSerialized] private Rigidbody cachedRigidbody;
        [System.NonSerialized] private Transform cachedTransform;

        public AgentBehavior GetBehavior()
        {
            if (cachedBehavior == null && gameObject != null)
                cachedBehavior = gameObject.GetComponent<AgentBehavior>();
            return cachedBehavior;
        }

        public Rigidbody GetRigidbody()
        {
            if (cachedRigidbody == null && gameObject != null)
                cachedRigidbody = gameObject.GetComponent<Rigidbody>();
            return cachedRigidbody;
        }

        public Transform GetTransform()
        {
            if (cachedTransform == null && gameObject != null)
                cachedTransform = gameObject.transform;
            return cachedTransform;
        }

        public void CacheComponents()
        {
            if (gameObject == null) return;
            GetBehavior();
            GetRigidbody();
            GetTransform();
        }
    }
}
