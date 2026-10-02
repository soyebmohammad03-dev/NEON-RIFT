using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A closed driving line with advisory speeds and named sections. Used by autopilot/AI drivers and by the
    /// validation tools that report per-section results. Points are in world space at road-surface height.
    /// </summary>
    public sealed class DrivingRoute : MonoBehaviour
    {
        [Serializable]
        public struct Section
        {
            public string name;
            [Tooltip("Distance along the route where the section starts, m.")]
            public float startDistance;
        }

        [SerializeField] private List<Vector3> points = new();
        [Tooltip("Advisory speed at each point, m/s.")]
        [SerializeField] private List<float> speeds = new();
        [SerializeField] private List<Section> sections = new();
        [SerializeField] private float length;

        private float[] cumulative;

        public int Count => points.Count;
        public float Length => length;
        public IReadOnlyList<Section> Sections => sections;
        public Vector3 PointAt(int index) => points[Wrap(index)];
        public float SpeedAt(int index) => speeds[Wrap(index)];

        public int Wrap(int index)
        {
            int n = points.Count;
            return ((index % n) + n) % n;
        }

        /// <summary>Distance along the route to point <paramref name="index"/>, m.</summary>
        public float DistanceAt(int index)
        {
            EnsureCumulative();
            return cumulative[Wrap(index)];
        }

        /// <summary>Nearest point, searching around <paramref name="hint"/> (pass −1 for a full search).</summary>
        public int FindClosest(Vector3 position, int hint = -1, int window = 40)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            int from = hint < 0 ? 0 : hint - window;
            int to = hint < 0 ? points.Count - 1 : hint + window;
            for (int i = from; i <= to; i++)
            {
                float d = (points[Wrap(i)] - position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = Wrap(i); }
            }
            return best;
        }

        /// <summary>Index of the point roughly <paramref name="distance"/> metres ahead of <paramref name="index"/>.</summary>
        public int IndexAhead(int index, float distance)
        {
            float travelled = 0f;
            int i = Wrap(index);
            for (int guard = 0; guard < points.Count && travelled < distance; guard++)
            {
                int next = Wrap(i + 1);
                travelled += Vector3.Distance(points[i], points[next]);
                i = next;
            }
            return i;
        }

        public string SectionAt(float routeDistance)
        {
            string current = sections.Count > 0 ? sections[0].name : string.Empty;
            foreach (var s in sections)
                if (routeDistance >= s.startDistance) current = s.name;
            return current;
        }

        private void EnsureCumulative()
        {
            if (cumulative != null && cumulative.Length == points.Count) return;
            cumulative = new float[points.Count];
            for (int i = 1; i < points.Count; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }

#if UNITY_EDITOR
        public void EditorConfigure(List<Vector3> routePoints, List<float> advisorySpeeds, List<Section> routeSections)
        {
            points = routePoints;
            speeds = advisorySpeeds;
            sections = routeSections;
            cumulative = null;
            length = 0f;
            for (int i = 0; i < points.Count; i++) length += Vector3.Distance(points[i], points[(i + 1) % points.Count]);
        }

        private void OnDrawGizmosSelected()
        {
            for (int i = 0; i < points.Count; i++)
            {
                float t = speeds.Count > i ? Mathf.InverseLerp(10f, 70f, speeds[i]) : 0f;
                Gizmos.color = Color.Lerp(Color.red, Color.green, t);
                Gizmos.DrawLine(points[i] + Vector3.up * 0.3f, points[(i + 1) % points.Count] + Vector3.up * 0.3f);
            }
        }
#endif
    }
}
