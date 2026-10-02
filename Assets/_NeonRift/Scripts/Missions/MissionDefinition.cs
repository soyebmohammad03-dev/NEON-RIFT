using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>
    /// A playable mission: which scene hosts it, how it is presented, and its ordered objectives.
    /// The scene supplies the targets (zones, interactables) and the reactions to world events; this asset supplies
    /// the sequence, timing and security rules. See <see cref="MissionProgress"/> for the runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Missions/Mission Definition", fileName = "MissionDefinition")]
    public sealed class MissionDefinition : ScriptableObject
    {
        [Tooltip("Stable identifier used for saves and results. Never change after release.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("One line under the title on the mission intro card.")]
        [SerializeField] private string tagline;
        [SerializeField, TextArea(2, 6)] private string briefing;
        [Tooltip("Scene name (must be in Build Profiles scene list) that hosts this mission.")]
        [SerializeField] private string sceneName;
        [Tooltip("Development missions (test tracks) are hidden from players in release builds.")]
        [SerializeField] private bool developmentOnly;

        [Header("Objectives")]
        [SerializeField] private List<ObjectiveDefinition> objectives = new();
        [Tooltip("World events fired when the mission starts.")]
        [SerializeField] private string[] eventsOnStart = Array.Empty<string>();
        [Tooltip("World events fired on success.")]
        [SerializeField] private string[] eventsOnComplete = Array.Empty<string>();
        [Tooltip("World events fired on failure.")]
        [SerializeField] private string[] eventsOnFail = Array.Empty<string>();
        [Tooltip("HUD messages triggered by world events.")]
        [SerializeField] private List<MissionAnnouncement> announcements = new();

        [Header("Rivals")]
        [Tooltip("Rival crews spawned from the vehicle catalog (the cars the player did not pick). 0 = solo run.")]
        [SerializeField, Min(0)] private int maxRivals = 2;

        [Header("Security")]
        [Tooltip("Seconds removed from a timed objective per 1.0 heat (heat is 0..1). Applied when the timer starts, " +
                 "and immediately for heat gained while it runs.")]
        [SerializeField, Min(0f)] private float heatTimePenalty = 25f;
        [Tooltip("A timed objective never starts with less than this, s.")]
        [SerializeField, Min(1f)] private float minimumTimeLimit = 20f;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Tagline => tagline;
        public string Briefing => briefing;
        public string SceneName => sceneName;
        public bool DevelopmentOnly => developmentOnly;
        public IReadOnlyList<ObjectiveDefinition> Objectives => objectives;
        public string[] EventsOnStart => eventsOnStart ?? Array.Empty<string>();
        public string[] EventsOnComplete => eventsOnComplete ?? Array.Empty<string>();
        public string[] EventsOnFail => eventsOnFail ?? Array.Empty<string>();
        public IReadOnlyList<MissionAnnouncement> Announcements => announcements;
        public float HeatTimePenalty => heatTimePenalty;
        public float MinimumTimeLimit => minimumTimeLimit;
        public int MaxRivals => maxRivals;

        /// <summary>Data problems that would break the mission at runtime.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(id)) problems.Add($"'{name}' has no id.");
            if (string.IsNullOrWhiteSpace(sceneName)) problems.Add($"'{name}' has no scene.");
            var ids = new HashSet<string>();
            for (int i = 0; i < objectives.Count; i++)
            {
                var o = objectives[i];
                if (o == null) { problems.Add($"Objective {i} is empty."); continue; }
                if (string.IsNullOrWhiteSpace(o.Id)) problems.Add($"Objective {i} has no id.");
                else if (!ids.Add(o.Id)) problems.Add($"Duplicate objective id '{o.Id}'.");
                if (string.IsNullOrWhiteSpace(o.TargetId)) problems.Add($"Objective '{o.Id}' has no target.");
                if (string.IsNullOrWhiteSpace(o.Title)) problems.Add($"Objective '{o.Id}' has no HUD title.");
            }
            return problems;
        }

#if UNITY_EDITOR
        public void EditorConfigure(string missionId, string missionName, string missionTagline, string missionBriefing, string scene,
                                    List<ObjectiveDefinition> missionObjectives, string[] onStart, string[] onComplete, string[] onFail,
                                    List<MissionAnnouncement> missionAnnouncements, float timePenalty, float minimumTime, int rivals = 2)
        {
            maxRivals = rivals;
            id = missionId;
            displayName = missionName;
            tagline = missionTagline;
            briefing = missionBriefing;
            sceneName = scene;
            developmentOnly = false;
            objectives = missionObjectives;
            eventsOnStart = onStart;
            eventsOnComplete = onComplete;
            eventsOnFail = onFail;
            announcements = missionAnnouncements;
            heatTimePenalty = timePenalty;
            minimumTimeLimit = minimumTime;
        }
#endif
    }
}
