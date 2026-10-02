using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>A body collision reported by <see cref="VehicleController.Collided"/> (damage, audio, camera shake).</summary>
    public readonly struct VehicleCollision
    {
        /// <summary>Total impulse of the contact, N·s.</summary>
        public readonly float Impulse;
        /// <summary>Closing speed at first contact, m/s.</summary>
        public readonly float RelativeSpeed;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly Collider Other;

        public VehicleCollision(float impulse, float relativeSpeed, Vector3 point, Vector3 normal, Collider other)
        {
            Impulse = impulse;
            RelativeSpeed = relativeSpeed;
            Point = point;
            Normal = normal;
            Other = other;
        }
    }
}
