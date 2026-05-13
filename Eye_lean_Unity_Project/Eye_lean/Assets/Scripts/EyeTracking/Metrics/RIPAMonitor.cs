// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using EyeTracking.Components;
using EyeTracking.Core;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Scene-scoped cognitive-load monitor. Pulls pupil samples each Update
    /// from <see cref="EyeTracker"/> and fans them out to every enabled
    /// <see cref="ICognitiveLoadDetector"/>. One detector is the
    /// <see cref="DisplayedMethod"/>, whose value drives <see cref="CurrentLoad"/>
    /// and the HUD; all enabled detectors get a per-row CSV column via
    /// <see cref="RIPACSVColumn"/>.
    ///
    /// Detectors:
    ///   • <see cref="CognitiveLoadMethod.RIPA2"/> — v1.3+ default (Jayawardena 2025).
    ///   • <see cref="CognitiveLoadMethod.Butterworth"/> — added v1.0.1
    ///     (Duchowski 2026, "Real-Time Cognitive Load Measurement of
    ///     Pupillary Oscillation", Proc. ACM CGIT 9(2) Article 23).
    ///   • FFT and DWT are reserved enum values; their analyzers ship in a
    ///     follow-up patch.
    ///
    /// The "monitor still records when HUD is hidden" semantics carry over:
    /// each enabled detector runs every frame so the CSV columns are complete
    /// regardless of which detector is displayed. During deterministic
    /// replay, the bootstrap skips spawning this monitor — recorded CSV
    /// columns are the authoritative playback source.
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

        [Header("Method selection")]
        [Tooltip("Which detector drives CurrentLoad / OnLoadChanged / HUD. All enabled detectors still record to CSV regardless of this choice.")]
        [SerializeField] private CognitiveLoadMethod displayedMethod = CognitiveLoadMethod.RIPA2;

        [Tooltip("Run RIPA2 detector (Jayawardena 2025). Default ON for back-compat with v1.0–v1.3 datasets.")]
        [SerializeField] private bool enableRipa2 = true;
        [Tooltip("Run Butterworth IIR LF/HF detector (Duchowski 2026, Listing 3). Default ON in v1.0.1.")]
        [SerializeField] private bool enableButterworth = true;
        [Tooltip("Run FFT periodogram LF/HF detector (Duchowski 2026, Listing 1). Needs ~34 s warm-up; CPU cost ~8 % of a single core at 60 Hz.")]
        [SerializeField] private bool enableFft = true;
        [Tooltip("Run db4 DWT LF/HF detector (Duchowski 2026, Listing 2). Needs ~34 s warm-up.")]
        [SerializeField] private bool enableDwt = true;

        [Header("RIPA2 filter sizing")]
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

        [Header("RIPA2 buffering")]
        [Tooltip("Rolling pupil-buffer length in seconds. Must cover the VLF SG window (≈3.3 s at the paper's parameters). Paper uses 4 s @ 300 Hz.")]
        [SerializeField] private float bufferSeconds = 4f;
        [Tooltip("Trailing moving-average window applied to raw RIPA2 outputs. Paper recommends 1–2 s.")]
        [SerializeField] private float smoothingSeconds = 1.5f;

        [Header("Butterworth filter sizing")]
        [Tooltip("LF/HF band boundary in Hz. Below this is the LF band (lowpass); between this and HighBand is the HF band (bandpass). Duchowski 2026 default 1.6 Hz.")]
        [SerializeField] private float bwLowBandHz = 1.6f;
        [Tooltip("Upper edge of the HF band in Hz. Duchowski 2026 default 4 Hz.")]
        [SerializeField] private float bwHighBandHz = 4.0f;
        [Tooltip("Butterworth filter order for both LP and BP. Paper recommends 4–6; default 4.")]
        [Range(2, 8)]
        [SerializeField] private int bwFilterOrder = 4;
        [Tooltip("Sliding power-estimation window for the Butterworth detector, in seconds. Paper default 5 s.")]
        [SerializeField] private float bwPowerWindowSeconds = 5.0f;

        [Header("FFT / DWT sizing")]
        [Tooltip("Rolling-buffer length (in samples) for the FFT and DWT detectors. Must be a power of two. 2048 ≈ 34 s at 60 Hz, covering the paper's 30 s default and 7.5–10 s minimums.")]
        [SerializeField] private int fftDwtBufferSamples = 2048;
        [Tooltip("Maximum DWT decomposition depth. Capped by floor(log2(bufferSamples)). At 60 Hz with 2048 samples, level 8 covers the LF band's lowest sub-octaves.")]
        [Range(2, 12)]
        [SerializeField] private int dwtMaxLevel = 8;

        [Header("LF/HF HUD scaling (Butterworth / FFT / DWT)")]
        [Tooltip("Ceiling on the raw LF/HF ratio. Duchowski 2026 §7.2 cites a 'practical range' of [0, 20] for raw pupil at 1 kHz from clinical eye trackers. HMD eye trackers that pre-smooth pupil (VIVE Focus Vision observed: ~98 % of pupil signal power below 1.6 Hz) produce typical ratios of 50–200. Set higher than the paper's 20 so the HUD doesn't pin against the cap on smoothed pupil streams.")]
        [SerializeField] private float lfHfMaxRatio = 200f;
        [Tooltip("Scale factor in smoothed = log(1+ratio) · scale. Paper default 0.5 maps ratio≈20 to the HUD ceiling of 1.5. With the cap raised to 200 a smaller scale (~0.28) is needed so log(1+200)·scale lands near the ceiling instead of clipping the whole upper range. Tune per device.")]
        [SerializeField] private float lfHfSmoothedScale = 0.28f;

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
        /// whether the *displayed* detector is producing data.
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

        public CognitiveLoadMethod DisplayedMethod
        {
            get => displayedMethod;
            set
            {
                // Accept if the requested detector exists, or if detectors
                // haven't been built yet (BuildDetectors runs in Start; the
                // serialized value is set in Awake by the inspector). When
                // build runs we'll fall back there if needed.
                if (activeDetectors.Count == 0 || GetDetector(value) != null)
                {
                    displayedMethod = value;
                    return;
                }
                CognitiveLoadMethod fallback = activeDetectors[0].Method;
                Debug.LogWarning($"[RIPAMonitor] Requested DisplayedMethod '{value}' is not enabled on this monitor — falling back to '{fallback}'. Enable the corresponding detector in the RIPAMonitor inspector to select it.");
                displayedMethod = fallback;
            }
        }

        /// <summary>True once the *displayed* detector has produced a valid reading.</summary>
        public bool IsValid
        {
            get
            {
                ICognitiveLoadDetector d = GetDisplayedDetector();
                return d != null && d.IsValid;
            }
        }

        /// <summary>Smoothed output of the displayed detector, in [0, 1.5].</summary>
        public float CurrentLoad { get; private set; }

        /// <summary>Raw (pre-smoothing) output of the displayed detector. Units depend on <see cref="DisplayedMethod"/>.</summary>
        public float CurrentRawLoad { get; private set; }

        /// <summary>RIPA2 analyzer if enabled; null otherwise. Kept for v1.3+ tooling.</summary>
        public RIPA2Analyzer Analyzer => ripa2Detector?.Analyzer;

        /// <summary>All detectors that this monitor will sample each frame. Stable order: RIPA2, Butterworth, ...</summary>
        public IReadOnlyList<ICognitiveLoadDetector> EnabledDetectors => activeDetectors;

        /// <summary>Look up a detector by method, or null if it's not enabled.</summary>
        public ICognitiveLoadDetector GetDetector(CognitiveLoadMethod method)
        {
            for (int i = 0; i < activeDetectors.Count; i++)
                if (activeDetectors[i].Method == method) return activeDetectors[i];
            return null;
        }

        // ---- Internals ----

        private readonly List<ICognitiveLoadDetector> activeDetectors = new List<ICognitiveLoadDetector>();
        private Ripa2Detector ripa2Detector;
        private ButterworthDetector butterworthDetector;
        private FftDetector fftDetector;
        private DwtDetector dwtDetector;
        private float resolvedSampleRateHz;
        private float lastPublishTime;

        // Diagnostic counters: surface where the pipeline drops samples when
        // a CSV column is unexpectedly zero post-session.
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
            BuildDetectors();
            lastPublishTime = Time.unscaledTime;
        }

        private void BuildDetectors()
        {
            activeDetectors.Clear();

            if (enableRipa2)
            {
                try
                {
                    var analyzer = RIPA2Analyzer.FromCutoffs(
                        resolvedSampleRateHz,
                        vlfCutoffHz, vlfPolyOrder,
                        lfCutoffHz, lfPolyOrder,
                        bufferSeconds, smoothingSeconds);
                    ripa2Detector = new Ripa2Detector(analyzer);
                    activeDetectors.Add(ripa2Detector);
                    Debug.Log($"[RIPAMonitor] RIPA2 active. fs={resolvedSampleRateHz:F1} Hz, M_VLF={analyzer.SgVlf.HalfWidth} (window {analyzer.SgVlf.WindowSize}), M_LF={analyzer.SgLf.HalfWidth} (window {analyzer.SgLf.WindowSize}), buffer={analyzer.BufferSize}, smoothing={analyzer.SmoothingSize}.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RIPAMonitor] Failed to construct RIPA2 analyzer: {e.Message}. Detector disabled.");
                }
            }

            if (enableButterworth)
            {
                try
                {
                    var bw = new ButterworthLfHfAnalyzer(
                        resolvedSampleRateHz,
                        bwLowBandHz, bwHighBandHz,
                        bwFilterOrder, bwPowerWindowSeconds,
                        lfHfMaxRatio, lfHfSmoothedScale);
                    butterworthDetector = new ButterworthDetector(bw);
                    activeDetectors.Add(butterworthDetector);
                    Debug.Log($"[RIPAMonitor] Butterworth active. fs={resolvedSampleRateHz:F1} Hz, LF=lowpass(<={bwLowBandHz} Hz), HF=bandpass({bwLowBandHz}-{bwHighBandHz} Hz), order={bwFilterOrder}, power window={bwPowerWindowSeconds:F1}s ({bw.PowerWindowSamples} samples), cap={lfHfMaxRatio:F0}, scale={lfHfSmoothedScale:F3}.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RIPAMonitor] Failed to construct Butterworth analyzer: {e.Message}. Detector disabled.");
                }
            }

            if (enableFft)
            {
                try
                {
                    var fft = new FftLfHfAnalyzer(
                        resolvedSampleRateHz,
                        fftDwtBufferSamples,
                        bwLowBandHz, bwHighBandHz,
                        lfHfMaxRatio, lfHfSmoothedScale);
                    fftDetector = new FftDetector(fft);
                    activeDetectors.Add(fftDetector);
                    Debug.Log($"[RIPAMonitor] FFT active. fs={resolvedSampleRateHz:F1} Hz, buffer={fftDwtBufferSamples} samples ({fft.BufferSeconds:F1}s), Δf={resolvedSampleRateHz/fftDwtBufferSamples:F4} Hz, cap={lfHfMaxRatio:F0}, scale={lfHfSmoothedScale:F3}.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RIPAMonitor] Failed to construct FFT analyzer: {e.Message}. Detector disabled.");
                }
            }

            if (enableDwt)
            {
                try
                {
                    // Cap maxLevel at floor(log2(bufferSamples)) so the
                    // cascade doesn't divide past length-1 at the bottom.
                    int capLevel = 0;
                    for (int v = fftDwtBufferSamples; v > 1; v >>= 1) capLevel++;
                    int level = Math.Min(dwtMaxLevel, capLevel);
                    var dwt = new DwtLfHfAnalyzer(
                        resolvedSampleRateHz,
                        fftDwtBufferSamples, level,
                        bwLowBandHz, bwHighBandHz,
                        lfHfMaxRatio, lfHfSmoothedScale);
                    dwtDetector = new DwtDetector(dwt);
                    activeDetectors.Add(dwtDetector);
                    Debug.Log($"[RIPAMonitor] DWT active. fs={resolvedSampleRateHz:F1} Hz, buffer={fftDwtBufferSamples} samples ({dwt.BufferSeconds:F1}s), max_level={level}, cap={lfHfMaxRatio:F0}, scale={lfHfSmoothedScale:F3}.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RIPAMonitor] Failed to construct DWT analyzer: {e.Message}. Detector disabled.");
                }
            }

            if (activeDetectors.Count == 0)
            {
                Debug.LogError("[RIPAMonitor] No detectors enabled. Monitor will produce no output.");
                enableMonitor = false;
                return;
            }

            // If the displayed method isn't among the enabled detectors, fall
            // back to the first enabled one so the HUD has something to show.
            if (GetDisplayedDetector() == null)
            {
                CognitiveLoadMethod fallback = activeDetectors[0].Method;
                Debug.LogWarning($"[RIPAMonitor] Displayed method '{displayedMethod}' is not enabled — falling back to '{fallback}'.");
                displayedMethod = fallback;
            }
        }

        private ICognitiveLoadDetector GetDisplayedDetector() => GetDetector(displayedMethod);

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
            if (!enableMonitor || activeDetectors.Count == 0) return;

            double pupil = SamplePupilDiameter();
            if (!double.IsNaN(pupil) && pupil > 0.0)
            {
                for (int i = 0; i < activeDetectors.Count; i++) activeDetectors[i].PushSample(pupil);
                diagPushedSamples++;
            }
            else
            {
                diagSkippedNaNSamples++;
            }

            ICognitiveLoadDetector displayed = GetDisplayedDetector();
            if (displayed == null) return;

            // Periodic diagnostic distinguishing the three zero-output causes:
            // (a) samples never reached the detector, (b) IsValid never became
            // true, or (c) the detector is producing genuinely small values.
            float now = Time.unscaledTime;
            if (now - diagLastReport >= DiagReportIntervalSec)
            {
                Debug.Log($"[RIPAMonitor] Diag: pushed={diagPushedSamples} skippedNaN={diagSkippedNaNSamples} " +
                          $"displayed={displayed.Method} valid={displayed.IsValid} " +
                          $"raw={displayed.CurrentRaw:F4} smoothed={displayed.CurrentSmoothed:F4}");
                diagLastReport = now;
            }

            if (!displayed.IsValid) return;
            if (!diagLoggedFirstValid)
            {
                diagLoggedFirstValid = true;
                Debug.Log($"[RIPAMonitor] First valid output ({displayed.Method}): raw={displayed.CurrentRaw:F4} smoothed={displayed.CurrentSmoothed:F4} (after {diagPushedSamples} pushed samples).");
            }

            if (publishIntervalSeconds > 0f && now - lastPublishTime < publishIntervalSeconds) return;
            lastPublishTime = now;

            float smoothed = displayed.CurrentSmoothed;
            float raw = displayed.CurrentRaw;
            // Always publish the latest detector outputs to the public
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
