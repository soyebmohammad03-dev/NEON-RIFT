using System;
using NeonRift.Audio;
using UnityEngine;

namespace NeonRift.Game
{
    /// <summary>
    /// Player settings (volumes, music on/off, graphics), kept in PlayerPrefs and applied to the mixer and the
    /// quality settings. Shared by the pause menu, the title screen and the global music toggle (M / gamepad Y).
    /// </summary>
    public sealed class GameSettings
    {
        private const string Prefix = "NeonRift.Settings.";
        private readonly AudioMixerService audio;

        public float Master { get; private set; } = 1f;
        public float Music { get; private set; } = 0.8f;
        public float Effects { get; private set; } = 1f;
        public float Ui { get; private set; } = 0.9f;
        public bool MusicOn { get; private set; } = true;
        public int Quality { get; private set; }
        public bool Fullscreen { get; private set; }
        public bool VSync { get; private set; } = true;

        /// <summary>Raised after any change is applied (the HUD's music hint listens).</summary>
        public event Action Changed;

        public GameSettings(AudioMixerService audioService)
        {
            audio = audioService;
            Quality = QualitySettings.GetQualityLevel();
            Fullscreen = Screen.fullScreen;
        }

        public void Load()
        {
            Master = PlayerPrefs.GetFloat(Prefix + "Master", Master);
            Music = PlayerPrefs.GetFloat(Prefix + "Music", Music);
            Effects = PlayerPrefs.GetFloat(Prefix + "Effects", Effects);
            Ui = PlayerPrefs.GetFloat(Prefix + "UI", Ui);
            MusicOn = PlayerPrefs.GetInt(Prefix + "MusicOn", MusicOn ? 1 : 0) == 1;
            Quality = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Quality", Quality), 0, QualitySettings.names.Length - 1);
            Fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", Fullscreen ? 1 : 0) == 1;
            VSync = PlayerPrefs.GetInt(Prefix + "VSync", VSync ? 1 : 0) == 1;
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "Master", Master);
            PlayerPrefs.SetFloat(Prefix + "Music", Music);
            PlayerPrefs.SetFloat(Prefix + "Effects", Effects);
            PlayerPrefs.SetFloat(Prefix + "UI", Ui);
            PlayerPrefs.SetInt(Prefix + "MusicOn", MusicOn ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Quality", Quality);
            PlayerPrefs.SetInt(Prefix + "Fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "VSync", VSync ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Pushes the audio settings to the mixer (graphics are applied when they change).</summary>
        public void ApplyAudio()
        {
            if (audio == null) return;
            audio.SetVolume(AudioChannel.Master, Master);
            audio.SetVolume(AudioChannel.Music, MusicOn ? Music : 0f);
            audio.SetVolume(AudioChannel.Effects, Effects);
            audio.SetVolume(AudioChannel.UI, Ui);
        }

        public void ApplyGraphics()
        {
            if (QualitySettings.GetQualityLevel() != Quality) QualitySettings.SetQualityLevel(Quality, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
#if !UNITY_EDITOR
            if (Screen.fullScreen != Fullscreen) Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
#endif
        }

        public void SetVolume(AudioChannel channel, float value)
        {
            value = Mathf.Clamp01(value);
            switch (channel)
            {
                case AudioChannel.Master: Master = value; break;
                case AudioChannel.Music: Music = value; break;
                case AudioChannel.Effects: Effects = value; break;
                case AudioChannel.UI: Ui = value; break;
            }
            Commit(audioChanged: true);
        }

        public float GetVolume(AudioChannel channel) => channel switch
        {
            AudioChannel.Master => Master,
            AudioChannel.Music => Music,
            AudioChannel.Effects => Effects,
            _ => Ui
        };

        public void ToggleMusic() => SetMusicOn(!MusicOn);

        public void SetMusicOn(bool on)
        {
            MusicOn = on;
            Commit(audioChanged: true);
        }

        public void SetQuality(int level)
        {
            Quality = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
            Commit(audioChanged: false);
        }

        public void SetFullscreen(bool on)
        {
            Fullscreen = on;
            Commit(audioChanged: false);
        }

        public void SetVSync(bool on)
        {
            VSync = on;
            Commit(audioChanged: false);
        }

        private void Commit(bool audioChanged)
        {
            if (audioChanged) ApplyAudio();
            else ApplyGraphics();
            Save();
            Changed?.Invoke();
        }
    }
}
