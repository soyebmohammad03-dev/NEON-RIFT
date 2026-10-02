using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Tyre, road, wind, suspension and impact sounds.</summary>
    [Serializable]
    public struct ChassisAudioSettings
    {
        [Header("Tyres")]
        [Tooltip("Loop for tyres sliding on grippy tarmac.")]
        public AudioClip skidLoop;
        [Range(0f, 2f)] public float skidVolume;
        [Tooltip("Combined slip (1 = tyre at peak grip) where squeal starts and where it is full.")]
        public Vector2 skidSlipRange;
        [Tooltip("Loop for tyres working near the limit (heavy braking, scrubbing understeer).")]
        public AudioClip scrubLoop;
        [Range(0f, 2f)] public float scrubVolume;
        public Vector2 scrubSlipRange;
        [Tooltip("Surfaces with less grip than this count as loose (grass, gravel).")]
        [Range(0f, 1f)] public float looseSurfaceGrip;
        public AudioClip looseSurfaceLoop;
        [Range(0f, 2f)] public float looseSurfaceVolume;

        [Header("Road and wind")]
        public AudioClip roadLoop;
        [Range(0f, 2f)] public float roadVolume;
        public AudioClip windLoop;
        [Range(0f, 2f)] public float windVolume;
        [Tooltip("Speed at which road and wind reach full level, km/h.")]
        [Min(10f)] public float fullSpeedKph;

        [Header("Suspension")]
        public AudioClip[] suspensionThumps;
        [Tooltip("Compression speed that starts a thump, m/s.")]
        [Min(0.1f)] public float thumpSpeed;
        [Range(0f, 2f)] public float thumpVolume;

        [Header("Impacts")]
        public AudioClip[] lightImpacts;
        public AudioClip[] mediumImpacts;
        public AudioClip[] heavyImpacts;
        [Tooltip("Impulse thresholds, N·s: below x is ignored, above y is medium, above z is heavy.")]
        public Vector3 impactThresholds;
        [Range(0f, 2f)] public float impactVolume;
    }
}
