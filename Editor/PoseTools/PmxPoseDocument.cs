using System;
using System.Collections.Generic;
using UnityEngine;

namespace PmxImport.Editor
{
    [Serializable]
    public sealed class PmxPoseKey
    {
        public int frame;
        public Vector3[] positions;
        public Quaternion[] rotations;
        public Vector3[] scales;
        public float[] expressions;
    }

    public sealed class PmxPoseDocument : ScriptableObject
    {
        public int frameRate = 30;
        public int endFrame = 60;
        public bool loop = true;
        public bool preservePhysics = true;
        public string[] bonePaths = Array.Empty<string>();
        public string meshPath;
        public string[] expressionNames = Array.Empty<string>();
        public List<PmxPoseKey> keys = new List<PmxPoseKey>();
    }
}
