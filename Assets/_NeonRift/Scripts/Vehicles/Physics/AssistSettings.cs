using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Driver aids. They act through the same physical channels a real car uses (throttle, brake torque).</summary>
    [Serializable]
    public struct AssistSettings
    {
        [Tooltip("Cuts throttle so driven tyres stay below their peak traction.")]
        public bool tractionControl;
        [Tooltip("Traction-control target as a fraction of peak tyre force.")]
        [Range(0.7f, 1.2f)] public float tractionControlTarget;
        [Tooltip("Electronic speed limiter, km/h. 0 = none.")]
        [Min(0f)] public float speedLimiterKph;

        public static AssistSettings Default => new AssistSettings
        {
            tractionControl = true,
            tractionControlTarget = 1f,
            speedLimiterKph = 0f
        };
    }
}
