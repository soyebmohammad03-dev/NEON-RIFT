using System.Collections.Generic;
using System.Linq;
using NeonRift.Audio;
using NeonRift.EditorTools.Audio;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NeonRift.Tests
{
    /// <summary>Engine blend model, tyre model, generated clips, mixer set-up, and VehicleAudio driven by real bench telemetry.</summary>
    public class VehicleAudioTests
    {
        private static EngineAudioSettings FiveLayerEngine()
        {
            var rpms = new[] { 800f, 1700f, 3200f, 5000f, 7200f };
            var layers = new List<EngineSoundLayer>();
            foreach (bool on in new[] { true, false })
                foreach (float r in rpms) layers.Add(new EngineSoundLayer { recordedRpm = r, onLoad = on, volume = 1f });
            return new EngineAudioSettings { layers = layers.ToArray(), minPitch = 0.3f, maxPitch = 3f };
        }

        [Test]
        public void EngineModel_IsEqualPower_AndCrossfadesLoad()
        {
            var model = new EngineSoundModel(FiveLayerEngine());
            var v = new float[model.LayerCount];
            var p = new float[model.LayerCount];
            for (float rpm = 800f; rpm <= 7200f; rpm += 37f)
                foreach (float load in new[] { 0f, 0.3f, 0.7f, 1f })
                {
                    model.Evaluate(rpm, load, v, p);
                    float power = v.Sum(x => x * x);
                    Assert.That(power, Is.EqualTo(1f).Within(0.02f), $"{rpm} rpm load {load}");
                }
            model.Evaluate(3000f, 1f, v, p);
            Assert.That(Enumerable.Range(0, model.LayerCount).Where(i => !model.Layer(i).onLoad).Sum(i => v[i]), Is.LessThan(1e-4f), "full load plays no overrun layers");
            model.Evaluate(3000f, 0f, v, p);
            Assert.That(Enumerable.Range(0, model.LayerCount).Where(i => model.Layer(i).onLoad).Sum(i => v[i]), Is.LessThan(1e-4f), "closed throttle plays no load layers");
        }

        [Test]
        public void EngineModel_IsContinuous_AndPitchTracksRpm()
        {
            var model = new EngineSoundModel(FiveLayerEngine());
            var v = new float[model.LayerCount];
            var p = new float[model.LayerCount];
            var lastV = new float[model.LayerCount];
            model.Evaluate(800f, 1f, lastV, p);
            for (float rpm = 801f; rpm <= 7200f; rpm += 1f)
            {
                model.Evaluate(rpm, 1f, v, p);
                for (int i = 0; i < v.Length; i++)
                {
                    Assert.That(Mathf.Abs(v[i] - lastV[i]), Is.LessThan(0.01f), $"volume step at {rpm} rpm, layer {i}");
                    if (v[i] > 0.01f) Assert.That(p[i] * model.Layer(i).recordedRpm, Is.EqualTo(rpm).Within(0.5f), "pitch × recorded rpm = engine rpm");
                }
                System.Array.Copy(v, lastV, v.Length);
            }
        }

        [Test]
        public void TyreModel_SquealsOnlyWhenSlidingAndMoving()
        {
            var range = new Vector2(1.05f, 1.7f);
            Assert.That(TyreSoundModel.SlipIntensity(0.9f, range, 1f, 20f), Is.EqualTo(0f));
            Assert.That(TyreSoundModel.SlipIntensity(1.7f, range, 1f, 20f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(TyreSoundModel.SlipIntensity(3f, range, 1f, 0.5f), Is.EqualTo(0f), "no squeal at standstill");
            Assert.That(TyreSoundModel.SlipIntensity(1.4f, range, 1f, 20f), Is.InRange(0.2f, 0.8f));
        }

        private static IEnumerable<VehicleAudioProfile> Profiles() =>
            VehiclePerformanceProbe.CatalogVehicles().Select(v => v.AudioProfile).Distinct();

        [Test]
        public void CatalogVehicles_HaveValidAudioProfiles()
        {
            foreach (var v in VehiclePerformanceProbe.CatalogVehicles())
            {
                Assert.That(v.AudioProfile, Is.Not.Null, v.Id);
                Assert.That(v.AudioProfile.Validate(), Is.Empty, v.Id);
                Assert.That(v.AudioProfile.Engine.layers.Length, Is.GreaterThanOrEqualTo(6), $"{v.Id} needs several rpm points at two loads");
            }
            Assert.That(Profiles().Count(), Is.GreaterThanOrEqualTo(3), "cars have distinct engine sounds");
        }

        [Test]
        public void LoopingClips_AreSeamless_AndOneShotsStartAndEndSilent()
        {
            foreach (var profile in Profiles())
            {
                var e = profile.Engine;
                var c = profile.Chassis;
                var loops = e.layers.Select(l => l.clip).Concat(new[] { c.skidLoop, c.scrubLoop, c.looseSurfaceLoop, c.roadLoop, c.windLoop });
                foreach (var clip in loops.Where(x => x != null))
                {
                    var x = Data(clip);
                    Assert.That(AudioSignalAnalysis.LoopSeamScore(x), Is.LessThan(4f), $"{clip.name} loop seam");
                    Assert.That(x.Any(float.IsNaN), Is.False, clip.name);
                    Assert.That(AudioSignalAnalysis.Rms(x, 0, x.Length), Is.GreaterThan(0.05f), $"{clip.name} is silent");
                }
                var oneShots = (e.shiftClips ?? new AudioClip[0]).Concat(e.overrunPops ?? new AudioClip[0]).Concat(c.lightImpacts).Concat(c.mediumImpacts).Concat(c.heavyImpacts).Concat(c.suspensionThumps);
                foreach (var clip in oneShots.Where(x => x != null))
                {
                    var x = Data(clip);
                    Assert.That(Mathf.Abs(x[0]), Is.LessThan(0.01f), $"{clip.name} starts with a click");
                    Assert.That(Mathf.Abs(x[x.Length - 1]), Is.LessThan(0.01f), $"{clip.name} ends with a click");
                }
            }
        }

        private static float[] Data(AudioClip clip)
        {
            var x = new float[clip.samples * clip.channels];
            clip.GetData(x, 0);
            return x;
        }

        [Test]
        public void Mixer_HasGroupsSnapshotsAndVolumes()
        {
            var config = AssetDatabase.LoadAssetAtPath<AudioMixerConfig>(AudioMixerBuilder.ConfigPath);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Mixer, Is.Not.Null);
            foreach (var g in new[] { config.Engine, config.Tires, config.Sfx, config.Ambience, config.Music, config.UI }) Assert.That(g, Is.Not.Null);
            foreach (MixerState s in System.Enum.GetValues(typeof(MixerState)))
                Assert.That(config.Mixer.FindSnapshot(config.SnapshotName(s)), Is.Not.Null, s.ToString());
            foreach (AudioChannel c in System.Enum.GetValues(typeof(AudioChannel)))
                Assert.That(config.Mixer.GetFloat(config.VolumeParameter(c), out _), Is.True, c.ToString());
            var game = AssetDatabase.LoadAssetAtPath<NeonRift.Game.GameConfig>("Assets/_NeonRift/Data/Config/GameConfig.asset");
            Assert.That(game.AudioMixer, Is.SameAs(config));
        }

        private static IEnumerable<TestCaseData> Vehicles() =>
            VehiclePerformanceProbe.CatalogVehicles().Select(v => new TestCaseData(v).SetName($"{{m}}({v.Id})"));

        [TestCaseSource(nameof(Vehicles))]
        public void VehicleAudio_FollowsTelemetry_WithoutRestartingLoops(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            var audio = b.Vehicle.GetComponent<VehicleAudio>();
            Assert.That(audio, Is.Not.Null, "prefab has VehicleAudio");
            audio.Configure(v.AudioProfile, b.Vehicle);
            int plays = audio.PlayCalls;
            Assert.That(plays, Is.EqualTo(audio.LoopCount));
            Assert.That(audio.EngineSource(0).outputAudioMixerGroup, Is.Not.Null, "engine routed to the mixer");
            Assert.That(audio.EngineSource(0).loop, Is.True);

            void Drive(float seconds) => b.Run(seconds, null, () => audio.Tick(VehicleTestBench.Dt));

            b.SetInput();
            Drive(1.5f);
            float idleRpm = audio.AudioRpm;
            Assert.That(idleRpm, Is.EqualTo(b.Vehicle.Telemetry.EngineRpm).Within(50f), "starts at idle");
            Assert.That(audio.LoadBlend, Is.LessThan(0.1f));

            b.SetInput(throttle: 1f);
            Drive(6f);
            Assert.That(audio.LoadBlend, Is.GreaterThan(0.8f), "on-load character under throttle");
            Assert.That(audio.AudioRpm, Is.EqualTo(b.Vehicle.Telemetry.EngineRpm).Within(b.Vehicle.Telemetry.EngineRpm * 0.15f), "audio rpm tracks the engine");
            Assert.That(audio.EngineGain, Is.GreaterThan(0f));
            int maxLayer = 0;
            for (int i = 1; i < audio.EngineLayerCount; i++) if (audio.EngineSource(i).volume > audio.EngineSource(maxLayer).volume) maxLayer = i;
            Assert.That(v.AudioProfile.Engine.layers[maxLayer].onLoad, Is.True, "loudest layer is an on-load loop");

            b.SetInput();
            Drive(1f);
            Assert.That(audio.LoadBlend, Is.LessThan(0.2f), "lifting off switches to overrun");

            b.SetInput(brake: 1f);
            Drive(6f);
            b.SetInput();
            Drive(2f);
            Assert.That(audio.AudioRpm, Is.EqualTo(Mathf.Max(b.Vehicle.Drivetrain.IdleRpm, b.Vehicle.Telemetry.EngineRpm)).Within(150f), "back to idle");

            if (v.PhysicsProfile.Transmission.ForwardGearCount > 1) Assert.That(audio.ShiftSounds, Is.GreaterThan(0), "gear changes are heard");
            Assert.That(audio.PlayCalls, Is.EqualTo(plays), "no loop was restarted while driving");
        }

        [TestCaseSource(nameof(Vehicles))]
        public void VehicleAudio_SkidFollowsTyreSlip(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            var audio = b.Vehicle.GetComponent<VehicleAudio>();
            audio.Configure(v.AudioProfile, b.Vehicle);
            VehiclePerformanceProbe.Settle(b, 1f);
            b.AccelerateTo(80f);
            float target = 80f * VehicleUnits.KphToMs;
            b.Run(1.5f, null, () => { b.HoldSpeed(target); audio.Tick(VehicleTestBench.Dt); });
            Assert.That(audio.SkidLevel, Is.LessThan(0.05f), "no squeal cruising straight");
            Assert.That(audio.RoadLevel, Is.GreaterThan(0.2f), "road noise at speed");

            float peakSkid = 0f;
            b.SetInput(throttle: 0.3f, steer: 1f, handbrake: true);
            b.Run(1.5f, null, () => { audio.Tick(VehicleTestBench.Dt); peakSkid = Mathf.Max(peakSkid, audio.SkidLevel); });
            Assert.That(peakSkid, Is.GreaterThan(0.3f), "handbrake slide squeals");
        }
    }
}
