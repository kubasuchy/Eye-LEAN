// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using UnityEngine;
using EyeTracking.Components;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Registers one CSV column per enabled <see cref="ICognitiveLoadDetector"/>
    /// on the scene's <see cref="SessionRecorder"/>, plus a legacy
    /// <c>LiveLoadIndex</c> column aliased to the currently-displayed
    /// detector. Drop on any GameObject in a scene that has a
    /// <see cref="SessionRecorder"/>; if no recorder is present this
    /// component is inert.
    ///
    /// CSV layout (v1.0.1+):
    ///   • <c>LiveLoadIndex</c>           — displayed detector's smoothed value (back-compat with v1.0–v1.3 tooling).
    ///   • <c>LiveLoadIndex_RIPA2</c>     — RIPA2 smoothed value.
    ///   • <c>LiveLoadIndex_BW</c>        — Butterworth LF/HF smoothed value.
    ///   • <c>LiveLoadIndex_BW_Raw</c>    — Butterworth raw LF/HF ratio (optional).
    ///   • <c>LiveLoadIndex_FFT</c>       — FFT periodogram LF/HF smoothed value.
    ///   • <c>LiveLoadIndex_DWT</c>       — db4 DWT LF/HF smoothed value.
    ///
    /// The per-detector columns let downstream tooling and the replay system
    /// switch between methods without recomputing — every method's value is
    /// always recorded regardless of which one was on-screen at the time.
    ///
    /// Registration must occur before the recorder locks its CSV header —
    /// added at execution order -40 to satisfy that ordering.
    /// </summary>
    [DefaultExecutionOrder(-40)] // after RIPAMonitor (-50), before SessionRecorder (0)
    public sealed class RIPACSVColumn : MonoBehaviour
    {
        [Tooltip("Legacy column name written for back-compat (always reflects RIPAMonitor.CurrentLoad, i.e. the displayed detector). Set empty to omit.")]
        [SerializeField] private string legacyColumnName = "LiveLoadIndex";

        [Tooltip("Format string for the float value.")]
        [SerializeField] private string format = "F4";

        [Tooltip("Write the smoothed value for each per-detector column (paper-recommended). When false, writes the raw clipped value.")]
        [SerializeField] private bool useSmoothedValue = true;

        [Tooltip("For the Butterworth detector, also record the raw (uncapped) LF/HF ratio as a separate column. Useful for offline analysis at scales beyond the [0, 1.5] HUD clip.")]
        [SerializeField] private bool butterworthRecordRawRatio = true;

        private SessionRecorder recorder;
        private RIPAMonitor monitor;
        private readonly List<string> registered = new List<string>();
        private bool diagLoggedNullMonitor;

        private void Awake()
        {
            recorder = FindFirstObjectByType<SessionRecorder>();
            monitor = RIPAMonitor.Instance;
            if (recorder == null)
            {
                // No SessionRecorder in scene — nothing to register. The
                // monitor is still available for HUD consumption.
                return;
            }
            if (monitor == null)
            {
                // Monitor hasn't been spawned yet by the bootstrap. The
                // value getters resolve lazily; we register columns now so
                // the header is correct, but values will be 0 until the
                // bootstrap creates the monitor.
                Debug.LogWarning("[RIPACSVColumn] No RIPAMonitor in scene at Awake; columns will register but values are 0 until a monitor appears.");
            }

            RegisterColumns();
        }

        private void RegisterColumns()
        {
            // Legacy alias: reflects the displayed detector. Preserves
            // v1.0–v1.3 downstream tooling that reads `LiveLoadIndex`.
            if (!string.IsNullOrEmpty(legacyColumnName))
            {
                recorder.RegisterMetric(legacyColumnName, () => DisplayedValue(useSmoothedValue), format);
                registered.Add(legacyColumnName);
            }

            // Always register a column per known detector method so the CSV
            // schema is stable across sessions (zeros for disabled methods
            // are clearer than columns appearing/disappearing). The
            // value-getter checks at write time whether the detector exists.
            RegisterPerDetector(CognitiveLoadMethod.RIPA2, "RIPA2");
            RegisterPerDetector(CognitiveLoadMethod.Butterworth, "BW");
            if (butterworthRecordRawRatio)
            {
                string name = "LiveLoadIndex_BW_Raw";
                recorder.RegisterMetric(name, () => DetectorRaw(CognitiveLoadMethod.Butterworth), format);
                registered.Add(name);
            }
            RegisterPerDetector(CognitiveLoadMethod.FFT, "FFT");
            RegisterPerDetector(CognitiveLoadMethod.DWT, "DWT");
        }

        private void RegisterPerDetector(CognitiveLoadMethod method, string suffix)
        {
            string name = "LiveLoadIndex_" + suffix;
            recorder.RegisterMetric(name, () => DetectorValue(method, useSmoothedValue), format);
            registered.Add(name);
        }

        private float DisplayedValue(bool smoothed)
        {
            if (monitor == null || !monitor) monitor = RIPAMonitor.Instance;
            if (monitor == null || !monitor)
            {
                if (!diagLoggedNullMonitor)
                {
                    diagLoggedNullMonitor = true;
                    Debug.LogWarning("[RIPACSVColumn] Monitor unresolved at first sample — LiveLoadIndex columns will be 0 until a monitor is in the scene.");
                }
                return 0f;
            }
            if (!monitor.Enabled || !monitor.IsValid) return 0f;
            return smoothed ? monitor.CurrentLoad : monitor.CurrentRawLoad;
        }

        private float DetectorValue(CognitiveLoadMethod method, bool smoothed)
        {
            if (monitor == null || !monitor) monitor = RIPAMonitor.Instance;
            if (monitor == null || !monitor || !monitor.Enabled) return 0f;
            ICognitiveLoadDetector d = monitor.GetDetector(method);
            if (d == null || !d.IsValid) return 0f;
            return smoothed ? d.CurrentSmoothed : d.CurrentRaw;
        }

        private float DetectorRaw(CognitiveLoadMethod method) => DetectorValue(method, false);

        private void OnDestroy()
        {
            // Best-effort unregister. If the header is already written, the
            // columns stay in the file as zeros from this point on.
            if (recorder == null) return;
            for (int i = 0; i < registered.Count; i++) recorder.UnregisterMetric(registered[i]);
            registered.Clear();
        }
    }
}
