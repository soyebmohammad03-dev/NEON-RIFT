using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Suspension, wheel and tyre set-up for one axle. Ride height comes from the model's wheel positions.</summary>
    [Serializable]
    public struct AxleSettings
    {
        [Header("Suspension")]
        [Tooltip("Natural frequency of the corner on its spring, Hz. Sets stiffness independent of mass (road 1.2–1.8, track 2–3).")]
        [Range(0.5f, 5f)] public float springFrequency;
        [Tooltip("Damping ratio in compression (fraction of critical damping).")]
        [Range(0.05f, 1.5f)] public float bumpDamping;
        [Tooltip("Damping ratio in extension (fraction of critical damping). Usually higher than bump.")]
        [Range(0.05f, 1.5f)] public float reboundDamping;
        [Tooltip("Shaft speed where the damper's valving blows off, m/s. Above it damping drops to the high-speed share, so sharp bumps are absorbed instead of slamming the body.")]
        [Range(0.02f, 1f)] public float damperKneeSpeed;
        [Tooltip("Damping above the knee speed as a share of the low-speed damping (digressive valving).")]
        [Range(0.1f, 1f)] public float highSpeedDamping;
        [Tooltip("Wheel travel available upward from ride height, m.")]
        [Range(0.01f, 0.4f)] public float bumpTravel;
        [Tooltip("Wheel travel available downward from ride height, m.")]
        [Range(0.01f, 0.4f)] public float droopTravel;
        [Tooltip("Bump-stop stiffness as a multiple of the spring rate; engages in the last 25% of bump travel.")]
        [Range(1f, 50f)] public float bumpStopScale;
        [Tooltip("Anti-roll bar rate at the wheel, N/m of left/right compression difference.")]
        [Min(0f)] public float antiRollStiffness;

        [Header("Wheel")]
        [Tooltip("Wheel + tyre + brake disc mass, kg. Sets spin inertia.")]
        [Range(5f, 60f)] public float wheelMass;

        [Header("Tyre")]
        public TyreSettings tyre;

        public static AxleSettings Default => new AxleSettings
        {
            springFrequency = 1.6f,
            bumpDamping = 0.35f,
            reboundDamping = 0.55f,
            damperKneeSpeed = 0.13f,
            highSpeedDamping = 0.35f,
            bumpTravel = 0.08f,
            droopTravel = 0.1f,
            bumpStopScale = 12f,
            antiRollStiffness = 20000f,
            wheelMass = 20f,
            tyre = TyreSettings.Default
        };
    }
}
