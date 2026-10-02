namespace NeonRift.Vehicles
{
    /// <summary>Input source whose values are set from code: tests, cutscenes, autopilots.</summary>
    public sealed class ScriptedDrivingInput : IVehicleInputSource
    {
        public DrivingInput Current;

        public DrivingInput ReadInput() => Current;
    }
}
