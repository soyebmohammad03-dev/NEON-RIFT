using System.Collections.Generic;
using System.Linq;
using System.Text;
using NeonRift.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Play Mode input checks driven from tooling (the unity CLI / MCP). Keys are written as state events
    /// into the real <see cref="Keyboard"/> device, so they travel the same path as physical key presses
    /// from the device onwards: action maps, enable/disable lifecycle, PlayerDrivingInput and VehicleController.
    /// The Game view must have focus (editor default: keyboards respect Game view focus).
    /// </summary>
    public static class InputPlaytest
    {
        private static readonly HashSet<Key> held = new();

        public static string Hold(params Key[] keys)
        {
            foreach (var k in keys) held.Add(k);
            return Push();
        }

        public static string Release(params Key[] keys)
        {
            foreach (var k in keys) held.Remove(k);
            return Push();
        }

        public static string ReleaseAll()
        {
            held.Clear();
            return Push();
        }

        /// <summary>Press and release one key over two input updates (for performed callbacks).</summary>
        public static string Tap(Key key)
        {
            Hold(key);
            InputSystem.Update();
            return Release(key);
        }

        private static string Push()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return "NO KEYBOARD DEVICE";
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(held.ToArray()));
            return "held: " + (held.Count == 0 ? "none" : string.Join("+", held));
        }

        // ---------------- Timed script ----------------

        /// <summary>One timed step: hold exactly these keys (and gamepad values) for a duration, then log a snapshot.</summary>
        public struct Step
        {
            public string Label;
            public Key[] Keys;
            public float Seconds;
            public Vector2 Stick;
            public float RightTrigger, LeftTrigger;
            public bool PadSouth, PadEast;
            public bool Teleport;
        }

        public const string ReportPath = "Logs/InputPlaytest.txt";
        private static List<Step> steps;
        private static int stepIndex;
        private static float stepEnds;
        private static StringBuilder report;
        private static Gamepad virtualPad;

        /// <summary>Runs the standard keyboard + gamepad drive script on game time and writes <see cref="ReportPath"/>.</summary>
        public static string RunStandardScript(bool includeGamepad)
        {
            Step S(string label, float seconds, params Key[] keys) => new Step { Label = label, Seconds = seconds, Keys = keys };
            var list = new List<Step>
            {
                new Step { Label = "teleport to spawn, settle", Seconds = 1.5f, Keys = new Key[0], Teleport = true },
                S("W from standstill", 1.2f, Key.W),
                S("W+D steering right", 0.9f, Key.W, Key.D),
                S("W+A steering left", 0.9f, Key.W, Key.A),
                S("S braking", 1.5f, Key.S),
                S("S hold at standstill (reverse)", 2.0f, Key.S),
                S("release", 1.0f),
                S("W", 1.0f, Key.W),
                S("Space handbrake while moving", 0.8f, Key.Space),
                S("release", 1.0f),
                new Step { Label = "teleport to spawn, settle", Seconds = 1.5f, Keys = new Key[0], Teleport = true },
                S("Up arrow from standstill", 1.2f, Key.UpArrow),
                S("Up+Right arrows", 0.9f, Key.UpArrow, Key.RightArrow),
                S("Up+Left arrows", 0.9f, Key.UpArrow, Key.LeftArrow),
                S("Down arrow braking", 2.5f, Key.DownArrow),
                S("E interact", 0.3f, Key.E),
                S("release", 1.0f),
            };
            if (includeGamepad)
            {
                list.Add(new Step { Label = "teleport to spawn, settle", Seconds = 1.5f, Keys = new Key[0], Teleport = true });
                list.Add(new Step { Label = "pad RT throttle 0.8", Seconds = 1.2f, Keys = new Key[0], RightTrigger = 0.8f });
                list.Add(new Step { Label = "pad RT + stick right", Seconds = 0.9f, Keys = new Key[0], RightTrigger = 0.8f, Stick = new Vector2(0.9f, 0f) });
                list.Add(new Step { Label = "pad RT + stick left", Seconds = 0.9f, Keys = new Key[0], RightTrigger = 0.8f, Stick = new Vector2(-0.9f, 0f) });
                list.Add(new Step { Label = "pad B/East handbrake", Seconds = 0.8f, Keys = new Key[0], PadEast = true });
                list.Add(new Step { Label = "pad LT brake", Seconds = 2.5f, Keys = new Key[0], LeftTrigger = 1f });
                list.Add(new Step { Label = "pad A/South interact", Seconds = 0.3f, Keys = new Key[0], PadSouth = true });
                list.Add(new Step { Label = "release", Seconds = 0.5f, Keys = new Key[0] });
            }
            return Run(list);
        }

        public static string Run(List<Step> script)
        {
            if (!Application.isPlaying) return "not in Play Mode";
            steps = script;
            stepIndex = -1;
            report = new StringBuilder();
            report.AppendLine($"InputPlaytest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  devices: {NeonRift.Input.PlayerDrivingInput.DescribeDevices(out _)}");
            if (System.IO.File.Exists(ReportPath)) System.IO.File.Delete(ReportPath);
            UnityEditor.EditorApplication.update -= Tick;
            UnityEditor.EditorApplication.update += Tick;
            Advance();
            return $"running {script.Count} steps";
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { Finish("Play Mode ended early"); return; }
            if (Time.time < stepEnds) return;
            report.AppendLine($"  -> {Snapshot()}{InteractState()}");
            Advance();
        }

        private static void Advance()
        {
            stepIndex++;
            if (stepIndex >= steps.Count) { Finish("done"); return; }
            var step = steps[stepIndex];
            if (step.Teleport)
            {
                var entry = Object.FindAnyObjectByType<MissionSceneEntry>();
                if (entry != null && entry.PlayerVehicle != null)
                {
                    var spawn = entry.SpawnPoint;
                    entry.PlayerVehicle.Teleport(spawn.Position, spawn.Rotation);
                    if (entry.ChaseCamera != null) entry.ChaseCamera.Snap();
                }
            }
            held.Clear();
            foreach (var k in step.Keys) held.Add(k);
            Push();
            if (step.Stick != Vector2.zero || step.RightTrigger > 0f || step.LeftTrigger > 0f || step.PadSouth || step.PadEast || virtualPad != null)
            {
                if (virtualPad == null) virtualPad = InputSystem.AddDevice<Gamepad>("PlaytestPad");
                var state = new GamepadState { leftStick = step.Stick, rightTrigger = step.RightTrigger, leftTrigger = step.LeftTrigger };
                if (step.PadSouth) state = state.WithButton(GamepadButton.South);
                if (step.PadEast) state = state.WithButton(GamepadButton.East);
                InputSystem.QueueStateEvent(virtualPad, state);
            }
            stepEnds = Time.time + step.Seconds;
            report.AppendLine($"[{Time.time,7:0.00}s] {step.Label} ({step.Seconds:0.0}s)");
        }

        private static void Finish(string why)
        {
            UnityEditor.EditorApplication.update -= Tick;
            held.Clear();
            if (Application.isPlaying) Push();
            if (virtualPad != null) { InputSystem.RemoveDevice(virtualPad); virtualPad = null; }
            report.AppendLine(why);
            System.IO.File.WriteAllText(ReportPath, report.ToString());
        }

        private static string InteractState()
        {
            foreach (var a in InputSystem.ListEnabledActions())
                if (a.name == "Interact") return a.IsPressed() ? " | Interact PRESSED" : string.Empty;
            return " | Interact action not enabled";
        }

        /// <summary>One-line summary of the player car, its input and the rivals.</summary>
        public static string Snapshot()
        {
            var entry = Object.FindAnyObjectByType<MissionSceneEntry>();
            if (entry == null) return "no MissionSceneEntry";
            var car = entry.PlayerVehicle;
            if (car == null) return "no player vehicle";
            var t = car.Telemetry;
            var i = car.LastInput;
            var sb = new StringBuilder();
            sb.Append($"player={car.name} src={car.InputSource?.GetType().Name} in(thr {i.Throttle:0.00} brk {i.Brake:0.00} steer {i.Steer:0.00} hb {i.Handbrake}) ");
            sb.Append($"speed {t.SpeedKph:0.0} km/h fwd {t.ForwardSpeed:0.00} gear {t.Gear} rpm {t.EngineRpm:0} steerAngle {t.SteerAngle:0.0} yaw {t.YawRate:0.0} heading {car.transform.eulerAngles.y:0.0} pos {car.transform.position:F1}");
            float rear = 0f;
            foreach (var w in car.Wheels) if (!w.IsFront) rear += Mathf.Abs(w.Rpm) * 0.5f;
            sb.Append($" rearWheelRpm {rear:0}");
            var rivals = entry.Rivals;
            if (rivals != null)
                foreach (var r in rivals.Rivals)
                    if (r.Car != null) sb.Append($" | rival {r.Car.name} src={r.Car.InputSource?.GetType().Name}");
            var director = entry.Director;
            if (director != null && director.Progress != null)
                sb.Append($" | mission {director.Progress.Phase} obj {director.Progress.ObjectiveIndex} focus {(director.Focused != null ? director.Focused.name : "-")}");
            return sb.ToString();
        }
    }
}
