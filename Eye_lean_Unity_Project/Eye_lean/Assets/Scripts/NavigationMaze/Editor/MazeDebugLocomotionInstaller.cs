// SPDX-License-Identifier: MIT
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EyeLean.NavigationMaze.DebugTools;

namespace EyeLean.NavigationMaze.EditorTools
{
    /// <summary>
    /// One-click installer for the editor-only WASD locomotion helper.
    /// Adds an <see cref="EditorDebugLocomotion"/> component to the camera's
    /// parent transform (the XR rig root, if present) so Mac-editor testers
    /// can navigate the maze without an HMD. Idempotent — re-running on a
    /// scene that already has the component is a no-op.
    /// </summary>
    public static class MazeDebugLocomotionInstaller
    {
        [MenuItem("Eye_lean/Debug/Add Editor Locomotion to Current Scene")]
        public static void Install()
        {
            var existing = Object.FindFirstObjectByType<EditorDebugLocomotion>();
            if (existing != null)
            {
                EditorUtility.DisplayDialog("Editor Locomotion",
                    "EditorDebugLocomotion is already present on '" + existing.gameObject.name + "'.\n\nWASD to move, right-click + drag to look, F1 to toggle.",
                    "OK");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                EditorUtility.DisplayDialog("Editor Locomotion",
                    "No Camera.main in the active scene. Open the Maze scene first.",
                    "OK");
                return;
            }

            // Prefer the camera's parent (the XR rig root, if present);
            // moving the camera itself would fight XR's per-frame pose
            // update. Falls back to the camera transform on non-XR setups.
            Transform host = cam.transform.parent != null ? cam.transform.parent : cam.transform;
            var added = host.gameObject.AddComponent<EditorDebugLocomotion>();
            EditorUtility.SetDirty(host.gameObject);
            EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            Selection.activeGameObject = host.gameObject;

            EditorUtility.DisplayDialog("Editor Locomotion",
                "Added EditorDebugLocomotion to '" + host.gameObject.name + "'.\n\nWASD to move, right-click + drag to look, F1 to toggle.\n\nThis component is compiled out of player builds (#if UNITY_EDITOR).",
                "OK");
        }
    }
}
