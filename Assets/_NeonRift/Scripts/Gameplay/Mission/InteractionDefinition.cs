using System.Collections.Generic;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// What kind of interaction an <see cref="Interactable"/> is (hack a terminal, extract a core …): its stages,
    /// how each is performed and what it costs. Shared by every interactable of that kind, so new mechanics are
    /// mostly data. With no steps it is a single "hold Interact" stage of <see cref="HoldSeconds"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Missions/Interaction Definition", fileName = "Interaction")]
    public sealed class InteractionDefinition : ScriptableObject
    {
        [Tooltip("Prompt verb, e.g. HACK, COLLECT, OVERRIDE.")]
        [SerializeField] private string verb = "HACK";
        [Tooltip("Device line on the terminal readout, e.g. S7 GRID // GATE CONTROLLER 04.")]
        [SerializeField] private string deviceName;
        [Tooltip("Single-stage interactions: hold the interact button this long, s.")]
        [SerializeField, Min(0f)] private float holdSeconds = 2f;
        [Tooltip("The vehicle must be slower than this to start or continue, km/h.")]
        [SerializeField, Min(0f)] private float maxSpeedKph = 10f;
        [Tooltip("Hold stages: progress lost per second when the button is released, as a fraction of the stage.")]
        [SerializeField, Min(0f)] private float decayPerSecond = 0.5f;
        [Tooltip("Heat added on completion (0..1). Intrusions raise security and shorten timed objectives.")]
        [SerializeField, Range(0f, 1f)] private float heatOnComplete;
        [Tooltip("Shown on the HUD with the heat gain.")]
        [SerializeField] private string heatReason = "INTRUSION LOGGED";
        [Tooltip("HUD message on completion.")]
        [SerializeField] private string completeMessage = "ACCESS GRANTED";

        [Tooltip("How much the mission's tension bed builds while this runs (0 = none, 1 = the big moment).")]
        [SerializeField, Range(0f, 1f)] private float tension;

        [Header("Stages")]
        [Tooltip("Ordered stages. Empty = one Hold stage of Hold Seconds.")]
        [SerializeField] private List<InteractionStep> steps = new();
        [Tooltip("After a failure (too many Timing misses) the device locks out for this long, s.")]
        [SerializeField, Min(0f)] private float lockoutSeconds = 4f;
        [SerializeField, Range(0f, 1f)] private float failHeat = 0.1f;
        [SerializeField] private string failMessage = "LOCKOUT · TRACE LOGGED";
        [Tooltip("World events raised when the interaction fails. Stage and failure/cancel event ids may contain {id}, " +
                 "replaced by the interactable's id, so one definition serves many devices.")]
        [SerializeField] private string[] eventsOnFail = System.Array.Empty<string>();
        [Tooltip("World events raised when a run is abandoned (the car drove off).")]
        [SerializeField] private string[] eventsOnCancel = System.Array.Empty<string>();

        // Not serialized (also not by the editor's domain-reload pass, which would turn it into an empty list).
        [System.NonSerialized] private List<InteractionStep> fallback;

        public string Verb => verb;
        public string DeviceName => string.IsNullOrEmpty(deviceName) ? verb : deviceName;
        public float HoldSeconds => holdSeconds;
        public float MaxSpeedKph => maxSpeedKph;
        public float DecayPerSecond => decayPerSecond;
        public float HeatOnComplete => heatOnComplete;
        public string HeatReason => heatReason;
        public string CompleteMessage => completeMessage;
        public float LockoutSeconds => lockoutSeconds;
        public float Tension => tension;
        public float FailHeat => failHeat;
        public string FailMessage => failMessage;
        public string[] EventsOnFail => eventsOnFail ?? System.Array.Empty<string>();
        public string[] EventsOnCancel => eventsOnCancel ?? System.Array.Empty<string>();
        /// <summary>True if this is a multi-stage interaction (shows the terminal readout).</summary>
        public bool IsSequence => steps != null && steps.Count > 0;

        public IReadOnlyList<InteractionStep> Steps
        {
            get
            {
                if (IsSequence) return steps;
                if (fallback == null || fallback.Count == 0)
                    fallback = new List<InteractionStep> { new(verb, InteractionStepKind.Hold, Mathf.Max(0.1f, holdSeconds)) };
                return fallback;
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(string promptVerb, float hold, float maxSpeed, float decay, float heat, string reason, string message)
        {
            verb = promptVerb;
            holdSeconds = hold;
            maxSpeedKph = maxSpeed;
            decayPerSecond = decay;
            heatOnComplete = heat;
            heatReason = reason;
            completeMessage = message;
            steps = new List<InteractionStep>();
            fallback = null;
        }

        public void EditorConfigureStages(string device, List<InteractionStep> stages, float lockout, float heatOnFail, string onFailMessage, string[] onFail,
                                          float tensionLevel)
        {
            tension = tensionLevel;
            deviceName = device;
            steps = stages ?? new List<InteractionStep>();
            lockoutSeconds = lockout;
            failHeat = heatOnFail;
            failMessage = onFailMessage;
            eventsOnFail = onFail ?? System.Array.Empty<string>();
            fallback = null;
        }

        public void EditorConfigureCancel(string[] onCancel) => eventsOnCancel = onCancel ?? System.Array.Empty<string>();
#endif
    }
}
