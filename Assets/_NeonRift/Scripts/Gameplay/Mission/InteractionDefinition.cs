using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// What kind of interaction an <see cref="Interactable"/> is (hack a terminal, collect a payload …): how it is
    /// performed and what it costs. Shared by every interactable of that kind, so new mechanics are mostly data.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Missions/Interaction Definition", fileName = "Interaction")]
    public sealed class InteractionDefinition : ScriptableObject
    {
        [Tooltip("Prompt verb, e.g. HACK, COLLECT, OVERRIDE.")]
        [SerializeField] private string verb = "HACK";
        [Tooltip("Hold the interact button this long, s.")]
        [SerializeField, Min(0f)] private float holdSeconds = 2f;
        [Tooltip("The vehicle must be slower than this to start or continue, km/h.")]
        [SerializeField, Min(0f)] private float maxSpeedKph = 10f;
        [Tooltip("Progress lost per second when the button is released, as a fraction of the hold time.")]
        [SerializeField, Min(0f)] private float decayPerSecond = 0.5f;
        [Tooltip("Heat added on completion (0..1). Intrusions raise security and shorten timed objectives.")]
        [SerializeField, Range(0f, 1f)] private float heatOnComplete;
        [Tooltip("Shown on the HUD with the heat gain.")]
        [SerializeField] private string heatReason = "INTRUSION LOGGED";
        [Tooltip("HUD message on completion.")]
        [SerializeField] private string completeMessage = "ACCESS GRANTED";

        public string Verb => verb;
        public float HoldSeconds => holdSeconds;
        public float MaxSpeedKph => maxSpeedKph;
        public float DecayPerSecond => decayPerSecond;
        public float HeatOnComplete => heatOnComplete;
        public string HeatReason => heatReason;
        public string CompleteMessage => completeMessage;

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
        }
#endif
    }
}
