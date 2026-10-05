using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PmxImport.Editor
{
    public static class PmxSceneRig
    {
        static readonly HashSet<string> MainBones = new HashSet<string> {
            "全ての親", "センター", "グルーブ", "腰", "上半身", "上半身2", "上半身２", "下半身", "首", "頭",
            "右肩", "右腕", "右ひじ", "右手首", "左肩", "左腕", "左ひじ", "左手首",
            "右足", "右ひざ", "右足首", "右つま先", "左足", "左ひざ", "左足首", "左つま先",
            "右足D", "右ひざD", "右足首D", "右足先EX", "左足D", "左ひざD", "左足首D", "左足先EX",
            "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
            "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
            "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes"
        };

        public static Transform[] VisibleBones(Transform[] bones, bool all, Transform selected)
        {
            if (bones == null) return System.Array.Empty<Transform>();
            var existing = bones.Where(t => t != null).Distinct().ToArray();
            if (all) return existing;
            var main = existing.Where(t => MainBones.Contains(t.name)).ToList();
            foreach (string side in new[] { "右", "左" })
                foreach (string part in new[] { "足", "ひざ", "足首" })
                    if (existing.Any(t => t.name == side + part + "D"))
                        main.RemoveAll(t => t.name == side + part);
            if (main.Count < 2) return existing;
            if (selected != null && !main.Contains(selected) && existing.Contains(selected)) main.Add(selected);
            return main.ToArray();
        }

        public static bool TryGetLimb(Transform[] bones, Transform end, out Transform upper, out Transform middle)
        {
            upper = middle = null;
            if (end == null || bones == null) return false;
            string upperName, middleName;
            if (end.name == "右手首" || end.name == "左手首")
            { string side = end.name.Substring(0, 1); upperName = side + "腕"; middleName = side + "ひじ"; }
            else if (end.name == "右足首D" || end.name == "左足首D")
            { string side = end.name.Substring(0, 1); upperName = side + "足D"; middleName = side + "ひざD"; }
            else if (end.name == "右足首" || end.name == "左足首")
            { string side = end.name.Substring(0, 1); upperName = side + "足"; middleName = side + "ひざ"; }
            else if (end.name == "LeftHand" || end.name == "RightHand")
            { string side = end.name.StartsWith("Left") ? "Left" : "Right"; upperName = side + "UpperArm"; middleName = side + "LowerArm"; }
            else if (end.name == "LeftFoot" || end.name == "RightFoot")
            { string side = end.name.StartsWith("Left") ? "Left" : "Right"; upperName = side + "UpperLeg"; middleName = side + "LowerLeg"; }
            else return false;
            upper = bones.FirstOrDefault(t => t != null && t.name == upperName);
            middle = bones.FirstOrDefault(t => t != null && t.name == middleName);
            return upper != null && middle != null && middle.IsChildOf(upper) && end.IsChildOf(middle);
        }

        public static bool SolveLimb(Transform upper, Transform middle, Transform end, Vector3 target, Vector3 bendHint)
        {
            if (upper == null || middle == null || end == null || !middle.IsChildOf(upper) || !end.IsChildOf(middle)) return false;
            Vector3 origin = upper.position;
            float a = Vector3.Distance(origin, middle.position), b = Vector3.Distance(middle.position, end.position);
            Vector3 offset = target - origin;
            if (a < .0001f || b < .0001f || offset.sqrMagnitude < .00000001f) return false;
            Vector3 direction = offset.normalized;
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - b) + .00001f, a + b - .00001f);
            Vector3 bend = Vector3.ProjectOnPlane(middle.position - origin, direction);
            if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(bendHint, direction);
            if (bend.sqrMagnitude < .000001f) bend = Vector3.Cross(direction, Vector3.up);
            if (bend.sqrMagnitude < .000001f) bend = Vector3.Cross(direction, Vector3.right);
            bend.Normalize();
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 desiredMiddle = origin + direction * along + bend * height;
            Vector3 desiredEnd = origin + direction * distance;
            Quaternion endRotation = end.rotation;
            upper.rotation = Quaternion.FromToRotation(middle.position - origin, desiredMiddle - origin) * upper.rotation;
            middle.rotation = Quaternion.FromToRotation(end.position - middle.position, desiredEnd - middle.position) * middle.rotation;
            end.rotation = endRotation;
            return true;
        }
    }
}
