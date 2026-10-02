using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>One seamless engine loop recorded (or synthesised) at a fixed rpm and load.</summary>
    [Serializable]
    public struct EngineSoundLayer
    {
        public AudioClip clip;
        [Tooltip("Engine speed the loop was recorded at, rpm. Playback pitch = current rpm / this.")]
        [Min(1f)] public float recordedRpm;
        [Tooltip("True for an on-throttle (driven) recording, false for overrun / coasting.")]
        public bool onLoad;
        [Tooltip("Per-layer trim, linear.")]
        [Range(0f, 2f)] public float volume;
    }
}
