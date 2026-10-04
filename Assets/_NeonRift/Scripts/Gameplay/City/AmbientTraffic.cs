using System;
using System.Collections.Generic;
using NeonRift.Missions;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Background traffic: a pool of simple low-poly cars (no physics, no colliders) driving the road graph's lanes
    /// with their head and tail lamps lit, so long avenues and the skyway carry streams of light. Inside
    /// <see cref="yieldRange"/> of the player a car brakes to a halt rather than drive at or through the player, and it
    /// is recycled onto a road further out as soon as it is off screen (or if the player drives into it), so cars never
    /// pop in view and never ghost through the player. In a lockdown they pull over and flash their hazards.
    /// One Update for the whole pool; no allocations per frame.
    /// </summary>
    public sealed class AmbientTraffic : MonoBehaviour, IMissionWorldComponent
    {
        private sealed class Car
        {
            public Transform Root;
            public Renderer Indicators;
            public int Edge, From, To;
            public float Along, Length, Speed, Cruise, Lane;
        }

        [SerializeField] private CityNavigation navigation;
        [SerializeField] private Transform[] cars = Array.Empty<Transform>();
        [Tooltip("Per car: the renderer of its indicator lamps (hazards in a lockdown).")]
        [SerializeField] private Renderer[] indicators = Array.Empty<Renderer>();
        [Tooltip("Cars stop for a player this far ahead in their lane; any car this close is recycled once off screen, m.")]
        [SerializeField, Min(20f)] private float yieldRange = 45f;
        [SerializeField, Min(30f)] private float spawnMin = 70f;
        [SerializeField, Min(100f)] private float spawnMax = 300f;
        [Tooltip("Cars further than this are recycled back near the player, so the traffic stays where it can be seen.")]
        [SerializeField, Min(150f)] private float recycleBeyond = 420f;
        [SerializeField] private Vector2 cruiseRange = new(9f, 15f);

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private readonly List<Car> pool = new();
        private List<int>[] adjacency;
        private readonly List<int> drivable = new();
        private readonly List<int> scratch = new();
        private MissionWorld world;
        private Transform viewer;
        private System.Random rng = new(1234);
        private bool lockdown, placedNearPlayer;
        private Vector3 eye, look = Vector3.forward;
        private readonly List<Vector3> drivers = new();
        private RivalDirector rivals;
        private bool rivalsSearched;
        private MaterialPropertyBlock block;

        public int Count => pool.Count;

        private void Start()
        {
            block = new MaterialPropertyBlock();
            Build();
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.SecurityChanged += OnSecurity;
            lockdown = false;
            placedNearPlayer = false;
        }

        public void Unbind()
        {
            if (world != null) world.SecurityChanged -= OnSecurity;
            world = null;
        }

        private void OnSecurity(SecurityLevel level) => lockdown = level == SecurityLevel.Lockdown;

        private void Build()
        {
            var net = navigation != null ? navigation.Network : null;
            if (net == null) { enabled = false; return; }
            adjacency = new List<int>[net.Nodes.Count];
            for (int i = 0; i < adjacency.Length; i++) adjacency[i] = new List<int>();
            for (int e = 0; e < net.Edges.Count; e++)
            {
                var edge = net.Edges[e];
                if (edge.roadClass is RoadClass.Alley or RoadClass.Service or RoadClass.Tunnel) continue;
                adjacency[edge.a].Add(e);
                adjacency[edge.b].Add(e);
                drivable.Add(e);
            }
            for (int i = 0; i < cars.Length; i++)
            {
                var car = new Car { Root = cars[i], Indicators = i < indicators.Length ? indicators[i] : null };
                pool.Add(car);
                Respawn(car, Vector3.zero, any: true);
            }
        }

        private Vector3 ViewerPosition()
        {
            if (world != null && world.PlayerBody != null) return world.PlayerBody.position;
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            return viewer != null ? viewer.position : Vector3.zero;
        }

        private void Update()
        {
            if (adjacency == null) return;
            var net = navigation.Network;
            Vector3 player = ViewerPosition();
            // The pool starts spread over the whole city; once a player exists, deal it out around them.
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            eye = viewer != null ? viewer.position : player;
            look = viewer != null ? viewer.forward : Vector3.forward;
            if (!placedNearPlayer && world != null && world.PlayerBody != null)
            {
                foreach (var car in pool) Respawn(car, player, any: false);
                placedNearPlayer = true;
            }
            // The player and the rival crews: traffic yields to and never overlaps any of them.
            drivers.Clear();
            drivers.Add(player);
            if (!rivalsSearched) { rivals = FindAnyObjectByType<RivalDirector>(); rivalsSearched = true; }
            if (rivals != null)
                foreach (var r in rivals.Rivals)
                    if (r.Car != null) drivers.Add(r.Car.transform.position);
            float dt = Time.deltaTime;
            bool blink = Mathf.Repeat(Time.time * 1.6f, 1f) < 0.5f;
            foreach (var car in pool)
            {
                Vector3 at = car.Root.position;
                // Yield: brake for a driver close ahead in (or across) this car's lane instead of driving at them.
                bool yielding = false;
                foreach (var driver in drivers)
                {
                    Vector3 rel = driver - at;
                    float ahead = Vector3.Dot(car.Root.forward, rel), lateral = Mathf.Abs(Vector3.Dot(car.Root.right, rel));
                    if (ahead > 0f && ahead < yieldRange && lateral < 4.5f) { yielding = true; break; }
                }
                float target = lockdown || yielding ? 0f : car.Cruise;
                car.Speed = Mathf.MoveTowards(car.Speed, target, (lockdown || yielding ? 6f : 3f) * dt);
                car.Along += car.Speed * dt;
                while (car.Along > car.Length) { car.Along -= car.Length; NextEdge(car); }
                Vector3 a = net.NodePosition(car.From), b = net.NodePosition(car.To);
                Vector3 dir = b - a;
                Vector3 flat = new(dir.x, 0f, dir.z);
                Vector3 right = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
                // Pull over in a lockdown: drift to the kerb as they stop.
                float lane = car.Lane + (lockdown ? Mathf.Clamp01(1f - car.Speed / Mathf.Max(1f, car.Cruise)) * 1.6f : 0f);
                Vector3 p = Vector3.Lerp(a, b, car.Length > 0f ? car.Along / car.Length : 0f) + right * lane;
                car.Root.SetPositionAndRotation(p, Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir : Vector3.forward));
                float d2 = (p - player).sqrMagnitude;
                bool offScreen = Vector3.Dot(look, (p - eye).normalized) < 0.3f;
                // Overlapping a driver (who drove into it) would ghost: recycle at once, as when far or close off screen.
                bool overlapping = false;
                foreach (var driver in drivers)
                {
                    Vector3 local = car.Root.InverseTransformPoint(driver);
                    if (Mathf.Abs(local.x) < 2.4f && Mathf.Abs(local.z) < 4f) { overlapping = true; break; }
                }
                if (overlapping || d2 > recycleBeyond * recycleBeyond || d2 < yieldRange * yieldRange && offScreen)
                    Respawn(car, player, any: false);
                if (car.Indicators != null)
                {
                    block.SetColor(EmissionColor, lockdown && blink ? new Color(3f, 1.4f, 0.1f) : Color.black);
                    car.Indicators.SetPropertyBlock(block);
                }
            }
        }

        private void NextEdge(Car car)
        {
            var net = navigation.Network;
            scratch.Clear();
            foreach (int e in adjacency[car.To])
                if (e != car.Edge && !navigation.IsBlocked(e)) scratch.Add(e);
            if (scratch.Count == 0) scratch.Add(car.Edge);   // dead end: turn back
            int next = scratch[rng.Next(scratch.Count)];
            var edge = net.Edges[next];
            int from = car.To, to = edge.a == from ? edge.b : edge.a;
            Assign(car, next, from, to);
        }

        private void Respawn(Car car, Vector3 player, bool any)
        {
            var net = navigation.Network;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int e = drivable[rng.Next(drivable.Count)];
                var edge = net.Edges[e];
                bool forward = rng.NextDouble() < 0.5;
                int from = forward ? edge.a : edge.b, to = forward ? edge.b : edge.a;
                float t = (float)rng.NextDouble();
                Vector3 p = Vector3.Lerp(net.NodePosition(from), net.NodePosition(to), t);
                float d = Vector3.Distance(p, player);
                if (!any && (d < spawnMin || d > spawnMax)) continue;
                // Never appear in plain view close by: near spawns go behind the camera.
                if (!any && placedNearPlayer && d < 160f && attempt < 30 && Vector3.Dot(look, (p - eye).normalized) > 0.2f) continue;
                Assign(car, e, from, to);
                car.Along = t * car.Length;
                car.Cruise = Mathf.Lerp(cruiseRange.x, cruiseRange.y, (float)rng.NextDouble()) * (edge.roadClass == RoadClass.Arterial || edge.roadClass == RoadClass.Skyway ? 1.25f : 1f);
                car.Speed = lockdown ? 0f : car.Cruise;
                return;
            }
        }

        private void Assign(Car car, int edgeIndex, int from, int to)
        {
            var net = navigation.Network;
            var edge = net.Edges[edgeIndex];
            car.Edge = edgeIndex;
            car.From = from;
            car.To = to;
            car.Length = Mathf.Max(0.5f, Vector3.Distance(net.NodePosition(from), net.NodePosition(to)));
            // Right-hand traffic: the kerb-side lane of the car's own carriageway.
            float half = edge.halfWidth - edge.median;
            int lanes = Mathf.Max(1, edge.lanesPerDirection);
            int laneIndex = rng.Next(lanes);
            car.Lane = edge.median + half * ((laneIndex + 0.5f) / lanes);
        }

#if UNITY_EDITOR
        public void EditorConfigure(CityNavigation nav, Transform[] pooledCars, Renderer[] indicatorRenderers)
        {
            navigation = nav;
            cars = pooledCars;
            indicators = indicatorRenderers;
        }
#endif
    }
}
