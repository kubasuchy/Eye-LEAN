// SPDX-License-Identifier: MIT
using UnityEngine;

/// <summary>
/// Drives environment generation from TrialManager phase changes. Spawns the
/// trial environment when entering ExperimentalPhase and tears it down on every
/// other phase. Also owns background audio and the exit-trigger callback that
/// completes the trial.
/// </summary>

namespace EyeLean.Skeleton
{
    public class EnvironmentManager : MonoBehaviour
    {
        #region Configuration

        [Header("Environment Settings")]
        [Tooltip("Width of environment (single source of truth for environment width)")]
        [Range(1.5f, 10.0f)]
        public float environmentWidth = 3.0f;

        [Tooltip("Default environment length when not specified by trial")]
        [Range(3.0f, 50.0f)]
        public float defaultEnvironmentLength = 10.0f;

        [Header("Materials")]
        [Tooltip("Floor material")]
        public Material floorMaterial;

        [Tooltip("Wall material")]
        public Material wallMaterial;

        [Header("Background Audio")]
        [Tooltip("Audio clips to play during experimental phase (one selected randomly per trial)")]
        public AudioClip[] backgroundAudioClips;

        [Tooltip("Volume for background audio")]
        [Range(0f, 1f)]
        public float backgroundAudioVolume = 0.5f;

        [Tooltip("Enable looping of background audio")]
        public bool loopBackgroundAudio = true;

        [Header("Debug")]
        [Tooltip("Show debug information")]
        public bool showDebugInfo = false;

        #endregion

        #region Private State

        private bool isEnvironmentActive = false;
        private Vector3 environmentOriginPosition;

        private TrialManager trialManager;
        private Camera subjectEye;
        private AgentManager agentManager;
        private StartingPlatform startingPlatform;
        private BaseEnvironment baseEnvironment;

        private AudioSource backgroundAudioSource;

        #endregion

        #region Events

        public System.Action OnEnvironmentStarted;
        public System.Action OnEnvironmentCompleted;

        #endregion

        #region Unity Lifecycle

        void Start()
        {
            trialManager = FindFirstObjectByType<TrialManager>();
            subjectEye = Camera.main ?? FindFirstObjectByType<Camera>();
            startingPlatform = FindFirstObjectByType<StartingPlatform>();

            InitializeSubsystems();
            InitializeBackgroundAudio();

            if (trialManager != null)
            {
                trialManager.OnPhaseChanged += OnPhaseChanged;
            }

            if (showDebugInfo) Debug.Log($"[EnvironmentManager] Initialized - Width: {environmentWidth}m");
        }

        void OnDestroy()
        {
            if (trialManager != null)
            {
                trialManager.OnPhaseChanged -= OnPhaseChanged;
            }

            if (baseEnvironment != null)
            {
                baseEnvironment.OnExitReached -= OnExitReached;
            }
        }

        #endregion

        #region Initialization

        void InitializeSubsystems()
        {
            baseEnvironment = gameObject.GetComponent<BaseEnvironment>();
            if (baseEnvironment == null)
            {
                baseEnvironment = gameObject.AddComponent<BaseEnvironment>();
            }

            baseEnvironment.CurrentWidth = environmentWidth;

            baseEnvironment.OnExitReached += OnExitReached;
            if (showDebugInfo) Debug.Log("[EnvironmentManager] Subscribed to BaseEnvironment.OnExitReached");

            agentManager = gameObject.GetComponent<AgentManager>();
            if (agentManager == null)
            {
                agentManager = gameObject.AddComponent<AgentManager>();
            }

            if (showDebugInfo) Debug.Log("[EnvironmentManager] Subsystems initialized");
        }

        void InitializeBackgroundAudio()
        {
            backgroundAudioSource = gameObject.AddComponent<AudioSource>();
            backgroundAudioSource.playOnAwake = false;
            backgroundAudioSource.spatialBlend = 0f; // 2D / non-positional
            backgroundAudioSource.volume = backgroundAudioVolume;
            backgroundAudioSource.loop = loopBackgroundAudio;

            int clipCount = backgroundAudioClips != null ? backgroundAudioClips.Length : 0;
            if (showDebugInfo) Debug.Log($"[EnvironmentManager] Background audio initialized - {clipCount} clips");
        }

        #endregion

        #region Phase Handling

        void OnPhaseChanged(TrialManager.TrialPhase newPhase)
        {
            if (newPhase == TrialManager.TrialPhase.ExperimentalPhase)
            {
                StartEnvironmentPhase();
            }
            else
            {
                StopEnvironmentPhase();
            }
        }

        public void StartEnvironmentPhase()
        {
            if (trialManager == null || subjectEye == null)
            {
                Debug.LogError("[EnvironmentManager] Missing required components!");
                return;
            }

            var currentTrialData = trialManager.GetCurrentTrial();
            if (currentTrialData == null)
            {
                Debug.LogError("[EnvironmentManager] No current trial data!");
                return;
            }

            // Per-trial length should come from extended TrialData; default for now
            float environmentLength = defaultEnvironmentLength;

            // Environment is anchored to the current platform position
            Vector3 platformPos = startingPlatform != null ? startingPlatform.GetPlatformPosition() : Vector3.zero;
            environmentOriginPosition = platformPos;

            GenerateEnvironment(environmentLength);

            isEnvironmentActive = true;
            OnEnvironmentStarted?.Invoke();

            StartBackgroundAudio();

            if (showDebugInfo) Debug.Log($"[EnvironmentManager] Environment started - Length: {environmentLength}m");
        }

        public void StopEnvironmentPhase()
        {
            if (!isEnvironmentActive) return;

            isEnvironmentActive = false;

            StopBackgroundAudio();

            if (agentManager != null)
            {
                agentManager.StopAgentSystem();
            }

            if (baseEnvironment != null)
            {
                baseEnvironment.ClearEnvironment();
            }
        }

        #endregion

        #region Environment Generation

        void GenerateEnvironment(float environmentLength)
        {
            baseEnvironment.GenerateEnvironment(environmentLength, environmentOriginPosition);

            // AgentManager initialization is experiment-specific; researchers should
            // call agentManager.Initialize / SpawnAgent themselves.
        }

        #endregion

        #region Background Audio

        void StartBackgroundAudio()
        {
            if (backgroundAudioSource == null) return;
            if (backgroundAudioClips == null || backgroundAudioClips.Length == 0) return;

            int randomIndex = Random.Range(0, backgroundAudioClips.Length);
            AudioClip selectedClip = backgroundAudioClips[randomIndex];

            if (selectedClip == null) return;

            backgroundAudioSource.clip = selectedClip;
            backgroundAudioSource.volume = backgroundAudioVolume;
            backgroundAudioSource.loop = loopBackgroundAudio;
            backgroundAudioSource.Play();

            if (showDebugInfo) Debug.Log($"[EnvironmentManager] Background audio started - '{selectedClip.name}'");
        }

        void StopBackgroundAudio()
        {
            if (backgroundAudioSource != null && backgroundAudioSource.isPlaying)
            {
                backgroundAudioSource.Stop();
                if (showDebugInfo) Debug.Log("[EnvironmentManager] Background audio stopped");
            }
        }

        #endregion

        #region Exit Handling

        void OnExitReached()
        {
            if (trialManager != null)
            {
                trialManager.SetPhase(TrialManager.TrialPhase.TrialComplete);
            }
            else
            {
                Debug.LogError("[EnvironmentManager] TrialManager is NULL - cannot complete trial!");
            }

            OnEnvironmentCompleted?.Invoke();
        }

        #endregion

        #region Public API

        /// <summary>True while a trial environment is currently spawned.</summary>
        public bool IsEnvironmentActive()
        {
            return isEnvironmentActive;
        }

        /// <summary>World-space origin of the active environment.</summary>
        public Vector3 GetEnvironmentOrigin()
        {
            return environmentOriginPosition;
        }

        /// <summary>Underlying <see cref="BaseEnvironment"/> for advanced configuration.</summary>
        public BaseEnvironment GetBaseEnvironment()
        {
            return baseEnvironment;
        }

        /// <summary>Underlying <see cref="AgentManager"/> for agent configuration.</summary>
        public AgentManager GetAgentManager()
        {
            return agentManager;
        }

        #endregion

        #region Debug

        void OnGUI()
        {
            if (!showDebugInfo || !Application.isPlaying) return;

            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(320, 10, 300, 100));
            GUILayout.Label("<b>ENVIRONMENT DEBUG</b>");
            GUILayout.Label($"Active: {isEnvironmentActive}");
            GUILayout.Label($"Width: {environmentWidth}m");
            if (isEnvironmentActive)
            {
                GUILayout.Label($"Origin: {environmentOriginPosition}");
            }
            GUILayout.EndArea();
        }

        [ContextMenu("Test Environment Generation")]
        public void TestEnvironmentGeneration()
        {
            if (showDebugInfo) Debug.Log("[EnvironmentManager] MANUAL TEST - Starting environment generation");
            StartEnvironmentPhase();
        }

        #endregion
    }
}
