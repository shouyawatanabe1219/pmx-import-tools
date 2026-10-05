using UnityEngine;

namespace PmxImport
{
    /// <summary>PhysXで近似したPMX制限の記録。移動はUnity単位、角度は度、ばねはPMX値。</summary>
    public sealed class PmxJointMetadata : MonoBehaviour
    {
        public string jointName;
        public int type;
        public ConfigurableJoint joint;
        public Vector3 linearMin, linearMax;
        public Vector3 angularMin, angularMax;
        public Vector3 linearSpring, angularSpring;
    }
}
