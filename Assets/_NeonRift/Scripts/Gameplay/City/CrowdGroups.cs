using System;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Pedestrian silhouettes in small groups (static low-poly figures, no colliders) on plazas, at the night market
    /// and on pavements. They give the streets a sense of people at a distance; a group steps out of sight when the
    /// player's car comes within <see cref="clearRadius"/> (they never stand in a car's way). Each group sways a little.
    /// Checked a few groups per frame, so the cost is flat.
    /// </summary>
    public sealed class CrowdGroups : MonoBehaviour, IMissionWorldComponent
    {
        [SerializeField] private Transform[] groups = Array.Empty<Transform>();
        [SerializeField, Min(2f)] private float clearRadius = 16f;
        [SerializeField, Min(1)] private int checksPerFrame = 6;

        private MissionWorld world;
        private Transform viewer;
        private int cursor;
        private Quaternion[] rest = Array.Empty<Quaternion>();

        public int Count => groups.Length;

        private void Awake()
        {
            rest = new Quaternion[groups.Length];
            for (int i = 0; i < groups.Length; i++) if (groups[i] != null) rest[i] = groups[i].localRotation;
        }

        public void Bind(MissionWorld missionWorld) => world = missionWorld;
        public void Unbind() => world = null;

        private void Update()
        {
            if (groups.Length == 0) return;
            Vector3 p;
            if (world != null && world.PlayerBody != null) p = world.PlayerBody.position;
            else
            {
                if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
                p = viewer != null ? viewer.position : Vector3.zero;
            }
            for (int k = 0; k < checksPerFrame; k++)
            {
                cursor = (cursor + 1) % groups.Length;
                var g = groups[cursor];
                if (g == null) continue;
                bool near = (g.position - p).sqrMagnitude < clearRadius * clearRadius;
                if (g.gameObject.activeSelf == near) g.gameObject.SetActive(!near);
                if (!near) g.localRotation = rest[cursor] * Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.3f + cursor) * 6f, 0f);
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform[] crowd) => groups = crowd;
#endif
    }
}
