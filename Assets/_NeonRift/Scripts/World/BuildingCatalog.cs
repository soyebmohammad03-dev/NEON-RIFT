using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NeonRift.World
{
    /// <summary>All building prefabs available to city layout, grouped by tier.</summary>
    [CreateAssetMenu(menuName = "Neon Rift/World/Building Catalog", fileName = "BuildingCatalog")]
    public sealed class BuildingCatalog : ScriptableObject
    {
        [SerializeField] private List<BuildingDefinition> buildings = new();

        public IReadOnlyList<BuildingDefinition> Buildings => buildings;

        public IEnumerable<BuildingDefinition> InTier(BuildingTier tier) =>
            buildings.Where(b => b != null && b.Tier == tier);

        public List<string> Validate()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null) { problems.Add($"Entry {i} is empty."); continue; }
                if (string.IsNullOrWhiteSpace(b.Id)) problems.Add($"'{b.name}' has no id.");
                else if (!ids.Add(b.Id)) problems.Add($"Duplicate building id '{b.Id}'.");
                if (b.Prefab == null) problems.Add($"'{b.name}' has no prefab.");
                if (b.Size.y <= 0f) problems.Add($"'{b.name}' has no measured size.");
            }
            return problems;
        }
    }
}
