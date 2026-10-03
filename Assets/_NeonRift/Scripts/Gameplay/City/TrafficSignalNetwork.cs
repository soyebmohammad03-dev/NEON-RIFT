using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Runs the city's traffic signals: every north–south and east–west lens shares one runtime material per colour, so
    /// the whole grid cycles (green, amber, all-red) with six material updates. In a lockdown the grid flashes red.
    /// Works without a mission (free roam); binds to one for the lockdown look.
    /// </summary>
    public sealed class TrafficSignalNetwork : MonoBehaviour, IMissionWorldComponent
    {
        [Tooltip("Lens renderers: N–S red, amber, green, then E–W red, amber, green.")]
        [SerializeField] private Renderer[] lenses = new Renderer[6];
        [SerializeField, Min(1f)] private float greenSeconds = 14f;
        [SerializeField, Min(0.5f)] private float amberSeconds = 3f;
        [SerializeField, Min(0f)] private float allRedSeconds = 1.5f;
        [SerializeField, Range(0f, 0.2f)] private float offLevel = 0.03f;
        [SerializeField, Min(0.2f)] private float lockdownFlashHz = 1.4f;
        [Tooltip("Grid anomaly (the heist pulling data): every signal flashes amber until the lockdown.")]
        [SerializeField] private string[] anomalyOn = Array.Empty<string>();

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private Material[] materials;
        private Color[] colours;
        private MissionWorld world;
        private bool lockdown, anomaly;

        private void Awake()
        {
            materials = new Material[lenses.Length];
            colours = new Color[lenses.Length];
            for (int i = 0; i < lenses.Length; i++)
            {
                if (lenses[i] == null) continue;
                materials[i] = new Material(lenses[i].sharedMaterial) { name = lenses[i].sharedMaterial.name + " (runtime)" };
                colours[i] = materials[i].GetColor(EmissionColor);
                lenses[i].sharedMaterial = materials[i];
            }
        }

        private void OnDestroy()
        {
            if (materials == null) return;
            foreach (var m in materials) if (m != null) Destroy(m);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.SecurityChanged += OnSecurityChanged;
            world.EventRaised += OnWorldEvent;
            lockdown = world.Security == SecurityLevel.Lockdown;
            anomaly = false;
        }

        private void OnWorldEvent(string eventId)
        {
            if (MissionWorld.Matches(anomalyOn, eventId)) anomaly = true;
        }

        public void Unbind()
        {
            if (world != null)
            {
                world.SecurityChanged -= OnSecurityChanged;
                world.EventRaised -= OnWorldEvent;
            }
            world = null;
            lockdown = anomaly = false;
        }

        private void OnSecurityChanged(SecurityLevel level) => lockdown = level == SecurityLevel.Lockdown;

        private void Update()
        {
            if (materials == null) return;
            if (lockdown)
            {
                bool on = Mathf.Repeat(Time.time * lockdownFlashHz, 1f) < 0.5f;
                Set(on, false, false, on, false, false);
                return;
            }
            if (anomaly)
            {
                // Irregular amber stutter: the grid is being rerouted under the player's feet.
                float u = Time.time * 2.3f;
                bool on = Mathf.Repeat(u, 1f) < 0.5f ^ Mathf.Repeat(u * 0.37f, 1f) < 0.12f;
                Set(false, on, false, false, on, false);
                return;
            }
            float half = greenSeconds + amberSeconds + allRedSeconds;
            float t = Mathf.Repeat(Time.time, half * 2f);
            bool nsPhase = t < half;
            float p = nsPhase ? t : t - half;
            bool green = p < greenSeconds, amber = !green && p < greenSeconds + amberSeconds;
            if (nsPhase) Set(!green && !amber, amber, green, true, false, false);
            else Set(true, false, false, !green && !amber, amber, green);
        }

        private void Set(bool nsRed, bool nsAmber, bool nsGreen, bool ewRed, bool ewAmber, bool ewGreen)
        {
            Lens(0, nsRed); Lens(1, nsAmber); Lens(2, nsGreen);
            Lens(3, ewRed); Lens(4, ewAmber); Lens(5, ewGreen);
        }

        private void Lens(int i, bool on)
        {
            if (i < materials.Length && materials[i] != null) materials[i].SetColor(EmissionColor, on ? colours[i] : colours[i] * offLevel);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Renderer[] lensRenderers) => lenses = lensRenderers;
        public void EditorConfigureEvents(string[] anomalyEvents) => anomalyOn = anomalyEvents ?? Array.Empty<string>();
#endif
    }
}
