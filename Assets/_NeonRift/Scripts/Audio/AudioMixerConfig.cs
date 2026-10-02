using System;
using UnityEngine;
using UnityEngine.Audio;

namespace NeonRift.Audio
{
    /// <summary>The game's mixer, its routing groups, snapshot names and exposed volume parameters.</summary>
    [CreateAssetMenu(menuName = "Neon Rift/Audio/Audio Mixer Config", fileName = "AudioMixerConfig")]
    public sealed class AudioMixerConfig : ScriptableObject
    {
        [Serializable]
        public struct StateSnapshot
        {
            public MixerState state;
            public string snapshot;
        }

        [Serializable]
        public struct ChannelParameter
        {
            public AudioChannel channel;
            public string parameter;
        }

        [SerializeField] private AudioMixer mixer;
        [Header("Routing")]
        [SerializeField] private AudioMixerGroup engine;
        [SerializeField] private AudioMixerGroup tires;
        [SerializeField] private AudioMixerGroup sfx;
        [SerializeField] private AudioMixerGroup ambience;
        [SerializeField] private AudioMixerGroup music;
        [SerializeField] private AudioMixerGroup ui;
        [Header("States and volumes")]
        [SerializeField] private StateSnapshot[] snapshots = Array.Empty<StateSnapshot>();
        [SerializeField] private ChannelParameter[] volumeParameters = Array.Empty<ChannelParameter>();

        public AudioMixer Mixer => mixer;
        public AudioMixerGroup Engine => engine;
        public AudioMixerGroup Tires => tires;
        public AudioMixerGroup Sfx => sfx;
        public AudioMixerGroup Ambience => ambience;
        public AudioMixerGroup Music => music;
        public AudioMixerGroup UI => ui;

        public string SnapshotName(MixerState state)
        {
            foreach (var s in snapshots) if (s.state == state) return s.snapshot;
            return null;
        }

        public string VolumeParameter(AudioChannel channel)
        {
            foreach (var p in volumeParameters) if (p.channel == channel) return p.parameter;
            return null;
        }

#if UNITY_EDITOR
        public void EditorConfigure(AudioMixer audioMixer, AudioMixerGroup engineGroup, AudioMixerGroup tiresGroup, AudioMixerGroup sfxGroup,
                                    AudioMixerGroup ambienceGroup, AudioMixerGroup musicGroup, AudioMixerGroup uiGroup,
                                    StateSnapshot[] stateSnapshots, ChannelParameter[] parameters)
        {
            mixer = audioMixer;
            engine = engineGroup;
            tires = tiresGroup;
            sfx = sfxGroup;
            ambience = ambienceGroup;
            music = musicGroup;
            ui = uiGroup;
            snapshots = stateSnapshots;
            volumeParameters = parameters;
        }
#endif
    }
}
