using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A set of security-coloured emissive strips and lights (building crowns, kerb lines, gantries) that change
    /// colour with the security level. A lockdown spreads outward from the theft: each group switches after a delay
    /// proportional to its distance from <see cref="MissionWorld.AlertOrigin"/>, so the city visibly turns on the player.
    /// All renderers in a group share one runtime material (batching-friendly); the group only updates while fading or pulsing.
    /// </summary>
    public sealed class SecurityLightGroup : MonoBehaviour, IMissionWorldComponent
    {
        [Serializable]
        public struct LevelLook
        {
            [ColorUsage(false, true)] public Color emission;
            public Color lightColor;
            [Min(0f)] public float lightIntensity;
            [Tooltip("Pulse depth while in this level (0 = steady).")]
            [Range(0f, 1f)] public float pulse;
        }

        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
        [SerializeField] private Light[] lights = Array.Empty<Light>();
        [SerializeField] private LevelLook calm = new() { emission = new Color(0.03f, 0.22f, 0.3f), lightColor = new Color(0.4f, 0.85f, 1f), lightIntensity = 1f };
        [SerializeField] private LevelLook alert = new() { emission = new Color(2.2f, 1.1f, 0.2f), lightColor = new Color(1f, 0.65f, 0.25f), lightIntensity = 2f };
        [SerializeField] private LevelLook lockdown = new() { emission = new Color(4f, 0.2f, 0.45f), lightColor = new Color(1f, 0.15f, 0.3f), lightIntensity = 3f, pulse = 0.35f };
        [Tooltip("Speed the lockdown spreads through the district, m/s.")]
        [SerializeField, Min(1f)] private float waveSpeed = 140f;
        [SerializeField, Min(0.01f)] private float fadeSeconds = 0.35f;
        [SerializeField, Min(0.1f)] private float pulseHz = 1.2f;
        [Tooltip("Local alert (a facility reacting to an intruder) without raising the city's security level.")]
        [SerializeField] private string[] alertOn = Array.Empty<string>();
        [Tooltip("Back to the city's current look (e.g. the facility's security was disabled).")]
        [SerializeField] private string[] calmOn = Array.Empty<string>();

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MissionWorld world;
        private Material material;
        private LevelLook from, to;
        private float delay, time;

        private void Awake()
        {
            if (renderers.Length > 0 && renderers[0] != null)
            {
                material = new Material(renderers[0].sharedMaterial) { name = renderers[0].sharedMaterial.name + " (runtime)" };
                foreach (var r in renderers) if (r != null) r.sharedMaterial = material;
            }
            from = to = calm;
            Apply(calm, 0f);
            enabled = false;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.SecurityChanged += OnSecurityChanged;
            world.EventRaised += OnWorldEvent;
            from = to = Look(world.Security);
            Apply(to, 0f);
            enabled = to.pulse > 0f;
        }

        public void Unbind()
        {
            if (world != null)
            {
                world.SecurityChanged -= OnSecurityChanged;
                world.EventRaised -= OnWorldEvent;
            }
            world = null;
        }

        private void OnWorldEvent(string eventId)
        {
            if (world == null || world.Security == SecurityLevel.Lockdown) return;
            if (MissionWorld.Matches(alertOn, eventId)) FadeTo(alert);
            else if (MissionWorld.Matches(calmOn, eventId)) FadeTo(Look(world.Security));
        }

        private void FadeTo(LevelLook look)
        {
            from = Current();
            to = look;
            delay = 0f;
            time = 0f;
            enabled = true;
        }

        private void OnSecurityChanged(SecurityLevel level)
        {
            from = Current();
            to = Look(level);
            delay = level == SecurityLevel.Lockdown ? Vector3.Distance(transform.position, world.AlertOrigin) / waveSpeed : 0f;
            time = 0f;
            enabled = true;
        }

        private void Update()
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01((time - delay) / fadeSeconds);
            var look = Lerp(from, to, t);
            Apply(look, time);
            if (t >= 1f && to.pulse <= 0f) enabled = false;
        }

        private LevelLook Current() => Lerp(from, to, Mathf.Clamp01((time - delay) / fadeSeconds));

        private LevelLook Look(SecurityLevel level) => level switch
        {
            SecurityLevel.Lockdown => lockdown,
            SecurityLevel.Alert => alert,
            _ => calm
        };

        private void Apply(LevelLook look, float t)
        {
            float pulse = 1f - look.pulse * (0.5f + 0.5f * Mathf.Sin(t * pulseHz * Mathf.PI * 2f));
            if (material != null) material.SetColor(EmissionColor, look.emission * pulse);
            foreach (var l in lights)
            {
                if (l == null) continue;
                l.color = look.lightColor;
                l.intensity = look.lightIntensity * pulse;
            }
        }

        private static LevelLook Lerp(LevelLook a, LevelLook b, float t) => new()
        {
            emission = Color.Lerp(a.emission, b.emission, t),
            lightColor = Color.Lerp(a.lightColor, b.lightColor, t),
            lightIntensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t),
            pulse = Mathf.Lerp(a.pulse, b.pulse, t)
        };

#if UNITY_EDITOR
        public void EditorConfigure(Renderer[] strips, Light[] groupLights)
        {
            renderers = strips ?? Array.Empty<Renderer>();
            lights = groupLights ?? Array.Empty<Light>();
        }

        public void EditorConfigureEvents(string[] localAlert, string[] localCalm)
        {
            alertOn = localAlert ?? Array.Empty<string>();
            calmOn = localCalm ?? Array.Empty<string>();
        }
#endif
    }
}
