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
    /// CSV layout (v1.0.1+) — per-method columns appear ONLY when that method is enabled:
    ///   • <c>LiveLoadIndex</c>           — displayed detector's smoothed value (back-compat with v1.0–v1.3 tooling).
    ///   • <c>LiveLoadIndex_RIPA2</c>     — RIPA2 smoothed value (when enabled).
    ///   • <c>LiveLoadIndex_BW</c>        — Butterworth LF/HF smoothed value (when enabled).
    ///   • <c>LiveLoadIndex_BW_Raw</c>    — Butterworth raw LF/HF ratio (when enabled and butterworthRecordRawRatio is on).
    ///   • <c>LiveLoadIndex_FFT</c>       — FFT periodogram LF/HF smoothed value (when enabled).
    ///   • <c>LiveLoadIndex_DWT</c>       — db4 DWT LF/HF smoothed value (when enabled).
    ///
    /// The per-detector columns let downstream tooling and the replay system
    /// switch between methods without recomputing — columns are registered only
    /// for methods that are enabled in the CognitiveLoadConfig.
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
                Debug.LogWarning("[RIPACSVColumn] No RIPAMonitor in scene at Awake; no cognitive-load columns will be registered.");
            }
            RegisterColumnsFor(recorder, monitor);
        }

        /// <summary>
        /// Register one CSV column per ENABLED cognitive-load method (plus the
        /// legacy alias when at least one method is on) against the given
        /// recorder. Columns for disabled methods are OMITTED entirely (not
        /// zero-filled), so the CSV header is config-dependent as of this
        /// feature. Public + parameterized so EditMode tests can drive it
        /// without the scene-scanning Awake path.
        /// </summary>
        public void RegisterColumnsFor(SessionRecorder targetRecorder, RIPAMonitor targetMonitor)
        {
            if (targetRecorder == null) return;
            recorder = targetRecorder;
            monitor = targetMonitor;

            // A null/absent monitor yields default(CognitiveLoadConfig) whose
            // Collect is false, so Plan returns an empty list → no columns.
            CognitiveLoadConfig cfg = targetMonitor != null ? targetMonitor.CurrentConfig : default;
            var plan = CognitiveLoadColumns.Plan(cfg, butterworthRecordRawRatio, legacyColumnName);

            for (int i = 0; i < plan.Count; i++)
            {
                CognitiveLoadColumn col = plan[i];
                CognitiveLoadMethod m = col.Method; // capture for the closures below
                switch (col.Kind)
                {
                    case CognitiveLoadColumnKind.LegacyDisplayed:
                        recorder.RegisterMetric(col.Name, () => DisplayedValue(useSmoothedValue), format);
                        break;
                    case CognitiveLoadColumnKind.DetectorSmoothed:
                        recorder.RegisterMetric(col.Name, () => DetectorValue(m, useSmoothedValue), format);
                        break;
                    case CognitiveLoadColumnKind.DetectorRaw:
                        recorder.RegisterMetric(col.Name, () => DetectorRaw(m), format);
                        break;
                }
                registered.Add(col.Name);
            }
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
