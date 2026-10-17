using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// CONTROL, the crew's remote command centre: one radio channel with unique, prioritised messages. Each line has a
    /// generated voice clip (spoken by the macOS speech synthesiser at build time, band-passed like a radio), a
    /// subtitle card and an entry in the device's message history. Rules: a line plays once unless it is marked
    /// repeatable (then it has a cooldown); a critical line interrupts a routine one; routine lines queue (stale ones
    /// are dropped); background audio ducks while anyone is speaking. Messages are triggered by real mission state
    /// only; nothing here talks to a network.
    /// </summary>
    public sealed class HarborRadio : MonoBehaviour
    {
        public enum Priority { Routine, Important, Critical }

        [Serializable]
        public struct Line
        {
            public string id;
            public string speaker;
            [TextArea] public string text;
            public AudioClip clip;
            public Priority priority;
            [Tooltip("0 = once per run.")] public float cooldown;
        }

        [SerializeField] private Line[] lines = Array.Empty<Line>();
        [SerializeField] private AudioSource voice;
        [SerializeField] private AudioSource fx;
        [SerializeField] private AudioClip squelchIn, squelchOut, staticBurst;
        [SerializeField] private HarborRainHud hud;

        /// <summary>0..1 duck amount for ambience while someone is on the radio (smoothed).</summary>
        public static float Duck { get; private set; }
        public string Current { get; private set; }
        public readonly List<string> Played = new();

        private readonly Dictionary<string, Line> byId = new();
        private readonly Dictionary<string, float> lastPlayed = new();
        private readonly List<(Line line, float queuedAt)> queue = new();
        private float speakingUntil, duckTarget;
        private Line speaking;

        private void Awake()
        {
            foreach (var l in lines) if (!string.IsNullOrEmpty(l.id)) byId[l.id] = l;
        }

        public bool Has(string id) => byId.ContainsKey(id);
        /// <summary>True once a line has been accepted this run (playing, played or queued).</summary>
        public bool Requested(string id) => lastPlayed.ContainsKey(id);

        public void ResetRadio()
        {
            queue.Clear();
            lastPlayed.Clear();
            Played.Clear();
            speakingUntil = 0f;
            Current = null;
            if (voice != null) voice.Stop();
            if (hud != null) hud.ShowRadio(null, null, 0f);
        }

        /// <summary>Queues a line. Returns false when it was suppressed (already played, cooling down or unknown).</summary>
        public bool Say(string id)
        {
            if (!byId.TryGetValue(id, out var line)) { Debug.LogWarning($"[HarborRadio] unknown line {id}"); return false; }
            if (lastPlayed.TryGetValue(id, out float t) && (line.cooldown <= 0f || Time.time - t < line.cooldown)) return false;
            foreach (var q in queue) if (q.line.id == id) return false;
            if (Current == id) return false;
            lastPlayed[id] = Time.time;

            if (line.priority == Priority.Critical && Time.time < speakingUntil && speaking.priority != Priority.Critical)
            {
                Begin(line);   // interrupt routine chatter
                return true;
            }
            if (Time.time >= speakingUntil && queue.Count == 0) Begin(line);
            else
            {
                // Critical lines go to the front; at most three waiting.
                int at = line.priority == Priority.Critical ? 0 : queue.Count;
                queue.Insert(at, (line, Time.time));
                if (queue.Count > 3) queue.RemoveAt(queue.Count - 1);
            }
            return true;
        }

        private void Begin(Line line)
        {
            speaking = line;
            Current = line.id;
            Played.Add(line.id);
            float length = line.clip != null ? line.clip.length : Mathf.Clamp(line.text.Length * 0.06f, 2f, 7f);
            speakingUntil = Time.time + length + 0.5f;
            if (fx != null && squelchIn != null) fx.PlayOneShot(squelchIn, 0.7f);
            if (voice != null)
            {
                voice.Stop();
                voice.clip = line.clip;
                if (line.clip != null) voice.PlayDelayed(0.12f);
            }
            // Every spoken line is subtitled (the radio card); the shared subtitle setting can turn the card off.
            NeonRift.Game.Subtitles.Report(line.speaker, line.text, length, line.clip != null);
            if (hud != null)
            {
                if (NeonRift.Game.Subtitles.Enabled) hud.ShowRadio(line.speaker, line.text, length + 1.6f, line.priority == Priority.Critical);
                hud.AddMessage(line.speaker, line.text, line.priority == Priority.Critical);
            }
            duckTarget = 1f;
        }

        private void Update()
        {
            if (Current != null && Time.time >= speakingUntil)
            {
                if (fx != null && squelchOut != null) fx.PlayOneShot(squelchOut, 0.6f);
                Current = null;
                duckTarget = 0f;
            }
            if (Current == null && queue.Count > 0)
            {
                // Drop routine lines that waited too long to still be true.
                while (queue.Count > 0 && queue[0].line.priority == Priority.Routine && Time.time - queue[0].queuedAt > 12f) queue.RemoveAt(0);
                if (queue.Count > 0) { var next = queue[0].line; queue.RemoveAt(0); Begin(next); }
            }
            Duck = Mathf.MoveTowards(Duck, duckTarget, Time.unscaledDeltaTime * 2.5f);
        }

        /// <summary>Storm interference: occasional static over the channel at high intensity.</summary>
        public void Interference(float intensity)
        {
            if (fx == null || staticBurst == null || Current == null) return;
            if (UnityEngine.Random.value < intensity * Time.deltaTime * 0.6f) fx.PlayOneShot(staticBurst, 0.25f + intensity * 0.3f);
        }

        public IEnumerable<string> LineIds() { foreach (var l in lines) yield return l.id; }
        public int ClipCount() { int n = 0; foreach (var l in lines) if (l.clip != null) n++; return n; }
        public int LineCount => lines.Length;
    }
}
