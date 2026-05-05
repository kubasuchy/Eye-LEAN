// SPDX-License-Identifier: MIT
using UnityEngine;
using System.Collections;
using System;

/// <summary>
/// Detects when the participant is standing on the trial start platform and
/// signals TrialManager to begin the next trial.
/// </summary>

namespace EyeLean.Skeleton
{
    public class StartingPlatform : MonoBehaviour
    {
        [Header("Platform Configuration")]
        [Tooltip("Radius of the platform detection area")]
        [SerializeField] private float platformRadius = 0.6f;
        
        [Tooltip("Height of the platform bubble")]
        [SerializeField] private float platformHeight = 0.1f;
        
        [Tooltip("Material for the platform visual")]
        [SerializeField] private Material platformMaterial;
        
        [Header("Detection Settings")]
        [Tooltip("Tag to identify the participant (should be on XR Rig)")]
        [SerializeField] private string participantTag = "Player";
        
        [Tooltip("Minimum time participant must stay on platform before detection")]
        [SerializeField] private float minimumStayTime = 0.5f;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;
        
        private bool participantOnPlatform = false;
        private float timeOnPlatform = 0f;
        private GameObject participantObject;
        private TrialManager trialManager;
        private bool experimentCompleted = false;

        // After the first trial, force participant to EXIT and RE-ENTER before activating.
        // Prevents an immediate re-trigger when ITI starts while they're still standing on it.
        private bool requireReEntry = false;

        // First trial bypasses re-entry: participant starts on the platform.
        private bool isFirstTrial = true;

        // Platform position + forward direction are captured once and held fixed for the whole experiment.
        private Vector3 initialPlatformPosition;
        private Vector3 initialForwardDirection;
        private bool platformPositionSet = false;
        private bool initialDirectionCaptured = false;

        [Header("Direction Detection")]
        [Tooltip("Angle tolerance for direction check (degrees from initial facing direction)")]
        [SerializeField] private float directionTolerance = 45f;
        
        // Platform visual components
        private GameObject platformVisual;
        private Collider platformCollider;
        
        void Start()
        {
            FindTrialManager();
            EnsureXRRigHasCollider();

            if (trialManager != null)
            {
                trialManager.OnPhaseChanged += OnTrialPhaseChanged;
                trialManager.OnTrialCompleted += OnTrialCompleted;
                trialManager.OnAllTrialsCompleted += OnExperimentCompleted;
            }

            // Must position platform at participant before creating visuals
            StartCoroutine(PositionThenInitialize());
        }
        
        void Update()
        {
            UpdatePlatformDetection();
            DebugColliderSetup();
        }
        
        /// <summary>
        /// Anchor the platform at the participant's initial location and forward direction.
        /// Position is captured once and held fixed for the entire experiment.
        /// </summary>
        void PositionPlatformAtParticipant()
        {
            if (platformPositionSet)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = FindFirstObjectByType<Camera>();
            }

            if (mainCamera == null)
            {
                UnityEngine.Debug.LogError("[StartingPlatform] Cannot position platform - no camera found!");
                return;
            }

            Vector3 participantPos = mainCamera.transform.position;
            initialPlatformPosition = new Vector3(participantPos.x, 0f, participantPos.z);
            transform.position = initialPlatformPosition;
            platformPositionSet = true;

            // Project forward to XZ plane so pitch can't influence the captured heading
            Vector3 cameraForward = mainCamera.transform.forward;
            initialForwardDirection = new Vector3(cameraForward.x, 0f, cameraForward.z).normalized;

            if (initialForwardDirection.sqrMagnitude > 0.01f)
            {
                initialDirectionCaptured = true;
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] FIXED DIRECTION SET: Must face {initialForwardDirection} (within {directionTolerance} deg)");
            }
            else
            {
                // Camera pointing straight up/down: fall back to +Z
                initialForwardDirection = Vector3.forward;
                initialDirectionCaptured = true;
                if (showDebugLogs)
                    UnityEngine.Debug.Log("[StartingPlatform] Camera forward was invalid, using default +Z direction");
            }

            if (showDebugLogs)
            {
                UnityEngine.Debug.Log($"[StartingPlatform] FIXED POSITION SET: Platform anchored at {initialPlatformPosition}");
                UnityEngine.Debug.Log($"[StartingPlatform] Position and direction will remain constant for ALL trials");
            }
        }
        
        /// <summary>
        /// Wait for VR tracking to stabilize, position the platform, then build visuals.
        /// Re-applies visibility for the current phase to handle the case where
        /// OnTrialPhaseChanged(ITI) fired before the platformVisual existed.
        /// </summary>
        IEnumerator PositionThenInitialize()
        {
            if (showDebugLogs)
                UnityEngine.Debug.Log("[StartingPlatform] Waiting 0.3 seconds for VR system to initialize before positioning platform...");

            yield return new WaitForSeconds(0.3f);

            PositionPlatformAtParticipant();

            InitializePlatform();

            if (trialManager != null)
            {
                var currentPhase = trialManager.GetCurrentPhase();
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] Post-init phase check: current phase is {currentPhase}");

                if (currentPhase == TrialManager.TrialPhase.InterTrialInterval)
                {
                    SetPlatformVisible(true);
                    SetPlatformColliderEnabled(true);
                    if (showDebugLogs)
                        UnityEngine.Debug.Log("[StartingPlatform] Platform visible for ITI phase");
                }
                else
                {
                    SetPlatformVisible(false);
                    if (showDebugLogs)
                        UnityEngine.Debug.Log("[StartingPlatform] Platform hidden (not in ITI phase)");
                }
            }
            else
            {
                SetPlatformVisible(false);
            }

            if (showDebugLogs)
                UnityEngine.Debug.Log("[StartingPlatform] Platform positioned and initialized at participant location");
        }

        IEnumerator DelayedPlatformPositioning()
        {
            if (showDebugLogs)
                UnityEngine.Debug.Log("[StartingPlatform] Waiting 0.5 seconds for VR system to initialize before positioning platform...");

            yield return new WaitForSeconds(0.5f);

            PositionPlatformAtParticipant();

            SetPlatformVisible(true);

            if (showDebugLogs)
                UnityEngine.Debug.Log("[StartingPlatform] Delayed positioning completed and platform is now visible");
        }

        /// <summary>
        /// Drive platform visibility from trial phase. Visual is shown only in ITI; the
        /// collider stays active during WaitingOnPlatform/FixationCross to detect
        /// the participant stepping off.
        /// </summary>
        void OnTrialPhaseChanged(TrialManager.TrialPhase newPhase)
        {
            switch (newPhase)
            {
                case TrialManager.TrialPhase.InterTrialInterval:
                    participantOnPlatform = false;
                    timeOnPlatform = 0f;
                    participantObject = null;

                    if (isFirstTrial)
                    {
                        requireReEntry = false;
                        if (showDebugLogs)
                            UnityEngine.Debug.Log("[StartingPlatform] Platform state RESET - FIRST TRIAL, no re-entry required");
                    }
                    else
                    {
                        requireReEntry = true;
                        if (showDebugLogs)
                            UnityEngine.Debug.Log("[StartingPlatform] Platform state RESET - requireReEntry=TRUE, participant must EXIT and RE-ENTER platform for next trial");
                    }

                    SetPlatformVisible(true);
                    SetPlatformColliderEnabled(true);
                    break;
                case TrialManager.TrialPhase.WaitingOnPlatform:
                case TrialManager.TrialPhase.FixationCross:
                    // Hide visual but keep collider live for exit detection
                    SetPlatformVisible(false);
                    SetPlatformColliderEnabled(true);
                    break;
                case TrialManager.TrialPhase.ExperimentalPhase:
                    SetPlatformVisible(false);
                    SetPlatformColliderEnabled(false);
                    break;
            }
        }

        void OnTrialCompleted(TrialManager.TrialData completedTrial)
        {
            if (experimentCompleted) return;

            SetPlatformVisible(true);
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[StartingPlatform] Trial {completedTrial.trialNumber} completed - platform visible at fixed position {transform.position}");
        }

        void OnExperimentCompleted()
        {
            experimentCompleted = true;

            SetPlatformVisible(false);
            SetPlatformColliderEnabled(false);

            participantOnPlatform = false;
            timeOnPlatform = 0f;
            participantObject = null;

            if (showDebugLogs)
                UnityEngine.Debug.Log("[StartingPlatform] Experiment completed - platform fully deactivated");
        }
        
        void SetPlatformVisible(bool visible)
        {
            if (platformVisual != null)
            {
                platformVisual.SetActive(visible);
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] Platform visual set to: {visible}");
            }
        }

        void SetPlatformColliderEnabled(bool enabled)
        {
            if (platformCollider != null)
            {
                platformCollider.enabled = enabled;
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] Platform collider enabled: {enabled}");
            }
        }
        
        void DebugColliderSetup()
        {
            if (!showDebugLogs) return;

            if (Time.time % 2f < Time.deltaTime)
            {
                if (platformCollider == null)
                {
                    UnityEngine.Debug.LogError("[StartingPlatform] Platform collider is NULL!");
                    return;
                }

                UnityEngine.Debug.Log($"[StartingPlatform] Collider status - IsTrigger: {platformCollider.isTrigger}, Enabled: {platformCollider.enabled}");

                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    UnityEngine.Debug.Log($"[StartingPlatform] Main Camera position: {mainCamera.transform.position}");

                    float fullDistance = Vector3.Distance(transform.position, mainCamera.transform.position);
                    Vector3 platformPos = new Vector3(transform.position.x, 0, transform.position.z);
                    Vector3 cameraPos = new Vector3(mainCamera.transform.position.x, 0, mainCamera.transform.position.z);
                    float horizontalDistance = Vector3.Distance(platformPos, cameraPos);

                    UnityEngine.Debug.Log($"[StartingPlatform] Distance - Full: {fullDistance:F2}m, Horizontal: {horizontalDistance:F2}m (Platform radius: {platformRadius:F2}m)");
                }

                Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, platformRadius * 2f);
                UnityEngine.Debug.Log($"[StartingPlatform] Found {nearbyColliders.Length} nearby colliders:");

                for (int i = 0; i < nearbyColliders.Length; i++)
                {
                    try
                    {
                        Collider col = nearbyColliders[i];
                        if (col == null)
                        {
                            UnityEngine.Debug.Log($"  - Collider {i} is NULL!");
                            continue;
                        }

                        string colName = col.name ?? "NULL_NAME";
                        string colTag = "UNTAGGED";
                        try { colTag = col.tag ?? "NULL_TAG"; } catch { colTag = "TAG_ERROR"; }

                        if (col != platformCollider)
                        {
                            float distance = Vector3.Distance(transform.position, col.transform.position);
                            string colliderType = col.GetType().Name;
                            UnityEngine.Debug.Log($"  - Collider {i}: '{colName}' Tag:'{colTag}' Type:{colliderType} Distance:{distance:F1}m Trigger:{col.isTrigger}");

                            if (colTag == "Player" || colName.ToLower().Contains("xr"))
                            {
                                UnityEngine.Debug.Log($"    ^ This appears to be the XR Rig collider!");
                            }
                        }
                        else
                        {
                            UnityEngine.Debug.Log($"  - Collider {i}: [SELF] '{colName}' (our platform collider)");
                        }
                    }
                    catch (System.Exception e)
                    {
                        UnityEngine.Debug.LogError($"  - Error processing collider {i}: {e.Message}");
                    }
                }
            }
        }
        
        void InitializePlatform()
        {
            CreatePlatformVisual();

            CreatePlatformCollider();
            
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[StartingPlatform] Platform visual components initialized at position {transform.position}");
        }

        void CreatePlatformVisual()
        {
            platformVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            platformVisual.name = "PlatformVisual";
            platformVisual.transform.SetParent(transform);
            platformVisual.transform.localPosition = Vector3.zero;

            platformVisual.transform.localScale = new Vector3(platformRadius * 2, platformHeight, platformRadius * 2);

            // Sink the sphere so only the top half (a "bubble") is above ground
            platformVisual.transform.localPosition = new Vector3(0, -platformHeight * 0.5f, 0);

            // The sphere's auto-collider would conflict with the trigger collider added below
            Destroy(platformVisual.GetComponent<Collider>());

            if (platformMaterial != null)
            {
                platformVisual.GetComponent<Renderer>().material = platformMaterial;
            }
            else
            {
                Material defaultMat = VRMaterialProvider.GetMaterial(new Color(0.2f, 0.8f, 0.2f));
                platformVisual.GetComponent<Renderer>().material = defaultMat;
            }

            if (showDebugLogs)
                UnityEngine.Debug.Log("[StartingPlatform] Platform visual created");
        }
        
        void CreatePlatformCollider()
        {
            SphereCollider sphereCollider = gameObject.AddComponent<SphereCollider>();
            sphereCollider.isTrigger = true;
            sphereCollider.radius = platformRadius;

            // Raise to standing height so the HMD (head) crosses the trigger volume
            sphereCollider.center = new Vector3(0, 1.6f, 0);

            platformCollider = sphereCollider;

            if (showDebugLogs)
                UnityEngine.Debug.Log($"[StartingPlatform] Platform collider created at height {sphereCollider.center.y}m");
        }
        
        void FindTrialManager()
        {
            trialManager = FindFirstObjectByType<TrialManager>();
            if (trialManager == null)
            {
                UnityEngine.Debug.LogWarning("[StartingPlatform] TrialManager not found in scene!");
            }
            else
            {
                if (showDebugLogs)
                    UnityEngine.Debug.Log("[StartingPlatform] Connected to TrialManager");
            }
        }
        
        /// <summary>
        /// Attach a full-body capsule collider to the HMD camera so the participant can
        /// interact with platform/obstacle triggers. Re-creates any existing collider to
        /// guarantee the floor-to-head dimensions.
        /// </summary>
        void EnsureXRRigHasCollider()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = FindFirstObjectByType<Camera>();
            }

            GameObject trackingTarget = null;

            if (mainCamera != null)
            {
                trackingTarget = mainCamera.gameObject;
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] Using Main Camera '{trackingTarget.name}' for collision detection");
            }
            else
            {
                GameObject[] xrRigCandidates = {
                    GameObject.Find("XR Rig"),
                    GameObject.Find("XRRig"),
                    GameObject.Find("XR Origin")
                };

                foreach (var candidate in xrRigCandidates)
                {
                    if (candidate != null)
                    {
                        trackingTarget = candidate;
                        break;
                    }
                }
            }

            if (trackingTarget == null)
            {
                UnityEngine.Debug.LogWarning("[StartingPlatform] No tracking target found! Cannot ensure collider setup.");
                return;
            }

            // Replace any existing collider to guarantee the dimensions match
            Collider existingCollider = trackingTarget.GetComponent<Collider>();
            if (existingCollider != null)
            {
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] Removing existing {existingCollider.GetType().Name} to reconfigure as full-body collider");
                Destroy(existingCollider);
            }

            CapsuleCollider capsule = trackingTarget.AddComponent<CapsuleCollider>();
            capsule.height = 1.7f;
            capsule.radius = 0.3f;
            capsule.direction = 1;
            // Camera is at "head" so center.y = -0.85 places the capsule bottom at floor
            capsule.center = new Vector3(0, -0.85f, 0);

            if (!trackingTarget.CompareTag("Player"))
            {
                try
                {
                    trackingTarget.tag = "Player";
                    if (showDebugLogs)
                        UnityEngine.Debug.Log($"[StartingPlatform] Tagged {trackingTarget.name} as 'Player'");
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[StartingPlatform] Could not set Player tag: {e.Message}");
                }
            }

            if (showDebugLogs)
            {
                float worldTop = trackingTarget.transform.position.y + capsule.center.y + capsule.height / 2;
                float worldBottom = trackingTarget.transform.position.y + capsule.center.y - capsule.height / 2;
                UnityEngine.Debug.Log($"[StartingPlatform] Full-body CapsuleCollider on {trackingTarget.name}");
                UnityEngine.Debug.Log($"[StartingPlatform] Capsule: Height={capsule.height}m, Radius={capsule.radius}m, Center={capsule.center}");
                UnityEngine.Debug.Log($"[StartingPlatform] World coverage: {worldBottom:F2}m (floor) to {worldTop:F2}m (head)");
            }

            EnsureRigidbody(trackingTarget);
        }

        void EnsureRigidbody(GameObject xrRig)
        {
            Rigidbody rb = xrRig.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = xrRig.AddComponent<Rigidbody>();
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] Added kinematic Rigidbody to {xrRig.name} for trigger detection");
            }
            else
            {
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] {xrRig.name} already has Rigidbody");
            }

            // Kinematic + no gravity so physics never moves the HMD camera; continuous
            // collision keeps fast head movements from tunneling out of trigger volumes
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[StartingPlatform] Configured {xrRig.name} Rigidbody: kinematic=true, interpolate=true, continuous collision");
        }
        
        void UpdatePlatformDetection()
        {
            if (participantOnPlatform)
            {
                if (requireReEntry)
                {
                    if (showDebugLogs && Time.frameCount % 120 == 0)
                    {
                        UnityEngine.Debug.Log("[StartingPlatform] Waiting for participant to EXIT and RE-ENTER platform (requireReEntry=true)");
                    }
                    return;
                }

                bool facingCorrect = IsParticipantFacingCorrectDirection();

                if (facingCorrect)
                {
                    timeOnPlatform += Time.deltaTime;

                    if (timeOnPlatform >= minimumStayTime && trialManager != null)
                    {
                        var currentPhase = trialManager.GetCurrentPhase();
                        if (currentPhase == TrialManager.TrialPhase.InterTrialInterval)
                        {
                            ActivatePlatform();
                        }
                    }
                }
                else
                {
                    if (timeOnPlatform > 0)
                    {
                        if (showDebugLogs)
                            UnityEngine.Debug.Log("[StartingPlatform] Direction check failed - timer reset (must face starting direction)");
                        timeOnPlatform = 0f;
                    }
                }
            }
        }

        /// <summary>Fail-open direction check: returns true if no direction has been captured yet, so initial startup never blocks activation.</summary>
        bool IsParticipantFacingCorrectDirection()
        {
            if (!initialDirectionCaptured)
            {
                return true;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return true;
            }

            Vector3 currentForward = mainCamera.transform.forward;
            Vector3 currentForwardXZ = new Vector3(currentForward.x, 0f, currentForward.z).normalized;

            if (currentForwardXZ.sqrMagnitude < 0.01f)
            {
                // Looking straight up/down: don't accept this as facing forward
                return false;
            }

            float angle = Vector3.Angle(currentForwardXZ, initialForwardDirection);

            return angle <= directionTolerance;
        }

        void ActivatePlatform()
        {
            if (experimentCompleted)
            {
                if (showDebugLogs)
                    UnityEngine.Debug.Log("[StartingPlatform] ActivatePlatform blocked - experiment is completed");
                return;
            }

            if (showDebugLogs)
                UnityEngine.Debug.Log("[StartingPlatform] ActivatePlatform() method entered");

            if (trialManager != null)
            {
                var currentPhase = trialManager.GetCurrentPhase();
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] ActivatePlatform called - current phase: {currentPhase}");

                if (currentPhase == TrialManager.TrialPhase.InterTrialInterval)
                {
                    if (showDebugLogs)
                        UnityEngine.Debug.Log("[StartingPlatform] Activated - transitioning to WaitingOnPlatform phase");

                    if (isFirstTrial)
                    {
                        isFirstTrial = false;
                        if (showDebugLogs)
                            UnityEngine.Debug.Log("[StartingPlatform] First trial activated - subsequent trials will require re-entry");
                    }

                    trialManager.SetPhase(TrialManager.TrialPhase.WaitingOnPlatform);

                    if (showDebugLogs)
                        UnityEngine.Debug.Log("[StartingPlatform] Activated - participant ready for trial");

                    trialManager.OnPlatformActivated();
                }
                else
                {
                    if (showDebugLogs)
                        UnityEngine.Debug.LogWarning($"[StartingPlatform] ActivatePlatform called but current phase is {currentPhase}, not InterTrialInterval - IGNORING");
                }
            }
            else
            {
                UnityEngine.Debug.LogError("[StartingPlatform] Cannot activate platform - TrialManager is NULL!");
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (showDebugLogs)
                UnityEngine.Debug.Log($"[StartingPlatform] TRIGGER ENTERED by: '{other.name}' (Tag: '{other.tag}') Type: {other.GetType().Name}");

            bool isParticipant = other.CompareTag(participantTag) || IsParticipantObject(other.gameObject);

            if (isParticipant)
            {
                participantOnPlatform = true;
                timeOnPlatform = 0f;
                participantObject = other.gameObject;

                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] Participant DETECTED on platform: {other.name}");
            }
            else
            {
                if (showDebugLogs)
                    UnityEngine.Debug.Log($"[StartingPlatform] Object '{other.name}' with tag '{other.tag}' is NOT recognized as participant");
            }
        }
        
        void OnTriggerExit(Collider other)
        {
            if (showDebugLogs)
            {
                UnityEngine.Debug.Log($"[StartingPlatform] TRIGGER EXITED by: '{other.name}' (Tag: '{other.tag}') Type: {other.GetType().Name}");
                UnityEngine.Debug.Log($"[StartingPlatform] Exit position - Other: {other.transform.position}, Platform: {transform.position}");
            }

            if (other.CompareTag(participantTag) || IsParticipantObject(other.gameObject))
            {
                participantOnPlatform = false;
                timeOnPlatform = 0f;
                participantObject = null;

                // Exit clears the re-entry requirement so the next enter counts as valid
                if (requireReEntry)
                {
                    requireReEntry = false;
                    if (showDebugLogs)
                        UnityEngine.Debug.Log("[StartingPlatform] Participant EXITED platform - requireReEntry cleared, re-entry will be valid");
                }

                if (showDebugLogs)
                {
                    UnityEngine.Debug.Log($"[StartingPlatform] Participant exited platform: {other.name}");
                }

                if (trialManager != null)
                {
                    var currentPhase = trialManager.GetCurrentPhase();

                    if (currentPhase == TrialManager.TrialPhase.WaitingOnPlatform)
                    {
                        trialManager.SetPhase(TrialManager.TrialPhase.InterTrialInterval);
                        if (showDebugLogs)
                            UnityEngine.Debug.Log("[StartingPlatform] Reset to InterTrialInterval - participant left before activation");
                    }
                    else if (currentPhase == TrialManager.TrialPhase.FixationCross)
                    {
                        trialManager.SetPhase(TrialManager.TrialPhase.InterTrialInterval);
                        if (showDebugLogs)
                            UnityEngine.Debug.Log("[StartingPlatform] Reset to InterTrialInterval - participant left platform during fixation cross");
                    }
                    else if (currentPhase == TrialManager.TrialPhase.ExperimentalPhase)
                    {
                        // Expected: participant walks off the platform during the trial
                        if (showDebugLogs)
                            UnityEngine.Debug.Log("[StartingPlatform] Participant left platform during experimental phase (normal)");
                    }
                }
            }
        }

        /// <summary>True if the GameObject is part of the XR rig hierarchy.</summary>
        bool IsParticipantObject(GameObject obj)
        {
            Transform current = obj.transform;
            while (current != null)
            {
                if (current.CompareTag(participantTag))
                {
                    if (showDebugLogs)
                        UnityEngine.Debug.Log($"[StartingPlatform] Found participant tag on: {current.name}");
                    return true;
                }
                current = current.parent;
            }

            string objName = obj.name.ToLower();
            bool isXRRelated = objName.Contains("xr") || objName.Contains("rig") ||
                              objName.Contains("hmd") || objName.Contains("head") ||
                              objName.Contains("camera") || objName.Contains("main camera");

            if (isXRRelated && showDebugLogs)
            {
                UnityEngine.Debug.Log($"[StartingPlatform] Detected XR-related object: {obj.name}");
            }

            return isXRRelated;
        }
        
        [ContextMenu("Test Platform Activation")]
        public void TestPlatformActivation()
        {
            UnityEngine.Debug.Log("[StartingPlatform] MANUAL TEST - Simulating platform activation");
            participantOnPlatform = true;
            timeOnPlatform = minimumStayTime + 0.1f;
            participantObject = Camera.main?.gameObject;
            ActivatePlatform();
        }

        [ContextMenu("Reset Platform State")]
        public void ResetPlatformState()
        {
            participantOnPlatform = false;
            timeOnPlatform = 0f;
            participantObject = null;
            UnityEngine.Debug.Log("[StartingPlatform] Platform state manually reset");

            if (trialManager != null)
            {
                trialManager.SetPhase(TrialManager.TrialPhase.InterTrialInterval);
                UnityEngine.Debug.Log("[StartingPlatform] Reset to InterTrialInterval phase");
            }
        }

        [ContextMenu("Remove Old XR Rig Collider")]
        public void RemoveOldXRRigCollider()
        {
            GameObject[] xrRigCandidates = {
                GameObject.Find("XR Rig"),
                GameObject.Find("XRRig"),
                GameObject.Find("XR Origin")
            };

            foreach (var candidate in xrRigCandidates)
            {
                if (candidate != null)
                {
                    Collider oldCollider = candidate.GetComponent<Collider>();
                    if (oldCollider != null)
                    {
                        UnityEngine.Debug.Log($"[StartingPlatform] Removing old collider from {candidate.name}");
                        DestroyImmediate(oldCollider);
                    }

                    Rigidbody oldRb = candidate.GetComponent<Rigidbody>();
                    if (oldRb != null)
                    {
                        UnityEngine.Debug.Log($"[StartingPlatform] Removing old Rigidbody from {candidate.name}");
                        DestroyImmediate(oldRb);
                    }
                }
            }

            UnityEngine.Debug.Log("[StartingPlatform] Old XR Rig colliders cleaned up");
        }
        
        [ContextMenu("Fix Tracking Collider Position")]
        public void FixTrackingColliderPosition()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = FindFirstObjectByType<Camera>();
            }

            GameObject trackingTarget = mainCamera?.gameObject;

            if (trackingTarget == null)
            {
                UnityEngine.Debug.LogError("[StartingPlatform] No tracking target found for fixing collider!");
                return;
            }

            CapsuleCollider capsule = trackingTarget.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.height = 1.7f;
                capsule.radius = 0.25f;
                capsule.center = new Vector3(0, -0.85f, 0);

                Vector3 worldCenter = trackingTarget.transform.TransformPoint(capsule.center);
                float worldBottom = trackingTarget.transform.position.y + capsule.center.y - capsule.height / 2;
                float worldTop = trackingTarget.transform.position.y + capsule.center.y + capsule.height / 2;
                UnityEngine.Debug.Log($"[StartingPlatform] Full-body collider configured. Height: {capsule.height}m, Center: {capsule.center}");
                UnityEngine.Debug.Log($"[StartingPlatform] World coverage: bottom={worldBottom:F2}m, top={worldTop:F2}m (camera at {trackingTarget.transform.position.y:F2}m)");
            }
            else
            {
                UnityEngine.Debug.LogError("[StartingPlatform] No CapsuleCollider found on tracking target!");
            }
        }
        
        #region Public API

        /// <summary>True if the participant is currently on the platform.</summary>
        public bool IsParticipantOnPlatform()
        {
            return participantOnPlatform;
        }

        /// <summary>Time (seconds) the participant has been on the platform.</summary>
        public float GetTimeOnPlatform()
        {
            return timeOnPlatform;
        }

        /// <summary>World-space platform position.</summary>
        public Vector3 GetPlatformPosition()
        {
            return transform.position;
        }

        /// <summary>Forward direction the participant must face to start a trial. Falls back to <c>Vector3.forward</c> until captured.</summary>
        public Vector3 GetInitialForwardDirection()
        {
            return initialDirectionCaptured ? initialForwardDirection : Vector3.forward;
        }

        /// <summary>True if the participant is currently facing the captured forward direction within tolerance.</summary>
        public bool IsFacingCorrectDirection()
        {
            return IsParticipantFacingCorrectDirection();
        }

        /// <summary>Reset detection state (testing hook).</summary>
        public void ResetPlatform()
        {
            participantOnPlatform = false;
            timeOnPlatform = 0f;
            participantObject = null;

            if (showDebugLogs)
                UnityEngine.Debug.Log("[StartingPlatform] Platform state reset");
        }
        
        #endregion
        
        #region Debug Visualization
        
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * platformHeight * 0.5f, platformRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 1.6f, platformRadius);

            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.6f);

            if (initialDirectionCaptured && initialForwardDirection.sqrMagnitude > 0.01f)
            {
                Vector3 arrowStart = transform.position + Vector3.up * 0.5f;
                Vector3 arrowEnd = arrowStart + initialForwardDirection * 2f;

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(arrowStart, arrowEnd);

                Vector3 right = Vector3.Cross(Vector3.up, initialForwardDirection).normalized;
                Gizmos.DrawLine(arrowEnd, arrowEnd - initialForwardDirection * 0.3f + right * 0.2f);
                Gizmos.DrawLine(arrowEnd, arrowEnd - initialForwardDirection * 0.3f - right * 0.2f);
            }
        }

        void OnGUI()
        {
            if (!showDebugLogs || !Application.isPlaying) return;

            bool facingCorrect = IsParticipantFacingCorrectDirection();

            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(320, 10, 280, 200));
            GUILayout.Label("<b>STARTING PLATFORM DEBUG</b>");
            GUILayout.Label($"On Platform: {participantOnPlatform}");
            GUILayout.Label($"Require Re-Entry: {requireReEntry}");
            GUILayout.Label($"First Trial: {isFirstTrial}");
            GUILayout.Label($"Facing Correct: {facingCorrect} (tol: {directionTolerance}°)");
            GUILayout.Label($"Time on Platform: {timeOnPlatform:F1}s / {minimumStayTime:F1}s");

            if (participantObject != null)
            {
                GUILayout.Label($"Participant: {participantObject.name}");
            }

            if (trialManager != null)
            {
                GUILayout.Label($"Trial Phase: {trialManager.GetCurrentPhase()}");
            }

            bool ready = participantOnPlatform && facingCorrect && !requireReEntry && timeOnPlatform >= minimumStayTime;
            GUILayout.Label($"Ready to Activate: {ready}");

            GUILayout.EndArea();
        }
        
        #endregion
        
        void OnDestroy()
        {
            if (trialManager != null)
            {
                trialManager.OnPhaseChanged -= OnTrialPhaseChanged;
                trialManager.OnTrialCompleted -= OnTrialCompleted;
                trialManager.OnAllTrialsCompleted -= OnExperimentCompleted;
            }
        }
    }
}
