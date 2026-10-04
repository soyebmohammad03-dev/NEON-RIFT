using NeonRift.Audio;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Plays the mission's 2D sounds through the game mixer: ambience on the Ambience group, cues and the interaction
    /// loop on UI/SFX. Sources are created once; loops are started once and only their volume/pitch move afterwards.
    /// </summary>
    public sealed class MissionAudio : MonoBehaviour
    {
        public enum Cue { Objective, InteractComplete, Heat, Lockdown, CountdownTick, Message, Success, Failure }

        [SerializeField] private MissionAudioSet set;
        [SerializeField, Range(0.5f, 2f)] private float interactPitchMin = 0.9f;
        [SerializeField, Range(0.5f, 3f)] private float interactPitchMax = 1.6f;

        private AudioSource ambience, interact, cues, stingers, tension, outro, track;
        private float trackTarget;
        private readonly AudioSource[] score = new AudioSource[ScoreMix.LayerCount];
        private readonly float[] scoreGain = new float[ScoreMix.LayerCount];
        private readonly float[] scoreTarget = new float[ScoreMix.LayerCount];
        private double scoreStart = -1.0, applyAt = -1.0;
        private SecurityLevel security, pendingSecurity;
        private float activity;
        private bool scoreEnded;

        /// <summary>Current gain 0..1 of a score layer (validation and tests).</summary>
        public float ScoreGain(ScoreLayer layer) => scoreGain[(int)layer];
        public bool ScorePlaying => scoreStart >= 0.0 && !scoreEnded;
        public SecurityLevel ScoreSecurity => security;

        public MissionAudioSet Set => set;

        public void Initialize(AudioMixerConfig mixer)
        {
            if (set == null) return;
            // Explicit Unity null checks (?? / ??= bypass UnityEngine.Object's null semantics).
            // Priorities (0 = never virtualised): with three cars' engine loops on the grid the voice limit is
            // reached, and the music, ambience and mission cues must be the last to drop.
            if (ambience == null) ambience = Create("Ambience", mixer != null ? mixer.Ambience : null, 8);
            if (interact == null) interact = Create("Interact", mixer != null ? mixer.Sfx : null, 24);
            if (cues == null) cues = Create("Cues", mixer != null ? mixer.UI : null, 4);
            if (stingers == null) stingers = Create("Stingers", mixer != null ? mixer.Sfx : null, 4);
            if (tension == null) tension = Create("Tension", mixer != null ? mixer.Sfx : null, 16);
            if (set.TensionLoop != null && !tension.isPlaying)
            {
                tension.clip = set.TensionLoop;
                tension.loop = true;
                tension.volume = 0f;
                tension.Play();
            }

            if (set.Ambience != null && !ambience.isPlaying)
            {
                ambience.clip = set.Ambience;
                ambience.loop = true;
                ambience.volume = set.AmbienceVolume;
                ambience.Play();
            }
            StartScore(mixer);
            if (set.InteractLoop != null && !interact.isPlaying)
            {
                interact.clip = set.InteractLoop;
                interact.loop = true;
                interact.volume = 0f;
                interact.Play();
            }
        }

        public void Play(Cue cue, float volume = 1f)
        {
            if (set == null || cues == null) return;
            var clip = set.Cue(cue);
            if (clip == null) return;
            (cue is Cue.Lockdown or Cue.Success or Cue.Failure ? stingers : cues).PlayOneShot(clip, volume);
        }

        /// <summary>Stage feedback of a multi-stage interaction (blips, denials, interference).</summary>
        public void Play(Interactable.Feedback feedback)
        {
            if (set == null || cues == null) return;
            var clip = set.Feedback(feedback);
            if (clip != null) (feedback is Interactable.Feedback.Failed or Interactable.Feedback.Interference ? stingers : cues).PlayOneShot(clip, 0.9f);
        }

        /// <summary>Plays the set's stinger for a world event, if it has one (data acquired, breach detected …).</summary>
        public void OnWorldEvent(string eventId)
        {
            if (set == null || stingers == null) return;
            foreach (var s in set.EventStingers)
                if (s.eventId == eventId && s.clip != null) stingers.PlayOneShot(s.clip, s.volume > 0f ? s.volume : 1f);
        }

        /// <summary>Tension bed under long interactions: <paramref name="level"/> 0..1 sets volume, <paramref name="progress"/> lifts pitch.</summary>
        public void SetTension(float level, float progress)
        {
            activity = level;
            if (tension == null || set == null) return;
            tension.volume = Mathf.MoveTowards(tension.volume, level * set.TensionVolume, Time.deltaTime * (level > tension.volume ? 0.8f : 0.5f));
            tension.pitch = Mathf.Lerp(0.85f, 1.25f, progress);
        }

        /// <summary>Interaction loop level (0 = silent) and progress 0..1 (raises pitch).</summary>
        public void SetInteract(float level, float progress)
        {
            if (interact == null || set == null) return;
            interact.volume = Mathf.MoveTowards(interact.volume, level * set.InteractVolume, Time.deltaTime * 4f);
            interact.pitch = Mathf.Lerp(interactPitchMin, interactPitchMax, progress);
        }

        public void StopLoops()
        {
            if (interact != null) interact.volume = 0f;
            if (tension != null) tension.volume = 0f;
        }

        private void OnEnable() => AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        private void OnDisable() => AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;

        /// <summary>
        /// A device change (headphones, a new output) resets the audio system and stops every source: restart the
        /// loops, and the score stems together on one new DSP tick so they stay sample-locked.
        /// </summary>
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            foreach (var loop in new[] { ambience, tension, interact })
                if (loop != null && loop.clip != null && !loop.isPlaying) loop.Play();
            if (track != null && track.clip != null && !track.isPlaying) track.Play();
            if (scoreStart < 0.0 || scoreEnded || track != null) return;
            double at = AudioSettings.dspTime + 0.2;
            foreach (var stem in score)
                if (stem != null && stem.clip != null)
                {
                    stem.Stop();
                    stem.PlayScheduled(at);
                }
            scoreStart = at;
            if (applyAt >= 0.0) applyAt = at;
            Debug.Log($"[Score] audio configuration changed (device {deviceWasChanged}): loops restarted, stems re-synced");
        }

        // ---------------- Adaptive score ----------------

        /// <summary>Starts every stem on the same DSP tick (sample-locked loops); the mix follows the mission state.</summary>
        private void StartScore(AudioMixerConfig mixer)
        {
            if (scoreStart >= 0.0) return;
            var group = mixer != null ? mixer.Music : null;
            if (set.MusicTrack != null)
            {
                // The licensed background track: one low loop, keeps playing (ducked) while paused.
                if (track == null) track = Create("Music_Track", group, 0);
                track.clip = set.MusicTrack;
                track.loop = true;
                track.volume = 0f;
                track.ignoreListenerPause = true;
                track.Play();
                trackTarget = set.MusicTrackVolume;
                if (outro == null) outro = Create("Score_Outro", group, 0);
                scoreStart = AudioSettings.dspTime;
                scoreEnded = false;
                Debug.Log($"[Music] background track '{set.MusicTrack.name}' at {set.MusicTrackVolume:0.00}");
                return;
            }
            double at = AudioSettings.dspTime + 0.2;
            bool any = false;
            for (int i = 0; i < score.Length; i++)
            {
                var clip = set.ScoreStem((ScoreLayer)i);
                if (clip == null) continue;
                if (score[i] == null) score[i] = Create("Score_" + (ScoreLayer)i, group, 0);
                score[i].clip = clip;
                score[i].loop = true;
                score[i].volume = 0f;
                score[i].PlayScheduled(at);
                any = true;
            }
            if (!any) return;
            if (outro == null) outro = Create("Score_Outro", group, 0);
            scoreStart = at;
            security = pendingSecurity = SecurityLevel.Calm;
            applyAt = -1.0;
            scoreEnded = false;
            Debug.Log($"[Score] {set.ScoreBpm:0} bpm, stems started at dsp {at:0.000}");
        }

        /// <summary>Security drives the score; the change lands on the next half bar (a lockdown hits on the grid).</summary>
        public void SetScoreSecurity(SecurityLevel level)
        {
            if (scoreStart < 0.0 || scoreEnded || level == pendingSecurity) return;
            pendingSecurity = level;
            double halfBar = 2.0 * 60.0 / set.ScoreBpm;
            applyAt = ScoreMix.NextGridTime(scoreStart, AudioSettings.dspTime + 0.05, halfBar);
        }

        /// <summary>
        /// Fades the stems out and plays the success or failure outro on the next beat. Returns false when there is no
        /// score (the caller then plays its plain cue).
        /// </summary>
        public bool EndScore(bool success)
        {
            if (scoreStart < 0.0 || scoreEnded) return false;
            scoreEnded = true;
            var clip = success ? set.OutroSuccess : set.OutroFailure;
            if (clip == null || outro == null) return true;
            double beat = 60.0 / set.ScoreBpm;
            outro.clip = clip;
            outro.loop = false;
            outro.volume = set.ScoreVolume;
            outro.PlayScheduled(ScoreMix.NextGridTime(scoreStart, AudioSettings.dspTime + 0.05, beat));
            return true;
        }

        private void Update()
        {
            if (scoreStart < 0.0 || set == null) return;
            if (track != null)
            {
                float goal = scoreEnded ? 0f : trackTarget;
                track.volume = Mathf.MoveTowards(track.volume, goal, Time.unscaledDeltaTime * (scoreEnded ? 0.4f : 0.25f));
                return;
            }
            if (applyAt >= 0.0 && AudioSettings.dspTime >= applyAt)
            {
                applyAt = -1.0;
                security = pendingSecurity;
            }
            ScoreMix.Targets(security, activity, scoreEnded, scoreTarget);
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < score.Length; i++)
            {
                if (score[i] == null) continue;
                // The stems fade quickly once the outro takes over.
                scoreGain[i] = scoreEnded ? Mathf.MoveTowards(scoreGain[i], 0f, dt / 1.2f) : ScoreMix.Step((ScoreLayer)i, scoreGain[i], scoreTarget[i], dt);
                score[i].volume = scoreGain[i] * set.ScoreVolume;
            }
        }

        private AudioSource Create(string sourceName, UnityEngine.Audio.AudioMixerGroup group, int priority)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.outputAudioMixerGroup = group;
            s.priority = priority;
            return s;
        }

#if UNITY_EDITOR
        public void EditorConfigure(MissionAudioSet audioSet) => set = audioSet;
#endif
    }
}
