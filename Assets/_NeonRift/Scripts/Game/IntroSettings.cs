using UnityEngine;
using UnityEngine.Video;

namespace NeonRift.Game
{
    /// <summary>
    /// When and how the opening cinematic plays. Data-driven so it never gets in the way during development:
    /// play it on the first launch only (default), always, or never; the Title screen can always replay it.
    /// The realtime version runs on a Timeline in the city; <see cref="Mode.Video"/> swaps in a pre-rendered clip.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Intro Settings", fileName = "IntroSettings")]
    public sealed class IntroSettings : ScriptableObject
    {
        public enum Policy { FirstLaunchOnly, Always, Never }
        public enum Mode { Realtime, Video }

        [SerializeField] private Policy policy = Policy.FirstLaunchOnly;
        [SerializeField] private Mode mode = Mode.Realtime;
        [Tooltip("Pre-rendered intro, used when Mode is Video.")]
        [SerializeField] private VideoClip video;
        [Tooltip("Allow Space / Start to skip to the title even on the very first launch.")]
        [SerializeField] private bool skippable = true;
        [Tooltip("PlayerPrefs key remembering that the intro has been seen on this machine.")]
        [SerializeField] private string seenKey = "NeonRift.IntroSeen";

        public Mode PlaybackMode => mode == Mode.Video && video != null ? Mode.Video : Mode.Realtime;
        public VideoClip Video => video;
        public bool Skippable => skippable;
        public bool Seen => PlayerPrefs.GetInt(seenKey, 0) == 1;

        public bool ShouldPlayOnBoot() => policy switch
        {
            Policy.Always => true,
            Policy.Never => false,
            _ => !Seen
        };

        public void MarkSeen()
        {
            if (Seen) return;
            PlayerPrefs.SetInt(seenKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Forget that the intro was seen (development: see it again on the next launch).</summary>
        public void ResetSeen()
        {
            PlayerPrefs.DeleteKey(seenKey);
            PlayerPrefs.Save();
        }

#if UNITY_EDITOR
        public void EditorConfigure(Policy introPolicy, bool canSkip)
        {
            policy = introPolicy;
            skippable = canSkip;
        }
#endif
    }
}
