// SPDX-License-Identifier: MIT
using UnityEngine;
using UnityEngine.XR;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Detects collisions between the VR participant and walking agents (proximity)
/// or traffic cones (topple). On detection, resets the current trial without
/// advancing the trial counter.
/// </summary>

namespace EyeLean.Skeleton
{
    public class PlayerCollisionDetector : MonoBehaviour
    {
        [Header("Agent Collision Settings")]
        [Tooltip("Enable collision detection with walking agents")]
        [SerializeField] private bool enableAgentCollision = false;

        [Tooltip("Center-to-center distance threshold for agent collision (in meters). For reference: two people touching would be ~0.5-0.6m apart.")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float agentCollisionRadius = 0.4f;

        [Header("Obstacle Collision Settings")]
        [Tooltip("Enable collision detection with traffic cones (only triggers when cone topples)")]
        [SerializeField] private bool enableObstacleCollision = false;

        [Tooltip("Angle from vertical at which a cone is considered 'toppled' (degrees)")]
        [Range(15f, 60f)]
        [SerializeField] private float coneToppleThreshold = 30f;

        [Header("Trial Reset Behavior")]
        [Tooltip("Cooldown time after a collision before another can trigger (prevents rapid resets)")]
        [Range(0.5f, 5.0f)]
        [SerializeField] private float collisionCooldown = 2.0f;

        [Tooltip("Reset trial on collision during Navigation phase")]
        [SerializeField] private bool resetDuringNavigation = true;

        [Tooltip("Reset trial on collision during Choice phase")]
        [SerializeField] private bool resetDuringChoice = false;

        [Header("Reaction Animation")]
        [Tooltip("Play agent reaction animation before resetting trial")]
        [SerializeField] private bool enableReactionAnimation = true;

        [Tooltip("Maximum wait time for reaction animation (safety fallback)")]
        [Range(1f, 10f)]
        [SerializeField] private float reactionAnimationTimeout = 5f;

        [Header("Visual Feedback")]
        [Tooltip("Show visual feedback (red X) on collision")]
        [SerializeField] private bool showCollisionFeedback = true;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        private TrialManager trialManager;
        private CapsuleCollider playerCollider;
        private Rigidbody playerRigidbody;
        private GameObject xrRig;

        private Camera cachedMainCamera;
        private InputDevice hmdDevice;
        private bool hmdDeviceFound = false;
        private Transform xrOriginTransform;

        private bool isOnCooldown = false;
        private float lastCollisionTime = 0f;
        private int agentCollisionsThisSession = 0;
        private int obstacleCollisionsThisSession = 0;

        private bool isWaitingForReaction = false;
        private GameObject currentReactingAgent = null;

        private List<GameObject> trackedCones = new List<GameObject>();
        private bool isTrackingCones = false;

        public System.Action<GameObject> OnPlayerAgentCollision;
        public System.Action<GameObject> OnPlayerObstacleCollision;

        void Start()
        {
            FindTrialManager();
            FindAndConfigurePlayerCollider();
            InitializeVRTracking();
            SubscribeToTrialEvents();

            if (showDebugLogs)
            {
                string agentStatus = enableAgentCollision ? $"ENABLED (radius: {agentCollisionRadius}m)" : "DISABLED";
                string obstacleStatus = enableObstacleCollision ? $"ENABLED (topple threshold: {coneToppleThreshold} deg)" : "DISABLED";
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Initialized - Agent collision: {agentStatus}, Obstacle collision: {obstacleStatus}");
            }
        }

        void InitializeVRTracking()
        {
            cachedMainCamera = Camera.main;
            if (cachedMainCamera == null)
            {
                cachedMainCamera = FindFirstObjectByType<Camera>();
            }

            if (cachedMainCamera != null && showDebugLogs)
            {
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Cached main camera: '{cachedMainCamera.name}'");
            }

            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
            {
                xrOriginTransform = xrOrigin.transform;
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[PlayerCollisionDetector] Found XR Origin: '{xrOrigin.name}'");
            }

            FindHMDDevice();
        }

        void FindHMDDevice()
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, devices);

            if (devices.Count > 0)
            {
                hmdDevice = devices[0];
                hmdDeviceFound = hmdDevice.isValid;
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[PlayerCollisionDetector] Found HMD device: '{hmdDevice.name}', valid={hmdDeviceFound}");
            }
            else
            {
                hmdDeviceFound = false;
                if (showDebugLogs)
                    UnityEngine.Debug.Log("[PlayerCollisionDetector] No HMD device found (running in editor or non-VR mode)");
            }
        }

        /// <summary>
        /// Resolve player position with fallbacks in priority order:
        /// cached camera, Camera.main, HMD device + XR Origin, XR Rig.
        /// </summary>
        Vector3 GetPlayerPosition()
        {
            if (cachedMainCamera != null)
            {
                return cachedMainCamera.transform.position;
            }

            if (Camera.main != null)
            {
                cachedMainCamera = Camera.main;
                return Camera.main.transform.position;
            }

            if (hmdDeviceFound && hmdDevice.isValid && xrOriginTransform != null)
            {
                if (hmdDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPos) ||
                    hmdDevice.TryGetFeatureValue(CommonUsages.centerEyePosition, out localPos))
                {
                    Vector3 worldPos = xrOriginTransform.TransformPoint(localPos);
                    if (showDebugLogs && Time.frameCount % 60 == 0)
                    {
                        UnityEngine.Debug.Log($"[PlayerCollisionDetector] Using HMD device position: local={localPos}, world={worldPos}");
                    }
                    return worldPos;
                }
            }
            else if (!hmdDeviceFound && Time.frameCount % 120 == 0)
            {
                // HMD may connect late; retry periodically
                FindHMDDevice();
            }

            if (xrRig != null)
            {
                if (showDebugLogs && Time.frameCount % 60 == 0)
                {
                    UnityEngine.Debug.LogWarning($"[PlayerCollisionDetector] Using XR Rig fallback position: {xrRig.transform.position}");
                }
                return xrRig.transform.position;
            }

            UnityEngine.Debug.LogError("[PlayerCollisionDetector] Cannot determine player position - all methods failed!");
            return Vector3.zero;
        }

        void Update()
        {
            if (showDebugLogs && Time.frameCount % 120 == 0)
            {
                string status = "";
                if (isWaitingForReaction) status = "WAITING FOR REACTION ANIMATION";
                else if (isOnCooldown) status = "ON COOLDOWN";
                else if (!enableAgentCollision && !enableObstacleCollision) status = "ALL DETECTION DISABLED";
                else if (trialManager == null) status = "NO TRIAL MANAGER";
                else if (!ShouldTriggerReset()) status = $"PHASE {trialManager.GetCurrentPhase()} - not detecting";
                else status = "ACTIVELY DETECTING";

                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Update status: {status} | Agent: {enableAgentCollision} | Obstacle: {enableObstacleCollision}");
            }

            if (isWaitingForReaction) return; // Don't detect during reaction animation
            if (isOnCooldown) return;
            if (!ShouldTriggerReset()) return;

            if (enableAgentCollision)
            {
                CheckAgentProximityCollisions();
            }

            if (enableObstacleCollision && isTrackingCones)
            {
                CheckConeToppling();
            }
        }

        void FixedUpdate()
        {
            if (!enableObstacleCollision || !isTrackingCones) return;
            if (trackedCones.Count == 0) return;

            if (Time.frameCount % 60 != 0) return;
            if (!showDebugLogs) return;

            Vector3 playerPos = GetPlayerPosition();
            if (playerPos == Vector3.zero) return;

            // Capsule height=1.7, center.y=-0.85, so head=playerPos.y, feet=playerPos.y-1.7
            float playerTop = playerPos.y;
            float playerBottom = playerPos.y - 1.7f;

            foreach (GameObject cone in trackedCones)
            {
                if (cone == null) continue;

                Collider coneCollider = cone.GetComponent<Collider>();
                if (coneCollider == null) continue;

                Bounds coneBounds = coneCollider.bounds;
                float horizontalDist = Vector2.Distance(
                    new Vector2(playerPos.x, playerPos.z),
                    new Vector2(coneBounds.center.x, coneBounds.center.z)
                );

                UnityEngine.Debug.Log($"[PlayerCollisionDetector] CONE DEBUG '{cone.name}':" +
                    $"\n  Player: head={playerPos.y:F2}m, body bottom={playerBottom:F2}m" +
                    $"\n  Cone: top={coneBounds.max.y:F2}m, bottom={coneBounds.min.y:F2}m" +
                    $"\n  Horizontal dist: {horizontalDist:F2}m (need <0.65m to touch)" +
                    $"\n  Vertical overlap: {(playerBottom < coneBounds.max.y && playerTop > coneBounds.min.y ? "YES" : "NO")}");
            }
        }

        void FindTrialManager()
        {
            trialManager = FindFirstObjectByType<TrialManager>();
            if (trialManager == null)
            {
                UnityEngine.Debug.LogError("[PlayerCollisionDetector] TrialManager not found in scene!");
            }
            else
            {
                if (showDebugLogs)
                    UnityEngine.Debug.Log("[PlayerCollisionDetector] Connected to TrialManager");
            }
        }

        void FindAndConfigurePlayerCollider()
        {
            xrRig = GameObject.FindWithTag("Player");

            if (xrRig == null)
            {
                string[] xrRigNames = { "XRRig", "XR Rig", "XR Origin" };
                foreach (var name in xrRigNames)
                {
                    xrRig = GameObject.Find(name);
                    if (xrRig != null) break;
                }
            }

            if (xrRig == null && Camera.main != null)
            {
                xrRig = Camera.main.transform.root.gameObject;
            }

            if (xrRig == null)
            {
                UnityEngine.Debug.LogWarning("[PlayerCollisionDetector] Could not find XRRig! Collision detection may not work.");
                return;
            }

            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Found XRRig: '{xrRig.name}'");

            playerCollider = xrRig.GetComponent<CapsuleCollider>();
            if (playerCollider == null)
            {
                if (Camera.main != null)
                {
                    playerCollider = Camera.main.GetComponent<CapsuleCollider>();
                }
            }

            if (playerCollider != null)
            {
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[PlayerCollisionDetector] Found player collider on '{playerCollider.gameObject.name}'");
            }
            else
            {
                if (showDebugLogs)
                    UnityEngine.Debug.LogWarning("[PlayerCollisionDetector] No CapsuleCollider found on XRRig or Main Camera");
            }

            playerRigidbody = xrRig.GetComponent<Rigidbody>();
            if (playerRigidbody == null && Camera.main != null)
            {
                playerRigidbody = Camera.main.GetComponent<Rigidbody>();
            }
        }

        void SubscribeToTrialEvents()
        {
            if (trialManager != null)
            {
                trialManager.OnPhaseChanged += OnTrialPhaseChanged;
                if (showDebugLogs)
                    UnityEngine.Debug.Log("[PlayerCollisionDetector] Subscribed to TrialManager phase changes");
            }
        }

        void OnTrialPhaseChanged(TrialManager.TrialPhase newPhase)
        {
            if (newPhase == TrialManager.TrialPhase.InterTrialInterval)
            {
                isOnCooldown = false;
                StopTrackingCones();

                if (showDebugLogs)
                {
                    UnityEngine.Debug.Log("[PlayerCollisionDetector] Cooldown reset - entering ITI");
                }
            }
            else if (newPhase == TrialManager.TrialPhase.ExperimentalPhase)
            {
                if (enableObstacleCollision)
                {
                    StartTrackingCones();
                }
            }

            if (showDebugLogs)
            {
                bool willDetect = ShouldTriggerResetForPhase(newPhase);
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Phase changed to {newPhase} - Detection active: {willDetect}");
            }
        }

        // Cones may not exist yet when ExperimentalPhase starts, so tracking re-scans periodically.
        void StartTrackingCones()
        {
            trackedCones.Clear();
            isTrackingCones = true;

            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Obstacle tracking ENABLED - will search for traffic cones");

            RefreshConeTracking();
        }

        void RefreshConeTracking()
        {
            int previousCount = trackedCones.Count;

            trackedCones.RemoveAll(c => c == null);

            GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in allObjects)
            {
                if (obj.name.StartsWith("TrafficCone_") && !trackedCones.Contains(obj))
                {
                    trackedCones.Add(obj);
                }
            }

            if (trackedCones.Count > previousCount && showDebugLogs)
            {
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Now tracking {trackedCones.Count} traffic cones for topple detection");
            }
        }

        void StopTrackingCones()
        {
            if (trackedCones.Count > 0 && showDebugLogs)
            {
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Stopped tracking {trackedCones.Count} traffic cones");
            }
            trackedCones.Clear();
            isTrackingCones = false;
        }

        bool ShouldTriggerReset()
        {
            if (trialManager == null) return false;
            return ShouldTriggerResetForPhase(trialManager.GetCurrentPhase());
        }

        bool ShouldTriggerResetForPhase(TrialManager.TrialPhase phase)
        {
            switch (phase)
            {
                case TrialManager.TrialPhase.ExperimentalPhase:
                    return resetDuringNavigation || resetDuringChoice;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Center-to-center proximity check against AgentBehavior root positions
        /// (avoids measuring to child VisionCone/Gap colliders which are positioned dynamically).
        /// </summary>
        void CheckAgentProximityCollisions()
        {
            Vector3 playerPosition = GetPlayerPosition();

            if (playerPosition == Vector3.zero)
            {
                if (showDebugLogs && Time.frameCount % 60 == 0)
                {
                    UnityEngine.Debug.LogWarning("[PlayerCollisionDetector] Player position is at origin - skipping collision check");
                }
                return;
            }

            // Wide search radius then exact distance test; agent body colliders are ~0.3m
            float searchRadius = 2.0f;
            Collider[] nearbyColliders = Physics.OverlapSphere(playerPosition, searchRadius);

            if (showDebugLogs && Time.frameCount % 60 == 0)
            {
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Proximity check - Player pos: {playerPosition}, found {nearbyColliders.Length} colliders in {searchRadius}m radius");
            }

            float closestAgentDistance = float.MaxValue;
            string closestAgentName = null;

            // Each agent has multiple colliders; dedupe by AgentBehavior reference
            HashSet<AgentBehavior> checkedAgents = new HashSet<AgentBehavior>();

            foreach (var collider in nearbyColliders)
            {
                if (collider == null) continue;

                if (IsAgentCollision(collider.gameObject, out AgentBehavior agentBehavior))
                {
                    if (agentBehavior != null && checkedAgents.Contains(agentBehavior))
                    {
                        continue;
                    }
                    if (agentBehavior != null)
                    {
                        checkedAgents.Add(agentBehavior);
                    }

                    Vector3 agentPosition;
                    string agentName;

                    if (agentBehavior != null)
                    {
                        agentPosition = agentBehavior.transform.position;
                        agentName = agentBehavior.gameObject.name;
                    }
                    else
                    {
                        agentPosition = collider.transform.position;
                        agentName = collider.gameObject.name;
                    }

                    // Horizontal-only so HMD height variance doesn't change collision threshold
                    float horizontalDistance = Vector2.Distance(
                        new Vector2(playerPosition.x, playerPosition.z),
                        new Vector2(agentPosition.x, agentPosition.z)
                    );

                    if (horizontalDistance < closestAgentDistance)
                    {
                        closestAgentDistance = horizontalDistance;
                        closestAgentName = agentName;
                    }

                    if (horizontalDistance <= agentCollisionRadius)
                    {
                        if (showDebugLogs)
                            UnityEngine.Debug.Log($"[PlayerCollisionDetector] COLLISION TRIGGERED - Agent '{agentName}' at distance {horizontalDistance:F3}m <= threshold {agentCollisionRadius}m");
                        ProcessAgentCollision(agentBehavior != null ? agentBehavior.gameObject : collider.gameObject, playerPosition);
                        break;
                    }
                }
            }

            if (showDebugLogs && Time.frameCount % 60 == 0 && closestAgentName != null)
            {
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Closest agent: '{closestAgentName}' at {closestAgentDistance:F2}m (threshold: {agentCollisionRadius}m)");
            }
        }

        void CheckConeToppling()
        {
            if (trackedCones.Count == 0)
            {
                // Cones spawn after the experimental phase starts; keep scanning
                if (Time.frameCount % 30 == 0)
                {
                    RefreshConeTracking();
                }
                return;
            }

            for (int i = trackedCones.Count - 1; i >= 0; i--)
            {
                GameObject cone = trackedCones[i];

                if (cone == null)
                {
                    trackedCones.RemoveAt(i);
                    continue;
                }

                // eulerAngles is 0-360; convert to -180..180 for tilt comparison
                float xAngle = cone.transform.eulerAngles.x;
                float zAngle = cone.transform.eulerAngles.z;

                if (xAngle > 180f) xAngle -= 360f;
                if (zAngle > 180f) zAngle -= 360f;

                float xTilt = Mathf.Abs(xAngle);
                float zTilt = Mathf.Abs(zAngle);

                if (showDebugLogs && Time.frameCount % 120 == 0 && i == 0)
                {
                    UnityEngine.Debug.Log($"[PlayerCollisionDetector] Cone '{cone.name}' tilt: X={xTilt:F1} deg, Z={zTilt:F1} deg (threshold: {coneToppleThreshold} deg)");
                }

                if (xTilt > coneToppleThreshold || zTilt > coneToppleThreshold)
                {
                    if (showDebugLogs)
                        UnityEngine.Debug.Log($"[PlayerCollisionDetector] CONE TOPPLE DETECTED - '{cone.name}' at X={xTilt:F1} deg, Z={zTilt:F1} deg > threshold {coneToppleThreshold} deg");
                    ProcessObstacleCollision(cone);

                    trackedCones.RemoveAt(i);
                    break;
                }
            }
        }

        // OnCollisionEnter may not fire reliably with kinematic rigidbodies; the
        // proximity check in CheckAgentProximityCollisions is the canonical path.
        void OnCollisionEnter(Collision collision)
        {
            if (!enableAgentCollision) return;
            if (isOnCooldown) return;

            if (!IsAgentCollision(collision.gameObject)) return;

            if (!ShouldTriggerReset()) return;

            Vector3 playerPosition = GetPlayerPosition();

            ProcessAgentCollision(collision.gameObject, playerPosition);
        }

        /// <summary>
        /// Identify whether a collider belongs to an agent body. VisionCone/Gap
        /// children move dynamically and don't reflect the agent's body position,
        /// so they are excluded.
        /// </summary>
        bool IsAgentCollision(GameObject other, out AgentBehavior agentBehavior)
        {
            agentBehavior = null;

            if (other == xrRig) return false;
            if (other.CompareTag("Player")) return false;

            string objName = other.name;
            if (objName == "Gap" || objName == "VisionCone" || objName.StartsWith("VisionCone"))
            {
                return false;
            }

            agentBehavior = other.GetComponent<AgentBehavior>();
            if (agentBehavior != null)
            {
                return true;
            }

            // Direct child of an AgentBehavior counts as a body collider; grandchildren
            // (e.g. VisionCone children) do not because they don't track the body
            AgentBehavior parentAgent = other.GetComponentInParent<AgentBehavior>();
            if (parentAgent != null)
            {
                if (other.transform.parent != null && other.transform.parent.GetComponent<AgentBehavior>() != null)
                {
                    agentBehavior = parentAgent;
                    return true;
                }
                if (showDebugLogs && Time.frameCount % 120 == 0)
                {
                    UnityEngine.Debug.Log($"[PlayerCollisionDetector] Skipping '{objName}' - grandchild of agent, not direct body collider");
                }
                return false;
            }

            // Last-resort name match for prefabs without an AgentBehavior
            if (objName.Contains("Male") || objName.Contains("Female"))
            {
                if (objName.Contains("Adult") || objName.Contains("Agent"))
                {
                    if (showDebugLogs && Time.frameCount % 120 == 0)
                    {
                        UnityEngine.Debug.Log($"[PlayerCollisionDetector] Agent matched by name pattern: '{objName}'");
                    }
                    return true;
                }
            }

            return false;
        }

        bool IsAgentCollision(GameObject other)
        {
            return IsAgentCollision(other, out _);
        }

        void ProcessAgentCollision(GameObject agent, Vector3 playerPosition)
        {
            // Start cooldown synchronously to prevent re-triggering this frame
            isOnCooldown = true;
            lastCollisionTime = Time.time;
            agentCollisionsThisSession++;

            Vector3 agentPosition = agent.transform.position;
            float horizontalDistance = Vector2.Distance(
                new Vector2(playerPosition.x, playerPosition.z),
                new Vector2(agentPosition.x, agentPosition.z)
            );

            if (showDebugLogs)
            {
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] AGENT COLLISION with '{agent.name}' - distance: {horizontalDistance:F2}m (threshold: {agentCollisionRadius}m)");
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Agent collisions this session: {agentCollisionsThisSession}");
            }

            AgentBehavior collidedAgentBehavior = agent.GetComponent<AgentBehavior>();
            if (collidedAgentBehavior == null)
            {
                collidedAgentBehavior = agent.GetComponentInParent<AgentBehavior>();
            }

            // Pause all OTHER agents; the collided one keeps moving for the reaction animation
            AgentBehavior.PauseAllAgents(collidedAgentBehavior);

            if (showCollisionFeedback)
            {
                if (TrialFeedbackUI.Instance != null)
                {
                    TrialFeedbackUI.Instance.ShowFailureFeedback();
                    if (showDebugLogs)
                        UnityEngine.Debug.Log("[PlayerCollisionDetector] Showing FAILURE feedback (red X)");
                }
                else
                {
                    UnityEngine.Debug.LogWarning("[PlayerCollisionDetector] TrialFeedbackUI not found in scene! Add TrialFeedbackUI component to a GameObject.");
                }
            }

            // Fire event before reset so subscribers can capture state
            OnPlayerAgentCollision?.Invoke(agent);

            if (enableReactionAnimation && collidedAgentBehavior != null)
            {
                isWaitingForReaction = true;
                currentReactingAgent = agent;

                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[PlayerCollisionDetector] Triggering REACTION animation on '{agent.name}' - trial will reset after animation completes");

                collidedAgentBehavior.PlayReactionAnimation(() =>
                {
                    OnReactionAnimationComplete(agent);
                });

                StartCoroutine(ReactionTimeoutRoutine(agent));
                return;
            }

            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Trial will reset immediately (reaction animation disabled or unavailable)");
            ResetTrial("agent collision");

            StartCoroutine(CollisionCooldownRoutine());
        }

        void OnReactionAnimationComplete(GameObject agent)
        {
            if (!isWaitingForReaction)
            {
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[PlayerCollisionDetector] Reaction callback received but not waiting (timeout may have triggered)");
                return;
            }

            isWaitingForReaction = false;
            currentReactingAgent = null;

            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Reaction animation COMPLETE - now resetting trial");
            ResetTrial("agent collision (after reaction animation)");

            StartCoroutine(CollisionCooldownRoutine());
        }

        // Safety net: force a trial reset if the reaction animation never invokes its callback
        IEnumerator ReactionTimeoutRoutine(GameObject agent)
        {
            yield return new WaitForSeconds(reactionAnimationTimeout);

            if (isWaitingForReaction && currentReactingAgent == agent)
            {
                UnityEngine.Debug.LogWarning($"[PlayerCollisionDetector] Reaction animation TIMEOUT after {reactionAnimationTimeout}s - forcing trial reset");

                AgentBehavior agentBehavior = agent?.GetComponent<AgentBehavior>();
                if (agentBehavior != null)
                {
                    agentBehavior.CancelReaction();
                }

                isWaitingForReaction = false;
                currentReactingAgent = null;

                ResetTrial("agent collision (reaction timeout)");
                StartCoroutine(CollisionCooldownRoutine());
            }
        }

        void ProcessObstacleCollision(GameObject cone)
        {
            // Start cooldown synchronously to prevent re-triggering this frame
            isOnCooldown = true;
            lastCollisionTime = Time.time;
            obstacleCollisionsThisSession++;

            float xTilt = cone.transform.eulerAngles.x;
            float zTilt = cone.transform.eulerAngles.z;
            if (xTilt > 180f) xTilt -= 360f;
            if (zTilt > 180f) zTilt -= 360f;

            if (showDebugLogs)
            {
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] CONE TOPPLED: '{cone.name}' (tilt: X={Mathf.Abs(xTilt):F1} deg, Z={Mathf.Abs(zTilt):F1} deg)");
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Trial will reset. Obstacle collisions this session: {obstacleCollisionsThisSession}");
            }

            // Pause every agent so the scene freezes while feedback plays
            AgentBehavior.PauseAllAgents();

            if (showCollisionFeedback)
            {
                if (TrialFeedbackUI.Instance != null)
                {
                    TrialFeedbackUI.Instance.ShowFailureFeedback();
                    if (showDebugLogs)
                        UnityEngine.Debug.Log("[PlayerCollisionDetector] Showing FAILURE feedback (red X) for cone topple");
                }
                else
                {
                    UnityEngine.Debug.LogWarning("[PlayerCollisionDetector] TrialFeedbackUI not found in scene! Add TrialFeedbackUI component to a GameObject.");
                }
            }

            // Fire event before reset so subscribers can capture state
            OnPlayerObstacleCollision?.Invoke(cone);

            ResetTrial("cone toppled");

            StartCoroutine(CollisionCooldownRoutine());
        }

        // SetPhase resets to ITI without advancing the trial counter (vs CompleteTrial)
        void ResetTrial(string reason)
        {
            if (trialManager != null)
            {
                TrialManager.TrialPhase currentPhase = trialManager.GetCurrentPhase();
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[PlayerCollisionDetector] Resetting trial from {currentPhase} to InterTrialInterval (reason: {reason})");

                trialManager.SetPhase(TrialManager.TrialPhase.InterTrialInterval);
            }
        }

        IEnumerator CollisionCooldownRoutine()
        {
            yield return new WaitForSeconds(collisionCooldown);
            isOnCooldown = false;

            if (showDebugLogs)
            {
                UnityEngine.Debug.Log("[PlayerCollisionDetector] Cooldown complete - collision detection re-enabled");
            }
        }

        #region Public API

        /// <summary>Enable/disable agent collision detection at runtime.</summary>
        public void SetAgentCollisionEnabled(bool enabled)
        {
            enableAgentCollision = enabled;
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Agent collision detection {(enabled ? "ENABLED" : "DISABLED")}");
        }

        /// <summary>Enable/disable obstacle (cone) collision detection at runtime.</summary>
        public void SetObstacleCollisionEnabled(bool enabled)
        {
            enableObstacleCollision = enabled;
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Obstacle collision detection {(enabled ? "ENABLED" : "DISABLED")}");

            if (trialManager != null && trialManager.GetCurrentPhase() == TrialManager.TrialPhase.ExperimentalPhase)
            {
                if (enabled && !isTrackingCones)
                {
                    StartTrackingCones();
                }
                else if (!enabled && isTrackingCones)
                {
                    StopTrackingCones();
                }
            }
        }

        /// <summary>True if agent collision detection is enabled.</summary>
        public bool IsAgentCollisionEnabled()
        {
            return enableAgentCollision;
        }

        /// <summary>True if obstacle collision detection is enabled.</summary>
        public bool IsObstacleCollisionEnabled()
        {
            return enableObstacleCollision;
        }

        /// <summary>Set the agent collision radius (meters, clamped 0.1-1.0).</summary>
        public void SetAgentCollisionRadius(float radius)
        {
            agentCollisionRadius = Mathf.Clamp(radius, 0.1f, 1.0f);
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Agent collision radius updated to {agentCollisionRadius}m");
        }

        /// <summary>Current agent collision radius in meters.</summary>
        public float GetAgentCollisionRadius()
        {
            return agentCollisionRadius;
        }

        /// <summary>Set the cone topple threshold (degrees from vertical, clamped 15-60).</summary>
        public void SetConeToppleThreshold(float degrees)
        {
            coneToppleThreshold = Mathf.Clamp(degrees, 15f, 60f);
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Cone topple threshold updated to {coneToppleThreshold} deg");
        }

        /// <summary>Total agent collisions this session.</summary>
        public int GetAgentCollisionCount()
        {
            return agentCollisionsThisSession;
        }

        /// <summary>Total obstacle (cone topple) collisions this session.</summary>
        public int GetObstacleCollisionCount()
        {
            return obstacleCollisionsThisSession;
        }

        /// <summary>Reset session collision counters.</summary>
        public void ResetCollisionCounts()
        {
            agentCollisionsThisSession = 0;
            obstacleCollisionsThisSession = 0;
            if (showDebugLogs)
                UnityEngine.Debug.Log("[PlayerCollisionDetector] Collision counts reset");
        }

        /// <summary>True if collision detection is currently in cooldown.</summary>
        public bool IsOnCooldown()
        {
            return isOnCooldown;
        }

        /// <summary>Enable/disable the agent reaction animation that runs before the trial reset.</summary>
        public void SetReactionAnimationEnabled(bool enabled)
        {
            enableReactionAnimation = enabled;
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[PlayerCollisionDetector] Reaction animation {(enabled ? "ENABLED" : "DISABLED")}");
        }

        /// <summary>True if the reaction animation is enabled.</summary>
        public bool IsReactionAnimationEnabled()
        {
            return enableReactionAnimation;
        }

        /// <summary>True while a reaction animation is playing (trial reset deferred until it completes).</summary>
        public bool IsWaitingForReaction()
        {
            return isWaitingForReaction;
        }

        #endregion

        #region Debug

        void OnDrawGizmosSelected()
        {
            Camera cam = cachedMainCamera != null ? cachedMainCamera : Camera.main;
            if (enableAgentCollision && cam != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(cam.transform.position, agentCollisionRadius);
            }

            if (enableObstacleCollision && isTrackingCones)
            {
                Gizmos.color = Color.yellow;
                foreach (var cone in trackedCones)
                {
                    if (cone != null)
                    {
                        Gizmos.DrawWireCube(cone.transform.position + Vector3.up * 0.5f, Vector3.one * 0.3f);
                    }
                }
            }
        }

        void OnGUI()
        {
            if (!showDebugLogs || !Application.isPlaying) return;

            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(10, 220, 320, 220));
            GUILayout.Label("<b>COLLISION DETECTOR DEBUG</b>");

            string agentStatus = enableAgentCollision ? $"ON (r={agentCollisionRadius:F2}m)" : "OFF";
            GUILayout.Label($"Agent Collision: {agentStatus}");
            GUILayout.Label($"  Collisions: {agentCollisionsThisSession}");

            string obstacleStatus = enableObstacleCollision ? $"ON (thresh={coneToppleThreshold:F0} deg)" : "OFF";
            GUILayout.Label($"Obstacle Collision: {obstacleStatus}");
            GUILayout.Label($"  Tracked Cones: {trackedCones.Count}");
            GUILayout.Label($"  Collisions: {obstacleCollisionsThisSession}");

            string reactionStatus = enableReactionAnimation ? "ON" : "OFF";
            GUILayout.Label($"Reaction Animation: {reactionStatus}");
            if (isWaitingForReaction)
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"  >>> PLAYING REACTION <<<");
                GUI.color = Color.white;
            }

            GUILayout.Label($"On Cooldown: {isOnCooldown}");

            if (trialManager != null)
            {
                bool detecting = ShouldTriggerReset() && !isWaitingForReaction;
                GUILayout.Label($"Currently Detecting: {detecting}");
            }

            GUILayout.EndArea();
        }

        #endregion

        void OnDestroy()
        {
            if (trialManager != null)
            {
                trialManager.OnPhaseChanged -= OnTrialPhaseChanged;
            }
        }
    }
}
