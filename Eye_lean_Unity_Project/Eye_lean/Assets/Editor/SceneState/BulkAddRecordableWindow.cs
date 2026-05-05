using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EyeLean.SceneState;

namespace EyeLean.Editor.SceneState
{
    /// <summary>
    /// Editor window for bulk-attaching <see cref="Recordable"/> components to
    /// scene objects matching a tag/layer/has-collider filter. Each add is
    /// wrapped in <see cref="Undo.AddComponent{T}"/> so Ctrl-Z works
    /// per-object. Final pass audits duplicate ids.
    /// </summary>
    public class BulkAddRecordableWindow : EditorWindow
    {
        private string filterTag = "Untagged";
        private LayerMask filterLayer = ~0;
        private bool requireCollider = false;
        private bool requireRenderer = false;
        private bool excludeStaticEnvironment = true;
        private string[] commonStaticNames = new[] { "Floor", "Ceiling", "BackWall", "FrontWall", "LeftWall", "RightWall" };

        private List<GameObject> previewMatches = new List<GameObject>();
        private Vector2 scroll;

        [MenuItem("Tools/Eye Tracking/Scene State/Bulk Add Recordable...")]
        private static void Open()
        {
            var w = GetWindow<BulkAddRecordableWindow>(true, "Bulk Add Recordable", true);
            w.minSize = new Vector2(420, 360);
            w.RecomputePreview();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Filter", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            filterTag = EditorGUILayout.TagField(new GUIContent("Tag"), filterTag);
            filterLayer = EditorGUILayout.MaskField(new GUIContent("Layer"), filterLayer, UnityEditorInternal.InternalEditorUtility.layers);
            requireCollider = EditorGUILayout.Toggle(new GUIContent("Require Collider"), requireCollider);
            requireRenderer = EditorGUILayout.Toggle(new GUIContent("Require Renderer"), requireRenderer);
            excludeStaticEnvironment = EditorGUILayout.Toggle(new GUIContent("Exclude common static names"), excludeStaticEnvironment);
            if (EditorGUI.EndChangeCheck()) RecomputePreview();

            if (GUILayout.Button("Refresh preview"))
            {
                RecomputePreview();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Preview matches ({previewMatches.Count})", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180));
            foreach (var go in previewMatches)
            {
                if (go == null) continue;
                EditorGUILayout.ObjectField(go, typeof(GameObject), true);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(previewMatches.Count == 0))
            {
                if (GUILayout.Button($"Add Recordable to {previewMatches.Count} matched object(s)"))
                {
                    AddRecordablesToMatches();
                }
            }

            if (GUILayout.Button("Audit duplicate ids in loaded scene(s)"))
            {
                AuditDuplicates();
            }
        }

        private void RecomputePreview()
        {
            previewMatches.Clear();
            var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var go in all)
            {
                if (go == null) continue;
                if (go.GetComponent<Recordable>() != null) continue;
                if (!string.IsNullOrEmpty(filterTag) && filterTag != "Untagged" && !go.CompareTag(filterTag)) continue;
                if (((1 << go.layer) & filterLayer.value) == 0) continue;
                if (requireCollider && go.GetComponent<Collider>() == null) continue;
                if (requireRenderer && go.GetComponent<Renderer>() == null) continue;
                if (excludeStaticEnvironment)
                {
                    bool skip = false;
                    for (int i = 0; i < commonStaticNames.Length; i++)
                    {
                        if (go.name == commonStaticNames[i]) { skip = true; break; }
                    }
                    if (skip) continue;
                }
                previewMatches.Add(go);
            }
        }

        private void AddRecordablesToMatches()
        {
            int added = 0;
            foreach (var go in previewMatches)
            {
                if (go == null) continue;
                if (go.GetComponent<Recordable>() != null) continue;
                Undo.AddComponent<Recordable>(go);
                added++;
            }
            Debug.Log($"[BulkAddRecordable] Added Recordable to {added} object(s).");
            RecomputePreview();
        }

        private void AuditDuplicates()
        {
            var seen = new Dictionary<string, Recordable>();
            int dups = 0;
            var all = Object.FindObjectsByType<Recordable>(FindObjectsSortMode.None);
            foreach (var r in all)
            {
                if (r == null || string.IsNullOrEmpty(r.UniqueId)) continue;
                if (seen.TryGetValue(r.UniqueId, out var prev) && prev != r)
                {
                    Debug.LogWarning($"[BulkAddRecordable] Duplicate id '{r.UniqueId}' on '{r.gameObject.name}' and '{prev.gameObject.name}'.", r);
                    dups++;
                }
                else
                {
                    seen[r.UniqueId] = r;
                }
            }
            Debug.Log($"[BulkAddRecordable] Audit complete. {all.Length} Recordables; {dups} duplicate(s) found.");
        }
    }
}
