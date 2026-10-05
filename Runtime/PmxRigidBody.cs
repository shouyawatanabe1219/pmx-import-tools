using UnityEngine;

namespace PmxImport
{
    /// <summary>PMX専用の剛体設定。衝突グループはUnityレイヤーとは独立。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("PMX/Pmx Rigid Body")]
    public sealed class PmxRigidBody : MonoBehaviour
    {
        public PmxPhysicsController controller;
        public PmxRigidBinding binding = new PmxRigidBinding();
        public Rigidbody Body => GetComponent<Rigidbody>();
        public Collider Shape => GetComponent<Collider>();

        public PmxRigidBinding GetBinding()
        {
            if (binding == null) binding = new PmxRigidBinding();
            binding.body = Body;
            binding.collider = Shape;
            binding.group = Mathf.Clamp(binding.group, 0, 15);
            binding.nonCollisionMask &= 0xffff;
            return binding;
        }

        public void SetCollisionFilter(int group, int nonCollisionMask)
        {
            var settings = GetBinding();
            settings.group = Mathf.Clamp(group, 0, 15);
            settings.nonCollisionMask = nonCollisionMask & 0xffff;
            if (controller != null) controller.ReapplyCollisionFilter();
        }

        void Reset() => GetBinding();
        void OnEnable()
        {
            if (controller != null) controller.ReapplyCollisionFilter();
        }
    }
}
