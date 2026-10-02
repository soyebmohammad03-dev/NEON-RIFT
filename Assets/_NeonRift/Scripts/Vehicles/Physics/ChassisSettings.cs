using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Mass properties of the sprung body.</summary>
    [Serializable]
    public struct ChassisSettings
    {
        [Tooltip("Total mass including driver and fluids, kg.")]
        [Min(100f)] public float mass;
        [Tooltip("Share of static weight carried by the front axle. Places the centre of mass along the wheelbase.")]
        [Range(0.3f, 0.7f)] public float frontWeightDistribution;
        [Tooltip("Centre-of-mass height above the ground, m.")]
        [Range(0.1f, 1.5f)] public float centreOfMassHeight;
        [Tooltip("Multiplies the box-approximated inertia (x = pitch, y = yaw, z = roll). Below 1 means mass is concentrated toward the centre.")]
        public Vector3 inertiaScale;
        [Tooltip("Rigidbody angular damping. Keep near zero: it is not a handling aid.")]
        [Min(0f)] public float angularDamping;

        public static ChassisSettings Default => new ChassisSettings
        {
            mass = 1500f,
            frontWeightDistribution = 0.5f,
            centreOfMassHeight = 0.45f,
            inertiaScale = new Vector3(0.8f, 0.8f, 0.8f),
            angularDamping = 0.02f
        };
    }
}
