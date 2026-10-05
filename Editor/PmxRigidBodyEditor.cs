using UnityEditor;
using UnityEngine;

namespace PmxImport.Editor
{
    [CustomEditor(typeof(PmxRigidBody))]
    public sealed class PmxRigidBodyEditor : UnityEditor.Editor
    {
        static readonly string[] Groups = {
            "Group 0", "Group 1", "Group 2", "Group 3", "Group 4", "Group 5", "Group 6", "Group 7",
            "Group 8", "Group 9", "Group 10", "Group 11", "Group 12", "Group 13", "Group 14", "Group 15"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"));
            var binding = serializedObject.FindProperty("binding");
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                foreach (string field in new[] { "name", "bone", "mode", "localPos", "localRot" })
                    EditorGUILayout.PropertyField(binding.FindPropertyRelative(field));
            var group = binding.FindPropertyRelative("group");
            group.intValue = EditorGUILayout.IntSlider("Collision Group", group.intValue, 0, 15);
            var mask = binding.FindPropertyRelative("nonCollisionMask");
            mask.intValue = EditorGUILayout.MaskField("Excluded PMX Groups", mask.intValue, Groups) & 0xffff;
            var rigid = (PmxRigidBody)target;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Unity Rigidbody", rigid.Body, typeof(Rigidbody), true);
                EditorGUILayout.ObjectField("Shape", rigid.Shape, typeof(Collider), true);
            }
            EditorGUILayout.HelpBox("PMXグループはUnityレイヤーとは独立です。質量・減衰は同じオブジェクトのRigidbody、形状はColliderで調整できます。", MessageType.Info);
            if (serializedObject.ApplyModifiedProperties() && Application.isPlaying && rigid.controller != null)
                rigid.controller.ReapplyCollisionFilter();
        }
    }
}
