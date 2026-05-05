// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EyeLean.Skeleton
{
    /// <summary>
    /// Session-level coordinator: owns the <see cref="ExperimentState"/> machine,
    /// auto-discovers <see cref="TrialManager"/>, enforces a session-timeout safety
    /// net, and shows the end-of-experiment thank-you canvas. Eye / head / CSV
    /// recording is handled by Eye_lean's <c>EyeTracker</c> + <c>HMDDataCollector</c>
    /// + <c>SessionRecorder</c> trio, not by this class.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class ExperimentManager : MonoBehaviour
    {
        [Header("Session Parameters")]
        [Tooltip("If true, the session starts automatically once every subsystem reports Ready. If false, call StartSession() from your own UI / controller.")]
        [SerializeField] private bool autoStartSession = false;
        [Tooltip("Hard ceiling for the session, in minutes. The manager auto-completes the session when this is exceeded — safety net against forgotten sessions.")]
        [SerializeField] private float sessionTimeoutMinutes = 60f;

        public enum ExperimentState
        {
            Uninitialized, Initializing, Ready, Running, Paused, Completed, Error
        }

        public event Action<ExperimentState> OnStateChanged;
        public event Action<string> OnSystemError;
        public event Action OnSessionStarted;
        public event Action OnSessionCompleted;

        private ExperimentState currentState = ExperimentState.Uninitialized;
        private float sessionStartTime;
        private TrialManager trialManager;
        private GameObject endOfExperimentCanvas;
        private readonly Dictionary<string, bool> subsystemStatus = new Dictionary<string, bool>();
        private readonly List<string> initializationErrors = new List<string>();

        private void Awake()
        {
            trialManager = FindFirstObjectByType<TrialManager>();
        }

        private void Start()
        {
            InitializeExperiment();
        }

        private void Update()
        {
            if (currentState == ExperimentState.Running &&
                Time.time - sessionStartTime > sessionTimeoutMinutes * 60f)
            {
                Debug.Log("[ExperimentManager] Session timeout reached.");
                StopSession();
            }
        }

        private void InitializeExperiment()
        {
            ChangeState(ExperimentState.Initializing);
            initializationErrors.Clear();
            subsystemStatus.Clear();

            bool ok = true;
            ok &= InitializeTrialManager();

            if (ok)
            {
                ChangeState(ExperimentState.Ready);
                if (autoStartSession) StartSession();
            }
            else
            {
                ChangeState(ExperimentState.Error);
                foreach (var e in initializationErrors)
                    Debug.LogError($"[ExperimentManager] Initialization error: {e}");
            }
            ReportSystemStatus();
        }

        private bool InitializeTrialManager()
        {
            if (trialManager == null)
            {
                initializationErrors.Add("TrialManager not found in scene");
                subsystemStatus["TrialManager"] = false;
                return false;
            }
            try
            {
                trialManager.OnPhaseChanged += OnTrialPhaseChanged;
                trialManager.OnTrialStarted += OnTrialStarted;
                trialManager.OnTrialCompleted += OnTrialCompleted;
                trialManager.OnAllTrialsCompleted += OnAllTrialsCompleted;
                subsystemStatus["TrialManager"] = true;
                return true;
            }
            catch (Exception e)
            {
                initializationErrors.Add($"TrialManager init failed: {e.Message}");
                subsystemStatus["TrialManager"] = false;
                return false;
            }
        }

        public void StartSession()
        {
            if (currentState != ExperimentState.Ready)
            {
                Debug.LogError("[ExperimentManager] Cannot start session - system not ready");
                return;
            }
            ChangeState(ExperimentState.Running);
            sessionStartTime = Time.time;
            OnSessionStarted?.Invoke();
        }

        public void StopSession()
        {
            if (currentState == ExperimentState.Running)
            {
                ChangeState(ExperimentState.Completed);
                OnSessionCompleted?.Invoke();
            }
        }

        public void PauseSession()
        {
            if (currentState == ExperimentState.Running) ChangeState(ExperimentState.Paused);
        }

        public void ResumeSession()
        {
            if (currentState == ExperimentState.Paused) ChangeState(ExperimentState.Running);
        }

        private void ChangeState(ExperimentState s)
        {
            if (currentState == s) return;
            var prev = currentState;
            currentState = s;
            Debug.Log($"[ExperimentManager] State: {prev} → {s}");
            OnStateChanged?.Invoke(s);
        }

        private void OnTrialPhaseChanged(TrialManager.TrialPhase _) { }
        private void OnTrialStarted(TrialManager.TrialData _) { }
        private void OnTrialCompleted(TrialManager.TrialData _) { }

        private void OnAllTrialsCompleted()
        {
            ShowEndOfExperimentMessage();
            StopSession();
        }

        public ExperimentState GetCurrentState() => currentState;
        public bool AreAllSystemsReady() => currentState == ExperimentState.Ready || currentState == ExperimentState.Running;
        public TrialManager.TrialData GetCurrentTrial() => trialManager?.GetCurrentTrial();
        public TrialManager.TrialPhase GetCurrentTrialPhase() =>
            trialManager?.GetCurrentPhase() ?? TrialManager.TrialPhase.InterTrialInterval;

        private void ShowEndOfExperimentMessage()
        {
            if (endOfExperimentCanvas != null) Destroy(endOfExperimentCanvas);

            Camera mainCamera = Camera.main ?? FindFirstObjectByType<Camera>();
            if (mainCamera == null) return;

            endOfExperimentCanvas = new GameObject("EndOfExperimentCanvas");
            var canvas = endOfExperimentCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            Vector3 pos = mainCamera.transform.position + mainCamera.transform.forward * 2.0f;
            endOfExperimentCanvas.transform.position = pos;
            endOfExperimentCanvas.transform.rotation = Quaternion.LookRotation(pos - mainCamera.transform.position);

            var rt = endOfExperimentCanvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(800, 600);
            endOfExperimentCanvas.transform.localScale = Vector3.one * 0.002f;

            endOfExperimentCanvas.AddComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 10f;
            endOfExperimentCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(endOfExperimentCanvas.transform, false);
            var bgImg = bg.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.85f);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero; bgRect.anchoredPosition = Vector2.zero;

            CreateLabel(endOfExperimentCanvas.transform, "ThankYouText", "Thanks for participating!", 72, FontStyle.Bold, Color.white, new Vector2(0.5f, 0.6f));
            CreateLabel(endOfExperimentCanvas.transform, "SubtitleText", "The experiment is now complete.\nPlease let the experimenter know and keep your headset on.", 36, FontStyle.Normal, new Color(0.8f, 0.8f, 0.8f, 1f), new Vector2(0.5f, 0.4f));
        }

        private static void CreateLabel(Transform parent, string name, string text, int size, FontStyle style, Color color, Vector2 anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<UnityEngine.UI.Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(700, 100);
            rt.anchoredPosition = Vector2.zero;
        }

        private void ReportSystemStatus()
        {
            Debug.Log($"[ExperimentManager] State: {currentState}");
            foreach (var s in subsystemStatus)
                Debug.Log($"[ExperimentManager] {s.Key}: {(s.Value ? "OK" : "FAIL")}");
        }

        private void OnApplicationQuit()
        {
            if (currentState == ExperimentState.Running) StopSession();
        }
    }
}
