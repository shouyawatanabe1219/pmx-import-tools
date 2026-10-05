// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PmxImport
{
    public enum PmxRigidMode { FollowBone, Physics, PhysicsWithBone }

    [Serializable]
    public class PmxRigidBinding
    {
        public string name;
        public Rigidbody body;
        public Collider collider;
        public Transform bone;
        public PmxRigidMode mode;
        public Vector3 localPos;
        public Quaternion localRot = Quaternion.identity;
        [Range(0, 15)] public int group;
        public int nonCollisionMask;
    }

    [DisallowMultipleComponent]
    public class PmxPhysicsController : MonoBehaviour
    {
        public Transform physicsRoot;
        public List<PmxRigidBody> rigidBodies = new List<PmxRigidBody>();
        [HideInInspector] public List<PmxRigidBinding> rigids = new List<PmxRigidBinding>();
        [Header("Simulation")]
        public bool simulate = true;
        public float gravityMultiplier = 1;
        public float teleportThreshold = 2;
        [Min(0)] public float poseJumpDistance = .25f;
        [Range(0, 180)] public float poseJumpAngle = 25;
        public bool detachFromHierarchy = true;
        [Tooltip("PMX剛体とモデル自身のRigidbodyの押し合いを防ぐ")]
        public bool ignoreOwnerCollisions = true;
        [Header("Solver")]
        public float maxAngularVelocity = 100;
        public int solverIterations = 10;
        public int solverVelocityIterations = 4;

        sealed class SimulationBody
        {
            public PmxRigidBinding settings;
            public bool dynamic;
            public bool tracked;
            public Vector3 pinPosition;
            public Quaternion pinRotation;
        }
        struct OwnerContact { public Collider shape, owner; public bool ignoredBefore; }
        readonly List<SimulationBody> simulation = new List<SimulationBody>();
        readonly List<SimulationBody> boneWriters = new List<SimulationBody>();
        readonly List<OwnerContact> contacts = new List<OwnerContact>();
        Vector3 previousPosition;
        Quaternion previousRotation;
        bool ready, resetRequested = true, simulationEnabled;

        public void ResetPhysics() { resetRequested = true; }
        public void ReapplyCollisionFilter() { SetupCollisionFilter(); }

        void Awake()
        {
            if (physicsRoot == null) return;
            if (rigidBodies.Count == 0) rigidBodies.AddRange(physicsRoot.GetComponentsInChildren<PmxRigidBody>(true));
            RefreshBindings();
            simulation.Clear(); boneWriters.Clear();
            foreach (var binding in rigids)
            {
                var state = new SimulationBody { settings = binding, dynamic = binding.body != null && !binding.body.isKinematic };
                simulation.Add(state);
                if (!state.dynamic) continue;
                binding.body.maxAngularVelocity = Mathf.Max(0, maxAngularVelocity);
                binding.body.solverIterations = Mathf.Max(1, solverIterations);
                binding.body.solverVelocityIterations = Mathf.Max(1, solverVelocityIterations);
                if (binding.bone != null && binding.mode != PmxRigidMode.FollowBone) boneWriters.Add(state);
            }
            boneWriters.Sort((a, b) => Depth(a.settings.bone).CompareTo(Depth(b.settings.bone)));
            if (Application.isPlaying && detachFromHierarchy) physicsRoot.SetParent(null, true);
            ready = true; simulationEnabled = true;
            AlignAll(); UpdateSimulationState();
        }

        void OnEnable()
        {
            if (physicsRoot != null) physicsRoot.gameObject.SetActive(true);
            SetupCollisionFilter();
            resetRequested = true;
            if (ready) AlignAll();
        }

        void OnDisable()
        {
            RestoreOwnerContacts();
            if (physicsRoot != null) physicsRoot.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            RestoreOwnerContacts();
            if (physicsRoot != null && !physicsRoot.IsChildOf(transform)) Destroy(physicsRoot.gameObject);
        }

        void FixedUpdate()
        {
            if (!ready || physicsRoot == null) return;
            UpdateSimulationState();
            if (!simulate) return;
            if (HasPoseJump()) resetRequested = true;
            if (resetRequested) AlignAll();
            CapturePins();
            foreach (var state in simulation)
            {
                var binding = state.settings; var body = binding.body;
                if (body == null) continue;
                Transform frame = binding.bone != null ? binding.bone : transform;
                if (binding.mode == PmxRigidMode.FollowBone)
                {
                    body.MovePosition(frame.TransformPoint(binding.localPos));
                    body.MoveRotation(frame.rotation * binding.localRot);
                }
                else
                {
                    if (binding.mode == PmxRigidMode.PhysicsWithBone && binding.bone != null) body.position = frame.TransformPoint(binding.localPos);
                    if (!body.isKinematic && gravityMultiplier != 1) body.AddForce(Physics.gravity * (gravityMultiplier - 1), ForceMode.Acceleration);
                }
            }
        }

        void LateUpdate()
        {
            if (!ready || !simulate || physicsRoot == null) return;
            if (HasPoseJump()) resetRequested = true;
            if (resetRequested) { AlignAll(); return; }
            foreach (var state in boneWriters)
            {
                var binding = state.settings;
                if (binding.body == null || binding.bone == null) continue;
                binding.bone.rotation = binding.body.transform.rotation * Quaternion.Inverse(binding.localRot);
                if (binding.mode == PmxRigidMode.Physics)
                    binding.bone.position = binding.body.transform.position - binding.bone.TransformVector(binding.localPos);
            }
        }

        static int Depth(Transform bone) { int depth = 0; for (; bone != null; bone = bone.parent) depth++; return depth; }

        void RefreshBindings()
        {
            if (rigidBodies.Count == 0) return;
            rigids.Clear();
            foreach (var component in rigidBodies)
            {
                if (component == null) continue;
                component.controller = this; rigids.Add(component.GetBinding());
            }
            for (int i = 0; i < simulation.Count && i < rigids.Count; i++) simulation[i].settings = rigids[i];
        }

        void UpdateSimulationState()
        {
            if (simulationEnabled == simulate) return;
            foreach (var state in simulation) if (state.dynamic && state.settings.body != null) state.settings.body.isKinematic = !simulate;
            simulationEnabled = simulate;
            if (simulate) resetRequested = true;
        }

        void AlignAll()
        {
            foreach (var state in simulation)
            {
                var binding = state.settings; var body = binding.body;
                if (body == null) continue;
                Transform frame = binding.bone != null ? binding.bone : transform;
                body.position = frame.TransformPoint(binding.localPos); body.rotation = frame.rotation * binding.localRot;
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            }
            Physics.SyncTransforms(); resetRequested = false; CapturePins();
        }

        bool HasPoseJump()
        {
            if (teleportThreshold > 0 && Vector3.Distance(previousPosition, transform.position) > teleportThreshold) return true;
            if (Jump(previousPosition, previousRotation, transform.position, transform.rotation)) return true;
            foreach (var state in simulation)
            {
                var binding = state.settings;
                if (!state.tracked || binding.body == null || binding.mode != PmxRigidMode.FollowBone) continue;
                Transform frame = binding.bone != null ? binding.bone : transform;
                if (Jump(state.pinPosition, state.pinRotation, frame.TransformPoint(binding.localPos), frame.rotation * binding.localRot)) return true;
            }
            return false;
        }

        bool Jump(Vector3 oldPosition, Quaternion oldRotation, Vector3 position, Quaternion rotation)
        {
            return (poseJumpDistance > 0 && Vector3.Distance(oldPosition, position) > poseJumpDistance) ||
                   (poseJumpAngle > 0 && Quaternion.Angle(oldRotation, rotation) > poseJumpAngle);
        }

        void CapturePins()
        {
            previousPosition = transform.position; previousRotation = transform.rotation;
            foreach (var state in simulation)
            {
                var binding = state.settings;
                state.tracked = binding.body != null && binding.mode == PmxRigidMode.FollowBone;
                if (!state.tracked) continue;
                Transform frame = binding.bone != null ? binding.bone : transform;
                state.pinPosition = frame.TransformPoint(binding.localPos); state.pinRotation = frame.rotation * binding.localRot;
            }
        }

        void SetupCollisionFilter()
        {
            RestoreOwnerContacts(); RefreshBindings();
            for (int first = 0; first < rigids.Count; first++)
                for (int second = first + 1; second < rigids.Count; second++)
                {
                    var a = rigids[first]; var b = rigids[second];
                    if (a.collider == null || b.collider == null) continue;
                    bool excluded = (a.nonCollisionMask & (1 << Mathf.Clamp(b.group, 0, 15))) != 0 ||
                                    (b.nonCollisionMask & (1 << Mathf.Clamp(a.group, 0, 15))) != 0;
                    Physics.IgnoreCollision(a.collider, b.collider, excluded);
                }
            var owner = ignoreOwnerCollisions ? GetComponentInParent<Rigidbody>() : null;
            if (owner == null) return;
            var shapes = new HashSet<Collider>(); foreach (var binding in rigids) if (binding.collider != null) shapes.Add(binding.collider);
            foreach (var shape in owner.GetComponentsInChildren<Collider>(true))
            {
                if (shape.attachedRigidbody != owner || shapes.Contains(shape)) continue;
                foreach (var pmx in shapes)
                {
                    contacts.Add(new OwnerContact { shape = pmx, owner = shape, ignoredBefore = Physics.GetIgnoreCollision(pmx, shape) });
                    Physics.IgnoreCollision(pmx, shape);
                }
            }
        }

        void RestoreOwnerContacts()
        {
            foreach (var contact in contacts) if (contact.shape != null && contact.owner != null) Physics.IgnoreCollision(contact.shape, contact.owner, contact.ignoredBefore);
            contacts.Clear();
        }
    }
}
