// SPDX-License-Identifier: MIT
using UnityEngine;
using UnityEngine.SceneManagement;
using EyeTracking.Core;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Auto-spawns a <see cref="RIPAMonitor"/> into every scene that does
    /// not already contain one. Skipped while ReplayMode.IsActive, since the
    /// recorded LiveLoadIndex column is the authoritative source during
    /// playback. Set <see cref="DisableAutoSpawn"/> before scene load to opt
    /// out for a specific scene.
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
            // Recorded RIPA values come from the replay CSV, not a live recompute.
            if (EyeLean.Replay.SceneState.ReplayMode.IsActive) return;

            var existing = FindFirstObjectByType<RIPAMonitor>();
            if (existing != null) return;

            var go = new GameObject("[RIPAMonitor]");
            // Scope to the active scene; DDOL would survive scene transitions
            // and break per-scene reset semantics.
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            go.AddComponent<RIPAMonitor>();
            // CSVColumn no-ops when no SessionRecorder is present.
            go.AddComponent<RIPACSVColumn>();
        }
    }
}
