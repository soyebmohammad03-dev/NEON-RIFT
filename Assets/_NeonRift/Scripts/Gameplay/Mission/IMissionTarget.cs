using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>Something an objective can point at: a zone to reach or an interactable to use.</summary>
    public interface IMissionTarget
    {
        string Id { get; }
        /// <summary>Name shown next to the HUD waypoint.</summary>
        string WaypointLabel { get; }
        Vector3 WaypointPosition { get; }
        /// <summary>Called when the target becomes / stops being the current objective (beacons, highlights).</summary>
        void SetObjectiveActive(bool active);
    }
}
