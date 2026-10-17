using System.Collections.Generic;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// An AI car for Harbor Rain (rival crew, security patrol): a catalog vehicle spawned through the stock
    /// <see cref="VehicleSpawnPoint"/> and driven by the stock <see cref="WaypointDriver"/>, so it has the same physics
    /// as the player's car. Adds looping, a simple pursuit (re-aimed twice a second) and stuck recovery.
    /// </summary>
    public sealed class HarborAIDriver : MonoBehaviour, IVehicleInputSource
    {
        public enum Mode { Idle, Route, Loop, Pursue }

        public VehicleController Vehicle { get; private set; }
        public Mode Current { get; private set; }
        public bool Arrived => Current == Mode.Route && driver != null && (driver.Finished || Vehicle != null && points.Count > 0 &&
                               Vector3.Distance(Vehicle.Body.position, new Vector3(points[^1].x, Vehicle.Body.position.y, points[^1].z)) < 7f);

        private VehicleSpawnPoint spawn;
        private WaypointDriver driver;
        private List<Vector3> points = new();
        private List<float> speeds = new();
        private Transform pursueTarget;
        private float pursueSpeed, nextAim, stuckTime;

        public static HarborAIDriver Create(string name, VehicleDefinition definition, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            var ai = go.AddComponent<HarborAIDriver>();
            ai.spawn = go.AddComponent<VehicleSpawnPoint>();
            ai.Vehicle = ai.spawn.Spawn(definition, name + "_Car");
            if (ai.Vehicle == null) return ai;
            foreach (var r in ai.Vehicle.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(ai);
            return ai;
        }

        public void ResetToStart()
        {
            Current = Mode.Idle;
            driver = null;
            if (Vehicle != null) Vehicle.Teleport(spawn.Position, spawn.Rotation);
        }

        public void Follow(IEnumerable<Vector3> route, float speed, bool loop)
        {
            points = new List<Vector3>(route);
            speeds = new List<float>();
            // Slow for corners (dock lanes are 12 m wide), cruise on straights.
            for (int i = 0; i < points.Count; i++)
            {
                bool corner = false;
                if (i > 0 && i < points.Count - 1 || loop)
                {
                    var a = points[(i - 1 + points.Count) % points.Count];
                    var b = points[(i + 1) % points.Count];
                    corner = Vector3.Angle(points[i] - a, b - points[i]) > 30f;
                }
                speeds.Add(corner ? Mathf.Min(speed, 7f) : speed);
            }
            if (!loop) speeds[^1] = 4f;   // WaypointDriver stops dead once a 0 m/s final point becomes its target
            Current = loop ? Mode.Loop : Mode.Route;
            Restart(0);
        }

        public void Pursue(Transform target, float speed)
        {
            pursueTarget = target;
            pursueSpeed = speed;
            Current = Mode.Pursue;
            nextAim = 0f;
        }

        public void Stop() { Current = Mode.Idle; driver = null; }

        private void Restart(int from)
        {
            if (Vehicle == null) return;
            var route = new List<Vector3> { Vehicle.Body.position };
            var s = new List<float> { speeds[from] };
            for (int i = from; i < points.Count; i++) { route.Add(points[i]); s.Add(speeds[i]); }
            if (Current == Mode.Loop) for (int i = 0; i < from; i++) { route.Add(points[i]); s.Add(speeds[i]); }
            driver = new WaypointDriver(Vehicle, route, s);
        }

        public DrivingInput ReadInput()
        {
            if (Vehicle == null || Current == Mode.Idle || driver == null && Current != Mode.Pursue)
                return new DrivingInput { Brake = 1f, Handbrake = Vehicle != null && Vehicle.Body.linearVelocity.magnitude < 1f };
            if (Current == Mode.Pursue && Time.time >= nextAim && pursueTarget != null)
            {
                nextAim = Time.time + 0.5f;
                var body = pursueTarget.GetComponent<Rigidbody>();
                Vector3 lead = pursueTarget.position + (body != null ? body.linearVelocity * 0.8f : Vector3.zero);
                driver = new WaypointDriver(Vehicle, new[] { Vehicle.Body.position, lead, lead + (lead - Vehicle.Body.position).normalized * 20f },
                                            new[] { pursueSpeed, pursueSpeed, pursueSpeed });
            }
            if (driver == null) return DrivingInput.None;
            if (Current == Mode.Loop && driver.Finished) Restart(0);
            if (Arrived) return new DrivingInput { Brake = 1f, Handbrake = Vehicle.Body.linearVelocity.magnitude < 1f };
            return driver.ReadInput();
        }

        private void FixedUpdate()
        {
            if (Vehicle == null || Current == Mode.Idle || Arrived) { stuckTime = 0f; return; }
            // Wedged against a stack or flipped: put the car back on its wheels.
            stuckTime = Vehicle.Body.linearVelocity.magnitude < 1f ? stuckTime + Time.fixedDeltaTime : 0f;
            if (stuckTime > 4f || Vector3.Dot(Vehicle.transform.up, Vector3.up) < 0.3f)
            {
                stuckTime = 0f;
                if (Current == Mode.Pursue || points.Count < 2) { Vehicle.Recover(); return; }
                // Wedged off the route: put the car back on its nearest waypoint, facing the next one.
                int nearest = 0;
                for (int i = 1; i < points.Count; i++)
                    if ((points[i] - Vehicle.Body.position).sqrMagnitude < (points[nearest] - Vehicle.Body.position).sqrMagnitude) nearest = i;
                int next = Mathf.Min(nearest + 1, points.Count - 1);
                var dir = points[next] - points[nearest];
                Vehicle.Teleport(points[nearest] + Vector3.up * 0.3f, Quaternion.LookRotation(dir.sqrMagnitude > 1f ? dir : Vehicle.transform.forward));
                Restart(next);
            }
        }
    }
}
