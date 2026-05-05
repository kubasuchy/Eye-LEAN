using UnityEngine.SceneManagement;

namespace EyeTracking.Core
{
    /// <summary>
    /// Implemented by any singleton or persistent (DontDestroyOnLoad)
    /// component that holds references to scene-bound GameObjects
    /// (Camera.main, its parent rig, FindObjectOfType results). The
    /// SceneTransitionCoordinator fans out scene-change events so each
    /// participant can null its cached refs and let a lazy resolver pick
    /// up the new scene's equivalents.
    ///
    /// Required because Unity's overloaded `==` operator marks objects
    /// destroyed only after the scene's OnDestroy pass — between unload
    /// and that flag commit, a persistent singleton dereferencing a
    /// cached Transform can read garbage and produce floods of NREs.
    ///
    /// Enforce by PR grep: `SceneManager.activeSceneChanged +=` should
    /// appear only in SceneTransitionCoordinator; every other consumer
    /// goes through this interface.
    /// </summary>
    public interface ISceneTransitionAware
    {
        /// <summary>
        /// Called by SceneTransitionCoordinator immediately before <paramref name="from"/>
        /// is unloaded. Implementations should null out any cached references
        /// to GameObjects in <paramref name="from"/> so a stale Unity-overloaded
        /// null-check can't return a false negative on the next frame.
        /// </summary>
        void OnSceneWillUnload(Scene from);

        /// <summary>
        /// Called by SceneTransitionCoordinator after <paramref name="to"/> has
        /// loaded and its first Awake pass has completed. Implementations may
        /// re-resolve scene-bound references here, or defer to a per-frame lazy
        /// resolver. Most implementations leave this empty and let the lazy
        /// resolver in their Update path do the work.
        /// </summary>
        void OnSceneDidLoad(Scene to);
    }
}
