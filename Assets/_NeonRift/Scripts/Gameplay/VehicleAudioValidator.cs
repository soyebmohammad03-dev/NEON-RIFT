using System.Collections.Generic;
using System.Text;
using NeonRift.Audio;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Development tool: drives a vehicle through a scripted audio test (idle, launch, lift-off, braking to a stop,
    /// idle, handbrake slide, wall impact, recovery) through the normal input interface, records the listener mix and
    /// reports per-phase telemetry and signal measurements. Optionally writes the recording as a WAV.
    /// </summary>
    public sealed class VehicleAudioValidator : MonoBehaviour
    {
        private enum Phase { Idle, Launch, LiftOff, Brake, IdleAgain, SlideRunUp, Slide, ImpactRunUp, AfterImpact, Done }

        private sealed class PhaseStats
        {
            public Phase Phase;
            public float Start, End, RpmSum, AudioRpmSum, LoadSum, GainSum, SkidPeak, SpeedMax;
            public int Frames, StartSample, EndSample, PlayCallsAtStart, PlayCallsAtEnd;
            public readonly List<(float rpm, float hz)> PitchTrack = new();
        }

        private VehicleController vehicle;
        private VehicleAudio audioComponent;
        private AudioOutputRecorder recorder;
        private IVehicleInputSource previousInput;
        private readonly ScriptedDrivingInput input = new();
        private readonly List<PhaseStats> stats = new();
        private Phase phase = Phase.Done;
        private float phaseTime;
        private string wavPath;
        private int impactsAtStart;
        private int impactsBeforeRunUp;
        private readonly float[] pitchWindow = new float[6144];

        public bool Running => phase != Phase.Done;
        public string Report { get; private set; } = string.Empty;

        public void Begin(VehicleController target, string saveWavPath = null)
        {
            vehicle = target;
            audioComponent = target.GetComponent<VehicleAudio>();
            var listener = FindAnyObjectByType<AudioListener>();
            if (!listener.TryGetComponent(out recorder)) recorder = listener.gameObject.AddComponent<AudioOutputRecorder>();
            recorder.Begin(150f);
            previousInput = vehicle.InputSource;
            vehicle.SetInputSource(input);
            wavPath = saveWavPath;
            stats.Clear();
            impactsAtStart = audioComponent.ImpactSounds;
            Report = string.Empty;
            Enter(Phase.Idle);
        }

        private void Enter(Phase next)
        {
            if (stats.Count > 0) Close(stats[stats.Count - 1]);
            phase = next;
            phaseTime = 0f;
            if (next == Phase.Done) { Finish(); return; }
            stats.Add(new PhaseStats { Phase = next, Start = Time.time, StartSample = recorder.Written, PlayCallsAtStart = audioComponent.PlayCalls });
            if (next == Phase.ImpactRunUp)
            {
                // 25 m short of the test track's side crash wall (x = −30), driving straight at it.
                vehicle.Teleport(new Vector3(-5f, 0.05f, 250f), Quaternion.LookRotation(Vector3.left, Vector3.up));
                impactsBeforeRunUp = audioComponent.ImpactSounds;
            }
        }

        private void Close(PhaseStats s)
        {
            s.End = Time.time;
            s.EndSample = recorder.Written;
            s.PlayCallsAtEnd = audioComponent.PlayCalls;
        }

        private void Update()
        {
            if (phase == Phase.Done || vehicle == null) return;
            phaseTime += Time.deltaTime;
            var t = vehicle.Telemetry;
            var s = stats[stats.Count - 1];
            s.Frames++;
            s.RpmSum += t.EngineRpm;
            s.AudioRpmSum += audioComponent.AudioRpm;
            s.LoadSum += audioComponent.LoadBlend;
            s.GainSum += audioComponent.EngineGain;
            s.SkidPeak = Mathf.Max(s.SkidPeak, audioComponent.SkidLevel);
            s.SpeedMax = Mathf.Max(s.SpeedMax, t.SpeedKph);

            switch (phase)
            {
                case Phase.Idle:
                    input.Current = default;
                    if (phaseTime > 3f) Enter(Phase.Launch);
                    break;
                case Phase.Launch:
                    input.Current = new DrivingInput { Throttle = 1f };
                    TrackPitch(s, t);
                    // Speed-capped so every car stays on the 600 m start straight through lift-off and braking.
                    if (phaseTime > 9f || t.SpeedKph > 165f) Enter(Phase.LiftOff);
                    break;
                case Phase.LiftOff:
                    input.Current = default;
                    if (phaseTime > 2f) Enter(Phase.Brake);
                    break;
                case Phase.Brake:
                    input.Current = new DrivingInput { Brake = 1f };
                    if (t.Speed < 0.5f || phaseTime > 12f) Enter(Phase.IdleAgain);
                    break;
                case Phase.IdleAgain:
                    input.Current = new DrivingInput { Handbrake = true };
                    if (phaseTime > 3f) Enter(Phase.SlideRunUp);
                    break;
                case Phase.SlideRunUp:
                    input.Current = new DrivingInput { Throttle = 1f };
                    if (t.SpeedKph > 75f || phaseTime > 10f) Enter(Phase.Slide);
                    break;
                case Phase.Slide:
                    input.Current = new DrivingInput { Throttle = 0.3f, Steer = 1f, Handbrake = phaseTime < 1.2f };
                    if (phaseTime > 2.5f) Enter(Phase.ImpactRunUp);
                    break;
                case Phase.ImpactRunUp:
                    input.Current = new DrivingInput { Throttle = 0.7f };
                    if (audioComponent.ImpactSounds > impactsBeforeRunUp || phaseTime > 10f) Enter(Phase.AfterImpact);
                    break;
                case Phase.AfterImpact:
                    input.Current = default;
                    if (phaseTime > 2.5f)
                    {
                        vehicle.Recover();
                        Enter(Phase.Done);
                    }
                    break;
            }
        }

        private void TrackPitch(PhaseStats s, in VehicleTelemetry t)
        {
            if (s.PitchTrack.Count >= 40 || phaseTime < (s.PitchTrack.Count + 1) * 0.25f) return;
            if (!recorder.CopyLatest(pitchWindow)) return;
            float hz = AudioSignalAnalysis.Pitch(pitchWindow, 0, pitchWindow.Length, recorder.SampleRate, 25f, 700f);
            s.PitchTrack.Add((t.EngineRpm, hz));
        }

        private void Finish()
        {
            recorder.End();
            vehicle.SetInputSource(previousInput);
            var samples = recorder.Samples();
            int rate = recorder.SampleRate;
            var sb = new StringBuilder();
            sb.AppendLine($"[AudioValidation] {vehicle.Profile.name} / {audioComponent.Profile.name}: {samples.Length / (float)rate:0.0} s recorded @ {rate} Hz");
            sb.AppendLine($"  loops {audioComponent.LoopCount}, Play() calls {audioComponent.PlayCalls}, one-shots {audioComponent.OneShotCount} (shifts {audioComponent.ShiftSounds}, pops {audioComponent.PopSounds}, thumps {audioComponent.ThumpSounds}, impacts {audioComponent.ImpactSounds - impactsAtStart})");
            sb.AppendLine($"  whole-run click score {AudioSignalAnalysis.ClickScore(samples):0.0}");
            sb.AppendLine("  phase        secs  rpm(tel)  rpm(audio)  load  gain   out dBFS  bright  click  skidPeak  vmax  playCalls");
            foreach (var s in stats)
            {
                int count = Mathf.Max(1, s.EndSample - s.StartSample);
                var slice = new float[count];
                System.Array.Copy(samples, s.StartSample, slice, 0, Mathf.Min(count, samples.Length - s.StartSample));
                float rms = AudioSignalAnalysis.Rms(slice, 0, slice.Length);
                float frames = Mathf.Max(1, s.Frames);
                sb.AppendLine($"  {s.Phase,-12} {s.End - s.Start,4:0.0}  {s.RpmSum / frames,7:0}  {s.AudioRpmSum / frames,9:0}  {s.LoadSum / frames,5:0.00} {s.GainSum / frames,5:0.00}  {20f * Mathf.Log10(Mathf.Max(rms, 1e-6f)),8:0.0}  {AudioSignalAnalysis.Brightness(slice, 0, slice.Length),6:0.000} {AudioSignalAnalysis.ClickScore(slice),6:0.0}  {s.SkidPeak,8:0.00}  {s.SpeedMax,4:0}  {s.PlayCallsAtStart}->{s.PlayCallsAtEnd}");
                if (s.PitchTrack.Count > 0)
                    sb.AppendLine("    pitch track (rpm → Hz): " + string.Join("  ", s.PitchTrack.ConvertAll(p => $"{p.rpm:0}→{p.hz:0}")));
            }
            if (!string.IsNullOrEmpty(wavPath)) AudioSignalAnalysis.WriteWav(wavPath, samples, rate);
            Report = sb.ToString();
            Debug.Log(Report);
        }
    }
}
