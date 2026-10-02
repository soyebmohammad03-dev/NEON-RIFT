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

        private CinemachineCamera cinemachineCamera;
        private VehicleController target;
        private float yaw, yawVelocity;
        private float followY, followYVelocity;
        private float lag, lagVelocity;
        private float fieldOfView, fieldOfViewVelocity;

        public VehicleController Target => target;

        private void Awake() => cinemachineCamera = GetComponent<CinemachineCamera>();

        public void SetTarget(VehicleController vehicle)
        {
            target = vehicle;
            Snap();
        }

        /// <summary>Jumps straight to the resting pose (after spawn or recovery).</summary>
        public void Snap()
        {
            if (target == null) return;
            var t = target.transform;
            yaw = FlatYaw(t.forward, yaw);
            followY = t.position.y;
            yawVelocity = followYVelocity = lag = lagVelocity = fieldOfViewVelocity = 0f;
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
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            if (cinemachineCamera != null) cinemachineCamera.Lens.FieldOfView = fieldOfView;
        }

        private static float FlatYaw(Vector3 direction, float fallback)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : fallback;
        }
    }
}
