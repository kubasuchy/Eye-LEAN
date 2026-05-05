// SPDX-License-Identifier: MIT
using UnityEngine;
using System.Collections;
using EyeTracking.Components;
using EyeTracking.Core;

/// <summary>
/// Audio-driven fixation cross: shows when the participant looks straight ahead and
/// advances the trial when the countdown clip finishes.
/// </summary>

namespace EyeLean.Skeleton
{
    public class FixationCross : MonoBehaviour
    {
        [Header("Audio Configuration")]
        [Tooltip("Audio clip for countdown - fixation duration will match this clip's length")]
        public AudioClip countdownClip;

        [Tooltip("Audio clip played when gaze breaks or participant steps off platform")]
        public AudioClip gazeBreakClip;

        [Header("Visual Configuration")]
        [Tooltip("Distance in front of participant to place fixation cross (meters)")]
        [Range(0.4f, 2.0f)]
        public float crossDistance = 0.8f;

        [Tooltip("Height offset above eye level for fixation cross (meters)")]
        [Range(-0.5f, 0.5f)]
        public float crossHeightOffset = 0.1f;

        [SerializeField]
        private GameObject fixationCross;

        public Camera subjectEye;

        private bool isActive = false, countingDown;

        private AudioSource audioSource;

        private TrialManager trialManager;

        private EyeTracker eyeTracker;

        private EnvironmentManager environmentManager;

        private StartingPlatform startingPlatform;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        public System.Action OnFixationCompleted;

        void Start()
        {
            subjectEye = Camera.main ?? FindFirstObjectByType<Camera>();
            trialManager = FindFirstObjectByType<TrialManager>();
            environmentManager = FindFirstObjectByType<EnvironmentManager>();
            startingPlatform = FindFirstObjectByType<StartingPlatform>();
            eyeTracker = FindFirstObjectByType<EyeTracker>();

            if (eyeTracker == null)
            {
                Debug.LogWarning("[FixationCross] EyeTracker not found - falling back to head direction for gaze detection");
            }
            else
            {
                if (showDebugLogs) Debug.Log("[FixationCross] Connected to EyeTracker for gaze-based fixation detection");
            }

            if (trialManager != null)
            {
                trialManager.OnPhaseChanged += OnPhaseChanged;
            }

            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                if (showDebugLogs) Debug.Log("[FixationCross] Created its own AudioSource component");
            }
            else
            {
                if (showDebugLogs) Debug.Log("[FixationCross] Using existing AudioSource component");
            }
            audioSource.playOnAwake = false;
            audioSource.loop = false;

            if (fixationCross != null)
            {
                fixationCross.SetActive(false);
            }
        }
        
        
        void OnPhaseChanged(TrialManager.TrialPhase newPhase)
        {
            if (showDebugLogs) Debug.Log($"[FixationCross] OnPhaseChanged received: {newPhase}");

            if (newPhase == TrialManager.TrialPhase.FixationCross)
            {
                if (showDebugLogs) Debug.Log("[FixationCross] Detected FixationCross phase - starting fixation");
                StartFixation();
            }
            else
            {
                if (showDebugLogs) Debug.Log($"[FixationCross] Detected {newPhase} phase - stopping fixation");
                StopFixation();
            }
        }
        
        public void StartFixation()
        {
            if (showDebugLogs) Debug.Log($"[FixationCross] StartFixation called - subjectEye: {(subjectEye != null ? subjectEye.name : "NULL")}, fixationCross: {(fixationCross != null ? fixationCross.name : "NULL")}");

            if (subjectEye == null)
            {
                Debug.LogError("[FixationCross] StartFixation FAILED - subjectEye is null");
                return;
            }

            if (fixationCross == null)
            {
                Debug.LogError("[FixationCross] StartFixation FAILED - fixationCross GameObject is null");
                return;
            }

            PositionCrossInFrontOfCamera();

            isActive = true;
            fixationCross.SetActive(false); // Hidden until participant looks straight ahead

            float duration = GetFixationDuration();
            if (showDebugLogs) Debug.Log($"[FixationCross] StartFixation SUCCESS - Duration: {duration}s, isActive: {isActive}, ready for gaze detection");
        }
        
        public void StopFixation()
        {
            if (showDebugLogs) Debug.Log($"[FixationCross] StopFixation called - isActive: {isActive}, countingDown: {countingDown}");

            isActive = false;

            if (fixationCross != null)
            {
                fixationCross.SetActive(false);
            }

            // Phase transition: stop audio without playing the break sound
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
                if (showDebugLogs) Debug.Log("[FixationCross] Stopped audio (no break sound - phase transition)");
            }

            // Bypass stopCountdown() to avoid triggering the gaze-break sound here
            StopAllCoroutines();
            countingDown = false;

            if (showDebugLogs) Debug.Log("[FixationCross] Fully stopped");
        }
        
        void PositionCrossInFrontOfCamera()
        {
            if (fixationCross != null && subjectEye != null)
            {
                Vector3 cameraPos = subjectEye.transform.position;

                // Anchor cross to platform forward, not camera forward, so it survives head turns
                Vector3 platformPos = startingPlatform != null ? startingPlatform.GetPlatformPosition() : Vector3.zero;
                Vector3 forwardDir = startingPlatform != null ? startingPlatform.GetInitialForwardDirection() : Vector3.forward;

                Vector3 crossPosition = platformPos + forwardDir * crossDistance;
                crossPosition.y = cameraPos.y + crossHeightOffset;

                fixationCross.transform.position = crossPosition;
                fixationCross.transform.LookAt(cameraPos);
                fixationCross.transform.Rotate(0, 180, 0);

                if (showDebugLogs) Debug.Log($"[FixationCross] Positioned cross at {crossPosition} ({crossDistance}m in direction {forwardDir}, {crossHeightOffset}m above eye level)");
            }
            else
            {
                Debug.LogError($"[FixationCross] Cannot position cross - fixationCross: {fixationCross != null}, subjectEye: {subjectEye != null}");
            }
        }
        
        void Update()
        {
            if (subjectEye == null)
            {
                return;
            }
            
            // Gate logic on both isActive and the FixationCross phase to avoid leaks across phases
            if (isActive && trialManager != null && trialManager.GetCurrentPhase() == TrialManager.TrialPhase.FixationCross)
            {
                if (showDebugLogs && Time.frameCount % 60 == 0)
                {
                    Debug.Log("[FixationCross] Update running - FixationCross phase active");
                }
                bool isLookingStraightAhead = isLookingAtCrossPosition(subjectEye);

                if (showDebugLogs && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[FixationCross] Gaze check - looking straight: {isLookingStraightAhead}, countingDown: {countingDown}, cross active: {fixationCross?.activeInHierarchy}");
                }

                if (isLookingStraightAhead)
                {
                    if (!fixationCross.activeInHierarchy)
                    {
                        if (showDebugLogs) Debug.Log("[FixationCross] Showing cross - participant looking straight ahead");
                        fixationCross.SetActive(true);
                    }

                    if (!countingDown)
                    {
                        if (showDebugLogs) Debug.Log("[FixationCross] Starting fresh countdown");
                        startCountdown();
                    }
                }
                else
                {
                    if (fixationCross.activeInHierarchy)
                    {
                        if (showDebugLogs) Debug.Log("[FixationCross] Hiding cross - gaze lost");
                        fixationCross.SetActive(false);
                    }

                    if (countingDown)
                    {
                        if (showDebugLogs) Debug.Log("[FixationCross] Stopping countdown - will restart fresh when gaze returns");
                        stopCountdown();
                    }
                }
            }
            else if (countingDown)
            {
                // FixationCross phase ended while counting down
                if (showDebugLogs) Debug.Log("[FixationCross] Update detected phase change - stopping countdown");
                stopCountdown();
            }
        }
        
        private void startCountdown()
        {
            if (showDebugLogs) Debug.Log("[FixationCross] Start countdown");

            if (countdownClip != null && audioSource != null)
            {
                audioSource.clip = countdownClip;
                audioSource.Play();
                if (showDebugLogs) Debug.Log($"[FixationCross] Playing countdown clip: {countdownClip.name}, duration: {countdownClip.length}s");
            }

            float duration = GetFixationDuration();
            StartCoroutine(countdownRoutine(duration));
            countingDown = true;
        }
        
        private void stopCountdown()
        {
            if (showDebugLogs) Debug.Log("[FixationCross] Stop countdown - gaze lost, will restart when gaze returns");
            StopAllCoroutines();

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }

            if (gazeBreakClip != null && audioSource != null)
            {
                audioSource.clip = gazeBreakClip;
                audioSource.Play();
                if (showDebugLogs) Debug.Log($"[FixationCross] Playing gaze break clip: {gazeBreakClip.name}");
            }

            countingDown = false;
            // Stay isActive so the countdown can restart when gaze returns
        }
        
        private IEnumerator countdownRoutine(float seconds)
        {
            if (showDebugLogs) Debug.Log($"[FixationCross] Begin countdown! Waiting for {seconds} seconds");
            float startTime = Time.time;

            // Drive duration from clip length so audio and visual stay synchronized
            if (countdownClip != null && audioSource != null)
            {
                if (showDebugLogs) Debug.Log("[FixationCross] Waiting for audio clip to finish playing...");
                while (audioSource.isPlaying && isActive)
                {
                    yield return null;
                }
                if (showDebugLogs) Debug.Log($"[FixationCross] Audio finished - audioSource.isPlaying: {audioSource.isPlaying}, isActive: {isActive}");
            }
            else
            {
                if (showDebugLogs) Debug.Log("[FixationCross] Using fallback timer (no audio clip)");
                yield return new WaitForSeconds(seconds);
            }

            float actualTime = Time.time - startTime;
            if (showDebugLogs) Debug.Log($"[FixationCross] Countdown completed! Waited {actualTime:F2}s (expected {seconds}s)");

            // Only complete if still active (gaze breaks may have reset countingDown)
            if (isActive)
            {
                if (showDebugLogs) Debug.Log("[FixationCross] SUCCESS - Audio clip finished, transitioning to ExperimentalPhase!");

                OnFixationCompleted?.Invoke();

                if (trialManager != null)
                {
                    if (showDebugLogs) Debug.Log("[FixationCross] Calling trialManager.SetPhase(ExperimentalPhase)");
                    trialManager.SetPhase(TrialManager.TrialPhase.ExperimentalPhase);
                    if (showDebugLogs) Debug.Log("[FixationCross] SetPhase call completed");
                }
                else
                {
                    Debug.LogError("[FixationCross] ERROR - trialManager is NULL!");
                }

                fixationCross.SetActive(false);
                isActive = false;
                countingDown = false;

                if (showDebugLogs) Debug.Log("[FixationCross] Cleanup completed");
            }
            else
            {
                if (showDebugLogs) Debug.Log($"[FixationCross] Countdown finished but NOT completing - isActive: {isActive} (fixation was stopped)");
            }
        }
        
        // True if gaze is within ~25 deg of the cross position. Uses EyeTracker when
        // available; falls back to head forward direction when not.
        private bool isLookingAtCrossPosition(Camera eye)
        {
            if (eye == null) return false;

            if (fixationCross == null) return false;

            Vector3 gazeOrigin;
            Vector3 gazeDirection;
            bool usingEyeTracking = false;

            if (eyeTracker != null && eyeTracker.HasValidGazeData())
            {
                gazeOrigin = eyeTracker.GetCombinedGazeOrigin();
                gazeDirection = eyeTracker.GetCombinedGazeDirection();
                usingEyeTracking = true;
            }
            else
            {
                gazeOrigin = eye.transform.position;
                gazeDirection = eye.transform.forward;
            }

            Vector3 actualCrossPosition = fixationCross.transform.position;

            Vector3 directionToCross = (actualCrossPosition - gazeOrigin).normalized;
            float dotProduct = Vector3.Dot(gazeDirection.normalized, directionToCross);

            // 0.9 dot ~ 25 degree cone
            bool isLookingStraight = dotProduct > 0.9f;

            if (showDebugLogs && Time.frameCount % 30 == 0)
            {
                string gazeSource = usingEyeTracking ? "EYE" : "HEAD";
                Debug.Log($"[FixationCross] GAZE {gazeSource}: dot={dotProduct:F2}, looking={isLookingStraight}, cross={actualCrossPosition}, origin={gazeOrigin}");
            }

            return isLookingStraight;
        }

        /// <summary>Fixation duration derived from countdown clip length (debug logging only).</summary>
        private float GetFixationDuration()
        {
            if (countdownClip != null)
            {
                return countdownClip.length;
            }

            Debug.LogWarning("[FixationCross] No countdown clip assigned, using 5-second fallback for logging");
            return 5.0f;
        }
        
        void OnDestroy()
        {
            if (trialManager != null)
            {
                trialManager.OnPhaseChanged -= OnPhaseChanged;
            }
        }
        
        [ContextMenu("Force Complete")]
        public void ForceComplete()
        {
            if (isActive)
            {
                OnFixationCompleted?.Invoke();
                if (trialManager != null)
                {
                    trialManager.SetPhase(TrialManager.TrialPhase.ExperimentalPhase);
                }
                StopFixation();
            }
        }
    }
}
