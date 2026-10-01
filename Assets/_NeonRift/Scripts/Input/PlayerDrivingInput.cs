using NeonRift.Vehicles;
using UnityEngine;

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
    }
}
