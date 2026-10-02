using System;
using UnityEngine;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>
    /// Offline engine sound synthesis. A combustion engine is modelled as per-cylinder exhaust pulses at the real
    /// firing angles, routed to two exhaust banks, each shaped by pipe resonances, a pipe comb, a muffler low-pass and
    /// saturation. An electric drive is modelled as motor/gear whine orders plus inverter noise. Every loop contains
    /// a whole number of engine cycles and is filtered to steady state, so it loops without a seam.
    /// </summary>
    public static class EngineSynth
    {
        [Serializable]
        public struct Resonance
        {
            public float hz, q, gain;
            public Resonance(float hz, float q, float gain) { this.hz = hz; this.q = q; this.gain = gain; }
        }

        /// <summary>Recipe for one engine's character.</summary>
        public sealed class Voice
        {
            public string Name;
            public bool Electric;
            // Combustion
            public int Cylinders = 8;
            /// <summary>Firing angles in crank degrees over the 720° cycle, in firing order.</summary>
            public float[] FireAngles;
            /// <summary>Exhaust bank (0/1) of each firing event, same order as <see cref="FireAngles"/>.</summary>
            public int[] Banks;
            public float PulseAttackMs = 0.25f, PulseDecayMs = 2f;
            public Resonance[] Resonances;
            /// <summary>Right-bank resonance/pipe scale (different pipe length gives the cross-plane burble).</summary>
            public float BankDetune = 1.06f;
            public float PipeDelayMs = 7f, PipeFeedback = 0.3f;
            public float MufflerOnHz = 2500f, MufflerOffHz = 1300f;
            public float NoiseOn = 0.35f, NoiseOff = 0.2f;
            public float PulseOff = 0.4f;
            public float IrregularityOn = 0.08f, IrregularityOff = 0.25f;
            public float DriveOn = 1.6f, DriveOff = 1.1f;
            public float IntakeOn = 0.12f, IntakeOff = 0.03f;
            public float IntakeHz = 900f;
            // Electric
            public float[] WhineOrders, WhineOn, WhineOff;
            public float InverterHz = 2600f, InverterOn = 0.05f, InverterOff = 0.02f;
            public float RumbleOn = 0.25f, RumbleOff = 0.15f;
            public float LoopSeconds = 1.6f;
        }

        /// <summary>Renders one seamless loop. <paramref name="actualRpm"/> is the exact rpm the loop represents.</summary>
        public static float[] Render(Voice v, float rpm, bool onLoad, out float actualRpm)
        {
            var rng = new System.Random((StableHash(v.Name) * 31 + Mathf.RoundToInt(rpm)) * 2 + (onLoad ? 1 : 0));
            return v.Electric ? RenderElectric(v, rpm, onLoad, rng, out actualRpm) : RenderCombustion(v, rpm, onLoad, rng, out actualRpm);
        }

        /// <summary>Deterministic string hash (string.GetHashCode is not guaranteed stable across runtimes).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        private static float[] RenderCombustion(Voice v, float rpm, bool onLoad, System.Random rng, out float actualRpm)
        {
            const int fs = Dsp.SampleRate;
            double cycle = 120.0 / rpm;                                        // four-stroke: one cycle = two revolutions
            int cycles = Mathf.Max(6, Mathf.RoundToInt((float)(v.LoopSeconds / cycle)));
            int n = Mathf.RoundToInt((float)(cycles * cycle * fs));
            double cycleSamples = (double)n / cycles;
            actualRpm = (float)(120.0 * fs / cycleSamples);

            var banks = new[] { new float[n], new float[n] };
            float attack = Mathf.Max(0.05f, v.PulseAttackMs) * 0.001f * fs;
            float decay = v.PulseDecayMs * 0.001f * fs;
            int length = Mathf.CeilToInt(decay * 7f);
            float level = onLoad ? 1f : v.PulseOff;
            float irregular = onLoad ? v.IrregularityOn : v.IrregularityOff;
            float noise = onLoad ? v.NoiseOn : v.NoiseOff;

            for (int k = 0; k < cycles; k++)
            {
                for (int e = 0; e < v.FireAngles.Length; e++)
                {
                    float amp = level * Mathf.Max(0.1f, 1f + irregular * Dsp.Gaussian(rng));
                    double t0 = (k + v.FireAngles[e] / 720.0) * cycleSamples;
                    float frac = (float)(t0 - Math.Floor(t0));
                    int start = (int)Math.Floor(t0);
                    var bank = banks[v.Banks[e]];
                    for (int j = 0; j < length; j++)
                    {
                        float t = j - frac;
                        if (t < 0f) continue;
                        float env = (1f - Mathf.Exp(-t / attack)) * Mathf.Exp(-t / decay);
                        float turbulence = noise * (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t / (decay * 1.6f));
                        bank[(start + j) % n] += amp * (env + turbulence);
                    }
                }
            }

            var mix = new float[n];
            for (int b = 0; b < 2; b++)
            {
                float scale = b == 0 ? 1f : v.BankDetune;
                var shaped = ShapeBank(banks[b], v, scale, onLoad);
                for (int i = 0; i < n; i++) mix[i] += shaped[i];
            }

            // Intake roar: band-limited noise modulated by the firing envelope, mostly under load.
            float intake = onLoad ? v.IntakeOn : v.IntakeOff;
            if (intake > 0f)
            {
                var env = new float[n];
                for (int i = 0; i < n; i++) env[i] = Mathf.Abs(banks[0][i]) + Mathf.Abs(banks[1][i]);
                var lp = Dsp.Biquad.LowPass(40f);
                env = Dsp.Periodic(env, lp.Process);
                var bp = Dsp.Biquad.BandPass(v.IntakeHz, 0.8f);
                var hiss = Dsp.Periodic(Dsp.Noise(n, rng), bp.Process);
                float envRms = Mathf.Max(1e-6f, Dsp.Rms(env)), mixRms = Mathf.Max(1e-6f, Dsp.Rms(mix)), hissRms = Mathf.Max(1e-6f, Dsp.Rms(hiss));
                for (int i = 0; i < n; i++) mix[i] += intake * mixRms / hissRms * hiss[i] * (0.5f + 0.5f * env[i] / envRms);
            }

            Dsp.NormalizeRms(mix, 0.2f);
            return mix;
        }

        private static float[] ShapeBank(float[] excitation, Voice v, float scale, bool onLoad)
        {
            int n = excitation.Length;
            var hp = Dsp.Biquad.HighPass(28f);
            var x = Dsp.Periodic(excitation, hp.Process);

            // Pipe resonances (parallel band-passes plus some direct sound).
            var y = new float[n];
            for (int i = 0; i < n; i++) y[i] = 0.25f * x[i];
            foreach (var r in v.Resonances)
            {
                var bp = Dsp.Biquad.BandPass(r.hz * scale, r.q);
                var band = Dsp.Periodic(x, bp.Process);
                for (int i = 0; i < n; i++) y[i] += r.gain * band[i];
            }

            int delay = Mathf.Max(1, Mathf.RoundToInt(v.PipeDelayMs * scale * 0.001f * Dsp.SampleRate));
            y = Dsp.PeriodicComb(y, delay, v.PipeFeedback);

            float cutoff = onLoad ? v.MufflerOnHz : v.MufflerOffHz;
            var lp1 = Dsp.Biquad.LowPass(cutoff);
            var lp2 = Dsp.Biquad.LowPass(cutoff * 1.4f);
            y = Dsp.Periodic(y, s => lp2.Process(lp1.Process(s)));

            float drive = onLoad ? v.DriveOn : v.DriveOff;
            float rms = Mathf.Max(1e-6f, Dsp.Rms(y));
            float norm = (float)Math.Tanh(drive);
            for (int i = 0; i < n; i++) y[i] = (float)Math.Tanh(drive * y[i] / (rms * 2.5f)) / norm;
            return y;
        }

        private static float[] RenderElectric(Voice v, float rpm, bool onLoad, System.Random rng, out float actualRpm)
        {
            const int fs = Dsp.SampleRate;
            double rev = 60.0 / rpm;
            int revs = Mathf.Max(8, Mathf.RoundToInt((float)(v.LoopSeconds / rev)));
            int n = Mathf.RoundToInt((float)(revs * rev * fs));
            actualRpm = (float)(60.0 * fs * revs / n);
            var x = new float[n];
            var levels = onLoad ? v.WhineOn : v.WhineOff;
            for (int o = 0; o < v.WhineOrders.Length; o++)
            {
                // Whole number of periods per loop: order × revolutions.
                double cyclesInLoop = Math.Round(v.WhineOrders[o] * revs);
                double phase = rng.NextDouble() * Math.PI * 2;
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / n;
                    // Slight periodic wobble so the whine is alive rather than a test tone.
                    double wobble = 1 + 0.15 * Math.Sin(2 * Math.PI * 3 * t + o);
                    x[i] += (float)(levels[o] * wobble * Math.Sin(2 * Math.PI * cyclesInLoop * t + phase));
                }
            }
            float whineRms = Mathf.Max(1e-6f, Dsp.Rms(x));

            var inverterBand = Dsp.Biquad.BandPass(v.InverterHz, 6f);
            var inverter = Dsp.Periodic(Dsp.Noise(n, rng), inverterBand.Process);
            var rumbleBand = Dsp.Biquad.LowPass(180f);
            var rumble = Dsp.Periodic(Dsp.Noise(n, rng), rumbleBand.Process);
            float invRms = Mathf.Max(1e-6f, Dsp.Rms(inverter)), rumbleRms = Mathf.Max(1e-6f, Dsp.Rms(rumble));
            float invLevel = onLoad ? v.InverterOn : v.InverterOff, rumbleLevel = onLoad ? v.RumbleOn : v.RumbleOff;
            for (int i = 0; i < n; i++)
                x[i] += whineRms * (invLevel * inverter[i] / invRms + rumbleLevel * rumble[i] / rumbleRms);
            Dsp.NormalizeRms(x, 0.18f);
            return x;
        }
    }
}
