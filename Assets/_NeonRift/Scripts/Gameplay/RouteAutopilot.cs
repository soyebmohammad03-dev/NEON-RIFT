using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Drives a vehicle along a <see cref="DrivingRoute"/> through the same <see cref="IVehicleInputSource"/> a player
    /// uses: pure-pursuit steering and a braking-distance speed planner. Used for physics validation laps; the
    /// starting point for AI opponents.
    /// </summary>
    public sealed class RouteAutopilot : IVehicleInputSource
    {
        private readonly DrivingRoute route;
        private readonly VehicleController vehicle;
        private int index = -1;

        /// <summary>Multiplies the route's advisory speeds (below 1 leaves a safety margin).</summary>
        public float SpeedScale { get; set; } = 0.95f;
        /// <summary>Deceleration the planner assumes when braking for slower sections, m/s².</summary>
        public float PlannedDeceleration { get; set; } = 6f;
        public int RouteIndex => index;

        public RouteAutopilot(DrivingRoute route, VehicleController vehicle)
        {
            this.route = route;
            this.vehicle = vehicle;
        }

        public DrivingInput ReadInput()
        {
            var body = vehicle.Body;
            Vector3 position = body.position;
            Quaternion rotation = body.rotation;
            float forwardSpeed = Vector3.Dot(body.linearVelocity, rotation * Vector3.forward);
            float speed = Mathf.Max(0f, forwardSpeed);

            index = route.FindClosest(position, index);
            float lookAhead = Mathf.Clamp(2.5f + 0.3f * speed, 4f, 25f);
            Vector3 target = route.PointAt(route.IndexAhead(index, lookAhead));
            Vector3 local = Quaternion.Inverse(rotation) * (target - position);
            float alpha = Mathf.Atan2(local.x, local.z);
            float steerAngle = Mathf.Atan(2f * vehicle.Wheelbase * Mathf.Sin(alpha) / lookAhead) * Mathf.Rad2Deg;
            float limit = Mathf.Max(1f, vehicle.Steering.MaxAngleAt(speed));

            float targetSpeed = route.SpeedAt(index) * SpeedScale;
            float horizon = speed * speed / (2f * PlannedDeceleration) + 15f;
            float travelled = 0f;
            int i = index;
            for (int guard = 0; guard < route.Count && travelled < horizon; guard++)
            {
                int next = route.Wrap(i + 1);
                travelled += Vector3.Distance(route.PointAt(i), route.PointAt(next));
                i = next;
                float allowed = Mathf.Sqrt(Mathf.Pow(route.SpeedAt(i) * SpeedScale, 2f) + 2f * PlannedDeceleration * travelled);
                targetSpeed = Mathf.Min(targetSpeed, allowed);
            }

            float error = targetSpeed - forwardSpeed;
            return new DrivingInput
            {
                Steer = Mathf.Clamp(steerAngle / limit, -1f, 1f),
                Throttle = error > 0f ? Mathf.Clamp01(error / 2.5f) : 0f,
                Brake = error < 0f ? Mathf.Clamp01(-error / 1.2f) : 0f
            };
        }
    }
}
