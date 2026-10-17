using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Harbor Rain's background music: the game's existing gameplay track (the same licensed clip and level Night Run's
    /// mission audio uses), on the Music mixer group so the player's music volume and the M toggle apply. It starts once
    /// and loops for the whole session — retries, cinematics and mission events never restart it. It eases down under
    /// radio dialogue (HarborRadio.Duck) and comes back smoothly afterwards, sits a little lower in the garage and on the
    /// results card, and keeps playing (ducked by the mixer) while paused, like Night Run's track.
    /// </summary>
    public sealed class HarborMusic : MonoBehaviour
    {
        [SerializeField] private AudioSource source;
        [Tooltip("Gameplay level (Night Run's track volume).")]
        [SerializeField] private float volume = 0.3f;
        [SerializeField, Range(0f, 1f)] private float radioDuck = 0.5f;
        [SerializeField] private float fadeSeconds = 2f;

        public float Level { get; set; } = 1f;
        public bool Playing => source != null && source.isPlaying;
        public int Starts { get; private set; }
        public float Volume => source != null ? source.volume : 0f;

        public void Begin()
        {
            if (source == null || source.clip == null || source.isPlaying) return;
            source.loop = true;
            source.ignoreListenerPause = true;
            source.volume = 0f;
            source.Play();
            Starts++;
        }

        private void Update()
        {
            if (source == null || !source.isPlaying) return;
            float target = volume * Level * Mathf.Lerp(1f, radioDuck, HarborRadio.Duck);
            source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * volume / Mathf.Max(0.1f, fadeSeconds) * 2f);
        }

#if UNITY_EDITOR
        public void EditorConfigure(AudioSource s, float level) { source = s; volume = level; }
#endif
    }
}
