using System.Collections.Generic;
using System.Text;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Development recorder: samples a vehicle every physics step and summarises the run per route section
    /// (speeds, accelerations, slip, body attitude, suspension use, contacts). Used for physics validation.
    /// </summary>
    public sealed class VehicleTelemetryLog : MonoBehaviour
    {
        private sealed class Stats
        {
            public string Name;
            public float Time, MinSpeed = float.MaxValue, MaxSpeed, MaxLatG, MaxAccelG, MaxBrakeG, MaxSlip, MaxRoll, MaxPitch, MaxYawRate;
            public float MinCompression = 1f, MaxCompression;
            public int Samples, BumpStopSamples, AirborneWheelSamples, BodyContacts;
        }

        private VehicleController vehicle;
        private DrivingRoute route;
        private readonly List<Stats> sections = new();
        private readonly Dictionary<string, Stats> byName = new();
        private int routeIndex = -1;
        private int nanSamples;
        private float lapStart = -1f, lastRouteDistance;
        private readonly List<float> laps = new();

        public bool Recording { get; private set; }

        public void Begin(VehicleController target, DrivingRoute drivingRoute)
        {
            if (vehicle != null) vehicle.Collided -= OnCollided;
            vehicle = target;
            route = drivingRoute;
            sections.Clear();
            byName.Clear();
            laps.Clear();
            nanSamples = 0;
            routeIndex = -1;
            lapStart = -1f;
            vehicle.Collided += OnCollided;
            Recording = true;
        }

        public void End()
        {
            Recording = false;
            if (vehicle != null) vehicle.Collided -= OnCollided;
        }

        private Stats Current
        {
            get
            {
                string name = "run";
                if (route != null && route.Count > 0)
                {
                    routeIndex = route.FindClosest(vehicle.Body.position, routeIndex);
                    name = route.SectionAt(route.DistanceAt(routeIndex));
                }
                if (!byName.TryGetValue(name, out var s))
                {
                    s = new Stats { Name = name };
                    byName[name] = s;
                    sections.Add(s);
                }
                return s;
            }
        }

        private void FixedUpdate()
        {
            if (!Recording || vehicle == null || !vehicle.IsConfigured) return;
            var body = vehicle.Body;
            var t = vehicle.Telemetry;
            if (float.IsNaN(body.position.x) || float.IsNaN(t.Speed)) { nanSamples++; return; }

            var s = Current;
            float dt = Time.fixedDeltaTime;
            s.Samples++;
            s.Time += dt;
            s.MinSpeed = Mathf.Min(s.MinSpeed, t.SpeedKph);
            s.MaxSpeed = Mathf.Max(s.MaxSpeed, t.SpeedKph);
            s.MaxLatG = Mathf.Max(s.MaxLatG, Mathf.Abs(t.LateralG));
            s.MaxAccelG = Mathf.Max(s.MaxAccelG, t.LongitudinalG);
            s.MaxBrakeG = Mathf.Max(s.MaxBrakeG, -t.LongitudinalG);
            s.MaxSlip = Mathf.Max(s.MaxSlip, t.MaxSlip);
            s.MaxYawRate = Mathf.Max(s.MaxYawRate, Mathf.Abs(t.YawRate));
            Vector3 euler = body.rotation.eulerAngles;
            s.MaxPitch = Mathf.Max(s.MaxPitch, Mathf.Abs(Mathf.DeltaAngle(0f, euler.x)));
            s.MaxRoll = Mathf.Max(s.MaxRoll, Mathf.Abs(Mathf.DeltaAngle(0f, euler.z)));
            foreach (var w in vehicle.Wheels)
            {
                if (!w.IsGrounded) { s.AirborneWheelSamples++; continue; }
                if (w.OnBumpStop) s.BumpStopSamples++;
                s.MinCompression = Mathf.Min(s.MinCompression, w.CompressionRatio);
                s.MaxCompression = Mathf.Max(s.MaxCompression, w.CompressionRatio);
            }

            if (route != null && route.Count > 0)
            {
                float d = route.DistanceAt(routeIndex);
                if (lastRouteDistance > route.Length * 0.9f && d < route.Length * 0.1f)
                {
                    if (lapStart >= 0f) laps.Add(Time.time - lapStart);
                    lapStart = Time.time;
                }
                lastRouteDistance = d;
            }
        }

        private void OnCollided(VehicleCollision c)
        {
            if (Recording) Current.BodyContacts++;
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[TelemetryLog] {vehicle?.Profile?.name} NaN samples: {nanSamples}  laps: {string.Join(", ", laps.ConvertAll(l => l.ToString("0.0") + " s"))}");
            sb.AppendLine("section          time   v min/max km/h  latG  accG  brkG  slip  roll° pitch° yaw°/s comp min/max  bumpStop airWheel contacts");
            foreach (var s in sections)
                sb.AppendLine($"{s.Name,-16} {s.Time,5:0.0}  {s.MinSpeed,5:0}/{s.MaxSpeed,-5:0}      {s.MaxLatG,4:0.00}  {s.MaxAccelG,4:0.00}  {s.MaxBrakeG,4:0.00}  {s.MaxSlip,4:0.00}  {s.MaxRoll,4:0.0}  {s.MaxPitch,4:0.0}  {s.MaxYawRate,5:0}   {s.MinCompression,4:0.00}/{s.MaxCompression,-4:0.00}   {s.BumpStopSamples,6} {s.AirborneWheelSamples,7} {s.BodyContacts,6}");
            return sb.ToString();
        }
    }
}
