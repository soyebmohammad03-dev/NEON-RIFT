using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>Ordered list of vehicles available to the player. Adding a car = adding a definition here.</summary>
    [CreateAssetMenu(menuName = "Neon Rift/Vehicles/Vehicle Catalog", fileName = "VehicleCatalog")]
    public sealed class VehicleCatalog : ScriptableObject
    {
        [SerializeField] private List<VehicleDefinition> vehicles = new();

        public IReadOnlyList<VehicleDefinition> Vehicles => vehicles;
        public int Count => vehicles.Count;
        public VehicleDefinition Default => vehicles.Count > 0 ? vehicles[0] : null;

        public bool TryGet(string vehicleId, out VehicleDefinition definition)
        {
            foreach (var v in vehicles)
            {
                if (v != null && v.Id == vehicleId)
                {
                    definition = v;
                    return true;
                }
            }
            definition = null;
            return false;
        }

        public int IndexOf(VehicleDefinition definition) => vehicles.IndexOf(definition);

        /// <summary>Returns human-readable problems; empty when the catalog is valid.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var seen = new HashSet<string>();
            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                if (v == null) { problems.Add($"Entry {i} is empty."); continue; }
                if (string.IsNullOrWhiteSpace(v.Id)) problems.Add($"'{v.name}' has no id.");
                else if (!seen.Add(v.Id)) problems.Add($"Duplicate vehicle id '{v.Id}'.");
                if (v.GameplayPrefab == null) problems.Add($"'{v.name}' has no gameplay prefab.");
                else
                {
                    if (v.GameplayPrefab.GetComponent<VehicleRig>() == null) problems.Add($"'{v.name}' gameplay prefab has no VehicleRig.");
                    if (v.GameplayPrefab.GetComponent<VehicleController>() == null) problems.Add($"'{v.name}' gameplay prefab has no VehicleController.");
                }
                if (v.PhysicsProfile == null) problems.Add($"'{v.name}' has no physics profile.");
                else foreach (var p in v.PhysicsProfile.Validate()) problems.Add($"'{v.name}' physics: {p}");
            }
            return problems;
        }
    }
}
