using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EyeLean.SceneState;

namespace EyeLean.Editor.SceneState
{
    /// <summary>
    /// Custom inspector for <see cref="Recordable"/>. Surfaces the GUID
    /// read-only with a Regenerate button and warns if any other Recordable
    /// in the loaded scene shares the same id (catches edit-time duplicate-
    /// prefab problems before they hit a recording).
    /// </summary>
    [CustomEditor(typeof(Recordable))]
    [CanEditMultipleObjects]
    public class RecordableEditor : UnityEditor.Editor
    {
        private SerializedProperty uniqueIdProp;

        private void OnEnable()
        {
            uniqueIdProp = serializedObject.FindProperty("uniqueId");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(uniqueIdProp, new GUIContent("Unique Id"));
            }

            if (GUILayout.Button("Regenerate Id"))
            {
                foreach (var t in targets)
                {
                    var rec = t as Recordable;
                    if (rec == null) continue;
                    Undo.RecordObject(rec, "Regenerate Recordable Id");
                    rec.RegenerateId();
                    EditorUtility.SetDirty(rec);
                }
            }

            // Duplicate detection across the loaded scene(s).
            var dup = FindDuplicateIdInScene();
            if (dup != null)
            {
                EditorGUILayout.HelpBox(
                    $"Duplicate id found on '{dup.gameObject.name}'. " +
                    "Click Regenerate Id on this or that one to fix. " +
                    "Cause is usually pasting the same Recordable into multiple scenes.",
                    MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private Recordable FindDuplicateIdInScene()
        {
            var self = target as Recordable;
            if (self == null || string.IsNullOrEmpty(self.UniqueId)) return null;
            var all = Object.FindObjectsByType<Recordable>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != self && all[i].UniqueId == self.UniqueId) return all[i];
            }
            return null;
        }
    }
}
