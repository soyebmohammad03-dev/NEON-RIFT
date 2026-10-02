using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A trigger volume the player can reach (objective destinations, extraction). Lives on the Trigger layer,
    /// which only collides with vehicles. Shows its beacon while it is the current objective.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class MissionZone : MonoBehaviour, IMissionWorldComponent, IMissionTarget
    {
        [Tooltip("Id referenced by mission objectives.")]
        [SerializeField] private string id;
        [SerializeField] private string waypointLabel;
        [Tooltip("Waypoint marker height above the zone's origin, m.")]
        [SerializeField] private float waypointHeight = 3f;
        [Tooltip("Shown only while this zone is the current objective (light pillar, ground decal).")]
        [SerializeField] private GameObject beacon;

        private MissionWorld world;
        private int overlaps;

        public string Id => id;
        public string WaypointLabel => waypointLabel;
        public Vector3 WaypointPosition => transform.position + Vector3.up * waypointHeight;
        public bool PlayerInside => overlaps > 0;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (beacon != null) beacon.SetActive(false);
        }

        public void Bind(MissionWorld missionWorld) => world = missionWorld;

        public void Unbind()
        {
            world = null;
            overlaps = 0;
        }

        public void SetObjectiveActive(bool active)
        {
            if (beacon != null) beacon.SetActive(active);
        }

        private void OnTriggerEnter(Collider other)
        {
            // The car body is several colliders: count them so entry fires once and exit only when all have left.
            if (world == null || !world.IsPlayer(other)) return;
            if (overlaps++ == 0) world.NotifyZoneEntered(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (world != null && world.IsPlayer(other)) overlaps = Mathf.Max(0, overlaps - 1);
        }

#if UNITY_EDITOR
        public void EditorConfigure(string zoneId, string label, float height, GameObject beaconObject)
        {
            id = zoneId;
            waypointLabel = label;
            waypointHeight = height;
            beacon = beaconObject;
        }
#endif
    }
}
