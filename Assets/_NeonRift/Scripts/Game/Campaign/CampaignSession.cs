using NeonRift.Missions;
using NeonRift.Vehicles;

namespace NeonRift.Game
{
    /// <summary>Where the connected campaign is right now (runtime; the persistent part is <see cref="CampaignProgress"/>).</summary>
    public enum CampaignStep
    {
        /// <summary>Opening, Level 1 car selection and Night Run.</summary>
        Level1,
        /// <summary>Night Run qualified: the portal sequence is playing in the city.</summary>
        Portal,
        /// <summary>Harbor Rain scene: the car comes through the portal, the new operation is unlocked.</summary>
        Arrival,
        /// <summary>The five-car selection for Harbor Rain.</summary>
        Level2Select,
        /// <summary>Harbor Rain trailer and mission.</summary>
        Level2,
        /// <summary>Both operations done: campaign results.</summary>
        Complete
    }

    /// <summary>
    /// The running campaign, created only by the campaign bootstrap's <see cref="GameRoot"/> and handed to scenes in the
    /// <see cref="GameContext"/>. Scenes check <c>context.Campaign != null</c>: in the standalone Night Run bootstrap it
    /// is null and every scene behaves exactly as before.
    /// </summary>
    public sealed class CampaignSession
    {
        public CampaignDefinition Definition { get; }
        public CampaignProgress Progress { get; }
        public CampaignStep Step { get; set; } = CampaignStep.Level1;
        /// <summary>Subtitles, voice, cards, teaser and flash layers (lives in the campaign bootstrap).</summary>
        public CampaignOverlay Overlay { get; internal set; }

        public CampaignSession(CampaignDefinition definition, CampaignProgress progress)
        {
            Definition = definition;
            Progress = progress;
        }

        public bool IsLevel2Step => Step is CampaignStep.Arrival or CampaignStep.Level2Select or CampaignStep.Level2 or CampaignStep.Complete;

        /// <summary>The car list Car Select shows at this point of the campaign.</summary>
        public VehicleCatalog SelectionCatalog => Step == CampaignStep.Level2Select ? Definition.Level2Selection : Definition.Level1Selection;

        /// <summary>The mission Car Select launches at this point of the campaign.</summary>
        public MissionDefinition SelectionMission => Step == CampaignStep.Level2Select ? Definition.Level2 : Definition.Level1;

        /// <summary>
        /// Harbor Rain cars: locked in the Level 1 selection always (they are intended for Level 2 only) and in the Level 2
        /// selection until the player has qualified out of Night Run. Every other car is always available.
        /// </summary>
        public bool IsLocked(VehicleDefinition vehicle, out string reason)
        {
            reason = null;
            if (!Definition.IsLevel2Vehicle(vehicle)) return false;
            if (Step == CampaignStep.Level2Select && Progress.HarborVehiclesUnlocked) return false;
            reason = Progress.HarborVehiclesUnlocked ? "HARBOR RAIN ONLY  ·  UNLOCKED FOR OPERATION KX-7"
                                                     : "HARBOR RAIN CAR  ·  QUALIFY IN NIGHT RUN TO UNLOCK";
            return true;
        }

        /// <summary>Plays a spoken line with its subtitle; returns its length in seconds (0 when unknown).</summary>
        public float Speak(string lineId) => Overlay != null ? Overlay.Speak(lineId) : 0f;
    }
}
