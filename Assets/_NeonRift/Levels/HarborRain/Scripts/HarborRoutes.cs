using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The harbour's road graph, built from the same road rectangles the level and the minimap use: a node at every
    /// junction and dead end, an edge along each road between consecutive nodes. <see cref="Find"/> runs Dijkstra
    /// from any point on a road to any other, skipping edges that pass through a closure (flood, sealed checkpoint,
    /// patrol roadblock, wave-swept section, closed gate) and adding cost for hazards (drone search areas, the rival
    /// crew). It only runs when the mission asks (about once a second plus on world events), never per frame.
    /// </summary>
    public sealed class HarborRoutes
    {
        private struct Edge { public int A, B, Road; public float Length; }

        private readonly Rect[] roads;
        private readonly List<Vector3> nodes = new();
        private readonly List<Edge> edges = new();
        private readonly List<int>[] adjacency;

        public readonly List<(Rect area, string label)> Closures = new();
        public readonly List<(Rect area, float extraMetres, string label)> Hazards = new();

        public int NodeCount => nodes.Count;
        public int EdgeCount => edges.Count;

        public HarborRoutes(Rect[] roadRects)
        {
            roads = roadRects;
            var perRoad = new List<float>[roads.Length];
            for (int i = 0; i < roads.Length; i++) perRoad[i] = new List<float> { Start(i), End(i) };
            for (int i = 0; i < roads.Length; i++)
            for (int j = i + 1; j < roads.Length; j++)
            {
                if (AlongZ(i) == AlongZ(j)) continue;
                var a = Grow(roads[i], 1.5f);
                if (!a.Overlaps(Grow(roads[j], 1.5f))) continue;
                int vertical = AlongZ(i) ? i : j, horizontal = vertical == i ? j : i;
                perRoad[vertical].Add(roads[horizontal].center.y);
                perRoad[horizontal].Add(roads[vertical].center.x);
            }
            var nodeOnRoad = new List<int>[roads.Length];
            for (int i = 0; i < roads.Length; i++)
            {
                perRoad[i].Sort();
                nodeOnRoad[i] = new List<int>();
                foreach (float s in perRoad[i])
                {
                    var p = Point(i, s);
                    int n = FindNode(p);
                    if (n < 0) { n = nodes.Count; nodes.Add(p); }
                    if (!nodeOnRoad[i].Contains(n)) nodeOnRoad[i].Add(n);
                }
                for (int k = 1; k < nodeOnRoad[i].Count; k++)
                {
                    int a = nodeOnRoad[i][k - 1], b = nodeOnRoad[i][k];
                    edges.Add(new Edge { A = a, B = b, Road = i, Length = Vector3.Distance(nodes[a], nodes[b]) });
                }
            }
            adjacency = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) adjacency[i] = new List<int>();
            for (int e = 0; e < edges.Count; e++) { adjacency[edges[e].A].Add(e); adjacency[edges[e].B].Add(e); }
        }

        private bool AlongZ(int i) => roads[i].height > roads[i].width;
        private float Start(int i) => AlongZ(i) ? roads[i].yMin : roads[i].xMin;
        private float End(int i) => AlongZ(i) ? roads[i].yMax : roads[i].xMax;
        private Vector3 Point(int i, float s) => AlongZ(i) ? new Vector3(roads[i].center.x, 0f, s) : new Vector3(s, 0f, roads[i].center.y);
        private static Rect Grow(Rect r, float m) => new(r.x - m, r.y - m, r.width + m * 2f, r.height + m * 2f);

        private int FindNode(Vector3 p)
        {
            for (int i = 0; i < nodes.Count; i++) if ((nodes[i] - p).sqrMagnitude < 9f) return i;
            return -1;
        }

        /// <summary>Nearest road to a point and the point's projection onto its centre line.</summary>
        public int NearestRoad(Vector3 p, out Vector3 onRoad)
        {
            int best = -1;
            float bestD = float.MaxValue;
            onRoad = p;
            for (int i = 0; i < roads.Length; i++)
            {
                float s = Mathf.Clamp(AlongZ(i) ? p.z : p.x, Start(i), End(i));
                var q = Point(i, s);
                float d = (new Vector3(p.x, 0f, p.z) - q).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; onRoad = q; }
            }
            return best;
        }

        private readonly List<(Rect area, string label)> active = new();

        private bool Blocked(Vector3 a, Vector3 b, out string why)
        {
            why = null;
            float len = Vector3.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 3f));
            for (int k = 0; k <= steps; k++)
            {
                var p = Vector3.Lerp(a, b, k / (float)steps);
                foreach (var (area, label) in active)
                    if (area.Contains(new Vector2(p.x, p.z))) { why = label; return true; }
            }
            return false;
        }

        private float Penalty(Vector3 a, Vector3 b)
        {
            // Each hazard counts once if any part of the edge (sampled every 4 m) runs through it.
            float extra = 0f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 4f));
            foreach (var (area, metres, _) in Hazards)
                for (int k = 0; k <= steps; k++)
                {
                    var p = Vector3.Lerp(a, b, k / (float)steps);
                    if (area.Contains(new Vector2(p.x, p.z))) { extra += metres; break; }
                }
            return extra;
        }

        /// <summary>
        /// Shortest open route from <paramref name="from"/> to <paramref name="to"/> as a polyline along road centre lines
        /// (starting at the car, ending at the target). False when every route is closed; <paramref name="blockedBy"/> then
        /// names the closure on the otherwise-best route.
        /// </summary>
        public bool Find(Vector3 from, Vector3 to, List<Vector3> path, out float metres, out string blockedBy)
        {
            path.Clear();
            metres = 0f;
            blockedBy = null;
            int roadA = NearestRoad(from, out var a), roadB = NearestRoad(to, out var b);
            if (roadA < 0 || roadB < 0) return false;
            // A closure the car (or the target) is already inside never blocks: the car can always drive out.
            active.Clear();
            foreach (var c in Closures)
                if (!Grow(c.area, 6f).Contains(new Vector2(from.x, from.z)) && !c.area.Contains(new Vector2(to.x, to.z))) active.Add(c);

            // Virtual start/end nodes joined to their road's neighbouring junctions.
            int start = nodes.Count, goal = nodes.Count + 1;
            var dist = new float[nodes.Count + 2];
            var prev = new int[nodes.Count + 2];
            var done = new bool[nodes.Count + 2];
            for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            Vector3 P(int i) => i == start ? a : i == goal ? b : nodes[i];

            List<(int to, float cost)> Neighbours(int n)
            {
                var list = new List<(int, float)>();
                if (n == start || n == goal)
                {
                    int road = n == start ? roadA : roadB;
                    foreach (var (node, _) in RoadNeighbours(road, P(n))) list.Add((node, 0f));
                    if (n == start && roadA == roadB) list.Add((goal, 0f));
                    return list;
                }
                foreach (int e in adjacency[n]) list.Add((edges[e].A == n ? edges[e].B : edges[e].A, 0f));
                foreach (var (node, _) in RoadNeighbours(roadB, b)) if (node == n) list.Add((goal, 0f));
                return list;
            }

            dist[start] = 0f;
            string firstBlock = null;
            for (int iter = 0; iter < dist.Length; iter++)
            {
                int u = -1;
                for (int i = 0; i < dist.Length; i++) if (!done[i] && dist[i] < float.MaxValue && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || u == goal) break;
                done[u] = true;
                foreach (var (v, _) in Neighbours(u))
                {
                    if (done[v]) continue;
                    if (Blocked(P(u), P(v), out var why)) { firstBlock ??= why; continue; }
                    float c = dist[u] + Vector3.Distance(P(u), P(v)) + Penalty(P(u), P(v));
                    if (c < dist[v]) { dist[v] = c; prev[v] = u; }
                }
            }
            if (dist[goal] == float.MaxValue) { blockedBy = firstBlock ?? "no road"; return false; }
            for (int n = goal; n >= 0; n = prev[n]) path.Insert(0, P(n));
            for (int i = 1; i < path.Count; i++) metres += Vector3.Distance(path[i - 1], path[i]);
            return true;
        }

        /// <summary>The junctions either side of a point on a road.</summary>
        private IEnumerable<(int node, float s)> RoadNeighbours(int road, Vector3 p)
        {
            float s = AlongZ(road) ? p.z : p.x;
            int below = -1, above = -1;
            float sb = float.MinValue, sa = float.MaxValue;
            foreach (var e in edges)
            {
                if (e.Road != road) continue;
                foreach (int n in new[] { e.A, e.B })
                {
                    float ns = AlongZ(road) ? nodes[n].z : nodes[n].x;
                    if (ns <= s && ns > sb) { sb = ns; below = n; }
                    if (ns >= s && ns < sa) { sa = ns; above = n; }
                }
            }
            if (below >= 0) yield return (below, sb);
            if (above >= 0 && above != below) yield return (above, sa);
        }

        /// <summary>Indices of the roads a polyline runs along, for comparing two routes.</summary>
        public string Signature(List<Vector3> path)
        {
            var sb = new System.Text.StringBuilder();
            int last = -1;
            for (int i = 1; i < path.Count; i++)
            {
                int r = NearestRoad((path[i - 1] + path[i]) * 0.5f, out _);
                if (r != last) { sb.Append(r).Append('.'); last = r; }
            }
            return sb.ToString();
        }

        /// <summary>The first point on the route at least <paramref name="ahead"/> metres along it.</summary>
        public static Vector3 Lookahead(List<Vector3> path, float ahead)
        {
            if (path.Count == 0) return Vector3.zero;
            float run = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float d = Vector3.Distance(path[i - 1], path[i]);
                if (run + d >= ahead) return Vector3.Lerp(path[i - 1], path[i], (ahead - run) / Mathf.Max(0.01f, d));
                run += d;
            }
            return path[^1];
        }
    }
}
