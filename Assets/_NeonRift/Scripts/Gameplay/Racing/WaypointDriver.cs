using System.Collections.Generic;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Drives a car along a short scripted polyline (cinematics: cars pulling out of a garage onto the street) through
    /// the normal input path: pure-pursuit steering and a speed target per waypoint with braking ahead of slower ones,
    /// real physics. Stops and holds the handbrake at the end. For open-road racing use <see cref="RacerDriver"/>.
    /// </summary>
    public sealed class WaypointDriver : IVehicleInputSource
    {
        private readonly VehicleController vehicle;
        private readonly List<Vector3> points = new();
        private readonly List<float> speeds = new();
        private int next = 1;
        private float startAt;

        public bool Finished { get; private set; }

        /// <param name="waypoints">World points, the first near the car.</param>
        /// <param name="targetSpeeds">Speed to hold arriving at each waypoint, m/s (the last is usually 0).</param>
        /// <param name="delay">Seconds to wait before setting off.</param>
        public WaypointDriver(VehicleController vehicle, IEnumerable<Vector3> waypoints, IEnumerable<float> targetSpeeds, float delay = 0f)
        {
            this.vehicle = vehicle;
            points.AddRange(waypoints);
            speeds.AddRange(targetSpeeds);
            while (speeds.Count < points.Count) speeds.Add(speeds.Count > 0 ? speeds[^1] : 5f);
            startAt = Time.time + delay;
        }

        public DrivingInput ReadInput()
        {
            var body = vehicle.Body;
            if (body == null || points.Count < 2) return DrivingInput.None;
            float forwardSpeed = Vector3.Dot(body.linearVelocity, body.rotation * Vector3.forward);
            if (Finished || Time.time < startAt)
                return new DrivingInput { Brake = forwardSpeed > 1f ? 1f : 0f, Handbrake = forwardSpeed <= 1f };

            Vector3 position = body.position;
            // Advance past waypoints we have reached (or passed: the next one is closer to the one after it).
            while (next < points.Count && Flat(points[next] - position).magnitude < Mathf.Max(3f, forwardSpeed * 0.35f)) next++;
            if (next >= points.Count)
            {
                Finished = true;
                return new DrivingInput { Brake = 1f };
            }
            float speed = Mathf.Max(0f, forwardSpeed);
            float lookAhead = Mathf.Clamp(3f + 0.35f * speed, 4f, 18f);
            Vector3 target = PointAhead(position, lookAhead);
            Vector3 local = Quaternion.Inverse(body.rotation) * (target - position);
            float alpha = Mathf.Atan2(local.x, Mathf.Max(local.z, 0.1f));
            float steerAngle = Mathf.Atan(2f * vehicle.Wheelbase * Mathf.Sin(alpha) / lookAhead) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(steerAngle / Mathf.Max(1f, vehicle.Steering.MaxAngleAt(speed)), -1f, 1f);

            // Speed: the waypoint target, but brake early enough for any slower waypoint ahead.
            float targetSpeed = speeds[next];
            float distance = Flat(points[next] - position).magnitude;
            for (int i = next; i < points.Count; i++)
            {
                if (i > next) distance += Flat(points[i] - points[i - 1]).magnitude;
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(speeds[i] * speeds[i] + 2f * 5f * distance));
            }
            float error = targetSpeed - forwardSpeed;
            return new DrivingInput
            {
                Steer = steer,
                Throttle = error > 0f ? Mathf.Clamp01(error / 3f) : 0f,
                Brake = error < -0.5f ? Mathf.Clamp01(-error / 2f) : 0f
            };
        }

        private Vector3 PointAhead(Vector3 position, float distance)
        {
            Vector3 from = position;
            for (int i = next; i < points.Count; i++)
            {
                float d = Flat(points[i] - from).magnitude;
                if (d >= distance) return from + Flat(points[i] - from).normalized * distance;
                distance -= d;
                from = points[i];
            }
            return points[^1];
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
