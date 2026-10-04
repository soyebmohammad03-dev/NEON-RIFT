using System;
using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>
    /// One stage of a rival crew's own heist, run in parallel with the player's: drive to a race marker (per crew,
    /// "via&gt;goal" routes allowed), stop there and work for a while (a relay hack, an uplink), then move on. The
    /// last stage is usually the extraction, where arriving finishes the crew's race. Crews progress through their
    /// task lists independently, so they are real competitors rather than escorts.
    /// </summary>
    [Serializable]
    public struct RivalTask
    {
        [Tooltip("Race marker route per rival (index = rival slot; the last entry repeats). \"via>goal\" drives through via markers first.")]
        public string[] markers;
        [Tooltip("Race-panel status while driving to the goal, e.g. TO RELAY.")]
        public string driveLabel;
        [Tooltip("Race-panel status while working at the goal, e.g. HACKING RELAY. Empty with 0 work = drive through.")]
        public string workLabel;
        [Tooltip("Seconds stopped at the goal (the crew's hack). 0 = no stop.")]
        [Min(0f)] public float workSeconds;
        [Tooltip("Random spread of the work time, ± fraction (0.15 = ±15 %).")]
        [Range(0f, 0.5f)] public float workJitter;
        [Tooltip("Arriving at this goal finishes the crew's race (extraction).")]
        public bool finish;
        [Tooltip("HUD line per rival when the work is done; {name} is the crew name. Empty = silent.")]
        public string[] doneMessages;

        public RivalTask(string[] markers, string driveLabel, string workLabel = null, float workSeconds = 0f, float workJitter = 0f,
                         bool finish = false, string[] doneMessages = null)
        {
            this.markers = markers;
            this.driveLabel = driveLabel;
            this.workLabel = workLabel;
            this.workSeconds = workSeconds;
            this.workJitter = workJitter;
            this.finish = finish;
            this.doneMessages = doneMessages ?? Array.Empty<string>();
        }

        public bool HasWork => workSeconds > 0f;

        /// <summary>The marker ids for rival <paramref name="index"/>, via points first, goal last.</summary>
        public string[] RouteFor(int index)
        {
            if (markers == null || markers.Length == 0) return Array.Empty<string>();
            string m = markers[Mathf.Min(index, markers.Length - 1)];
            return string.IsNullOrEmpty(m) ? Array.Empty<string>() : m.Split('>');
        }

        public string MessageFor(int index, string crew) =>
            doneMessages == null || index >= doneMessages.Length || string.IsNullOrEmpty(doneMessages[index]) ? null : doneMessages[index].Replace("{name}", crew);
    }
}
