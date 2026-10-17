using System;
using System.Collections.Generic;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The port police: two or three units driven by the project's stock <see cref="RacerDriver"/> (the same AI as the
    /// Night Run rivals: road-graph planning, racing line, braking-distance speed, obstacle and traffic probes, back-out
    /// when stuck) over Harbor Rain's own <see cref="RoadNetwork"/> through a <see cref="CityNavigation"/>. Closed
    /// roads (sealed gates, flooded quays, the roadblock, wave-swept coast sections) are mirrored onto invisible
    /// <see cref="SecurityBarrier"/>s, so units plan round them exactly like the rivals plan round Night Run's gates.
    ///
    /// States: PATROL (lights off, cruising a loop) → ALERT (theft: sirens, dispatch call, every unit routes to the
    /// last known position) → PURSUIT (a unit has line of sight: chasers aim at the car's predicted position, the
    /// interceptor races to a junction on the player's route ahead and parks on one side of it while it can get there
    /// first) → SEARCH (no unit has seen the car for a while and it is far away: units sweep the roads round the last
    /// known position with lights on, sirens off; seen again → PURSUIT). Units never teleport during a pursuit (the
    /// stock driver only resets a hopelessly stuck car when it is outside the player's view) and nothing spawns near
    /// the player. Decisions run a few times a second per unit, staggered.
    /// </summary>
    public sealed class HarborPolice : MonoBehaviour
    {
        public enum State { Patrol, Alert, Pursuit, Search, StoodDown }
        public enum Role { Chase, Flank, Intercept }

        [Serializable]
        public struct UnitSetup
        {
            public string callsign;
            public Role role;
            public Vector3[] patrol;
        }

        [Serializable]
        public struct Closure
        {
            public string id;
            public SecurityBarrier barrier;
        }

        [SerializeField] private VehicleDefinition car;
        [SerializeField] private RacerProfile profile;
        [SerializeField] private CityNavigation navigation;
        [SerializeField] private UnitSetup[] setups = Array.Empty<UnitSetup>();
        [SerializeField] private Closure[] closures = Array.Empty<Closure>();
        [Tooltip("Parked units (the police post, checkpoints): lights on when the alert goes out.")]
        [SerializeField] private HarborPoliceCar[] parkedKits = Array.Empty<HarborPoliceCar>();
        [Tooltip("Line-of-sight range for a unit to see the player, m.")]
        [SerializeField] private float sightRange = 160f;
        [Tooltip("Seconds without any unit seeing the car (while it is far away) before the units go to SEARCH.")]
        [SerializeField] private float loseSeconds = 9f;
        [SerializeField] private float loseDistance = 140f;
        [SerializeField] private float patrolPace = 0.42f, searchPace = 0.62f;

        public sealed class Unit
        {
            public string Callsign;
            public Role Role;
            public VehicleController Car;
            public RacerDriver Driver;
            public HarborPoliceCar Kit;
            public Vector3[] Patrol;
            public int PatrolIndex;
            public Vector3 SpawnPosition;
            public Quaternion SpawnRotation;
            public Vector3 Goal;
            public bool GoalHold, Intercepting, Sees;
            public Vector3 InterceptAt, InterceptDirection;
            public float NextDecision, NextIntercept, InterceptUntil, Distance, LastPlan;
            public float TravelledInPursuit;
            public Vector3 LastPosition;
            public int MaxJump;   // largest single-frame position jump the player could see during a pursuit, cm (teleport check)
            public int OffscreenRecoveries;
        }

        private readonly List<Unit> units = new();
        private readonly List<VehicleController> traffic = new();
        private readonly List<RacerDriver> peers = new();
        private readonly Dictionary<string, SecurityBarrier> closureById = new();
        private VehicleController player;
        private System.Random rng = new(11);
        private float sinceSeen, stateTime, nextEstimate;
        private int sightMask;

        public State Current { get; private set; }
        /// <summary>The units (a List, so per-frame foreach loops do not allocate an enumerator).</summary>
        public List<Unit> Units => units;
        public Vector3 LastKnown { get; private set; }
        public float SinceSeen => sinceSeen;
        public bool AnySees { get; private set; }
        public int Interceptions { get; private set; }
        public int Reacquired { get; private set; }
        public int Losses { get; private set; }
        public float ClosestDistance { get; private set; } = float.MaxValue;
        public CityNavigation Navigation => navigation;
        /// <summary>Raised on state changes and notable events: "alert", "pursuit", "intercept", "lost", "reacquired".</summary>
        public event Action<string, Unit> Event;
        /// <summary>The player's predicted route (CONTROL's line), as a point the given metres ahead; null when unknown.</summary>
        public Func<float, Vector3?> RouteAhead;
        /// <summary>True where a unit must not park (wave sections, floods, the roadblock).</summary>
        public Func<Vector3, bool> NoStopping;
        /// <summary>Water depth at a point (0 = dry), used to slow units through floods and wave wash.</summary>
        public Func<Vector3, float> WaterDepth;

        // ---------------- Lifecycle ----------------

        public void Spawn(VehicleController playerCar, IEnumerable<VehicleController> others)
        {
            if (units.Count > 0 || car == null || profile == null) return;
            sightMask = LayerMask.GetMask("Environment");
            foreach (var c in closures) if (c.barrier != null && !string.IsNullOrEmpty(c.id)) closureById[c.id] = c.barrier;
            foreach (var s in setups)
            {
                if (s.patrol == null || s.patrol.Length < 2) continue;
                var go = new GameObject("Police_" + s.callsign);
                go.transform.SetParent(transform, false);
                var rot = Quaternion.LookRotation(Flat(s.patrol[1] - s.patrol[0]));
                go.transform.SetPositionAndRotation(s.patrol[0] + Vector3.up * 0.05f, rot);
                var spawn = go.AddComponent<VehicleSpawnPoint>();
                var vc = spawn.Spawn(car, "PoliceCar_" + s.callsign);
                if (vc == null) continue;
                var u = new Unit
                {
                    Callsign = s.callsign, Role = s.role, Car = vc, Patrol = s.patrol, SpawnPosition = spawn.Position, SpawnRotation = rot,
                    Kit = vc.GetComponentInChildren<HarborPoliceCar>(true),
                    Driver = new RacerDriver(vc, profile, navigation)
                };
                if (u.Kit != null) u.Kit.Phase = units.Count * 0.37f;
                foreach (var r in vc.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(u.Driver);
                units.Add(u);
                peers.Add(u.Driver);
            }
            foreach (var u in units) { u.Driver.Peers = peers; traffic.Add(u.Car); }
            if (others != null) foreach (var o in others) if (o != null) traffic.Add(o);
            SetPlayer(playerCar);
            ResetUnits();
        }

        public void SetPlayer(VehicleController playerCar)
        {
            if (player != null) traffic.Remove(player);
            player = playerCar;
            if (player != null && !traffic.Contains(player)) traffic.Add(player);
            foreach (var u in units) { u.Driver.Traffic = traffic; u.Driver.PlayerCar = player; }
        }

        /// <summary>Retry / restart: every unit back at its patrol start (the only time units are ever placed), lights off.</summary>
        public void ResetUnits()
        {
            Current = State.Patrol;
            stateTime = sinceSeen = 0f;
            Interceptions = Reacquired = Losses = 0;
            ClosestDistance = float.MaxValue;
            rng = new System.Random(11);
            foreach (var c in closureById.Values) if (c != null && !c.IsOpen) c.Open();
            foreach (var u in units)
            {
                u.Car.Teleport(u.SpawnPosition, u.SpawnRotation);
                u.PatrolIndex = 1;
                u.Intercepting = false;
                u.NextDecision = Time.time + units.IndexOf(u) * 0.1f;
                u.TravelledInPursuit = 0f;
                u.MaxJump = 0;
                u.OffscreenRecoveries = 0;
                u.LastPosition = u.SpawnPosition;
                u.Driver.PaceScale = patrolPace;
                SetGoal(u, u.Patrol[1], false, force: true);
                if (u.Kit != null) u.Kit.Set(HarborPoliceCar.Lights.Off, false, false);
            }
            foreach (var k in parkedKits) if (k != null) k.Set(HarborPoliceCar.Lights.Off, false, false);
        }

        /// <summary>The theft (or an early alarm): every unit responds to <paramref name="at"/> with lights and sirens.</summary>
        public void Alert(Vector3 at)
        {
            if (Current is State.Alert or State.Pursuit) return;
            LastKnown = at;
            sinceSeen = 0f;
            Enter(State.Alert);
            foreach (var k in parkedKits) if (k != null) k.Set(HarborPoliceCar.Lights.Respond, false, false);
            Event?.Invoke("alert", null);
            foreach (var u in units) u.NextDecision = Time.time + units.IndexOf(u) * 0.15f;
        }

        /// <summary>Another sensor (a drone lock, a camera) has the car: refresh the last known position.</summary>
        public void Report(Vector3 at)
        {
            if (Current is State.Patrol or State.StoodDown) return;
            LastKnown = at;
            sinceSeen = 0f;
            if (Current == State.Search) { Reacquired++; Enter(State.Pursuit); Event?.Invoke("reacquired", null); }
        }

        /// <summary>Extraction or failure: units pull up where they are; lights stay on, sirens off.</summary>
        public void StandDown()
        {
            Enter(State.StoodDown);
            foreach (var u in units)
            {
                u.Driver.Hold();
                if (u.Kit != null) u.Kit.Set(HarborPoliceCar.Lights.Search, false, false);
            }
        }

        /// <summary>
        /// A closure that has just gone up (a flood barricade) never traps a unit that is already inside it: units can
        /// always wade out (their routes still avoid the closed stretch).
        /// </summary>
        public void LetUnitsOut(Collider[] barricades)
        {
            foreach (var u in units)
                if (u.Car != null)
                    foreach (var mine in u.Car.GetComponentsInChildren<Collider>())
                        foreach (var b in barricades) if (b != null && b.enabled) Physics.IgnoreCollision(mine, b, true);
        }

        /// <summary>Mirrors a world closure onto the police road graph (no-op for unknown ids).</summary>
        public void SetClosed(string id, bool closed)
        {
            if (!closureById.TryGetValue(id, out var b) || b == null) return;
            if (closed && b.IsOpen) b.Close();
            else if (!closed && !b.IsOpen) b.Open();
        }

        public bool IsClosed(string id) => closureById.TryGetValue(id, out var b) && b != null && !b.IsOpen;

        // ---------------- Per frame ----------------

        private void Update()
        {
            if (units.Count == 0 || player == null || player.Body == null) return;
            float dt = Time.deltaTime;
            stateTime += dt;
            Vector3 p = player.Body.position;
            AnySees = false;
            foreach (var u in units)
            {
                if (u.Car == null || u.Car.Body == null) continue;
                Vector3 c = u.Car.Body.position;
                u.Distance = Vector3.Distance(Flat(c), Flat(p));
                if (Current is State.Alert or State.Pursuit or State.Search)
                {
                    ClosestDistance = Mathf.Min(ClosestDistance, u.Distance);
                    float jump = Vector3.Distance(c, u.LastPosition);
                    u.TravelledInPursuit += jump;
                    // Teleport check: movement beyond what the car's own speed explains this frame.
                    float excess = jump - u.Car.Body.linearVelocity.magnitude * dt * 1.5f - 0.2f;
                    // Only a jump the player could see counts (the stock driver puts a hopelessly stuck car back on its line
                    // only while it is out of view; those are counted separately as OffscreenRecoveries).
                    if (dt > 0f && excess > 0.5f && !Visible(c) && !Visible(u.LastPosition)) u.OffscreenRecoveries++;
                    else if (dt > 0f) u.MaxJump = Mathf.Max(u.MaxJump, Mathf.RoundToInt(Mathf.Max(0f, excess) * 100f));
                }
                u.LastPosition = c;
                // Two sight lines (cab height and roof-bar height) so a kerb, a low flood barrier or a bollard does not blind a unit.
                u.Sees = u.Distance < sightRange && (!Physics.Linecast(c + Vector3.up * 1.6f, p + Vector3.up * 1.2f, sightMask, QueryTriggerInteraction.Ignore)
                                                    || !Physics.Linecast(c + Vector3.up * 2.8f, p + Vector3.up * 2.2f, sightMask, QueryTriggerInteraction.Ignore));
                if (u.Sees && Current is not (State.Patrol or State.StoodDown)) AnySees = true;
            }

            if (Current is State.Alert or State.Pursuit or State.Search)
            {
                if (AnySees)
                {
                    LastKnown = p;
                    sinceSeen = 0f;
                    if (Current == State.Alert) { Enter(State.Pursuit); Event?.Invoke("pursuit", Nearest()); }
                    else if (Current == State.Search) { Reacquired++; Enter(State.Pursuit); Event?.Invoke("reacquired", Nearest()); }
                }
                else
                {
                    sinceSeen += dt;
                    float nearest = float.MaxValue;
                    foreach (var u in units) nearest = Mathf.Min(nearest, u.Distance);
                    if (Current == State.Pursuit && sinceSeen > loseSeconds && nearest > loseDistance)
                    {
                        Losses++;
                        Enter(State.Search);
                        Event?.Invoke("lost", null);
                    }
                }
            }

            foreach (var u in units)
            {
                if (Time.time < u.NextDecision || u.Car == null || u.Car.Body == null) continue;
                u.NextDecision = Time.time + 0.3f;
                Decide(u, p);
            }
        }

        private void Enter(State next)
        {
            Current = next;
            stateTime = 0f;
            foreach (var u in units)
            {
                if (u.Kit == null) continue;
                switch (next)
                {
                    case State.Patrol: u.Kit.Set(HarborPoliceCar.Lights.Off, false, false); break;
                    case State.Alert: u.Kit.Set(HarborPoliceCar.Lights.Respond, true, false); break;
                    case State.Pursuit: u.Kit.Set(HarborPoliceCar.Lights.Respond, true, false); break;
                    case State.Search: u.Kit.Set(HarborPoliceCar.Lights.Search, false, false); break;
                }
                if (next is State.Search or State.Patrol) u.Intercepting = false;
            }
        }

        private Unit Nearest()
        {
            Unit best = null;
            foreach (var u in units) if (best == null || u.Distance < best.Distance) best = u;
            return best;
        }

        private void Decide(Unit u, Vector3 p)
        {
            var pv = player.Body.linearVelocity;
            float depth = WaterDepth != null ? WaterDepth(u.Car.Body.position) : 0f;
            float wet = depth > 0.04f ? Mathf.Lerp(0.8f, 0.55f, Mathf.InverseLerp(0.04f, 0.3f, depth)) : 1f;
            switch (Current)
            {
                case State.Patrol:
                {
                    u.Driver.PaceScale = patrolPace * wet;
                    var target = u.Patrol[u.PatrolIndex];
                    if (Vector3.Distance(Flat(u.Car.Body.position), Flat(target)) < 14f || u.Driver.AtGoal)
                    {
                        u.PatrolIndex = (u.PatrolIndex + 1) % u.Patrol.Length;
                        SetGoal(u, u.Patrol[u.PatrolIndex], false, force: true);
                    }
                    break;
                }
                case State.Alert:
                    // Respond: dispatch (cameras, drones, the alarm) passes on an estimated position every few seconds —
                    // the car's position rounded to the nearest ~40 m — and the units converge on it from different sides.
                    u.Driver.PaceScale = 1f * wet;
                    if (Time.time >= nextEstimate)
                    {
                        nextEstimate = Time.time + 3f;
                        LastKnown = new Vector3(Mathf.Round(p.x / 40f) * 40f, 0f, Mathf.Round(p.z / 40f) * 40f);
                    }
                    SetGoal(u, LastKnown + Spread(u, 14f) + pv * 2f, false);
                    break;
                case State.Pursuit:
                {
                    // Controlled catch-up, no rubber band: a few percent quicker when far behind, a little slower when on the bumper.
                    float pace = u.Distance > 260f ? 1.06f : u.Distance < 30f ? 0.93f : 1f;
                    u.Driver.PaceScale = pace * wet;
                    if (u.Role == Role.Intercept && TryIntercept(u, p, pv)) break;
                    float lead = u.Role == Role.Flank ? 2.4f : 1.1f;
                    Vector3 aim = u.Sees || sinceSeen < 2f ? p + pv * lead : LastKnown;
                    SetGoal(u, aim, false);
                    break;
                }
                case State.Search:
                {
                    u.Driver.PaceScale = searchPace * wet;
                    if (u.Driver.AtGoal || Vector3.Distance(Flat(u.Car.Body.position), Flat(u.Goal)) < 16f || stateTime < 0.35f)
                        SetGoal(u, SearchPoint(u), false, force: true);
                    break;
                }
                case State.StoodDown:
                    break;
            }
        }

        /// <summary>
        /// The interceptor: if it can reach a point on the player's route ahead clearly before the player, it drives there
        /// and parks on one side of the road (a gap stays open, so it is never an unavoidable block); it pulls out to chase
        /// once the player is close or has gone past.
        /// </summary>
        private bool TryIntercept(Unit u, Vector3 p, Vector3 pv)
        {
            if (u.Intercepting)
            {
                Vector3 toPlayer = Flat(p - u.InterceptAt);
                bool passed = Vector3.Dot(toPlayer, u.InterceptDirection) > 8f;
                if (toPlayer.magnitude < 40f || passed || Time.time > u.InterceptUntil)
                {
                    u.Intercepting = false;
                    u.NextIntercept = Time.time + 9f;
                    return false;
                }
                return true;
            }
            if (Time.time < u.NextIntercept || RouteAhead == null) return false;
            u.NextIntercept = Time.time + 2f;
            const float ahead = 230f;
            var point = RouteAhead(ahead);
            var further = RouteAhead(ahead + 12f);
            if (point == null || further == null) return false;
            Vector3 at = point.Value, dir = Flat(further.Value - at).normalized;
            if (dir.sqrMagnitude < 0.5f || NoStopping != null && NoStopping(at)) return false;
            float playerSpeed = Mathf.Max(12f, Flat(pv).magnitude);
            float mine = Vector3.Distance(Flat(u.Car.Body.position), Flat(at)) * 1.35f / 32f;
            if (mine > ahead / playerSpeed - 2.5f) return false;
            var side = Vector3.Cross(Vector3.up, dir) * (rng.NextDouble() < 0.5 ? -3.6f : 3.6f);
            u.Intercepting = true;
            u.InterceptAt = at;
            u.InterceptDirection = dir;
            u.InterceptUntil = Time.time + 25f;
            Interceptions++;
            SetGoal(u, at + side, true, force: true);
            Event?.Invoke("intercept", u);
            return true;
        }

        private Vector3 SearchPoint(Unit u)
        {
            var net = navigation != null ? navigation.Network : null;
            if (net == null || net.Nodes.Count == 0) return LastKnown + Spread(u, 40f);
            // A random junction within 260 m of the last known position (the first sweep goes to the position itself).
            var near = new List<Vector3>();
            for (int i = 0; i < net.Nodes.Count; i++)
                if (Vector3.Distance(Flat(net.Nodes[i].position), Flat(LastKnown)) < 260f) near.Add(net.Nodes[i].position);
            if (near.Count == 0 || stateTime < 1f && u.Role != Role.Flank) return LastKnown + Spread(u, 12f);
            return near[rng.Next(near.Count)];
        }

        private Vector3 Spread(Unit u, float metres)
        {
            float a = units.IndexOf(u) * 2.1f;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * metres;
        }

        /// <summary>Replans only when the goal moved enough to matter (or when forced), so a unit's line is stable.</summary>
        private void SetGoal(Unit u, Vector3 goal, bool hold, bool force = false)
        {
            if (!force && u.GoalHold == hold && u.Driver.State == RacerDriver.Mode.Driving &&
                (Vector3.Distance(Flat(u.Goal), Flat(goal)) < 14f || Time.time - u.LastPlan < 0.6f)) return;
            u.LastPlan = Time.time;
            u.Goal = goal;
            u.GoalHold = hold;
            u.Driver.SetGoal(goal, hold);
        }

        /// <summary>Heading of the player's car as a compass word (east, west, north, south).</summary>
        public string PlayerHeading()
        {
            if (player == null || player.Body == null) return "east";
            var v = player.Body.linearVelocity;
            if (v.sqrMagnitude < 4f) v = player.transform.forward;
            return Mathf.Abs(v.x) >= Mathf.Abs(v.z) ? (v.x > 0f ? "east" : "west") : (v.z > 0f ? "north" : "south");
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private static bool Visible(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.z < 300f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }

#if UNITY_EDITOR
        public void EditorConfigure(VehicleDefinition policeCar, RacerProfile driverProfile, CityNavigation nav, UnitSetup[] unitSetups, Closure[] roadClosures)
        {
            car = policeCar; profile = driverProfile; navigation = nav; setups = unitSetups; closures = roadClosures;
        }
#endif
    }
}
