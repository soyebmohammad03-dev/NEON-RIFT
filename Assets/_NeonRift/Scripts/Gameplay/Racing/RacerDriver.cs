using NeonRift.Vehicles;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// AI driver for a <see cref="VehicleController"/>, plugged in through the same <see cref="IVehicleInputSource"/>
    /// the player uses: no transforms are moved and no forces are faked. It plans a route over the city's road graph
    /// (re-planning when gates change), follows a <see cref="RacingLine"/> with pure-pursuit steering and a
    /// braking-distance speed planner, probes ahead for cars and obstacles to pick a clear lateral line (overtaking),
    /// backs out when stuck, and parks at hold points.
    /// </summary>
    public sealed class RacerDriver : IVehicleInputSource
    {
        public enum Mode { Idle, Driving, Holding }

        private const float ProbeInterval = 0.1f;
        private const float ProbeRadius = 0.85f;
        private const float OffLineDistance = 22f;
        private static readonly float[] Candidates = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        private readonly VehicleController vehicle;
        private readonly RacerProfile profile;
        private readonly CityNavigation navigation;
        private readonly RacingLine line = new();
        private readonly RoadPath path = new();
        private readonly int probeMask;

        private Vector3 goal;
        private bool holdAtGoal;
        private int index = -1, planVersion = -1;
        private float offset, targetOffset;
        private float probeTimer, obstacleDistance = float.MaxValue, obstacleSpeed;
        private float stuckTimer, reverseTimer, uprightTimer, blockedTimer, lastSteer;
        private int reverses;

        public Mode State { get; private set; } = Mode.Idle;
        public VehicleController Vehicle => vehicle;
        public RacerProfile Profile => profile;
        public RacingLine Line => line;
        /// <summary>Pace multiplier (rubber band), applied to the target speed.</summary>
        public float PaceScale { get; set; } = 1f;
        /// <summary>Route distance left to the current goal, m.</summary>
        public float Remaining => line.Count > 0 && index >= 0 ? line.Remaining(index) : float.MaxValue;
        public bool AtGoal { get; private set; }
        public int Recoveries { get; private set; }

        public RacerDriver(VehicleController vehicle, RacerProfile profile, CityNavigation navigation)
        {
            this.vehicle = vehicle;
            this.profile = profile;
            this.navigation = navigation;
            probeMask = LayerMask.GetMask("Vehicle", "Environment");
        }

        /// <summary>Drive to <paramref name="target"/>; when <paramref name="hold"/>, stop and wait there.</summary>
        public void SetGoal(Vector3 target, bool hold)
        {
            goal = target;
            holdAtGoal = hold;
            AtGoal = false;
            State = Mode.Driving;
            Replan();
        }

        /// <summary>Stop where the car is and wait.</summary>
        public void Hold()
        {
            State = Mode.Holding;
        }

        private void Replan()
        {
            planVersion = navigation != null ? navigation.Version : 0;
            index = -1;
            if (navigation == null || !navigation.Plan(vehicle.Body.position, goal, path))
            {
                line.Build(null, null, 0f, 0f, 0f, false);
                return;
            }
            // The graph ends on the nearest road; finish the last metres to the exact goal (a parking slot).
            if (Vector3.Distance(path.Points[^1], goal) > 1f)
            {
                path.Edges.Add(path.Edges[^1]);
                path.Points.Add(goal);
            }
            line.Build(path, navigation.Network, profile.CornerAcceleration, profile.MaxSpeed, profile.StreetSpeedScale, stopAtEnd: true);
            index = line.FindClosest(vehicle.Body.position, -1);
            offset = targetOffset = Mathf.Clamp(LateralOf(vehicle.Body.position, index), line.MinOffset(index), line.MaxOffset(index));
        }

        private float LateralOf(Vector3 position, int i) => Vector3.Dot(position - line.PointAt(i), line.RightAt(i));

        public DrivingInput ReadInput()
        {
            var body = vehicle.Body;
            if (body == null) return DrivingInput.None;
            float dt = Time.fixedDeltaTime;
            Vector3 position = body.position;
            Quaternion rotation = body.rotation;
            float forwardSpeed = Vector3.Dot(body.linearVelocity, rotation * Vector3.forward);

            if (State == Mode.Holding || State == Mode.Idle || line.Count < 2) return Park(forwardSpeed);
            if (navigation != null && navigation.Version != planVersion) Replan();
            if (line.Count < 2) return Park(forwardSpeed);

            index = line.FindClosest(position, index);
            if (Mathf.Abs(LateralOf(position, index)) > OffLineDistance || Vector3.Distance(position, line.PointAt(index)) > OffLineDistance * 1.5f) Replan();
            if (line.Count < 2) return Park(forwardSpeed);

            // Arrival.
            if (line.Remaining(index) < 4f || (index >= line.Count - 2 && forwardSpeed < 3f))
            {
                AtGoal = true;
                if (holdAtGoal) { State = Mode.Holding; return Park(forwardSpeed); }
            }

            if (Recovering(dt, position, rotation, forwardSpeed, out var recovery)) return recovery;

            float speed = Mathf.Max(0f, forwardSpeed);
            UpdateProbe(dt, position, rotation, speed);
            float minO = line.MinOffset(index), maxO = line.MaxOffset(index);
            targetOffset = Mathf.Clamp(targetOffset, minO, maxO);
            offset = Mathf.MoveTowards(offset, targetOffset, profile.LaneChangeRate * dt);
            offset = Mathf.Clamp(offset, minO, maxO);

            // Pure pursuit on the offset line.
            float lookAhead = Mathf.Clamp(profile.LookAheadMin + profile.LookAheadTime * speed, profile.LookAheadMin, profile.LookAheadMax);
            int ahead = line.IndexAhead(index, lookAhead);
            Vector3 target = line.PointAt(ahead) + line.RightAt(ahead) * Mathf.Clamp(offset, line.MinOffset(ahead), line.MaxOffset(ahead));
            Vector3 local = Quaternion.Inverse(rotation) * (target - position);
            float alpha = Mathf.Atan2(local.x, Mathf.Max(local.z, -50f));
            float distance = Mathf.Max(lookAhead, new Vector2(local.x, local.z).magnitude);
            float steerAngle = Mathf.Atan(2f * vehicle.Wheelbase * Mathf.Sin(alpha) / distance) * Mathf.Rad2Deg;
            float limit = Mathf.Max(1f, vehicle.Steering.MaxAngleAt(speed));
            float steer = Mathf.Clamp(steerAngle / limit, -1f, 1f);
            if (local.z < 0f) steer = Mathf.Sign(local.x == 0f ? 1f : local.x);   // target behind: full lock round
            lastSteer = steer;

            // Speed: braking-distance planner over the line, then traffic ahead.
            float targetSpeed = line.SpeedAt(index) * PaceScale;
            float horizon = speed * speed / (2f * profile.Braking) + 20f;
            float start = line.DistanceAt(index);
            for (int i = index + 1; i < line.Count; i++)
            {
                float d = line.DistanceAt(i) - start;
                if (d > horizon) break;
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(Mathf.Pow(line.SpeedAt(i) * PaceScale, 2f) + 2f * profile.Braking * d));
            }
            if (obstacleDistance < float.MaxValue)
            {
                float gap = Mathf.Max(0f, obstacleDistance - 6f);
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(obstacleSpeed * obstacleSpeed + 2f * profile.Braking * gap));
            }
            if (local.z < 0f) targetSpeed = Mathf.Min(targetSpeed, 6f);

            float error = targetSpeed - forwardSpeed;
            return new DrivingInput
            {
                Steer = steer,
                Throttle = error > 0f ? Mathf.Clamp01(error / 2.5f) : 0f,
                Brake = error < -0.3f ? Mathf.Clamp01(-error / 1.5f) : 0f
            };
        }

        /// <summary>Brake to a stop, then handbrake only (holding the brake at a standstill selects reverse).</summary>
        private DrivingInput Park(float forwardSpeed)
        {
            stuckTimer = reverseTimer = 0f;
            bool moving = Mathf.Abs(forwardSpeed) > 1.5f;
            return new DrivingInput { Brake = moving && forwardSpeed > 0f ? 1f : 0f, Throttle = moving && forwardSpeed < 0f ? 0.4f : 0f, Handbrake = !moving };
        }

        /// <summary>Stuck against something: back out with opposite lock; after repeated failures (or upside down) right the car.</summary>
        private bool Recovering(float dt, Vector3 position, Quaternion rotation, float forwardSpeed, out DrivingInput input)
        {
            input = default;
            Vector3 up = rotation * Vector3.up;
            uprightTimer = up.y < 0.3f ? uprightTimer + dt : 0f;
            if (uprightTimer > 1.5f)
            {
                Reset();
                return false;
            }
            if (reverseTimer > 0f)
            {
                reverseTimer -= dt;
                // Brake at a standstill engages reverse in the vehicle model; steer the other way to swing the nose out.
                input = new DrivingInput { Brake = 1f, Steer = -lastSteer };
                if (reverseTimer <= 0f) stuckTimer = 0f;
                return true;
            }
            // Waiting behind something that does not move (a parked car, a sealed gate) also counts as stuck.
            bool blocked = obstacleDistance < 8f && obstacleSpeed < 0.5f && Mathf.Abs(forwardSpeed) < 0.5f;
            blockedTimer = blocked ? blockedTimer + dt : 0f;
            bool trying = vehicle.LastInput.Throttle > 0.3f || blockedTimer > 3f;
            stuckTimer = trying && Mathf.Abs(forwardSpeed) < 1f ? stuckTimer + dt : Mathf.Max(0f, stuckTimer - dt * 2f);
            if (stuckTimer < profile.StuckSeconds) return false;
            stuckTimer = 0f;
            if (++reverses > profile.ReversesBeforeReset)
            {
                Reset();
                return false;
            }
            reverseTimer = profile.ReverseSeconds;
            input = new DrivingInput { Brake = 1f, Steer = -lastSteer };
            return true;
        }

        private void Reset()
        {
            vehicle.Recover();
            reverses = 0;
            uprightTimer = 0f;
            Recoveries++;
            Replan();
        }

        /// <summary>
        /// Every 0.1 s, sweep the road ahead at five lateral positions. Pick the clearest line close to the preferred
        /// lane (and to the current line, to avoid weaving); remember the nearest blocker on the chosen line.
        /// </summary>
        private void UpdateProbe(float dt, Vector3 position, Quaternion rotation, float speed)
        {
            probeTimer -= dt;
            if (probeTimer > 0f) return;
            probeTimer = ProbeInterval;
            float probe = profile.ProbeBase + speed * profile.ProbeTime;
            int ahead = line.IndexAhead(index, probe);
            float minO = line.MinOffset(ahead), maxO = line.MaxOffset(ahead);
            float preferred = line.LaneOffset(ahead);
            Vector3 origin = position + rotation * new Vector3(0f, 0.95f, 2.2f);
            float bestScore = float.MinValue, bestOffset = targetOffset, bestDistance = float.MaxValue, bestSpeed = 0f;
            foreach (float c in Candidates)
            {
                float o = Mathf.Lerp(minO, maxO, c);
                Vector3 target = line.PointAt(ahead) + line.RightAt(ahead) * o + Vector3.up * 0.95f;
                Vector3 dir = target - origin;
                float len = dir.magnitude;
                float clear = probe;
                float otherSpeed = 0f;
                if (len > 0.1f && Physics.SphereCast(origin, ProbeRadius, dir / len, out var hit, len, probeMask, QueryTriggerInteraction.Ignore)
                    && hit.rigidbody != vehicle.Body)
                {
                    clear = hit.distance;
                    otherSpeed = hit.rigidbody != null ? Mathf.Max(0f, Vector3.Dot(hit.rigidbody.linearVelocity, dir / len)) : 0f;
                }
                // A slow car ahead is only "clear" for an aggressive driver who will go round it.
                float score = clear - Mathf.Abs(o - preferred) * (1.2f - profile.Aggression) * 2f - Mathf.Abs(o - targetOffset) * 0.6f;
                if (score > bestScore) { bestScore = score; bestOffset = o; bestDistance = clear < probe - 0.01f ? clear : float.MaxValue; bestSpeed = otherSpeed; }
            }
            targetOffset = bestOffset;
            obstacleDistance = bestDistance;
            obstacleSpeed = bestSpeed;
        }
    }
}
