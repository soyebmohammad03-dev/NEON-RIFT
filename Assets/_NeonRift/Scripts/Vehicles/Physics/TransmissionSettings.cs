using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Gearbox, final drive and differentials. Shifting is automatic; manual shifting can call the same gearbox.</summary>
    [Serializable]
    public struct TransmissionSettings
    {
        public DriveType driveType;
        [Tooltip("All-wheel drive only: share of drive torque sent to the front axle.")]
        [Range(0f, 1f)] public float frontTorqueShare;
        [Tooltip("Forward ratios, first gear first. One entry = single-speed (electric).")]
        public float[] gearRatios;
        [Tooltip("Reverse ratio (positive number).")]
        [Min(0.1f)] public float reverseRatio;
        [Min(0.1f)] public float finalDrive;
        [Tooltip("Share of engine torque reaching the wheels.")]
        [Range(0.5f, 1f)] public float efficiency;
        [Tooltip("Engine speed that triggers an upshift, rpm.")]
        [Min(0f)] public float shiftUpRpm;
        [Tooltip("Engine speed that triggers a downshift, rpm.")]
        [Min(0f)] public float shiftDownRpm;
        [Tooltip("Time with drive torque cut while changing gear, s.")]
        [Range(0f, 1f)] public float shiftTime;
        [Tooltip("Time for the clutch to re-engage after a shift, s.")]
        [Range(0.01f, 1f)] public float clutchEngageTime;
        [Tooltip("Limited-slip strength on each driven axle: 0 = open, 1 = locked. Fraction of the left/right speed difference removed each 0.01 s.")]
        [Range(0f, 1f)] public float differentialLock;

        public int ForwardGearCount => gearRatios?.Length ?? 0;

        public static TransmissionSettings Default => new TransmissionSettings
        {
            driveType = DriveType.RearWheelDrive,
            frontTorqueShare = 0.4f,
            gearRatios = new[] { 3.2f, 2.2f, 1.6f, 1.25f, 1.0f, 0.82f },
            reverseRatio = 3.0f,
            finalDrive = 3.4f,
            efficiency = 0.88f,
            shiftUpRpm = 6600f,
            shiftDownRpm = 3200f,
            shiftTime = 0.15f,
            clutchEngageTime = 0.1f,
            differentialLock = 0.3f
        };
    }
}
