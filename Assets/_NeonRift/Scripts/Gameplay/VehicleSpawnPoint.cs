using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>Where a vehicle enters a mission scene. Forward (blue axis) is the driving direction.</summary>
    public sealed class VehicleSpawnPoint : MonoBehaviour
    {
        [Tooltip("Spawn this high above the point so suspension settles instead of intersecting the road.")]
        [SerializeField, Min(0f)] private float dropHeight = 0.3f;

        public GameObject Spawn(VehicleDefinition definition)
        {
            if (definition == null || definition.GameplayPrefab == null)
            {
                Debug.LogError($"[Spawn] '{definition?.name ?? "null"}' has no gameplay prefab.", this);
                return null;
            }
            var instance = Instantiate(definition.GameplayPrefab, transform.position + Vector3.up * dropHeight, transform.rotation);
            instance.name = $"PlayerVehicle_{definition.Id}";
            return instance;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.33f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(0f, 0.7f, 0f), new Vector3(2f, 1.4f, 4.6f));
            Gizmos.DrawLine(new Vector3(0f, 0.7f, 2.3f), new Vector3(0f, 0.7f, 4f));
        }
    }
}
