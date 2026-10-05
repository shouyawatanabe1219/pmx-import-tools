// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using PmxImport;
using PmxImport.Editor;

public static class PmxValidation
{
    static int checks;
    static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        checks++;
    }
    static void Near(float actual, float expected, string label) => Check(Mathf.Abs(actual - expected) < 0.00001f, label);
    static void Call(PmxPhysicsController c, string name) => typeof(PmxPhysicsController)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, null);
    static PmxImporter Reimport(PmxImporter importer)
    {
        string path = importer.assetPath;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        return (PmxImporter)AssetImporter.GetAtPath(path);
    }

    [MenuItem("Tools/PMX/Tests/Core Regression")]
    public static void Run()
    {
        if(Directory.Exists("Assets/__PmxToolsGeneratedTests")) throw new InvalidOperationException("Test folder already exists; choose a fresh test project.");
        checks = 0;
        try
        {
            Directory.CreateDirectory("Assets/__PmxToolsGeneratedTests");
            foreach (int width in new[] { 1, 2, 4 })
            foreach (bool utf8 in new[] { false, true })
            {
                string path = "Assets/__PmxToolsGeneratedTests/model_" + width + "_" + utf8 + ".pmx";
                WriteFixture(path, width, utf8);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (PmxImporter)AssetImporter.GetAtPath(path);
                Check(importer != null, "importer");
                importer.extractMaterialsOnImport = false;
                importer.useMmdToonShader = false;
                importer.importPhysics = true;
                importer.importJoints = true;
                importer.importBlendShapes = true;
                importer = Reimport(importer);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(model != null, "root");
                var physics = model.GetComponent<PmxPhysicsController>();
                Check(physics != null && physics.rigids.Count == 3, "rigid count");
                Check(physics.rigidBodies.Count == 3 && model.GetComponentsInChildren<PmxRigidBody>().Length == 3, "PMX components generated");
                Check(physics.rigidBodies.All(r => r.controller == physics && r.Body == r.binding.body && r.Shape == r.binding.collider), "component ownership");
                Check(physics.rigidBodies.All(r => r.gameObject.layer == 0), "no Unity layers consumed");
                Check(physics.rigids[0].collider is SphereCollider, "sphere");
                Check(physics.rigids[1].collider is BoxCollider, "box");
                Check(physics.rigids[2].collider is CapsuleCollider, "capsule");
                Near(((SphereCollider)physics.rigids[0].collider).radius, 0.08f, "radius");
                Near(((BoxCollider)physics.rigids[1].collider).size.y, 0.32f, "half extents");
                Near(((CapsuleCollider)physics.rigids[2].collider).height, 0.32f, "capsule height");
                Check(physics.rigids[0].body.isKinematic && !physics.rigids[1].body.isKinematic, "modes");
                Check(physics.rigids[2].mode == PmxRigidMode.PhysicsWithBone && physics.rigids[2].bone == null, "unbound mode2");
                Check(physics.rigids[1].nonCollisionMask == 1, "mask");
                Check(physics.rigids[0].nonCollisionMask == 0, "file FFFF permits every group");
                Check(physics.rigids[2].nonCollisionMask == 0xffff, "file zero excludes every group");
                Near(physics.rigids[1].body.mass, 2f, "mass");
                Near(physics.rigids[1].body.linearDamping, -Mathf.Log(0.8f), "damping");
                Near(physics.rigids[1].collider.sharedMaterial.bounciness, 0.3f, "restitution");
                Near(physics.rigids[1].collider.sharedMaterial.dynamicFriction, 0.4f, "friction");
                Check(AssetDatabase.LoadAllAssetsAtPath(path).OfType<PhysicsMaterial>().Count() == 3, "material subassets");
                var joints = model.GetComponentsInChildren<ConfigurableJoint>();
                Check(joints.Length == 2, "joint count");
                Check(joints[0].connectedBody == physics.rigids[1].body, "connected body");
                Check(joints[0].xMotion == ConfigurableJointMotion.Locked, "linear lock");
                Near(joints[0].xDrive.positionSpring, 10f, "spring");
                Check(joints[1].xDrive.positionSpring == 0f, "6dof without spring");
                Check(Vector3.Distance(joints[0].transform.TransformPoint(joints[0].anchor),
                    joints[0].connectedBody.transform.TransformPoint(joints[0].connectedAnchor)) < 0.00001f, "anchors");
                var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
                Check(smr.bones.Length == 2 && smr.bones[1].parent == smr.bones[0], "bone hierarchy");
                Check(smr.sharedMesh.vertexCount == 3 && smr.sharedMesh.blendShapeCount == 1, "mesh and morph");
                Check(smr.sharedMesh.triangles.SequenceEqual(new[] { 0, 2, 1 }), "winding");
                Near(smr.sharedMesh.vertices[1].x, 0.08f, "mesh scale");
                Check(model.GetComponent<PmxFaceController>().morphs.Count == 2, "face and group");
                string colorProperty = smr.sharedMaterials[0].HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                Color materialColor = smr.sharedMaterials[0].GetColor(colorProperty);
                Check(materialColor == new Color(0.2f, 0.4f, 0.6f, 1f), "material color");

                // Runtime synchronizer methods execute against real PhysX objects in a scratch scene.
                var instance = UnityEngine.Object.Instantiate(model);
                var c = instance.GetComponent<PmxPhysicsController>();
                Call(c, "Awake"); Call(c, "OnEnable");
                Check(Physics.GetIgnoreCollision(c.rigids[0].collider, c.rigids[1].collider), "collision filter");
                Check(object.ReferenceEquals(c.rigids[1], c.rigidBodies[1].binding), "component settings authoritative");
                c.rigidBodies[1].SetCollisionFilter(1, 0);
                Check(!Physics.GetIgnoreCollision(c.rigids[0].collider, c.rigids[1].collider), "clear exclusion");
                c.rigidBodies[1].SetCollisionFilter(1, 1);
                Check(Physics.GetIgnoreCollision(c.rigids[0].collider, c.rigids[1].collider), "restore exclusion");
                var replacement = c.rigidBodies[1].binding;
                c.rigidBodies[1].binding = new PmxRigidBinding {
                    name = replacement.name, bone = replacement.bone, mode = replacement.mode,
                    group = replacement.group, nonCollisionMask = 0,
                    localPos = replacement.localPos, localRot = replacement.localRot
                };
                c.ReapplyCollisionFilter();
                Check(!Physics.GetIgnoreCollision(c.rigids[0].collider, c.rigids[1].collider), "replacement settings refreshed");
                c.rigidBodies[1].SetCollisionFilter(1, 1);
                var other = UnityEngine.Object.Instantiate(model);
                var otherController = other.GetComponent<PmxPhysicsController>();
                Call(otherController, "Awake"); Call(otherController, "OnEnable");
                Check(!Physics.GetIgnoreCollision(c.rigids[0].collider, otherController.rigids[1].collider), "model filter isolation");
                UnityEngine.Object.DestroyImmediate(other);
                c.simulate = false; Call(c, "FixedUpdate");
                Check(c.rigids[1].body.isKinematic, "pause simulation");
                c.simulate = true; c.ResetPhysics(); Call(c, "FixedUpdate");
                Check(!c.rigids[1].body.isKinematic, "resume simulation");
                c.rigids[2].body.position = Vector3.one * 10f;
                c.ResetPhysics(); Call(c, "LateUpdate");
                Check(Vector3.Distance(c.rigids[2].body.position, c.transform.TransformPoint(c.rigids[2].localPos)) < 0.00001f, "reset unbound body");
                UnityEngine.Object.DestroyImmediate(instance);

                importer.importJoints = false; importer = Reimport(importer);
                model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(model.GetComponentsInChildren<Rigidbody>().Length == 3 && model.GetComponentsInChildren<ConfigurableJoint>().Length == 0, "joints disabled");
                importer.importJoints = true; importer.importBlendShapes = false; importer = Reimport(importer);
                model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.blendShapeCount == 0, "morph disabled");
                Check(model.GetComponentsInChildren<ConfigurableJoint>().Length == 2, "physics after skipped morph");
                importer.importBlendShapes = true; importer.importPhysics = false; importer = Reimport(importer);
                model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(model.GetComponent<PmxPhysicsController>() == null && model.GetComponentsInChildren<Rigidbody>().Length == 0, "physics disabled");
                smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
                Check(smr.sharedMesh.blendShapeCount == 1 && smr.sharedMesh.vertexCount == 3 && smr.bones.Length == 2, "nonphysics regression");
                Check(smr.sharedMaterials[0].GetColor(colorProperty) == materialColor, "material regression");
            }
            Debug.Log("PMX_VALIDATION_PASS " + checks);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            throw;
        }
        finally { AssetDatabase.DeleteAsset("Assets/__PmxToolsGeneratedTests"); }
    }

    static void Text(BinaryWriter w, Encoding enc, string value)
    {
        byte[] data = enc.GetBytes(value); w.Write(data.Length); w.Write(data);
    }
    static void V3(BinaryWriter w, float x = 0, float y = 0, float z = 0) { w.Write(x); w.Write(y); w.Write(z); }
    static void V4(BinaryWriter w, float x = 0, float y = 0, float z = 0, float v = 0) { V3(w, x, y, z); w.Write(v); }
    static void Index(BinaryWriter w, int width, int value)
    {
        if (width == 1) w.Write((sbyte)value);
        else if (width == 2) w.Write((short)value);
        else w.Write(value);
    }
    static void WriteFixture(string path, int width, bool utf8)
    {
        using var w = new BinaryWriter(File.Create(path));
        Encoding enc = utf8 ? Encoding.UTF8 : Encoding.Unicode;
        void T(string s) => Text(w, enc, s);
        void I(int i) => Index(w, width, i);
        w.Write(Encoding.ASCII.GetBytes("PMX ")); w.Write(2.1f); w.Write((byte)8);
        w.Write(new byte[] { (byte)(utf8 ? 1 : 0), 0, (byte)width, (byte)width, (byte)width, (byte)width, (byte)width, (byte)width });
        T("検証モデル"); T(""); T(""); T("");
        w.Write(3);
        for (int i = 0; i < 3; i++)
        {
            V3(w, i == 1 ? 1 : 0, i == 2 ? 1 : 0); V3(w, 0, 0, 1);
            w.Write(0f); w.Write(0f); w.Write((byte)0); I(0); w.Write(1f);
        }
        w.Write(3); I(0); I(1); I(2); w.Write(0); // faces, textures
        w.Write(1); T("材質"); T(""); V4(w, .2f, .4f, .6f, 1);
        V3(w); w.Write(10f); V3(w); w.Write((byte)0); V4(w); w.Write(0f);
        I(-1); I(-1); w.Write((byte)0); w.Write((byte)1); w.Write((byte)0); T(""); w.Write(3);
        w.Write(2);
        for (int i = 0; i < 2; i++)
        {
            T("骨" + i); T(""); V3(w, 0, i, 0); I(i - 1);
            w.Write(0); w.Write((ushort)0); V3(w);
        }
        w.Write(2);
        T("あ"); T(""); w.Write((byte)3); w.Write((byte)1); w.Write(1); I(0); V3(w, .1f, 0, 0);
        T("グループ"); T(""); w.Write((byte)3); w.Write((byte)0); w.Write(1); I(0); w.Write(.5f);
        w.Write(1); T("表示"); T(""); w.Write((byte)0); w.Write(2);
        w.Write((byte)0); I(1); w.Write((byte)1); I(1);
        w.Write(3);
        for (int i = 0; i < 3; i++)
        {
            // 生PMXは衝突許可マスク。全許可・group0禁止・全禁止をそれぞれ検証する。
            T("剛体" + i); T(""); I(i == 2 ? -1 : i); w.Write((byte)i); w.Write((ushort)(i == 0 ? 0xffff : i == 1 ? 0xfffe : 0));
            w.Write((byte)i); V3(w, 1, 2, 3); V3(w, 0, i, .5f); V3(w, .1f, .2f, .3f);
            w.Write(2f); w.Write(.2f); w.Write(.1f); w.Write(.3f); w.Write(.4f); w.Write((byte)i);
        }
        w.Write(2);
        for (int i = 0; i < 2; i++)
        {
            T("接続" + i); T(""); w.Write((byte)i); I(i); I(i + 1);
            V3(w, 0, 1, .5f); V3(w, .1f, .2f, .3f);
            V3(w); V3(w); V3(w, -.2f, -.3f, -.4f); V3(w, .2f, .3f, .4f);
            V3(w, 10, 20, 30); V3(w, 10, 20, 20);
        }
        w.Write(0); // SoftBody
    }
}
