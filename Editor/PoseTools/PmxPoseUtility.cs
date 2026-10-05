using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PmxImport.Editor
{
    public static class PmxPoseUtility
    {
        public static Transform[] Bones(GameObject root)
        {
            var mesh = root != null ? root.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (mesh == null || mesh.sharedMesh == null || mesh.bones.Length == 0)
                throw new InvalidOperationException("ボーン付きPMXモデルを指定してください。");
            var bones = mesh.bones.Where(t => t != null).Distinct().ToArray();
            if (bones.Any(t => t == root.transform || !t.IsChildOf(root.transform)))
                throw new InvalidOperationException("モデルのルートを指定してください。");
            var paths = bones.Select(t => AnimationUtility.CalculateTransformPath(t, root.transform)).ToArray();
            if (paths.Distinct().Count() != paths.Length)
                throw new InvalidOperationException("同じパスのボーンがあるため記録できません。");
            return bones;
        }

        public static void Initialize(PmxPoseDocument doc, GameObject root)
        {
            var bones = Bones(root);
            var mesh = root.GetComponentInChildren<SkinnedMeshRenderer>();
            doc.bonePaths = bones.Select(t => AnimationUtility.CalculateTransformPath(t, root.transform)).ToArray();
            doc.meshPath = AnimationUtility.CalculateTransformPath(mesh.transform, root.transform);
            doc.expressionNames = Enumerable.Range(0, mesh.sharedMesh.blendShapeCount)
                .Select(mesh.sharedMesh.GetBlendShapeName).ToArray();
            doc.keys.Clear();
            Record(doc, root, 0);
        }

        public static void Validate(PmxPoseDocument doc, GameObject root)
        {
            if (doc == null || root == null) throw new InvalidOperationException("モデルと作業データを指定してください。");
            if (doc.frameRate < 1 || doc.frameRate > 120 || doc.endFrame < 1 || doc.endFrame > 3600)
                throw new InvalidOperationException("FPSは1〜120、終了フレームは1〜3600にしてください。");
            if (doc.bonePaths.Length == 0 || doc.bonePaths.Distinct().Count() != doc.bonePaths.Length ||
                doc.bonePaths.Any(p => string.IsNullOrEmpty(p) || root.transform.Find(p) == null))
                throw new InvalidOperationException("作業データのボーン構造がこのモデルと一致しません。");
            var mesh = Mesh(doc, root);
            if (mesh == null || mesh.sharedMesh == null ||
                doc.expressionNames.Any(n => mesh.sharedMesh.GetBlendShapeIndex(n) < 0))
                throw new InvalidOperationException("作業データの表情がこのモデルと一致しません。");
            if (doc.keys.Count == 0 || doc.keys.Select(k => k.frame).Distinct().Count() != doc.keys.Count)
                throw new InvalidOperationException("キーフレームが不正です。");
            foreach (var k in doc.keys)
            {
                if (k.frame < 0 || k.frame > doc.endFrame || k.positions == null || k.rotations == null ||
                    k.scales == null || k.expressions == null || k.positions.Length != doc.bonePaths.Length ||
                    k.rotations.Length != doc.bonePaths.Length || k.scales.Length != doc.bonePaths.Length ||
                    k.expressions.Length != doc.expressionNames.Length)
                    throw new InvalidOperationException("キーの長さまたはフレーム範囲が不正です。");
                for (int i = 0; i < k.positions.Length; i++)
                {
                    var q = k.rotations[i];
                    if (!Finite(k.positions[i]) || !Finite(k.scales[i]) || !Finite(new Vector3(q.x, q.y, q.z)) ||
                        !Finite(q.w) || Quaternion.Dot(q, q) < 0.0001f)
                        throw new InvalidOperationException("ポーズに不正な数値があります。");
                }
                if (k.expressions.Any(v => !Finite(v))) throw new InvalidOperationException("表情に不正な数値があります。");
            }
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        public static SkinnedMeshRenderer Mesh(PmxPoseDocument doc, GameObject root) =>
            root.transform.Find(doc.meshPath)?.GetComponent<SkinnedMeshRenderer>();

        public static PmxPoseKey Capture(PmxPoseDocument doc, GameObject root, int frame)
        {
            var bones = doc.bonePaths.Select(p => root.transform.Find(p)).ToArray();
            var mesh = Mesh(doc, root);
            return new PmxPoseKey {
                frame = frame, positions = bones.Select(t => t.localPosition).ToArray(),
                rotations = bones.Select(t => t.localRotation).ToArray(), scales = bones.Select(t => t.localScale).ToArray(),
                expressions = doc.expressionNames.Select(n => mesh.GetBlendShapeWeight(mesh.sharedMesh.GetBlendShapeIndex(n))).ToArray()
            };
        }

        public static void Record(PmxPoseDocument doc, GameObject root, int frame)
        {
            if (frame < 0 || frame > doc.endFrame) throw new ArgumentOutOfRangeException(nameof(frame));
            var key = Capture(doc, root, frame);
            doc.keys.RemoveAll(k => k.frame == frame);
            doc.keys.Add(key);
            doc.keys.Sort((a, b) => a.frame.CompareTo(b.frame));
            EditorUtility.SetDirty(doc);
        }

        public static PmxPoseKey Evaluate(PmxPoseDocument doc, float frame)
        {
            var keys = doc.keys.OrderBy(k => k.frame).ToArray();
            if (keys.Length == 0) throw new InvalidOperationException("キーがありません。");
            var a = keys[0]; var b = keys[keys.Length - 1];
            if (frame <= a.frame) b = a;
            else if (frame >= b.frame) a = b;
            else
                for (int i = 1; i < keys.Length; i++)
                    if (frame <= keys[i].frame) { a = keys[i - 1]; b = keys[i]; break; }
            float t = a == b ? 0f : (frame - a.frame) / (b.frame - a.frame);
            var result = new PmxPoseKey {
                positions = new Vector3[a.positions.Length], rotations = new Quaternion[a.rotations.Length],
                scales = new Vector3[a.scales.Length], expressions = new float[a.expressions.Length]
            };
            for (int i = 0; i < result.positions.Length; i++)
            {
                result.positions[i] = Vector3.Lerp(a.positions[i], b.positions[i], t);
                result.rotations[i] = Quaternion.Slerp(a.rotations[i], b.rotations[i], t);
                result.scales[i] = Vector3.Lerp(a.scales[i], b.scales[i], t);
            }
            for (int i = 0; i < result.expressions.Length; i++)
                result.expressions[i] = Mathf.Lerp(a.expressions[i], b.expressions[i], t);
            return result;
        }

        public static void Apply(PmxPoseDocument doc, GameObject root, PmxPoseKey key)
        {
            for (int i = 0; i < doc.bonePaths.Length; i++)
            {
                var bone = root.transform.Find(doc.bonePaths[i]);
                if (bone == null) continue;
                bone.localPosition = key.positions[i]; bone.localRotation = key.rotations[i]; bone.localScale = key.scales[i];
            }
            var mesh = Mesh(doc, root);
            if (mesh == null || mesh.sharedMesh == null) return;
            for (int i = 0; i < doc.expressionNames.Length; i++)
            {
                int index = mesh.sharedMesh.GetBlendShapeIndex(doc.expressionNames[i]);
                if (index >= 0) mesh.SetBlendShapeWeight(index, key.expressions[i]);
            }
        }

        public static AnimationClip Export(PmxPoseDocument doc, GameObject root)
        {
            Validate(doc, root);
            var clip = new AnimationClip { name = doc.name, frameRate = doc.frameRate };
            var dynamicBones = new HashSet<Transform>();
            if (doc.preservePhysics)
            {
                var physics = root.GetComponent<PmxPhysicsController>();
                if (physics != null)
                {
                    foreach (var body in physics.rigidBodies)
                        if (body != null && body.binding.bone != null && body.binding.mode != PmxRigidMode.FollowBone)
                            dynamicBones.Add(body.binding.bone);
                    foreach (var body in physics.rigids)
                        if (body.bone != null && body.mode != PmxRigidMode.FollowBone) dynamicBones.Add(body.bone);
                }
            }
            var samples = Enumerable.Range(0, doc.endFrame + 1).Select(f => Evaluate(doc, f)).ToArray();
            for (int i = 0; i < doc.bonePaths.Length; i++)
            {
                if (dynamicBones.Contains(root.transform.Find(doc.bonePaths[i]))) continue;
                int bone = i;
                string path = doc.bonePaths[i];
                foreach (string property in new[] { "m_LocalPosition", "m_LocalRotation", "m_LocalScale" })
                {
                    int dimensions = property == "m_LocalRotation" ? 4 : 3;
                    for (int axis = 0; axis < dimensions; axis++)
                    {
                        int component = axis;
                        Curve(clip, path, typeof(Transform), property + "." + "xyzw"[axis], samples, doc.frameRate,
                            k => property == "m_LocalRotation" ? k.rotations[bone][component] :
                                property == "m_LocalPosition" ? k.positions[bone][component] : k.scales[bone][component]);
                    }
                }
            }
            for (int i = 0; i < doc.expressionNames.Length; i++)
            {
                int expression = i;
                Curve(clip, doc.meshPath, typeof(SkinnedMeshRenderer), "blendShape." + doc.expressionNames[i],
                    samples, doc.frameRate, k => k.expressions[expression]);
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = doc.loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        static void Curve(AnimationClip clip, string path, Type type, string property, PmxPoseKey[] samples,
            int fps, Func<PmxPoseKey, float> value)
        {
            var values = samples.Select(value).ToArray();
            bool constant = values.All(v => Mathf.Abs(v - values[0]) < 0.000001f);
            var curve = constant ? new AnimationCurve(new Keyframe(0f, values[0]),
                new Keyframe((float)(samples.Length - 1) / fps, values[0])) :
                new AnimationCurve(values.Select((v, f) => new Keyframe((float)f / fps, v)).ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }
    }
}
