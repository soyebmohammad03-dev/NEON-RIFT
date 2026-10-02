using System;
using UnityEngine;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>Small offline DSP toolkit for the procedural sound generator (editor only).</summary>
    public static class Dsp
    {
        public const int SampleRate = 44100;

        /// <summary>RBJ-cookbook biquad.</summary>
        public sealed class Biquad
        {
            private readonly float b0, b1, b2, a1, a2;
            private float z1, z2;

            private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
            {
                this.b0 = (float)(b0 / a0); this.b1 = (float)(b1 / a0); this.b2 = (float)(b2 / a0);
                this.a1 = (float)(a1 / a0); this.a2 = (float)(a2 / a0);
            }

            public float Process(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }

            private static (double w, double cos, double alpha) Prep(double hz, double q)
            {
                double w = 2 * Math.PI * Math.Min(hz, SampleRate * 0.45) / SampleRate;
                return (w, Math.Cos(w), Math.Sin(w) / (2 * q));
            }

            public static Biquad LowPass(double hz, double q = 0.7071)
            {
                var (_, c, a) = Prep(hz, q);
                return new Biquad((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
            }

            public static Biquad HighPass(double hz, double q = 0.7071)
            {
                var (_, c, a) = Prep(hz, q);
                return new Biquad((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
            }

            /// <summary>Constant 0 dB peak gain band-pass.</summary>
            public static Biquad BandPass(double hz, double q)
            {
                var (_, c, a) = Prep(hz, q);
                return new Biquad(a, 0, -a, 1 + a, -2 * c, 1 - a);
            }
        }

        /// <summary>
        /// Applies a stateful process to a periodic excitation so the result loops seamlessly: the process runs over
        /// the buffer twice and the second (steady-state) pass is kept.
        /// </summary>
        public static float[] Periodic(float[] input, Func<float, float> process)
        {
            int n = input.Length;
            var output = new float[n];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    float y = process(input[i]);
                    if (pass == 1) output[i] = y;
                }
            return output;
        }

        /// <summary>Periodic feedback comb (pipe resonance): y[i] = x[i] + g·y[i−d], run to steady state.</summary>
        public static float[] PeriodicComb(float[] input, int delay, float feedback)
        {
            int n = input.Length;
            var y = new float[n];
            for (int pass = 0; pass < 4; pass++)
                for (int i = 0; i < n; i++)
                    y[i] = input[i] + feedback * y[((i - delay) % n + n) % n];
            return y;
        }

        public static float[] Noise(int n, System.Random rng)
        {
            var x = new float[n];
            for (int i = 0; i < n; i++) x[i] = (float)(rng.NextDouble() * 2 - 1);
            return x;
        }

        public static float Gaussian(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }

        public static float Rms(float[] x)
        {
            double s = 0;
            foreach (var v in x) s += v * v;
            return x.Length > 0 ? (float)Math.Sqrt(s / x.Length) : 0f;
        }

        public static void RemoveDc(float[] x)
        {
            double mean = 0;
            foreach (var v in x) mean += v;
            mean /= Math.Max(1, x.Length);
            for (int i = 0; i < x.Length; i++) x[i] -= (float)mean;
        }

        public static void NormalizeRms(float[] x, float target)
        {
            RemoveDc(x);
            float rms = Rms(x);
            if (rms <= 1e-9f) return;
            float peak = 0f;
            foreach (var v in x) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = Mathf.Min(target / rms, 0.97f / Mathf.Max(peak, 1e-9f));
            for (int i = 0; i < x.Length; i++) x[i] *= gain;
        }

        public static void NormalizePeak(float[] x, float target)
        {
            float peak = 0f;
            foreach (var v in x) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak <= 1e-9f) return;
            for (int i = 0; i < x.Length; i++) x[i] *= target / peak;
        }

        /// <summary>Short fades at both ends of a one-shot so it can never click.</summary>
        public static void FadeEdges(float[] x, float inMs = 1.5f, float outMs = 20f)
        {
            int fi = Mathf.Max(1, Mathf.RoundToInt(inMs * 0.001f * SampleRate));
            int fo = Mathf.Max(1, Mathf.RoundToInt(outMs * 0.001f * SampleRate));
            for (int i = 0; i < fi && i < x.Length; i++) x[i] *= i / (float)fi;
            for (int i = 0; i < fo && i < x.Length; i++) x[x.Length - 1 - i] *= i / (float)fo;
        }

        /// <summary>Adds a decaying sine starting at <paramref name="start"/> seconds.</summary>
        public static void AddRing(float[] x, float start, float hz, float decaySeconds, float level, float phase = 0f)
        {
            int s0 = Mathf.RoundToInt(start * SampleRate);
            int len = Mathf.Min(x.Length - s0, Mathf.RoundToInt(decaySeconds * 7f * SampleRate));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float attack = Mathf.Clamp01(t / 0.0015f);
                x[s0 + i] += level * attack * Mathf.Exp(-t / decaySeconds) * Mathf.Sin(2f * Mathf.PI * hz * t + phase);
            }
        }

        /// <summary>Adds filtered decaying noise starting at <paramref name="start"/> seconds.</summary>
        public static void AddNoiseBurst(float[] x, float start, float decaySeconds, float level, Biquad filter, System.Random rng)
        {
            int s0 = Mathf.RoundToInt(start * SampleRate);
            int len = Mathf.Min(x.Length - s0, Mathf.RoundToInt(decaySeconds * 7f * SampleRate));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float attack = Mathf.Clamp01(t / 0.0008f);
                float v = (float)(rng.NextDouble() * 2 - 1) * level * attack * Mathf.Exp(-t / decaySeconds);
                x[s0 + i] += filter != null ? filter.Process(v) : v;
            }
        }
    }
}
