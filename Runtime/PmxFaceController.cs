// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PmxImport
{
    public enum PmxMorphPanel { System, Eyebrow, Eye, Mouth, Other }
    [Serializable] public class PmxGroupItem { public string blendShape; public float rate; }
    [Serializable] public class PmxMorphInfo
    {
        public string name;
        public PmxMorphPanel panel;
        public bool isGroup;
        public string blendShape;
        public List<PmxGroupItem> items = new List<PmxGroupItem>();
        [Range(0, 1)] public float weight;
    }

    [DisallowMultipleComponent]
    public class PmxFaceController : MonoBehaviour
    {
        public SkinnedMeshRenderer target;
        public List<PmxMorphInfo> morphs = new List<PmxMorphInfo>();
        public string morphA = "あ", morphI = "い", morphU = "う", morphE = "え", morphO = "お";
        public bool lipSyncFromAudio;
        public AudioSource audioSource;
        public float gain = 10, threshold = .01f, lipSyncMaxWeight = .8f, attackSpeed = 25, releaseSpeed = 12;
        public bool autoBlink = true;
        public string blinkMorph = "まばたき";
        public Vector2 blinkInterval = new Vector2(2, 5);
        public float blinkDuration = .16f;
        [Range(0, 1)] public float blinkMaxWeight = 1;
        readonly float[] vowels = new float[5], samples = new float[256];
        readonly Dictionary<string, float> output = new Dictionary<string, float>();
        readonly HashSet<string> previouslyDriven = new HashSet<string>();
        readonly HashSet<string> visiting = new HashSet<string>();
        float nextBlink, blinkStarted = -1, blinkWeight, audioWeight;

        PmxMorphInfo Find(string name) => string.IsNullOrEmpty(name) ? null : morphs.Find(m => m != null && m.name == name);
        public bool HasMorph(string morphName) => Find(morphName) != null;
        public float GetMorph(string morphName) => Find(morphName)?.weight ?? 0;
        public void SetMorph(string morphName, float weight01) { var morph = Find(morphName); if (morph != null) morph.weight = Mathf.Clamp01(weight01); }
        public void SetVowels(float a, float i, float u, float e, float o) { vowels[0] = Mathf.Clamp01(a); vowels[1] = Mathf.Clamp01(i); vowels[2] = Mathf.Clamp01(u); vowels[3] = Mathf.Clamp01(e); vowels[4] = Mathf.Clamp01(o); }
        public void ResetAll()
        {
            foreach (var morph in morphs) if (morph != null) morph.weight = 0;
            Array.Clear(vowels, 0, vowels.Length); blinkWeight = audioWeight = 0; Apply();
        }
        void OnEnable() { ScheduleBlink(); }
        void ScheduleBlink() { nextBlink = Time.time + UnityEngine.Random.Range(Mathf.Max(.1f, blinkInterval.x), Mathf.Max(.1f, blinkInterval.y)); }
        void Update()
        {
            if (autoBlink && Time.time >= nextBlink && blinkStarted < 0) blinkStarted = Time.time;
            blinkWeight = 0;
            if (autoBlink && blinkStarted >= 0)
            {
                float phase = (Time.time - blinkStarted) / Mathf.Max(.02f, blinkDuration);
                blinkWeight = Mathf.Clamp01(1 - Mathf.Abs(phase * 2 - 1)) * blinkMaxWeight;
                if (phase >= 1) { blinkStarted = -1; ScheduleBlink(); }
            }
            else if (!autoBlink) blinkStarted = -1;
            float desired = 0;
            if (lipSyncFromAudio && audioSource != null && audioSource.isPlaying)
            {
                audioSource.GetOutputData(samples, 0); double energy = 0; foreach (float sample in samples) energy += sample * sample;
                float rms = (float)Math.Sqrt(energy / samples.Length);
                desired = Mathf.Clamp01(Mathf.Max(0, rms - threshold) * Mathf.Max(0, gain)) * Mathf.Clamp01(lipSyncMaxWeight);
            }
            audioWeight = Mathf.MoveTowards(audioWeight, desired, Time.deltaTime * Mathf.Max(0, desired > audioWeight ? attackSpeed : releaseSpeed));
            Apply();
        }
        public void Apply()
        {
            if (target == null || target.sharedMesh == null) return;
            output.Clear(); visiting.Clear();
            foreach (var morph in morphs) if (morph != null) Add(morph.name, Mathf.Clamp01(morph.weight));
            string[] names = { morphA, morphI, morphU, morphE, morphO };
            for (int i = 0; i < vowels.Length; i++) Add(names[i], vowels[i]);
            Add(morphA, audioWeight); Add(blinkMorph, blinkWeight);
            foreach (string name in previouslyDriven) if (!output.ContainsKey(name)) Write(name, 0);
            previouslyDriven.Clear();
            foreach (var item in output) { Write(item.Key, Mathf.Clamp01(item.Value) * 100); previouslyDriven.Add(item.Key); }
        }
        void Write(string shape, float weight) { int index = target.sharedMesh.GetBlendShapeIndex(shape); if (index >= 0) target.SetBlendShapeWeight(index, weight); }
        void Add(string name, float weight)
        {
            if (weight <= 0 || string.IsNullOrEmpty(name) || !visiting.Add(name)) return;
            var morph = Find(name);
            if (morph != null && morph.isGroup)
            {
                foreach (var item in morph.items) if (item != null) Add(item.blendShape, weight * item.rate);
            }
            else
            {
                string shape = morph != null && !string.IsNullOrEmpty(morph.blendShape) ? morph.blendShape : name;
                output.TryGetValue(shape, out float existing); output[shape] = existing + weight;
            }
            visiting.Remove(name);
        }
    }
}
