using System;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>How an engine's loops are blended from telemetry.</summary>
    [Serializable]
    public struct EngineAudioSettings
    {
        [Tooltip("Loops at several rpm points, with on- and off-load versions. Neighbouring rpm points are crossfaded.")]
        public EngineSoundLayer[] layers;
        [Tooltip("Overall engine level, linear.")]
        [Range(0f, 2f)] public float volume;
        [Tooltip("Level at idle relative to the redline, linear.")]
        [Range(0f, 1f)] public float idleVolume;
        [Tooltip("Level on the overrun (throttle closed) relative to full load, linear.")]
        [Range(0f, 1f)] public float offLoadVolume;
        [Tooltip("Smoothing of the rpm fed to the loops, s. Keeps shifts quick without stepping.")]
        [Range(0.005f, 0.3f)] public float rpmSmoothing;
        [Tooltip("Time for the on-load character to come in when the throttle opens, s.")]
        [Range(0.01f, 1f)] public float loadAttack;
        [Tooltip("Time for the on-load character to fade when the throttle closes, s.")]
        [Range(0.01f, 1f)] public float loadRelease;
        [Tooltip("Share of load kept during a gear change (ignition/torque cut).")]
        [Range(0f, 1f)] public float shiftLoad;
        [Tooltip("Playback pitch limits; layers outside are faded out rather than over-stretched.")]
        [Range(0.25f, 1f)] public float minPitch;
        [Range(1f, 4f)] public float maxPitch;

        [Header("Gear shifts")]
        public AudioClip[] shiftClips;
        [Range(0f, 2f)] public float shiftVolume;

        [Header("Overrun pops (combustion engines)")]
        public AudioClip[] overrunPops;
        [Tooltip("Average pops per second on a closed throttle at high rpm.")]
        [Range(0f, 20f)] public float popRate;
        [Tooltip("No pops below this engine speed, rpm.")]
        [Min(0f)] public float popMinRpm;
        [Range(0f, 2f)] public float popVolume;
    }
}
