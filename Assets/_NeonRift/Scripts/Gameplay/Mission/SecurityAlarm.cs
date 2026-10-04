using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Lockdown sirens and rotating beacons. Silent and dark until security reaches <see cref="activeFrom"/>, then each
    /// siren fades in after the lockdown wave reaches it. Updates only while active.
    /// </summary>
    public sealed class SecurityAlarm : MonoBehaviour, IMissionWorldComponent
    {
        [SerializeField] private SecurityLevel activeFrom = SecurityLevel.Lockdown;
        [SerializeField] private AudioSource[] sirens = Array.Empty<AudioSource>();
        [Tooltip("Ten sirens overlap across the district: each sits well below the engine and the score.")]
        [SerializeField, Range(0f, 1f)] private float sirenVolume = 0.35f;
        [SerializeField, Min(0.05f)] private float fadeSeconds = 1.5f;
        [Tooltip("Spinning beacon heads (spin about local Y).")]
        [SerializeField] private Transform[] beacons = Array.Empty<Transform>();
        [Tooltip("Lights parented to the beacons; enabled while active.")]
        [SerializeField] private Light[] beaconLights = Array.Empty<Light>();
        [SerializeField] private float spinDegreesPerSecond = 300f;
        [SerializeField, Min(1f)] private float waveSpeed = 140f;
        [Tooltip("Starts the beacons early on these events (a facility's own alarm); sirens still wait for the security level.")]
        [SerializeField] private string[] beaconsOn = Array.Empty<string>();

        private MissionWorld world;
        private float[] delays;
        private float time;

        private void Awake() => SetActive(false);

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.SecurityChanged += OnSecurityChanged;
            world.EventRaised += OnWorldEvent;
            SetActive(world.Security >= activeFrom);
        }

        private void OnWorldEvent(string eventId)
        {
            if (enabled || !MissionWorld.Matches(beaconsOn, eventId)) return;
            delays = null;
            time = 0f;
            SetActive(true);
            foreach (var s in sirens) if (s != null) s.Stop();
            early = true;
        }

        private bool early;

        public void Unbind()
        {
            if (world != null)
            {
                world.SecurityChanged -= OnSecurityChanged;
                world.EventRaised -= OnWorldEvent;
            }
            world = null;
            early = false;
            SetActive(false);
        }

        private void OnSecurityChanged(SecurityLevel level)
        {
            if (level < activeFrom || (enabled && !early)) return;
            early = false;
            delays = new float[sirens.Length];
            for (int i = 0; i < sirens.Length; i++)
                if (sirens[i] != null) delays[i] = Vector3.Distance(sirens[i].transform.position, world.AlertOrigin) / waveSpeed;
            time = 0f;
            SetActive(true);
            foreach (var s in sirens)
                if (s != null) { s.volume = 0f; s.loop = true; s.Play(); }
        }

        private void SetActive(bool active)
        {
            enabled = active;
            foreach (var b in beacons) if (b != null) b.gameObject.SetActive(active);
            foreach (var l in beaconLights) if (l != null) l.enabled = active;
            if (!active) foreach (var s in sirens) if (s != null) s.Stop();
        }

        private void Update()
        {
            time += Time.deltaTime;
            for (int i = 0; i < sirens.Length; i++)
                if (sirens[i] != null && delays != null)
                    sirens[i].volume = sirenVolume * Mathf.Clamp01((time - delays[i]) / fadeSeconds);
            float step = spinDegreesPerSecond * Time.deltaTime;
            foreach (var b in beacons) if (b != null) b.Rotate(0f, step, 0f, Space.Self);
        }

#if UNITY_EDITOR
        public void EditorConfigure(AudioSource[] sirenSources, Transform[] spinning, Light[] lights)
        {
            sirens = sirenSources ?? Array.Empty<AudioSource>();
            beacons = spinning ?? Array.Empty<Transform>();
            beaconLights = lights ?? Array.Empty<Light>();
        }

        public void EditorConfigureEvents(string[] earlyBeacons) => beaconsOn = earlyBeacons ?? Array.Empty<string>();
#endif
    }
}
