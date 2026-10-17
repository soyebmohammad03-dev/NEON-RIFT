using NeonRift.Game;
using UnityEngine;

namespace NeonRift.Intro
{
    /// <summary>
    /// Campaign bootstrap only: a short Harbor Rain teaser cut into the Night Run opening just before the title — the
    /// city feed breaks up and an intercepted transmission from a stormy container port plays for a few seconds
    /// (frames captured from the real Harbor Rain level, slow drift, static, one radio line with its subtitle), then the
    /// signal drops back to the NEON RIFT title. It hints at what lies beyond Level 1 without showing the heist.
    /// </summary>
    internal sealed class IntroHarborTeaser
    {
        public const float ShotSeconds = 2.15f;
        private const float LeadIn = 0.7f, TailOut = 0.8f;

        private readonly CampaignSession campaign;
        private readonly AudioSource sfx;
        private AudioSource drone;
        private float start = -1f;
        private bool spoke;

        public bool Running => start >= 0f;
        public float Duration => LeadIn + Mathf.Max(1, Frames.Length) * ShotSeconds + TailOut;
        private Texture2D[] Frames => campaign.Definition.TeaserFrames;

        public IntroHarborTeaser(CampaignSession session, AudioSource effects)
        {
            campaign = session;
            sfx = effects;
        }

        public static bool Available(CampaignSession session) =>
            session != null && session.Overlay != null && session.Definition.TeaserFrames != null && session.Definition.TeaserFrames.Length > 0;

        public void Begin(GameObject host)
        {
            start = Time.unscaledTime;
            spoke = false;
            var def = campaign.Definition;
            if (sfx != null && def.TeaserStatic != null) sfx.PlayOneShot(def.TeaserStatic, 0.8f);
            if (def.TeaserDrone != null)
            {
                drone = host.AddComponent<AudioSource>();
                drone.clip = def.TeaserDrone;
                drone.loop = true;
                drone.spatialBlend = 0f;
                drone.volume = 0.75f;
                drone.outputAudioMixerGroup = sfx != null ? sfx.outputAudioMixerGroup : null;
                drone.Play();
            }
            campaign.Overlay.SetHint("SKIP  ·  SPACE / START");
            Debug.Log($"[Intro] Harbor Rain teaser ({Frames.Length} shots, {Duration:0.0}s)");
        }

        /// <summary>Advances the teaser; returns false once it has finished.</summary>
        public bool Tick()
        {
            if (start < 0f) return false;
            var overlay = campaign.Overlay;
            float t = Time.unscaledTime - start;
            var frames = Frames;
            var captions = campaign.Definition.TeaserCaptions;
            if (t >= Duration) { End(); return false; }

            if (!spoke && t >= LeadIn + 0.3f)
            {
                spoke = true;
                campaign.Speak("teaser");
            }

            float body = t - LeadIn;
            int shot = Mathf.Clamp(Mathf.FloorToInt(body / ShotSeconds), 0, frames.Length - 1);
            float u = Mathf.Clamp01((body - shot * ShotSeconds) / ShotSeconds);
            // Hard glitch cuts between shots, heavy static at the head and the tail.
            float edge = Mathf.Min(u, 1f - u) * ShotSeconds;
            float glitch = t < LeadIn ? 1f : t > Duration - TailOut ? Mathf.InverseLerp(Duration - TailOut, Duration, t) * 1.2f : edge < 0.12f ? 0.75f : 0.04f;
            float alpha = t < LeadIn ? Mathf.InverseLerp(0f, LeadIn, t) : t > Duration - 0.25f ? Mathf.InverseLerp(Duration, Duration - 0.25f, t) : 1f;
            float scale = 1.06f + 0.07f * u;
            var drift = new Vector2(((shot & 1) == 0 ? -1.4f : 1.4f) * (u - 0.5f), -0.6f * (u - 0.5f));
            string caption = shot < captions.Length ? captions[shot] : string.Empty;
            int secs = 47 * 60 + 13 + Mathf.FloorToInt(t);
            string code = $"KOWLOON BAY  ·  CAM {7 + shot * 3:00}  ·  02:{secs / 60:00}:{secs % 60:00}";
            overlay.SetTeaser(t < LeadIn * 0.5f ? null : frames[shot], scale, drift, alpha, Mathf.Clamp01(glitch), body < 0f ? string.Empty : caption, code);
            return true;
        }

        public void End()
        {
            if (start < 0f) return;
            start = -1f;
            if (drone != null) { drone.Stop(); Object.Destroy(drone); drone = null; }
            var overlay = campaign.Overlay;
            if (overlay != null)
            {
                overlay.HideTeaser();
                overlay.StopVoice();
                overlay.SetHint(null);
            }
        }
    }
}
