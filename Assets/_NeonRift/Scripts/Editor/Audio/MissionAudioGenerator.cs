using System;
using System.IO;
using System.Text;
using NeonRift.Audio;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>
    /// Re-runnable recipe for the mission sounds: lockdown siren, barrier klaxon/motor/slam, the city ambience bed,
    /// the hacking loop and HUD/story cues. Synthesised in-house and deterministic like the vehicle audio, so there
    /// are no third-party samples. Loops are built from periodic signals (integer cycles per loop) so they never click.
    /// Produces the clips plus the <see cref="MissionAudioSet"/> asset the mission scene uses.
    /// </summary>
    public static class MissionAudioGenerator
    {
        public const string ClipFolder = VehicleAudioGenerator.ClipRoot + "/Mission";
        public const string SetPath = "Assets/_NeonRift/Data/Missions/MissionAudio_NightRun.asset";
        private const float Tau = Mathf.PI * 2f;

        public struct Clips
        {
            public AudioClip Siren, Klaxon, Motor, Slam;
            public MissionAudioSet Set;
        }

        [MenuItem("Neon Rift/Audio/Generate Mission Audio")]
        public static void GenerateFromMenu() => Debug.Log(Generate(out _));

        public static string Generate(out Clips clips)
        {
            var log = new StringBuilder("[MissionAudio] generated:\n");
            clips = new Clips
            {
                Siren = Save("Siren_Loop", Siren(), true, log),
                Klaxon = Save("Klaxon_Loop", Klaxon(), true, log),
                Motor = Save("BarrierMotor_Loop", Motor(), true, log),
                Slam = Save("Barrier_Slam", Slam(), false, log)
            };
            var ambience = Save("City_AmbienceLoop", Ambience(), true, log);
            var hack = Save("Hack_Loop", HackLoop(), true, log);
            var objective = Save("Cue_Objective", Chime(new[] { 659.3f, 987.8f }, 0.11f, 0.5f), false, log);
            var complete = Save("Cue_InteractComplete", Chime(new[] { 784f, 1046.5f, 1568f }, 0.07f, 0.7f, sweep: true), false, log);
            var heat = Save("Cue_Heat", Warning(new[] { 880f, 622f }, 0.13f), false, log);
            var lockdown = Save("Cue_Lockdown", Lockdown(), false, log);
            var tick = Save("Cue_Tick", Tick(1250f, 0.035f), false, log);
            var message = Save("Cue_Message", Tick(1700f, 0.02f, twin: true), false, log);
            var success = Save("Cue_Success", Chord(new[] { 523.3f, 659.3f, 784f, 1046.5f }, 0.09f, 2.6f, 1f), false, log);
            var failure = Save("Cue_Failure", Chord(new[] { 392f, 311.1f, 261.6f, 196f }, 0.22f, 2.4f, -1f), false, log);

            var set = AssetDatabase.LoadAssetAtPath<MissionAudioSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<MissionAudioSet>();
                VehiclePrefabBuilder.EnsureFolder(Path.GetDirectoryName(SetPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.EditorConfigure(ambience, hack, objective, complete, heat, lockdown, tick, message, success, failure);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            clips.Set = set;
            log.AppendLine($"  set: {SetPath}");
            return log.ToString();
        }

        // ---------------- Loops ----------------

        /// <summary>Wailing lockdown siren, 4 s period. The pitch sweep averages to an integer cycle count, so the loop is seamless.</summary>
        private static float[] Siren()
        {
            const float period = 4f;
            int n = Mathf.RoundToInt(period * Dsp.SampleRate);
            var x = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                // 650..1150 Hz, slow rise, faster fall; averages exactly 900 Hz → 3600 cycles per loop.
                float sweep = -Mathf.Cos(Tau * t / period) - 0.25f * Mathf.Sin(2f * Tau * t / period);
                double hz = 900.0 + 250.0 * sweep;
                phase += hz / Dsp.SampleRate;
                float p = (float)(phase - Math.Floor(phase));
                float s = Mathf.Sin(Tau * p) + 0.45f * Mathf.Sin(2f * Tau * p) + 0.25f * Mathf.Sin(3f * Tau * p) + 0.12f * Mathf.Sin(5f * Tau * p);
                x[i] = (float)Math.Tanh(1.4f * s);
            }
            var bp = Dsp.Biquad.BandPass(1100f, 0.6f);
            var y = Dsp.Periodic(x, bp.Process);
            // Distant city echo: two long periodic reflections.
            var echo = Dsp.PeriodicComb(y, Mathf.RoundToInt(0.137f * Dsp.SampleRate), 0.35f);
            for (int i = 0; i < n; i++) y[i] = 0.75f * y[i] + 0.25f * echo[i];
            Dsp.NormalizeRms(y, 0.22f);
            return y;
        }

        /// <summary>Two-tone barrier klaxon, 1 s loop (two blasts).</summary>
        private static float[] Klaxon()
        {
            int n = Dsp.SampleRate;
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float local = t % 0.5f;
                float hz = t < 0.5f ? 440f : 370f;
                float gate = Mathf.Clamp01(local / 0.01f) * Mathf.Clamp01((0.36f - local) / 0.02f);
                float p = (hz * t) % 1f;
                float square = Mathf.Sign(Mathf.Sin(Tau * p)) * 0.6f + 0.4f * Mathf.Sin(Tau * p);
                x[i] = gate * square;
            }
            var lp = Dsp.Biquad.LowPass(2600f);
            var y = Dsp.Periodic(x, lp.Process);
            Dsp.NormalizeRms(y, 0.2f);
            return y;
        }

        /// <summary>Hydraulic gate drive: 50 Hz hum, gear whine and rumble, 2 s loop.</summary>
        private static float[] Motor()
        {
            var rng = new System.Random(808);
            int n = Dsp.SampleRate * 2;
            var lp = Dsp.Biquad.LowPass(300f);
            var rumble = Dsp.Periodic(Dsp.Noise(n, rng), lp.Process);
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float hum = Mathf.Sin(Tau * 50f * t) + 0.5f * Mathf.Sin(Tau * 100f * t) + 0.3f * Mathf.Sin(Tau * 150f * t);
                float whine = 0.15f * Mathf.Sin(Tau * 610f * t) * (0.7f + 0.3f * Mathf.Sin(Tau * 3f * t));
                y[i] = 0.5f * hum + whine + 1.6f * rumble[i];
            }
            Dsp.NormalizeRms(y, 0.2f);
            return y;
        }

        /// <summary>City bed: traffic rumble, electrical hum, air and a distant elevated-train swell. 16 s loop.</summary>
        private static float[] Ambience()
        {
            var rng = new System.Random(1201);
            const float seconds = 16f;
            int n = Mathf.RoundToInt(seconds * Dsp.SampleRate);
            var noise = Dsp.Noise(n, rng);
            var low = Dsp.Biquad.LowPass(160f);
            var traffic = Dsp.Periodic(noise, low.Process);
            var bandA = Dsp.Biquad.BandPass(700f, 0.8f);
            var air = Dsp.Periodic(Dsp.Noise(n, rng), bandA.Process);
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float u = t / seconds;
                float swell = 0.5f + 0.5f * Mathf.Sin(Tau * u - 1.3f);
                float trafficLevel = 1f + 0.35f * Mathf.Sin(Tau * 2f * u) + 0.2f * Mathf.Sin(Tau * 5f * u + 1f);
                float hum = 0.06f * (Mathf.Sin(Tau * 60f * t) + 0.5f * Mathf.Sin(Tau * 120f * t) + 0.3f * Mathf.Sin(Tau * 180f * t));
                // Neon transformer buzz, slowly pulsing.
                float buzz = 0.012f * Mathf.Sign(Mathf.Sin(Tau * 240f * t)) * (0.6f + 0.4f * Mathf.Sin(Tau * 3f * u));
                y[i] = 2.2f * traffic[i] * trafficLevel + 0.25f * air[i] * (0.4f + swell) + hum + buzz;
            }
            Dsp.NormalizeRms(y, 0.16f);
            return y;
        }

        /// <summary>Data-intrusion chatter: a fixed pattern of short blips on a 16th grid, 2 s loop.</summary>
        private static float[] HackLoop()
        {
            var rng = new System.Random(77);
            int n = Dsp.SampleRate * 2;
            var x = new float[n];
            float[] scale = { 1046.5f, 1174.7f, 1396.9f, 1568f, 1760f, 2093f, 2349.3f };
            for (int step = 0; step < 32; step++)
            {
                if (rng.NextDouble() < 0.18) continue;
                float start = step * (2f / 32f);
                float hz = scale[rng.Next(scale.Length)];
                int s0 = Mathf.RoundToInt(start * Dsp.SampleRate);
                int len = Mathf.RoundToInt(0.045f * Dsp.SampleRate);
                for (int i = 0; i < len && s0 + i < n; i++)
                {
                    float t = i / (float)Dsp.SampleRate;
                    float env = Mathf.Clamp01(t / 0.002f) * Mathf.Exp(-t / 0.015f);
                    x[s0 + i] += env * Mathf.Sign(Mathf.Sin(Tau * hz * t)) * 0.5f;
                }
            }
            // Under-bed: modem-like 2-tone pulse.
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float hz = ((int)(t * 16f) % 2 == 0) ? 1200f : 2200f;
                x[i] += 0.08f * Mathf.Sin(Tau * hz * t);
            }
            var lp = Dsp.Biquad.LowPass(5000f);
            var y = Dsp.Periodic(x, lp.Process);
            Dsp.NormalizeRms(y, 0.17f);
            return y;
        }

        // ---------------- One-shots ----------------

        private static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Dsp.SampleRate)];

        private static float[] Slam()
        {
            var rng = new System.Random(919);
            var x = Buffer(1.6f);
            Dsp.AddNoiseBurst(x, 0f, 0.09f, 1f, Dsp.Biquad.LowPass(900f), rng);
            Dsp.AddRing(x, 0f, 58f, 0.25f, 0.9f);
            Dsp.AddRing(x, 0f, 131f, 0.18f, 0.45f);
            Dsp.AddRing(x, 0.002f, 347f, 0.35f, 0.18f);
            Dsp.AddRing(x, 0.004f, 811f, 0.22f, 0.08f);
            Dsp.AddNoiseBurst(x, 0.05f, 0.3f, 0.12f, Dsp.Biquad.BandPass(2400f, 2f), rng);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.95f);
            return x;
        }

        private static float[] Chime(float[] notes, float spacing, float decay, bool sweep = false)
        {
            var x = Buffer(spacing * notes.Length + decay * 5f);
            for (int k = 0; k < notes.Length; k++)
            {
                Dsp.AddRing(x, k * spacing, notes[k], decay, 0.5f);
                Dsp.AddRing(x, k * spacing, notes[k] * 2.01f, decay * 0.4f, 0.15f);
                Dsp.AddRing(x, k * spacing, notes[k] * 3.98f, decay * 0.15f, 0.06f);
            }
            if (sweep) Dsp.AddNoiseBurst(x, 0f, 0.12f, 0.15f, Dsp.Biquad.HighPass(3000f), new System.Random(5));
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.8f);
            return x;
        }

        private static float[] Warning(float[] notes, float length)
        {
            var x = Buffer(length * notes.Length + 0.1f);
            for (int k = 0; k < notes.Length; k++)
            {
                int s0 = Mathf.RoundToInt(k * length * Dsp.SampleRate);
                int len = Mathf.RoundToInt(length * 0.85f * Dsp.SampleRate);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Dsp.SampleRate;
                    float env = Mathf.Clamp01(t / 0.004f) * Mathf.Clamp01((length * 0.85f - t) / 0.015f);
                    float p = (notes[k] * t) % 1f;
                    x[s0 + i] += env * (0.6f * Mathf.Sin(Tau * p) + 0.4f * (p < 0.5f ? 1f : -1f) * 0.5f);
                }
            }
            var lp = Dsp.Biquad.LowPass(3500f);
            for (int i = 0; i < x.Length; i++) x[i] = lp.Process(x[i]);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.75f);
            return x;
        }

        /// <summary>Lockdown hit: sub drop, detuned saw stab closing down, metallic tail.</summary>
        private static float[] Lockdown()
        {
            var rng = new System.Random(1313);
            var x = Buffer(3.2f);
            float[] chord = { 110f, 116.5f, 164.8f, 220f, 233.1f };
            double[] phases = new double[chord.Length * 2];
            var lp = Dsp.Biquad.LowPass(4000f);
            for (int i = 0; i < x.Length; i++)
            {
                float t = i / (float)Dsp.SampleRate;
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-t / 0.9f);
                float s = 0f;
                for (int k = 0; k < chord.Length; k++)
                    for (int d = 0; d < 2; d++)
                    {
                        int idx = k * 2 + d;
                        phases[idx] += chord[k] * (d == 0 ? 0.997 : 1.004) / Dsp.SampleRate;
                        s += (float)(2.0 * (phases[idx] - Math.Floor(phases[idx])) - 1.0);
                    }
                // Filter closes as the stab decays.
                x[i] = env * s * 0.12f;
                float sub = Mathf.Sin(Tau * (55f - 20f * Mathf.Clamp01(t / 1.2f)) * t) * Mathf.Exp(-t / 0.8f) * Mathf.Clamp01(t / 0.004f);
                x[i] += 0.9f * sub;
            }
            for (int i = 0; i < x.Length; i++) x[i] = lp.Process(x[i]);
            Dsp.AddNoiseBurst(x, 0f, 0.5f, 0.2f, Dsp.Biquad.BandPass(1800f, 1.2f), rng);
            Dsp.AddRing(x, 0f, 1567f, 0.6f, 0.05f);
            Dsp.FadeEdges(x, 1f, 60f);
            Dsp.NormalizePeak(x, 0.95f);
            return x;
        }

        private static float[] Tick(float hz, float decay, bool twin = false)
        {
            var x = Buffer(twin ? 0.25f : 0.15f);
            Dsp.AddRing(x, 0f, hz, decay, 0.6f);
            if (twin) Dsp.AddRing(x, 0.09f, hz * 1.335f, decay, 0.5f);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.6f);
            return x;
        }

        /// <summary>Arpeggiated chord with a sustained pad; <paramref name="direction"/> 1 rises, −1 falls (failure).</summary>
        private static float[] Chord(float[] notes, float spacing, float length, float direction)
        {
            var x = Buffer(length + 0.5f);
            for (int k = 0; k < notes.Length; k++)
            {
                float start = k * spacing;
                Dsp.AddRing(x, start, notes[k], length * 0.35f, 0.4f);
                Dsp.AddRing(x, start, notes[k] * 2f, length * 0.12f, 0.1f);
                int s0 = Mathf.RoundToInt(start * Dsp.SampleRate);
                for (int i = s0; i < x.Length; i++)
                {
                    float t = (i - s0) / (float)Dsp.SampleRate;
                    float env = Mathf.Clamp01(t / 0.25f) * Mathf.Exp(-t / (length * 0.45f));
                    float hz = notes[k] * (direction < 0f ? 1f - 0.03f * Mathf.Clamp01(t / length) : 1f);
                    x[i] += 0.08f * env * (Mathf.Sin(Tau * hz * 1.003f * t) + Mathf.Sin(Tau * hz * 0.997f * t));
                }
            }
            Dsp.FadeEdges(x, 1.5f, 120f);
            Dsp.NormalizePeak(x, 0.75f);
            return x;
        }

        // ---------------- Assets ----------------

        private static AudioClip Save(string name, float[] samples, bool loop, StringBuilder log)
        {
            string path = $"{ClipFolder}/{name}.wav";
            VehiclePrefabBuilder.EnsureFolder(ClipFolder);
            AudioSignalAnalysis.WriteWav(Path.GetFullPath(path), samples, Dsp.SampleRate);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono = true;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            // ADPCM everywhere: sample-accurate loop points and cheap decoding. Long beds stay compressed in memory.
            bool longBed = samples.Length > Dsp.SampleRate * 8;
            settings.loadType = longBed ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            settings.preloadAudioData = true;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            log.AppendLine($"  {name}: {samples.Length / (float)Dsp.SampleRate:0.00}s{(loop ? " loop" : string.Empty)}");
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
