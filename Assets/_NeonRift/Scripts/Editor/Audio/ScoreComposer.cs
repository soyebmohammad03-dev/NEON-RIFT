using System;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>
    /// The Neon Rift score: original, fully procedural music (no samples, no licensed material). One 8-bar theme in
    /// A minor at about 96 bpm (Am(add9) – Fmaj7 – Dm9 – Esus4 → E, two bars each), rendered as stems of exactly the same
    /// length so they can be layered sample-locked and crossfaded by the game state:
    /// <list type="bullet">
    /// <item>Bed: detuned-saw pad and sub (calm driving, the intro's opening);</item>
    /// <item>Pulse: soft kick, sidechained eighth-note bass, ticking hats (moving, alert);</item>
    /// <item>Arp: sixteenth plucks over the chords with a dotted-eighth echo (heist work, tension);</item>
    /// <item>Drive: four-on-the-floor, claps, rolling sixteenth bass, alarm stabs, tom fills (lockdown);</item>
    /// <item>Outros: success (the theme resolving to A major) and failure (a sinking cluster), one-shots.</item>
    /// </list>
    /// Every note that rings past the loop end is wrapped to the start and every filter runs to steady state, so the
    /// loops are seamless.
    /// </summary>
    public static class ScoreComposer
    {
        public const int Bars = 8;
        private const int Rate = Dsp.SampleRate;
        private const float NominalBpm = 96f;
        /// <summary>Unity's ADPCM pads a clip to whole 128-sample blocks; a loop that is not a multiple would gain a gap.</summary>
        private const int Block = 128;

        /// <summary>Samples in one loop of every stem: 8 bars at ~96 bpm, rounded up to whole ADPCM blocks (~20 s).</summary>
        public static readonly int LoopSamples = (int)Math.Ceiling(Bars * 4 * 60.0 / NominalBpm * Rate / Block) * Block;
        /// <summary>The exact tempo of the loop (95.995 bpm), so the beat grid at runtime matches the audio.</summary>
        public static readonly float Bpm = (float)(Bars * 4 * 60.0 * Rate / LoopSamples);
        private static readonly double Beat = LoopSamples / (double)Rate / (Bars * 4);

        private readonly struct Segment
        {
            public readonly int Bar, Length, Root;
            public readonly int[] Voicing;

            public Segment(int bar, int length, int root, int[] voicing)
            {
                Bar = bar;
                Length = length;
                Root = root;
                Voicing = voicing;
            }
        }

        // MIDI notes. Roots in octave 2; voicings around middle C.
        private static readonly Segment[] Progression =
        {
            new(0, 2, 45, new[] { 57, 60, 64, 71 }),   // Am(add9)
            new(2, 2, 41, new[] { 53, 57, 60, 64 }),   // Fmaj7
            new(4, 2, 38, new[] { 50, 53, 57, 64 }),   // Dm9 (no 7th)
            new(6, 1, 40, new[] { 52, 57, 59, 64 }),   // Esus4
            new(7, 1, 40, new[] { 52, 56, 59, 64 }),   // E (leading tone back to Am)
        };

        private static Segment SegmentAt(int bar)
        {
            bar %= Bars;
            foreach (var s in Progression)
                if (bar >= s.Bar && bar < s.Bar + s.Length) return s;
            return Progression[0];
        }

        private static double Hz(double midi) => 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);
        private static int At(double beats) => (int)Math.Round(beats * Beat * Rate);
        private static int Samples(double seconds) => (int)Math.Round(seconds * Rate);

        // ---------------- Stems ----------------

        public static float[] Bed()
        {
            int n = LoopSamples;
            var x = new float[n];
            var rng = new Random(71);
            foreach (var seg in Progression)
            {
                int start = At(seg.Bar * 4), length = At(seg.Length * 4);
                foreach (int m in seg.Voicing)
                    AddWrapped(x, start, PadTone(Hz(m), length, 1.1, 1.8, rng, 0.16));
                AddWrapped(x, start, SubTone(Hz(seg.Root - 12), length, 0.6, 1.4, 0.32));
            }
            var lp = Dsp.Biquad.LowPass(1250, 0.6);
            var y = Dsp.Periodic(x, v => lp.Process(v));
            // Breath: filtered noise swelling once per bar (periods divide the loop).
            var airLp = Dsp.Biquad.LowPass(2200);
            var air = Dsp.Periodic(Dsp.Noise(n, rng), v => airLp.Process(v));
            double bar = 4 * Beat;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                y[i] += air[i] * (float)(0.018 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * t / (bar * 2))));
            }
            Dsp.RemoveDc(y);
            Dsp.NormalizeRms(y, 0.11f);
            return y;
        }

        public static float[] Pulse()
        {
            int n = LoopSamples;
            var x = new float[n];
            var rng = new Random(17);
            var kicks = new int[Bars * 2];
            for (int bar = 0; bar < Bars; bar++)
            {
                kicks[bar * 2] = At(bar * 4);
                kicks[bar * 2 + 1] = At(bar * 4 + 2);
                AddWrapped(x, kicks[bar * 2], Kick(118, 48, 9, 0.34, 0.75));
                AddWrapped(x, kicks[bar * 2 + 1], Kick(118, 48, 9, 0.34, 0.6));
            }
            // Eighth-note bass on the chord root, offbeats accented, ducked under the kick.
            var bass = new float[n];
            for (int e = 0; e < Bars * 8; e++)
            {
                var seg = SegmentAt(e / 8);
                double f = Hz(seg.Root);
                bool off = e % 2 == 1;
                AddWrapped(bass, At(e * 0.5), BassNote(f, Samples(Beat * 0.5 * 0.92), off ? 0.5 : 0.3, 900));
            }
            Sidechain(bass, kicks, 0.65, 9);
            for (int i = 0; i < n; i++) x[i] += bass[i];
            // Hats: sixteenths, the offbeat eighth strongest.
            var hp = Dsp.Biquad.HighPass(7500);
            var hiss = Dsp.Periodic(Dsp.Noise(n, rng), v => hp.Process(v));
            for (int s = 0; s < Bars * 16; s++)
            {
                double level = s % 4 == 2 ? 0.26 : s % 2 == 1 ? 0.1 : 0.06;
                AddEnveloped(x, hiss, At(s * 0.25), Samples(0.09), 55, level);
            }
            Dsp.RemoveDc(x);
            Dsp.NormalizeRms(x, 0.12f);
            return x;
        }

        public static float[] Arp()
        {
            int n = LoopSamples;
            var x = new float[n];
            int[] pattern = { 0, 2, 1, 3, 2, 4, 3, 5, 4, 6, 5, 7, 6, 4, 3, 1 };
            for (int s = 0; s < Bars * 16; s++)
            {
                var seg = SegmentAt(s / 16);
                int p = pattern[s % 16];
                int m = seg.Voicing[p % 4] + 12 * (p / 4) + 12;
                double accent = s % 4 == 0 ? 1.0 : 0.62;
                AddWrapped(x, At(s * 0.25), Pluck(Hz(m), Samples(0.42), 0.16 * accent));
            }
            // Dotted-eighth echo, circular so the tail wraps into the loop start.
            int d = At(0.75);
            var y = new float[n];
            Array.Copy(x, y, n);
            double gain = 0.42;
            for (int tap = 1; tap <= 5; tap++, gain *= 0.42)
                for (int i = 0; i < n; i++) y[(i + tap * d) % n] += (float)(x[i] * gain);
            var hp = Dsp.Biquad.HighPass(260);
            var z = Dsp.Periodic(y, v => hp.Process(v));
            Dsp.RemoveDc(z);
            Dsp.NormalizeRms(z, 0.085f);
            return z;
        }

        public static float[] Drive()
        {
            int n = LoopSamples;
            var x = new float[n];
            var rng = new Random(29);
            var kicks = new int[Bars * 4];
            for (int b = 0; b < Bars * 4; b++)
            {
                kicks[b] = At(b);
                AddWrapped(x, kicks[b], Kick(150, 44, 8, 0.32, 0.95));
            }
            // Claps on 2 and 4: three quick noise bursts and a short tail, band-passed.
            var bp = Dsp.Biquad.BandPass(1400, 0.9);
            var clapNoise = Dsp.Periodic(Dsp.Noise(n, rng), v => bp.Process(v));
            for (int b = 0; b < Bars * 4; b++)
            {
                if (b % 2 == 0) continue;
                int s0 = At(b);
                for (int k = 0; k < 3; k++) AddEnveloped(x, clapNoise, s0 + Samples(0.011 * k), Samples(0.03), 90, 0.5);
                AddEnveloped(x, clapNoise, s0 + Samples(0.033), Samples(0.25), 16, 0.42);
            }
            // Rolling sixteenth bass with octave jumps, ducked under the kick.
            int[] octave = { 0, 0, 12, 0, 0, 12, 0, 0, 0, 0, 12, 0, 0, 12, 0, 12 };
            var bass = new float[n];
            for (int s = 0; s < Bars * 16; s++)
            {
                var seg = SegmentAt(s / 16);
                AddWrapped(bass, At(s * 0.25), BassNote(Hz(seg.Root + octave[s % 16]), Samples(Beat * 0.25 * 0.85), 0.42, 1400));
            }
            Sidechain(bass, kicks, 0.7, 11);
            for (int i = 0; i < n; i++) x[i] += bass[i];
            // Open hats on the offbeat eighths.
            var hp = Dsp.Biquad.HighPass(6000);
            var hiss = Dsp.Periodic(Dsp.Noise(n, rng), v => hp.Process(v));
            for (int b = 0; b < Bars * 4; b++) AddEnveloped(x, hiss, At(b + 0.5), Samples(0.18), 18, 0.2);
            // Alarm motif: a minor-second stab (A5 → Bb5) at the top of every other bar.
            for (int bar = 1; bar < Bars; bar += 2)
            {
                AddWrapped(x, At(bar * 4), Stab(Hz(81), Samples(Beat * 0.45), 0.12));
                AddWrapped(x, At(bar * 4 + 0.5), Stab(Hz(82), Samples(Beat * 0.45), 0.12));
            }
            // Tom fill on the last beat of bars 4 and 8.
            foreach (int bar in new[] { 3, 7 })
                for (int k = 0; k < 4; k++)
                    AddWrapped(x, At(bar * 4 + 3 + k * 0.25), Kick(230 - k * 30, 110 - k * 15, 14, 0.2, 0.45));
            Dsp.RemoveDc(x);
            Dsp.NormalizeRms(x, 0.14f);
            return x;
        }

        /// <summary>The theme resolving to A major: a swelling chord and a bell arpeggio, 7 s.</summary>
        public static float[] OutroSuccess()
        {
            int n = Samples(7.0);
            var x = new float[n];
            var rng = new Random(3);
            foreach (int m in new[] { 57, 61, 64, 69 })
                AddClipped(x, 0, PadTone(Hz(m), Samples(1.2), 0.08, 5.0, rng, 0.17));
            AddClipped(x, 0, SubTone(Hz(33), Samples(1.2), 0.05, 4.0, 0.35));
            int[] bells = { 81, 85, 88, 93 };
            for (int k = 0; k < bells.Length; k++) AddClipped(x, Samples(0.18 * k), Bell(Hz(bells[k]), Samples(5.0), 0.16));
            var lp = Dsp.Biquad.LowPass(5200);
            for (int i = 0; i < n; i++) x[i] = lp.Process(x[i]);
            Dsp.RemoveDc(x);
            Dsp.NormalizePeak(x, 0.8f);
            Dsp.FadeEdges(x, 2f, 400f);
            return x;
        }

        /// <summary>Failure: a low A/Bb/E cluster sinking a semitone under a noise wash, 6 s.</summary>
        public static float[] OutroFailure()
        {
            int n = Samples(6.0);
            var x = new float[n];
            var rng = new Random(13);
            foreach (int m in new[] { 33, 34, 40, 45 })
            {
                double f0 = Hz(m), phase = rng.NextDouble();
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)Rate;
                    double f = f0 * Math.Pow(2.0, -t / 5.0 / 12.0);
                    phase += f / Rate;
                    double env = Math.Min(1.0, t / 0.05) * Math.Exp(-t / 2.4);
                    x[i] += (float)((2.0 * (phase % 1.0) - 1.0) * env * 0.22);
                }
            }
            var noise = Dsp.Noise(n, rng);
            var wash = Dsp.Biquad.LowPass(700);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                x[i] += wash.Process(noise[i]) * (float)(0.5 * Math.Min(1.0, t / 0.4) * Math.Exp(-t / 2.0));
            }
            var lp = Dsp.Biquad.LowPass(520, 0.9);
            for (int i = 0; i < n; i++) x[i] = lp.Process(x[i]);
            AddClipped(x, 0, Kick(90, 32, 4, 1.4, 0.9));
            Dsp.RemoveDc(x);
            Dsp.NormalizePeak(x, 0.8f);
            Dsp.FadeEdges(x, 2f, 400f);
            return x;
        }

        /// <summary>The intro's groove: pulse with the arp folded in low (one clip, same loop as the stems).</summary>
        public static float[] IntroGroove()
        {
            var pulse = Pulse();
            var arp = Arp();
            var x = new float[pulse.Length];
            for (int i = 0; i < x.Length; i++) x[i] = pulse[i] + arp[i] * 0.55f;
            Dsp.NormalizeRms(x, 0.13f);
            return x;
        }

        // ---------------- Voices ----------------

        /// <summary>Three detuned band-limited saws with a slow attack and a long release.</summary>
        private static float[] PadTone(double hz, int length, double attack, double release, Random rng, double level)
        {
            int total = length + Samples(release);
            var y = new float[total];
            double[] detune = { -0.0047, 0.0, 0.0049 };   // about ±8 cents
            foreach (double dt in detune)
            {
                double f = hz * (1.0 + dt), inc = f / Rate, phase = rng.NextDouble();
                for (int i = 0; i < total; i++)
                {
                    double saw = 2.0 * phase - 1.0 - PolyBlep(phase, inc);
                    phase += inc;
                    if (phase >= 1.0) phase -= 1.0;
                    y[i] += (float)(saw / detune.Length);
                }
            }
            for (int i = 0; i < total; i++) y[i] *= (float)(level * Envelope(i, length, attack, release));
            return y;
        }

        private static float[] SubTone(double hz, int length, double attack, double release, double level)
        {
            int total = length + Samples(release);
            var y = new float[total];
            for (int i = 0; i < total; i++)
                y[i] = (float)(Math.Sin(2 * Math.PI * hz * i / Rate) * level * Envelope(i, length, attack, release));
            return y;
        }

        /// <summary>Sine-plus-saw bass through a one-pole low-pass, quick attack and decay to a held level.</summary>
        private static float[] BassNote(double hz, int length, double level, double cutoff)
        {
            int total = length + Samples(0.02);
            var y = new float[total];
            double inc = hz / Rate, phase = 0, lp = 0, a = 1 - Math.Exp(-2 * Math.PI * cutoff / Rate);
            for (int i = 0; i < total; i++)
            {
                double t = i / (double)Rate;
                double saw = 2.0 * phase - 1.0 - PolyBlep(phase, inc);
                double v = Math.Sin(2 * Math.PI * phase) * 0.7 + saw * 0.45;
                phase += inc;
                if (phase >= 1.0) phase -= 1.0;
                lp += a * (v - lp);
                double env = Math.Min(1.0, t / 0.004) * (0.55 + 0.45 * Math.Exp(-t * 14));
                if (i > length) env *= Math.Max(0.0, 1.0 - (i - length) / (double)Samples(0.02));
                y[i] = (float)(Math.Tanh(lp * 1.6) * env * level);
            }
            return y;
        }

        /// <summary>Plucked saw: the filter closes as the note decays.</summary>
        private static float[] Pluck(double hz, int length, double level)
        {
            var y = new float[length];
            double inc = hz / Rate, phase = 0, lp = 0;
            for (int i = 0; i < length; i++)
            {
                double t = i / (double)Rate;
                double cutoff = 600 + 3800 * Math.Exp(-t * 18);
                double a = 1 - Math.Exp(-2 * Math.PI * cutoff / Rate);
                double saw = 2.0 * phase - 1.0 - PolyBlep(phase, inc);
                phase += inc;
                if (phase >= 1.0) phase -= 1.0;
                lp += a * (saw - lp);
                double env = Math.Min(1.0, t / 0.002) * Math.Exp(-t * 9) * Math.Min(1.0, (length - i) / (double)Samples(0.01));
                y[i] = (float)(lp * env * level);
            }
            return y;
        }

        /// <summary>Pitched kick: a sine sweeping from <paramref name="startHz"/> to <paramref name="endHz"/>.</summary>
        private static float[] Kick(double startHz, double endHz, double sweep, double seconds, double level)
        {
            int total = Samples(seconds);
            var y = new float[total];
            double phase = 0;
            for (int i = 0; i < total; i++)
            {
                double t = i / (double)Rate;
                double f = endHz + (startHz - endHz) * Math.Exp(-t * sweep * 3);
                phase += f / Rate;
                double env = Math.Min(1.0, t / 0.0015) * Math.Exp(-t * sweep) * Math.Min(1.0, (total - i) / (double)Samples(0.01));
                y[i] = (float)(Math.Sin(2 * Math.PI * phase) * env * level);
            }
            return y;
        }

        /// <summary>Hollow square stab (odd harmonics), short decay.</summary>
        private static float[] Stab(double hz, int length, double level)
        {
            var y = new float[length];
            for (int i = 0; i < length; i++)
            {
                double t = i / (double)Rate;
                double v = 0;
                for (int h = 1; h <= 9; h += 2) v += Math.Sin(2 * Math.PI * hz * h * t) / h;
                double env = Math.Min(1.0, t / 0.003) * Math.Exp(-t * 7) * Math.Min(1.0, (length - i) / (double)Samples(0.015));
                y[i] = (float)(v * env * level);
            }
            return y;
        }

        /// <summary>Inharmonic bell: three partials with their own decays.</summary>
        private static float[] Bell(double hz, int length, double level)
        {
            var y = new float[length];
            double[] ratio = { 1.0, 2.76, 5.4 }, amp = { 1.0, 0.45, 0.22 }, decay = { 2.4, 1.1, 0.5 };
            for (int i = 0; i < length; i++)
            {
                double t = i / (double)Rate, v = 0;
                for (int k = 0; k < ratio.Length; k++) v += amp[k] * Math.Exp(-t / decay[k]) * Math.Sin(2 * Math.PI * hz * ratio[k] * t);
                y[i] = (float)(v * Math.Min(1.0, t / 0.002) * level);
            }
            return y;
        }

        // ---------------- Helpers ----------------

        /// <summary>Attack (smooth), hold until <paramref name="length"/>, then a cosine release.</summary>
        private static double Envelope(int i, int length, double attack, double release)
        {
            double t = i / (double)Rate;
            double a = attack > 0 ? Math.Min(1.0, t / attack) : 1.0;
            a = a * a * (3 - 2 * a);
            if (i <= length) return a;
            double r = (i - length) / (release * Rate);
            return r >= 1.0 ? 0.0 : a * 0.5 * (1 + Math.Cos(Math.PI * r));
        }

        private static double PolyBlep(double t, double dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1.0; }
            if (t > 1.0 - dt) { t = (t - 1.0) / dt; return t * t + t + t + 1.0; }
            return 0.0;
        }

        /// <summary>Adds <paramref name="note"/> at <paramref name="start"/>, wrapping past the end to the start (loops).</summary>
        private static void AddWrapped(float[] x, int start, float[] note)
        {
            int n = x.Length;
            for (int i = 0; i < note.Length; i++) x[(start + i) % n] += note[i];
        }

        /// <summary>Adds <paramref name="note"/> at <paramref name="start"/>, dropping what falls past the end (one-shots).</summary>
        private static void AddClipped(float[] x, int start, float[] note)
        {
            for (int i = 0; i < note.Length && start + i < x.Length; i++) x[start + i] += note[i];
        }

        /// <summary>Gates a periodic noise source with an exponential decay at <paramref name="start"/> (wrapped).</summary>
        private static void AddEnveloped(float[] x, float[] source, int start, int length, double decay, double level)
        {
            int n = x.Length;
            for (int i = 0; i < length; i++)
            {
                double t = i / (double)Rate;
                double env = Math.Min(1.0, t / 0.0007) * Math.Exp(-t * decay) * Math.Min(1.0, (length - i) / (double)Samples(0.004));
                int k = (start + i) % n;
                x[k] += (float)(source[k] * env * level);
            }
        }

        /// <summary>Ducks <paramref name="x"/> after each kick (circularly), recovering exponentially.</summary>
        private static void Sidechain(float[] x, int[] kicks, double depth, double recover)
        {
            int n = x.Length;
            var gain = new float[n];
            for (int i = 0; i < n; i++) gain[i] = 1f;
            int span = Samples(0.6), duck = Samples(0.003);
            foreach (int k in kicks)
                for (int i = 0; i < span; i++)
                {
                    int j = (k + i) % n;
                    // A 3 ms ramp into the duck: an instant gain step would click on the sustained bass.
                    float g = (float)(1.0 - depth * Math.Min(1.0, i / (double)duck) * Math.Exp(-i / (double)Rate * recover));
                    if (g < gain[j]) gain[j] = g;
                }
            for (int i = 0; i < n; i++) x[i] *= gain[i];
        }
    }
}
