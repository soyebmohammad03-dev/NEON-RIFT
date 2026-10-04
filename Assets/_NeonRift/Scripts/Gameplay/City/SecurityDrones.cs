using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Security drones. Dormant until the counter-intrusion or a lockdown; then they lift off from the Data Core and
    /// hunt the crews: most of them tail the player, the rest each lock on to a rival crew still running. A drone flies
    /// above and behind its car (leading it by its velocity), red/blue strobes flashing, and holds its searchlight
    /// on the car, so the light on the cars visibly comes from the drones. Presentation (and a strong cue that the
    /// city is looking for you); they log no heat themselves. A handful of transforms and spot lights in one loop.
    /// </summary>
    public sealed class SecurityDrones : MonoBehaviour, IMissionWorldComponent
    {
        [SerializeField] private Transform[] drones = Array.Empty<Transform>();
        [SerializeField] private Renderer[] strobes = Array.Empty<Renderer>();
        [SerializeField] private Light[] searchlights = Array.Empty<Light>();
        [SerializeField] private Transform[] beams = Array.Empty<Transform>();
        [SerializeField] private string[] launchOn = Array.Empty<string>();
        [Tooltip("Rival crews: the last drones each tail one rival still running (the rest stay on the player).")]
        [SerializeField] private RivalDirector rivals;
        [SerializeField, Min(5f)] private float altitude = 15f;
        [Tooltip("How far behind and to the side of its car a drone flies, m.")]
        [SerializeField, Min(1f)] private float trail = 9f;
        [Tooltip("Cruise speed, m/s; a drone always flies faster than the car it tails.")]
        [SerializeField, Min(1f)] private float speed = 30f;

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

        /// <summary>The car drone <paramref name="i"/> tails: one rival per drone from the end of the list, else the player.</summary>
        private Rigidbody TargetFor(int i)
        {
            if (rivals != null)
            {
                int k = drones.Length - 1 - i;
                if (k < rivals.Rivals.Count && k < drones.Length - 1)
                {
                    var r = rivals.Rivals[k];
                    if (!r.Finished && r.Car != null) return r.Car.Body;
                }
            }
            return world.PlayerBody;
        }

        private void Update()
        {
            if (!active || world == null) return;
            float t = Time.time - activeSince;
            float dt = Time.deltaTime;
            for (int i = 0; i < drones.Length; i++)
            {
                var d = drones[i];
                if (d == null) continue;
                var body = TargetFor(i);
                Vector3 target = body != null ? body.position : home;
                Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
                Vector3 flatV = new(velocity.x, 0f, velocity.z);
                Vector3 forward = flatV.sqrMagnitude > 4f ? flatV.normalized : (body != null ? Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized : Vector3.forward);
                Vector3 side = Vector3.Cross(Vector3.up, forward);
                // Each drone keeps its own slot: behind, to the left or right, a little higher than the one before.
                float lateral = (i % 3 - 1) * trail * 0.8f;
                float back = trail * (i % 3 == 1 ? 1.4f : 0.9f);
                Vector3 slot = target + velocity * 0.45f - forward * back + side * lateral
                               + Vector3.up * (altitude + (i % 3) * 1.6f + Mathf.Sin(t * 0.9f + i) * 0.8f);
                // Climb out of the core first, then chase.
                Vector3 goal = t < 2.5f ? home + Vector3.up * (altitude * Mathf.Clamp01(t / 2.5f)) : slot;
                float maxSpeed = Mathf.Max(speed, flatV.magnitude + 12f);
                Vector3 desired = Vector3.ClampMagnitude((goal - d.position) * 1.6f, maxSpeed);
                velocities[i] = Vector3.Lerp(velocities[i], desired, dt * 2.5f);
                d.position += velocities[i] * dt;
                Vector3 flat = new(velocities[i].x, 0f, velocities[i].z);
                Vector3 face = flat.sqrMagnitude > 1f ? flat : forward;
                // Nose down into the chase, banked by its sideways speed.
                float pitch = Mathf.Clamp(flat.magnitude * 0.5f, 0f, 18f);
                float roll = Mathf.Clamp(-Vector3.Dot(velocities[i], Vector3.Cross(Vector3.up, face.normalized)) * 0.8f, -20f, 20f);
                d.rotation = Quaternion.Slerp(d.rotation, Quaternion.LookRotation(face) * Quaternion.Euler(pitch, 0f, roll), dt * 4f);
                // Searchlight: held on the car, with a slight hand-held wander.
                Vector3 spot = target + velocity * 0.1f + new Vector3(Mathf.Sin(t * 1.7f + i * 2f), 0f, Mathf.Cos(t * 1.3f + i)) * 1.2f;
                if (i < searchlights.Length && searchlights[i] != null) searchlights[i].transform.rotation = Quaternion.LookRotation(spot - searchlights[i].transform.position);
                if (i < beams.Length && beams[i] != null)
                {
                    Vector3 from = beams[i].parent != null ? beams[i].parent.position : d.position;
                    beams[i].position = from;
                    beams[i].rotation = Quaternion.LookRotation(spot - from) * Quaternion.Euler(90f, 0f, 0f);
                    beams[i].localScale = new Vector3(1f, Mathf.Min(60f, Vector3.Distance(from, spot)), 1f);
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
        public void EditorConfigure(Transform[] droneTransforms, Renderer[] strobeRenderers, Light[] lights, Transform[] beamTransforms, string[] launch,
                                    RivalDirector rivalDirector)
        {
            rivals = rivalDirector;
            drones = droneTransforms;
            strobes = strobeRenderers;
            searchlights = lights;
            beams = beamTransforms;
            launchOn = launch;
        }
#endif
    }
}
