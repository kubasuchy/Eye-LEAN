using UnityEngine;
using UnityEngine.SceneManagement;

namespace EyeTracking.Core
{
    /// <summary>
    /// Hidden persistent MonoBehaviour that wires the static
    /// <see cref="EyeTrackerFactory"/> cache into the
    /// <see cref="SceneTransitionCoordinator"/> ISceneTransitionAware fan-out.
    /// On scene change it calls <see cref="EyeTrackerFactory.Invalidate"/> so
    /// the next <c>GetEyeTracker</c> call re-detects against the new scene's
    /// XR state. Auto-bootstrapped BeforeSceneLoad.
    /// </summary>
    public sealed class EyeTrackerFactoryBootstrapper : MonoBehaviour, ISceneTransitionAware
    {
        private static EyeTrackerFactoryBootstrapper _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("[EyeTrackerFactoryBootstrapper]");
            _instance = go.AddComponent<EyeTrackerFactoryBootstrapper>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            SceneTransitionCoordinator.Instance.Register(this);
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

        public void OnSceneWillUnload(Scene from)
        {
            EyeTrackerFactory.Invalidate();
        }

        public void OnSceneDidLoad(Scene to)
        {
            // Lazy: next GetEyeTracker() call re-detects.
        }
    }
}
