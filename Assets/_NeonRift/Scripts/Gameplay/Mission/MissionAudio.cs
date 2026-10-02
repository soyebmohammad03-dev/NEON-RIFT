using NeonRift.Audio;
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

        private AudioSource ambience, interact, cues, stingers;

        public MissionAudioSet Set => set;

        public void Initialize(AudioMixerConfig mixer)
        {
            if (set == null) return;
            // Explicit Unity null checks (?? / ??= bypass UnityEngine.Object's null semantics).
            if (ambience == null) ambience = Create("Ambience", mixer != null ? mixer.Ambience : null);
            if (interact == null) interact = Create("Interact", mixer != null ? mixer.Sfx : null);
            if (cues == null) cues = Create("Cues", mixer != null ? mixer.UI : null);
            if (stingers == null) stingers = Create("Stingers", mixer != null ? mixer.Sfx : null);

            if (set.Ambience != null && !ambience.isPlaying)
            {
                ambience.clip = set.Ambience;
                ambience.loop = true;
                ambience.volume = set.AmbienceVolume;
                ambience.Play();
            }
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
        }

        private AudioSource Create(string sourceName, UnityEngine.Audio.AudioMixerGroup group)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.outputAudioMixerGroup = group;
            return s;
        }

#if UNITY_EDITOR
        public void EditorConfigure(MissionAudioSet audioSet) => set = audioSet;
#endif
    }
}
