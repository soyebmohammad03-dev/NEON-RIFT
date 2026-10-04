using System;
using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>Stems of the adaptive score, all the same length and tempo.</summary>
    public enum ScoreLayer
    {
        Bed,
        Pulse,
        Arp,
        Drive
    }

    /// <summary>
    /// How the adaptive score is mixed from the mission state (pure logic). Calm driving is the pad with a light
    /// groove; heist work brings the arpeggio in with the interaction's tension; an alert pushes the groove forward;
    /// a lockdown hands over to the drive stem. State changes land on the music's half-bar grid.
    /// </summary>
    public static class ScoreMix
    {
        public const int LayerCount = 4;
        /// <summary>Gain change per second while a layer rises / falls (the drive stem slams in faster).</summary>
        public const float RiseRate = 0.8f, FallRate = 0.4f, DriveRiseRate = 4f;

        /// <summary>Target gain 0..1 per <see cref="ScoreLayer"/> for the state, written into <paramref name="into"/>.</summary>
        /// <param name="activity">Interaction tension 0..1 (heist work in progress).</param>
        /// <param name="ended">The mission is over: every layer fades out (an outro takes over).</param>
        public static void Targets(SecurityLevel security, float activity, bool ended, float[] into)
        {
            if (into == null || into.Length < LayerCount) throw new ArgumentException("needs one slot per layer", nameof(into));
            activity = Mathf.Clamp01(activity);
            float bed, pulse, arp, drive;
            if (ended) bed = pulse = arp = drive = 0f;
            else switch (security)
            {
                case SecurityLevel.Lockdown:
                    bed = 0.2f; pulse = 0.55f; arp = 0.25f * activity; drive = 1f;
                    break;
                case SecurityLevel.Alert:
                    bed = 0.55f; pulse = 0.85f; arp = 0.35f + 0.5f * activity; drive = 0f;
                    break;
                default:
                    bed = 0.8f; pulse = 0.45f * (1f - 0.5f * activity); arp = 0.8f * activity; drive = 0f;
                    break;
            }
            into[(int)ScoreLayer.Bed] = bed;
            into[(int)ScoreLayer.Pulse] = pulse;
            into[(int)ScoreLayer.Arp] = arp;
            into[(int)ScoreLayer.Drive] = drive;
        }

        /// <summary>Moves <paramref name="gain"/> toward <paramref name="target"/> at the layer's rate.</summary>
        public static float Step(ScoreLayer layer, float gain, float target, float dt)
        {
            float rate = target > gain ? (layer == ScoreLayer.Drive ? DriveRiseRate : RiseRate) : FallRate;
            return Mathf.MoveTowards(gain, target, rate * dt);
        }

        /// <summary>
        /// The next point on the grid of <paramref name="gridSeconds"/> (e.g. a half bar) after
        /// <paramref name="now"/>, counted from the music's start time. Both in DSP seconds.
        /// </summary>
        public static double NextGridTime(double start, double now, double gridSeconds)
        {
            if (gridSeconds <= 0.0 || now <= start) return Math.Max(start, now);
            double steps = Math.Ceiling((now - start) / gridSeconds - 1e-9);
            return start + steps * gridSeconds;
        }
    }
}
