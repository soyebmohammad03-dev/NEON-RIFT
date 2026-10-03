using System;
using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>
    /// A tasking for the rival crews, issued when a world event fires: where each crew goes (race marker ids, one per
    /// rival; the last repeats), whether it stops there, whether arriving finishes its race, and the radio line the
    /// HUD shows when it gets there. Gives each crew a role in the same operation (overwatch, scouting, staging) and
    /// makes them react to the heist as it happens instead of waiting for objectives.
    /// </summary>
    [Serializable]
    public struct RivalOrder
    {
        [Tooltip("World event that issues the order (e.g. mission.start, core.extract.begin, core.breached).")]
        public string eventId;
        [Tooltip("Reaction time after the event, s.")]
        [Min(0f)] public float delay;
        [Tooltip("Race marker id per rival (index = rival slot); the last entry is used for any further rivals. " +
                 "\"via>goal\" drives through via markers first (to arrive from the right direction).")]
        public string[] markers;
        [Tooltip("Stop and wait at the goal.")]
        public bool hold;
        [Tooltip("Arriving counts as finishing the race (extraction).")]
        public bool finish;
        [Tooltip("HUD line per rival when it arrives; {name} is the crew name. Empty = silent.")]
        public string[] arrivalMessages;

        public RivalOrder(string eventId, float delay, string[] markers, bool hold, bool finish = false, string[] arrivalMessages = null)
        {
            this.eventId = eventId;
            this.delay = delay;
            this.markers = markers;
            this.hold = hold;
            this.finish = finish;
            this.arrivalMessages = arrivalMessages ?? Array.Empty<string>();
        }

        /// <summary>The marker for rival <paramref name="index"/>.</summary>
        public string MarkerFor(int index) => markers == null || markers.Length == 0 ? null : markers[Mathf.Min(index, markers.Length - 1)];

        /// <summary>The marker ids for rival <paramref name="index"/>, via points first, goal last.</summary>
        public string[] RouteFor(int index)
        {
            string m = MarkerFor(index);
            return string.IsNullOrEmpty(m) ? Array.Empty<string>() : m.Split('>');
        }

        public string MessageFor(int index, string crew) =>
            arrivalMessages == null || index >= arrivalMessages.Length || string.IsNullOrEmpty(arrivalMessages[index]) ? null : arrivalMessages[index].Replace("{name}", crew);
    }
}
