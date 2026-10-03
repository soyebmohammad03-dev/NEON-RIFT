using System;
using UnityEngine;

namespace NeonRift.Missions
{
    public enum InteractionStepKind
    {
        /// <summary>Runs by itself while the player stays engaged (stopped in the zone).</summary>
        Auto,
        /// <summary>Hold Interact for the duration; releasing it bleeds progress.</summary>
        Hold,
        /// <summary>A cursor sweeps a track; press Interact while it is inside the window. Misses cost heat.</summary>
        Timing,
        /// <summary>Runs by itself, but interference stalls it until the player presses Interact to re-sync.</summary>
        Sustain
    }

    /// <summary>A progress cue inside a step: raises a world event once the step passes <see cref="at"/>.</summary>
    [Serializable]
    public struct InteractionCue
    {
        [Range(0f, 1f)] public float at;
        public string eventId;

        public InteractionCue(float at, string eventId)
        {
            this.at = at;
            this.eventId = eventId;
        }
    }

    /// <summary>
    /// One stage of a multi-stage interaction (CONNECTING, AUTHENTICATING, BYPASSING …). Pure data; run by
    /// <see cref="InteractionRun"/>. Steps raise world events as they start and finish, so the environment (screens,
    /// locks, machinery, lights, audio) reacts through the same event ids missions already use.
    /// </summary>
    [Serializable]
    public sealed class InteractionStep
    {
        [Tooltip("Terminal label, e.g. AUTHENTICATING.")]
        [SerializeField] private string label = "CONNECTING";
        [Tooltip("Secondary line under the label.")]
        [SerializeField] private string detail;
        [SerializeField] private InteractionStepKind kind;
        [Tooltip("Auto/Hold/Sustain: seconds to complete. Timing: seconds for one sweep of the cursor across the track.")]
        [SerializeField, Min(0.1f)] private float duration = 1f;
        [Tooltip("A cancelled interaction restarts from the last checkpoint step reached instead of from the beginning.")]
        [SerializeField] private bool checkpoint;

        [Header("Timing")]
        [Tooltip("Width of the target window as a fraction of the track.")]
        [SerializeField, Range(0.05f, 0.6f)] private float window = 0.24f;
        [Tooltip("Centre of the first window (moves after every miss).")]
        [SerializeField, Range(0.15f, 0.85f)] private float windowCentre = 0.62f;
        [Tooltip("Misses allowed before the whole interaction fails (0 = unlimited).")]
        [SerializeField, Min(0)] private int maxMisses = 3;
        [SerializeField, Range(0f, 1f)] private float missHeat = 0.04f;

        [Header("Sustain")]
        [Tooltip("Points in the step (0..1) where interference stalls it until the player re-syncs.")]
        [SerializeField] private float[] interference = Array.Empty<float>();
        [Tooltip("Seconds to re-sync before the link drops and progress rolls back.")]
        [SerializeField, Min(0.5f)] private float responseSeconds = 2.5f;
        [Tooltip("Progress lost when interference is not answered in time (fraction of the step).")]
        [SerializeField, Range(0f, 0.5f)] private float rollback = 0.1f;

        [Header("World events")]
        [SerializeField] private string eventOnStart;
        [SerializeField] private string eventOnComplete;
        [SerializeField] private InteractionCue[] cues = Array.Empty<InteractionCue>();

        public string Label => label;
        public string Detail => detail;
        public InteractionStepKind Kind => kind;
        public float Duration => Mathf.Max(0.1f, duration);
        public bool Checkpoint => checkpoint;
        public float Window => window;
        public float WindowCentre => windowCentre;
        public int MaxMisses => maxMisses;
        public float MissHeat => missHeat;
        public float[] Interference => interference ?? Array.Empty<float>();
        public float ResponseSeconds => responseSeconds;
        public float Rollback => rollback;
        public string EventOnStart => eventOnStart;
        public string EventOnComplete => eventOnComplete;
        public InteractionCue[] Cues => cues ?? Array.Empty<InteractionCue>();

        public InteractionStep() { }

        public InteractionStep(string label, InteractionStepKind kind, float duration, string detail = null, bool checkpoint = false,
                               string eventOnStart = null, string eventOnComplete = null)
        {
            this.label = label;
            this.kind = kind;
            this.duration = duration;
            this.detail = detail;
            this.checkpoint = checkpoint;
            this.eventOnStart = eventOnStart;
            this.eventOnComplete = eventOnComplete;
        }

        public InteractionStep WithTiming(float windowWidth, float centre, int misses, float heatPerMiss)
        {
            window = windowWidth;
            windowCentre = centre;
            maxMisses = misses;
            missHeat = heatPerMiss;
            return this;
        }

        public InteractionStep WithInterference(float[] at, float response, float lost)
        {
            interference = at ?? Array.Empty<float>();
            responseSeconds = response;
            rollback = lost;
            return this;
        }

        public InteractionStep WithCues(params InteractionCue[] stepCues)
        {
            cues = stepCues ?? Array.Empty<InteractionCue>();
            return this;
        }
    }
}
