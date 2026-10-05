// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace PmxImport.Editor
{
    [ScriptedImporter(7, "pmx")]
    public class PmxImporter : ScriptedImporter
    {
        [Min(.000001f)] public float scale = .08f;
        public bool importBlendShapes = true, addFaceController = true, importPhysics = true, importJoints = true;
        public bool useMmdToonShader = true, alphaClipFromTexture = true;
        public float edgePixelScale = 1.5f;
        public bool extractMaterialsOnImport = true;

        Vector3 Position(Vector3 value) => new Vector3(value.x, value.y, -value.z) * scale;
        static Quaternion Orientation(Vector3 radians) => Quaternion.Euler(new Vector3(-radians.x, -radians.y, radians.z) * Mathf.Rad2Deg);
        static string DisplayName(string name, int index, string prefix) => string.IsNullOrEmpty(name) ? prefix + index : name.Replace('/', '_').Replace('\\', '_');

        public override void OnImportAsset(AssetImportContext context)
        {
            GameObject root = null;
            try
            {
                if (scale <= 0 || float.IsInfinity(scale) || float.IsNaN(scale)) throw new InvalidDataException("PMX scale must be finite and positive.");
                var document = PmxDocument.Load(context.assetPath);
                root = new GameObject(Path.GetFileNameWithoutExtension(context.assetPath)); root.SetActive(false);
                Transform[] bones = BuildBones(root.transform, document.bones);
                Mesh mesh = BuildMesh(document, bones, root.transform);
                context.AddObjectToAsset("mesh", mesh);
                var meshObject = new GameObject("Mesh"); meshObject.transform.SetParent(root.transform, false);
                var renderer = meshObject.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.bones = bones;
                renderer.rootBone = bones.Length > 0 ? bones[0] : root.transform; renderer.updateWhenOffscreen = true;
                var remaps = GetExternalObjectMap(); var materials = new Material[document.surfaces.Length];
                var names = new HashSet<string>();
                for (int i = 0; i < materials.Length; i++)
                {
                    var surface = document.surfaces[i]; string name = DisplayName(surface.name, i, "Material_"); if (!names.Add(name)) name += "_" + i;
                    var material = BuildMaterial(context, document, surface); material.name = name;
                    context.AddObjectToAsset("material_" + i, material);
                    var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                    materials[i] = remaps.TryGetValue(key, out UnityEngine.Object mapped) && mapped is Material external ? external : material;
                }
                renderer.sharedMaterials = materials;
                if (importBlendShapes && addFaceController)
                {
                    var face = root.AddComponent<PmxFaceController>(); face.target = renderer;
                    for (int i = 0; i < document.morphs.Length; i++)
                    {
                        var morph = document.morphs[i]; if (morph.kind != 0 && morph.kind != 1) continue;
                        var entry = new PmxMorphInfo { name = MorphName(document, i), panel = (PmxMorphPanel)Mathf.Clamp(morph.panel, 0, 4), isGroup = morph.kind == 0, blendShape = morph.kind == 1 ? MorphName(document, i) : "" };
                        if (entry.isGroup)
                        {
                            var weights = new Dictionary<int, float>(); Flatten(document, i, 1, new HashSet<int>(), weights);
                            foreach (var item in weights) entry.items.Add(new PmxGroupItem { blendShape = MorphName(document, item.Key), rate = item.Value });
                        }
                        face.morphs.Add(entry);
                    }
                }
                if (importPhysics && document.bodies.Length != 0) BuildPhysics(context, root, bones, document);
                root.SetActive(true); context.AddObjectToAsset("root", root); context.SetMainObject(root);
                if (extractMaterialsOnImport) PmxMaterialExtractor.Queue(context.assetPath);
            }
            catch (Exception error)
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                context.LogImportError("PMX import failed: " + error.Message);
            }
        }

        Transform[] BuildBones(Transform root, PmxDocument.Bone[] records)
        {
            Transform container = root;
            if (records.Length == 0) return new[] { container };
            var bones = new Transform[records.Length];
            for (int i = 0; i < bones.Length; i++) { bones[i] = new GameObject(DisplayName(records[i].name, i, "Bone_")).transform; bones[i].SetParent(container, false); bones[i].localPosition = Position(records[i].position); }
            for (int i = 0; i < bones.Length; i++)
            {
                int parent = records[i].parent;
                if (parent < 0 || parent >= bones.Length || parent == i) continue;
                bool cycle = false; int cursor = parent;
                for (int n = 0; n <= bones.Length && cursor >= 0 && cursor < bones.Length; n++) { if (cursor == i || n == bones.Length) { cycle = true; break; } cursor = records[cursor].parent; }
                if (!cycle) bones[i].SetParent(bones[parent], true);
            }
            return bones;
        }

        Mesh BuildMesh(PmxDocument document, Transform[] bones, Transform root)
        {
            var mesh = new Mesh { name = "PMX Mesh", indexFormat = document.vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = document.vertices.Select(v => Position(v.position)).ToArray();
            mesh.normals = document.vertices.Select(v => new Vector3(v.normal.x, v.normal.y, -v.normal.z).normalized).ToArray();
            mesh.uv = document.vertices.Select(v => new Vector2(v.uv.x, 1 - v.uv.y)).ToArray();
            mesh.colors = document.vertices.Select(v => new Color(1, 1, 1, Mathf.Max(0, v.edge))).ToArray();
            int extra = document.vertices.Length > 0 ? document.vertices[0].additional.Length : 0;
            for (int channel = 0; channel < extra; channel++) mesh.SetUVs(channel + 1, document.vertices.Select(v => v.additional[channel]).ToList());
            mesh.boneWeights = document.vertices.Select(v => NormalizeSkin(v.skin, bones.Length)).ToArray();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * root.localToWorldMatrix).ToArray();
            var triangles = (int[])document.indices.Clone(); for (int i = 0; i < triangles.Length; i += 3) { int value = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = value; }
            mesh.subMeshCount = document.surfaces.Length;
            int offset = 0;
            for (int i = 0; i < document.surfaces.Length; i++)
            {
                int count = document.surfaces[i].indices;
                if (count < 0 || count % 3 != 0 || count > triangles.Length - offset) throw new InvalidDataException("PMX material surface range is invalid.");
                mesh.SetTriangles(triangles, offset, count, i, false); offset += count;
            }
            if (offset != triangles.Length) throw new InvalidDataException("PMX materials do not cover all faces.");
            if (importBlendShapes)
                for (int i = 0; i < document.morphs.Length; i++)
                {
                    if (document.morphs[i].kind != 1) continue;
                    var deltas = new Vector3[document.vertices.Length];
                    foreach (var item in document.morphs[i].offsets)
                    { if (item.index < 0 || item.index >= deltas.Length) throw new InvalidDataException("Invalid morph vertex index."); deltas[item.index] += Position(item.delta); }
                    mesh.AddBlendShapeFrame(MorphName(document, i), 100, deltas, null, null);
                }
            mesh.RecalculateBounds(); return mesh;
        }

        static BoneWeight NormalizeSkin(BoneWeight weight, int count)
        {
            int[] indices = { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
            float[] values = { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
            float total = 0;
            for (int i = 0; i < 4; i++) { if (indices[i] < 0 || indices[i] >= count) { indices[i] = 0; values[i] = 0; } values[i] = Mathf.Max(0, values[i]); total += values[i]; }
            if (total < .000001f) { values[0] = 1; total = 1; }
            return new BoneWeight { boneIndex0 = indices[0], boneIndex1 = indices[1], boneIndex2 = indices[2], boneIndex3 = indices[3], weight0 = values[0] / total, weight1 = values[1] / total, weight2 = values[2] / total, weight3 = values[3] / total };
        }
        static string MorphName(PmxDocument data, int index)
        {
            string name = DisplayName(data.morphs[index].name, index, "Morph_");
            for (int earlier = 0; earlier < index; earlier++) if (DisplayName(data.morphs[earlier].name, earlier, "Morph_") == name) return name + "_" + index;
            return name;
        }
        static void Flatten(PmxDocument data, int index, float weight, HashSet<int> visiting, Dictionary<int, float> output)
        {
            if (index < 0 || index >= data.morphs.Length || !visiting.Add(index)) return;
            var morph = data.morphs[index];
            if (morph.kind == 1) { output.TryGetValue(index, out float previous); output[index] = previous + weight; }
            else if (morph.kind == 0) foreach (var item in morph.offsets) Flatten(data, item.index, weight * item.influence, visiting, output);
            visiting.Remove(index);
        }

        Texture2D Texture(AssetImportContext context, PmxDocument document, int index)
        {
            if (index < 0 || index >= document.textures.Length) return null;
            string entry = document.textures[index].Replace('\\', '/');
            if (Path.IsPathRooted(entry)) { context.LogImportWarning("Absolute texture path is ignored: " + Path.GetFileName(entry)); return null; }
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(context.assetPath), entry));
            string project = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(project, StringComparison.OrdinalIgnoreCase)) return null;
            string path = full.Substring(project.Length).Replace('\\', '/');
            context.DependsOnSourceAsset(path); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        Material BuildMaterial(AssetImportContext context, PmxDocument document, PmxDocument.Surface surface)
        {
            Shader shader = useMmdToonShader ? Shader.Find("PmxImport/MMD Toon") : null;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No compatible model shader was found.");
            var material = new Material(shader);
            SetColor(material, "_BaseColor", surface.diffuse); SetColor(material, "_Color", surface.diffuse);
            SetColor(material, "_SpecColor", surface.specular); SetColor(material, "_AmbientColor", surface.ambient);
            SetColor(material, "_EdgeColor", surface.outline);
            SetFloat(material, "_SpecPower", surface.shininess); SetFloat(material, "_EdgeScale", Mathf.Max(0, edgePixelScale));
            SetFloat(material, "_EdgeSize", (surface.flags & 16) != 0 ? Mathf.Max(0, surface.edge) : 0);
            SetFloat(material, "_Cull", (surface.flags & 1) != 0 ? 0 : 2);
            var texture = Texture(context, document, surface.texture);
            if (texture != null) { if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture); if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture); }
            var sphere = Texture(context, document, surface.sphere);
            if (sphere != null && material.HasProperty("_SphereTex")) material.SetTexture("_SphereTex", sphere);
            SetFloat(material, "_Sphere", sphere != null ? surface.sphereMode : 0);
            var toon = surface.sharedToon ? null : Texture(context, document, surface.toon);
            if (toon != null && material.HasProperty("_ToonTex")) material.SetTexture("_ToonTex", toon);
            SetFloat(material, "_UseToonTexture", toon != null ? 1 : 0);
            bool transparent = surface.diffuse.a < .999f;
            bool clip = !transparent && alphaClipFromTexture && texture != null && UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat);
            SetFloat(material, "_AlphaClip", clip ? 1 : 0); SetFloat(material, "_Cutoff", .3f);
            if (clip) material.EnableKeyword("_ALPHATEST_ON");
            SetFloat(material, "_Surface", transparent ? 1 : 0); SetFloat(material, "_ZWrite", transparent ? 0 : 1);
            SetFloat(material, "_SrcBlend", transparent ? (int)BlendMode.SrcAlpha : (int)BlendMode.One);
            SetFloat(material, "_DstBlend", transparent ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.Zero);
            material.renderQueue = transparent ? (int)RenderQueue.Transparent : clip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : clip ? "TransparentCutout" : "Opaque");
            if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }
        static void SetFloat(Material material, string property, float value) { if (material.HasProperty(property)) material.SetFloat(property, value); }
        static void SetColor(Material material, string property, Color value) { if (material.HasProperty(property)) material.SetColor(property, value); }

        void BuildPhysics(AssetImportContext context, GameObject root, Transform[] bones, PmxDocument document)
        {
            var controller = root.AddComponent<PmxPhysicsController>();
            var container = new GameObject("Physics").transform; container.SetParent(root.transform, false); controller.physicsRoot = container;
            var bodies = new Rigidbody[document.bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                var record = document.bodies[i]; if (record.group > 15 || record.shape > 2 || record.mode > 2) throw new InvalidDataException("Invalid PMX rigid-body setting.");
                var obj = new GameObject("Rigid_" + i + "_" + DisplayName(record.name, i, "Body_")); obj.transform.SetParent(container, false);
                obj.transform.localPosition = Position(record.position); obj.transform.localRotation = Orientation(record.radians);
                Vector3 dimensions = new Vector3(Mathf.Abs(record.size.x), Mathf.Abs(record.size.y), Mathf.Abs(record.size.z)) * scale;
                Collider shape;
                if (record.shape == 0) { var sphere = obj.AddComponent<SphereCollider>(); sphere.radius = Mathf.Max(.0001f, dimensions.x); shape = sphere; }
                else if (record.shape == 1) { var box = obj.AddComponent<BoxCollider>(); box.size = Vector3.Max(dimensions * 2, Vector3.one * .0001f); shape = box; }
                else { var capsule = obj.AddComponent<CapsuleCollider>(); capsule.radius = Mathf.Max(.0001f, dimensions.x); capsule.height = Mathf.Max(capsule.radius * 2, dimensions.y + capsule.radius * 2); capsule.direction = 1; shape = capsule; }
                var physicsMaterial = new PhysicsMaterial("PMX Body " + i) { dynamicFriction = Mathf.Max(0, record.friction), staticFriction = Mathf.Max(0, record.friction), bounciness = Mathf.Clamp01(record.bounce) };
                context.AddObjectToAsset("physics_material_" + i, physicsMaterial); shape.sharedMaterial = physicsMaterial;
                var body = obj.AddComponent<Rigidbody>(); bodies[i] = body; body.mass = Mathf.Max(.0001f, record.mass);
                body.isKinematic = record.mode == 0 || record.mass <= 0; body.useGravity = !body.isKinematic;
                body.linearDamping = Attenuation(record.movementDamping); body.angularDamping = Attenuation(record.rotationDamping);
                body.interpolation = RigidbodyInterpolation.Interpolate;
                Transform bone = record.bone >= 0 && record.bone < document.bones.Length ? bones[record.bone] : null;
                Transform frame = bone != null ? bone : root.transform;
                var component = obj.AddComponent<PmxRigidBody>(); component.controller = controller;
                component.binding = new PmxRigidBinding { name = record.name, body = body, collider = shape, bone = bone, mode = (PmxRigidMode)record.mode, group = record.group, nonCollisionMask = (~record.allowedGroups) & 0xffff, localPos = frame.InverseTransformPoint(obj.transform.position), localRot = Quaternion.Inverse(frame.rotation) * obj.transform.rotation };
                controller.rigidBodies.Add(component); controller.rigids.Add(component.binding);
            }
            if (importJoints)
                for (int i = 0; i < document.connections.Length; i++)
                {
                    var connection = document.connections[i];
                    if (connection.kind > 1) { context.LogImportWarning("Unsupported PMX joint type: " + connection.kind); continue; }
                    if (connection.first < 0 || connection.first >= bodies.Length || connection.second < 0 || connection.second >= bodies.Length || connection.first == connection.second) { context.LogImportWarning("Invalid joint body reference: " + connection.name); continue; }
                    BuildJoint(root.transform, bodies[connection.first], bodies[connection.second], connection);
                }
        }
        static float Attenuation(float damping) => -Mathf.Log(1 - Mathf.Clamp(damping, 0, .9999f));
        void BuildJoint(Transform root, Rigidbody first, Rigidbody second, PmxDocument.Connection record)
        {
            var joint = first.gameObject.AddComponent<ConfigurableJoint>(); joint.connectedBody = second; joint.autoConfigureConnectedAnchor = false;
            Vector3 pivot = root.TransformPoint(Position(record.position)); Quaternion axes = root.rotation * Orientation(record.radians);
            joint.anchor = first.transform.InverseTransformPoint(pivot); joint.connectedAnchor = second.transform.InverseTransformPoint(pivot);
            joint.axis = first.transform.InverseTransformDirection(axes * Vector3.right); joint.secondaryAxis = first.transform.InverseTransformDirection(axes * Vector3.up);
            joint.enableCollision = true; joint.enablePreprocessing = false;
            Vector3 low = new Vector3(record.translationLow.x, record.translationLow.y, -record.translationHigh.z) * scale;
            Vector3 high = new Vector3(record.translationHigh.x, record.translationHigh.y, -record.translationLow.z) * scale;
            joint.xMotion = Motion(low.x, high.x); joint.yMotion = Motion(low.y, high.y); joint.zMotion = Motion(low.z, high.z);
            float radius = 0; for (int axis = 0; axis < 3; axis++) if (low[axis] <= high[axis]) radius += Mathf.Pow(Mathf.Max(Mathf.Abs(low[axis]), Mathf.Abs(high[axis])), 2);
            joint.linearLimit = Limit(Mathf.Sqrt(radius));
            Vector3 angularLow = new Vector3(-record.rotationHigh.x, -record.rotationHigh.y, record.rotationLow.z) * Mathf.Rad2Deg;
            Vector3 angularHigh = new Vector3(-record.rotationLow.x, -record.rotationLow.y, record.rotationHigh.z) * Mathf.Rad2Deg;
            joint.angularXMotion = Motion(angularLow.x, angularHigh.x); joint.angularYMotion = Motion(angularLow.y, angularHigh.y); joint.angularZMotion = Motion(angularLow.z, angularHigh.z);
            joint.lowAngularXLimit = Limit(Mathf.Clamp(angularLow.x, -177, 177)); joint.highAngularXLimit = Limit(Mathf.Clamp(angularHigh.x, -177, 177));
            joint.angularYLimit = Limit(Mathf.Min(177, Mathf.Max(Mathf.Abs(angularLow.y), Mathf.Abs(angularHigh.y)))); joint.angularZLimit = Limit(Mathf.Min(177, Mathf.Max(Mathf.Abs(angularLow.z), Mathf.Abs(angularHigh.z))));
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;
            if (record.kind == 0)
            {
                joint.xDrive = Spring(record.translationSpring.x); joint.yDrive = Spring(record.translationSpring.y); joint.zDrive = Spring(record.translationSpring.z);
                joint.angularXDrive = Spring(record.rotationSpring.x * scale * scale); joint.angularYZDrive = Spring(Mathf.Max(record.rotationSpring.y, record.rotationSpring.z) * scale * scale);
            }
            var metadata = first.gameObject.AddComponent<PmxJointMetadata>(); metadata.joint = joint; metadata.jointName = record.name; metadata.type = record.kind;
            metadata.linearMin = low; metadata.linearMax = high; metadata.angularMin = angularLow; metadata.angularMax = angularHigh; metadata.linearSpring = record.translationSpring; metadata.angularSpring = record.rotationSpring;
        }
        static ConfigurableJointMotion Motion(float low, float high) => low > high ? ConfigurableJointMotion.Free : Mathf.Abs(low) < .000001f && Mathf.Abs(high) < .000001f ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
        static SoftJointLimit Limit(float value) => new SoftJointLimit { limit = value };
        static JointDrive Spring(float stiffness) => new JointDrive { positionSpring = Mathf.Max(0, stiffness), maximumForce = float.MaxValue };
    }

    internal static class PmxMaterialExtractor
    {
        static readonly HashSet<string> queued = new HashSet<string>();
        public static void Queue(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !queued.Add(path)) return;
            EditorApplication.delayCall += () => { try { Extract(path); } finally { queued.Remove(path); } };
        }
        public static int Extract(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as PmxImporter; if (importer == null) return 0;
            string folder = Path.GetDirectoryName(path).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(path) + "_Materials";
            var mapped = importer.GetExternalObjectMap(); int count = 0;
            foreach (var material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name); if (mapped.ContainsKey(key)) continue;
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileNameWithoutExtension(path) + "_Materials");
                string safe = material.name; foreach (char character in Path.GetInvalidFileNameChars()) safe = safe.Replace(character, '_');
                string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + safe + ".mat");
                var copy = new Material(material) { name = material.name }; AssetDatabase.CreateAsset(copy, destination); importer.AddRemap(key, copy); count++;
            }
            if (count > 0) { EditorUtility.SetDirty(importer); AssetDatabase.SaveAssets(); importer.SaveAndReimport(); }
            return count;
        }
        public static void ClearRemaps(PmxImporter importer)
        {
            foreach (var key in importer.GetExternalObjectMap().Keys.ToArray()) if (key.type == typeof(Material)) importer.RemoveRemap(key);
            EditorUtility.SetDirty(importer); importer.SaveAndReimport();
        }
    }
    [CustomEditor(typeof(PmxImporter))]
    public class PmxImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var importer = (PmxImporter)target;
            if (GUILayout.Button("マテリアルを抽出")) PmxMaterialExtractor.Extract(importer.assetPath);
            if (GUILayout.Button("マテリアルの外部参照を解除")) PmxMaterialExtractor.ClearRemaps(importer);
        }
    }
}
