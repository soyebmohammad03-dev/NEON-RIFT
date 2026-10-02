using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Combined-slip tyre: force rises to a peak at the peak slip ratio / angle, then falls toward the sliding grip.
    /// Grip values are friction coefficients on a reference asphalt surface (grip 1).
    /// </summary>
    [Serializable]
    public struct TyreSettings
    {
        [Tooltip("Peak longitudinal friction coefficient (traction and braking).")]
        [Range(0.3f, 2.5f)] public float longitudinalGrip;
        [Tooltip("Peak lateral friction coefficient (cornering).")]
        [Range(0.3f, 2.5f)] public float lateralGrip;
        [Tooltip("Slip ratio at which longitudinal force peaks (0.06–0.15 typical).")]
        [Range(0.02f, 0.4f)] public float peakSlipRatio;
        [Tooltip("Slip angle at which lateral force peaks, degrees (5–10 typical).")]
        [Range(2f, 20f)] public float peakSlipAngle;
        [Tooltip("Grip left when fully sliding, as a fraction of peak grip.")]
        [Range(0.3f, 1f)] public float slidingGripRatio;
        [Tooltip("How quickly grip falls from peak to sliding once past the peak slip. Higher = more abrupt breakaway.")]
        [Range(0.1f, 3f)] public float breakawaySharpness;
        [Tooltip("Grip lost per unit of extra load relative to the static load. Makes load transfer cost grip (0.05–0.2 typical).")]
        [Range(0f, 0.4f)] public float loadSensitivity;
        [Tooltip("Rolling-resistance coefficient (0.010–0.015 typical).")]
        [Range(0f, 0.05f)] public float rollingResistance;
        [Tooltip("Radial stiffness of the tyre, N/m (road 200–280k, slick 280–350k). Acts in series with the bump stop when the suspension bottoms.")]
        [Range(50000f, 600000f)] public float verticalStiffness;

        public static TyreSettings Default => new TyreSettings
        {
            longitudinalGrip = 1.1f,
            lateralGrip = 1.05f,
            peakSlipRatio = 0.1f,
            peakSlipAngle = 8f,
            slidingGripRatio = 0.8f,
            breakawaySharpness = 0.8f,
            loadSensitivity = 0.1f,
            rollingResistance = 0.013f,
            verticalStiffness = 250000f
        };
    }
}
