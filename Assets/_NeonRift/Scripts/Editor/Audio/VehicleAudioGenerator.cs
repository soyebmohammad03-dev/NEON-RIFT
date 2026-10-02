using System.Collections.Generic;
using System.IO;
using System.Text;
using NeonRift.Audio;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Audio
{
    /// <summary>
    /// Re-runnable recipe for every vehicle sound: synthesises the engine loops, tyre/road/wind beds and one-shots
    /// (deterministic, so re-running reproduces identical files), sets import settings, and builds a
    /// <see cref="VehicleAudioProfile"/> per car linked to its definition. All audio is generated in-house: no
    /// third-party samples, no licence obligations.
    /// </summary>
    public static class VehicleAudioGenerator
    {
        public const string ClipRoot = "Assets/_NeonRift/Audio/Generated";
        public const string ProfileFolder = "Assets/_NeonRift/Data/Vehicles/Audio";
        private const string Licence = "Procedurally synthesised in-house by VehicleAudioGenerator (Neon Rift ▸ Audio ▸ Generate Vehicle Audio). Project-owned, no third-party samples.";

        // Cross-plane V8, firing order 1-5-4-8-6-3-7-2 every 90°: cylinders 1-4 on bank 0, 5-8 on bank 1.
        private static readonly float[] CrossPlaneAngles = { 0, 90, 180, 270, 360, 450, 540, 630 };
        private static readonly int[] CrossPlaneBanks = { 0, 1, 0, 1, 1, 0, 1, 0 };

        public static EngineSynth.Voice RoadV8 => new()
        {
            Name = "V8Road", FireAngles = CrossPlaneAngles, Banks = CrossPlaneBanks,
            PulseDecayMs = 2.4f, PulseAttackMs = 0.3f,
            Resonances = new[] { new EngineSynth.Resonance(95, 1.5f, 1f), new EngineSynth.Resonance(190, 2.5f, 0.7f), new EngineSynth.Resonance(420, 3f, 0.45f), new EngineSynth.Resonance(1150, 2.5f, 0.18f) },
            BankDetune = 1.07f, PipeDelayMs = 7.5f, PipeFeedback = 0.32f, MufflerOnHz = 2400f, MufflerOffHz = 1200f,
            NoiseOn = 0.35f, NoiseOff = 0.25f, PulseOff = 0.38f, IrregularityOn = 0.06f, IrregularityOff = 0.28f,
            DriveOn = 1.8f, DriveOff = 1.2f, IntakeOn = 0.1f, IntakeOff = 0.03f, IntakeHz = 900f, LoopSeconds = 1.6f
        };

        public static EngineSynth.Voice RaceV8 => new()
        {
            Name = "V8Race", FireAngles = CrossPlaneAngles, Banks = CrossPlaneBanks,
            PulseDecayMs = 1.6f, PulseAttackMs = 0.2f,
            Resonances = new[] { new EngineSynth.Resonance(120, 1.4f, 0.9f), new EngineSynth.Resonance(320, 2.2f, 0.9f), new EngineSynth.Resonance(900, 2.5f, 0.65f), new EngineSynth.Resonance(2600, 2f, 0.35f) },
            BankDetune = 1.04f, PipeDelayMs = 4.5f, PipeFeedback = 0.42f, MufflerOnHz = 6500f, MufflerOffHz = 3800f,
            NoiseOn = 0.5f, NoiseOff = 0.35f, PulseOff = 0.45f, IrregularityOn = 0.05f, IrregularityOff = 0.3f,
            DriveOn = 2.6f, DriveOff = 1.6f, IntakeOn = 0.18f, IntakeOff = 0.05f, IntakeHz = 1400f, LoopSeconds = 1.6f
        };

        public static EngineSynth.Voice ElectricDrive => new()
        {
            Name = "EV", Electric = true,
            WhineOrders = new[] { 6f, 12f, 18f, 36f }, WhineOn = new[] { 1f, 0.6f, 0.35f, 0.15f }, WhineOff = new[] { 1f, 0.3f, 0.15f, 0.08f },
            InverterHz = 2400f, InverterOn = 0.06f, InverterOff = 0.02f, RumbleOn = 0.2f, RumbleOff = 0.15f, LoopSeconds = 1.5f
        };

        [MenuItem("Neon Rift/Audio/Generate Vehicle Audio")]
        public static void GenerateFromMenu() => Debug.Log(Generate());

        public static string Generate()
        {
            var log = new StringBuilder("[AudioGen]\n");
            VehiclePrefabBuilder.EnsureFolder(ClipRoot);
            VehiclePrefabBuilder.EnsureFolder(ProfileFolder);

            var roadEngine = EngineLayers(RoadV8, "Engine_V8Road", new[] { 800f, 1700f, 3200f, 5000f, 7200f }, log);
            var raceEngine = EngineLayers(RaceV8, "Engine_V8Race", new[] { 1100f, 2200f, 3700f, 5500f, 7500f }, log);
            var evEngine = EngineLayers(ElectricDrive, "Engine_EV", new[] { 800f, 2500f, 6000f, 11000f, 16000f }, log);

            var skid = Save("Tyres/Tyres_SkidLoop", Skid(), true);
            var scrub = Save("Tyres/Tyres_ScrubLoop", Scrub(), true);
            var loose = Save("Tyres/Tyres_LooseSurfaceLoop", Loose(), true);
            var road = Save("Road/Road_RollLoop", Road(), true);
            var wind = Save("Road/Wind_Loop", Wind(), true);
            var roadShifts = new[] { Save("Mechanical/Shift_Road_1", Shift(1, false), false), Save("Mechanical/Shift_Road_2", Shift(2, false), false) };
            var raceShifts = new[] { Save("Mechanical/Shift_Race_1", Shift(3, true), false), Save("Mechanical/Shift_Race_2", Shift(4, true), false) };
            var evShifts = new[] { Save("Mechanical/Shift_EV_1", EvClick(), false) };
            var pops = new List<AudioClip>();
            for (int i = 0; i < 4; i++) pops.Add(Save($"Engine_Pops/Pop_{i + 1}", Pop(i), false));
            var thumps = new[] { Save("Mechanical/Thump_1", Thump(1), false), Save("Mechanical/Thump_2", Thump(2), false) };
            var light = new[] { Save("Impacts/Impact_Light_1", Impact(1, 0), false), Save("Impacts/Impact_Light_2", Impact(1, 1), false) };
            var medium = new[] { Save("Impacts/Impact_Medium_1", Impact(2, 0), false), Save("Impacts/Impact_Medium_2", Impact(2, 1), false) };
            var heavy = new[] { Save("Impacts/Impact_Heavy_1", Impact(3, 0), false), Save("Impacts/Impact_Heavy_2", Impact(3, 1), false) };

            ChassisAudioSettings Chassis(float skidVol, float roadVol, float windVol) => new()
            {
                skidLoop = skid, skidVolume = skidVol, skidSlipRange = new Vector2(1.05f, 1.7f),
                scrubLoop = scrub, scrubVolume = 0.45f, scrubSlipRange = new Vector2(0.6f, 1.05f),
                looseSurfaceGrip = 0.8f, looseSurfaceLoop = loose, looseSurfaceVolume = 0.7f,
                roadLoop = road, roadVolume = roadVol, windLoop = wind, windVolume = windVol, fullSpeedKph = 250f,
                suspensionThumps = thumps, thumpSpeed = 0.7f, thumpVolume = 0.6f,
                lightImpacts = light, mediumImpacts = medium, heavyImpacts = heavy,
                impactThresholds = new Vector3(40f, 1500f, 6000f), impactVolume = 1f
            };

            var sls = Profile("VehicleAudio_MercedesSLS", new EngineAudioSettings
            {
                layers = roadEngine, volume = 0.9f, idleVolume = 0.35f, offLoadVolume = 0.45f, rpmSmoothing = 0.04f,
                loadAttack = 0.06f, loadRelease = 0.14f, shiftLoad = 0.25f, minPitch = 0.5f, maxPitch = 1.8f,
                shiftClips = roadShifts, shiftVolume = 0.35f, overrunPops = pops.ToArray(), popRate = 4f, popMinRpm = 3500f, popVolume = 0.5f
            }, Chassis(0.6f, 0.45f, 0.4f), "Front-mid cross-plane V8, valved road exhaust. " + Licence);

            var gt3 = Profile("VehicleAudio_MercedesSLSGT3", new EngineAudioSettings
            {
                layers = raceEngine, volume = 1f, idleVolume = 0.3f, offLoadVolume = 0.5f, rpmSmoothing = 0.03f,
                loadAttack = 0.04f, loadRelease = 0.1f, shiftLoad = 0.15f, minPitch = 0.5f, maxPitch = 1.8f,
                shiftClips = raceShifts, shiftVolume = 0.5f, overrunPops = pops.ToArray(), popRate = 6f, popMinRpm = 4000f, popVolume = 0.7f
            }, Chassis(0.7f, 0.55f, 0.45f), "Cross-plane V8 with race exhaust: brighter, rawer, louder overrun. " + Licence);

            var terzo = Profile("VehicleAudio_LamborghiniTerzo", new EngineAudioSettings
            {
                layers = evEngine, volume = 0.75f, idleVolume = 0.15f, offLoadVolume = 0.6f, rpmSmoothing = 0.05f,
                loadAttack = 0.1f, loadRelease = 0.2f, shiftLoad = 1f, minPitch = 0.3f, maxPitch = 2f,
                shiftClips = evShifts, shiftVolume = 0.15f, overrunPops = null, popRate = 0f, popMinRpm = 0f, popVolume = 0f
            }, Chassis(0.6f, 0.5f, 0.45f), "Electric drive: motor and reduction-gear whine with inverter noise (fictional EV interpretation). " + Licence);

            Link("Vehicle_MercedesSLS", sls, log);
            Link("Vehicle_MercedesSLSGT3", gt3, log);
            Link("Vehicle_LamborghiniTerzo", terzo, log);
            Link("Vehicle_MercedesAMGGT3", gt3, log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static EngineSoundLayer[] EngineLayers(EngineSynth.Voice voice, string folder, float[] rpms, StringBuilder log)
        {
            var layers = new List<EngineSoundLayer>();
            foreach (bool onLoad in new[] { true, false })
                foreach (float rpm in rpms)
                {
                    var samples = EngineSynth.Render(voice, rpm, onLoad, out float actual);
                    var clip = Save($"{folder}/{voice.Name}_{(onLoad ? "On" : "Off")}_{rpm:0}", samples, true, engine: true);
                    layers.Add(new EngineSoundLayer { clip = clip, recordedRpm = actual, onLoad = onLoad, volume = onLoad ? 1f : 0.9f });
                    log.AppendLine($"  {voice.Name} {(onLoad ? "on " : "off")} {actual:0.0} rpm, {samples.Length / (float)Dsp.SampleRate:0.00} s, seam {AudioSignalAnalysis.LoopSeamScore(samples):0.00}");
                }
            return layers.ToArray();
        }

        // ---------------- Beds (seamless loops) ----------------

        private static float[] Skid()
        {
            var rng = new System.Random(101);
            int n = Dsp.SampleRate * 2;
            var noise = Dsp.Noise(n, rng);
            var y = new float[n];
            foreach (var (hz, gain) in new[] { (930f, 1f), (1410f, 0.75f), (2080f, 0.5f), (3150f, 0.2f) })
            {
                var bp = Dsp.Biquad.BandPass(hz, 22f);
                var band = Dsp.Periodic(noise, bp.Process);
                for (int i = 0; i < n; i++) y[i] += gain * band[i];
            }
            var hiss = Dsp.Biquad.BandPass(2500f, 0.7f);
            var h = Dsp.Periodic(Dsp.Noise(n, rng), hiss.Process);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                float am = (float)(1 + 0.25 * System.Math.Sin(2 * System.Math.PI * 13 * t) + 0.15 * System.Math.Sin(2 * System.Math.PI * 29 * t + 1));
                y[i] = y[i] * am + 0.08f * h[i];
            }
            Dsp.NormalizeRms(y, 0.2f);
            return y;
        }

        private static float[] Scrub()
        {
            var rng = new System.Random(202);
            int n = Dsp.SampleRate * 2;
            var a = Dsp.Biquad.BandPass(650f, 0.9f);
            var b = Dsp.Biquad.BandPass(1800f, 1.2f);
            var noise = Dsp.Noise(n, rng);
            var y1 = Dsp.Periodic(noise, a.Process);
            var y2 = Dsp.Periodic(noise, b.Process);
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                float grain = (float)(0.7 + 0.3 * System.Math.Sin(2 * System.Math.PI * 37 * t) * System.Math.Sin(2 * System.Math.PI * 11 * t));
                y[i] = (y1[i] + 0.5f * y2[i]) * grain;
            }
            Dsp.NormalizeRms(y, 0.2f);
            return y;
        }

        private static float[] Loose()
        {
            var rng = new System.Random(303);
            int n = Dsp.SampleRate * 2;
            var low = Dsp.Biquad.LowPass(260f);
            var y = Dsp.Periodic(Dsp.Noise(n, rng), low.Process);
            float lowRms = Mathf.Max(1e-6f, Dsp.Rms(y));
            for (int i = 0; i < n; i++) y[i] /= lowRms;
            // Stones: short bright ticks at random times, wrapped around the loop.
            var ticks = new float[n];
            for (int k = 0; k < 260; k++)
            {
                int start = rng.Next(n);
                float level = (float)(0.3 + rng.NextDouble());
                int len = 220;
                for (int j = 0; j < len; j++) ticks[(start + j) % n] += level * (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-j / 40f);
            }
            var hp = Dsp.Biquad.HighPass(1500f);
            ticks = Dsp.Periodic(ticks, hp.Process);
            for (int i = 0; i < n; i++) y[i] += 1.2f * ticks[i];
            Dsp.NormalizeRms(y, 0.2f);
            return y;
        }

        private static float[] Road()
        {
            var rng = new System.Random(404);
            int n = (int)(Dsp.SampleRate * 2.5f);
            var lp = Dsp.Biquad.LowPass(420f);
            var mid = Dsp.Biquad.BandPass(1100f, 1.5f);
            var noise = Dsp.Noise(n, rng);
            var rumble = Dsp.Periodic(noise, lp.Process);
            var tone = Dsp.Periodic(noise, mid.Process);
            var y = new float[n];
            for (int i = 0; i < n; i++) y[i] = rumble[i] + 0.15f * tone[i];
            Dsp.NormalizeRms(y, 0.2f);
            return y;
        }

        private static float[] Wind()
        {
            var rng = new System.Random(505);
            int n = Dsp.SampleRate * 3;
            var lp = Dsp.Biquad.LowPass(1300f);
            var hp = Dsp.Biquad.HighPass(140f);
            var y = Dsp.Periodic(Dsp.Noise(n, rng), s => hp.Process(lp.Process(s)));
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                y[i] *= (float)(1 + 0.3 * System.Math.Sin(2 * System.Math.PI * t) + 0.2 * System.Math.Sin(2 * System.Math.PI * 3 * t + 2));
            }
            Dsp.NormalizeRms(y, 0.2f);
            return y;
        }

        // ---------------- One-shots ----------------

        private static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Dsp.SampleRate)];

        private static float[] Shift(int seed, bool race)
        {
            var rng = new System.Random(600 + seed);
            var x = Buffer(race ? 0.3f : 0.35f);
            Dsp.AddNoiseBurst(x, 0f, 0.0012f, 1f, Dsp.Biquad.HighPass(1500f), rng);
            Dsp.AddRing(x, 0.002f, race ? 140f : 105f + seed * 8f, race ? 0.025f : 0.045f, 0.8f);
            Dsp.AddRing(x, 0.004f, race ? 2300f : 1650f + seed * 60f, 0.05f, 0.18f);
            Dsp.AddRing(x, 0.004f, race ? 3400f : 2450f, 0.03f, 0.08f);
            if (race) Dsp.AddNoiseBurst(x, 0.01f, 0.06f, 0.25f, Dsp.Biquad.BandPass(4200f, 1.2f), rng);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.8f);
            return x;
        }

        private static float[] EvClick()
        {
            var x = Buffer(0.12f);
            Dsp.AddRing(x, 0f, 1900f, 0.012f, 0.6f);
            Dsp.AddRing(x, 0f, 600f, 0.02f, 0.3f);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.5f);
            return x;
        }

        private static float[] Pop(int seed)
        {
            var rng = new System.Random(700 + seed);
            var x = Buffer(0.3f);
            int count = 1 + seed % 3;
            for (int k = 0; k < count; k++)
            {
                float t = k * (0.035f + 0.02f * (float)rng.NextDouble());
                Dsp.AddNoiseBurst(x, t, 0.018f + 0.01f * (float)rng.NextDouble(), 1f - 0.25f * k, Dsp.Biquad.LowPass(1600f + 900f * (float)rng.NextDouble()), rng);
                Dsp.AddRing(x, t, 60f + 25f * (float)rng.NextDouble(), 0.03f, 0.6f - 0.15f * k);
            }
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.85f);
            return x;
        }

        private static float[] Thump(int seed)
        {
            var rng = new System.Random(800 + seed);
            var x = Buffer(0.25f);
            Dsp.AddRing(x, 0f, 48f + 10f * seed, 0.05f, 1f);
            Dsp.AddNoiseBurst(x, 0f, 0.02f, 0.4f, Dsp.Biquad.LowPass(500f), rng);
            Dsp.AddRing(x, 0.003f, 320f + 40f * seed, 0.015f, 0.12f);
            Dsp.FadeEdges(x);
            Dsp.NormalizePeak(x, 0.8f);
            return x;
        }

        private static float[] Impact(int severity, int variant)
        {
            var rng = new System.Random(900 + severity * 10 + variant);
            float length = severity == 1 ? 0.35f : severity == 2 ? 0.8f : 1.6f;
            var x = Buffer(length);
            switch (severity)
            {
                case 1:
                    Dsp.AddNoiseBurst(x, 0f, 0.006f, 0.8f, Dsp.Biquad.BandPass(2200f, 0.8f), rng);
                    Dsp.AddRing(x, 0f, 820f + 140f * variant, 0.04f, 0.5f);
                    Dsp.AddRing(x, 0f, 1730f + 200f * variant, 0.025f, 0.25f);
                    Dsp.AddRing(x, 0f, 140f, 0.03f, 0.4f);
                    break;
                case 2:
                    Dsp.AddRing(x, 0f, 68f + 8f * variant, 0.12f, 1f);
                    Dsp.AddNoiseBurst(x, 0f, 0.09f, 0.7f, Dsp.Biquad.LowPass(3200f), rng);
                    Dsp.AddRing(x, 0.005f, 610f + 70f * variant, 0.15f, 0.25f);
                    Dsp.AddRing(x, 0.005f, 1460f + 90f * variant, 0.12f, 0.15f);
                    Dsp.AddRing(x, 0.008f, 2730f, 0.08f, 0.08f);
                    break;
                default:
                    Dsp.AddRing(x, 0f, 44f + 6f * variant, 0.28f, 1f);
                    Dsp.AddNoiseBurst(x, 0f, 0.25f, 0.9f, Dsp.Biquad.LowPass(2600f), rng);
                    Dsp.AddRing(x, 0.004f, 470f + 50f * variant, 0.3f, 0.3f);
                    Dsp.AddRing(x, 0.004f, 1180f + 70f * variant, 0.22f, 0.2f);
                    // Debris: scattered small pings and rattles after the hit.
                    for (int k = 0; k < 18; k++)
                    {
                        float t = 0.05f + (float)rng.NextDouble() * 0.9f;
                        Dsp.AddRing(x, t, 1800f + (float)rng.NextDouble() * 3500f, 0.01f + 0.02f * (float)rng.NextDouble(), 0.05f + 0.1f * (float)rng.NextDouble());
                        Dsp.AddNoiseBurst(x, t, 0.004f, 0.12f, Dsp.Biquad.HighPass(2500f), rng);
                    }
                    break;
            }
            Dsp.FadeEdges(x, 1f, 60f);
            Dsp.NormalizePeak(x, severity == 1 ? 0.6f : severity == 2 ? 0.8f : 0.95f);
            return x;
        }

        // ---------------- Assets ----------------

        private static AudioClip Save(string relativePath, float[] samples, bool loop, bool engine = false)
        {
            string path = $"{ClipRoot}/{relativePath}.wav";
            VehiclePrefabBuilder.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AudioSignalAnalysis.WriteWav(Path.GetFullPath(path), samples, Dsp.SampleRate);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono = true;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
            // Engine loops stay PCM on desktop (cleanest pitch-shifting); everything else and mobile use ADPCM
            // (3.5:1, cheap to decode, sample-accurate loops).
            settings.compressionFormat = engine ? AudioCompressionFormat.PCM : AudioCompressionFormat.ADPCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            var mobile = settings;
            mobile.compressionFormat = AudioCompressionFormat.ADPCM;
            importer.SetOverrideSampleSettings("Android", mobile);
            importer.SetOverrideSampleSettings("iPhone", mobile);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static VehicleAudioProfile Profile(string name, EngineAudioSettings engine, ChassisAudioSettings chassis, string notes)
        {
            string path = $"{ProfileFolder}/{name}.asset";
            var p = AssetDatabase.LoadAssetAtPath<VehicleAudioProfile>(path);
            if (p == null) { p = ScriptableObject.CreateInstance<VehicleAudioProfile>(); AssetDatabase.CreateAsset(p, path); }
            p.EditorConfigure(engine, chassis, notes);
            EditorUtility.SetDirty(p);
            return p;
        }

        private static void Link(string definitionName, VehicleAudioProfile profile, StringBuilder log)
        {
            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>($"Assets/_NeonRift/Data/Vehicles/{definitionName}.asset");
            if (def == null) { log.AppendLine($"  missing definition {definitionName}"); return; }
            var so = new SerializedObject(def);
            so.FindProperty("audioProfile").objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  {definitionName} → {profile.name}");
        }
    }
}
