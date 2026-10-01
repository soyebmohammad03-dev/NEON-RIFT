using System;
using NeonRift.Missions;
using NeonRift.Vehicles;

namespace NeonRift.Game
{
    /// <summary>Choices and results that travel between scenes for one play session.</summary>
    public sealed class RunSession
    {
        public VehicleDefinition SelectedVehicle { get; private set; }
        public MissionDefinition SelectedMission { get; private set; }
        public RunResult? LastResult { get; private set; }

        public bool IsReadyToLaunch => SelectedVehicle != null && SelectedMission != null;

        public void SelectVehicle(VehicleDefinition vehicle)
        {
            SelectedVehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
        }

        public void SelectMission(MissionDefinition mission)
        {
            SelectedMission = mission ?? throw new ArgumentNullException(nameof(mission));
        }

        public void RecordResult(RunResult result) => LastResult = result;

        public void ClearResult() => LastResult = null;
    }
}
