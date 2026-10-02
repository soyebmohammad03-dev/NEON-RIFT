using System.Collections.Generic;
using NeonRift.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Heading-up minimap drawn with Painter2D: roads of the <see cref="RoadNetwork"/> by class, closed gates in red,
    /// the GPS route to the objective, rival and target markers, the player arrow at the centre. A passive view:
    /// the HUD pushes state in and calls <see cref="MarkDirtyRepaint"/> at a modest rate.
    /// </summary>
    public sealed class CityMinimap : VisualElement
    {
        private static readonly Color RoadArterial = new(0.42f, 0.5f, 0.62f, 0.9f);
        private static readonly Color RoadStreet = new(0.3f, 0.36f, 0.45f, 0.85f);
        private static readonly Color RoadMinor = new(0.22f, 0.25f, 0.32f, 0.8f);
        private static readonly Color Skyway = new(0.35f, 0.85f, 1f, 0.9f);
        private static readonly Color Closed = new(1f, 0.2f, 0.25f, 1f);
        private static readonly Color RouteCalm = new(0.3f, 0.95f, 1f, 1f);
        private static readonly Color RouteLockdown = new(1f, 0.3f, 0.75f, 1f);
        private static readonly Color RivalColour = new(1f, 0.62f, 0.15f, 1f);

        private RoadNetwork network;
        private System.Func<int, bool> isClosed;
        private readonly List<Vector3> route = new();
        private readonly List<Vector3> rivals = new();
        private Vector3 centre, target;
        private float heading;
        private bool hasTarget, lockdown;

        /// <summary>Metres from the centre to the edge of the map.</summary>
        public float Range { get; set; } = 240f;

        public CityMinimap()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void SetNetwork(RoadNetwork roadNetwork, System.Func<int, bool> closed)
        {
            network = roadNetwork;
            isClosed = closed;
        }

        public void SetView(Vector3 position, float headingDegrees, bool lockdownState)
        {
            centre = position;
            heading = headingDegrees;
            lockdown = lockdownState;
        }

        public void SetRoute(IReadOnlyList<Vector3> points)
        {
            route.Clear();
            if (points != null) route.AddRange(points);
        }

        public void SetTarget(bool visible, Vector3 position)
        {
            hasTarget = visible;
            target = position;
        }

        public void SetRivals(IEnumerable<Vector3> positions)
        {
            rivals.Clear();
            if (positions != null) rivals.AddRange(positions);
        }

        private Vector2 ToMap(Vector3 world, Vector2 size)
        {
            float scale = size.x * 0.5f / Range;
            float r = -heading * Mathf.Deg2Rad;
            float dx = world.x - centre.x, dz = world.z - centre.z;
            float x = dx * Mathf.Cos(r) - dz * Mathf.Sin(r);
            float z = dx * Mathf.Sin(r) + dz * Mathf.Cos(r);
            return size * 0.5f + new Vector2(x, -z) * scale;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var size = contentRect.size;
            if (size.x < 4f || network == null) return;
            var p = ctx.painter2D;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            float scale = size.x * 0.5f / Range;
            float reach = Range * 1.5f;

            var edges = network.Edges;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < edges.Count; i++)
                {
                    var e = edges[i];
                    bool skyway = e.roadClass == RoadClass.Skyway;
                    if ((pass == 1) != skyway) continue;     // skyway drawn over the streets
                    Vector3 a = network.NodePosition(e.a), b = network.NodePosition(e.b);
                    if (OutOfReach(a, b, reach)) continue;
                    bool closed = isClosed != null && isClosed(i);
                    p.strokeColor = closed ? Closed : skyway ? Skyway : e.roadClass switch
                    {
                        RoadClass.Arterial => RoadArterial, RoadClass.Street => RoadStreet, _ => RoadMinor
                    };
                    p.lineWidth = Mathf.Max(1.5f, e.halfWidth * 2f * scale * (closed ? 1.3f : 1f));
                    p.BeginPath();
                    p.MoveTo(ToMap(a, size));
                    p.LineTo(ToMap(b, size));
                    p.Stroke();
                }

            if (route.Count >= 2)
            {
                p.strokeColor = lockdown ? RouteLockdown : RouteCalm;
                p.lineWidth = 3f;
                p.BeginPath();
                p.MoveTo(ToMap(route[0], size));
                for (int i = 1; i < route.Count; i++) p.LineTo(ToMap(route[i], size));
                p.Stroke();
            }

            foreach (var r in rivals) Dot(p, Clamp(ToMap(r, size), size), 4.5f, RivalColour);
            if (hasTarget)
            {
                var t = Clamp(ToMap(target, size), size);
                Dot(p, t, 7f, lockdown ? RouteLockdown : RouteCalm);
                Dot(p, t, 3f, Color.white);
            }

            // Player: an arrow pointing up (heading-up map).
            var c = size * 0.5f;
            p.fillColor = Color.white;
            p.BeginPath();
            p.MoveTo(c + new Vector2(0f, -9f));
            p.LineTo(c + new Vector2(6.5f, 7f));
            p.LineTo(c + new Vector2(0f, 3.5f));
            p.LineTo(c + new Vector2(-6.5f, 7f));
            p.ClosePath();
            p.Fill();
        }

        private bool OutOfReach(Vector3 a, Vector3 b, float reach)
        {
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x), minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
            return maxX < centre.x - reach || minX > centre.x + reach || maxZ < centre.z - reach || minZ > centre.z + reach;
        }

        /// <summary>Keeps off-map markers on the map's rim (round map).</summary>
        private static Vector2 Clamp(Vector2 point, Vector2 size)
        {
            var c = size * 0.5f;
            var d = point - c;
            float max = size.x * 0.5f - 8f;
            return d.magnitude > max ? c + d.normalized * max : point;
        }

        private static void Dot(Painter2D p, Vector2 at, float radius, Color colour)
        {
            p.fillColor = colour;
            p.BeginPath();
            p.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }
    }
}
