using System;
using System.Collections.Generic;

namespace NeonRift.Game
{
    /// <summary>
    /// The game's subtitle channel. Every spoken line goes through here: cinematic and campaign lines are displayed by
    /// the campaign overlay (<see cref="Shown"/>), and Harbor Rain's radio reports its lines here and draws them in its
    /// own HUD card (<see cref="Report"/>), so one setting (Settings ▸ Subtitles, default ON) and one log cover both.
    /// Text-only messages are not spoken and are not subtitles; they never pass through here.
    /// </summary>
    public static class Subtitles
    {
        public struct Entry
        {
            public string Speaker, Text;
            public float Seconds;
            public bool Voiced;
            public float At;
        }

        private static bool enabled = true;

        /// <summary>Player setting (mirrors <see cref="GameSettings.SubtitlesOn"/>).</summary>
        public static bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value) return;
                enabled = value;
                if (!enabled) Clear();
                EnabledChanged?.Invoke(enabled);
            }
        }

        /// <summary>A line to draw (speaker, text, seconds on screen).</summary>
        public static event Action<string, string, float> Shown;
        public static event Action Cleared;
        public static event Action<bool> EnabledChanged;

        /// <summary>Recent spoken lines (newest last, capped), for tests and validation.</summary>
        public static readonly List<Entry> Log = new();
        /// <summary>The line on screen in the overlay right now (null when clear).</summary>
        public static string Current { get; private set; }
        public static string CurrentSpeaker { get; private set; }
        private static float currentUntil;

        /// <summary>Speaks a line through the shared overlay.</summary>
        public static void Show(string speaker, string text, float seconds, bool voiced = true)
        {
            Record(speaker, text, seconds, voiced);
            if (!enabled || string.IsNullOrEmpty(text)) return;
            Current = text;
            CurrentSpeaker = speaker;
            currentUntil = UnityEngine.Time.unscaledTime + seconds;
            Shown?.Invoke(speaker, text, seconds);
        }

        /// <summary>A line drawn by its owner's own UI (Harbor Rain radio card): logged, not drawn by the overlay.</summary>
        public static void Report(string speaker, string text, float seconds, bool voiced) => Record(speaker, text, seconds, voiced);

        /// <summary>Removes any line on screen (skips, scene changes, end of a line).</summary>
        public static void Clear()
        {
            Current = null;
            CurrentSpeaker = null;
            Cleared?.Invoke();
        }

        /// <summary>The overlay calls this when its line has run its time.</summary>
        public static void Expire()
        {
            if (Current != null && UnityEngine.Time.unscaledTime >= currentUntil) { Current = null; CurrentSpeaker = null; }
        }

        private static void Record(string speaker, string text, float seconds, bool voiced)
        {
            Log.Add(new Entry { Speaker = speaker, Text = text, Seconds = seconds, Voiced = voiced, At = UnityEngine.Time.unscaledTime });
            if (Log.Count > 200) Log.RemoveAt(0);
        }
    }
}
