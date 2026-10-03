using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.Audio;

namespace NeonRift.Frontend
{
    /// <summary>
    /// Engine sound for a car standing in the garage, from the car's own recordings in its
    /// <see cref="VehicleAudioProfile"/>: the lowest off-load loop idles at the profile's idle rpm, and a blip (selection,
    /// departure) sweeps the rpm up and back while crossfading into an on-load loop. Pitch is always rpm / recorded rpm,
    /// as in the driving model, so every car sounds like itself. No physics is involved: the showroom car is frozen.
    /// </summary>
    public sealed class ShowroomEngine : MonoBehaviour
    {
        [SerializeField] private AudioMixerGroup output;
        [SerializeField, Range(0f, 1f)] private float volume = 0.6f;
        [Tooltip("Seconds for the rpm to reach the blip peak, and the decay time back to idle.")]
        [SerializeField] private Vector2 blipRiseFall = new(0.16f, 0.85f);

        private AudioSource idle, rev;
        private float idleRecorded = 1f, revRecorded = 1f, idleRpm = 800f, maxRpm = 7000f, minPitch = 0.5f, maxPitch = 2f;
        private float idleLevel = 0.5f, engineVolume = 1f;
        private float rpm, peakRpm, blipStart = -10f, hold;
        private float master, masterTarget, fadeSpeed = 1f;

        public float Rpm => rpm;
        public bool IsPlaying => idle != null && idle.isPlaying;

        private AudioSource Source(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0.7f;
            s.minDistance = 4f;
            s.maxDistance = 40f;
            s.dopplerLevel = 0f;
            s.outputAudioMixerGroup = output;
            return s;
        }

        /// <summary>Starts idling with <paramref name="vehicle"/>'s sound, fading in over <paramref name="fadeIn"/> s.</summary>
        public void Play(VehicleDefinition vehicle, float fadeIn = 0.6f)
        {
            var profile = vehicle != null ? vehicle.AudioProfile : null;
            var layers = profile != null ? profile.Engine.layers : null;
            if (layers == null || layers.Length == 0 || vehicle.PhysicsProfile == null) { Stop(0.2f); return; }
            if (idle == null) idle = Source("ShowroomEngine_Idle");
            if (rev == null) rev = Source("ShowroomEngine_Rev");

            var engine = vehicle.PhysicsProfile.Engine;
            idleRpm = Mathf.Max(500f, engine.idleRpm);
            maxRpm = Mathf.Max(idleRpm + 1000f, engine.maxRpm);
            var settings = profile.Engine;
            minPitch = settings.minPitch;
            maxPitch = settings.maxPitch;
            idleLevel = Mathf.Max(0.2f, settings.idleVolume);
            engineVolume = Mathf.Max(0.1f, settings.volume);

            EngineSoundLayer low = layers[0], high = layers[0];
            float lowRpm = float.MaxValue, highScore = float.MaxValue;
            foreach (var l in layers)
            {
                if (l.clip == null) continue;
                if (!l.onLoad && l.recordedRpm < lowRpm) { low = l; lowRpm = l.recordedRpm; }
                float score = Mathf.Abs(l.recordedRpm - Mathf.Lerp(idleRpm, maxRpm, 0.55f)) - (l.onLoad ? 10000f : 0f);
                if (score < highScore) { high = l; highScore = score; }
            }
            if (low.clip == null) low = high;
            Assign(idle, low.clip, out idleRecorded, low.recordedRpm);
            Assign(rev, high.clip, out revRecorded, high.recordedRpm);
            rpm = idleRpm;
            blipStart = -10f;
            masterTarget = 1f;
            fadeSpeed = 1f / Mathf.Max(0.05f, fadeIn);
        }

        private static void Assign(AudioSource s, AudioClip clip, out float recorded, float recordedRpm)
        {
            recorded = Mathf.Max(1f, recordedRpm);
            if (s.clip != clip)
            {
                s.clip = clip;
                s.time = clip != null ? Random.Range(0f, clip.length * 0.9f) : 0f;
            }
            if (clip != null && !s.isPlaying) s.Play();
        }

        /// <summary>A throttle blip to <paramref name="peak"/> of the rev range, held for <paramref name="holdSeconds"/>.</summary>
        public void Blip(float peak = 0.55f, float holdSeconds = 0.05f)
        {
            peakRpm = Mathf.Lerp(idleRpm, maxRpm, Mathf.Clamp01(peak));
            hold = Mathf.Max(0f, holdSeconds);
            blipStart = Time.unscaledTime;
        }

        public void Stop(float fadeOut = 0.4f)
        {
            masterTarget = 0f;
            fadeSpeed = 1f / Mathf.Max(0.05f, fadeOut);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            master = Mathf.MoveTowards(master, masterTarget, fadeSpeed * dt);
            if (idle == null) return;
            if (master <= 0f && masterTarget <= 0f)
            {
                if (idle.isPlaying) idle.Stop();
                if (rev.isPlaying) rev.Stop();
                return;
            }

            // Blip envelope: fast rise, hold, exponential fall (the engine's inertia), in unscaled time.
            float t = Time.unscaledTime - blipStart;
            float env;
            if (t < 0f || t > blipRiseFall.x + hold + blipRiseFall.y * 5f) env = 0f;
            else if (t < blipRiseFall.x) env = Mathf.SmoothStep(0f, 1f, t / blipRiseFall.x);
            else if (t < blipRiseFall.x + hold) env = 1f;
            else env = Mathf.Exp(-(t - blipRiseFall.x - hold) / Mathf.Max(0.05f, blipRiseFall.y * 0.45f));
            rpm = Mathf.Lerp(idleRpm, Mathf.Max(idleRpm, peakRpm), env);

            float load = Mathf.Clamp01(env * 1.4f);
            float level = Mathf.Lerp(idleLevel, 1f, env) * engineVolume * volume * master;
            idle.pitch = Mathf.Clamp(rpm / idleRecorded, minPitch, maxPitch);
            rev.pitch = Mathf.Clamp(rpm / revRecorded, minPitch, maxPitch);
            idle.volume = level * (1f - load);
            rev.volume = level * load;
            if (idle.clip != null && !idle.isPlaying) idle.Play();
            if (rev.clip != null && !rev.isPlaying) rev.Play();
        }

#if UNITY_EDITOR
        public void EditorConfigure(AudioMixerGroup group) => output = group;
#endif
    }
}
