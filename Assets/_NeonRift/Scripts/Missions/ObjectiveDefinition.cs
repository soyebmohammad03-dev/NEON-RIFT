using System;
using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>
    /// One step of a mission. Objectives run in order; each one names a scene target (zone or interactable id),
    /// what it means for security, and which world events it fires. Scene objects react to those event ids,
    /// so a mission is assembled from data without mission-specific code.
    /// </summary>
    [Serializable]
    public sealed class ObjectiveDefinition
    {
        [Tooltip("Stable id, unique within the mission.")]
        [SerializeField] private string id;
        [SerializeField] private ObjectiveKind kind;
        [Tooltip("Id of the MissionZone (Reach) or Interactable (Interact) in the mission scene.")]
        [SerializeField] private string targetId;
        [Tooltip("Short HUD line, e.g. REACH THE DATA CORE.")]
        [SerializeField] private string title;
        [Tooltip("Secondary HUD line with the how-to.")]
        [SerializeField] private string detail;
        [Tooltip("Security is raised to at least this level when the objective starts.")]
        [SerializeField] private SecurityLevel securityLevel;
        [Tooltip("Seconds to complete the objective (0 = untimed). Heat shortens it, see the mission's heat penalty.")]
        [SerializeField, Min(0f)] private float timeLimit;
        [Tooltip("Shown as the failure reason when the time limit runs out.")]
        [SerializeField] private string timeoutReason = "TIME EXPIRED";
        [Tooltip("Seconds between the previous objective completing and this one starting (a beat for the story; nothing is current meanwhile).")]
        [SerializeField, Min(0f)] private float startDelay;
        [Tooltip("World events fired when the objective starts.")]
        [SerializeField] private string[] eventsOnStart = Array.Empty<string>();
        [Tooltip("World events fired when the objective is completed.")]
        [SerializeField] private string[] eventsOnComplete = Array.Empty<string>();

        [Header("Rivals")]
        [Tooltip("Race marker id rival drivers head for during this objective (empty = they hold where they are).")]
        [SerializeField] private string rivalGoalId;
        [Tooltip("Seconds rivals wait after the objective starts before they set off (reaction time).")]
        [SerializeField, Min(0f)] private float rivalStartDelay;

        public string Id => id;
        public ObjectiveKind Kind => kind;
        public string TargetId => targetId;
        public string Title => title;
        public string Detail => detail;
        public SecurityLevel SecurityLevel => securityLevel;
        public float TimeLimit => timeLimit;
        public string TimeoutReason => timeoutReason;
        public string[] EventsOnStart => eventsOnStart ?? Array.Empty<string>();
        public string[] EventsOnComplete => eventsOnComplete ?? Array.Empty<string>();
        public string RivalGoalId => rivalGoalId;
        public float RivalStartDelay => rivalStartDelay;
        public float StartDelay => startDelay;

        public ObjectiveDefinition() { }

        public ObjectiveDefinition(string id, ObjectiveKind kind, string targetId, string title, string detail,
                                   SecurityLevel securityLevel = SecurityLevel.Calm, float timeLimit = 0f, string timeoutReason = "TIME EXPIRED",
                                   string[] eventsOnStart = null, string[] eventsOnComplete = null, string rivalGoalId = null, float rivalStartDelay = 0f,
                                   float startDelay = 0f)
        {
            this.startDelay = startDelay;
            this.rivalGoalId = rivalGoalId;
            this.rivalStartDelay = rivalStartDelay;
            this.id = id;
            this.kind = kind;
            this.targetId = targetId;
            this.title = title;
            this.detail = detail;
            this.securityLevel = securityLevel;
            this.timeLimit = timeLimit;
            this.timeoutReason = timeoutReason;
            this.eventsOnStart = eventsOnStart ?? Array.Empty<string>();
            this.eventsOnComplete = eventsOnComplete ?? Array.Empty<string>();
        }
    }
}
