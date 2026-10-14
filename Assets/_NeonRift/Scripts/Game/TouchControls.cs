using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace NeonRift.Game
{
    /// <summary>
    /// On-screen controls for touch devices. Instead of driving anything itself, it feeds a virtual Gamepad, so every
    /// existing binding (Driving and Menu maps, the music toggle) and the vehicle/mission code are reused unchanged:
    /// GAS = right trigger, BRAKE = left trigger, steering = left stick X, HANDBRAKE = B, HACK = A, RECOVER = Select,
    /// PAUSE / SKIP = Start, MUSIC = Y, START (intro title) = A. Every frame each control is "held" while any active
    /// touch is inside it, so several fingers work at once and a control releases when its finger lifts or slides off.
    /// Optional tilt steering reads the gravity vector (accelerometer); see <see cref="ReadTilt"/>.
    /// Created by <see cref="GameRoot"/> only when a touchscreen is present; desktop input is untouched.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        /// <summary>True while the touch layer is running (the mission HUD moves its corner panels clear of the buttons).</summary>
        public static bool Active { get; private set; }

        private struct Pad
        {
            public Rect Rect;    // IMGUI space (top-left origin)
            public string Label;
            public bool Held;
        }

        private static readonly Color Line = new(0.33f, 0.91f, 1f, 0.85f);
        private static readonly Color Fill = new(0.02f, 0.03f, 0.05f, 0.35f);
        private static readonly Color FillHeld = new(0.33f, 0.91f, 1f, 0.45f);

        private enum Id { Left, Right, Gas, Brake, Handbrake, Hack, Recover, Pause, Music, Center, Skip, Start, Count }

        private readonly Pad[] pads = new Pad[(int)Id.Count];
        private readonly bool[] visible = new bool[(int)Id.Count];
        private Input.NeonRiftControls controls;
        private IGameFlow flow;
        private GameSettings settings;
        private Gamepad pad;
        private GUIStyle labelStyle;
        private float touchSteer, tiltSteer, tiltFiltered;
        private bool sensorsOn, appPaused, calibrateNext;
        private Vector3 gravityFiltered;
        private Rect layoutSafeArea;
        private Vector2 layoutScreen;

        public void Initialize(Input.NeonRiftControls gameControls, IGameFlow gameFlow, GameSettings gameSettings)
        {
            controls = gameControls;
            flow = gameFlow;
            settings = gameSettings;
            pad = InputSystem.AddDevice<Gamepad>("NeonRiftTouchPad");
            settings.TiltCalibrateRequested += () => calibrateNext = true;
            Active = true;
            Debug.Log("[Touch] on-screen controls enabled (virtual gamepad). Tilt sensor: " +
                      (GravitySensor.current != null ? "gravity" : Accelerometer.current != null ? "accelerometer" : "none"));
        }

        public static bool TiltAvailable => GravitySensor.current != null || Accelerometer.current != null;

        private void OnDestroy()
        {
            Active = false;
            SetSensors(false);
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        }

        private void OnApplicationPause(bool paused)
        {
            appPaused = paused;
            if (paused) { SetSensors(false); ReleaseAll(); }
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) ReleaseAll();
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < pads.Length; i++) pads[i].Held = false;
            touchSteer = tiltSteer = 0f;
            if (pad != null && pad.added) InputSystem.QueueStateEvent(pad, new GamepadState());
        }

        // ---------------- Per frame ----------------

        private void Update()
        {
            if (pad == null || appPaused) return;
            Layout();
            UpdateVisibility(out bool driving);

            for (int i = 0; i < pads.Length; i++) pads[i].Held = false;
            var screen = Touchscreen.current;
            if (screen != null)
                foreach (var touch in screen.touches)
                {
                    if (!touch.press.isPressed) continue;
                    Vector2 p = touch.position.ReadValue();
                    p.y = Screen.height - p.y;
                    for (int i = 0; i < pads.Length; i++)
                        if (visible[i] && pads[i].Rect.Contains(p)) pads[i].Held = true;
                }

            bool tilt = driving && settings.TiltSteering && TiltAvailable;
            SetSensors(tilt);
            float dt = Time.unscaledDeltaTime;
            float target = (Held(Id.Right) ? 1f : 0f) - (Held(Id.Left) ? 1f : 0f);
            touchSteer = Mathf.MoveTowards(touchSteer, target, 6f * dt);
            if (tilt && Held(Id.Center)) calibrateNext = true;
            float steer = tilt ? ReadTilt(dt) : touchSteer;
            if (!driving) steer = touchSteer = tiltSteer = 0f;

            var state = new GamepadState
            {
                leftStick = new Vector2(Mathf.Clamp(steer, -1f, 1f), 0f),
                rightTrigger = Held(Id.Gas) ? 1f : 0f,
                leftTrigger = Held(Id.Brake) ? 1f : 0f
            };
            if (Held(Id.Handbrake)) state = state.WithButton(GamepadButton.East);
            if (Held(Id.Hack) || Held(Id.Start)) state = state.WithButton(GamepadButton.South);
            if (Held(Id.Recover)) state = state.WithButton(GamepadButton.Select);
            if (Held(Id.Pause) || Held(Id.Skip)) state = state.WithButton(GamepadButton.Start);
            if (Held(Id.Music)) state = state.WithButton(GamepadButton.North);
            InputSystem.QueueStateEvent(pad, state);
        }

        private bool Held(Id id) => visible[(int)id] && pads[(int)id].Held;

        private void UpdateVisibility(out bool driving)
        {
            var state = flow != null ? flow.State : GameState.None;
            bool transitioning = flow == null || flow.IsTransitioning;
            driving = !transitioning && state == GameState.Mission && controls.Driving.enabled;   // the pause menu disables Driving
            bool intro = !transitioning && state == GameState.Intro;
            bool tilt = settings.TiltSteering && TiltAvailable;
            for (int i = 0; i < visible.Length; i++) visible[i] = driving;
            visible[(int)Id.Left] = visible[(int)Id.Right] = driving && !tilt;
            visible[(int)Id.Center] = driving && tilt;
            visible[(int)Id.Music] = driving || intro;
            visible[(int)Id.Skip] = visible[(int)Id.Start] = intro;
        }

        // ---------------- Tilt steering ----------------

        /// <summary>
        /// Steering from the phone's roll, like turning a wheel. The gravity vector (from the gravity sensor, or a
        /// low-passed accelerometer) is in screen space (the Input System compensates for screen orientation), so its
        /// angle in the screen plane is the roll. The gyroscope only measures rotation rate, so it is not used.
        /// Roll relative to the calibrated neutral goes through a dead zone and a sensitivity range, then is smoothed.
        /// </summary>
        private float ReadTilt(float dt)
        {
            Vector3 g;
            var gravity = GravitySensor.current;
            if (gravity != null && gravity.enabled) g = gravity.gravity.ReadValue();
            else if (Accelerometer.current != null && Accelerometer.current.enabled) g = Accelerometer.current.acceleration.ReadValue();
            else return tiltSteer = 0f;
            gravityFiltered = Vector3.Lerp(gravityFiltered, g, 1f - Mathf.Exp(-12f * dt));

            // Phone lying (almost) flat: the roll angle is meaningless, so ease back to straight.
            float planar = new Vector2(gravityFiltered.x, gravityFiltered.y).magnitude;
            if (planar < 0.25f) return tiltSteer = Mathf.MoveTowards(tiltSteer, 0f, 3f * dt);

            float roll = Mathf.Atan2(gravityFiltered.x, -gravityFiltered.y) * Mathf.Rad2Deg;
            if (calibrateNext)
            {
                calibrateNext = false;
                settings.SetTiltNeutral(roll);
            }
            float angle = Mathf.DeltaAngle(settings.TiltNeutral, roll);
            const float deadZone = 3f;
            float fullLock = Mathf.Lerp(40f, 12f, Mathf.InverseLerp(0.5f, 2f, settings.TiltSensitivity));
            float raw = Mathf.Sign(angle) * Mathf.Clamp01((Mathf.Abs(angle) - deadZone) / (fullLock - deadZone));
            tiltFiltered = Mathf.Lerp(tiltFiltered, raw, 1f - Mathf.Exp(-10f * dt));
            return tiltSteer = Mathf.Clamp(tiltFiltered, -1f, 1f);
        }

        private void SetSensors(bool on)
        {
            if (sensorsOn == on) return;
            sensorsOn = on;
            foreach (InputDevice sensor in new InputDevice[] { GravitySensor.current, Accelerometer.current })
            {
                if (sensor == null) continue;
                if (on) InputSystem.EnableDevice(sensor);
                else InputSystem.DisableDevice(sensor);
            }
            if (on) { gravityFiltered = new Vector3(0f, -1f, 0f); tiltFiltered = 0f; }
            else tiltSteer = 0f;
        }

        // ---------------- Layout and drawing ----------------

        private void Layout()
        {
            var safe = Screen.safeArea;
            var size = new Vector2(Screen.width, Screen.height);
            if (safe == layoutSafeArea && size == layoutScreen && labelStyle != null) return;
            layoutSafeArea = safe;
            layoutScreen = size;
            // Same scale as the UI Toolkit panel (1920x1080 reference, match 0.5).
            float s = Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f));
            float left = safe.xMin, right = safe.xMax, top = Screen.height - safe.yMax, bottom = Screen.height - safe.yMin;
            Rect BL(float x, float y, float w, float h) => new(left + x * s, bottom - (y + h) * s, w * s, h * s);
            Rect BR(float x, float y, float w, float h) => new(right - (x + w) * s, bottom - (y + h) * s, w * s, h * s);
            Rect TR(float x, float y, float w, float h) => new(right - (x + w) * s, top + y * s, w * s, h * s);

            Set(Id.Left, BL(36, 36, 220, 220), "◀");
            Set(Id.Right, BL(276, 36, 220, 220), "▶");
            Set(Id.Center, BL(36, 36, 200, 90), "CENTER TILT");
            Set(Id.Gas, BR(36, 36, 230, 270), "GAS");
            Set(Id.Brake, BR(286, 36, 200, 200), "BRAKE");
            Set(Id.Handbrake, BR(286, 256, 200, 120), "HANDBRAKE");
            Set(Id.Hack, BR(36, 326, 230, 140), "HACK");
            Set(Id.Recover, BR(36, 486, 230, 90), "RECOVER");
            Set(Id.Pause, TR(36, 36, 120, 84), "II");
            Set(Id.Music, TR(176, 36, 140, 84), "MUSIC");
            Set(Id.Skip, BR(36, 36, 240, 110), "SKIP");
            Set(Id.Start, BR(296, 36, 240, 110), "START");

            labelStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Max(12, Mathf.RoundToInt(28 * s)),
                normal = { textColor = new Color(0.9f, 0.98f, 1f, 0.95f) }
            };
        }

        private void Set(Id id, Rect rect, string label)
        {
            pads[(int)id].Rect = rect;
            pads[(int)id].Label = label;
        }

        private void OnGUI()
        {
            if (pad == null || labelStyle == null || Event.current.type != EventType.Repaint) return;
            float radius = 18f * labelStyle.fontSize / 28f;
            for (int i = 0; i < pads.Length; i++)
            {
                if (!visible[i]) continue;
                var r = pads[i].Rect;
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, pads[i].Held ? FillHeld : Fill, 0f, radius);
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Line, 2f, radius);
                GUI.Label(r, pads[i].Label, labelStyle);
            }
        }
    }
}
