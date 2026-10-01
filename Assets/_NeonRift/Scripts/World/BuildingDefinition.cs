using NeonRift.Core;
using UnityEngine;

namespace NeonRift.World
{
    /// <summary>Distance band a building is authored for. Drives placement, LOD and lighting budgets.</summary>
    public enum BuildingTier
    {
        /// <summary>Lines the driving corridor; full detail, colliders, may receive real lights.</summary>
        Hero,
        /// <summary>One or two blocks back; visible detail, emissive windows only.</summary>
        Midground,
        /// <summary>Silhouette and window glow only; no colliders.</summary>
        Skyline
    }

    /// <summary>One placeable building prefab and the facts the city layout needs about it.</summary>
    [CreateAssetMenu(menuName = "Neon Rift/World/Building Definition", fileName = "Building")]
    public sealed class BuildingDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [SerializeField] private BuildingTier tier;
        [Tooltip("Width (X), height (Y), depth (Z) in metres. Prefab pivot is ground centre; +Z is the street-facing side.")]
        [SerializeField] private Vector3 size;
        [SerializeField] private bool hasCollider;
        [SerializeField] private int triangleCount;
        [SerializeField, TextArea(1, 4)] private string notes;
        [SerializeField] private AssetLicense license;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public GameObject Prefab => prefab;
        public BuildingTier Tier => tier;
        public Vector3 Size => size;
        public bool HasCollider => hasCollider;
        public int TriangleCount => triangleCount;
        public string Notes => notes;
        public AssetLicense License => license;
    }
}
