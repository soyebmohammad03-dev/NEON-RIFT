using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Steering angle = smoothed input × speed-dependent limit. The limit falls with speed toward the angle the
    /// front tyres can actually use (kinematic angle for the grip-limited corner + peak slip angle).
    /// </summary>
    [Serializable]
    public struct SteeringSettings
    {
        [Tooltip("Road-wheel lock at parking speed, degrees.")]
        [Range(10f, 50f)] public float maxSteerAngle;
        [Tooltip("0 = constant lock at all speeds, 1 = lock follows the grip-limited angle at speed.")]
        [Range(0f, 1f)] public float speedSensitivity;
        [Tooltip("Front slip angle allowed at full input at speed, as a multiple of the tyre's peak slip angle.")]
        [Range(0.5f, 2f)] public float slipAngleAllowance;
        [Tooltip("Input travel per second toward the requested steer (1 = centre to full lock in 1 s).")]
        [Range(0.5f, 20f)] public float steerRate;
        [Tooltip("Input travel per second back toward centre or through it.")]
        [Range(0.5f, 20f)] public float returnRate;
        [Tooltip("Steer and return rates are multiplied by this at the high-speed reference.")]
        [Range(0.1f, 1f)] public float highSpeedRateScale;
        [Tooltip("Speed at which the high-speed rate scale applies fully, km/h.")]
        [Min(10f)] public float highSpeedReferenceKph;
        [Tooltip("0 = parallel steer, 1 = full Ackermann geometry.")]
        [Range(0f, 1f)] public float ackermann;

        public static SteeringSettings Default => new SteeringSettings
        {
            maxSteerAngle = 33f,
            speedSensitivity = 1f,
            slipAngleAllowance = 0.9f,
            steerRate = 4f,
            returnRate = 6f,
            highSpeedRateScale = 0.55f,
            highSpeedReferenceKph = 200f,
            ackermann = 0.6f
        };
    }
}
