using System;
using System.Collections.Generic;
using NeonRift.Missions;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// City traffic from the generic passenger car pack (<see cref="TrafficCar"/>: real Rigidbodies). Two pools:
    /// <list type="bullet">
    /// <item><b>Roaming</b>: driving the road graph's kerb-side lanes around the player, keeping their distance from
    /// whatever is ahead (other traffic, the player, the rival crews), slowing for corners, pulling over in a
    /// lockdown.</item>
    /// <item><b>Parked</b>: dealt into the parking-lot and garage-deck bays nearest the player (kinematic until hit).</item>
    /// </list>
    /// Cars are only placed or recycled out of the player's view, so nothing pops. A car hit by the player or a rival
    /// reacts physically (shunted, spun, hazards) and stays where it ended up until it is out of sight.
    /// </summary>
    public sealed class CityTraffic : MonoBehaviour, IMissionWorldComponent
    {
        private sealed class Roamer
        {
            public TrafficCar Car;
            public int Edge = -1, From, To;
            public float Lane, Cruise, StuckTime;
            public int Seed;
        }

        [SerializeField] private CityNavigation navigation;
        [SerializeField] private TrafficCar[] prefabs = Array.Empty<TrafficCar>();
        [Tooltip("Bays for parked cars: x, y, z, yaw (degrees).")]
        [SerializeField] private Vector4[] spots = Array.Empty<Vector4>();
        [SerializeField, Min(0)] private int roamingCount = 14;
        [SerializeField, Min(0)] private int parkedCount = 26;
        [SerializeField, Min(30f)] private float spawnMin = 70f;
        [SerializeField, Min(60f)] private float spawnMax = 260f;
        [SerializeField, Min(100f)] private float recycleBeyond = 380f;
        [SerializeField, Min(50f)] private float parkedRadius = 170f;
        [SerializeField] private Vector2 cruiseRange = new(8f, 13f);

        private static readonly Color[] Tints =
        {
            Color.white, Color.white, new(0.55f, 0.55f, 0.58f), new(0.35f, 0.36f, 0.4f), new(0.85f, 0.9f, 1f), new(1f, 0.82f, 0.78f)
        };

        private readonly List<Roamer> roamers = new();
        private readonly List<TrafficCar> parked = new();
        private readonly Dictionary<TrafficCar, int> parkedSpot = new();
        private readonly HashSet<int> usedSpots = new();
        private readonly List<int> scratch = new();
        private readonly List<int> drivable = new();
        private HashSet<int> drivableSet = new();
        private List<int>[] adjacency;
        private System.Random rng = new(4242);
        private MissionWorld world;
        private Transform viewer;
        private Camera viewCamera;
        private bool lockdown;
        private float nextParkCheck;
        private int obstacleMask;

        public int RoamingActive { get; private set; }
        public int ParkedActive => parkedSpot.Count;
        public int SpotCount => spots.Length;
        public IReadOnlyList<TrafficCar> ParkedCars => parked;

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.SecurityChanged += OnSecurity;
            lockdown = false;
            placed = false;
        }

        public void Unbind()
        {
            if (world != null) world.SecurityChanged -= OnSecurity;
            world = null;
        }

        private void OnSecurity(SecurityLevel level) => lockdown = level == SecurityLevel.Lockdown;

        private bool placed;

        private void Start()
        {
            int traffic = LayerMask.NameToLayer("Traffic");
            if (traffic >= 0) Physics.IgnoreLayerCollision(traffic, traffic, true);
            obstacleMask = LayerMask.GetMask("Vehicle", "Traffic");
            var net = navigation != null ? navigation.Network : null;
            if (net == null || prefabs.Length == 0) { enabled = false; return; }
            // Drivable edges: no alleys, service roads, tunnels or the mission compound...
            var usable = new HashSet<int>();
            for (int e = 0; e < net.Edges.Count; e++)
            {
                var edge = net.Edges[e];
                if (edge.roadClass is RoadClass.Alley or RoadClass.Service or RoadClass.Tunnel) continue;
                if (InCompound(net.NodePosition(edge.a)) || InCompound(net.NodePosition(edge.b))) continue;
                usable.Add(e);
            }
            // ...and no dead ends: a spur (like the approach to the compound gate) would strand cars at its end,
            // blocking the road. Prune nodes with a single drivable edge until none are left.
            var degree = new int[net.Nodes.Count];
            bool pruned = true;
            while (pruned)
            {
                pruned = false;
                Array.Clear(degree, 0, degree.Length);
                foreach (int e in usable) { degree[net.Edges[e].a]++; degree[net.Edges[e].b]++; }
                usable.RemoveWhere(e => { bool dead = degree[net.Edges[e].a] < 2 || degree[net.Edges[e].b] < 2; pruned |= dead; return dead; });
            }
            adjacency = new List<int>[net.Nodes.Count];
            for (int i = 0; i < adjacency.Length; i++) adjacency[i] = new List<int>();
            foreach (int e in usable)
            {
                adjacency[net.Edges[e].a].Add(e);
                adjacency[net.Edges[e].b].Add(e);
                drivable.Add(e);
            }
            drivableSet = usable;
            for (int i = 0; i < roamingCount; i++) roamers.Add(new Roamer { Car = Create(i), Seed = rng.Next() });
            for (int i = 0; i < parkedCount; i++) parked.Add(Create(roamingCount + i));
        }

        private TrafficCar Create(int i)
        {
            var car = Instantiate(prefabs[i % prefabs.Length], transform);
            car.name = $"{prefabs[i % prefabs.Length].name}_{i:00}";
            car.SetTint(Tints[rng.Next(Tints.Length)]);
            car.gameObject.SetActive(false);
            return car;
        }

        private Vector3 Player()
        {
            if (world != null && world.PlayerBody != null) return world.PlayerBody.position;
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            return viewer != null ? viewer.position : Vector3.zero;
        }

        /// <summary>True if <paramref name="p"/> could be seen by the gameplay camera (with a margin).</summary>
        private bool InView(Vector3 p)
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return false;
            Vector3 v = viewCamera.WorldToViewportPoint(p);
            return v.z > -4f && v.z < 260f && v.x > -0.25f && v.x < 1.25f && v.y > -0.3f && v.y < 1.3f;
        }

        private void Update()
        {
            if (adjacency == null) return;
            Vector3 player = Player();
            if (!placed && world != null && world.PlayerBody != null)
            {
                // First deal: anywhere in range (the opening frames are a fade-in).
                placed = true;
                foreach (var r in roamers) Spawn(r, player, allowVisible: true);
                FillSpots(player, allowVisible: true);
            }
            if (!placed) return;

            int active = 0;
            foreach (var r in roamers)
            {
                if (!r.Car.gameObject.activeSelf) { Spawn(r, player, allowVisible: false); continue; }
                active++;
                Vector3 p = r.Car.Body.position;
                float d = Vector3.Distance(p, player);
                bool visible = InView(p);
                bool wrecked = !r.Car.Upright || r.StuckTime > 25f;
                if (!visible && (d > recycleBeyond || (wrecked && d > 40f) || (r.Car.Disturbed && d > 120f)))
                {
                    Spawn(r, player, allowVisible: false);
                    continue;
                }
                Steer(r);
            }
            RoamingActive = active;
            if (Time.time >= nextParkCheck)
            {
                nextParkCheck = Time.time + 1f;
                FillSpots(player, allowVisible: false);
            }
        }

        // ---------------- Roaming ----------------

        private void Steer(Roamer r)
        {
            var car = r.Car;
            var net = navigation.Network;
            if (car.State == TrafficCar.Mode.Knocked)
            {
                r.Edge = -1;   // re-acquire the road once it has come to rest
                return;
            }
            if (r.Edge < 0 && !Acquire(r)) { car.Stop(); return; }
            if (car.State == TrafficCar.Mode.Stopped) car.Resume();

            Vector3 p = car.Body.position;
            Vector3 a = net.NodePosition(r.From), b = net.NodePosition(r.To);
            Vector3 dir = Flat(b - a);
            float length = dir.magnitude;
            if (length < 0.5f) { NextEdge(r); return; }
            dir /= length;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            float along = Vector3.Dot(Flat(p - a), dir);
            float speed = Mathf.Max(0f, car.ForwardSpeed);
            float look = 6f + speed * 0.6f;
            if (along + 2f >= length) { NextEdge(r); return; }

            float lane = r.Lane + (lockdown ? 1.6f : 0f);
            Vector3 aim;
            float target = lockdown ? 0f : r.Cruise;
            float remaining = length - along;
            int next = PeekNext(r);
            if (remaining < look && next >= 0)
            {
                // Aim through the junction onto the next edge's lane.
                var e = net.Edges[next];
                int nTo = e.a == r.To ? e.b : e.a;
                Vector3 nd = Flat(net.NodePosition(nTo) - b).normalized;
                Vector3 nr = Vector3.Cross(Vector3.up, nd);
                aim = b + nd * (look - remaining) + nr * LaneFor(next);
                float turn = Vector3.Angle(dir, nd);
                if (turn > 25f) target = Mathf.Min(target, Mathf.Lerp(7f, 4.5f, Mathf.InverseLerp(25f, 100f, turn)));
            }
            else aim = a + dir * (along + look) + right * lane;
            aim.y = p.y;

            // Keep a braking distance from whatever is ahead: other traffic, the player, the rival crews.
            Vector3 fwd = car.transform.forward;
            Vector3 origin = p + Vector3.up * 0.9f + fwd * (car.Size.z * 0.5f + 0.3f);
            float probe = 4f + speed * 1.6f;
            if (Physics.SphereCast(origin, 1.0f, fwd, out var hit, probe, obstacleMask, QueryTriggerInteraction.Ignore) && hit.rigidbody != car.Body)
            {
                float theirs = hit.rigidbody != null ? Mathf.Max(0f, Vector3.Dot(hit.rigidbody.linearVelocity, fwd)) : 0f;
                float gap = Mathf.Max(0f, hit.distance - 2.5f);
                target = Mathf.Min(target, Mathf.Sqrt(theirs * theirs + 2f * 4f * gap));
            }
            // Junctions: give way to the player and the rivals (they never stop for traffic): halt short of the line.
            if (remaining < 22f && adjacency[r.To].Count > 2 && JunctionBusy(b, car))
                target = Mathf.Min(target, Mathf.Sqrt(2f * 5f * Mathf.Max(0f, remaining - 9f)));
            car.Aim = aim;
            car.TargetSpeed = target;
            r.StuckTime = target > 1f && speed < 0.5f ? r.StuckTime + Time.deltaTime : 0f;
        }

        private float LaneFor(int edgeIndex)
        {
            var e = navigation.Network.Edges[edgeIndex];
            float half = e.halfWidth - e.median;
            int lanes = Mathf.Max(1, e.lanesPerDirection);
            // Kerb-side lane of the car's own carriageway (right-hand traffic).
            return e.median + half * ((lanes - 0.5f) / lanes);
        }

        /// <summary>The edge taken after this one: stable for the car (its seed), varied between cars, never a U-turn.</summary>
        private int PeekNext(Roamer r)
        {
            scratch.Clear();
            foreach (int e in adjacency[r.To])
                if (e != r.Edge && !navigation.IsBlocked(e)) scratch.Add(e);
            if (scratch.Count == 0) return -1;
            return scratch[(int)((uint)(r.Seed * 73856093 ^ r.Edge * 19349663 ^ r.To * 83492791) % (uint)scratch.Count)];
        }

        private void NextEdge(Roamer r)
        {
            int next = PeekNext(r);
            if (next < 0) { r.Edge = -1; return; }
            var e = navigation.Network.Edges[next];
            r.From = r.To;
            r.To = e.a == r.From ? e.b : e.a;
            r.Edge = next;
            r.Lane = LaneFor(next);
        }

        /// <summary>Joins the nearest drivable edge in the direction the car is facing.</summary>
        private bool Acquire(Roamer r)
        {
            var net = navigation.Network;
            Vector3 p = r.Car.Body.position;
            if (!net.TryGetNearestEdge(p, out int edge, out _, out float distance) || distance > 12f || !drivableSet.Contains(edge)) return false;
            var e = net.Edges[edge];
            Vector3 ab = Flat(net.NodePosition(e.b) - net.NodePosition(e.a));
            bool forward = Vector3.Dot(ab, r.Car.transform.forward) >= 0f;
            r.Edge = edge;
            r.From = forward ? e.a : e.b;
            r.To = forward ? e.b : e.a;
            r.Lane = LaneFor(edge);
            return true;
        }

        private void Spawn(Roamer r, Vector3 player, bool allowVisible)
        {
            var net = navigation.Network;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                int edgeIndex = drivable[rng.Next(drivable.Count)];
                var e = net.Edges[edgeIndex];
                bool forward = rng.NextDouble() < 0.5;
                int from = forward ? e.a : e.b, to = forward ? e.b : e.a;
                Vector3 a = net.NodePosition(from), b = net.NodePosition(to);
                Vector3 dir = Flat(b - a);
                float len = dir.magnitude;
                if (len < 20f) continue;
                dir /= len;
                float t = Mathf.Lerp(8f, len - 8f, (float)rng.NextDouble());
                Vector3 p = a + dir * t + Vector3.Cross(Vector3.up, dir) * LaneFor(edgeIndex);
                float d = Vector3.Distance(p, player);
                if (d < spawnMin || d > spawnMax) continue;
                if (!allowVisible && InView(p)) continue;
                if (!Ground(ref p)) continue;
                if (Physics.CheckBox(p + Vector3.up * 1f, new Vector3(1.4f, 0.8f, 3f), Quaternion.LookRotation(dir), obstacleMask, QueryTriggerInteraction.Ignore)) continue;
                r.Car.gameObject.SetActive(true);
                r.Car.Drive(p, Quaternion.LookRotation(dir), lockdown ? 0f : 6f);
                r.Edge = edgeIndex;
                r.From = from;
                r.To = to;
                r.Lane = LaneFor(edgeIndex);
                r.Cruise = Mathf.Lerp(cruiseRange.x, cruiseRange.y, (float)rng.NextDouble()) * (e.roadClass is RoadClass.Arterial or RoadClass.Skyway ? 1.3f : 1f);
                r.StuckTime = 0f;
                r.Seed = rng.Next();
                return;
            }
            if (r.Car.gameObject.activeSelf && !InView(r.Car.Body.position)) r.Car.gameObject.SetActive(false);
        }

        // ---------------- Parked ----------------

        private readonly List<(float d, int i)> candidates = new();

        private void FillSpots(Vector3 player, bool allowVisible)
        {
            if (spots.Length == 0) return;
            candidates.Clear();
            for (int i = 0; i < spots.Length; i++)
            {
                float d = Vector3.Distance(player, spots[i]);
                if (d < parkedRadius && !InCompound(spots[i])) candidates.Add((d, i));
            }
            candidates.Sort((x, y) => x.d.CompareTo(y.d));
            int wanted = Mathf.Min(parked.Count, candidates.Count);
            // Release cars whose bay is no longer among the nearest (or that were knocked out of it), out of sight.
            foreach (var car in parked)
            {
                if (!parkedSpot.TryGetValue(car, out int spot)) continue;
                bool keep = false;
                for (int k = 0; k < wanted; k++) if (candidates[k].i == spot) { keep = true; break; }
                Vector3 p = car.Body.position;
                if (keep && !(car.Disturbed && Vector3.Distance(p, player) > 120f)) continue;
                if (InView(p)) continue;
                parkedSpot.Remove(car);
                usedSpots.Remove(spot);
                car.gameObject.SetActive(false);
            }
            for (int k = 0; k < wanted; k++)
            {
                int spot = candidates[k].i;
                if (usedSpots.Contains(spot)) continue;
                Vector3 p = spots[spot];
                if (!allowVisible && InView(p) && candidates[k].d < 120f) continue;
                TrafficCar free = null;
                foreach (var car in parked) if (!parkedSpot.ContainsKey(car)) { free = car; break; }
                if (free == null) break;
                free.gameObject.SetActive(true);
                free.Park(p, Quaternion.Euler(0f, spots[spot].w, 0f));
                parkedSpot[free] = spot;
                usedSpots.Add(spot);
            }
        }

        private bool Ground(ref Vector3 p)
        {
            if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f, LayerMask.GetMask("Drivable", "Default"), QueryTriggerInteraction.Ignore)) return false;
            p.y = hit.point.y;
            return true;
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        [Tooltip("No traffic or parked cars inside this area (x/z min, x/z max): the Data Core compound.")]
        [SerializeField] private Rect exclusion = new(100f, 55f, 120f, 155f);

        private bool InCompound(Vector3 p) => exclusion.Contains(new Vector2(p.x, p.z));

        private readonly List<Rigidbody> drivers = new();
        private RivalDirector rivalDirector;
        private bool rivalsLooked;

        /// <summary>The player and the rival crews: they never yield, so traffic gives way to them at junctions.</summary>
        private List<Rigidbody> Drivers()
        {
            drivers.Clear();
            if (world != null && world.PlayerBody != null) drivers.Add(world.PlayerBody);
            if (!rivalsLooked) { rivalDirector = FindAnyObjectByType<RivalDirector>(); rivalsLooked = true; }
            if (rivalDirector != null)
                for (int i = 0; i < rivalDirector.Rivals.Count; i++)
                    if (rivalDirector.Rivals[i].Car != null) drivers.Add(rivalDirector.Rivals[i].Car.Body);
            return drivers;
        }

        /// <summary>True if a player or rival car will reach <paramref name="node"/> within a few seconds.</summary>
        private bool JunctionBusy(Vector3 node, TrafficCar self)
        {
            foreach (var d in Drivers())
            {
                Vector3 to = Flat(node - d.position);
                float dist = to.magnitude;
                if (dist > 70f) continue;
                if (dist < 8f) return true;
                float closing = Vector3.Dot(Flat(d.linearVelocity), to / dist);
                if (closing > 2f && dist / closing < 3.5f) return true;
            }
            return false;
        }

#if UNITY_EDITOR
        public void EditorConfigure(CityNavigation nav, TrafficCar[] carPrefabs, Vector4[] parkingSpots, int roaming, int parkedCars)
        {
            navigation = nav;
            prefabs = carPrefabs;
            spots = parkingSpots;
            roamingCount = roaming;
            parkedCount = parkedCars;
        }
#endif
    }
}
