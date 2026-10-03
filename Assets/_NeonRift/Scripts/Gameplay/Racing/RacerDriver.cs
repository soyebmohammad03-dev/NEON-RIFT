using System.Collections.Generic;
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
    /// tracks the other cars (player and rivals) to avoid side-swipes and yield to predicted collisions, backs out when
    /// stuck (remembering the blocked road so the next plan avoids it), and parks at hold points.
    /// </summary>
    public sealed class RacerDriver : IVehicleInputSource
    {
        public enum Mode { Idle, Driving, Holding }

        private const float ProbeInterval = 0.1f;
        private const float ProbeRadius = 0.85f;
        private const float OffLineDistance = 22f;
        private static readonly float[] Candidates = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        /// <summary>Half-width of the lane a car occupies when choosing lines around traffic, m.</summary>
        private const float CarClearance = 2.4f;
        /// <summary>How long a road edge stays expensive after the car got stuck on it, s.</summary>
        private const float BlockedEdgeMemory = 25f;
        private const float BlockedEdgePenalty = 250f;

        private struct Nearby
        {
            public float Along, Lateral, LateralVelocity, AlongSpeed;
        }

        private struct Body
        {
            public Vector3 Position, Forward, Velocity;
        }

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
        private string obstacleName = "none";
        private float yieldUntil, yieldSpeed;
        /// <summary>Speed cap from a car directly in front in this car's own frame (fail-safe behind the line logic), m/s.</summary>
        private float guardSpeed = float.MaxValue;
        private bool carBehind;
        private readonly List<Nearby> nearby = new();
        private readonly List<Body> others = new();
        private readonly Dictionary<int, float> blockedEdges = new();
        private RoadGraph.EdgeCost avoidCost;

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
        public int Reversals { get; private set; }
        public int Yields { get; private set; }
        /// <summary>Compact state for validation tools.</summary>
        public string DebugState =>
            $"{State} off {offset:0.0}->{targetOffset:0.0} obst {obstacleName} {(obstacleDistance < float.MaxValue ? obstacleDistance.ToString("0.0") : "-")} " +
            $"v {obstacleSpeed:0.0} stuck {stuckTimer:0.0} blocked {blockedTimer:0.0} yield {(Time.time < yieldUntil ? yieldSpeed.ToString("0.0") : "-")} in {vehicle.LastInput.Throttle:0.0}/{vehicle.LastInput.Brake:0.0}/{vehicle.LastInput.Steer:0.0}";
        /// <summary>Every car on the road this driver should respect (player and rivals; its own car is skipped).</summary>
        public IReadOnlyList<VehicleController> Traffic { get; set; }
        /// <summary>The player's car: rivals give way to it on crossing courses whoever has the right of way.</summary>
        public VehicleController PlayerCar { get; set; }

        public RacerDriver(VehicleController vehicle, RacerProfile profile, CityNavigation navigation)
        {
            this.vehicle = vehicle;
            this.profile = profile;
            this.navigation = navigation;
            probeMask = LayerMask.GetMask("Vehicle", "Environment");
            avoidCost = AvoidCost;
        }

        private float AvoidCost(int edge) => blockedEdges.TryGetValue(edge, out float until) && until > Time.time ? BlockedEdgePenalty : 0f;

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
            if (navigation == null || !navigation.Plan(vehicle.Body.position, goal, path, blockedEdges.Count > 0 ? avoidCost : null))
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

            float speed = Mathf.Max(0f, forwardSpeed);
            UpdateProbe(dt, position, rotation, speed);
            if (Recovering(dt, position, rotation, forwardSpeed, out var recovery)) return recovery;

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
            if (Time.time < yieldUntil) targetSpeed = Mathf.Min(targetSpeed, yieldSpeed);
            targetSpeed = Mathf.Min(targetSpeed, guardSpeed);
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
                // Never back into a car: stop reversing and let the stuck logic try again (or reset) instead.
                if (carBehind && forwardSpeed < 0.5f) reverseTimer = 0f;
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
            if (carBehind)
            {
                // Something is (or is about to be) behind us: hold still rather than back into it, and try again shortly.
                stuckTimer = profile.StuckSeconds * 0.5f;
                input = new DrivingInput { Handbrake = true };
                return true;
            }
            stuckTimer = 0f;
            if (++reverses > profile.ReversesBeforeReset)
            {
                Reset();
                return false;
            }
            reverseTimer = profile.ReverseSeconds;
            Reversals++;
            Debug.Log($"[Rivals] {vehicle.name} stuck at {position:F0}: reversing ({reverses}/{profile.ReversesBeforeReset}), obstacle {obstacleName} {(obstacleDistance < float.MaxValue ? obstacleDistance.ToString("0.0") + " m" : string.Empty)}");
            RememberBlocked(position);
            input = new DrivingInput { Brake = 1f, Steer = -lastSteer };
            return true;
        }

        /// <summary>Makes the road edge the car is stuck on expensive for a while; from the second reversal on, re-plan around it.</summary>
        private void RememberBlocked(Vector3 position)
        {
            if (navigation == null || navigation.Network == null) return;
            if (!navigation.Network.TryGetNearestEdge(position, out int edge, out _, out float distance) || distance > 25f) return;
            blockedEdges[edge] = Time.time + BlockedEdgeMemory;
            if (reverses >= 2) Replan();
        }

        private void Reset()
        {
            // Out of the player's sight a hopelessly stuck rival is put back on its line; in sight it is only righted.
            if (!InPlayerView(vehicle.Body.position) && line.Count > 1 && index >= 0)
            {
                int back = Mathf.Max(0, index - 3);
                Vector3 forward = (line.PointAt(back + 1) - line.PointAt(back)).normalized;
                Vector3 place = line.PointAt(back) + line.RightAt(back) * line.LaneOffset(back) + Vector3.up * 0.5f;
                if (forward.sqrMagnitude > 0.5f && !Physics.CheckSphere(place + Vector3.up * 0.5f, 2.2f, LayerMask.GetMask("Vehicle"), QueryTriggerInteraction.Ignore))
                    vehicle.Teleport(place, Quaternion.LookRotation(forward, Vector3.up));
                else vehicle.Recover();
            }
            else vehicle.Recover();
            Debug.Log($"[Rivals] {vehicle.name} reset at {vehicle.Body.position:F0} after {reverses} reversals");
            reverses = 0;
            uprightTimer = 0f;
            Recoveries++;
            Replan();
        }

        private static bool InPlayerView(Vector3 position)
        {
            var camera = Camera.main;
            if (camera == null) return false;
            Vector3 v = camera.WorldToViewportPoint(position);
            return v.z > 0f && v.z < 120f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }

        /// <summary>
        /// Puts every other car within range into this driver's line frame (distance along the route, lateral offset)
        /// and yields when a crossing car is on a collision course.
        /// </summary>
        private void ScanTraffic(Vector3 position, Quaternion rotation, float speed, float range)
        {
            nearby.Clear();
            others.Clear();
            guardSpeed = float.MaxValue;
            carBehind = false;
            if (Traffic == null) return;
            Vector3 velocity = vehicle.Body.linearVelocity;
            Vector3 forward = rotation * Vector3.forward;
            float start = line.DistanceAt(index);
            foreach (var other in Traffic)
            {
                if (other == null || other == vehicle || other.Body == null) continue;
                Vector3 p = other.Body.position, v = other.Body.linearVelocity;
                Vector3 r = p - position;
                if (r.sqrMagnitude > (range + 10f) * (range + 10f)) continue;
                others.Add(new Body { Position = p, Forward = other.transform.forward, Velocity = v });

                // Fail-safe in our own frame: a car straight ahead inside stopping distance caps our speed; one close behind blocks reversing.
                Vector3 own = Quaternion.Inverse(rotation) * r;
                if (Mathf.Abs(own.x) < 2.2f)
                {
                    if (own.z > 0f)
                    {
                        float gap = Mathf.Max(0f, own.z - 5f);
                        float theirs = Mathf.Max(0f, Vector3.Dot(v, forward));
                        guardSpeed = Mathf.Min(guardSpeed, theirs + Mathf.Sqrt(2f * profile.Braking * gap));
                    }
                    else if (own.z > -7.5f) carBehind = true;
                }
                // Reversing blind into the road is the dangerous move: block it if any car is, or within a second will be,
                // in a 6 m x 10 m box behind us (cars crossing behind a car park entrance, for example).
                Vector3 soon = Quaternion.Inverse(rotation) * (r + (v - velocity) * 1f);
                if ((Mathf.Abs(own.x) < 3f && own.z < 1f && own.z > -10f) || (Mathf.Abs(soon.x) < 3f && soon.z < 1f && soon.z > -10f))
                    carBehind = true;

                // Closest approach within our stopping horizon: give way if it is a hit and the other car is ahead of us,
                // or if it is the player crossing our path (the player never yields, so the AI always does).
                Vector3 w = v - velocity;
                w.y = 0f;
                r.y = 0f;
                float horizon = Mathf.Clamp(speed / Mathf.Max(profile.Braking, 1f) + 0.6f, 1.6f, 2.6f);
                float tca = w.sqrMagnitude > 0.01f ? Mathf.Clamp(-Vector3.Dot(r, w) / w.sqrMagnitude, 0f, horizon) : 0f;
                float miss = (r + w * tca).magnitude;
                Vector3 flatV = new(v.x, 0f, v.z), flatOwn = new(velocity.x, 0f, velocity.z);
                bool crossing = flatV.magnitude > 2f && (flatOwn.magnitude < 1f || Vector3.Angle(flatV, flatOwn) > 25f);
                bool ahead = Vector3.Dot(r, forward) > 1f;
                if (tca > 0.05f && miss < 2.8f && (ahead || (crossing && other == PlayerCar && Vector3.Dot(r, forward) > -3f)))
                {
                    float closing = -Vector3.Dot(r.normalized, w);
                    yieldSpeed = Mathf.Max(0f, Vector3.Dot(v, forward) - 1f);
                    if (closing > 1f && yieldUntil < Time.time) Yields++;
                    yieldUntil = Time.time + 0.5f;
                }

                int ci = line.FindClosest(p, index);
                float lateral = LateralOf(p, ci);
                if (Mathf.Abs(lateral) > 12f) continue;   // on a cross street: the time-to-collision check and the sphere casts handle it
                Vector3 tangent = (line.PointAt(ci + 1) - line.PointAt(Mathf.Max(0, ci - 1))).normalized;
                nearby.Add(new Nearby
                {
                    Along = line.DistanceAt(ci) - start,
                    Lateral = lateral,
                    LateralVelocity = Vector3.Dot(v, line.RightAt(ci)),
                    AlongSpeed = Vector3.Dot(v, tangent)
                });
            }
        }

        /// <summary>
        /// Distance along the line, at lateral offset <paramref name="o"/>, to the first point a car body (a 3.8 m segment
        /// on its long axis) comes within 2.2 m of; <paramref name="horizon"/> when the path is clear.
        /// </summary>
        private float PathClearance(float o, float horizon, out float theirSpeed)
        {
            theirSpeed = 0f;
            if (others.Count == 0) return horizon;
            float start = line.DistanceAt(index);
            for (int i = index + 1; i < line.Count; i++)
            {
                float d = line.DistanceAt(i) - start;
                if (d > horizon) break;
                if (d < 3f) continue;   // alongside: handled by the side-risk term
                Vector3 q = line.PointAt(i) + line.RightAt(i) * Mathf.Clamp(o, line.MinOffset(i), line.MaxOffset(i));
                foreach (var b in others)
                {
                    Vector3 axis = new Vector3(b.Forward.x, 0f, b.Forward.z).normalized * 1.9f;
                    Vector3 rel = q - b.Position;
                    rel.y = 0f;
                    float t = axis.sqrMagnitude > 0f ? Mathf.Clamp(Vector3.Dot(rel, axis) / axis.sqrMagnitude, -1f, 1f) : 0f;
                    if ((rel - axis * t).sqrMagnitude > 2.2f * 2.2f) continue;
                    Vector3 tangent = (line.PointAt(i + 1) - line.PointAt(i - 1)).normalized;
                    theirSpeed = Mathf.Max(0f, Vector3.Dot(b.Velocity, tangent));
                    return d;
                }
            }
            return horizon;
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
            ScanTraffic(position, rotation, speed, probe);
            float bestScore = float.MinValue, bestOffset = targetOffset, bestDistance = float.MaxValue, bestSpeed = 0f;
            string bestName = "none";
            foreach (float c in Candidates)
            {
                float o = Mathf.Lerp(minO, maxO, c);
                Vector3 target = line.PointAt(ahead) + line.RightAt(ahead) * o + Vector3.up * 0.95f;
                Vector3 dir = target - origin;
                float len = dir.magnitude;
                float clear = probe;
                float otherSpeed = 0f;
                string name = "none";
                if (len > 0.1f && Physics.SphereCast(origin, ProbeRadius, dir / len, out var hit, len, probeMask, QueryTriggerInteraction.Ignore)
                    && hit.rigidbody != vehicle.Body)
                {
                    clear = hit.distance;
                    name = hit.collider.name;
                    otherSpeed = hit.rigidbody != null ? Mathf.Max(0f, Vector3.Dot(hit.rigidbody.linearVelocity, dir / len)) : 0f;
                }
                // Cars on the route: where will they be laterally when we reach them? Alongside cars block the line outright.
                float sideRisk = 0f;
                foreach (var n in nearby)
                {
                    float reach = Mathf.Clamp(n.Along / Mathf.Max(speed - n.AlongSpeed, 2f), 0f, 1.2f);
                    float lateralThen = n.Lateral + n.LateralVelocity * reach;
                    if (Mathf.Abs(o - lateralThen) > CarClearance) continue;
                    if (n.Along > -6f && n.Along < 5f) sideRisk += 40f;
                }
                // Cars standing or driving on this candidate's path (exact through corners: it follows the line).
                float occupied = PathClearance(o, probe, out float occupantSpeed);
                if (occupied < clear)
                {
                    clear = occupied;
                    otherSpeed = occupantSpeed;
                    name = "car on path";
                }
                // A slow car ahead is only "clear" for an aggressive driver who will go round it.
                float score = -sideRisk + clear - Mathf.Abs(o - preferred) * (1.2f - profile.Aggression) * 2f - Mathf.Abs(o - targetOffset) * 0.6f;
                if (score > bestScore) { bestScore = score; bestOffset = o; bestDistance = clear < probe - 0.01f ? clear : float.MaxValue; bestSpeed = otherSpeed; bestName = name; }
            }
            targetOffset = bestOffset;
            // Fail-safe on the path actually being driven right now (the offset only eases towards the chosen one).
            float occupiedNow = PathClearance(offset, probe, out float nowSpeed);
            if (occupiedNow < probe) guardSpeed = Mathf.Min(guardSpeed, nowSpeed + Mathf.Sqrt(2f * profile.Braking * Mathf.Max(0f, occupiedNow - 2.5f)));
            obstacleDistance = bestDistance;
            obstacleSpeed = bestSpeed;
            obstacleName = bestName;
        }
    }
}
