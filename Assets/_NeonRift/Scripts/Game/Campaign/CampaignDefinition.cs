using System;
using NeonRift.Missions;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Game
{
    /// <summary>One spoken line: the same record drives the audio clip and its subtitle, so the two cannot drift apart.</summary>
    [Serializable]
    public struct VoiceLine
    {
        public string id;
        public string speaker;
        [TextArea] public string text;
        public AudioClip clip;

        /// <summary>Subtitle time: the clip's length, or a reading-time estimate for a line without audio.</summary>
        public float Seconds => clip != null ? clip.length : Mathf.Clamp(text != null ? text.Length * 0.065f : 2f, 2f, 7f);
        public bool Voiced => clip != null;
    }

    /// <summary>
    /// The connected two-level campaign (new campaign bootstrap only): which missions form it, which cars each car
    /// selection offers, which cars Level 2 unlocks, the spoken lines and the presentation assets of the bridges
    /// (Harbor Rain teaser in the opening, the portal). The standalone Night Run config has no campaign.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Campaign/Campaign Definition", fileName = "CampaignDefinition")]
    public sealed class CampaignDefinition : ScriptableObject
    {
        [SerializeField] private string id = "neon_rift_campaign";
        [SerializeField] private string displayName = "NEON RIFT";

        [Header("Missions")]
        [SerializeField] private MissionDefinition level1;
        [SerializeField] private MissionDefinition level2;

        [Header("Cars")]
        [Tooltip("Level 1 car selection (all five cars; the Level 2 cars show locked).")]
        [SerializeField] private VehicleCatalog level1Selection;
        [Tooltip("Level 2 car selection (all five cars).")]
        [SerializeField] private VehicleCatalog level2Selection;
        [Tooltip("Cars that exist for Harbor Rain only: locked in Level 1, unlocked for Level 2 by qualifying in Night Run.")]
        [SerializeField] private VehicleDefinition[] level2Vehicles = Array.Empty<VehicleDefinition>();

        [Header("Voice (every line is subtitled)")]
        [SerializeField] private VoiceLine[] lines = Array.Empty<VoiceLine>();

        [Header("Harbor Rain teaser (Night Run opening)")]
        [Tooltip("Frames captured from the Harbor Rain level, shown as an intercepted transmission.")]
        [SerializeField] private Texture2D[] teaserFrames = Array.Empty<Texture2D>();
        [SerializeField] private string[] teaserCaptions = Array.Empty<string>();
        [SerializeField] private AudioClip teaserStatic, teaserDrone;

        [Header("Portal")]
        [SerializeField] private Material portalRing, portalCore, portalSparks;
        [SerializeField] private AudioClip portalOpen, portalHum, portalEnter, portalArrive;
        [Tooltip("Night Run: a clear straight where the portal sequence is staged (start of the run-up), world.")]
        [SerializeField] private Vector3 nightRunPortalStage;
        [SerializeField] private float nightRunPortalYaw;
        [SerializeField] private float nightRunPortalDistance = 46f;

        public string Id => id;
        public string DisplayName => displayName;
        public MissionDefinition Level1 => level1;
        public MissionDefinition Level2 => level2;
        public VehicleCatalog Level1Selection => level1Selection;
        public VehicleCatalog Level2Selection => level2Selection;
        public VehicleDefinition[] Level2Vehicles => level2Vehicles;
        public VoiceLine[] Lines => lines;
        public Texture2D[] TeaserFrames => teaserFrames;
        public string[] TeaserCaptions => teaserCaptions;
        public AudioClip TeaserStatic => teaserStatic;
        public AudioClip TeaserDrone => teaserDrone;
        public Material PortalRing => portalRing;
        public Material PortalCore => portalCore;
        public Material PortalSparks => portalSparks;
        public AudioClip PortalOpen => portalOpen;
        public AudioClip PortalHum => portalHum;
        public AudioClip PortalEnter => portalEnter;
        public AudioClip PortalArrive => portalArrive;
        public Vector3 NightRunPortalStage => nightRunPortalStage;
        public float NightRunPortalYaw => nightRunPortalYaw;
        public float NightRunPortalDistance => nightRunPortalDistance;

        public bool IsLevel2Vehicle(VehicleDefinition v)
        {
            if (v == null) return false;
            foreach (var x in level2Vehicles) if (x == v) return true;
            return false;
        }

        public bool TryGetLine(string lineId, out VoiceLine line)
        {
            foreach (var l in lines)
                if (l.id == lineId) { line = l; return true; }
            line = default;
            return false;
        }

#if UNITY_EDITOR
        public void EditorConfigure(MissionDefinition l1, MissionDefinition l2, VehicleCatalog s1, VehicleCatalog s2, VehicleDefinition[] unlocks)
        {
            level1 = l1; level2 = l2; level1Selection = s1; level2Selection = s2; level2Vehicles = unlocks;
        }

        public void EditorConfigureLines(VoiceLine[] voice) => lines = voice;

        public void EditorConfigureTeaser(Texture2D[] frames, string[] captions, AudioClip staticClip, AudioClip drone)
        {
            teaserFrames = frames; teaserCaptions = captions; teaserStatic = staticClip; teaserDrone = drone;
        }

        public void EditorConfigurePortal(Material ring, Material core, Material sparks, AudioClip open, AudioClip hum, AudioClip enter, AudioClip arrive,
                                          Vector3 stage, float yaw, float distance)
        {
            portalRing = ring; portalCore = core; portalSparks = sparks;
            portalOpen = open; portalHum = hum; portalEnter = enter; portalArrive = arrive;
            nightRunPortalStage = stage; nightRunPortalYaw = yaw; nightRunPortalDistance = distance;
        }
#endif
    }
}
