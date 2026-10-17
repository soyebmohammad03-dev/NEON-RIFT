using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Generated audio for Harbor Rain (no third-party recordings exist in the project for rain, sea or radio):
    /// synthesised storm beds, wave impacts, water, ship horn, machinery, radio and device cues, and the command
    /// centre's voice lines, spoken by the macOS speech synthesiser (<c>say</c>) and run through a radio band-pass.
    /// Everything is written to Generated/Audio as 16-bit WAV and imported as Vorbis.
    /// </summary>
    public static class HarborRainAudio
    {
        private const string Folder = "Assets/_NeonRift/Levels/HarborRain/Generated/Audio";
        private const int Rate = 44100;

        public struct VoiceLine
        {
            public string Id, Speaker, Text;
            public HarborRadio.Priority Priority;
            public float Cooldown;
            public VoiceLine(string id, string speaker, HarborRadio.Priority p, string text, float cooldown = 0f)
            { Id = id; Speaker = speaker; Priority = p; Text = text; Cooldown = cooldown; }
        }

        private const HarborRadio.Priority R = HarborRadio.Priority.Routine, I = HarborRadio.Priority.Important, C = HarborRadio.Priority.Critical;

        /// <summary>The operation's radio script. CONTROL runs the job; OVERWATCH flies the crew's drone; PORT SECURITY is intercepted.</summary>
        public static readonly VoiceLine[] Lines =
        {
            new("start", "CONTROL", I, "Control to Driver. You're at the south perimeter of Kowloon Bay. Your pass opens the checkpoint. Keep your transponder quiet. We have eyes on the gate."),
            new("inside", "CONTROL", R, "You're in. The terminal office is up the spine, left on the terminal road. Manifest kiosk is outside the office."),
            new("manifest_open", "CONTROL", I, "Pull the manifest. KX-7 is hidden under a cover listing. Match all three clues. We get one clean read."),
            new("manifest_wrong", "CONTROL", I, "Wrong container. The port flagged the query. Read the clues again.", 6f),
            new("manifest_r1", "CONTROL", I, "Good read. KX-7 is in restricted bay R-1. Two patrols are working the container lanes."),
            new("manifest_r2", "CONTROL", I, "Good read. KX-7 is in restricted bay R-2. Two patrols are working the container lanes."),
            new("manifest_r3", "CONTROL", I, "Good read. KX-7 is in restricted bay R-3. Two patrols are working the container lanes."),
            new("rival", "OVERWATCH", I, "Overwatch here. Another crew just came in through the freight yard. They're after the same box."),
            new("rival_close", "OVERWATCH", C, "Rival crew is two hundred metres from the restricted area. Move."),
            new("optional_relay", "OVERWATCH", R, "If you have a minute, there's a drone relay on the berth apron. Spike it and their drones go blind for a while."),
            new("relay_done", "OVERWATCH", I, "Relay's down. Their drones are flying blind for forty-five seconds after the alarm."),
            new("gate_open", "CONTROL", I, "Security gate. Run the bypass. Lock each reel when it shows the badge code."),
            new("gate_fail", "CONTROL", R, "Reader kicked you out. Reset and go again.", 8f),
            new("gate_ok", "CONTROL", I, "Camera loop is live. You've got a short window before the system notices the feed."),
            new("lock_start", "CONTROL", I, "That's KX-7. Release the twistlocks, then hold the seal signal steady while the spreader lifts."),
            new("lock_fail", "CONTROL", R, "Signal dropped. Steady it.", 8f),
            new("theft", "CONTROL", C, "Cargo confirmed. The alarm is live. Port security has your position. Get to the east gate."),
            new("spotted", "OVERWATCH", I, "You've been seen. Break line of sight.", 25f),
            new("alarm_early", "CONTROL", C, "Alarm's up early. The lockdown clock is running. Finish the job, fast."),
            new("security_chatter", "PORT SECURITY", R, "All units, theft in the restricted area. Seal the terminal. Unit four, block the quay road.", 0f),
            new("blockade", "OVERWATCH", C, "Driver, hold. Drone feed shows a patrol blocking the road ahead. Sending you a new route now."),
            new("reroute", "CONTROL", I, "New route on your map. Follow the line.", 14f),
            new("no_route", "CONTROL", C, "Every road east is closed. Push through or wait for the water to drop.", 20f),
            new("flood_close", "CONTROL", I, "The low dock road is going under. Routing you around it."),
            new("checkpoint_sealed", "CONTROL", I, "Lane eighty checkpoint just sealed. Routing you around."),
            new("drone_search", "OVERWATCH", R, "Drone sweep over your sector. Stay under the cranes if you can.", 30f),
            new("drone_lock", "OVERWATCH", I, "Drone has a lock. That speeds up their lockdown. Get under cover.", 30f),
            new("override", "CONTROL", C, "East gate is in lockdown. Get to the junction box and reroute the power."),
            new("override_fail", "CONTROL", R, "Breaker tripped. Re-route it.", 8f),
            new("override_ok", "CONTROL", I, "Gate's open. Out onto the coast road."),
            new("coast", "CONTROL", C, "Storm surge warning. Waves are overtopping the outer seawall. Do not enter the exposed section until the water drops."),
            new("bypass", "CONTROL", R, "If you don't like the sea road, the inland service road is dry. It's longer."),
            new("wave_warn", "OVERWATCH", I, "Big set rolling in. Hold short of the marked section.", 18f),
            new("wave_clear", "OVERWATCH", I, "Water's draining. Go now.", 18f),
            new("wave_hit", "CONTROL", R, "You took that wave. Ease off and let the tyres find the road.", 20f),
            new("extraction", "CONTROL", I, "Extraction is open. One clear approach. Bring it home."),
            new("extracted", "CONTROL", I, "Cargo's through. Clean work, Driver. Control out."),
            new("failed", "CONTROL", C, "That's lockdown. Abort. We'll find another way out."),
            // Harbor Rain 2.0: the port police (DISPATCH and the units, intercepted) and CONTROL's pursuit calls.
            new("police_dispatch", "PORT POLICE", C, "All units, all units. Cargo theft at the restricted container area. Suspect vehicle leaving the scene. Respond code three."),
            new("police_pursuit_east", "PORT POLICE", I, "Kilo one, in pursuit. Suspect heading east.", 25f),
            new("police_pursuit_west", "PORT POLICE", I, "Kilo one, in pursuit. Suspect heading west.", 25f),
            new("police_pursuit_north", "PORT POLICE", I, "Kilo one, in pursuit. Suspect heading north, toward the quay.", 25f),
            new("police_pursuit_south", "PORT POLICE", I, "Kilo one, in pursuit. Suspect heading south.", 25f),
            new("police_intercept", "PORT POLICE", I, "Kilo three. I'm cutting across to block the next junction.", 30f),
            new("police_lost", "PORT POLICE", I, "Dispatch, we've lost visual. All units, search the surrounding roads.", 30f),
            new("police_reacquired", "PORT POLICE", I, "Visual reacquired. Suspect is back on the road. Close in.", 30f),
            new("police_coast", "PORT POLICE", I, "Suspect is out on the coast road. Follow them along the seawall. Watch the water.", 0f),
            new("police_close", "CONTROL", R, "Police right on your tail. Keep moving, use the corners.", 45f),
            new("police_lost_control", "CONTROL", R, "You're out of their sight. Stay off the main roads and they'll lose you.", 45f),
            new("police_standdown", "PORT POLICE", R, "All units, suspect has left the port by sea. Stand down.", 0f),
            new("crane_align", "CONTROL", R, "Gantry's moving. Spreader coming over KX-7. Hold still."),
            new("crane_locked", "OVERWATCH", R, "All four corners locked. Taking the weight."),
            // Campaign pass: the line the mission already asks for when KX-7 lands on the crew truck (it was never scripted).
            new("crane_delivered", "CONTROL", I, "KX-7 is on the crew truck. It leaves by the north gate once you're clear."),
        };

        private static string VoiceFor(string speaker) => speaker switch
        {
            "OVERWATCH" => "Karen",
            "PORT SECURITY" => "Moira",
            "PORT POLICE" => "Ralph",
            _ => "Daniel"
        };

        public sealed class Clips
        {
            public AudioClip RainHeavy, RainMetal, Wind, WaterLap, WaterRush, Generator, Machinery, TyreSpray, PowerHum;
            public AudioClip[] Gusts, WaveImpacts, Thunder;
            public AudioClip ShipHorn, SquelchIn, SquelchOut, Static, DeviceNotify, DeviceCritical;
            public AudioClip UiTick, UiConfirm, UiError, UiSuccess, Spark, GateClunk, LockRelease, WaveRoar, Hydraulic, MetalImpact, SurgeHorn, Drain;
            public AudioClip PoliceYelp, TwistClack, CraneMotor, CableStrain, AlarmBell, ReverseBeep;
            public readonly Dictionary<string, AudioClip> Voice = new();
        }

        public static Clips Build(StringBuilder report)
        {
            Directory.CreateDirectory(Folder);
            var rng = new Random(23);
            var c = new Clips
            {
                RainHeavy = Loop("HR_RainHeavy", 10f, x => Rain(x, rng, 1f)),
                RainMetal = Loop("HR_RainOnMetal", 6f, x => RainMetal(x, rng)),
                Wind = Loop("HR_WindLoop", 12f, x => Wind(x, rng)),
                WaterLap = Loop("HR_WaterLap", 9f, x => Lap(x, rng)),
                WaterRush = Loop("HR_WaterRush", 6f, x => Rush(x, rng)),
                Generator = Loop("HR_GeneratorHum", 4f, x => Hum(x, rng, 50.0, 0.3)),
                PowerHum = Loop("HR_PowerHum", 3f, x => Hum(x, rng, 100.0, 0.15)),
                Machinery = Loop("HR_Machinery", 8f, x => Machinery(x, rng)),
                TyreSpray = Loop("HR_TyreSpray", 4f, x => Spray(x, rng)),
                Gusts = new[] { One("HR_Gust0", 4.5f, x => Gust(x, rng, 0)), One("HR_Gust1", 5.5f, x => Gust(x, rng, 1)), One("HR_Gust2", 3.8f, x => Gust(x, rng, 2)) },
                WaveImpacts = new[] { One("HR_WaveImpact0", 6f, x => Wave(x, rng, 0)), One("HR_WaveImpact1", 7f, x => Wave(x, rng, 1)) },
                Thunder = new[] { One("HR_ThunderNear", 7f, x => Thunder(x, rng, 0)), One("HR_ThunderRoll", 9f, x => Thunder(x, rng, 1)), One("HR_ThunderFar", 8f, x => Thunder(x, rng, 2)) },
                ShipHorn = One("HR_ShipHorn", 5f, Horn),
                SquelchIn = One("HR_SquelchIn", 0.22f, x => Squelch(x, rng, true)),
                SquelchOut = One("HR_SquelchOut", 0.3f, x => Squelch(x, rng, false)),
                Static = One("HR_StaticBurst", 0.5f, x => StaticBurst(x, rng)),
                DeviceNotify = One("HR_DeviceNotify", 0.9f, x => Device(x, false)),
                DeviceCritical = One("HR_DeviceCritical", 1.3f, x => Device(x, true)),
                UiTick = One("HR_UiTick", 0.06f, x => Blip(x, 2200, 0.05)),
                UiConfirm = One("HR_UiConfirm", 0.25f, x => Chirp(x, 900, 1600)),
                UiError = One("HR_UiError", 0.45f, x => Buzz(x)),
                UiSuccess = One("HR_UiSuccess", 0.8f, Success),
                Spark = One("HR_Spark", 1.2f, x => Spark(x, rng)),
                GateClunk = One("HR_GateClunk", 1.4f, x => Clunk(x, rng)),
                LockRelease = One("HR_LockRelease", 0.7f, x => Clunk(x, rng, 0.4)),
                WaveRoar = One("HR_WaveRoar", 4.5f, x => Roar(x, rng)),
                Hydraulic = One("HR_Hydraulic", 3f, x => Hydraulic(x, rng)),
                MetalImpact = One("HR_MetalImpact", 2f, x => MetalImpact(x, rng)),
                SurgeHorn = One("HR_SurgeHorn", 3.2f, SurgeHorn),
                Drain = Loop("HR_DrainGurgle", 6f, x => Drain(x, rng)),
                PoliceYelp = Loop("HR_PoliceYelp", 4.5f, Yelp),
                TwistClack = One("HR_TwistlockClack", 0.6f, x => TwistClack(x, rng)),
                CraneMotor = Loop("HR_CraneMotor", 6f, x => CraneMotor(x, rng)),
                CableStrain = One("HR_CableStrain", 2.4f, x => CableStrain(x, rng)),
                AlarmBell = Loop("HR_AlarmBell", 3f, AlarmBell),
                ReverseBeep = Loop("HR_ReverseBeep", 2f, x => { for (int i = 0; i < x.Length; i++) { double t = i / (double)Rate; x[i] = (float)((t % 1.0) < 0.5 ? Math.Sin(2 * Math.PI * 1150 * t) * 0.6 : 0); } }),
            };

            int spoken = 0, missing = 0;
            foreach (var line in Lines)
            {
                var clip = Speak(line);
                if (clip != null) { c.Voice[line.Id] = clip; spoken++; } else missing++;
            }
            report.AppendLine($"audio: 38 generated clips, {spoken}/{Lines.Length} radio lines spoken ({missing} missing) — voices Daniel (CONTROL), Karen (OVERWATCH), Moira (PORT SECURITY), Ralph (PORT POLICE)");
            return c;
        }

        // ---------------- Voice ----------------

        /// <summary>Generates one radio line's clip (same voice and radio processing as a full build).</summary>
        public static AudioClip SpeakLine(VoiceLine line) => Speak(line);

        private static AudioClip Speak(VoiceLine line)
        {
            string path = $"{Folder}/HR_VO_{line.Id}.wav";
            string raw = Path.Combine(Path.GetTempPath(), $"hr_vo_{line.Id}.wav");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/say",
                    $"-v {VoiceFor(line.Speaker)} -r 188 -o \"{raw}\" --file-format=WAVE --data-format=LEI16@22050 \"{line.Text.Replace("\"", "")}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit(20000);
                if (!File.Exists(raw)) return null;
                var (samples, rate) = ReadWav(raw);
                File.Delete(raw);
                // Radio: band-pass 320 Hz–3.2 kHz, soft clip, a faint noise bed and 120 ms lead-in.
                var rng = new Random(line.Id.GetHashCode());
                int lead = rate / 8;
                var x = new float[samples.Length + lead * 2];
                double hp = 0, hpPrev = 0, lp = 0, lp2 = 0;
                double aHp = Math.Exp(-2 * Math.PI * 320 / rate), aLp = 1 - Math.Exp(-2 * Math.PI * 3200 / rate);
                for (int i = 0; i < x.Length; i++)
                {
                    double s = i >= lead && i - lead < samples.Length ? samples[i - lead] : 0;
                    hp = aHp * (hp + s - hpPrev); hpPrev = s;
                    lp += (hp - lp) * aLp; lp2 += (lp - lp2) * aLp;
                    double v = Math.Tanh(lp2 * 2.2) * 0.8 + (rng.NextDouble() * 2 - 1) * 0.012;
                    x[i] = (float)v;
                }
                return Write(path, x, rate, false, 0.85f);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HarborRain] voice line {line.Id} failed: {e.Message}");
                return null;
            }
        }

        private static (float[] samples, int rate) ReadWav(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int rate = BitConverter.ToInt32(bytes, 24);
            int pos = 12;
            while (pos + 8 < bytes.Length)
            {
                string id = Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                if (id == "data")
                {
                    var s = new float[size / 2];
                    for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToInt16(bytes, pos + 8 + i * 2) / 32768f;
                    return (s, rate);
                }
                pos += 8 + size;
            }
            return (new float[0], rate);
        }

        // ---------------- Synthesis ----------------

        private static double N(Random r) => r.NextDouble() * 2 - 1;

        private static void Rain(float[] x, Random r, double heavy)
        {
            // Dense pink-ish hiss + thousands of tiny droplet transients + a low body.
            double b0 = 0, b1 = 0, b2 = 0, low = 0, hp = 0, prev = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double w = N(r);
                b0 = 0.99765 * b0 + w * 0.0990460; b1 = 0.96300 * b1 + w * 0.2965164; b2 = 0.57000 * b2 + w * 1.0526913;
                double pink = (b0 + b1 + b2 + w * 0.1848) * 0.18;
                hp = 0.985 * (hp + pink - prev); prev = pink;
                low += (w - low) * 0.012;
                x[i] = (float)(hp * 0.9 + low * 2.4 * heavy);
            }
            int drops = (int)(x.Length / Rate * 900 * heavy);
            for (int d = 0; d < drops; d++)
            {
                int at = r.Next(x.Length);
                double f = 1800 + r.NextDouble() * 4500, amp = 0.04 + r.NextDouble() * 0.14, decay = 280 + r.NextDouble() * 500;
                for (int k = 0; k < 300 && at + k < x.Length; k++) x[at + k] += (float)(Math.Sin(2 * Math.PI * f * k / Rate) * amp * Math.Exp(-k / (double)Rate * decay));
            }
        }

        private static void RainMetal(float[] x, Random r)
        {
            Rain(x, r, 0.4);
            for (int i = 0; i < x.Length; i++) x[i] *= 0.4f;
            int pings = x.Length / Rate * 380;
            for (int d = 0; d < pings; d++)
            {
                int at = r.Next(x.Length);
                double f = 2600 + r.NextDouble() * 5200, amp = 0.08 + r.NextDouble() * 0.22;
                for (int k = 0; k < 2200 && at + k < x.Length; k++)
                {
                    double t = k / (double)Rate;
                    x[at + k] += (float)((Math.Sin(2 * Math.PI * f * t) + 0.5 * Math.Sin(2 * Math.PI * f * 1.73 * t)) * amp * Math.Exp(-t * 60));
                }
            }
        }

        private static void Wind(float[] x, Random r)
        {
            double a = 0, b = 0, phase = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double env = 0.55 + 0.3 * Math.Sin(2 * Math.PI * t / x.Length * Rate * 2) + 0.15 * Math.Sin(2 * Math.PI * t * 0.37);
                double cutoff = 0.004 + 0.012 * env;
                a += (N(r) - a) * cutoff;
                b += (a - b) * cutoff * 1.5;
                phase += 2 * Math.PI * (180 + 120 * env) / Rate;
                x[i] = (float)(b * 26 * env + Math.Sin(phase) * 0.02 * env * env);
            }
        }

        private static void Gust(float[] x, Random r, int k)
        {
            double a = 0, b = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)x.Length;
                double env = Math.Sin(Math.PI * Math.Pow(t, 0.7)) * (1 + 0.3 * Math.Sin(t * (11 + k * 3)));
                double cutoff = 0.006 + 0.03 * env;
                a += (N(r) - a) * cutoff;
                b += (a - b) * cutoff;
                x[i] = (float)(b * 22 * env);
            }
        }

        private static void Lap(float[] x, Random r)
        {
            double a = 0, b = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double slosh = Math.Pow(Math.Max(0, Math.Sin(2 * Math.PI * t * 0.45) * 0.6 + Math.Sin(2 * Math.PI * t * 0.71 + 1) * 0.4), 2);
                a += (N(r) - a) * (0.02 + 0.05 * slosh);
                b += (a - b) * 0.06;
                x[i] = (float)(b * 8 * (0.25 + slosh) + a * 0.3 * slosh);
            }
        }

        private static void Rush(float[] x, Random r)
        {
            double a = 0, b = 0, g = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                a += (N(r) - a) * 0.08;
                b += (a - b) * 0.2;
                g += (N(r) - g) * 0.0006;
                x[i] = (float)(b * (1.2 + Math.Sin(t * 17) * 0.2 + g * 6) + N(r) * 0.05);
            }
        }

        private static void Wave(float[] x, Random r, int k)
        {
            // A deep thump, a crashing body of noise and a long hissing wash-back.
            double a = 0, b = 0, c = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double boom = Math.Sin(2 * Math.PI * (38 + 20 * Math.Exp(-t * 6)) * t) * Math.Exp(-t * 2.2) * Math.Min(1, t * 40);
                a += (N(r) - a) * 0.05;
                b += (a - b) * 0.12;
                double crash = b * 7 * Math.Exp(-Math.Max(0, t - 0.05) * (1.4 + k * 0.3)) * Math.Min(1, t * 12);
                c += (N(r) - c) * 0.4;
                double hiss = c * 0.3 * Math.Max(0, Math.Sin(Math.PI * Math.Min(1, (t - 0.6) / (x.Length / (double)Rate - 0.6))));
                x[i] = (float)(boom * 1.4 + crash + hiss);
            }
        }

        /// <summary>A wave set building and rolling in: a rising low roar with surf noise swelling to a peak.</summary>
        private static void Roar(float[] x, Random r)
        {
            double a = 0, b = 0, c = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)x.Length;
                double env = Math.Pow(t, 1.6) * (1 - Math.Pow(t, 12));
                a += (N(r) - a) * (0.01 + 0.05 * t);
                b += (a - b) * 0.05;
                c += (N(r) - c) * 0.3;
                x[i] = (float)((b * 9 + c * 0.25 * t) * env + Math.Sin(2 * Math.PI * (30 + 25 * t) * i / Rate) * 0.3 * env);
            }
        }

        /// <summary>Crane hoist: a hydraulic whine sweeping up over a motor drone, with a valve hiss at the start.</summary>
        private static void Hydraulic(float[] x, Random r)
        {
            double ph = 0, a = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate, u = i / (double)x.Length;
                double env = Math.Min(1, t * 6) * Math.Min(1, (1 - u) * 5);
                ph += 2 * Math.PI * (180 + 260 * u) / Rate;
                a += (N(r) - a) * 0.25;
                double hiss = t < 0.4 ? a * Math.Exp(-t * 6) * 0.8 : 0;
                x[i] = (float)((Math.Sin(ph) * 0.35 + Math.Sin(ph * 2.01) * 0.2 + Math.Sin(2 * Math.PI * 55 * t) * 0.35) * env + hiss + a * 0.05 * env);
            }
        }

        /// <summary>Steel on steel: a heavy clank with a ringing tail (twistlocks, the spreader taking the load).</summary>
        private static void MetalImpact(float[] x, Random r)
        {
            double[] modes = { 187, 431, 772, 1230, 1890 };
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate, v = 0;
                for (int m = 0; m < modes.Length; m++) v += Math.Sin(2 * Math.PI * modes[m] * t) * Math.Exp(-t * (3 + m * 2)) / (m + 1);
                v += N(r) * Math.Exp(-t * 40) * 0.8;
                x[i] = (float)v;
            }
        }

        private static void Thunder(float[] x, Random r, int k)
        {
            double a = 0, b = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double w = N(r);
                a += (w - a) * (k == 0 ? 0.05 : 0.025);
                b += (a - b) * 0.08;
                double crack = k == 0 && t < 0.3 ? Math.Exp(-t * 14) * w * 0.8 : 0;
                double env = Math.Min(1, t * (k == 2 ? 2 : 8)) * Math.Exp(-t * (0.45 + k * 0.08)) * (1 + 0.6 * Math.Sin(t * (2.3 + k)) * Math.Sin(t * 0.9));
                double rumble = Math.Sin(2 * Math.PI * (32 + k * 6) * t) * env * 0.35;
                x[i] = (float)(b * 10 * env + crack + rumble);
            }
        }

        private static void Horn(float[] x)
        {
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double env = Math.Min(1, t * 3) * Math.Min(1, (x.Length / (double)Rate - t) * 1.5);
                double f = 73 * (1 + 0.004 * Math.Sin(2 * Math.PI * 5 * t));
                double v = 0;
                for (int h = 1; h <= 9; h++) v += Math.Sin(2 * Math.PI * f * h * t) / Math.Pow(h, 0.9);
                x[i] = (float)(Math.Tanh(v * 0.8) * env);
            }
        }

        /// <summary>Seawall surge warning: a two-tone electronic horn (three hi-lo cycles), slightly distorted.</summary>
        private static void SurgeHorn(float[] x)
        {
            double ph = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double f = (t % 1.0) < 0.5 ? 620 : 470;
                ph += 2 * Math.PI * f / Rate;
                double env = Math.Min(1, t * 20) * Math.Min(1, (x.Length / (double)Rate - t) * 4);
                double v = Math.Sin(ph) + Math.Sin(ph * 2) * 0.35 + Math.Sin(ph * 3) * 0.2;
                x[i] = (float)(Math.Tanh(v * 1.6) * env * 0.8);
            }
        }

        /// <summary>Water draining through grates and scuppers: trickling noise with irregular gulps.</summary>
        private static void Drain(float[] x, Random r)
        {
            double a = 0, b = 0, gulp = 0, gph = 0, gf = 0;
            for (int i = 0; i < x.Length; i++)
            {
                a += (N(r) - a) * 0.12;
                b += (a - b) * 0.3;
                if (gulp < 0.001 && r.NextDouble() < 0.00012) { gulp = 1; gf = 180 + r.NextDouble() * 260; }
                gulp *= 0.99965;
                gph += 2 * Math.PI * gf * (1 + gulp * 0.6) / Rate;
                x[i] = (float)(b * 0.6 + Math.Sin(gph) * gulp * 0.35 + N(r) * 0.02);
            }
        }

        private static void Hum(float[] x, Random r, double f, double noise)
        {
            double a = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                a += (N(r) - a) * 0.03;
                x[i] = (float)(Math.Sin(2 * Math.PI * f * t) * 0.5 + Math.Sin(2 * Math.PI * f * 2 * t) * 0.3 + Math.Sin(2 * Math.PI * f * 3 * t) * 0.12 + a * noise * 4);
            }
        }

        private static void Machinery(float[] x, Random r)
        {
            Hum(x, r, 31.0, 0.6);
            for (int i = 0; i < x.Length; i++) x[i] *= 0.5f;
            // Clanks every ~0.8 s (a conveyor / crane winch), slightly irregular.
            for (double t = 0.2; t < x.Length / (double)Rate - 0.4; t += 0.75 + r.NextDouble() * 0.2)
            {
                int at = (int)(t * Rate);
                double f = 280 + r.NextDouble() * 200;
                for (int k = 0; k < Rate / 3 && at + k < x.Length; k++)
                {
                    double tt = k / (double)Rate;
                    x[at + k] += (float)((Math.Sin(2 * Math.PI * f * tt) * 0.6 + N(r) * 0.4) * Math.Exp(-tt * 22) * 0.9);
                }
            }
        }

        private static void Spray(float[] x, Random r)
        {
            double hp = 0, prev = 0, a = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double w = N(r);
                hp = 0.9 * (hp + w - prev); prev = w;
                a += (hp - a) * 0.5;
                x[i] = (float)(a * (0.8 + 0.2 * Math.Sin(i / (double)Rate * 23)));
            }
        }

        private static void Squelch(float[] x, Random r, bool open)
        {
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)x.Length;
                double env = open ? Math.Exp(-t * 6) : Math.Exp(-t * 4) * Math.Min(1, t * 20);
                x[i] = (float)((N(r) * 0.6 + Math.Sin(2 * Math.PI * (open ? 1400 : 900) * i / Rate) * 0.3) * env);
            }
        }

        private static void StaticBurst(float[] x, Random r)
        {
            double a = 0;
            for (int i = 0; i < x.Length; i++)
            {
                a += (N(r) - a) * 0.6;
                double env = Math.Sin(Math.PI * i / x.Length) * (r.NextDouble() < 0.02 ? 1.6 : 1);
                x[i] = (float)(a * env);
            }
        }

        private static void Device(float[] x, bool critical)
        {
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate, v = 0;
                // Two vibration pulses (low buzz) then a chime (two notes; three rising for critical).
                double buzz = (t < 0.12 || t > 0.18 && t < 0.3) ? Math.Sign(Math.Sin(2 * Math.PI * 160 * t)) * 0.18 : 0;
                double[] notes = critical ? new[] { 880.0, 1175, 1568 } : new[] { 1046.5, 1568 };
                for (int n = 0; n < notes.Length; n++)
                {
                    double s = 0.34 + n * 0.14;
                    if (t >= s) v += Math.Sin(2 * Math.PI * notes[n] * (t - s)) * Math.Exp(-(t - s) * 7) * 0.5;
                }
                x[i] = (float)(buzz + v);
            }
        }

        private static void Blip(float[] x, double f, double decay)
        {
            for (int i = 0; i < x.Length; i++) { double t = i / (double)Rate; x[i] = (float)(Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t / decay * 3)); }
        }

        private static void Chirp(float[] x, double f0, double f1)
        {
            double ph = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)x.Length;
                ph += 2 * Math.PI * (f0 + (f1 - f0) * t) / Rate;
                x[i] = (float)(Math.Sin(ph) * Math.Sin(Math.PI * t));
            }
        }

        private static void Buzz(float[] x)
        {
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double gate = (t < 0.18 || t > 0.24) ? 1 : 0;
                x[i] = (float)(Math.Tanh(Math.Sin(2 * Math.PI * 140 * t) * 4) * 0.5 * gate * Math.Exp(-t * 2));
            }
        }

        private static void Success(float[] x)
        {
            double[] notes = { 659.3, 880, 1318.5 };
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate, v = 0;
                for (int n = 0; n < notes.Length; n++)
                {
                    double s = n * 0.1;
                    if (t >= s) v += Math.Sin(2 * Math.PI * notes[n] * (t - s)) * Math.Exp(-(t - s) * 4) * 0.4;
                }
                x[i] = (float)v;
            }
        }

        private static void Spark(float[] x, Random r)
        {
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double crackle = r.NextDouble() < 0.02 * Math.Exp(-t * 3) ? N(r) * 3 : 0;
                x[i] = (float)(crackle + N(r) * 0.25 * Math.Exp(-t * 5) + Math.Sin(2 * Math.PI * 120 * t) * 0.2 * Math.Exp(-t * 2));
            }
        }

        private static void Clunk(float[] x, Random r, double hi = 1)
        {
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                x[i] = (float)(Math.Sin(2 * Math.PI * 70 * t) * Math.Exp(-t * 8) + (N(r) * 0.5 + Math.Sin(2 * Math.PI * 410 * t) * 0.4) * Math.Exp(-t * 26) * hi);
            }
        }

        /// <summary>Police yelp: a fast up-down sweep (650 Hz to 1.5 kHz, 3.3 per second) on a buzzy horn-speaker tone.</summary>
        private static void Yelp(float[] x)
        {
            double ph = 0;
            int n = x.Length;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                // A whole number of sweeps over the clip so the loop is seamless.
                double cycles = Math.Round(n / (double)Rate * 3.3);
                double u = (i / (double)n * cycles) % 1.0;
                double tri = u < 0.5 ? u * 2 : 2 - u * 2;
                double f = 650 + 850 * tri;
                ph += 2 * Math.PI * f / Rate;
                double v = Math.Sin(ph) + Math.Sin(ph * 3) * 0.28 + Math.Sin(ph * 5) * 0.12;
                x[i] = (float)(Math.Tanh(v * 2.2) * 0.7);
            }
        }

        /// <summary>A twistlock turning home in a corner casting: a bright steel click with a short ring.</summary>
        private static void TwistClack(float[] x, Random r)
        {
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double click = N(r) * Math.Exp(-t * 180) * 0.9;
                double ring = (Math.Sin(2 * Math.PI * 1840 * t) * 0.5 + Math.Sin(2 * Math.PI * 2970 * t) * 0.3) * Math.Exp(-t * 22);
                double thud = Math.Sin(2 * Math.PI * 120 * t) * Math.Exp(-t * 30) * 0.6;
                x[i] = (float)(click + ring * 0.6 + thud);
            }
        }

        /// <summary>Crane drive: an electric motor whine with gear mesh and a little rumble (loops while the gantry, trolley or hoist moves).</summary>
        private static void CraneMotor(float[] x, Random r)
        {
            double lp = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                lp += (N(r) - lp) * 0.02;
                double whine = Math.Sin(2 * Math.PI * 240 * t) * 0.35 + Math.Sin(2 * Math.PI * 480 * t) * 0.18 + Math.Sin(2 * Math.PI * 1320 * t) * 0.05;
                double gear = Math.Sin(2 * Math.PI * 62 * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 8 * t)) * 0.25;
                x[i] = (float)(whine + gear + lp * 1.4);
            }
        }

        /// <summary>Wire ropes taking a heavy load: low groaning creaks and a steel twang.</summary>
        private static void CableStrain(float[] x, Random r)
        {
            double ph = 0, lp = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double f = 95 + 40 * Math.Sin(2 * Math.PI * 1.3 * t) + 25 * Math.Sin(2 * Math.PI * 4.1 * t);
                ph += 2 * Math.PI * f / Rate;
                lp += (N(r) - lp) * 0.05;
                double env = Math.Min(1, t * 6) * Math.Exp(-t * 0.9);
                double creak = Math.Tanh(Math.Sin(ph) * 3 + lp * 2) * env * 0.5;
                double twang = Math.Sin(2 * Math.PI * 310 * t) * Math.Exp(-t * 3) * (t < 0.05 ? t / 0.05 : 1) * 0.35;
                x[i] = (float)(creak + twang);
            }
        }

        /// <summary>Port security alarm bell: a 9 Hz electric bell striker on a 1.1 kHz gong.</summary>
        private static void AlarmBell(float[] x)
        {
            double env = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                if (i % (Rate / 9) == 0) env = 1;
                env *= 0.99975;
                double v = Math.Sin(2 * Math.PI * 1120 * t) * 0.6 + Math.Sin(2 * Math.PI * 2760 * t) * 0.25 + Math.Sin(2 * Math.PI * 4230 * t) * 0.1;
                x[i] = (float)(v * env * 0.8);
            }
        }

        // ---------------- Files ----------------

        private static AudioClip Loop(string name, float seconds, Action<float[]> fill)
        {
            var x = new float[(int)(Rate * seconds)];
            fill(x);
            int fade = Rate / 2;
            for (int i = 0; i < fade; i++) { float t = i / (float)fade; x[i] = x[i] * t + x[x.Length - fade + i] * (1 - t); }
            Array.Resize(ref x, x.Length - fade);
            return Write($"{Folder}/{name}.wav", x, Rate, true, 0.9f);
        }

        private static AudioClip One(string name, float seconds, Action<float[]> fill)
        {
            var x = new float[(int)(Rate * seconds)];
            fill(x);
            int fade = Math.Min(x.Length / 10, Rate / 20);
            for (int i = 0; i < fade; i++) x[x.Length - 1 - i] *= i / (float)fade;
            return Write($"{Folder}/{name}.wav", x, Rate, false, 0.92f);
        }

        /// <summary>Writes peak-normalised 16-bit mono PCM and imports it (Vorbis; streamed for long beds).</summary>
        private static AudioClip Write(string path, float[] samples, int rate, bool loop, float peakTarget)
        {
            float peak = 0f;
            foreach (var s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            float gain = peak > 0f ? peakTarget / peak : 1f;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
                w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * gain * 32767f), -32768, 32767));
            }
            AssetDatabase.ImportAsset(path);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = samples.Length > rate * 8 ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.55f;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.loadInBackground = loop;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
