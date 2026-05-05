using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EyeTracking.Core
{
    /// <summary>
    /// Single subscriber to <see cref="SceneManager.activeSceneChanged"/>;
    /// fans the event out to every registered
    /// <see cref="ISceneTransitionAware"/> participant. Persistent
    /// (DontDestroyOnLoad). Auto-instantiates on first access or via
    /// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/> bootstrap.
    /// Centralizing the cleanup path is required so multiple persistent
    /// singletons can be invalidated in a guaranteed order (factory
    /// before providers, providers before consumers).
    /// </summary>
    public sealed class SceneTransitionCoordinator : MonoBehaviour
    {
        private static SceneTransitionCoordinator _instance;
        private static bool _quitting;
        private readonly List<ISceneTransitionAware> _participants = new List<ISceneTransitionAware>(8);

        public static SceneTransitionCoordinator Instance
        {
            get
            {
                if (_quitting) return null;
                if (_instance == null) Bootstrap();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("[SceneTransitionCoordinator]");
            _instance = go.AddComponent<SceneTransitionCoordinator>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            SceneManager.activeSceneChanged += HandleSceneChanged;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                SceneManager.activeSceneChanged -= HandleSceneChanged;
                _instance = null;
            }
        }

        private void OnApplicationQuit() => _quitting = true;

        /// <summary>
        /// Register a participant. Idempotent — safe to call from Awake even
        /// when the singleton hasn't been instantiated yet (we'll be created
        /// on first access).
        /// </summary>
        public void Register(ISceneTransitionAware participant)
        {
            if (participant == null) return;
            if (_participants.Contains(participant)) return;
            _participants.Add(participant);
        }

        public void Unregister(ISceneTransitionAware participant)
        {
            if (participant == null) return;
            _participants.Remove(participant);
        }

        private void HandleSceneChanged(Scene from, Scene to)
        {
            Debug.Log($"[SceneTransitionCoordinator] Scene change: '{from.name}' -> '{to.name}' ({_participants.Count} participants notified)");
            // Snapshot the list because participants may unregister during
            // notification (e.g., a scene-bound consumer destroyed mid-call).
            var snapshot = _participants.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnSceneWillUnload(from); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnSceneDidLoad(to); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }
    }
}
