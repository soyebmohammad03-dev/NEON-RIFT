using System.Collections.Generic;
using NeonRift.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// North-up minimap drawn with Painter2D: roads of the <see cref="RoadNetwork"/> by class, closed gates in red,
    /// the GPS route to the objective, rival and target markers. The map is centred on the player but never rotates
    /// (north is always up, see <see cref="MinimapProjection"/>); the player arrow and rival chevrons turn with their
    /// world heading. A passive view: the HUD pushes state in and calls <see cref="MarkDirtyRepaint"/> at a modest rate.
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
        private readonly List<(Vector3 position, float heading)> rivals = new();
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

        /// <param name="headingDegrees">Compass heading of the player (0 = north, clockwise), see <see cref="MinimapProjection.Heading"/>.</param>
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

        /// <summary>Other mission cars: world position and compass heading.</summary>
        public void SetRivals(IEnumerable<(Vector3 position, float heading)> cars)
        {
            rivals.Clear();
            if (cars != null) rivals.AddRange(cars);
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var size = contentRect.size;
            if (size.x < 4f || network == null) return;
            var p = ctx.painter2D;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            var map = new MinimapProjection(centre, size, Range);
            float scale = map.Scale;
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
                    p.MoveTo(map.ToMap(a));
                    p.LineTo(map.ToMap(b));
                    p.Stroke();
                }

            if (route.Count >= 2)
            {
                p.strokeColor = lockdown ? RouteLockdown : RouteCalm;
                p.lineWidth = 3f;
                p.BeginPath();
                p.MoveTo(map.ToMap(route[0]));
                for (int i = 1; i < route.Count; i++) p.LineTo(map.ToMap(route[i]));
                p.Stroke();
            }

            foreach (var r in rivals) Arrow(p, Clamp(map.ToMap(r.position), size), r.heading, 0.62f, RivalColour);
            if (hasTarget)
            {
                var t = Clamp(map.ToMap(target), size);
                Dot(p, t, 7f, lockdown ? RouteLockdown : RouteCalm);
                Dot(p, t, 3f, Color.white);
            }

            // North tick on the rim: the map is north-up, this just makes it legible.
            var north = new Vector2(size.x * 0.5f, 9f);
            p.fillColor = new Color(1f, 1f, 1f, 0.85f);
            p.BeginPath();
            p.MoveTo(north + new Vector2(0f, -5f));
            p.LineTo(north + new Vector2(4.5f, 4f));
            p.LineTo(north + new Vector2(-4.5f, 4f));
            p.ClosePath();
            p.Fill();

            // Player: an arrow at the centre turned to the car's world heading.
            Arrow(p, size * 0.5f, heading, 1f, Color.white);
        }

        private static readonly Vector2[] ArrowShape = { new(0f, -9f), new(6.5f, 7f), new(0f, 3.5f), new(-6.5f, 7f) };

        private static void Arrow(Painter2D p, Vector2 at, float headingDegrees, float size, Color colour)
        {
            p.fillColor = colour;
            p.BeginPath();
            p.MoveTo(at + MinimapProjection.MarkerRotation(ArrowShape[0] * size, headingDegrees));
            for (int i = 1; i < ArrowShape.Length; i++) p.LineTo(at + MinimapProjection.MarkerRotation(ArrowShape[i] * size, headingDegrees));
            p.ClosePath();
            p.Fill();
        }

        /// <summary>Dev readout: player heading and where the objective sits on the (north-up) map.</summary>
        public string Describe()
        {
            var map = new MinimapProjection(centre, contentRect.size, Range);
            string t = hasTarget ? $"target px {map.ToMap(target):F0} (world {target.x:0},{target.z:0})" : "no target";
            return $"north-up, heading {heading:0}°, {t}";
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
