using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    [Serializable]
    public struct BrakeSettings
    {
        [Tooltip("Total brake torque across all four wheels at full pedal, Nm.")]
        [Min(0f)] public float maxBrakeTorque;
        [Tooltip("Share of brake torque on the front axle.")]
        [Range(0f, 1f)] public float frontBias;
        [Tooltip("Handbrake torque on each rear wheel, Nm. The handbrake also opens the clutch.")]
        [Min(0f)] public float handbrakeTorque;
        [Tooltip("Anti-lock: limits each wheel's brake torque to what its tyre can transmit.")]
        public bool antiLock;
        [Tooltip("Anti-lock target as a fraction of peak tyre force. Below 1 keeps the tyre on the stable side of the peak.")]
        [Range(0.7f, 1f)] public float antiLockTarget;

        public static BrakeSettings Default => new BrakeSettings
        {
            maxBrakeTorque = 7000f,
            frontBias = 0.65f,
            handbrakeTorque = 2000f,
            antiLock = true,
            antiLockTarget = 0.95f
        };
    }
}
