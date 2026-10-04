using System;
using System.Collections.Generic;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.Audio;

namespace NeonRift.Audio
{
    /// <summary>
    /// Plays a vehicle's sound from its telemetry: layered engine loops, tyre squeal/scrub, loose-surface, road and
    /// wind beds, gear shifts, overrun pops, suspension thumps and impacts. Every loop is started once in
    /// <see cref="Configure"/> and then only its volume and pitch change; one-shots use PlayOneShot on dedicated
    /// sources. The same component and code serve every car; differences live in <see cref="VehicleAudioProfile"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleAudio : MonoBehaviour
    {
        /// <summary>Fade-in after configuration so the first buffer never clicks, s.</summary>
        private const float StartFadeTime = 0.35f;
        /// <summary>Smoothing for tyre, road and wind levels, s.</summary>
        private const float BedSmoothing = 0.08f;
        /// <summary>Minimum time between impact sounds, s.</summary>
        private const float ImpactCooldown = 0.12f;
        /// <summary>Minimum time between thumps on one wheel, s.</summary>
        private const float ThumpCooldown = 0.18f;
        /// <summary>Overrun pops only happen this long after lifting off, s.</summary>
        private const float OverrunWindow = 2.5f;

        [Header("Routing")]
        [SerializeField] private AudioMixerGroup engineGroup;
        [SerializeField] private AudioMixerGroup tiresGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;
        [SerializeField] private AudioMixerGroup ambienceGroup;

        [Header("3D")]
        [Tooltip("Spatial blend for cars heard from outside. The player's own car uses the player blend.")]
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
        [Tooltip("Spatial blend for the car the camera follows: mostly 2D so the chase camera does not swing the mix.")]
        [SerializeField, Range(0f, 1f)] private float playerSpatialBlend = 0.25f;
        [SerializeField, Min(0.1f)] private float minDistance = 5f;
        [SerializeField, Min(1f)] private float maxDistance = 250f;

        private VehicleController vehicle;
        private VehicleAudioProfile profile;
        private EngineSoundModel engineModel;
        private AudioSource[] engineSources = Array.Empty<AudioSource>();
        private float[] layerVolumes = Array.Empty<float>();
        private float[] layerPitches = Array.Empty<float>();
        private AudioSource skid, scrub, loose, road, wind, engineOneShots, sfxOneShots;
        private float[] thumpTimers = Array.Empty<float>();
        private float startFade;
        private float impactTimer;
        private float liftOffTime = float.NegativeInfinity;
        private float lastThrottle;
        private bool isPlayer;

        public bool IsConfigured => profile != null && vehicle != null;
        public VehicleAudioProfile Profile => profile;
        /// <summary>Number of AudioSource.Play() calls made. Equals the loop count after configuration and only grows when
        /// the output device changes (the audio system stops every source then, and the loops are restarted).</summary>
        public int PlayCalls { get; private set; }
        /// <summary>One-shots played (shifts, pops, thumps, impacts).</summary>
        public int OneShotCount { get; private set; }
        public int ShiftSounds { get; private set; }
        public int PopSounds { get; private set; }
        public int ThumpSounds { get; private set; }
        public int ImpactSounds { get; private set; }
        public int LoopCount { get; private set; }
        /// <summary>Smoothed rpm driving the engine loops.</summary>
        public float AudioRpm { get; private set; }
        /// <summary>Smoothed on-load blend, 0..1.</summary>
        public float LoadBlend { get; private set; }
        /// <summary>Overall engine gain this frame, linear.</summary>
        public float EngineGain { get; private set; }
        public float SkidLevel { get; private set; }
        public float ScrubLevel { get; private set; }
        public float LooseLevel { get; private set; }
        public float RoadLevel { get; private set; }
        public float WindLevel { get; private set; }
        public int EngineLayerCount => engineSources.Length;
        public AudioSource EngineSource(int index) => engineSources[index];

        /// <summary>Raised for every impact sound with its category (1 light, 2 medium, 3 heavy).</summary>
        public event Action<int, VehicleCollision> ImpactPlayed;

        /// <summary>Builds the sources for a profile and starts every loop once (silent until <see cref="Update"/> raises it).</summary>
        public void Configure(VehicleAudioProfile audioProfile, VehicleController controller)
        {
            if (audioProfile == null) throw new ArgumentNullException(nameof(audioProfile));
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            Unsubscribe();
            ClearSources();

            profile = audioProfile;
            vehicle = controller;
            var engine = profile.Engine;
            var chassis = profile.Chassis;
            engineModel = new EngineSoundModel(engine);

            engineSources = new AudioSource[engineModel.LayerCount];
            layerVolumes = new float[engineSources.Length];
            layerPitches = new float[engineSources.Length];
            for (int i = 0; i < engineSources.Length; i++)
                engineSources[i] = CreateLoop($"Engine_{(engine.layers[i].onLoad ? "On" : "Off")}_{engine.layers[i].recordedRpm:0}", engine.layers[i].clip, engineGroup, 0);
            skid = CreateLoop("Tyres_Skid", chassis.skidLoop, tiresGroup, 40);
            scrub = CreateLoop("Tyres_Scrub", chassis.scrubLoop, tiresGroup, 50);
            loose = CreateLoop("Tyres_Loose", chassis.looseSurfaceLoop, tiresGroup, 60);
            road = CreateLoop("Road", chassis.roadLoop, tiresGroup, 70);
            wind = CreateLoop("Wind", chassis.windLoop, ambienceGroup, 90);
            engineOneShots = CreateSource("Engine_OneShots", engineGroup, 20);
            sfxOneShots = CreateSource("SFX_OneShots", sfxGroup, 30);

            thumpTimers = new float[vehicle.Wheels.Count];
            AudioRpm = vehicle.Drivetrain.IdleRpm;
            LoadBlend = 0f;
            startFade = 0f;
            ApplySpatial();

            // Each loop starts exactly once, at a random phase so identical cars do not phase-lock.
            foreach (var source in AllLoops())
            {
                if (source == null || source.clip == null) continue;
                source.timeSamples = UnityEngine.Random.Range(0, source.clip.samples);
                source.Play();
                PlayCalls++;
                LoopCount++;
            }

            vehicle.Drivetrain.Gearbox.GearChanged += OnGearChanged;
            vehicle.Collided += OnCollided;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        /// <summary>A device change (headphones, a new output) resets the audio system and stops every source: restart the loops.</summary>
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (this == null) return;
            foreach (var source in AllLoops())
            {
                if (source == null || source.clip == null || source.isPlaying) continue;
                source.timeSamples = UnityEngine.Random.Range(0, source.clip.samples);
                source.Play();
                PlayCalls++;
            }
        }

        /// <summary>The camera follows this car: mostly 2D, no Doppler.</summary>
        public void SetPlayerView(bool player)
        {
            isPlayer = player;
            ApplySpatial();
        }

        /// <summary>
        /// Added to the priority of every source of a car the camera is not following. A car carries ~20 loops, and
        /// with three cars on the grid the voice limit would otherwise virtualise the music and ambience (default
        /// priority) before any rival engine layer.
        /// </summary>
        public const int OtherCarPriorityOffset = 140;
        private readonly Dictionary<AudioSource, int> basePriority = new();

        private void ApplySpatial()
        {
            foreach (var s in GetComponentsInChildren<AudioSource>(true))
            {
                s.spatialBlend = isPlayer ? playerSpatialBlend : spatialBlend;
                s.dopplerLevel = isPlayer ? 0f : 0.5f;
                if (basePriority.TryGetValue(s, out int priority))
                    s.priority = Mathf.Clamp(priority + (isPlayer ? 0 : OtherCarPriorityOffset), 0, 256);
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the audio state by <paramref name="deltaTime"/>. Called from Update; tools and tests may call it directly.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsConfigured) return;
            float dt = Mathf.Min(deltaTime, 0.1f);
            var t = vehicle.Telemetry;
            startFade = Mathf.MoveTowards(startFade, 1f, dt / StartFadeTime);
            UpdateEngine(t, dt);
            UpdateTyres(t, dt);
            UpdateThumps(dt);
            impactTimer -= dt;
        }

        private void UpdateEngine(in VehicleTelemetry t, float dt)
        {
            var e = profile.Engine;
            AudioRpm += (Mathf.Max(0f, t.EngineRpm) - AudioRpm) * Blend(dt, e.rpmSmoothing);

            float targetLoad = t.EngineTorque < 0f ? 0f : Mathf.Clamp01(t.Throttle);
            if (t.IsShifting) targetLoad *= e.shiftLoad;
            LoadBlend += (targetLoad - LoadBlend) * Blend(dt, targetLoad > LoadBlend ? e.loadAttack : e.loadRelease);

            float rpmLevel = Mathf.Lerp(e.idleVolume, 1f, Mathf.Pow(Mathf.Clamp01(t.NormalizedEngineRpm), 0.6f));
            EngineGain = e.volume * rpmLevel * Mathf.Lerp(e.offLoadVolume, 1f, LoadBlend) * startFade;

            engineModel.Evaluate(AudioRpm, LoadBlend, layerVolumes, layerPitches);
            for (int i = 0; i < engineSources.Length; i++)
            {
                var s = engineSources[i];
                if (s == null) continue;
                s.volume = layerVolumes[i] * EngineGain;
                s.pitch = layerPitches[i];
            }

            // Overrun pops: only shortly after lifting off at high rpm.
            if (lastThrottle > 0.3f && t.Throttle < 0.05f) liftOffTime = Time.time;
            lastThrottle = t.Throttle;
            if (e.popRate > 0f && e.overrunPops != null && e.overrunPops.Length > 0 && t.Throttle < 0.05f
                && Time.time - liftOffTime < OverrunWindow && AudioRpm > e.popMinRpm)
            {
                float rate = e.popRate * Mathf.Clamp01((AudioRpm - e.popMinRpm) / 1500f);
                if (UnityEngine.Random.value < rate * dt)
                {
                    OneShot(engineOneShots, e.overrunPops, e.popVolume * UnityEngine.Random.Range(0.6f, 1f), UnityEngine.Random.Range(0.85f, 1.15f));
                    PopSounds++;
                }
            }
        }

        private void UpdateTyres(in VehicleTelemetry t, float dt)
        {
            var c = profile.Chassis;
            float skidSum = 0f, scrubSum = 0f, looseSum = 0f, grounded = 0f, slipSum = 0f;
            float speed = t.Speed;
            foreach (var w in vehicle.Wheels)
            {
                if (!w.IsGrounded) continue;
                grounded++;
                float loadRatio = w.StaticLoad > 0f ? w.Load / w.StaticLoad : 1f;
                if (w.SurfaceGrip < c.looseSurfaceGrip)
                {
                    looseSum += 1f;
                    continue;
                }
                // Squeal comes mostly from sideways sliding; a tyre dragged straight (lock-up, wheelspin) mostly scrubs.
                float skidAmount = TyreSoundModel.SlipIntensity(w.CombinedSlip, c.skidSlipRange, loadRatio, speed)
                                   * TyreSoundModel.SquealDirectionWeight(w.SlipRatio, w.SlipAngle);
                skidSum += skidAmount;
                slipSum += w.CombinedSlip * skidAmount;
                scrubSum += TyreSoundModel.SlipIntensity(w.CombinedSlip, c.scrubSlipRange, loadRatio, speed) * (1f - skidAmount * 0.6f);
            }
            float k = Blend(dt, BedSmoothing);
            float speedFactor = Mathf.Clamp01(speed / (c.fullSpeedKph * VehicleUnits.KphToMs));
            SkidLevel += (Mathf.Clamp01(skidSum * 0.5f) - SkidLevel) * k;
            ScrubLevel += (Mathf.Clamp01(scrubSum * 0.4f) - ScrubLevel) * k;
            LooseLevel += ((grounded > 0f ? looseSum / 4f : 0f) * TyreSoundModel.Smooth(0.5f, 8f, speed) - LooseLevel) * k;
            RoadLevel += ((grounded / 4f) * Mathf.Sqrt(speedFactor) * (1f - LooseLevel) - RoadLevel) * k;
            WindLevel += (speedFactor * speedFactor - WindLevel) * k;

            Set(skid, SkidLevel * c.skidVolume, 0.9f + 0.15f * Mathf.Clamp01(skidSum > 0f ? slipSum / skidSum - 1f : 0f) + 0.05f * speedFactor);
            Set(scrub, ScrubLevel * c.scrubVolume, 0.85f + 0.3f * speedFactor);
            Set(loose, LooseLevel * c.looseSurfaceVolume, 0.8f + 0.5f * speedFactor);
            Set(road, RoadLevel * c.roadVolume, 0.7f + 0.6f * speedFactor);
            Set(wind, WindLevel * c.windVolume, 0.85f + 0.35f * speedFactor);
        }

        private void UpdateThumps(float dt)
        {
            var c = profile.Chassis;
            if (c.suspensionThumps == null || c.suspensionThumps.Length == 0) return;
            for (int i = 0; i < thumpTimers.Length; i++)
            {
                thumpTimers[i] -= dt;
                var w = vehicle.Wheels[i];
                if (!w.IsGrounded || thumpTimers[i] > 0f || w.CompressionVelocity < c.thumpSpeed) continue;
                thumpTimers[i] = ThumpCooldown;
                float strength = Mathf.Clamp01((w.CompressionVelocity - c.thumpSpeed) / (c.thumpSpeed * 2f) + 0.3f) + (w.OnBumpStop ? 0.3f : 0f);
                OneShot(sfxOneShots, c.suspensionThumps, c.thumpVolume * Mathf.Clamp01(strength), UnityEngine.Random.Range(0.9f, 1.1f));
                ThumpSounds++;
            }
        }

        private void OnGearChanged(int from, int to)
        {
            var e = profile.Engine;
            if (e.shiftClips == null || e.shiftClips.Length == 0 || from == to) return;
            ShiftSounds++;
            OneShot(sfxOneShots, e.shiftClips, e.shiftVolume * (0.6f + 0.4f * LoadBlend), UnityEngine.Random.Range(0.95f, 1.05f));
        }

        private void OnCollided(VehicleCollision collision)
        {
            var c = profile.Chassis;
            if (impactTimer > 0f || collision.Impulse < c.impactThresholds.x) return;
            int category = collision.Impulse >= c.impactThresholds.z ? 3 : collision.Impulse >= c.impactThresholds.y ? 2 : 1;
            var clips = category == 3 ? c.heavyImpacts : category == 2 ? c.mediumImpacts : c.lightImpacts;
            if (clips == null || clips.Length == 0) return;
            impactTimer = ImpactCooldown;
            float level = Mathf.Clamp01(Mathf.Log10(collision.Impulse / c.impactThresholds.x + 1f) / Mathf.Log10(c.impactThresholds.z / c.impactThresholds.x + 1f));
            sfxOneShots.transform.position = collision.Point;
            OneShot(sfxOneShots, clips, c.impactVolume * Mathf.Lerp(0.35f, 1f, level), UnityEngine.Random.Range(0.92f, 1.08f));
            sfxOneShots.transform.localPosition = Vector3.zero;
            ImpactSounds++;
            ImpactPlayed?.Invoke(category, collision);
        }

        private void OneShot(AudioSource source, AudioClip[] clips, float volume, float pitch)
        {
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (clip == null || source == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume * startFade);
            OneShotCount++;
        }

        private static void Set(AudioSource s, float volume, float pitch)
        {
            if (s == null) return;
            s.volume = volume;
            s.pitch = pitch;
        }

        private static float Blend(float dt, float timeConstant) => timeConstant <= 0f ? 1f : 1f - Mathf.Exp(-dt / timeConstant);

        private AudioSource CreateLoop(string name, AudioClip clip, AudioMixerGroup group, int priority)
        {
            if (clip == null) return null;
            var s = CreateSource(name, group, priority);
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            return s;
        }

        private AudioSource CreateSource(string name, AudioMixerGroup group, int priority)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.outputAudioMixerGroup = group;
            s.priority = priority;
            basePriority[s] = priority;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            return s;
        }

        private System.Collections.Generic.IEnumerable<AudioSource> AllLoops()
        {
            foreach (var s in engineSources) yield return s;
            yield return skid;
            yield return scrub;
            yield return loose;
            yield return road;
            yield return wind;
        }

        private void ClearSources()
        {
            foreach (var s in GetComponentsInChildren<AudioSource>(true))
                if (s.transform.parent == transform)
                {
                    if (Application.isPlaying) Destroy(s.gameObject);
                    else DestroyImmediate(s.gameObject);
                }
            engineSources = Array.Empty<AudioSource>();
            PlayCalls = OneShotCount = ShiftSounds = ImpactSounds = PopSounds = ThumpSounds = LoopCount = 0;
        }

        private void Unsubscribe()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (vehicle == null) return;
            vehicle.Drivetrain.Gearbox.GearChanged -= OnGearChanged;
            vehicle.Collided -= OnCollided;
        }

        private void OnDestroy() => Unsubscribe();
    }
}
