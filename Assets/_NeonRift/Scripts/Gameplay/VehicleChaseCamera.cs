using NeonRift.Vehicles;
using Unity.Cinemachine;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Chase camera that positions a <see cref="CinemachineCamera"/> behind a vehicle. Heading swings toward the
    /// direction of travel when the car slides, the look point leads the velocity, the lens widens slightly with speed
    /// and the camera pulls back a little under acceleration. It follows the body rigidly in position and never damps
    /// roll or pitch, so it shows what the chassis is doing rather than hiding it.
    /// Runs before the CinemachineBrain so the brain picks up this frame's pose.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class VehicleChaseCamera : MonoBehaviour
    {
        [Header("Framing")]
        [Tooltip("Distance behind the car at rest, m.")]
        [SerializeField, Min(1f)] private float distance = 5.6f;
        [Tooltip("Camera height above the car's ground pivot, m.")]
        [SerializeField, Min(0.2f)] private float height = 1.75f;
        [Tooltip("Height of the aim point above the car's ground pivot, m.")]
        [SerializeField, Min(0f)] private float lookHeight = 0.95f;
        [Tooltip("Extra distance at the car's top speed, m.")]
        [SerializeField] private float speedDistance = 1.2f;
        [Tooltip("Height change at the car's top speed, m (negative = lower).")]
        [SerializeField] private float speedHeight = -0.25f;

        [Header("Heading")]
        [Tooltip("Time for the camera to swing onto a new heading, s.")]
        [SerializeField, Range(0.02f, 1f)] private float yawSmoothTime = 0.22f;
        [Tooltip("0 = always behind the nose, 1 = always behind the direction of travel.")]
        [SerializeField, Range(0f, 1f)] private float travelHeadingWeight = 0.55f;
        [Tooltip("Below this speed the camera stays behind the nose, m/s.")]
        [SerializeField, Min(0.5f)] private float travelHeadingMinSpeed = 4f;

        [Header("Look-ahead")]
        [Tooltip("Aim point leads the car by velocity × this, s.")]
        [SerializeField, Range(0f, 1f)] private float lookAheadTime = 0.18f;
        [SerializeField, Min(0f)] private float maxLookAhead = 6f;

        [Header("Lag")]
        [Tooltip("Vertical smoothing of the follow point, s. Filters wheel-hop, keeps jumps readable.")]
        [SerializeField, Range(0f, 0.5f)] private float heightSmoothTime = 0.08f;
        [Tooltip("Pull-back per g of longitudinal acceleration, m (pushes in under braking).")]
        [SerializeField, Range(0f, 3f)] private float accelerationLag = 0.7f;
        [SerializeField, Range(0.02f, 1f)] private float accelerationLagSmoothTime = 0.25f;

        [Header("Lens")]
        [SerializeField, Range(30f, 90f)] private float baseFieldOfView = 58f;
        [Tooltip("Field of view added at top speed (scales with speed²), degrees.")]
        [SerializeField, Range(0f, 25f)] private float speedFieldOfView = 9f;
        [SerializeField, Range(0.02f, 2f)] private float fieldOfViewSmoothTime = 0.35f;

        [Header("Collision")]
        [Tooltip("Geometry the camera must not pass through.")]
        [SerializeField] private LayerMask obstacleLayers = (1 << 7) | (1 << 8);
        [SerializeField, Range(0.05f, 1f)] private float collisionRadius = 0.25f;
        [SerializeField, Min(0.5f)] private float minDistance = 1.6f;

        [Header("Cornering")]
        [Tooltip("Camera roll into a corner per g of lateral acceleration, degrees (subtle: it reads as weight, not tilt).")]
        [SerializeField, Range(0f, 4f)] private float rollPerG = 1.6f;
        [SerializeField, Range(0f, 6f)] private float maxRoll = 3f;
        [SerializeField, Range(0.02f, 1f)] private float rollSmoothTime = 0.3f;

        [Header("Cinematic beat")]
        [Tooltip("How far round from behind the car the camera swings to frame car and subject, degrees.")]
        [SerializeField, Range(0f, 120f)] private float beatSwing = 55f;
        [SerializeField, Min(0f)] private float beatRise = 3.4f;
        [SerializeField, Min(0f)] private float beatPullBack = 5f;
        [Tooltip("Blend in / out time of a beat, unscaled s.")]
        [SerializeField, Range(0.05f, 2f)] private float beatBlend = 0.5f;

        [Header("Shake and events")]
        [Tooltip("Shake per unit of collision impulse / car mass (m/s of velocity change).")]
        [SerializeField, Range(0f, 0.2f)] private float impactShake = 0.05f;
        [Tooltip("Faint high-frequency buzz at top speed, m.")]
        [SerializeField, Range(0f, 0.1f)] private float speedShake = 0.012f;
        [SerializeField, Range(0.5f, 10f)] private float shakeDecay = 3.5f;
        [SerializeField, Range(0f, 1.5f)] private float maxShake = 0.45f;

        private CinemachineCamera cinemachineCamera;
        private float shake, focusUntil, focusWeight;
        private Vector3 focusPoint;
        private VehicleController target;
        private float yaw, yawVelocity;
        private float followY, followYVelocity;
        private float lag, lagVelocity;
        private float fieldOfView, fieldOfViewVelocity;
        private float roll, rollVelocity;
        private float beatStart = -10f, beatEnd = -10f;
        private Vector3 beatSubject;

        public VehicleController Target => target;

        private void Awake() => cinemachineCamera = GetComponent<CinemachineCamera>();

        public void SetTarget(VehicleController vehicle)
        {
            if (target != null) target.Collided -= OnCollided;
            target = vehicle;
            if (target != null) target.Collided += OnCollided;
            Snap();
        }

        private void OnDestroy()
        {
            if (target != null) target.Collided -= OnCollided;
        }

        private void OnCollided(VehicleCollision collision)
        {
            if (target.Body == null) return;
            Kick(collision.Impulse / target.Body.mass * impactShake);
        }

        /// <summary>Adds camera shake (m of offset, decays).</summary>
        public void Kick(float amount) => shake = Mathf.Min(maxShake, shake + Mathf.Max(0f, amount));

        /// <summary>Briefly biases the aim towards a world point (mission beats: the theft, a gate sealing).</summary>
        public void Focus(Vector3 point, float seconds, float weight = 0.3f)
        {
            focusPoint = point;
            focusUntil = Time.time + seconds;
            focusWeight = weight;
        }

        /// <summary>
        /// A short cinematic beat: the camera swings wide and high so the car and <paramref name="subject"/> share the frame,
        /// then blends back to the chase. Timed on unscaled time, so it works under slow motion.
        /// </summary>
        public void CinematicBeat(Vector3 subject, float seconds)
        {
            beatSubject = subject;
            beatStart = Time.unscaledTime;
            beatEnd = beatStart + Mathf.Max(seconds, beatBlend * 2f);
        }

        public bool InBeat => Time.unscaledTime < beatEnd;

        private float BeatWeight()
        {
            float now = Time.unscaledTime;
            if (now >= beatEnd || now < beatStart) return 0f;
            float w = Mathf.Min((now - beatStart) / beatBlend, (beatEnd - now) / beatBlend);
            w = Mathf.Clamp01(w);
            return w * w * (3f - 2f * w);
        }

        /// <summary>Jumps straight to the resting pose (after spawn or recovery).</summary>
        public void Snap()
        {
            if (target == null) return;
            var t = target.transform;
            yaw = FlatYaw(t.forward, yaw);
            followY = t.position.y;
            yawVelocity = followYVelocity = lag = lagVelocity = fieldOfViewVelocity = roll = rollVelocity = 0f;
            fieldOfView = baseFieldOfView;
            UpdatePose(0f);
        }

        private void LateUpdate()
        {
            if (target != null) UpdatePose(Time.deltaTime);
        }

        private void UpdatePose(float dt)
        {
            var t = target.transform;
            var telemetry = target.Telemetry;
            Vector3 velocity = target.Body != null ? target.Body.linearVelocity : Vector3.zero;
            Vector3 flatVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            float speed = flatVelocity.magnitude;

            float noseYaw = FlatYaw(t.forward, yaw);
            float headingYaw = noseYaw;
            if (telemetry.ForwardSpeed > 0f && speed > travelHeadingMinSpeed)
            {
                float travelYaw = FlatYaw(flatVelocity, noseYaw);
                float blend = travelHeadingWeight * Mathf.Clamp01((speed - travelHeadingMinSpeed) / travelHeadingMinSpeed);
                headingYaw = noseYaw + Mathf.DeltaAngle(noseYaw, travelYaw) * blend;
            }

            float n = Mathf.Clamp01(telemetry.NormalizedSpeed);
            if (dt > 0f)
            {
                yaw = Mathf.SmoothDampAngle(yaw, headingYaw, ref yawVelocity, yawSmoothTime, Mathf.Infinity, dt);
                followY = heightSmoothTime > 0f ? Mathf.SmoothDamp(followY, t.position.y, ref followYVelocity, heightSmoothTime, Mathf.Infinity, dt) : t.position.y;
                lag = Mathf.SmoothDamp(lag, telemetry.LongitudinalG * accelerationLag, ref lagVelocity, accelerationLagSmoothTime, Mathf.Infinity, dt);
                fieldOfView = Mathf.SmoothDamp(fieldOfView, baseFieldOfView + speedFieldOfView * n * n, ref fieldOfViewVelocity, fieldOfViewSmoothTime, Mathf.Infinity, dt);
                // Lean into the corner: positive lateral g (turning right) rolls the horizon a touch.
                float targetRoll = Mathf.Clamp(-telemetry.LateralG * rollPerG, -maxRoll, maxRoll) * Mathf.Clamp01(speed / 8f);
                roll = Mathf.SmoothDamp(roll, targetRoll, ref rollVelocity, rollSmoothTime, Mathf.Infinity, dt);
            }

            Vector3 pivot = new Vector3(t.position.x, followY, t.position.z);
            Vector3 aimOrigin = pivot + Vector3.up * lookHeight;
            Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height + speedHeight * n, -(distance + speedDistance * n + lag));
            Vector3 position = pivot + offset;

            Vector3 toCamera = position - aimOrigin;
            float length = toCamera.magnitude;
            var scene = target.gameObject.scene.GetPhysicsScene();
            if (length > 1e-3f && scene.SphereCast(aimOrigin, collisionRadius, toCamera / length, out var hit, length, obstacleLayers, QueryTriggerInteraction.Ignore))
                position = aimOrigin + toCamera / length * Mathf.Max(minDistance, hit.distance);

            // Lead only forward travel: reversing would pull the aim point towards (or past) the camera and pitch it
            // down onto the roof. Fades in over the first few m/s so a stop/start never snaps the view.
            float forwardLead = Mathf.Clamp01(telemetry.ForwardSpeed / 3f);
            Vector3 lookAt = aimOrigin + Vector3.ClampMagnitude(flatVelocity * (lookAheadTime * forwardLead), maxLookAhead);
            if (Time.time < focusUntil)
            {
                // Ease in and out over the focus window, never more than the weight.
                float remaining = focusUntil - Time.time;
                float w = focusWeight * Mathf.Clamp01(remaining / 0.6f);
                Vector3 toFocus = (focusPoint - aimOrigin).normalized * Vector3.Distance(lookAt, position);
                lookAt = Vector3.Lerp(lookAt, aimOrigin + toFocus, w);
            }
            float beat = BeatWeight();
            if (beat > 0f)
            {
                // Stand off on the far side of the car from the subject, swung round and raised, aiming between them.
                Vector3 away = Vector3.ProjectOnPlane(pivot - beatSubject, Vector3.up);
                if (away.sqrMagnitude < 1f) away = -(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
                away = Quaternion.Euler(0f, beatSwing, 0f) * away.normalized;
                Vector3 beatPosition = aimOrigin + away * (distance + beatPullBack) + Vector3.up * (height + beatRise);
                Vector3 toBeat = beatPosition - aimOrigin;
                if (scene.SphereCast(aimOrigin, collisionRadius, toBeat.normalized, out var beatHit, toBeat.magnitude, obstacleLayers, QueryTriggerInteraction.Ignore))
                    beatPosition = aimOrigin + toBeat.normalized * Mathf.Max(minDistance, beatHit.distance);
                Vector3 beatLook = Vector3.Lerp(aimOrigin, beatSubject, 0.4f);
                position = Vector3.Lerp(position, beatPosition, beat);
                lookAt = Vector3.Lerp(lookAt, beatLook, beat);
            }
            if (dt > 0f)
            {
                shake = Mathf.MoveTowards(shake, 0f, shake * shakeDecay * dt + 0.02f * dt);
                float amplitude = shake + speedShake * n * n;
                if (amplitude > 1e-4f)
                {
                    float time = Time.time * 23f;
                    var jitter = new Vector3(Mathf.PerlinNoise(time, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, time) - 0.5f, 0f) * (2f * amplitude);
                    position += Quaternion.Euler(0f, yaw, 0f) * jitter;
                }
            }
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up) * Quaternion.Euler(0f, 0f, roll * (1f - beat)));
            if (cinemachineCamera != null) cinemachineCamera.Lens.FieldOfView = fieldOfView;
        }

        private static float FlatYaw(Vector3 direction, float fallback)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : fallback;
        }
    }
}
