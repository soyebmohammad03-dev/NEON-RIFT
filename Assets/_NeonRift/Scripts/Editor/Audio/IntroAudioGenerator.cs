using System;
using System.IO;
using System.Text;
using NeonRift.Audio;
using NeonRift.EditorTools.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>
    /// Original, procedural audio for the opening cinematic. Everything is synthesised here (no samples, no licensed
    /// music). The "music" (drone, pulse) is a PLACEHOLDER layer meant to be replaced by a composed score:
    /// <list type="bullet">
    /// <item>Drone: low evolving pad with mains hum, 16 s seamless loop;</item>
    /// <item>Pulse: 120 bpm low pulse with a bass figure, 8 s seamless loop (placeholder music bed);</item>
    /// <item>Riser: 6 s noise and pitch rise into the title;</item>
    /// <item>Impact: sub drop with a metallic ring (cuts, title);</item>
    /// <item>Whoosh: short filtered noise swell for quick cuts;</item>
    /// <item>Scan: security scanner beeps and data chirps;</item>
    /// <item>Ignition: starter crank, catch and settle.</item>
    /// </list>
    /// </summary>
    public static class IntroAudioGenerator
    {
        public const string ClipFolder = "Assets/_NeonRift/Audio/Generated/Intro";
        private const int Rate = Dsp.SampleRate;

        public struct Clips
        {
            public AudioClip Drone, Pulse, Riser, Impact, Whoosh, Scan, Ignition;
        }

        [MenuItem("Neon Rift/Intro/Generate Intro Audio")]
        private static void GenerateFromMenu() => Debug.Log(Generate(out _));

        public static string Generate(out Clips clips)
        {
            var log = new StringBuilder("[IntroAudio] generated (original, procedural; drone and pulse are placeholder music):\n");
            clips = new Clips
            {
                Drone = Save("Intro_Drone_Placeholder", Drone(), true, log),
                Pulse = Save("Intro_Pulse_Placeholder", Pulse(), true, log),
                Riser = Save("Intro_Riser", Riser(), false, log),
                Impact = Save("Intro_Impact", Impact(), false, log),
                Whoosh = Save("Intro_Whoosh", Whoosh(), false, log),
                Scan = Save("Intro_Scan", Scan(), false, log),
                Ignition = Save("Intro_Ignition", Ignition(), false, log)
            };
            return log.ToString();
        }

        private static float[] Drone()
        {
            const float seconds = 16f;
            int n = (int)(seconds * Rate);
            var x = new float[n];
            var rng = new System.Random(11);
            // Frequencies on a 1/16 Hz grid and LFO periods dividing 16 s: the loop is seamless.
            float[] partials = { 55f, 82.5f, 110f, 164.9375f };
            float[] levels = { 0.5f, 0.28f, 0.2f, 0.07f };
            var low = Dsp.Biquad.LowPass(380);
            var air = Dsp.Periodic(Dsp.Noise(n, rng), v => low.Process(v));   // steady-state pass: loops cleanly
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double swell = 0.65 + 0.35 * Math.Sin(2 * Math.PI * t / 16.0);
                double s = 0;
                for (int k = 0; k < partials.Length; k++)
                    s += levels[k] * Math.Sin(2 * Math.PI * partials[k] * t + 0.4 * Math.Sin(2 * Math.PI * t / 8.0 + k));
                double hum = 0.04 * Math.Sin(2 * Math.PI * 50 * t) + 0.025 * Math.Sin(2 * Math.PI * 100 * t) + 0.012 * Math.Sin(2 * Math.PI * 150 * t);
                double breath = air[i] * 0.35 * (0.6 + 0.4 * Math.Sin(2 * Math.PI * t / 4.0));
                x[i] = (float)(s * swell * 0.55 + hum + breath);
            }
            Dsp.RemoveDc(x);
            Dsp.NormalizeRms(x, 0.12f);
            return x;
        }

        private static float[] Pulse()
        {
            const float bpm = 120f, seconds = 8f;
            int n = (int)(seconds * Rate);
            var x = new float[n];
            var rng = new System.Random(5);
            float beat = 60f / bpm;
            float[] bass = { 55f, 55f, 49f, 58.27f };   // A, A, G, Bb: one note per bar
            var hat = Dsp.Biquad.HighPass(7000);
            var hiss = Dsp.Periodic(Dsp.Noise(n, rng), v => hat.Process(v));
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double inBeat = t % beat;
                int bar = (int)(t / (beat * 4)) % bass.Length;
                // Kick-like thump: falling sine on every beat.
                double kick = Math.Exp(-inBeat * 18) * Math.Sin(2 * Math.PI * (45 + 90 * Math.Exp(-inBeat * 30)) * inBeat) * 0.9;
                // Bass: plucked saw-ish on the off-beat eighths.
                double eighth = t % (beat * 0.5);
                double bassEnv = Math.Exp(-eighth * 6) * (((int)(t / (beat * 0.5)) % 2 == 1) ? 1 : 0.35);
                double f = bass[bar];
                double saw = 0;
                for (int h = 1; h <= 5; h++) saw += Math.Sin(2 * Math.PI * f * h * t) / h;
                // Hats on the off-beats.
                double hatEnv = Math.Exp(-((t + beat * 0.5) % beat) * 60);
                x[i] = (float)(kick + saw * bassEnv * 0.22 + hiss[i] * hatEnv * 0.25);
            }
            Dsp.RemoveDc(x);
            Dsp.NormalizeRms(x, 0.13f);
            return x;
        }

        private static float[] Riser()
        {
            const float seconds = 6f;
            int n = (int)(seconds * Rate);
            var x = new float[n];
            var rng = new System.Random(3);
            var noise = Dsp.Noise(n, rng);
            double phase = 0;
            var band = new SweepBand();
            for (int i = 0; i < n; i++)
            {
                double p = i / (double)n;
                double env = Math.Pow(p, 2.2);
                double centre = 300 * Math.Pow(20, p);
                double f = 110 * Math.Pow(8, p);
                phase += 2 * Math.PI * f / Rate;
                x[i] = (float)((band.Process(noise[i], centre * 0.6, centre * 1.6) * 1.6 + Math.Sin(phase) * 0.35 + Math.Sin(phase * 1.5) * 0.15) * env);
            }
            Dsp.FadeEdges(x, 5f, 30f);
            Dsp.NormalizePeak(x, 0.8f);
            return x;
        }

        private static float[] Impact()
        {
            const float seconds = 3f;
            int n = (int)(seconds * Rate);
            var x = new float[n];
            var rng = new System.Random(9);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double f = 38 + 34 * Math.Exp(-t * 6);
                phase += 2 * Math.PI * f / Rate;
                x[i] = (float)(Math.Sin(phase) * Math.Exp(-t * 1.6) * 0.9);
            }
            Dsp.AddNoiseBurst(x, 0f, 0.35f, 0.5f, Dsp.Biquad.LowPass(900), rng);
            Dsp.AddRing(x, 0.005f, 420f, 0.9f, 0.12f);
            Dsp.AddRing(x, 0.005f, 1270f, 0.5f, 0.06f, 0.3f);
            Dsp.FadeEdges(x, 0.5f, 80f);
            Dsp.NormalizePeak(x, 0.9f);
            return x;
        }

        private static float[] Whoosh()
        {
            const float seconds = 0.7f;
            int n = (int)(seconds * Rate);
            var x = new float[n];
            var noise = Dsp.Noise(n, new System.Random(21));
            var band = new SweepBand();
            for (int i = 0; i < n; i++)
            {
                double p = i / (double)n;
                double centre = 2400 * Math.Pow(0.25, p);
                double env = Math.Sin(Math.PI * Math.Pow(p, 0.6));
                x[i] = (float)(band.Process(noise[i], centre * 0.5, centre * 1.8) * env);
            }
            Dsp.FadeEdges(x, 2f, 40f);
            Dsp.NormalizePeak(x, 0.6f);
            return x;
        }

        private static float[] Scan()
        {
            const float seconds = 4f;
            int n = (int)(seconds * Rate);
            var x = new float[n];
            var rng = new System.Random(17);
            for (float b = 0.15f; b < seconds - 0.2f; b += 0.5f)
            {
                Dsp.AddRing(x, b, 1800f, 0.06f, 0.35f);
                Dsp.AddRing(x, b + 0.09f, 2400f, 0.04f, 0.18f);
            }
            // Data chirps: short random tones.
            for (int k = 0; k < 18; k++)
                Dsp.AddRing(x, (float)rng.NextDouble() * (seconds - 0.3f), 2800f + (float)rng.NextDouble() * 2400f, 0.015f, 0.08f);
            Dsp.FadeEdges(x, 1f, 30f);
            Dsp.NormalizePeak(x, 0.55f);
            return x;
        }

        private static float[] Ignition()
        {
            const float seconds = 2.4f;
            int n = (int)(seconds * Rate);
            var x = new float[n];
            var rng = new System.Random(31);
            var noise = Dsp.Noise(n, rng);
            var buzz = Dsp.Biquad.BandPass(180, 2.5);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double s = 0;
                if (t < 0.85)
                {
                    // Starter: a motor whine chopped by compression strokes (~11 Hz), getting quicker.
                    double crank = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (9 + 4 * t) * t);
                    s += buzz.Process(noise[i]) * crank * 0.6 + Math.Sin(2 * Math.PI * 160 * t) * 0.08 * crank;
                }
                else
                {
                    // Catch: firing flares to ~3000 rpm (V8: 4 pulses per rev) and settles towards idle.
                    double u = t - 0.85;
                    double rpm = 900 + 2100 * Math.Exp(-u * 3) * Math.Min(1, u * 12);
                    double f = rpm / 60.0 * 4;
                    phase += 2 * Math.PI * f / Rate;
                    double fire = 0;
                    for (int h = 1; h <= 6; h++) fire += Math.Sin(phase * h) / (h * 1.3);
                    s += fire * 0.45 * Math.Min(1, u * 20) * (0.6 + 0.4 * Math.Exp(-u * 1.5));
                }
                x[i] = (float)s;
            }
            Dsp.RemoveDc(x);
            Dsp.FadeEdges(x, 2f, 200f);
            Dsp.NormalizePeak(x, 0.85f);
            return x;
        }

        /// <summary>Sweepable band-pass: difference of two one-pole low-passes whose cutoffs can change every sample.</summary>
        private sealed class SweepBand
        {
            private double lo, hi;

            public double Process(double x, double lowHz, double highHz)
            {
                double aHi = 1 - Math.Exp(-2 * Math.PI * highHz / Rate);
                double aLo = 1 - Math.Exp(-2 * Math.PI * lowHz / Rate);
                hi += aHi * (x - hi);
                lo += aLo * (x - lo);
                return hi - lo;
            }
        }

        private static AudioClip Save(string name, float[] samples, bool loop, StringBuilder log)
        {
            string path = $"{ClipFolder}/{name}.wav";
            VehiclePrefabBuilder.EnsureFolder(ClipFolder);
            AudioSignalAnalysis.WriteWav(Path.GetFullPath(path), samples, Rate);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = samples.Length > Rate * 8 ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            log.AppendLine($"  {name}: {samples.Length / (float)Rate:0.00}s{(loop ? " loop" : string.Empty)}");
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
