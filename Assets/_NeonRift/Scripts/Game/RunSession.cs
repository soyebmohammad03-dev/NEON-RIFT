using System;
using System.Collections.Generic;
using NeonRift.Missions;
using NeonRift.Vehicles;

namespace NeonRift.Game
{
    /// <summary>Choices and results that travel between scenes for one play session.</summary>
    public sealed class RunSession
    {
        private readonly List<VehicleDefinition> rivals = new();

        public VehicleDefinition SelectedVehicle { get; private set; }
        /// <summary>Cars the rival crews drive: by default every catalog car the player did not pick.</summary>
        public IReadOnlyList<VehicleDefinition> Rivals => rivals;
        public MissionDefinition SelectedMission { get; private set; }
        public RunResult? LastResult { get; private set; }

        public bool IsReadyToLaunch => SelectedVehicle != null && SelectedMission != null;

        /// <summary>True when Car Select is entered from the intro's push-in (it opens on the matching close-up).</summary>
        public bool ArrivedFromIntro { get; private set; }
        public void MarkIntroArrival() => ArrivedFromIntro = true;
        public void ClearIntroArrival() => ArrivedFromIntro = false;

        public void SelectVehicle(VehicleDefinition vehicle)
        {
            SelectedVehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
            rivals.Clear();
        }

        /// <summary>Picks the player's car and makes the rest of the catalog the rival field.</summary>
        public void SelectVehicle(VehicleDefinition vehicle, VehicleCatalog catalog)
        {
            SelectVehicle(vehicle);
            rivals.Clear();
            if (catalog == null) return;
            foreach (var v in catalog.Vehicles)
                if (v != null && v != vehicle) rivals.Add(v);
        }

        public void SelectMission(MissionDefinition mission)
        {
            SelectedMission = mission ?? throw new ArgumentNullException(nameof(mission));
        }

        public void RecordResult(RunResult result) => LastResult = result;

        public void ClearResult() => LastResult = null;
    }
}
