using UnityEngine;

namespace NeonRift.Audio
{
    /// <summary>
    /// Runtime front for the mixer: user volume channels and state snapshots (menus, results, lockdown, ducking).
    /// Owned by the composition root and handed to scenes through the game context.
    /// </summary>
    public sealed class AudioMixerService
    {
        /// <summary>Linear volume treated as silence, mapped to −80 dB.</summary>
        private const float Silence = 0.0001f;

        private readonly AudioMixerConfig config;

        public MixerState State { get; private set; } = MixerState.Gameplay;
        public bool IsAvailable => config != null && config.Mixer != null;

        public AudioMixerService(AudioMixerConfig config) => this.config = config;

        /// <summary>Blends to the snapshot for <paramref name="state"/> over <paramref name="seconds"/>.</summary>
        public void TransitionTo(MixerState state, float seconds = 0.5f)
        {
            if (!IsAvailable) return;
            var name = config.SnapshotName(state);
            var snapshot = string.IsNullOrEmpty(name) ? null : config.Mixer.FindSnapshot(name);
            if (snapshot == null)
            {
                Debug.LogWarning($"[Audio] No snapshot for state {state}.");
                return;
            }
            snapshot.TransitionTo(Mathf.Max(0f, seconds));
            State = state;
        }

        /// <summary>Sets a user volume (0..1 linear, perceptual curve applied by dB conversion).</summary>
        public void SetVolume(AudioChannel channel, float linear)
        {
            if (!IsAvailable) return;
            var parameter = config.VolumeParameter(channel);
            if (string.IsNullOrEmpty(parameter)) return;
            config.Mixer.SetFloat(parameter, LinearToDecibels(linear));
        }

        public float GetVolume(AudioChannel channel)
        {
            if (!IsAvailable) return 1f;
            var parameter = config.VolumeParameter(channel);
            return !string.IsNullOrEmpty(parameter) && config.Mixer.GetFloat(parameter, out float db) ? DecibelsToLinear(db) : 1f;
        }

        public static float LinearToDecibels(float linear) => 20f * Mathf.Log10(Mathf.Clamp(linear, Silence, 1f));
        public static float DecibelsToLinear(float db) => Mathf.Pow(10f, db / 20f);
    }
}
