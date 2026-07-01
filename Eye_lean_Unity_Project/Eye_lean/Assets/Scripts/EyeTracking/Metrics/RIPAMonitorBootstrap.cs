// SPDX-License-Identifier: MIT
using UnityEngine;
using UnityEngine.SceneManagement;
using EyeTracking.Core;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Auto-spawns a <see cref="RIPAMonitor"/> into every scene that does
    /// not already contain one, UNLESS the scene's
    /// <see cref="ICognitiveLoadConfigProvider"/> (Eye_lean's EyeTracker)
    /// reports collection disabled — in which case no monitor, no CSV columns,
    /// and no HUD are created. Provider-less scenes default to all-on. Set
    /// <see cref="DisableAutoSpawn"/> before scene load to force-opt-out
    /// globally regardless of any provider.
    ///
    /// Behavior during deterministic replay (v1.0.1+): when the scene config
    /// enables collection, the monitor IS spawned and pulls pupil samples from
    /// <see cref="ReplayingEyeTracker"/>
    /// via <c>EyeTrackerFactory</c>. All enabled detectors recompute against
    /// the recorded pupil stream, producing the same values that were
    /// recorded live. The researcher can switch <c>DisplayedMethod</c> at
    /// replay time to view any method's HUD output, regardless of which
    /// detector was selected during the original session. CSV writes are
    /// still suppressed in replay (handled by SessionRecorder).
    ///
    /// Previously (v1.0–v1.3) the monitor was skipped during replay because
    /// the recorded LiveLoadIndex column was treated as authoritative. With
    /// multiple detectors recording per-method columns this is unnecessary —
    /// recomputation is deterministic and unlocks runtime method switching.
    /// </summary>
    public sealed class RIPAMonitorBootstrap : MonoBehaviour, ISceneTransitionAware
    {
        private static RIPAMonitorBootstrap _instance;

        /// <summary>
        /// When true, suppresses auto-spawn for scenes that have no monitor.
        /// Scenes with a manually placed RIPAMonitor are always honored.
        /// </summary>
        public static bool DisableAutoSpawn { get; set; } = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("[RIPAMonitorBootstrap]");
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<RIPAMonitorBootstrap>();
            DontDestroyOnLoad(go);
            // Past AfterSceneLoad on this call — handle the already-loaded scene now.
            _instance.EnsureMonitorInActiveScene();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            if (SceneTransitionCoordinator.Instance != null)
            {
                SceneTransitionCoordinator.Instance.Register(this);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                if (SceneTransitionCoordinator.Instance != null)
                {
                    SceneTransitionCoordinator.Instance.Unregister(this);
                }
                _instance = null;
            }
        }

        public void OnSceneWillUnload(Scene from) { }
        public void OnSceneDidLoad(Scene to) { EnsureMonitorInActiveScene(); }

        private void EnsureMonitorInActiveScene()
        {
            if (DisableAutoSpawn) return;

            var existing = FindFirstObjectByType<RIPAMonitor>();
            if (existing != null) return; // a manually placed monitor wins; configure it directly.

            // Resolve this scene's collection preferences. Provider-less scenes
            // (e.g. a menu with no rig) use Default (all on) so nothing changes
            // for them. The same scene-authored config is read during replay,
            // so "off" is honored on playback too.
            CognitiveLoadConfig cfg = ResolveConfig();
            if (!cfg.CollectsAnything) return; // master off (or every method off) → no monitor, no columns, no HUD.

            var go = new GameObject("[RIPAMonitor]");
            // Scope to the active scene; DDOL would survive scene transitions
            // and break per-scene reset semantics.
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            var monitor = go.AddComponent<RIPAMonitor>();
            // Apply BEFORE RIPAMonitor.Start()/BuildDetectors runs (AddComponent
            // ran Awake synchronously; Start is later this frame).
            monitor.ApplyConfig(cfg);
            // CSVColumn no-ops when no SessionRecorder is present; when present it
            // reads monitor.CurrentConfig in its Awake to pick enabled columns.
            go.AddComponent<RIPACSVColumn>();
        }

        /// <summary>
        /// Find the active scene's first <see cref="ICognitiveLoadConfigProvider"/>
        /// (Eye_lean's EyeTracker). Falls back to <see cref="CognitiveLoadConfig.Default"/>
        /// when none is present.
        /// </summary>
        private static CognitiveLoadConfig ResolveConfig()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICognitiveLoadConfigProvider provider)
                    return provider.GetCognitiveLoadConfig();
            }
            return CognitiveLoadConfig.Default;
        }
    }
}
