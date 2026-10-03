using System.Text;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRift.Input
{
    /// <summary>Bridges the Driving action map to the device-agnostic <see cref="IVehicleInputSource"/>.</summary>
    public sealed class PlayerDrivingInput : IVehicleInputSource
    {
        private readonly NeonRiftControls.DrivingActions actions;

        public PlayerDrivingInput(NeonRiftControls controls)
        {
            actions = controls.Driving;
        }

        public DrivingInput ReadInput()
        {
            return new DrivingInput
            {
                Throttle = Mathf.Clamp01(actions.Throttle.ReadValue<float>()),
                Brake = Mathf.Clamp01(actions.Brake.ReadValue<float>()),
                Steer = Mathf.Clamp(actions.Steer.ReadValue<float>(), -1f, 1f),
                Handbrake = actions.Handbrake.IsPressed()
            };
        }

        /// <summary>
        /// Lists the input devices the Input System can see. With no keyboard and no gamepad the Driving
        /// map cannot produce values, whatever the bindings say, so callers log this when a mission starts.
        /// </summary>
        public static string DescribeDevices(out bool canDrive)
        {
            var text = new StringBuilder();
            canDrive = false;
            foreach (var device in InputSystem.devices)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(device.displayName ?? device.name);
                if (!device.enabled) text.Append(" (disabled)");
                if (device.enabled && (device is Keyboard || device is Gamepad)) canDrive = true;
            }
            return text.Length > 0 ? text.ToString() : "none";
        }
    }
}
