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
            // No layerCullSpherical: it only works with the built-in renderer (URP ignores it and logs a warning).
            cam.layerCullDistances = distances;
        }

#if UNITY_EDITOR
        public void EditorConfigure(Entry[] cullEntries) => entries = cullEntries;
#endif
    }
}
