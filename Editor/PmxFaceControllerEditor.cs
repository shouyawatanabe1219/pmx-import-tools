// SPDX-License-Identifier: MIT
using UnityEditor;
using UnityEngine;

namespace PmxImport.Editor
{
    [CustomEditor(typeof(PmxFaceController))]
    public class PmxFaceControllerEditor : UnityEditor.Editor
    {
        string search = "";
        public override void OnInspectorGUI()
        {
            var face = (PmxFaceController)target;
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "morphs");
            if (serializedObject.ApplyModifiedProperties()) face.Apply();
            search = EditorGUILayout.TextField("モーフ検索", search);
            if (GUILayout.Button("表情をリセット")) { Undo.RecordObject(face, "Reset PMX expressions"); face.ResetAll(); EditorUtility.SetDirty(face); }
            foreach (PmxMorphPanel panel in System.Enum.GetValues(typeof(PmxMorphPanel)))
            {
                bool heading = false;
                foreach (var morph in face.morphs)
                {
                    if (morph == null || morph.panel != panel || (!string.IsNullOrEmpty(search) && (morph.name ?? "").IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    if (!heading) { EditorGUILayout.LabelField(panel.ToString(), EditorStyles.boldLabel); heading = true; }
                    EditorGUI.BeginChangeCheck(); float value = EditorGUILayout.Slider(morph.name + (morph.isGroup ? " (Group)" : ""), morph.weight, 0, 1);
                    if (!EditorGUI.EndChangeCheck()) continue;
                    Undo.RecordObject(face, "Set PMX expression"); morph.weight = value; face.Apply(); EditorUtility.SetDirty(face); SceneView.RepaintAll();
                }
            }
        }
    }
}
