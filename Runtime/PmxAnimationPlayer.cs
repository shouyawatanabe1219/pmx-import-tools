using UnityEngine;

namespace PmxImport
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PmxAnimationPlayer : MonoBehaviour
    {
        public AnimationClip clip;
        public bool playOnEnable = true;
        public bool loop = true;
        [Min(0f)] public float speed = 1f;
        public bool animateExpressions = true;
        public float CurrentTime { get; private set; }
        public bool IsPlaying { get; private set; }

        PmxFaceController face;
        PmxPhysicsController physics;
        bool faceWasEnabled;
        bool ownsFace;
        double startTime;
        float startOffset;
        float lastFixedTime;

        void Awake()
        {
            face = GetComponent<PmxFaceController>();
            physics = GetComponent<PmxPhysicsController>();
        }

        void OnEnable() { if (playOnEnable) Play(); }
        void OnDisable() { Pause(); ReleaseFace(); }

        public void Play()
        {
            if (clip == null) return;
            if (GetComponent<Animator>() is Animator animator && animator.enabled ||
                GetComponent<Animation>() is Animation animation && animation.enabled)
            {
                Debug.LogWarning("PmxAnimationPlayer: Animator / Animation を無効にしてから再生してください。", this);
                return;
            }
            startOffset = CurrentTime;
            startTime = Time.timeAsDouble;
            lastFixedTime = CurrentTime;
            IsPlaying = true;
            AcquireFace();
            Sample(CurrentTime);
            if (physics != null) physics.ResetPhysics();
        }

        public void Pause() { IsPlaying = false; }

        public void Stop()
        {
            IsPlaying = false;
            CurrentTime = 0f;
            Sample(0f);
            ReleaseFace();
            if (physics != null) physics.ResetPhysics();
        }

        public void Seek(float seconds)
        {
            CurrentTime = Mathf.Clamp(seconds, 0f, clip != null ? clip.length : 0f);
            startOffset = CurrentTime;
            startTime = Time.timeAsDouble;
            AcquireFace();
            Sample(CurrentTime);
            if (physics != null) physics.ResetPhysics();
        }

        float TimeAt(double now)
        {
            float t = startOffset + Mathf.Max(0f, (float)(now - startTime)) * Mathf.Max(0f, speed);
            if (clip == null || clip.length <= 0f) return 0f;
            return loop ? Mathf.Repeat(t, clip.length) : Mathf.Clamp(t, 0f, clip.length);
        }

        void FixedUpdate()
        {
            if (!IsPlaying) return;
            float next = TimeAt(Time.fixedTimeAsDouble);
            if (loop && next < lastFixedTime && physics != null) physics.ResetPhysics();
            lastFixedTime = next;
            Sample(next);
        }

        void Update()
        {
            if (!IsPlaying || clip == null) return;
            float next = TimeAt(Time.timeAsDouble);
            if (loop && next < CurrentTime && physics != null) physics.ResetPhysics();
            CurrentTime = next;
            Sample(next);
            if (!loop && next >= clip.length) IsPlaying = false;
        }

        void Sample(float time) { if (clip != null) clip.SampleAnimation(gameObject, time); }

        void AcquireFace()
        {
            if (!animateExpressions || ownsFace || face == null) return;
            faceWasEnabled = face.enabled;
            face.enabled = false;
            ownsFace = true;
        }

        void ReleaseFace()
        {
            if (!ownsFace || face == null) return;
            face.enabled = faceWasEnabled;
            ownsFace = false;
        }
    }
}
