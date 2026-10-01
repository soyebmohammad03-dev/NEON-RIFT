namespace NeonRift.Vehicles
{
    /// <summary>Normalised driver intent, identical for player and AI drivers.</summary>
    public struct DrivingInput
    {
        /// <summary>0..1</summary>
        public float Throttle;
        /// <summary>0..1</summary>
        public float Brake;
        /// <summary>-1 (left) .. 1 (right)</summary>
        public float Steer;
        public bool Handbrake;

        public static readonly DrivingInput None = default;
    }

    /// <summary>Anything that can drive a vehicle: the player's controls, an AI driver, a replay.</summary>
    public interface IVehicleInputSource
    {
        DrivingInput ReadInput();
    }

    /// <summary>Implemented by a vehicle's controller so whoever spawns it can decide who drives it.</summary>
    public interface IVehicleInputReceiver
    {
        void SetInputSource(IVehicleInputSource source);
    }
}
