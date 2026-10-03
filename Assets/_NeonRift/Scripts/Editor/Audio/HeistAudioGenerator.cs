using System;
using System.Text;
using UnityEngine;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>
    /// The heist sounds: terminal stage blips, denials, interference, re-sync, lockout and cancel cues; the tension
    /// bed under the extraction; the facility hum, arm servos, data stream, chamber hydraulics, docking clunk and
    /// release; and the "data acquired" / "breach detected" stingers. Synthesised in-house and deterministic (no
    /// samples). Loops use integer cycles per loop length so they never click. Saved by <see cref="MissionAudioGenerator"/>.
    /// </summary>
    public static class HeistAudioGenerator
    {
        private const float Tau = Mathf.PI * 2f;

        public struct Clips
        {
            public AudioClip StepStart, StepDone, Miss, Interference, Resync, Lockout, Cancel;
            public AudioClip Tension, Hum, Servo, Stream, ChamberOpen, Dock, Release, Acquired, Breach;
            public AudioClip GateUnlock, GateLatch;
        }

        public static Clips Generate(Func<string, float[], bool, StringBuilder, AudioClip> save, StringBuilder log)
        {
            return new Clips
            {
                StepStart = save("Heist_StepStart", Chirp(1300f, 2100f, 0.06f, 0.35f), false, log),
                StepDone = save("Heist_StepDone", TwoTone(1568f, 2093f, 0.055f), false, log),
                Miss = save("Heist_Denied", Denied(), false, log),
                Interference = save("Heist_Interference", Interference(), false, log),
                Resync = save("Heist_Resync", Chirp(700f, 2600f, 0.22f, 0.5f), false, log),
                Lockout = save("Heist_Lockout", Lockout(), false, log),
                Cancel = save("Heist_Cancel", PowerDown(), false, log),
                Tension = save("Heist_Tension_Loop", Tension(), true, log),
                Hum = save("Heist_FacilityHum_Loop", Hum(), true, log),
                Servo = save("Heist_Servo_Loop", Servo(), true, log),
                Stream = save("Heist_DataStream_Loop", Stream(), true, log),
                ChamberOpen = save("Heist_ChamberOpen", ChamberOpen(), false, log),
                Dock = save("Heist_Dock", Clunk(0.9f, 140f), false, log),
                Release = save("Heist_Release", Release(), false, log),
                Acquired = save("Heist_DataAcquired", Acquired(), false, log),
                Breach = save("Heist_Breach", Breach(), false, log),
                GateUnlock = save("Gate_LockRelease", LockRelease(), false, log),
                GateLatch = save("Gate_Latch", Clunk(0.5f, 220f), false, log),
            };
        }

        private static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Dsp.SampleRate)];

        // ---------------- Terminal cues ----------------

        /// <summary>A short swept sine blip (terminal key / stage start / re-sync).</summary>
        private static float[] Chirp(float from, float to, float length, float level)
        {
            var x = Buffer(length + 0.08f);
            double phase = 0;
            int n = Mathf.RoundToInt(length * Dsp.SampleRate);
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                phase += Mathf.Lerp(from, to, u * u) / Dsp.SampleRate;
                float env = Mathf.Clamp01(u / 0.05f) * (1f - u);
                x[i] = env * (Mathf.Sin(Tau * (float)phase) + 0.25f * Mathf.Sign(Mathf.Sin(Tau * (float)phase)));
            }
            Dsp.AddRing(x, length * 0.6f, to * 1.5f, 0.03f, 0.2f);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, level);
            return x;
        }

        private static float[] TwoTone(float a, float b, float spacing)
        {
            var x = Buffer(spacing * 2f + 0.4f);
            Dsp.AddRing(x, 0f, a, 0.05f, 0.5f);
            Dsp.AddRing(x, spacing, b, 0.08f, 0.5f);
            Dsp.AddRing(x, spacing, b * 2.002f, 0.03f, 0.12f);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.5f);
            return x;
        }

        /// <summary>Access denied: a low, buzzy double square pulse.</summary>
        private static float[] Denied()
        {
            var x = Buffer(0.42f);
            for (int k = 0; k < 2; k++)
            {
                int s0 = Mathf.RoundToInt(k * 0.17f * Dsp.SampleRate), len = Mathf.RoundToInt(0.13f * Dsp.SampleRate);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Dsp.SampleRate;
                    float env = Mathf.Clamp01(t / 0.004f) * Mathf.Clamp01((0.13f - t) / 0.01f);
                    float hz = k == 0 ? 196f : 165f;
                    x[s0 + i] += env * (Mathf.Sign(Mathf.Sin(Tau * hz * t)) * 0.6f + 0.4f * Mathf.Sin(Tau * hz * 3.01f * t));
                }
            }
            var lp = Dsp.Biquad.LowPass(2400f);
            for (int i = 0; i < x.Length; i++) x[i] = lp.Process(x[i]);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.7f);
            return x;
        }

        /// <summary>Interference: crushed noise and stuttering tones, 0.7 s.</summary>
        private static float[] Interference()
        {
            var rng = new System.Random(4242);
            var x = Buffer(0.7f);
            float held = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                // Sample-and-hold noise (bit-crushed texture) gated in a stutter.
                if (i % 24 == 0) held = (float)(rng.NextDouble() * 2 - 1);
                bool gate = ((int)(t * 38f) % 3) != 1;
                float env = Mathf.Clamp01(t / 0.005f) * Mathf.Exp(-t / 0.35f);
                float tone = Mathf.Sign(Mathf.Sin(Tau * (900f + 600f * Mathf.Sin(Tau * 23f * t)) * t));
                x[i] = env * (gate ? 1f : 0.15f) * (0.7f * held + 0.3f * tone);
            }
            var bp = Dsp.Biquad.BandPass(1800f, 0.7f);
            for (int i = 0; i < x.Length; i++) x[i] = bp.Process(x[i]) * 1.5f + x[i] * 0.3f;
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.8f);
            return x;
        }

        private static float[] Lockout()
        {
            var x = Buffer(1.1f);
            float[] notes = { 880f, 660f, 440f };
            for (int k = 0; k < notes.Length; k++)
            {
                int s0 = Mathf.RoundToInt(k * 0.22f * Dsp.SampleRate), len = Mathf.RoundToInt(0.18f * Dsp.SampleRate);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Dsp.SampleRate;
                    float env = Mathf.Clamp01(t / 0.003f) * Mathf.Clamp01((0.18f - t) / 0.02f);
                    x[s0 + i] += env * Mathf.Sign(Mathf.Sin(Tau * notes[k] * t)) * 0.5f;
                }
            }
            var lp = Dsp.Biquad.LowPass(3000f);
            for (int i = 0; i < x.Length; i++) x[i] = lp.Process(x[i]);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.75f);
            return x;
        }

        private static float[] PowerDown()
        {
            var x = Buffer(0.6f);
            double phase = 0;
            for (int i = 0; i < x.Length; i++)
            {
                float u = i / (float)x.Length;
                phase += Mathf.Lerp(900f, 90f, Mathf.Sqrt(u)) / Dsp.SampleRate;
                x[i] = (1f - u) * Mathf.Sin(Tau * (float)phase) * Mathf.Clamp01(u / 0.01f);
            }
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.5f);
            return x;
        }

        // ---------------- Loops ----------------

        /// <summary>
        /// Extraction tension bed, 8 s: a 120 bpm sub pulse, a detuned minor pad whose filter breathes once per loop,
        /// and 16th ticks. Runtime volume and pitch follow progress, so it builds without the loop changing.
        /// </summary>
        private static float[] Tension()
        {
            const float seconds = 8f;
            int n = Mathf.RoundToInt(seconds * Dsp.SampleRate);
            var rng = new System.Random(808);
            var x = new float[n];
            // Pad: A2 minor (110, 130.8 ≈ 131, 165), integer cycles per 8 s via 0.125 Hz multiples.
            float[] pad = { 110f, 131f, 165f, 220f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float s = 0f;
                foreach (var hz in pad)
                    for (int d = -1; d <= 1; d += 2)
                    {
                        float f = hz + d * 0.25f;   // ±0.25 Hz keeps whole cycles in 8 s
                        float p = (f * t) % 1f;
                        s += (2f * p - 1f) * 0.12f;
                    }
                x[i] = s;
            }
            // Breathing low-pass: crude one-pole with a periodic cutoff, run twice for steady state.
            float y = 0f;
            var padOut = new float[n];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Dsp.SampleRate;
                    float cutoff = 380f + 900f * (0.5f - 0.5f * Mathf.Cos(Tau * t / seconds));
                    float a = 1f - Mathf.Exp(-Tau * cutoff / Dsp.SampleRate);
                    y += a * (x[i] - y);
                    if (pass == 1) padOut[i] = y;
                }
            var outBuf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float beat = (t * 2f) % 1f;                   // 120 bpm
                float sub = Mathf.Sin(Tau * 55f * t) * Mathf.Exp(-beat / 0.12f) * 0.9f;
                float tickT = (t * 8f) % 1f / 8f;             // 16ths at 120 bpm
                float tick = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-tickT / 0.006f) * (((int)(t * 8f)) % 4 == 2 ? 0.25f : 0.1f);
                outBuf[i] = padOut[i] * 0.9f + sub + tick;
            }
            var hp = Dsp.Biquad.HighPass(30f);
            outBuf = Dsp.Periodic(outBuf, hp.Process);
            Dsp.NormalizeRms(outBuf, 0.18f);
            return outBuf;
        }

        /// <summary>Facility hum: transformer fundamental and harmonics, a resonant whine and air. 4 s.</summary>
        private static float[] Hum()
        {
            const float seconds = 4f;
            int n = Mathf.RoundToInt(seconds * Dsp.SampleRate);
            var rng = new System.Random(31);
            var air = Dsp.Periodic(Dsp.Noise(n, rng), Dsp.Biquad.BandPass(500f, 0.5f).Process);
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float swell = 0.8f + 0.2f * Mathf.Sin(Tau * t / seconds);
                x[i] = swell * (0.5f * Mathf.Sin(Tau * 50f * t) + 0.35f * Mathf.Sin(Tau * 100f * t) + 0.2f * Mathf.Sin(Tau * 150f * t)
                                + 0.06f * Mathf.Sin(Tau * 412f * t) * (0.6f + 0.4f * Mathf.Sin(Tau * 0.5f * t)))
                       + 0.4f * air[i];
            }
            Dsp.NormalizeRms(x, 0.2f);
            return x;
        }

        /// <summary>Arm servo: motor whine with a gear rattle, 2 s.</summary>
        private static float[] Servo()
        {
            int n = Dsp.SampleRate * 2;
            var rng = new System.Random(5150);
            var rattle = Dsp.Periodic(Dsp.Noise(n, rng), Dsp.Biquad.BandPass(2800f, 3f).Process);
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float whine = Mathf.Sin(Tau * 880f * t + 2f * Mathf.Sin(Tau * 6f * t)) * 0.5f + Mathf.Sin(Tau * 1760f * t) * 0.15f;
                float teeth = Mathf.Pow(Mathf.Abs(Mathf.Sin(Tau * 40f * t)), 12f);
                x[i] = whine * 0.6f + rattle[i] * teeth * 2.5f + 0.2f * Mathf.Sin(Tau * 110f * t);
            }
            Dsp.NormalizeRms(x, 0.2f);
            return x;
        }

        /// <summary>Data stream: dense granular blips over a filtered noise wash, 2 s.</summary>
        private static float[] Stream()
        {
            int n = Dsp.SampleRate * 2;
            var rng = new System.Random(9001);
            var x = new float[n];
            for (int g = 0; g < 96; g++)
            {
                int s0 = rng.Next(n);
                float hz = 1500f + (float)rng.NextDouble() * 3500f;
                int len = Mathf.RoundToInt((0.008f + (float)rng.NextDouble() * 0.02f) * Dsp.SampleRate);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Dsp.SampleRate;
                    int k = (s0 + i) % n;   // wrap so the loop is seamless
                    x[k] += Mathf.Sin(Tau * hz * t) * Mathf.Sin(Mathf.PI * i / len) * 0.4f;
                }
            }
            var wash = Dsp.Periodic(Dsp.Noise(n, rng), Dsp.Biquad.BandPass(3200f, 1.2f).Process);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                x[i] += wash[i] * 0.25f * (0.6f + 0.4f * Mathf.Sin(Tau * 2f * t));
            }
            Dsp.NormalizeRms(x, 0.16f);
            return x;
        }

        // ---------------- Machinery one-shots ----------------

        /// <summary>Chamber opening: latch clunks, then a long hydraulic hiss and a low rumble.</summary>
        private static float[] ChamberOpen()
        {
            var rng = new System.Random(77);
            var x = Buffer(4.2f);
            for (int k = 0; k < 6; k++)
            {
                float at = k * 0.18f;
                Dsp.AddNoiseBurst(x, at, 0.04f, 0.6f, Dsp.Biquad.LowPass(1200f), rng);
                Dsp.AddRing(x, at, 95f + k * 7f, 0.12f, 0.5f);
                Dsp.AddRing(x, at + 0.002f, 640f, 0.05f, 0.12f);
            }
            var hiss = Dsp.Biquad.HighPass(1800f);
            for (int i = Mathf.RoundToInt(0.9f * Dsp.SampleRate); i < x.Length; i++)
            {
                float t = i / (float)Dsp.SampleRate - 0.9f;
                float env = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((3.1f - t) / 1.2f);
                x[i] += hiss.Process((float)(rng.NextDouble() * 2 - 1)) * env * 0.35f;
                x[i] += Mathf.Sin(Tau * 42f * t) * env * 0.35f;
            }
            Dsp.AddRing(x, 3.6f, 70f, 0.25f, 0.7f);
            Dsp.AddNoiseBurst(x, 3.6f, 0.06f, 0.5f, Dsp.Biquad.LowPass(900f), rng);
            Dsp.FadeEdges(x, 1f, 80f);
            Dsp.NormalizePeak(x, 0.9f);
            return x;
        }

        /// <summary>A heavy metal clunk with a latch click (docking, gate latch).</summary>
        private static float[] Clunk(float level, float body)
        {
            var rng = new System.Random(13);
            var x = Buffer(0.9f);
            Dsp.AddNoiseBurst(x, 0f, 0.03f, 0.8f, Dsp.Biquad.LowPass(1500f), rng);
            Dsp.AddRing(x, 0f, body, 0.15f, 0.8f);
            Dsp.AddRing(x, 0f, body * 2.7f, 0.06f, 0.3f);
            Dsp.AddRing(x, 0.06f, 2400f, 0.01f, 0.4f);
            Dsp.AddNoiseBurst(x, 0.06f, 0.008f, 0.4f, Dsp.Biquad.HighPass(3000f), rng);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, level);
            return x;
        }

        private static float[] Release()
        {
            var rng = new System.Random(21);
            var x = Buffer(1.6f);
            var hp = Dsp.Biquad.HighPass(2500f);
            for (int i = 0; i < x.Length; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                x[i] = hp.Process((float)(rng.NextDouble() * 2 - 1)) * Mathf.Exp(-t / 0.35f) * 0.6f;
            }
            Dsp.AddRing(x, 0.05f, 180f, 0.12f, 0.6f);
            Dsp.AddRing(x, 0.05f, 1900f, 0.02f, 0.2f);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.75f);
            return x;
        }

        /// <summary>Gate locks releasing: a ratchet of bolts drawing back, then a pneumatic sigh.</summary>
        private static float[] LockRelease()
        {
            var rng = new System.Random(64);
            var x = Buffer(1.4f);
            for (int k = 0; k < 4; k++)
            {
                float at = 0.05f + k * 0.11f;
                Dsp.AddNoiseBurst(x, at, 0.02f, 0.7f, Dsp.Biquad.BandPass(1400f, 1.5f), rng);
                Dsp.AddRing(x, at, 260f - k * 20f, 0.06f, 0.5f);
            }
            var hp = Dsp.Biquad.HighPass(1600f);
            for (int i = Mathf.RoundToInt(0.55f * Dsp.SampleRate); i < x.Length; i++)
            {
                float t = i / (float)Dsp.SampleRate - 0.55f;
                x[i] += hp.Process((float)(rng.NextDouble() * 2 - 1)) * Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t / 0.3f) * 0.4f;
            }
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.85f);
            return x;
        }

        // ---------------- Stingers ----------------

        /// <summary>Data acquired: a rising whoosh into a bright, slightly uneasy major-add9 stab.</summary>
        private static float[] Acquired()
        {
            var rng = new System.Random(2024);
            var x = Buffer(3f);
            var bp = Dsp.Biquad.BandPass(2000f, 0.8f);
            int rise = Mathf.RoundToInt(0.6f * Dsp.SampleRate);
            for (int i = 0; i < rise; i++)
            {
                float u = i / (float)rise;
                x[i] = bp.Process((float)(rng.NextDouble() * 2 - 1)) * u * u * 0.8f;
            }
            float[] chord = { 293.7f, 370f, 440f, 659.3f, 880f };
            for (int k = 0; k < chord.Length; k++)
            {
                Dsp.AddRing(x, 0.6f, chord[k], 0.9f, 0.35f);
                Dsp.AddRing(x, 0.6f, chord[k] * 2.003f, 0.3f, 0.08f);
            }
            Dsp.AddRing(x, 0.6f, 73.4f, 0.6f, 0.8f);
            Dsp.FadeEdges(x, 1f, 120f);
            Dsp.NormalizePeak(x, 0.85f);
            return x;
        }

        /// <summary>Breach detected: a distorted two-tone alarm hit over a sub boom.</summary>
        private static float[] Breach()
        {
            var x = Buffer(2.6f);
            for (int i = 0; i < x.Length; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float hz = ((int)(t * 6f) % 2 == 0) ? 740f : 554f;
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-t / 1.1f);
                float s = Mathf.Sin(Tau * hz * t) + 0.5f * Mathf.Sin(Tau * hz * 2f * t);
                x[i] = (float)Math.Tanh(2.2f * s) * env * 0.45f;
                x[i] += Mathf.Sin(Tau * (48f - 18f * Mathf.Clamp01(t)) * t) * Mathf.Exp(-t / 0.7f) * Mathf.Clamp01(t / 0.004f);
            }
            var lp = Dsp.Biquad.LowPass(4500f);
            for (int i = 0; i < x.Length; i++) x[i] = lp.Process(x[i]);
            Dsp.FadeEdges(x, 1f, 120f);
            Dsp.NormalizePeak(x, 0.95f);
            return x;
        }
    }
}
