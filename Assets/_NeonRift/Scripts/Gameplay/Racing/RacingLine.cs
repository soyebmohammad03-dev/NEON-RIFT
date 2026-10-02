using System.Collections.Generic;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A drivable line built from a <see cref="RoadPath"/>: the road centreline with rounded corners, resampled every
    /// 2 m, with per-sample tangent, usable lateral range (kerbs, median) and an advisory speed from curvature and the
    /// street's limit. Drivers steer to "point + right × offset", so lane choice and overtaking are just offsets.
    /// Open (not looped); the last sample can carry a stop.
    /// </summary>
    public sealed class RacingLine
    {
        public const float Spacing = 2f;

        private readonly List<Vector3> points = new();
        private readonly List<Vector3> rights = new();
        private readonly List<float> speeds = new();
        private readonly List<float> minOffset = new();
        private readonly List<float> maxOffset = new();
        private readonly List<float> laneOffset = new();
        private readonly List<float> cumulative = new();

        public int Count => points.Count;
        public float Length => cumulative.Count > 0 ? cumulative[^1] : 0f;
        public bool StopsAtEnd { get; private set; }
        public Vector3 PointAt(int i) => points[Clamp(i)];
        public Vector3 RightAt(int i) => rights[Clamp(i)];
        public float SpeedAt(int i) => speeds[Clamp(i)];
        public float MinOffset(int i) => minOffset[Clamp(i)];
        public float MaxOffset(int i) => maxOffset[Clamp(i)];
        /// <summary>Preferred offset: the fast lane of the travel direction (right-hand traffic).</summary>
        public float LaneOffset(int i) => laneOffset[Clamp(i)];
        public float DistanceAt(int i) => cumulative[Clamp(i)];
        public Vector3 End => points[^1];

        private int Clamp(int i) => Mathf.Clamp(i, 0, points.Count - 1);

        /// <summary>Builds the line. <paramref name="grip"/> is the lateral acceleration the driver commits to in corners, m/s².</summary>
        public void Build(RoadPath path, RoadNetwork network, float grip, float maxSpeed, float speedScale, bool stopAtEnd)
        {
            points.Clear(); rights.Clear(); speeds.Clear(); minOffset.Clear(); maxOffset.Clear(); laneOffset.Clear(); cumulative.Clear();
            StopsAtEnd = stopAtEnd;
            if (path == null || !path.IsValid) return;

            // Corner-rounded polyline with, per raw point, the edge it belongs to.
            var raw = new List<(Vector3 p, int edge)> { (path.Points[0], path.Edges[0]) };
            for (int i = 1; i < path.Points.Count - 1; i++)
            {
                Vector3 a = path.Points[i - 1], p = path.Points[i], b = path.Points[i + 1];
                Vector3 da = Flat(a - p), db = Flat(b - p);
                float turn = Vector3.Angle(-da, db);
                if (turn < 4f || da.sqrMagnitude < 1f || db.sqrMagnitude < 1f) { raw.Add((p, path.Edges[i])); continue; }
                var e0 = network.Edges[path.Edges[i - 1]];
                var e1 = network.Edges[path.Edges[i]];
                float cap = Mathf.Max(e0.halfWidth, e1.halfWidth) * 1.4f + 4f;
                float t = Mathf.Min(cap, Mathf.Min(da.magnitude, db.magnitude) * 0.45f);
                Vector3 p0 = p + (a - p).normalized * t, p2 = p + (b - p).normalized * t;
                for (int k = 0; k <= 8; k++)
                {
                    float u = k / 8f;
                    raw.Add(((1 - u) * (1 - u) * p0 + 2 * (1 - u) * u * p + u * u * p2, k < 4 ? path.Edges[i - 1] : path.Edges[i]));
                }
            }
            raw.Add((path.Points[^1], path.Edges[^1]));

            // Resample.
            var edges = new List<int>();
            points.Add(raw[0].p);
            edges.Add(raw[0].edge);
            float carry = 0f;
            for (int i = 1; i < raw.Count; i++)
            {
                Vector3 from = raw[i - 1].p, to = raw[i].p;
                float len = Vector3.Distance(from, to);
                if (len < 1e-4f) continue;
                float s = Spacing - carry;
                while (s <= len)
                {
                    points.Add(Vector3.Lerp(from, to, s / len));
                    edges.Add(raw[i].edge);
                    s += Spacing;
                }
                carry = len - (s - Spacing);
            }
            if ((points[^1] - raw[^1].p).sqrMagnitude > 0.01f) { points.Add(raw[^1].p); edges.Add(raw[^1].edge); }

            float run = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) run += Vector3.Distance(points[i - 1], points[i]);
                cumulative.Add(run);
                Vector3 tangent = Flat(points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]);
                Vector3 right = tangent.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, tangent.normalized) : Vector3.right;
                rights.Add(right);
                var e = network.Edges[edges[i]];
                float inner = e.median > 0f ? e.median + 1.2f : -e.halfWidth + 1.3f;
                float outer = e.halfWidth - 1.3f;
                if (outer < inner) outer = inner = (inner + outer) * 0.5f;
                minOffset.Add(inner);
                maxOffset.Add(outer);
                // Fast lane of the right-hand carriageway (single lane: its centre).
                float half = e.halfWidth - e.median;
                float lane = e.median + half * (e.lanesPerDirection >= 2 ? 0.3f : 0.5f);
                laneOffset.Add(e.roadClass is RoadClass.Alley or RoadClass.Service ? Mathf.Clamp(0f, inner, outer) : Mathf.Clamp(lane, inner, outer));
                speeds.Add(Mathf.Min(maxSpeed, e.speedLimit * speedScale));
            }
            // Curvature limit on the lane line, held from a little before the apex to a little after it.
            var curve = new float[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                int a = Mathf.Max(0, i - 3), b = Mathf.Min(points.Count - 1, i + 3);
                Vector3 pa = points[a] + rights[a] * laneOffset[a], pi = points[i] + rights[i] * laneOffset[i], pb = points[b] + rights[b] * laneOffset[b];
                float k = Curvature(pa, pi, pb);
                curve[i] = k > 1e-4f ? Mathf.Sqrt(grip / k) : float.MaxValue;
            }
            for (int i = 0; i < points.Count; i++)
                for (int k = Mathf.Max(0, i - 4); k <= Mathf.Min(points.Count - 1, i + 4); k++)
                    speeds[i] = Mathf.Min(speeds[i], curve[k]);
            if (stopAtEnd) speeds[^1] = 0f;
        }

        public int FindClosest(Vector3 position, int hint, int window = 30)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            int from = hint < 0 ? 0 : Mathf.Max(0, hint - window);
            int to = hint < 0 ? points.Count - 1 : Mathf.Min(points.Count - 1, hint + window);
            for (int i = from; i <= to; i++)
            {
                Vector3 d = points[i] - position;
                float sq = d.x * d.x + d.z * d.z + d.y * d.y * 4f;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        public int IndexAhead(int index, float distance)
        {
            float target = DistanceAt(index) + distance;
            int i = Clamp(index);
            while (i < points.Count - 1 && cumulative[i] < target) i++;
            return i;
        }

        public float Remaining(int index) => Length - DistanceAt(index);

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private static float Curvature(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector2 p = new(a.x, a.z), q = new(b.x, b.z), r = new(c.x, c.z);
            float area = Mathf.Abs((q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x)) * 0.5f;
            float d = (q - p).magnitude * (r - q).magnitude * (r - p).magnitude;
            return d > 1e-5f ? 4f * area / d : 0f;
        }
    }
}
