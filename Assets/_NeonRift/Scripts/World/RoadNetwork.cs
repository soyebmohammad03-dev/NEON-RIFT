using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.World
{
    public enum RoadClass
    {
        Arterial,
        Street,
        Alley,
        Service,
        Skyway,
        Tunnel
    }

    [Serializable]
    public struct RoadNode
    {
        public Vector3 position;
    }

    [Serializable]
    public struct RoadEdge
    {
        public int a, b;
        [Tooltip("Half the carriageway width, m.")]
        public float halfWidth;
        public int lanesPerDirection;
        [Tooltip("Half width of a central median (pillars, planters) traffic keeps out of, m.")]
        public float median;
        public RoadClass roadClass;
        public string street;
        [Tooltip("Advisory top speed on this edge, m/s.")]
        public float speedLimit;
        [Tooltip("Id of a scene barrier that can close this edge (empty = never closes).")]
        public string blockerId;
    }

    [Serializable]
    public struct CityDistrict
    {
        public string id;
        public string displayName;
        [Tooltip("XZ area (x, z, width, depth). The first district containing a point wins.")]
        public Rect area;
        public Color colour;
    }

    /// <summary>
    /// The drivable road graph of a city: nodes at intersections and road ends, edges with width, lanes, class,
    /// street name and an optional blocker id (security gates). Generated with the district, used at runtime for
    /// navigation (HUD route, AI drivers, race standings) and location readouts. Pure data: see <see cref="RoadGraph"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/World/Road Network", fileName = "RoadNetwork")]
    public sealed class RoadNetwork : ScriptableObject
    {
        [SerializeField] private string cityName;
        [SerializeField] private List<RoadNode> nodes = new();
        [SerializeField] private List<RoadEdge> edges = new();
        [SerializeField] private List<CityDistrict> districts = new();
        [Tooltip("XZ extent of the drivable city (minimap framing).")]
        [SerializeField] private Rect bounds;

        public string CityName => cityName;
        public IReadOnlyList<RoadNode> Nodes => nodes;
        public IReadOnlyList<RoadEdge> Edges => edges;
        public IReadOnlyList<CityDistrict> Districts => districts;
        public Rect Bounds => bounds;

        public Vector3 NodePosition(int index) => nodes[index].position;

        /// <summary>The district containing <paramref name="position"/>, or false outside every district.</summary>
        public bool TryGetDistrict(Vector3 position, out CityDistrict district)
        {
            var p = new Vector2(position.x, position.z);
            foreach (var d in districts)
                if (d.area.Contains(p))
                {
                    district = d;
                    return true;
                }
            district = default;
            return false;
        }

        /// <summary>Nearest point on any edge (XZ distance, elevation used to tell stacked roads apart).</summary>
        public bool TryGetNearestEdge(Vector3 position, out int edgeIndex, out float t, out float distance)
        {
            edgeIndex = -1;
            t = 0f;
            distance = float.MaxValue;
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                Vector3 a = nodes[e.a].position, b = nodes[e.b].position;
                float u = ProjectXZ(position, a, b);
                Vector3 p = Vector3.Lerp(a, b, u);
                float dx = position.x - p.x, dz = position.z - p.z;
                // A car on the skyway deck is not on the street beneath it.
                float dy = Mathf.Max(0f, Mathf.Abs(position.y - p.y) - 2.5f) * 4f;
                float d = Mathf.Sqrt(dx * dx + dz * dz) + dy;
                if (d < distance)
                {
                    distance = d;
                    edgeIndex = i;
                    t = u;
                }
            }
            return edgeIndex >= 0;
        }

        /// <summary>Parameter (0..1) of the point on segment a–b nearest to p, in the XZ plane.</summary>
        public static float ProjectXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float len = abx * abx + abz * abz;
            if (len < 1e-6f) return 0f;
            return Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / len);
        }

        /// <summary>Returns human-readable problems; empty when the network is valid.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e.a < 0 || e.a >= nodes.Count || e.b < 0 || e.b >= nodes.Count) problems.Add($"Edge {i} references a missing node.");
                else if (e.a == e.b) problems.Add($"Edge {i} is a loop.");
                if (e.halfWidth <= 0f) problems.Add($"Edge {i} ({e.street}) has no width.");
            }
            if (nodes.Count > 0 && edges.Count > 0)
            {
                var graph = new RoadGraph(this);
                int reached = graph.CountReachable(0);
                if (reached != nodes.Count) problems.Add($"Only {reached}/{nodes.Count} nodes are connected to node 0.");
            }
            return problems;
        }

#if UNITY_EDITOR
        public void EditorConfigure(string city, List<RoadNode> roadNodes, List<RoadEdge> roadEdges, List<CityDistrict> cityDistricts, Rect cityBounds)
        {
            cityName = city;
            nodes = roadNodes;
            edges = roadEdges;
            districts = cityDistricts;
            bounds = cityBounds;
        }
#endif
    }
}
