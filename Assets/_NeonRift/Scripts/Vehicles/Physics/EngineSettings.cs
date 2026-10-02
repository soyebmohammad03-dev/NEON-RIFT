using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Engine or traction motor. Torque = peak torque × curve(rpm / max rpm) × throttle.</summary>
    [Serializable]
    public struct EngineSettings
    {
        [Tooltip("Idle speed, rpm. 0 for an electric motor.")]
        [Min(0f)] public float idleRpm;
        [Tooltip("Rev limit, rpm. Fuel is cut softly over the last 1.5%.")]
        [Min(1000f)] public float maxRpm;
        [Tooltip("Engine speed held by the slipping clutch at full throttle from standstill, rpm. 0 = no clutch (electric).")]
        [Min(0f)] public float launchRpm;
        [Tooltip("Peak crank torque, Nm.")]
        [Min(1f)] public float peakTorque;
        [Tooltip("Normalised torque (y, 0..1) against normalised engine speed (x = rpm / max rpm).")]
        public AnimationCurve torqueCurve;
        [Tooltip("Engine-braking torque at max rpm with the throttle closed, Nm (regen for electric). Scales linearly from idle.")]
        [Min(0f)] public float engineBrakeTorque;
        [Tooltip("Rotating inertia of crank and flywheel (or rotor), kg·m². Sets how fast revs rise and fall and how heavy low gears feel.")]
        [Range(0.01f, 1f)] public float inertia;

        public float TorqueAt(float rpm)
        {
            if (torqueCurve == null || torqueCurve.length == 0 || maxRpm <= 0f) return 0f;
            return peakTorque * Mathf.Max(0f, torqueCurve.Evaluate(Mathf.Clamp01(rpm / maxRpm)));
        }

        /// <summary>Peak power in kW and the rpm it occurs at, sampled from the curve.</summary>
        public float PeakPowerKw(out float atRpm)
        {
            float best = 0f;
            atRpm = 0f;
            for (int i = 0; i <= 200; i++)
            {
                float rpm = maxRpm * i / 200f;
                float kw = TorqueAt(rpm) * rpm * VehicleUnits.RpmToRadPerSec / 1000f;
                if (kw > best) { best = kw; atRpm = rpm; }
            }
            return best;
        }

        public static EngineSettings Default => new EngineSettings
        {
            idleRpm = 900f,
            maxRpm = 7000f,
            launchRpm = 3000f,
            peakTorque = 500f,
            torqueCurve = new AnimationCurve(
                new Keyframe(0f, 0.45f), new Keyframe(0.3f, 0.85f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0.82f)),
            engineBrakeTorque = 90f,
            inertia = 0.2f
        };
    }
}
