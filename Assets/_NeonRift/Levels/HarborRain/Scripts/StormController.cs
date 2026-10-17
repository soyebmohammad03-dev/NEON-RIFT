using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The storm: camera-following rain driven diagonally by one world-space wind (blowing in off the sea from the
    /// north-east), ground splashes, lightning (a shadowless directional flash, an ambient lift and lit clouds), thunder
    /// delayed by the strike's distance (speed of sound, capped), a layered audio bed (heavy rain, wind, random gusts)
    /// that grows with <see cref="Intensity"/> and ducks under the radio, and fog that thickens with intensity.
    /// </summary>
    public sealed class StormController : MonoBehaviour
    {
        [SerializeField] private ParticleSystem rain;
        [SerializeField] private ParticleSystem splashes;
        [SerializeField] private Light lightning;
        [SerializeField] private AudioSource rainAudio;
        [SerializeField] private AudioSource windAudio;
        [SerializeField] private AudioSource gustAudio;
        [SerializeField] private AudioSource thunderAudio;
        [SerializeField] private AudioClip[] thunderClips = new AudioClip[0];
        [SerializeField] private AudioClip[] gustClips = new AudioClip[0];
        [SerializeField] private Vector2 rainRate = new(1800f, 5200f);
        [SerializeField] private Vector2 splashRate = new(160f, 520f);
        [SerializeField] private Vector2 fogDensity = new(0.009f, 0.017f);
        [Tooltip("Seconds between strikes at low / full intensity.")]
        [SerializeField] private Vector2 strikeInterval = new(18f, 6f);
        [SerializeField, Range(0f, 1f)] private float startIntensity = 0.45f;
        [Header("Wind")]
        [SerializeField] private Vector3 windDirection = new(-0.6f, 0f, -0.8f);
        [SerializeField] private Vector2 windSpeed = new(5f, 14f);
        [Header("Audio levels (linear, before mixer)")]
        [SerializeField] private Vector2 rainVolume = new(0.55f, 1f);
        [SerializeField] private Vector2 windVolume = new(0.3f, 0.85f);

        public float Intensity { get; private set; }
        /// <summary>World-space wind velocity (m/s) including gusts.</summary>
        public Vector3 Wind => windDirection.normalized * (Mathf.Lerp(windSpeed.x, windSpeed.y, Intensity) * (1f + gust * 0.5f));
        public Vector3 WindDirection => windDirection.normalized;
        public float LastThunderDelay { get; private set; }
        /// <summary>Lightning flash 0..1 (for the ocean shader and anything else that should catch it).</summary>
        public float Flash { get; private set; }
        /// <summary>Current gust strength 0..1 (crosswind on exposed roads).</summary>
        public float Gust => gust;
        public int Strikes { get; private set; }
        public float RainVolumeNow => rainAudio != null ? rainAudio.volume : 0f;
        public bool RainPlaying => rainAudio != null && rainAudio.isPlaying;

        private float target, rampRate, nextStrike, flashT = -1f, thunderAt = -1f, thunderGain, gust, nextGust;
        private Color baseAmbient;
        private bool ambientCaptured;
        private Transform follow;
        private Material sky;
        private Color skyClouds;
        private static readonly int CloudColor = Shader.PropertyToID("_CloudColor");

        private void Awake() => ResetStorm();

        public void ResetStorm()
        {
            Intensity = target = startIntensity;
            nextStrike = Time.time + 6f;
            nextGust = Time.time + 8f;
            flashT = thunderAt = -1f;
            Strikes = 0;
            if (lightning != null) lightning.intensity = 0f;
            Apply();
        }

        /// <summary>Ramps the storm to <paramref name="value"/> over <paramref name="seconds"/>.</summary>
        public void Escalate(float value, float seconds)
        {
            target = Mathf.Clamp01(value);
            rampRate = Mathf.Abs(target - Intensity) / Mathf.Max(0.1f, seconds);
            nextStrike = Mathf.Min(nextStrike, Time.time + 1.5f);
        }

        /// <summary>A strike at a random distance: the flash now, thunder after distance / 343 m/s (capped at 6 s).</summary>
        public void Strike(float distance = -1f)
        {
            if (distance < 0f) distance = Random.Range(250f, 2600f) * Mathf.Lerp(1f, 0.55f, Intensity);
            flashT = 0f;
            Strikes++;
            LastThunderDelay = Mathf.Min(6f, distance / 343f);
            thunderAt = Time.time + LastThunderDelay;
            thunderGain = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(250f, 2600f, distance));
            nextStrike = Time.time + Mathf.Lerp(strikeInterval.x, strikeInterval.y, Intensity) * Random.Range(0.7f, 1.3f);
        }

        private void LateUpdate()
        {
            if (!ambientCaptured)
            {
                baseAmbient = RenderSettings.ambientSkyColor;
                ambientCaptured = true;
                // A runtime copy of the sky so lightning can light the cloud deck without touching the asset.
                if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty(CloudColor))
                {
                    sky = new Material(RenderSettings.skybox);
                    skyClouds = sky.GetColor(CloudColor);
                    RenderSettings.skybox = sky;
                }
            }
            if (follow == null && Camera.main != null) follow = Camera.main.transform;
            var wind = Wind;
            if (follow != null)
            {
                // Rain volume rides with the camera, offset upwind so the slanted streaks fill the view.
                var p = follow.position + Vector3.ProjectOnPlane(follow.forward, Vector3.up).normalized * 8f;
                if (rain != null) rain.transform.position = p + Vector3.up * 16f - wind * 0.5f;
                if (splashes != null) splashes.transform.position = new Vector3(p.x, 0.04f, p.z);
            }

            if (!Mathf.Approximately(Intensity, target))
                Intensity = Mathf.MoveTowards(Intensity, target, rampRate * Time.deltaTime);

            // Gusts: a swell in wind speed with a gust whoosh.
            if (Time.time >= nextGust)
            {
                nextGust = Time.time + Random.Range(Mathf.Lerp(14f, 5f, Intensity), Mathf.Lerp(24f, 10f, Intensity));
                gust = 1f;
                if (gustAudio != null && gustClips.Length > 0)
                    gustAudio.PlayOneShot(gustClips[Random.Range(0, gustClips.Length)], Mathf.Lerp(0.45f, 1f, Intensity) * (1f - HarborRadio.Duck * 0.4f));
            }
            gust = Mathf.MoveTowards(gust, 0f, Time.deltaTime * 0.35f);
            Apply();

            if (Time.time >= nextStrike) Strike();
            if (flashT >= 0f)
            {
                flashT += Time.deltaTime;
                // Two quick pulses then a fade.
                float f = flashT < 0.08f ? 1f : flashT < 0.16f ? 0.25f : flashT < 0.24f ? 0.85f : Mathf.Max(0f, 1f - (flashT - 0.24f) / 0.5f) * 0.6f;
                if (lightning != null) lightning.intensity = f * Mathf.Lerp(3.5f, 7f, Intensity);
                Flash = f;
                RenderSettings.ambientSkyColor = baseAmbient + new Color(0.18f, 0.2f, 0.26f) * f;
                if (sky != null) sky.SetColor(CloudColor, skyClouds + new Color(0.5f, 0.55f, 0.7f) * f);
                if (flashT > 0.8f)
                {
                    flashT = -1f;
                    if (lightning != null) lightning.intensity = 0f;
                    Flash = 0f;
                    RenderSettings.ambientSkyColor = baseAmbient;
                    if (sky != null) sky.SetColor(CloudColor, skyClouds);
                }
            }
            if (thunderAt > 0f && Time.time >= thunderAt)
            {
                thunderAt = -1f;
                if (thunderAudio != null && thunderClips.Length > 0)
                    thunderAudio.PlayOneShot(thunderClips[thunderGain > 0.8f ? 0 : Random.Range(1, thunderClips.Length)], thunderGain);
            }
        }

        private void Apply()
        {
            var wind = Wind;
            if (rain != null)
            {
                var e = rain.emission; e.rateOverTime = Mathf.Lerp(rainRate.x, rainRate.y, Intensity);
                var v = rain.velocityOverLifetime;
                v.x = new ParticleSystem.MinMaxCurve(wind.x * 0.8f, wind.x * 1.1f);
                v.z = new ParticleSystem.MinMaxCurve(wind.z * 0.8f, wind.z * 1.1f);
            }
            if (splashes != null) { var e = splashes.emission; e.rateOverTime = Mathf.Lerp(splashRate.x, splashRate.y, Intensity); }
            float duck = 1f - HarborRadio.Duck * 0.45f;
            if (rainAudio != null) rainAudio.volume = Mathf.Lerp(rainVolume.x, rainVolume.y, Intensity) * duck;
            if (windAudio != null)
            {
                windAudio.volume = Mathf.Lerp(windVolume.x, windVolume.y, Intensity) * (0.8f + gust * 0.4f) * duck;
                windAudio.pitch = 0.9f + Intensity * 0.15f + gust * 0.12f;
            }
            RenderSettings.fogDensity = Mathf.Lerp(fogDensity.x, fogDensity.y, Intensity);
        }

        private void OnDisable()
        {
            if (ambientCaptured) RenderSettings.ambientSkyColor = baseAmbient;
        }
    }
}
