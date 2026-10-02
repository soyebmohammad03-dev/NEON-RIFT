using System;
using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>A HUD message shown when a world event fires (mission narrative lives in data, not code).</summary>
    [Serializable]
    public struct MissionAnnouncement
    {
        [Tooltip("World event id that triggers the message.")]
        public string eventId;
        public string text;
        public MessageTone tone;
        [Tooltip("Delay after the event, s.")]
        [Min(0f)] public float delay;
        [Tooltip("Show as a full-width banner instead of a toast. Text before '|' is the title, after it the subtitle.")]
        public bool banner;

        public MissionAnnouncement(string eventId, string text, MessageTone tone, float delay = 0f, bool banner = false)
        {
            this.eventId = eventId;
            this.text = text;
            this.tone = tone;
            this.delay = delay;
            this.banner = banner;
        }
    }
}
