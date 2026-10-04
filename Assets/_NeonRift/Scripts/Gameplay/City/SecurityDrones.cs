using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Security drones. Dormant until the counter-intrusion or a lockdown; then they lift off from the Data Core and
    /// hunt: each orbits a point that trails the player at altitude, red/blue strobes flashing, a searchlight cone
    /// swept over the street below. Presentation (and a strong cue that the city is looking for you); they log no heat
    /// themselves. A handful of transforms and spot lights, updated in one loop.
    /// </summary>
    public sealed class SecurityDrones : MonoBehaviour, IMissionWorldComponent
    {
        [SerializeField] private Transform[] drones = Array.Empty<Transform>();
        [SerializeField] private Renderer[] strobes = Array.Empty<Renderer>();
        [SerializeField] private Light[] searchlights = Array.Empty<Light>();
        [SerializeField] private Transform[] beams = Array.Empty<Transform>();
        [SerializeField] private string[] launchOn = Array.Empty<string>();
        [SerializeField, Min(5f)] private float altitude = 26f;
        [SerializeField, Min(5f)] private float orbitRadius = 22f;
        [SerializeField, Min(1f)] private float speed = 24f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MissionWorld world;
        private bool active;
        private float activeSince;
        private Vector3 home;
        private MaterialPropertyBlock block;
        private Vector3[] velocities = Array.Empty<Vector3>();

        public bool Active => active;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            home = transform.position;
            velocities = new Vector3[drones.Length];
            SetVisible(false);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.EventRaised += OnEvent;
            world.SecurityChanged += OnSecurity;
            active = false;
            for (int i = 0; i < drones.Length; i++) if (drones[i] != null) drones[i].position = home + Vector3.up * (2f + i);
            SetVisible(false);
        }

        public void Unbind()
        {
            if (world != null)
            {
                world.EventRaised -= OnEvent;
                world.SecurityChanged -= OnSecurity;
            }
            world = null;
            active = false;
            SetVisible(false);
        }

        private void OnEvent(string id)
        {
            if (MissionWorld.Matches(launchOn, id)) Launch();
        }

        private void OnSecurity(SecurityLevel level)
        {
            if (level == SecurityLevel.Lockdown) Launch();
        }

        private void Launch()
        {
            if (active) return;
            active = true;
            activeSince = Time.time;
            SetVisible(true);
        }

        private void SetVisible(bool on)
        {
            foreach (var d in drones) if (d != null) d.gameObject.SetActive(on);
            enabled = on;
        }

        private void Update()
        {
            if (!active || world == null) return;
            Vector3 target = world.PlayerBody != null ? world.PlayerBody.position : home;
            float t = Time.time - activeSince;
            for (int i = 0; i < drones.Length; i++)
            {
                var d = drones[i];
                if (d == null) continue;
                // Climb out of the core first, then orbit a point over the player (drones spread round the circle).
                float phase = t * 0.35f + i * Mathf.PI * 2f / drones.Length;
                Vector3 orbit = target + new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase)) * orbitRadius + Vector3.up * (altitude + Mathf.Sin(t * 0.7f + i) * 2f);
                Vector3 goal = t < 2.5f ? home + Vector3.up * (altitude * Mathf.Clamp01(t / 2.5f)) : orbit;
                Vector3 desired = Vector3.ClampMagnitude(goal - d.position, speed);
                velocities[i] = Vector3.Lerp(velocities[i], desired, Time.deltaTime * 1.5f);
                d.position += velocities[i] * Time.deltaTime;
                Vector3 flat = new(velocities[i].x, 0f, velocities[i].z);
                if (flat.sqrMagnitude > 0.5f) d.rotation = Quaternion.Slerp(d.rotation, Quaternion.LookRotation(flat) * Quaternion.Euler(10f, 0f, 0f), Time.deltaTime * 3f);
                // Searchlight: sweeps the street around the player.
                Vector3 sweep = target + new Vector3(Mathf.Sin(t * 1.3f + i * 2f), 0f, Mathf.Cos(t * 1.1f + i)) * 7f;
                if (i < searchlights.Length && searchlights[i] != null) searchlights[i].transform.rotation = Quaternion.LookRotation(sweep - searchlights[i].transform.position);
                if (i < beams.Length && beams[i] != null)
                {
                    Vector3 from = beams[i].parent != null ? beams[i].parent.position : d.position;
                    beams[i].position = from;
                    beams[i].rotation = Quaternion.LookRotation(sweep - from) * Quaternion.Euler(90f, 0f, 0f);
                    beams[i].localScale = new Vector3(1f, Mathf.Min(60f, Vector3.Distance(from, sweep)), 1f);
                }
                if (i < strobes.Length && strobes[i] != null)
                {
                    float s = Mathf.Repeat(Time.time * 2.4f + i * 0.37f, 1f);
                    block.SetColor(EmissionColor, s < 0.15f ? new Color(4f, 0.15f, 0.15f) : s > 0.5f && s < 0.65f ? new Color(0.2f, 0.5f, 4f) : Color.black);
                    strobes[i].SetPropertyBlock(block);
                }
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform[] droneTransforms, Renderer[] strobeRenderers, Light[] lights, Transform[] beamTransforms, string[] launch)
        {
            drones = droneTransforms;
            strobes = strobeRenderers;
            searchlights = lights;
            beams = beamTransforms;
            launchOn = launch;
        }
#endif
    }
}
