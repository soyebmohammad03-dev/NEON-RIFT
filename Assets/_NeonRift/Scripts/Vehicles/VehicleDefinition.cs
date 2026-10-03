using NeonRift.Core;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Identity and presentation data for one selectable vehicle.</summary>
    [CreateAssetMenu(menuName = "Neon Rift/Vehicles/Vehicle Definition", fileName = "VehicleDefinition")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        [Tooltip("Stable identifier used for saves and lookups. Never change after release.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string manufacturer;
        [Tooltip("Class shown in Car Select, e.g. GRAND TOURER.")]
        [SerializeField] private string category;
        [SerializeField, TextArea(2, 5)] private string description;

        [Header("Prefabs")]
        [Tooltip("Driveable prefab spawned in missions.")]
        [SerializeField] private GameObject gameplayPrefab;
        [Tooltip("Optional presentation-only prefab for Car Select. Falls back to the gameplay prefab.")]
        [SerializeField] private GameObject showroomPrefab;

        [Header("Driving")]
        [Tooltip("Physics tuning applied to the gameplay prefab when it is spawned.")]
        [SerializeField] private VehiclePhysicsProfile physicsProfile;
        [Tooltip("Engine, tyre and impact sounds played from the vehicle's telemetry.")]
        [SerializeField] private VehicleAudioProfile audioProfile;

        [Header("Car Select")]
        [SerializeField] private VehicleDisplayStats displayStats = VehicleDisplayStats.Default;
        [Tooltip("True until the figures are confirmed against the physics profile on the test track.")]
        [SerializeField] private bool displayStatsProvisional = true;

        [Header("Source asset")]
        [SerializeField] private AssetLicense license;

        public string Id => id;
        public bool DisplayStatsProvisional => displayStatsProvisional;
        public AssetLicense License => license;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Manufacturer => manufacturer;
        public string Category => category;

        /// <summary>RWD / AWD / FWD from the physics profile (the single source of tuning).</summary>
        public string DrivetrainLabel => physicsProfile == null ? "—" : physicsProfile.Transmission.driveType switch
        {
            DriveType.FrontWheelDrive => "FWD",
            DriveType.RearWheelDrive => "RWD",
            _ => "AWD"
        };
        public string Description => description;
        public GameObject GameplayPrefab => gameplayPrefab;
        public VehiclePhysicsProfile PhysicsProfile => physicsProfile;
        public VehicleAudioProfile AudioProfile => audioProfile;
        public GameObject ShowroomPrefab => showroomPrefab != null ? showroomPrefab : gameplayPrefab;
        public VehicleDisplayStats DisplayStats => displayStats;
    }
}
