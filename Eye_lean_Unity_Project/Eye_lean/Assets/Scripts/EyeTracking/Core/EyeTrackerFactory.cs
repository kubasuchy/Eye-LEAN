using UnityEngine;

namespace EyeTracking.Core
{
    /// <summary>
    /// Factory for creating and managing eye tracker instances.
    /// Automatically detects available eye tracking hardware and returns the appropriate provider.
    /// </summary>
    public static class EyeTrackerFactory
    {
        private static IEyeTracker _cachedTracker;
        private static IEyeTracker _replayOverride; // deterministic replay routes here
        private static bool _initialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Install a replay-time tracker that returns recorded gaze samples
        /// instead of live hardware. Set by <c>ReplayController</c> when
        /// entering deterministic-replay playback. Pass <c>null</c> to
        /// clear the override and resume hardware detection on the next
        /// <see cref="GetEyeTracker"/> call.
        /// </summary>
        public static void SetReplayOverride(IEyeTracker tracker)
        {
            lock (_lock) { _replayOverride = tracker; }
        }

        /// <summary>
        /// Get the current eye tracker instance.
        /// Automatically detects and initializes the appropriate provider.
        /// Thread-safe singleton pattern.
        /// </summary>
        public static IEyeTracker GetEyeTracker()
        {
            // Replay override wins so every IEyeTracker consumer
            // (CalibrationWorldUI, gameplay gaze checks, anything via the
            // factory) reads recorded samples during deterministic replay.
            var ovr = _replayOverride;
            if (ovr != null) return ovr;

            if (!_initialized)
            {
                lock (_lock)
                {
                    if (!_initialized)
                    {
                        _cachedTracker = DetectAndCreateTracker();
                        // Always ensure we have a valid tracker (NullEyeTracker at minimum)
                        if (_cachedTracker == null)
                        {
                            _cachedTracker = new NullEyeTracker();
                        }
                        _initialized = true;
                    }
                }
            }
            return _cachedTracker;
        }

        /// <summary>
        /// Force re-detection of eye tracking hardware.
        /// Useful if hardware state changes at runtime.
        /// Thread-safe.
        /// </summary>
        public static void Reinitialize()
        {
            lock (_lock)
            {
                _initialized = false;
                _cachedTracker = DetectAndCreateTracker();
                // Always ensure we have a valid tracker (NullEyeTracker at minimum)
                if (_cachedTracker == null)
                {
                    _cachedTracker = new NullEyeTracker();
                }
                _initialized = true;
            }
        }

        /// <summary>
        /// Invalidate the cached tracker so the next <see cref="GetEyeTracker"/>
        /// call re-runs detection. Called by EyeTrackerFactoryBootstrapper on
        /// scene change so a stale provider reference (with stale internal
        /// transforms) doesn't outlive the scene that set it up. The provider
        /// singleton itself (e.g. OpenXREyeTrackerProvider.Instance) is
        /// unaffected — it's the static cache here that we drop.
        /// </summary>
        public static void Invalidate()
        {
            lock (_lock)
            {
                _initialized = false;
                _cachedTracker = null;
            }
        }

        /// <summary>
        /// Check if any eye tracker is available.
        /// </summary>
        public static bool IsAnyTrackerAvailable()
        {
            var tracker = GetEyeTracker();
            return tracker != null && tracker.IsAvailable;
        }

        /// <summary>
        /// Get the name of the current eye tracker device.
        /// </summary>
        public static string GetCurrentDeviceName()
        {
            var tracker = GetEyeTracker();
            return tracker?.DeviceName ?? "None";
        }

        /// <summary>
        /// Detect available eye tracking hardware and create the appropriate provider.
        /// Priority order: VIVE OpenXR > Varjo > HoloLens > Mock
        /// </summary>
        private static IEyeTracker DetectAndCreateTracker()
        {
            Debug.Log("[EyeTrackerFactory] Detecting eye tracking hardware...");

#if USE_OPENXR
            // Try VIVE OpenXR first
            if (TryGetVIVEOpenXRTracker(out IEyeTracker viveTracker))
            {
                Debug.Log($"[EyeTrackerFactory] Using VIVE OpenXR eye tracker: {viveTracker.DeviceName}");
                TryAutoLoadDefaultProfile();
                return viveTracker;
            }
#endif

#if USE_VARJO
            // Try Varjo
            if (TryGetVarjoTracker(out IEyeTracker varjoTracker))
            {
                Debug.Log($"[EyeTrackerFactory] Using Varjo eye tracker: {varjoTracker.DeviceName}");
                return varjoTracker;
            }
#endif

#if USE_HOLOLENS
            // Try HoloLens 2
            if (TryGetHoloLensTracker(out IEyeTracker holoLensTracker))
            {
                Debug.Log($"[EyeTrackerFactory] Using HoloLens 2 eye tracker: {holoLensTracker.DeviceName}");
                return holoLensTracker;
            }
#endif

            // No hardware found
            Debug.LogWarning("[EyeTrackerFactory] No eye tracking hardware detected. Eye tracking will be unavailable.");
            return new NullEyeTracker();
        }

#if USE_OPENXR
        private static bool TryGetVIVEOpenXRTracker(out IEyeTracker tracker)
        {
            try
            {
                // Check if OpenXREyeTracker singleton exists and is available
                if (Providers.OpenXREyeTrackerProvider.Instance != null &&
                    Providers.OpenXREyeTrackerProvider.Instance.IsAvailable)
                {
                    tracker = Providers.OpenXREyeTrackerProvider.Instance;
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[EyeTrackerFactory] Failed to initialize VIVE OpenXR tracker: {e.Message}");
            }

            tracker = null;
            return false;
        }
#endif

#if USE_VARJO
        private static bool TryGetVarjoTracker(out IEyeTracker tracker)
        {
            // Varjo provider is not yet implemented
            // To implement: Add VarjoEyeTrackerProvider.cs in Providers/ folder
            // that implements IEyeTracker interface using Varjo SDK
            Debug.LogWarning("[EyeTrackerFactory] USE_VARJO is defined but Varjo provider is not implemented. " +
                            "Eye tracking will fall back to another available provider or NullEyeTracker. " +
                            "To add Varjo support, implement VarjoEyeTrackerProvider : IEyeTracker");
            tracker = null;
            return false;
        }
#endif

        /// <summary>
        /// Best-effort auto-load of the per-user EyeTrackingProfile from
        /// Application.persistentDataPath/EyeLeanProfiles/_default.json. Called
        /// once when a hardware tracker is first selected. Logs whether the
        /// file existed and whether it applied so we can diagnose
        /// auto-load problems from the device log alone (the OpenXREyeTracker
        /// MonoBehaviour's own auto-load path runs in a different lifecycle
        /// and isn't always exercised in every scene).
        /// </summary>
        private static void TryAutoLoadDefaultProfile()
        {
            try
            {
                string dir = Configuration.EyeTrackingProfile.DefaultDirectory;
                string path = System.IO.Path.Combine(
                    dir, Configuration.EyeTrackingProfile.DefaultProfileFileName);
                if (!System.IO.File.Exists(path))
                {
                    Debug.Log($"[EyeTrackerFactory] No default profile at {path}; using identity correction.");
                    return;
                }
                var profile = Configuration.EyeTrackingProfile.Load(path);
                Configuration.ActiveProfile.Apply(profile);
                Debug.Log($"[EyeTrackerFactory] Auto-loaded default profile '{profile.metadata.profileName}' from {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[EyeTrackerFactory] Auto-load of default profile failed: {e.Message}");
            }
        }

#if USE_HOLOLENS
        private static bool TryGetHoloLensTracker(out IEyeTracker tracker)
        {
            // HoloLens 2 provider is not yet implemented
            // To implement: Add HoloLensEyeTrackerProvider.cs in Providers/ folder
            // that implements IEyeTracker interface using Windows.Perception.People.EyesPose
            Debug.LogWarning("[EyeTrackerFactory] USE_HOLOLENS is defined but HoloLens provider is not implemented. " +
                            "Eye tracking will fall back to another available provider or NullEyeTracker. " +
                            "To add HoloLens 2 support, implement HoloLensEyeTrackerProvider : IEyeTracker");
            tracker = null;
            return false;
        }
#endif
    }

    /// <summary>
    /// Null object pattern implementation for when no eye tracker is available.
    /// All methods return false/default values.
    /// </summary>
    public class NullEyeTracker : IEyeTracker
    {
        public bool IsAvailable => false;
        public string DeviceName => "None";
        public float SamplingRateHz => 0f;

        public bool GetCombinedGazeOrigin(out Vector3 origin) { origin = Vector3.zero; return false; }
        public bool GetCombinedGazeDirection(out Vector3 direction) { direction = Vector3.forward; return false; }

        public bool GetLeftEyeOrigin(out Vector3 origin) { origin = Vector3.zero; return false; }
        public bool GetLeftEyeDirection(out Vector3 direction) { direction = Vector3.forward; return false; }
        public bool GetLeftEyeOpenness(out float openness) { openness = 0f; return false; }
        public bool GetLeftPupilDiameter(out float diameterMm) { diameterMm = 0f; return false; }
        public bool GetLeftPupilPosition(out Vector2 position) { position = Vector2.zero; return false; }

        public bool GetRightEyeOrigin(out Vector3 origin) { origin = Vector3.zero; return false; }
        public bool GetRightEyeDirection(out Vector3 direction) { direction = Vector3.forward; return false; }
        public bool GetRightEyeOpenness(out float openness) { openness = 0f; return false; }
        public bool GetRightPupilDiameter(out float diameterMm) { diameterMm = 0f; return false; }
        public bool GetRightPupilPosition(out Vector2 position) { position = Vector2.zero; return false; }
    }
}
