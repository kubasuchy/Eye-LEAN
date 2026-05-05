// SPDX-License-Identifier: MIT
using UnityEngine;
using EyeTracking.Components;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Adds a `LiveLoadIndex` column to the SessionRecorder CSV that
    /// reflects <see cref="RIPAMonitor.CurrentLoad"/> each row. Drop on any
    /// GameObject in a scene that has a <see cref="SessionRecorder"/>; if no
    /// recorder is present this component is inert. Registration must occur
    /// before the recorder locks its CSV header — added at execution order
    /// -40 to satisfy that ordering.
    /// </summary>
    [DefaultExecutionOrder(-40)] // after RIPAMonitor (-50), before SessionRecorder (0)
    public sealed class RIPACSVColumn : MonoBehaviour
    {
        [Tooltip("Column name written to the CSV header. Default 'LiveLoadIndex' is preserved for downstream-tooling compatibility.")]
        [SerializeField] private string columnName = "LiveLoadIndex";

        [Tooltip("Format string for the float value.")]
        [SerializeField] private string format = "F4";

        [Tooltip("Write the smoothed value (paper-recommended). When false, writes the raw clipped RIPA2 value.")]
        [SerializeField] private bool useSmoothedValue = true;

        private SessionRecorder recorder;
        private RIPAMonitor monitor;
        private bool registered;

        private void Awake()
        {
            recorder = FindFirstObjectByType<SessionRecorder>();
            monitor = RIPAMonitor.Instance;
            if (recorder == null)
            {
                // No SessionRecorder in scene — nothing to register. RIPAMonitor
                // is still available for HUD consumption.
                return;
            }
            recorder.RegisterMetric(columnName, ValueGetter, format);
            registered = true;
        }

        private float ValueGetter()
        {
            // Resolve monitor lazily — RIPAMonitorBootstrap may spawn it
            // after this component's Awake.
            if (monitor == null || !monitor) monitor = RIPAMonitor.Instance;
            if (monitor == null || !monitor)
            {
                if (!diagLoggedNullMonitor)
                {
                    diagLoggedNullMonitor = true;
                    Debug.LogWarning("[RIPACSVColumn] Monitor unresolved at first sample — LiveLoadIndex column will be 0 until a monitor is in the scene.");
                }
                return 0f;
            }
            if (!monitor.Enabled || !monitor.IsValid) return 0f;
            return useSmoothedValue ? monitor.CurrentLoad : monitor.CurrentRawLoad;
        }
        private bool diagLoggedNullMonitor;

        private void OnDestroy()
        {
            // Best-effort unregister. If the header is already written, the
            // column stays in the file as zeros from this point on.
            if (registered && recorder != null) recorder.UnregisterMetric(columnName);
        }
    }
}
