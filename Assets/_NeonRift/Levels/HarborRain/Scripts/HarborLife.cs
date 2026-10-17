using System;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Background activity that makes the port feel worked: service trucks and straddle carriers on fixed service
    /// loops (kinematic, off the main routes; they stop when the player's car is in front of them), gantry trolleys and
    /// spreaders cycling (see <see cref="HarborCraneCycle"/>), ships at anchor rolling in the swell, sweeping searchlights, rotating beacons, steam / exhaust bursts and distant ship horns. Far movers update at a
    /// quarter rate. No physics-driven props.
    /// </summary>
    public sealed class HarborLife : MonoBehaviour
    {
        [Serializable]
        public sealed class Mover
        {
            public Transform body;
            public Vector3[] path = Array.Empty<Vector3>();
            public float speed = 5f;
            public float pause = 4f;
            /// <summary>
            /// One-way vehicle (articulated trucks can't spin round at a path end): it drives to the last point, unloads
            /// for <see cref="pause"/>, and re-enters at the first point only when neither end is in the camera's view.
            /// Speed ramps up and down (lorry acceleration / braking) and heading turns at a ~12 m radius.
            /// </summary>
            public bool recycle;
            [NonSerialized] public int next;
            [NonSerialized] public float wait;
            [NonSerialized] public float v;
        }

        [Serializable]
        public sealed class Oscillator
        {
            public Transform target;
            public Vector3 offset;
            public float period = 20f;
            public float phase;
            [NonSerialized] public Vector3 origin;
            [NonSerialized] public bool captured;
        }

        /// <summary>A vessel riding the swell: slow roll / pitch (degrees) and heave (m) about its rest pose.</summary>
        [Serializable]
        public sealed class Rocker
        {
            public Transform target;
            public Vector3 degrees = new(1.2f, 0f, 0.4f);
            public float heave = 0.3f;
            public float period = 9f;
            public float phase;
            [NonSerialized] public Quaternion rotation;
            [NonSerialized] public Vector3 position;
            [NonSerialized] public bool captured;
        }

        [Serializable]
        public sealed class Spinner
        {
            public Transform target;
            public float degreesPerSecond = 180f;
        }

        [SerializeField] private Mover[] movers = Array.Empty<Mover>();
        [SerializeField] private Oscillator[] oscillators = Array.Empty<Oscillator>();
        [SerializeField] private Spinner[] spinners = Array.Empty<Spinner>();
        [SerializeField] private Rocker[] rockers = Array.Empty<Rocker>();
        [SerializeField] private ParticleSystem[] steam = Array.Empty<ParticleSystem>();
        [SerializeField] private AudioSource[] horns = Array.Empty<AudioSource>();
        [SerializeField] private AudioClip hornClip;
        [SerializeField] private Vector2 hornInterval = new(35f, 70f);

        public int MoverCount => movers.Length;
        public int OscillatorCount => oscillators.Length;
        public int SteamCount => steam.Length;
        public int Stops { get; private set; }

        private float nextHorn, nextSteam;
        private int frame;

        private void OnEnable()
        {
            nextHorn = Time.time + 12f;
            nextSteam = Time.time + 2f;
            foreach (var m in movers) if (m.body != null && m.path.Length > 0) m.body.position = m.path[0];
        }

        private static bool Seen(Camera cam, Vector3 p)
        {
            if (cam == null) return false;
            var v = cam.WorldToViewportPoint(p + Vector3.up * 2f);
            return v.z > 0f && v.z < 260f && v.x > -0.15f && v.x < 1.15f && v.y > -0.15f && v.y < 1.15f;
        }

        /// <summary>One-way mover step: eased speed, radius-limited turning, unload pause, out-of-view re-entry.</summary>
        private void Recycle(Mover m, Camera cam, Vector3 car, float dt)
        {
            var b = m.body;
            if (m.next >= m.path.Length)
            {
                // Parked at the far end: come back round only when nobody can see either end.
                if (Seen(cam, b.position) || Seen(cam, m.path[0])) return;
                b.position = m.path[0];
                b.rotation = Quaternion.LookRotation(Flat(m.path[1] - m.path[0]));
                m.next = 1; m.v = 0f; m.wait = m.pause * 0.5f;
                return;
            }
            var target = m.path[m.next];
            var to = Flat(target - b.position);
            float remaining = to.magnitude;
            for (int k = m.next + 1; k < m.path.Length; k++) remaining += Vector3.Distance(m.path[k - 1], m.path[k]);
            var ahead = Flat(car - b.position);
            bool blocked = ahead.magnitude < 22f && Vector3.Dot(ahead.normalized, b.forward) > 0.35f;
            if (blocked) Stops++;
            float want = blocked ? 0f : Mathf.Min(m.speed, Mathf.Sqrt(2f * 1.1f * Mathf.Max(0f, remaining - 0.5f)));
            m.v = Mathf.MoveTowards(m.v, want, (want < m.v ? 2.2f : 0.9f) * dt);
            if (to.magnitude < Mathf.Max(2.5f, m.v * dt * 2f))
            {
                m.next++;
                if (m.next >= m.path.Length) { m.v = 0f; m.wait = m.pause; }
                return;
            }
            var heading = Quaternion.LookRotation(to.normalized);
            b.rotation = Quaternion.RotateTowards(b.rotation, heading, Mathf.Rad2Deg * Mathf.Max(m.v, 0.5f) / 12f * dt);
            b.position += b.forward * m.v * dt;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private void Update()
        {
            frame++;
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            var player = HarborRainEntry.Active != null && HarborRainEntry.Active.Driving != null ? HarborRainEntry.Active.Driving.PlayerVehicle : null;
            Vector3 car = player != null && player.Body != null ? player.Body.position : new Vector3(0f, -999f, 0f);

            for (int i = 0; i < movers.Length; i++)
            {
                var m = movers[i];
                if (m.body == null || m.path.Length < 2) continue;
                bool far = (m.body.position - eye).sqrMagnitude > 400f * 400f;
                if (far && (frame + i) % 4 != 0) continue;
                float dt = far ? Time.deltaTime * 4f : Time.deltaTime;
                if (m.wait > 0f) { m.wait -= dt; continue; }
                if (m.recycle) { Recycle(m, cam, car, dt); continue; }
                var target = m.path[m.next];
                var to = target - m.body.position;
                to.y = 0f;
                // Yield to the player: stop if the car is in the 14 m ahead of the vehicle.
                var ahead = car - m.body.position;
                ahead.y = 0f;
                if (ahead.magnitude < 14f && Vector3.Dot(ahead.normalized, m.body.forward) > 0.3f) { Stops++; continue; }
                float step = m.speed * dt;
                if (to.magnitude <= step)
                {
                    m.body.position = new Vector3(target.x, m.body.position.y, target.z);
                    m.next = (m.next + 1) % m.path.Length;
                    m.wait = m.pause;
                    continue;
                }
                var dir = to.normalized;
                m.body.position += dir * step;
                m.body.rotation = Quaternion.RotateTowards(m.body.rotation, Quaternion.LookRotation(dir), 90f * dt);
            }

            float t = Time.time;
            foreach (var o in oscillators)
            {
                if (o.target == null) continue;
                if (!o.captured) { o.origin = o.target.localPosition; o.captured = true; }
                float s = 0.5f - 0.5f * Mathf.Cos((t / o.period + o.phase) * Mathf.PI * 2f);
                o.target.localPosition = o.origin + o.offset * s;
            }
            foreach (var s in spinners) if (s.target != null) s.target.Rotate(0f, s.degreesPerSecond * Time.deltaTime, 0f, Space.World);
            float storm = HarborWater.Storm;
            foreach (var r in rockers)
            {
                if (r.target == null) continue;
                if (!r.captured) { r.rotation = r.target.localRotation; r.position = r.target.localPosition; r.captured = true; }
                float w = (t / r.period + r.phase) * Mathf.PI * 2f, k = 0.6f + storm * 0.6f;
                r.target.localRotation = Quaternion.Euler(r.degrees.x * Mathf.Sin(w) * k, 0f, r.degrees.z * Mathf.Sin(w * 0.73f + 1.1f) * k) * r.rotation;
                r.target.localPosition = r.position + Vector3.up * (r.heave * Mathf.Sin(w * 1.3f + 0.4f) * k);
            }

            if (steam.Length > 0 && t >= nextSteam)
            {
                nextSteam = t + UnityEngine.Random.Range(0.8f, 2.5f);
                var vent = steam[UnityEngine.Random.Range(0, steam.Length)];
                if (vent != null) vent.Emit(UnityEngine.Random.Range(14, 30));
            }
            if (horns.Length > 0 && hornClip != null && t >= nextHorn)
            {
                nextHorn = t + UnityEngine.Random.Range(hornInterval.x, hornInterval.y);
                var h = horns[UnityEngine.Random.Range(0, horns.Length)];
                if (h != null) h.PlayOneShot(hornClip, UnityEngine.Random.Range(0.7f, 1f));
            }
        }
    }
}
