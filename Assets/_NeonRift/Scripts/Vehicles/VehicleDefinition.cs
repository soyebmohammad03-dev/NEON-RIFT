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
        [SerializeField, TextArea(2, 5)] private string description;

        [Header("Prefabs")]
        [Tooltip("Driveable prefab spawned in missions.")]
        [SerializeField] private GameObject gameplayPrefab;
        [Tooltip("Optional presentation-only prefab for Car Select. Falls back to the gameplay prefab.")]
        [SerializeField] private GameObject showroomPrefab;

        [Header("Car Select")]
        [SerializeField] private VehicleDisplayStats displayStats = VehicleDisplayStats.Default;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Manufacturer => manufacturer;
        public string Description => description;
        public GameObject GameplayPrefab => gameplayPrefab;
        public GameObject ShowroomPrefab => showroomPrefab != null ? showroomPrefab : gameplayPrefab;
        public VehicleDisplayStats DisplayStats => displayStats;
    }
}
