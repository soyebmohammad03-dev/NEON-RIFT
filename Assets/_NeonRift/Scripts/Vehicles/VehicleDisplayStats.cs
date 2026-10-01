using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Player-facing figures shown in Car Select. Authored for now; once the physics profile exists
    /// these must be checked against measured test-track results.
    /// </summary>
    [Serializable]
    public struct VehicleDisplayStats
    {
        [Min(0f)] public float topSpeedKph;
        [Min(0f)] public float zeroToHundredSeconds;
        [Min(0f)] public float powerHp;
        [Min(0f)] public float massKg;
        [Range(0f, 10f)] public float handlingRating;

        public static VehicleDisplayStats Default => new VehicleDisplayStats
        {
            topSpeedKph = 280f,
            zeroToHundredSeconds = 4f,
            powerHp = 500f,
            massKg = 1500f,
            handlingRating = 5f
        };
    }
}
