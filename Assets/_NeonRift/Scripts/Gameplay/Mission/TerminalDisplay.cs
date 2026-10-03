using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// The physical side of an <see cref="Interactable"/>: its screens (NeonRift/TerminalScreen) show the stage strip,
    /// scrolling data and progress, change colour with the state (standby, working, denied, complete, lockout), tear
    /// on misses and interference, and a small light and 3D blips sell it from the car. Presentation only.
    /// </summary>
    public sealed class TerminalDisplay : MonoBehaviour
    {
        [SerializeField] private Interactable source;
        [SerializeField] private Renderer[] screens = Array.Empty<Renderer>();
        [SerializeField] private Light glow;
        [SerializeField] private AudioSource speaker;
        [SerializeField] private AudioClip blip;
        [SerializeField] private AudioClip denied;
        [SerializeField, ColorUsage(false, true)] private Color standby = new(0.25f, 1.1f, 1.6f);
        [SerializeField, ColorUsage(false, true)] private Color working = new(0.9f, 2.4f, 3f);
        [SerializeField, ColorUsage(false, true)] private Color complete = new(0.4f, 2.6f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color alarm = new(3f, 0.25f, 0.35f);
        [SerializeField, ColorUsage(false, true)] private Color locked = new(1.2f, 0.15f, 0.2f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int StepsId = Shader.PropertyToID("_Steps");
        private static readonly int StepId = Shader.PropertyToID("_Step");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int GlitchId = Shader.PropertyToID("_Glitch");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private MaterialPropertyBlock block;
        private float glitch, flashUntil;
        private Color flashColour;
        private float glowBase, seed;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (glow != null) glowBase = glow.intensity;
            seed = Mathf.Repeat(transform.position.x * 0.37f + transform.position.z * 0.11f, 17f);
        }

        private void OnEnable()
        {
            if (source != null) source.FeedbackRaised += OnFeedback;
        }

        private void OnDisable()
        {
            if (source != null) source.FeedbackRaised -= OnFeedback;
        }

        private void OnFeedback(Interactable _, Interactable.Feedback feedback)
        {
            switch (feedback)
            {
                case Interactable.Feedback.Miss:
                case Interactable.Feedback.LinkDropped:
                case Interactable.Feedback.Failed:
                    glitch = 1f;
                    Flash(alarm, 0.35f);
                    Play(denied);
                    break;
                case Interactable.Feedback.Interference:
                    glitch = 1f;
                    Flash(alarm, 0.2f);
                    break;
                case Interactable.Feedback.Cancelled:
                    glitch = 0.6f;
                    break;
                case Interactable.Feedback.StepCompleted:
                case Interactable.Feedback.Resynced:
                case Interactable.Feedback.Completed:
                    Flash(complete * 1.6f, 0.18f);
                    Play(blip);
                    break;
                case Interactable.Feedback.Started:
                case Interactable.Feedback.StepStarted:
                    Play(blip);
                    break;
            }
        }

        private void Flash(Color colour, float seconds)
        {
            flashColour = colour;
            flashUntil = Time.time + seconds;
        }

        private void Play(AudioClip clip)
        {
            if (speaker != null && clip != null) speaker.PlayOneShot(clip);
        }

        private void Update()
        {
            if (source == null) return;
            var run = source.Run;
            bool done = source.Current == Interactable.State.Completed;
            bool active = run != null && run.Running;
            bool interference = active && run.InterferencePending;
            if (interference) glitch = Mathf.Max(glitch, 0.5f + 0.5f * Mathf.Sin(Time.time * 30f));
            glitch = Mathf.MoveTowards(glitch, 0f, Time.deltaTime * 2.5f);

            Color colour; float mode;
            if (done) { colour = complete; mode = 2f; }
            else if (source.LockedOut) { colour = locked; mode = 3f; }
            else if (interference) { colour = alarm; mode = 3f; }
            else if (active) { colour = working; mode = 1f; }
            else if (source.Current == Interactable.State.Available) { colour = standby; mode = 0f; }
            else { colour = standby * 0.35f; mode = 0f; }
            if (Time.time < flashUntil) colour = flashColour;

            block.SetColor(ColorId, colour);
            block.SetFloat(ProgressId, source.Progress);
            block.SetFloat(StepsId, run != null ? run.Steps.Count : 1f);
            block.SetFloat(StepId, done ? 99f : run != null ? run.StepIndex : 0f);
            block.SetFloat(ModeId, mode);
            block.SetFloat(GlitchId, glitch);
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i] == null) continue;
                block.SetFloat(SeedId, i * 3.7f + seed);
                screens[i].SetPropertyBlock(block);
            }
            if (glow != null)
            {
                float max = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
                glow.color = max > 0f ? colour / max : Color.black;
                glow.intensity = glowBase * (active ? 1.6f : 1f) * (1f - glitch * 0.6f);
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Interactable interactable, Renderer[] screenRenderers, Light light, AudioSource audio, AudioClip blipClip, AudioClip deniedClip)
        {
            source = interactable;
            screens = screenRenderers ?? Array.Empty<Renderer>();
            glow = light;
            speaker = audio;
            blip = blipClip;
            denied = deniedClip;
        }
#endif
    }
}
