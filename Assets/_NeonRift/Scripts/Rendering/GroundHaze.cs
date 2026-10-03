using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.Rendering
{
    /// <summary>
    /// Ground-hugging haze (exponential height fog) layered on top of URP's distance fog: the driving foreground stays
    /// clear (<see cref="startDistance"/>), haze thickens with distance close to the ground, and tower tops rise out of it
    /// so the skyline stays readable. Blended like any Volume override, so alert and lockdown profiles can tint it.
    /// Drawn by <see cref="GroundHazeFeature"/>.
    /// </summary>
    [Serializable, VolumeComponentMenu("Neon Rift/Ground Haze")]
    public sealed class GroundHaze : VolumeComponent
    {
        [Tooltip("Extinction at the base height, per metre. 0 disables the effect.")]
        public ClampedFloatParameter density = new(0f, 0f, 0.05f);
        [Tooltip("Height where the haze is densest, m.")]
        public FloatParameter baseHeight = new(0f);
        [Tooltip("Height over which density falls by e, m.")]
        public MinFloatParameter falloff = new(20f, 0.5f);
        [Tooltip("Distance from the camera with no haze at all, m.")]
        public MinFloatParameter startDistance = new(35f, 0f);
        [Tooltip("Haze colour (light pollution scattered in it).")]
        public ColorParameter color = new(new Color(0.12f, 0.09f, 0.075f), hdr: true, showAlpha: false, showEyeDropper: false);
        [Tooltip("Upper limit of haze opacity, so distant silhouettes never vanish.")]
        public ClampedFloatParameter maxOpacity = new(0.8f, 0f, 1f);

        public bool IsActive() => active && density.value > 0f && maxOpacity.value > 0f;
    }
}
