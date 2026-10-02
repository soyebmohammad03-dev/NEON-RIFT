using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Aerodynamic coefficients multiplied by reference area (C·A, m²). Forces scale with speed squared.</summary>
    [Serializable]
    public struct AeroSettings
    {
        [Tooltip("Drag coefficient × frontal area, m². Road sports car ≈ 0.6–0.8.")]
        [Min(0f)] public float dragArea;
        [Tooltip("Downforce coefficient × area acting at the front axle, m².")]
        [Min(0f)] public float frontDownforceArea;
        [Tooltip("Downforce coefficient × area acting at the rear axle, m².")]
        [Min(0f)] public float rearDownforceArea;
        [Tooltip("Air density, kg/m³.")]
        [Min(0.5f)] public float airDensity;

        public float DragForce(float speed) => 0.5f * airDensity * dragArea * speed * speed;

        public static AeroSettings Default => new AeroSettings
        {
            dragArea = 0.7f,
            frontDownforceArea = 0.05f,
            rearDownforceArea = 0.1f,
            airDensity = 1.225f
        };
    }
}
