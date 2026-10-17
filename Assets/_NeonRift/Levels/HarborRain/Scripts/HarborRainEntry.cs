using NeonRift.Audio;
using NeonRift.Game;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Scene entry point for the standalone Harbor Rain level. Pressing Play in this scene lets the existing Bootstrap
    /// (loaded automatically by <c>BootstrapLoader</c>) adopt it unchanged; this then reuses the stock
    /// <see cref="MissionSceneEntry"/> (on a child object, so the flow does not pick it directly) for vehicle spawn,
    /// player input, chase camera and recovery, and starts the self-contained <see cref="HarborRainMission"/>.
    /// The scene is not in the build list and nothing in the original game references it.
    /// </summary>
    public sealed class HarborRainEntry : MonoBehaviour, ISceneEntryPoint
    {
        [SerializeField] private MissionSceneEntry driving;
        [SerializeField] private HarborRainMission mission;
        [Tooltip("Optional: a specific car (otherwise the session's car, or the catalog default).")]
        [SerializeField] private VehicleDefinition vehicleOverride;

        public MissionSceneEntry Driving => driving;
        public HarborRainMission Mission => mission;
        public static HarborRainEntry Active { get; private set; }
        public GameContext Context { get; private set; }

        public HarborCampaign Campaign { get; private set; }

        public void Enter(GameContext context)
        {
            Active = this;
            Context = context;
            var catalog = context.Config.VehicleCatalog;
            if (context.Campaign != null) { EnterCampaign(context); return; }
            if (vehicleOverride != null) context.Session.SelectVehicle(vehicleOverride, catalog);
            else
            {
                // Harbor Rain's own last pick (an unlocked Level 2 car or a catalog car); otherwise the catalog default.
                string last = PlayerPrefs.GetString(HarborGarage.LastCarKey, "");
                VehicleDefinition pick = null;
                foreach (var (car, locked, _) in mission.Garage.Cars(catalog)) if (!locked && car.Id == last) pick = car;
                if (pick == null) pick = context.Session.SelectedVehicle != null ? context.Session.SelectedVehicle : catalog != null ? catalog.Default : null;
                if (pick != null) context.Session.SelectVehicle(pick, catalog);
            }

            driving.Enter(context);
            context.Audio.TransitionTo(MixerState.Gameplay, 0.5f);
            if (driving.PlayerVehicle == null)
            {
                Debug.LogError("[HarborRain] No player vehicle was spawned; the mission cannot start.", this);
                return;
            }
            mission.Begin(context, driving);
            Debug.Log($"[HarborRain] started with {driving.PlayerVehicle.name}");
        }

        /// <summary>
        /// Campaign bootstrap: the arrival through the portal (the Night Run car, no mission yet), or the operation with the
        /// car chosen in the five-car selection (its trailer, then the mission). The level's own garage and last-car memory
        /// are not used: the campaign's selection is the car that spawns.
        /// </summary>
        private void EnterCampaign(GameContext context)
        {
            var campaign = context.Campaign;
            HarborGarage.ExternalUnlock = v => campaign.Progress.HarborVehiclesUnlocked && campaign.Definition.IsLevel2Vehicle(v);
            bool arrival = campaign.Step == CampaignStep.Arrival;
            if (!arrival) campaign.Step = CampaignStep.Level2;
            driving.Enter(context);
            context.Audio.TransitionTo(MixerState.Gameplay, 0.5f);
            if (driving.PlayerVehicle == null)
            {
                Debug.LogError("[HarborRain] No player vehicle was spawned; the campaign cannot continue.", this);
                return;
            }
            if (!arrival) mission.Begin(context, driving, campaign: true);
            Campaign = HarborCampaign.Create(this, context, arrival ? HarborCampaign.Mode.Arrival : HarborCampaign.Mode.Trailer);
            Debug.Log($"[HarborRain] campaign {(arrival ? "arrival" : "operation")} with {driving.PlayerVehicle.name} (selected {context.Session.SelectedVehicle?.Id})");
        }

        public void Exit()
        {
            if (Campaign != null) { Destroy(Campaign.gameObject); Campaign = null; }
            HarborGarage.ExternalUnlock = null;
            mission.End();
            driving.Exit();
            if (Active == this) Active = null;
        }
    }
}
