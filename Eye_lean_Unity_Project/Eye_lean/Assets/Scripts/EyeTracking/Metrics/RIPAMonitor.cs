// SPDX-License-Identifier: MIT
using System;
using UnityEngine;
using UnityEngine.Events;
using EyeTracking.Components;
using EyeTracking.Core;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Scene-scoped wrapper around <see cref="RIPA2Analyzer"/> (Jayawardena,
    /// Jayawardana & Gwizdka 2025, *J. Eye Movement Research* 18(6) #70 —
    /// see ACKNOWLEDGMENTS.md). Pulls pupil samples each Update from
    /// <see cref="EyeTracker"/> (or any registered IEyeTracker via
    /// <see cref="EyeTrackerFactory"/>) and publishes the smoothed RIPA2
    /// value through <see cref="OnLoadChanged"/>. Auto-spawned by
    /// <see cref="RIPAMonitorBootstrap"/> if absent from the scene.
    ///
    /// Intentionally NOT DontDestroyOnLoad: scene-spanning eye-tracking
    /// components hold stale transforms after a scene reload and flood NREs.
    /// The static <see cref="Instance"/> cache is re-resolved per scene.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class RIPAMonitor : MonoBehaviour
    {
        [Serializable]
        public sealed class FloatEvent : UnityEvent<float> { }

        [Header("Master switch")]
        [Tooltip("When false, no work and no events. Toggle at runtime via Enabled.")]
        [SerializeField] private bool enableMonitor = true;

        [Header("Filter sizing")]
        [Tooltip("If non-zero, override the auto-detected sample rate. The eye tracker reports its native rate (60–90 Hz on VIVE Focus Vision); set this to pin a fixed rate for cross-session reproducibility.")]
        [SerializeField] private float sampleRateOverrideHz = 0f;
        [Tooltip("VLF target cutoff in Hz. Paper default 0.29 Hz (Medeiros et al. 2021 optimal VLF band 0.06–0.29).")]
        [SerializeField] private float vlfCutoffHz = 0.29f;
        [Tooltip("LF target cutoff in Hz. Paper default 4 Hz (Peysakhovich et al. 2017 cognitive-load LF band 1.6–4).")]
        [SerializeField] private float lfCutoffHz = 4.0f;
        [Tooltip("Polynomial order of the VLF SG filter. Paper uses 2.")]
        [Range(1, 6)]
        [SerializeField] private int vlfPolyOrder = 2;
        [Tooltip("Polynomial order of the LF SG filter. Paper uses 4.")]
        [Range(1, 8)]
        [SerializeField] private int lfPolyOrder = 4;

        [Header("Buffering")]
        [Tooltip("Rolling pupil-buffer length in seconds. Must cover the VLF SG window (≈3.3 s at the paper's parameters). Paper uses 4 s @ 300 Hz.")]
        [SerializeField] private float bufferSeconds = 4f;
        [Tooltip("Trailing moving-average window applied to raw RIPA2 outputs. Paper recommends 1–2 s.")]
        [SerializeField] private float smoothingSeconds = 1.5f;

        [Header("Publish cadence")]
        [Tooltip("Minimum interval between OnLoadChanged events. The internal compute still runs every frame; this throttles the event for HUD update.")]
        [SerializeField] private float publishIntervalSeconds = 0.0f;

        [Header("Component References (auto-found)")]
        [SerializeField] private EyeTracker eyeTracker;

        public FloatEvent OnLoadChanged;

        // ---- Scene singleton ----

        private static RIPAMonitor _instance;
        private static bool _quitting;

        /// <summary>
        /// Returns the active scene's RIPAMonitor, or null. Cached but
        /// re-resolved when the cached instance becomes invalid (scene
        /// reload, manual destroy). Use <see cref="IsValid"/> to check
        /// whether the analyzer is actually producing data.
        /// </summary>
        public static RIPAMonitor Instance
        {
            get
            {
                if (_quitting) return null;
                if (_instance != null && _instance) return _instance;
                _instance = FindFirstObjectByType<RIPAMonitor>();
                return _instance;
            }
        }

        // ---- Public API ----

        public bool Enabled
        {
            get => enableMonitor;
            set => enableMonitor = value;
        }

        /// <summary>True once the analyzer has buffered enough samples to
        /// produce a valid RIPA2 reading (full VLF window).</summary>
        public bool IsValid => analyzer != null && analyzer.IsValid;

        /// <summary>Smoothed RIPA2 value clipped to [0, 1.5]. Paper-recommended
        /// metric for the gauge / consumer code.</summary>
        public float CurrentLoad { get; private set; }

        /// <summary>Raw (unsmoothed) clipped RIPA2 value. Available for
        /// downstream filters that want their own smoothing.</summary>
        public float CurrentRawLoad { get; private set; }

        public RIPA2Analyzer Analyzer => analyzer;

        // ---- Internals ----

        private RIPA2Analyzer analyzer;
        private float resolvedSampleRateHz;
        private float lastPublishTime;
        // Diagnostic counters: surface where the pipeline drops samples when
        // the LiveLoadIndex CSV column is unexpectedly zero post-session.
        private int diagPushedSamples;
        private int diagSkippedNaNSamples;
        private bool diagLoggedFirstValid;
        private float diagLastReport;
        private const float DiagReportIntervalSec = 5f;

        private void Awake()
        {
            if (_instance != null && _instance != this && _instance)
            {
                Debug.LogWarning($"[RIPAMonitor] Duplicate instance on '{gameObject.name}'; destroying duplicate. Existing instance is on '{_instance.gameObject.name}'.");
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnApplicationQuit() { _quitting = true; }

        private void Start()
        {
            if (eyeTracker == null) eyeTracker = GetComponent<EyeTracker>() ?? FindFirstObjectByType<EyeTracker>();
            ResolveSampleRate();
            try
            {
                analyzer = RIPA2Analyzer.FromCutoffs(
                    resolvedSampleRateHz,
                    vlfCutoffHz, vlfPolyOrder,
                    lfCutoffHz, lfPolyOrder,
                    bufferSeconds, smoothingSeconds);
                Debug.Log($"[RIPAMonitor] Active. fs={resolvedSampleRateHz:F1} Hz, M_VLF={analyzer.SgVlf.HalfWidth} (window {analyzer.SgVlf.WindowSize}), M_LF={analyzer.SgLf.HalfWidth} (window {analyzer.SgLf.WindowSize}), buffer={analyzer.BufferSize} samples, smoothing={analyzer.SmoothingSize} samples.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RIPAMonitor] Failed to construct analyzer: {e.Message}. Monitor disabled.");
                enableMonitor = false;
            }
            lastPublishTime = Time.unscaledTime;
        }

        private void ResolveSampleRate()
        {
            if (sampleRateOverrideHz > 0f) { resolvedSampleRateHz = sampleRateOverrideHz; return; }
            // Try the live tracker for a native rate.
            try
            {
                IEyeTracker tracker = EyeTrackerFactory.GetEyeTracker();
                if (tracker != null && tracker.SamplingRateHz > 1f)
                {
                    resolvedSampleRateHz = tracker.SamplingRateHz;
                    return;
                }
            }
            catch (Exception) { /* fall through */ }
            // Sensible default that fits VIVE Focus Vision and most modern HMDs.
            resolvedSampleRateHz = 60f;
        }

        private void Update()
        {
            if (!enableMonitor || analyzer == null) return;

            double pupil = SamplePupilDiameter();
            if (!double.IsNaN(pupil) && pupil > 0.0)
            {
                analyzer.PushSample(pupil);
                diagPushedSamples++;
            }
            else
            {
                diagSkippedNaNSamples++;
            }

            // Periodic diagnostic distinguishing the three zero-output causes:
            // (a) samples never reached the analyzer, (b) IsValid never became
            // true, or (c) the analyzer is producing genuinely small values.
            float now = Time.unscaledTime;
            if (now - diagLastReport >= DiagReportIntervalSec)
            {
                Debug.Log($"[RIPAMonitor] Diag: pushed={diagPushedSamples} skippedNaN={diagSkippedNaNSamples} " +
                          $"buffered={analyzer.SamplesBuffered}/{analyzer.SgVlf.WindowSize} valid={analyzer.IsValid} " +
                          $"raw={analyzer.CurrentRaw:F4} smoothed={analyzer.CurrentSmoothed:F4}");
                diagLastReport = now;
            }

            if (!analyzer.IsValid) return;
            if (!diagLoggedFirstValid)
            {
                diagLoggedFirstValid = true;
                Debug.Log($"[RIPAMonitor] First valid output: raw={analyzer.CurrentRaw:F4} smoothed={analyzer.CurrentSmoothed:F4} (after {diagPushedSamples} pushed samples).");
            }

            if (publishIntervalSeconds > 0f && now - lastPublishTime < publishIntervalSeconds) return;
            lastPublishTime = now;

            float smoothed = (float)analyzer.CurrentSmoothed;
            float raw = (float)analyzer.CurrentRaw;
            // Always publish the latest analyzer outputs to the public
            // properties (a calm session can produce values within
            // Mathf.Approximately's ~9.5e-6 tolerance of zero, which would
            // otherwise leave CurrentLoad pinned at its initial 0). Only the
            // OnLoadChanged event is gated by the approximate-equality check,
            // so subscribers (HUD gauge etc.) aren't spammed.
            float prev = CurrentLoad;
            CurrentLoad = smoothed;
            CurrentRawLoad = raw;
            if (Mathf.Approximately(smoothed, prev)) return;
            try { OnLoadChanged?.Invoke(CurrentLoad); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // Average the per-eye pupil diameters that report valid this frame.
        private double SamplePupilDiameter()
        {
            if (eyeTracker != null)
            {
                EyeFrameSample s = eyeTracker.SampleSnapshot();
                bool hasL = s.HasLeftValid && s.LeftPupilDiameter > 0f && !float.IsNaN(s.LeftPupilDiameter);
                bool hasR = s.HasRightValid && s.RightPupilDiameter > 0f && !float.IsNaN(s.RightPupilDiameter);
                if (hasL && hasR) return (s.LeftPupilDiameter + s.RightPupilDiameter) * 0.5;
                if (hasL) return s.LeftPupilDiameter;
                if (hasR) return s.RightPupilDiameter;
                return double.NaN;
            }
            // Fallback for scenes without an EyeTracker MonoBehaviour;
            // the IEyeTracker factory is always populated.
            try
            {
                var tracker = EyeTrackerFactory.GetEyeTracker();
                if (tracker == null || !tracker.IsAvailable) return double.NaN;
                bool hasL = tracker.GetLeftPupilDiameter(out float lmm) && lmm > 0f;
                bool hasR = tracker.GetRightPupilDiameter(out float rmm) && rmm > 0f;
                if (hasL && hasR) return (lmm + rmm) * 0.5;
                if (hasL) return lmm;
                if (hasR) return rmm;
            }
            catch (Exception) { /* fall through to NaN */ }
            return double.NaN;
        }
    }
}
