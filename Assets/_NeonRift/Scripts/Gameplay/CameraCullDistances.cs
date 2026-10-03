using System;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Per-layer cull distances for a camera (Unity does not serialize <see cref="Camera.layerCullDistances"/>).
    /// Small street furniture on the Detail layer disappears beyond a distance where it is a few pixels anyway;
    /// everything else keeps the camera's far plane.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraCullDistances : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string layer;
            [Min(0f)] public float distance;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        [Tooltip("Cull against a sphere around the camera instead of the far plane, so turning the camera does not pop props.")]
        [SerializeField] private bool spherical = true;

        public float DistanceFor(int layer) => GetComponent<Camera>().layerCullDistances[layer];

        private void OnEnable() => Apply();

        public void Apply()
        {
            var cam = GetComponent<Camera>();
            var distances = new float[32];
            foreach (var e in entries)
            {
                int layer = LayerMask.NameToLayer(e.layer);
                if (layer >= 0) distances[layer] = e.distance;
            }
            cam.layerCullDistances = distances;
            cam.layerCullSpherical = spherical;
        }

#if UNITY_EDITOR
        public void EditorConfigure(Entry[] cullEntries) => entries = cullEntries;
#endif
    }
}
