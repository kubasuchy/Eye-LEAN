// SPDX-License-Identifier: MIT
using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// Walking agent: animation-driven motion with VisionCone collision avoidance,
/// reaction animations, and a static registry for fast scene-wide pause/resume.
/// </summary>

namespace EyeLean.Skeleton
{
    public class AgentBehavior : MonoBehaviour
    {
        // Debug logging is expensive on mobile VR; leave off in production
        public static bool EnableDebugLogging = false;

        [Header("Basic Movement")]
        public VisionConeScript visionCone;
        public Vector3 targetDirection = Vector3.forward;
        public float targetSpeed = 1.0f;

        [Header("Reaction Animation")]
        [Tooltip("Number of reaction animations available in the animator")]
        public int reactionClipCount = 3;

        // Boundaries set at runtime from EnvironmentManager
        private float environmentMinX = -1.0f;
        private float environmentMaxX = 1.0f;
        private float environmentWidth = 3.0f;

        private Rigidbody rb;
        private Animator animator;

        private bool hasStarted = false;
        private bool shouldMove = true;

        // true = lock to walking state; false = allow state machine transitions
        private bool restrictToWalkingState = false;

        private bool wasWalkingLastFrame = false;
        private float lastStateChangeTime = 0f;

        private int walkStyleIndex = 0;
        private float clipInherentSpeed = 1.0f;
        private bool hasInitializedAnimationSpeed = false;

        private bool isPlayingReaction = false;
        private Action onReactionComplete;
        private Coroutine reactionCoroutine;

        private bool isPaused = false;
        private float pausedAnimatorSpeed = 1f;

        // Throttle raycasts per-agent. Each agent has a different offset so the
        // overall raycast load is spread evenly across frames.
        private int raycastFrameOffset;
        private float cachedNearestDistance = float.MaxValue;
        private const int RAYCAST_INTERVAL = 4; // ~18 Hz at 72 Hz display

        // Static registry avoids FindObjectsOfType in PauseAllAgents
        private static System.Collections.Generic.HashSet<AgentBehavior> allAgents = new System.Collections.Generic.HashSet<AgentBehavior>();

        void Start()
        {
            InitializeComponents();
        }

        /// <summary>OnEnable runs on every pool re-use, so re-initialization happens here as well as Start.</summary>
        void OnEnable()
        {
            if (EnableDebugLogging) Debug.Log($"[AGENT_ENABLE] {gameObject.name} OnEnable called - hasStarted={hasStarted}, restrictToWalking={restrictToWalkingState}");

            allAgents.Add(this);

            raycastFrameOffset = GetInstanceID() % RAYCAST_INTERVAL;
            cachedNearestDistance = float.MaxValue;

            framesSinceEnable = 0;

            if (hasStarted)
            {
                ForceImmediateWalkingState();
            }
        }

        void OnDisable()
        {
            allAgents.Remove(this);

            VisionConeScript.RemoveFromCache(gameObject);

            CancelReaction();
        }

        void InitializeComponents()
        {
            if (visionCone == null)
            {
                visionCone = GetComponentInChildren<VisionConeScript>();
            }

            animator = GetComponent<Animator>();
            rb = GetComponent<Rigidbody>();

            if (animator != null)
            {
                // Root motion lets the walking animation drive position
                animator.applyRootMotion = true;
            }

            CalculateEnvironmentBoundaries();

            hasStarted = true;

            ForceImmediateWalkingState();

            if (EnableDebugLogging) Debug.Log($"[AGENT_INIT] {gameObject.name} initialization complete");
        }

        // Force walking state from both Start and OnEnable so pooled agents reuse cleanly
        void ForceImmediateWalkingState()
        {
            if (restrictToWalkingState && animator != null)
            {
                animator.SetBool("IsWalking", true);
                animator.SetInteger("WalkStyleIndex", walkStyleIndex);
            }
            else if (shouldMove && targetSpeed > 0.1f && animator != null)
            {
                animator.SetBool("IsWalking", true);
            }
        }

        // Backup walk-state enforcement for the first few frames after enable
        private int framesSinceEnable = 0;
        private const int FORCE_WALK_FRAMES = 5;

        public void SetMovementParameters(Vector3 direction, float speed)
        {
            targetDirection = direction.normalized;
            targetSpeed = speed;

            // Spawn agents already in motion for natural pedestrian flow
            if (speed > 0.1f)
            {
                shouldMove = true;

                if (targetDirection.magnitude > 0.1f)
                {
                    transform.rotation = Quaternion.LookRotation(targetDirection.normalized);
                }

                if (hasStarted)
                {
                    UpdateBasicAnimation();
                }
            }
        }

        public void SetAnimatorRestriction(bool restrictToWalking)
        {
            restrictToWalkingState = restrictToWalking;

            if (restrictToWalking && animator != null && hasStarted)
            {
                animator.SetBool("IsWalking", true);
            }
        }

        /// <summary>Assign walk style index and target speed at spawn.</summary>
        public void InitializeAnimationStyle(int styleIndex, float targetWalkSpeed)
        {
            walkStyleIndex = styleIndex;
            targetSpeed = targetWalkSpeed;
            hasInitializedAnimationSpeed = false;

            if (animator != null)
            {
                animator.SetInteger("WalkStyleIndex", walkStyleIndex);
                if (EnableDebugLogging) Debug.Log($"[ANIM DEBUG] INIT {gameObject.name}: StyleIndex={walkStyleIndex}, TargetSpeed={targetWalkSpeed:F3}m/s");
            }
        }

        // Run in LateUpdate so the animator has actually entered the walking state
        // before its current clip is read
        private void ApplyAnimationSpeedMultiplier()
        {
            if (hasInitializedAnimationSpeed || animator == null) return;
            if (!animator.GetBool("IsWalking")) return;

            AnimatorClipInfo[] clipInfos = animator.GetCurrentAnimatorClipInfo(0);
            if (clipInfos.Length > 0)
            {
                AnimationClip clip = clipInfos[0].clip;
                clipInherentSpeed = clip.averageSpeed.magnitude;

                if (clipInherentSpeed < 0.1f)
                {
                    clipInherentSpeed = 1.0f;
                    if (EnableDebugLogging) Debug.LogWarning($"[ANIM DEBUG] {gameObject.name}: Clip '{clip.name}' has zero averageSpeed, using fallback 1.0 m/s");
                }

                float rawMultiplier = targetSpeed / clipInherentSpeed;
                float clampedMultiplier = Mathf.Clamp(rawMultiplier, 0.5f, 2.0f);
                bool wasClamped = Mathf.Abs(rawMultiplier - clampedMultiplier) > 0.01f;

                animator.speed = clampedMultiplier;
                hasInitializedAnimationSpeed = true;

                if (EnableDebugLogging)
                {
                    Debug.Log($"[ANIM DEBUG] SPEED {gameObject.name}: " +
                              $"Clip='{clip.name}', " +
                              $"Style={walkStyleIndex}, " +
                              $"Target={targetSpeed:F3}m/s, " +
                              $"Inherent={clipInherentSpeed:F3}m/s, " +
                              $"Multiplier={clampedMultiplier:F3}" +
                              (wasClamped ? $" (CLAMPED from {rawMultiplier:F3})" : ""));
                }
            }
            else
            {
                // Clip info not yet populated; retry on next frame
                if (EnableDebugLogging) Debug.LogWarning($"[ANIM DEBUG] {gameObject.name}: No clip info available yet, deferring speed calculation");
            }
        }

        /// <summary>Reset animation state when returning the agent to its pool.</summary>
        public void ResetForPool()
        {
            if (EnableDebugLogging) Debug.Log($"[ANIM DEBUG] RESET {gameObject.name}: Clearing animation state for pool reuse");
            hasInitializedAnimationSpeed = false;
            walkStyleIndex = 0;
            if (animator != null) animator.speed = 1.0f;
        }

        void CalculateEnvironmentBoundaries()
        {
            EnvironmentManager envManager = FindFirstObjectByType<EnvironmentManager>();
            if (envManager != null)
            {
                environmentWidth = envManager.environmentWidth;
                if (EnableDebugLogging) Debug.Log($"[AgentBehavior] Found environment width: {environmentWidth}m from EnvironmentManager");
            }
            else
            {
                environmentWidth = 3.0f;
                if (EnableDebugLogging) Debug.LogWarning($"[AgentBehavior] EnvironmentManager not found, using default width: {environmentWidth}m");
            }

            // Environment is centered at X=0
            float halfWidth = environmentWidth * 0.5f;
            environmentMinX = -halfWidth;
            environmentMaxX = halfWidth;

            if (EnableDebugLogging) Debug.Log($"[AgentBehavior] Boundaries calculated: [{environmentMinX:F2}, {environmentMaxX:F2}] for {environmentWidth}m environment");
        }
        
        void Update()
        {
            if (isPaused) return;

            // Belt-and-braces: re-assert walking state for the first few frames after
            // enable so the animator has time to settle into the walking sub-state
            if (restrictToWalkingState && framesSinceEnable < FORCE_WALK_FRAMES && animator != null)
            {
                animator.SetBool("IsWalking", true);
                framesSinceEnable++;
            }
        }

        void LateUpdate()
        {
            if (isPaused) return;

            if (!hasInitializedAnimationSpeed && hasStarted)
            {
                ApplyAnimationSpeedMultiplier();
            }
        }

        public void UpdateMovement()
        {
            if (isPaused) return;

            if (!hasStarted || targetDirection == Vector3.zero || targetSpeed <= 0 || !shouldMove)
            {
                UpdateBasicAnimation();
                return;
            }

            bool visionConeTriggered = false;
            float collisionSlowdownFactor = 1.0f;

            if (visionCone != null)
            {
                visionConeTriggered = visionCone.STOP;

                if (visionConeTriggered && restrictToWalkingState)
                {
                    // Slowdown is graded by distance: VisionCone.STOP only flags risk,
                    // proximity decides severity
                    float nearestDistance = GetNearestAgentDistance();

                    if (nearestDistance < 0.6f)
                    {
                        collisionSlowdownFactor = 0.4f;
                    }
                    else if (nearestDistance < 1.0f)
                    {
                        collisionSlowdownFactor = 0.6f;
                    }
                    else if (nearestDistance < 1.5f)
                    {
                        collisionSlowdownFactor = 0.8f;
                    }
                }
                else if (visionConeTriggered)
                {
                    // Non-restricted mode: stop completely on collision risk
                    if (EnableDebugLogging) Debug.LogWarning($"[COLLISION] {gameObject.name} STOPPING - normal mode");
                }
            }

            EnforceEnvironmentBoundaries();

            // Position is animation-driven (root motion); only rotation is set here
            if (targetDirection.magnitude > 0.1f)
            {
                transform.rotation = Quaternion.LookRotation(targetDirection.normalized);
            }

            UpdateCollisionAvoidanceAnimation(visionConeTriggered, collisionSlowdownFactor);
        }

        private static int agentLayerMask = -1;
        private static bool layerMaskInitialized = false;

        /// <summary>Distance to nearest agent in front (throttled, layer-masked, cached).</summary>
        float GetNearestAgentDistance()
        {
            if ((Time.frameCount + raycastFrameOffset) % RAYCAST_INTERVAL != 0)
            {
                return cachedNearestDistance;
            }

            float nearestDistance = float.MaxValue;

            if (!layerMaskInitialized)
            {
                int agentLayer = LayerMask.NameToLayer("Agent");
                int characterLayer = LayerMask.NameToLayer("Character");
                int defaultLayer = LayerMask.NameToLayer("Default");

                if (agentLayer >= 0)
                    agentLayerMask = 1 << agentLayer;
                else if (characterLayer >= 0)
                    agentLayerMask = 1 << characterLayer;
                else
                    agentLayerMask = 1 << defaultLayer;

                layerMaskInitialized = true;
            }

            // Single forward raycast is much cheaper than SphereCastAll and sufficient here
            float detectionRange = visionCone != null ? visionCone.maxVisionRadius : 2.5f;

            if (Physics.Raycast(
                transform.position + Vector3.up * 0.9f,
                transform.forward,
                out RaycastHit hit,
                detectionRange,
                agentLayerMask,
                QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.root != transform.root)
                {
                    nearestDistance = hit.distance;
                }
            }

            cachedNearestDistance = nearestDistance;
            return nearestDistance;
        }

        void UpdateBasicAnimation()
        {
            if (animator == null) return;

            bool isMoving = (targetSpeed > 0.1f && shouldMove && hasStarted);

            if (animator.runtimeAnimatorController != null)
            {
                if (restrictToWalkingState)
                {
                    animator.SetBool("IsWalking", true);
                    if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} animation: IsWalking = true (restricted mode)");
                }
                else
                {
                    animator.SetBool("IsWalking", isMoving);
                    if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} animation: IsWalking = {isMoving}");
                }
            }
        }

        void UpdateCollisionAvoidanceAnimation(bool visionConeTriggered, float slowdownFactor = 1.0f)
        {
            if (animator == null) return;

            if (restrictToWalkingState)
            {
                // Walking-only mode: keep walking but modulate speed for avoidance
                animator.SetBool("IsWalking", true);

                if (hasInitializedAnimationSpeed && visionConeTriggered)
                {
                    float baseMultiplier = targetSpeed / Mathf.Max(clipInherentSpeed, 0.1f);
                    float adjustedMultiplier = Mathf.Clamp(baseMultiplier * slowdownFactor, 0.15f, 2.0f);
                    animator.speed = adjustedMultiplier;
                }
                else if (hasInitializedAnimationSpeed)
                {
                    float normalMultiplier = Mathf.Clamp(targetSpeed / Mathf.Max(clipInherentSpeed, 0.1f), 0.5f, 2.0f);
                    animator.speed = normalMultiplier;
                }
            }
            else
            {
                // Normal mode: stop walking entirely on avoidance
                bool shouldWalk = (targetSpeed > 0.1f && shouldMove && hasStarted && !visionConeTriggered);

                if (shouldWalk != wasWalkingLastFrame)
                {
                    float timeSinceLastChange = Time.time - lastStateChangeTime;
                    if (EnableDebugLogging)
                    {
                        string transitionType = shouldWalk ? "IDLE->WALKING" : "WALKING->IDLE";
                        string reason = !shouldWalk ? (visionConeTriggered ? "collision avoidance" : "other") : "collision cleared";
                        Debug.LogWarning($"[TRANSITION] {gameObject.name} {transitionType} after {timeSinceLastChange:F2}s - Reason: {reason}");
                    }
                    lastStateChangeTime = Time.time;
                    wasWalkingLastFrame = shouldWalk;
                }

                animator.SetBool("IsWalking", shouldWalk);
            }
        }

        void EnforceEnvironmentBoundaries()
        {
            Vector3 currentPos = transform.position;

            // Only intervene when the agent has strayed outside the bounds
            if (currentPos.x < environmentMinX || currentPos.x > environmentMaxX)
            {
                // Clamp X only; preserve Y and Z from the root-motion animation
                float clampedX = Mathf.Clamp(currentPos.x, environmentMinX, environmentMaxX);
                transform.position = new Vector3(clampedX, currentPos.y, currentPos.z);

                if (EnableDebugLogging) Debug.LogWarning($"[BOUNDARY] {gameObject.name} clamped X: {currentPos.x:F2} -> {clampedX:F2} (bounds: {environmentMinX:F2} to {environmentMaxX:F2})");
            }
        }

        public Vector3 GetCurrentVelocity()
        {
            // Root motion routes velocity through the animator, not the rigidbody
            if (animator != null && animator.applyRootMotion)
            {
                return animator.velocity;
            }
            return rb != null ? rb.linearVelocity : Vector3.zero;
        }
        
        public bool IsStopped()
        {
            bool stoppedByCollision = (visionCone != null && visionCone.STOP);
            return !shouldMove || targetSpeed <= 0 || stoppedByCollision;
        }
        
        public bool IsIdle()
        {
            return !shouldMove;
        }

        #region Reaction Animation System

        /// <summary>True while a reaction animation is playing.</summary>
        public bool IsPlayingReaction()
        {
            return isPlayingReaction;
        }

        /// <summary>Play a reaction animation (random by default), then invoke <paramref name="onComplete"/>. Pass <paramref name="specificReactionIndex"/> &gt;= 0 to force a specific clip.</summary>
        public void PlayReactionAnimation(Action onComplete, int specificReactionIndex = -1)
        {
            if (animator == null)
            {
                Debug.LogWarning($"[AgentBehavior] {gameObject.name} has no Animator - skipping reaction animation");
                onComplete?.Invoke();
                return;
            }

            if (isPlayingReaction)
            {
                Debug.LogWarning($"[AgentBehavior] {gameObject.name} already playing reaction - ignoring new request");
                return;
            }

            onReactionComplete = onComplete;
            isPlayingReaction = true;

            int reactionIndex = specificReactionIndex >= 0 ? specificReactionIndex : UnityEngine.Random.Range(0, reactionClipCount);

            if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} starting REACTION animation (index: {reactionIndex})");

            animator.SetBool("IsWalking", false);
            animator.SetInteger("ReactionIndex", reactionIndex);
            animator.SetTrigger("IsReacting"); // Trigger auto-resets after firing

            animator.speed = 1.0f;

            if (reactionCoroutine != null)
            {
                StopCoroutine(reactionCoroutine);
            }
            reactionCoroutine = StartCoroutine(WaitForReactionComplete());
        }

        private IEnumerator WaitForReactionComplete()
        {
            // Animator needs at least one frame to start the transition
            yield return null;
            yield return null;

            float timeout = 5.0f;
            float elapsed = 0f;
            bool enteredReactionState = false;

            while (elapsed < timeout)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

                if (stateInfo.IsName("Reaction_0") || stateInfo.IsName("Reaction_1") || stateInfo.IsName("Reaction_2") ||
                    stateInfo.IsTag("Reaction"))
                {
                    enteredReactionState = true;
                    if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} entered reaction state, waiting for completion...");
                    break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!enteredReactionState)
            {
                // State not detected; fall back to a fixed wait so the trial still resets
                Debug.LogWarning($"[AgentBehavior] {gameObject.name} couldn't detect reaction state - using fallback timing");
                yield return new WaitForSeconds(2.0f);
            }
            else
            {
                elapsed = 0f;
                while (elapsed < timeout)
                {
                    AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

                    bool stillInReaction = stateInfo.IsName("Reaction_0") || stateInfo.IsName("Reaction_1") ||
                                           stateInfo.IsName("Reaction_2") || stateInfo.IsTag("Reaction");

                    if (!stillInReaction || stateInfo.normalizedTime >= 0.95f)
                    {
                        if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} reaction animation completed (normalizedTime: {stateInfo.normalizedTime:F2})");
                        break;
                    }

                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            FinishReaction();
        }

        private void FinishReaction()
        {
            if (!isPlayingReaction) return;

            if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} REACTION COMPLETE - invoking callback");

            isPlayingReaction = false;

            if (animator != null)
            {
                if (restrictToWalkingState)
                {
                    animator.SetBool("IsWalking", true);
                }

                if (hasInitializedAnimationSpeed && clipInherentSpeed > 0.1f)
                {
                    float normalMultiplier = Mathf.Clamp(targetSpeed / clipInherentSpeed, 0.5f, 2.0f);
                    animator.speed = normalMultiplier;
                }
            }

            // Callback typically triggers the trial reset
            Action callback = onReactionComplete;
            onReactionComplete = null;
            callback?.Invoke();
        }

        /// <summary>Force-cancel any playing reaction animation. Does NOT invoke the completion callback.</summary>
        public void CancelReaction()
        {
            if (reactionCoroutine != null)
            {
                StopCoroutine(reactionCoroutine);
                reactionCoroutine = null;
            }

            if (isPlayingReaction)
            {
                if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} reaction CANCELLED");
                isPlayingReaction = false;

                onReactionComplete = null;
            }
        }

        #endregion

        #region Pause/Resume (for collision events)

        /// <summary>True while the agent is paused.</summary>
        public bool IsPaused()
        {
            return isPaused;
        }

        /// <summary>Pause the agent: freeze movement and animation. Used to freeze the scene during another agent's collision response.</summary>
        public void PauseAgent()
        {
            if (isPaused) return;

            isPaused = true;

            if (animator != null)
            {
                pausedAnimatorSpeed = animator.speed;
                animator.speed = 0f;
            }

            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} PAUSED");
        }

        /// <summary>Resume the agent: restore animator speed.</summary>
        public void ResumeAgent()
        {
            if (!isPaused) return;

            isPaused = false;

            if (animator != null)
            {
                animator.speed = pausedAnimatorSpeed;
            }

            if (EnableDebugLogging) Debug.Log($"[AgentBehavior] {gameObject.name} RESUMED");
        }

        /// <summary>Pause every active agent (uses the static registry). Pass <paramref name="excludeAgent"/> to leave one agent active (e.g., the one playing the reaction animation).</summary>
        public static void PauseAllAgents(AgentBehavior excludeAgent = null)
        {
            int count = 0;
            foreach (var agent in allAgents)
            {
                if (agent != null && agent.gameObject.activeInHierarchy && agent != excludeAgent)
                {
                    agent.PauseAgent();
                    count++;
                }
            }
            if (EnableDebugLogging) Debug.Log($"[AgentBehavior] PAUSED {count} agents" + (excludeAgent != null ? $" (excluded: {excludeAgent.gameObject.name})" : ""));
        }

        /// <summary>Resume every active agent (uses the static registry).</summary>
        public static void ResumeAllAgents()
        {
            int count = 0;
            foreach (var agent in allAgents)
            {
                if (agent != null && agent.gameObject.activeInHierarchy)
                {
                    agent.ResumeAgent();
                    count++;
                }
            }
            if (EnableDebugLogging) Debug.Log($"[AgentBehavior] RESUMED {count} agents");
        }

        #endregion

        #region Static Agent Registry

        /// <summary>All currently active agents. Prefer this over <c>FindObjectsByType&lt;AgentBehavior&gt;()</c>.</summary>
        public static System.Collections.Generic.IReadOnlyCollection<AgentBehavior> GetAllAgents()
        {
            return allAgents;
        }

        /// <summary>Active agent count.</summary>
        public static int ActiveAgentCount => allAgents.Count;

        /// <summary>Clear the registry (call when resetting scene/experiment).</summary>
        public static void ClearRegistry()
        {
            allAgents.Clear();
        }

        #endregion
    }
}
