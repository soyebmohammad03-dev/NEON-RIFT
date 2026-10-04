using System;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A physical city car (generic passenger car pack). Rigidbody with four raycast springs holding the body up,
    /// lateral tyre grip and a drive/brake force, so it rolls, pitches, gets shunted and spins when a player or rival
    /// car hits it. <see cref="CityTraffic"/> sets where it is heading and how fast; the car does the physics:
    /// <list type="bullet">
    /// <item>Parked: kinematic (no cost) until something hits it, then dynamic: it takes the hit and rolls to a stop.</item>
    /// <item>Driving: follows its aim point at its target speed.</item>
    /// <item>Knocked: a hard hit stops it driving for a few seconds (hazards); it then rejoins the road.</item>
    /// </list>
    /// Traffic cars never collide with each other (Traffic layer ignores itself); they keep their distance by
    /// looking ahead instead. Engine loop pitched by speed, impact sounds by collision strength.
    /// </summary>
    public sealed class TrafficCar : MonoBehaviour
    {
        public enum Mode { Parked, Driving, Knocked, Stopped }

        [SerializeField] private Rigidbody body;
        [SerializeField] private Transform[] wheels = Array.Empty<Transform>();
        [SerializeField] private float wheelRadius = 0.35f;
        [Tooltip("Half track (x) and half wheelbase (z), m.")]
        [SerializeField] private Vector2 halfTrackBase = new(0.8f, 1.35f);
        [SerializeField] private Vector3 size = new(1.9f, 1.4f, 4.5f);
        [SerializeField] private AudioSource engine;
        [SerializeField] private AudioSource sfx;
        [SerializeField] private AudioClip engineLoop;
        [SerializeField] private AudioClip[] impacts = Array.Empty<AudioClip>();
        [SerializeField] private Renderer[] paintRenderers = Array.Empty<Renderer>();

        private const float Travel = 0.28f, Sag = 0.07f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private float spring, damper, wheelSpin, knockedUntil, lastImpact;
        private int groundMask;
        private MaterialPropertyBlock block;

        public Mode State { get; private set; } = Mode.Parked;
        public Rigidbody Body => body;
        public Vector3 Size => size;
        public float ForwardSpeed => body != null && !body.isKinematic ? Vector3.Dot(body.linearVelocity, transform.forward) : 0f;
        /// <summary>World point the car steers for, and the speed it aims for (set by <see cref="CityTraffic"/>).</summary>
        public Vector3 Aim { get; set; }
        public float TargetSpeed { get; set; }
        /// <summary>Hit by a player or rival car since it was placed (it then stays where it ended up until recycled).</summary>
        public bool Disturbed { get; private set; }
        public bool Upright => transform.up.y > 0.5f;
        public int Hits { get; private set; }
        public event Action<TrafficCar, Collision> Hit;

        private void Awake()
        {
            float quarter = body.mass * 9.81f / 4f;
            spring = quarter / Sag;
            damper = 2f * Mathf.Sqrt(spring * body.mass / 4f) * 0.55f;
            groundMask = LayerMask.GetMask("Default", "Drivable", "Environment");
            block = new MaterialPropertyBlock();
            if (engine != null) engine.clip = engineLoop;
        }

        /// <summary>Paint tint (1 = the pack's own colour).</summary>
        public void SetTint(Color tint)
        {
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColor, tint);
            foreach (var r in paintRenderers) if (r != null) r.SetPropertyBlock(block, 0);
        }

        /// <summary>Puts the car in a bay (kinematic, engine off) at <paramref name="position"/>.</summary>
        public void Park(Vector3 position, Quaternion rotation)
        {
            Place(position, rotation);
            body.isKinematic = true;
            State = Mode.Parked;
            if (engine != null) engine.Stop();
        }

        /// <summary>Starts the car driving from <paramref name="position"/> at <paramref name="speed"/>.</summary>
        public void Drive(Vector3 position, Quaternion rotation, float speed)
        {
            Place(position, rotation);
            body.isKinematic = false;
            body.linearVelocity = rotation * Vector3.forward * speed;
            body.angularVelocity = Vector3.zero;
            State = Mode.Driving;
            if (engine != null && engineLoop != null)
            {
                engine.timeSamples = UnityEngine.Random.Range(0, engineLoop.samples);
                engine.Play();
            }
        }

        private void Place(Vector3 position, Quaternion rotation)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Disturbed = false;
            Hits = 0;
            knockedUntil = 0f;
        }

        /// <summary>Back to driving after a knock (the controller re-routes it first).</summary>
        public void Resume()
        {
            if (State is Mode.Knocked or Mode.Stopped) State = Mode.Driving;
        }

        public void Stop() { if (State == Mode.Driving) State = Mode.Stopped; }

        public bool KnockedOver => State == Mode.Knocked && Time.time >= knockedUntil;

        private void FixedUpdate()
        {
            if (body.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            Vector3 up = transform.up, fwd = transform.forward, right = transform.right;
            int grounded = 0;
            // Four springs at the wheel positions.
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = new((i & 1) == 0 ? -halfTrackBase.x : halfTrackBase.x, wheelRadius + Travel, (i & 2) == 0 ? -halfTrackBase.y : halfTrackBase.y);
                Vector3 origin = transform.TransformPoint(local);
                if (!Physics.Raycast(origin, -up, out var hit, wheelRadius + Travel * 2f, groundMask, QueryTriggerInteraction.Ignore)) continue;
                grounded++;
                float compression = wheelRadius + Travel - hit.distance + Sag;
                if (compression <= 0f) continue;
                float closing = Vector3.Dot(body.GetPointVelocity(origin), up);
                float force = Mathf.Max(0f, spring * compression - damper * closing);
                body.AddForceAtPosition(up * force, origin);
            }
            if (grounded < 2) return;

            Vector3 v = body.linearVelocity;
            float forward = Vector3.Dot(v, fwd), lateral = Vector3.Dot(v, right);
            float mu = 9.81f * 0.95f;
            // Tyres: kill sideways slip (up to the grip limit, so a hard hit still slides the car).
            float latAccel = Mathf.Clamp(-lateral * 10f, -mu, mu);
            float longAccel;
            if (State == Mode.Driving)
            {
                longAccel = Mathf.Clamp((TargetSpeed - forward) * 1.6f, -7f, 3.2f);
                // Steer: yaw rate toward the aim point, limited by speed (no turning on the spot).
                Vector3 to = Aim - body.position;
                to.y = 0f;
                float angle = to.sqrMagnitude > 0.25f ? Vector3.SignedAngle(new Vector3(fwd.x, 0f, fwd.z), to, Vector3.up) * Mathf.Deg2Rad : 0f;
                float maxYaw = 1.3f * Mathf.Clamp01(Mathf.Abs(forward) / 3f);
                float yawRate = Mathf.Clamp(angle * 1.8f, -maxYaw, maxYaw) * Mathf.Sign(forward == 0f ? 1f : forward);
                float yawError = yawRate - body.angularVelocity.y;
                body.AddTorque(Vector3.up * yawError * body.inertiaTensor.y * 6f, ForceMode.Force);
            }
            else
            {
                // Parked-and-hit, knocked or stopped: brake to a halt.
                longAccel = Mathf.Clamp(-forward * 2f, -7f, 7f);
                if (State == Mode.Knocked && Time.time >= knockedUntil && Mathf.Abs(forward) < 0.5f && Upright) State = Mode.Stopped;
            }
            body.AddForce((right * latAccel + fwd * longAccel) * body.mass * (grounded / 4f), ForceMode.Force);
        }

        private void Update()
        {
            float speed = ForwardSpeed;
            wheelSpin = Mathf.Repeat(wheelSpin + speed / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg * Time.deltaTime, 360f);
            var spin = Quaternion.Euler(wheelSpin, 0f, 0f);
            foreach (var w in wheels) if (w != null) w.localRotation = spin;
            if (engine != null && engine.isPlaying)
            {
                float s = Mathf.Abs(speed);
                engine.pitch = Mathf.Lerp(engine.pitch, 0.72f + s / 18f * 0.8f, Time.deltaTime * 3f);
                engine.volume = Mathf.Lerp(engine.volume, State == Mode.Driving ? 0.32f + s / 25f * 0.25f : 0.18f, Time.deltaTime * 2f);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            var other = collision.rigidbody;
            if (other == null || other == body) return;
            float relative = collision.relativeVelocity.magnitude;
            if (relative < 1.2f) return;
            Disturbed = true;
            Hits++;
            if (body.isKinematic)
            {
                // A parked car comes alive when hit: momentum share of the hitting car, then real physics.
                body.isKinematic = false;
                float share = other.mass / (other.mass + body.mass);
                body.AddForce(-collision.relativeVelocity * share * 0.85f, ForceMode.VelocityChange);
                body.AddTorque(Vector3.up * UnityEngine.Random.Range(-1f, 1f) * relative * 0.15f, ForceMode.VelocityChange);
            }
            if (relative > 3f)
            {
                State = Mode.Knocked;
                knockedUntil = Time.time + Mathf.Clamp(relative * 0.4f, 2.5f, 7f);
            }
            if (sfx != null && impacts.Length > 0 && Time.time - lastImpact > 0.25f)
            {
                lastImpact = Time.time;
                int tier = relative > 12f ? 2 : relative > 5f ? 1 : 0;   // light, medium, heavy pairs
                int index = Mathf.Min(impacts.Length - 1, tier * 2 + UnityEngine.Random.Range(0, 2));
                sfx.PlayOneShot(impacts[index], Mathf.Clamp01(0.4f + relative / 20f));
            }
            Hit?.Invoke(this, collision);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Rigidbody rb, Transform[] wheelTransforms, float radius, Vector2 trackBase, Vector3 bodySize, AudioSource engineSource,
                                    AudioSource sfxSource, AudioClip loop, AudioClip[] impactClips, Renderer[] paint)
        {
            body = rb;
            wheels = wheelTransforms;
            wheelRadius = radius;
            halfTrackBase = trackBase;
            size = bodySize;
            engine = engineSource;
            sfx = sfxSource;
            engineLoop = loop;
            impacts = impactClips;
            paintRenderers = paint;
        }
#endif
    }
}
