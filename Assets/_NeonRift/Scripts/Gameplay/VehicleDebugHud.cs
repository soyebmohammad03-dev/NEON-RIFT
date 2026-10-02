using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Development overlay with live vehicle telemetry and per-wheel suspension/tyre state. F3 toggles it.
    /// Not part of the player HUD; development builds only.
    /// </summary>
    public sealed class VehicleDebugHud : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Key toggleKey = Key.F3;

        private VehicleController vehicle;
        private GUIStyle style;

        public void SetTarget(VehicleController target) => vehicle = target;

        private void Awake()
        {
            if (!Debug.isDebugBuild) enabled = false;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || vehicle == null || !vehicle.IsConfigured) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, normal = { textColor = Color.white } };

            var t = vehicle.Telemetry;
            string gear = t.Gear < 0 ? "R" : t.Gear == 0 ? "N" : t.Gear.ToString();
            var text = new System.Text.StringBuilder(512);
            text.AppendLine($"<b>{vehicle.Profile.name}</b>  {t.SpeedKph,5:0} km/h  gear {gear}{(t.IsShifting ? "*" : " ")}  {t.EngineRpm,5:0} rpm");
            text.AppendLine($"thr {t.Throttle:0.00} (in {t.ThrottleInput:0.00})  brk {t.Brake:0.00}  steer {t.SteerAngle,5:0.0}°  TC {t.TractionLimit:0.00}  clutch {t.Clutch:0.00}");
            text.AppendLine($"long {t.LongitudinalG,5:0.00} g  lat {t.LateralG,5:0.00} g  yaw {t.YawRate,6:0.0}°/s  slip {t.MaxSlip:0.00}");
            text.AppendLine("wheel  load N   comp   rpm    slipR   slipA°  grip");
            foreach (var w in vehicle.Wheels)
            {
                string colour = !w.IsGrounded ? "#888888" : w.CombinedSlip > 1f ? "#ff7070" : w.OnBumpStop ? "#ffd060" : "#ffffff";
                text.AppendLine($"<color={colour}>{w.Position,-10} {w.Load,6:0} {w.CompressionRatio,5:0.00} {w.Rpm,6:0} {w.SlipRatio,7:0.00} {w.SlipAngle,7:0.0} {w.SurfaceGrip,4:0.00}</color>");
            }
            text.Append("F3 hide  ·  R recover");
            GUI.Box(new Rect(10, 10, 520, 190), GUIContent.none);
            GUI.Label(new Rect(18, 14, 510, 185), text.ToString(), style);
        }
    }
}
