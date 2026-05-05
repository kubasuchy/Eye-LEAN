// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using UnityEngine;
using EyeTracking.Components;
using EyeLean.SceneState;

namespace EyeLean.Skeleton.Examples
{
    /// <summary>
    /// Reference implementation of <see cref="IExperimentPhaseHandler"/> that
    /// exercises the Eye_lean integration points researchers typically need:
    /// per-frame metric columns (<c>SessionRecorder.RegisterMetric</c>), discrete
    /// events (<c>SceneEventRecorder</c>), replay-friendly stimulus spawning
    /// (<c>SceneStateRecorder.MarkRecordableSeeded</c>), and reading the live
    /// cognitive-load index from <c>RIPAMonitor</c>. Copy this file as a starting
    /// point for a new experimental phase handler.
    /// </summary>
    public sealed class EyeleanDemoPhaseHandler : MonoBehaviour, IExperimentPhaseHandler
    {
        [Header("Phase Configuration")]
        [SerializeField] private float maxPhaseDuration = 5f;
        [SerializeField] private float stimulusMinDistance = 1.5f;
        [SerializeField] private float stimulusMaxDistance = 3.5f;
        [SerializeField] private Color stimulusColor = new Color(0.95f, 0.30f, 0.30f);
        [SerializeField] private KeyCode editorResponseKey = KeyCode.Space;

        private TrialManager trialManager;
        private SessionRecorder sessionRecorder;
        private GameObject stimulus;
        private bool isPhaseActive;
        private float phaseStartTime;
        private float responseTime;
        private bool responseReceived;

        private void Awake()
        {
            trialManager = FindFirstObjectByType<TrialManager>();
            sessionRecorder = FindFirstObjectByType<SessionRecorder>();
            if (trialManager == null)
            {
                Debug.LogError("[EyeleanDemo] TrialManager not found in scene.");
                return;
            }
            trialManager.SetPhaseHandler(this);

            // Per-frame metric: getter is called once per CSV row. Use this
            // pattern for any Boolean/numeric column derived from scene state.
            if (sessionRecorder != null)
            {
                sessionRecorder.RegisterMetric(
                    "StimulusVisible",
                    () => (isPhaseActive && stimulus != null && stimulus.activeSelf) ? 1f : 0f,
                    "F0");
            }
            else
            {
                Debug.LogWarning("[EyeleanDemo] SessionRecorder not found — metric column not registered.");
            }
        }

        private void Update()
        {
            if (!isPhaseActive || responseReceived) return;
            // Editor-side response key. Hook XRI's primary-button action for headset.
            if (Input.GetKeyDown(editorResponseKey)) RecordResponse();
        }

        public void OnPhaseStart()
        {
            isPhaseActive = true;
            phaseStartTime = Time.time;
            responseTime = 0f;
            responseReceived = false;
            SpawnStimulus();
            // Sidecar event so analysts can find stimulus-onset frames without scanning the per-frame CSV
            int trial = trialManager?.GetCurrentTrial()?.trialNumber ?? 0;
            SceneEventRecorder.RecordKV("StimulusOnset", "stimulus",
                ("trial", trial.ToString()),
                ("color", $"{stimulusColor.r:F2};{stimulusColor.g:F2};{stimulusColor.b:F2}"));
        }

        public void OnPhaseEnd()
        {
            isPhaseActive = false;
            if (stimulus != null) Destroy(stimulus);
            if (!responseReceived)
            {
                SceneEventRecorder.RecordKV("ResponseTimeout", "",
                    ("trial", (trialManager?.GetCurrentTrial()?.trialNumber ?? 0).ToString()),
                    ("phaseDuration", (Time.time - phaseStartTime).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        public bool IsPhaseComplete()
        {
            if (responseReceived) return true;
            return Time.time - phaseStartTime >= maxPhaseDuration;
        }

        public Dictionary<string, object> GetPhaseData()
        {
            // Each key becomes a CSV column (declared lazily) AND a field on the
            // TrialCompleted event row.
            float load = 0f;
            var monitor = EyeTracking.Metrics.RIPAMonitor.Instance;
            if (monitor != null && monitor.IsValid) load = monitor.CurrentLoad;
            return new Dictionary<string, object>
            {
                { "responseReceived", responseReceived },
                { "responseTime", responseTime },
                { "phaseDuration", Time.time - phaseStartTime },
                { "ripaAtResponse", load },
            };
        }

        private void SpawnStimulus()
        {
            stimulus = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stimulus.name = "DemoStimulus";
            // Distance is deterministic across replays because TrialManager seeds Random.InitState
            float distance = Random.Range(stimulusMinDistance, stimulusMaxDistance);
            Camera cam = Camera.main ?? FindFirstObjectByType<Camera>();
            Vector3 pos = cam != null
                ? cam.transform.position + cam.transform.forward * distance
                : new Vector3(0, 1.5f, distance);
            stimulus.transform.position = pos;
            stimulus.transform.localScale = Vector3.one * 0.2f;

            var renderer = stimulus.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = stimulusColor;

            // Deterministic seed so replay re-creates the stimulus at the same scene-state row
            int trial = trialManager?.GetCurrentTrial()?.trialNumber ?? 0;
            SceneStateRecorder.MarkRecordableSeeded(stimulus, $"DemoStimulus_{trial}");
        }

        private void RecordResponse()
        {
            responseReceived = true;
            responseTime = Time.time - phaseStartTime;
            int trial = trialManager?.GetCurrentTrial()?.trialNumber ?? 0;
            SceneEventRecorder.RecordKV("ResponseGiven", "stimulus",
                ("trial", trial.ToString()),
                ("rt", responseTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
