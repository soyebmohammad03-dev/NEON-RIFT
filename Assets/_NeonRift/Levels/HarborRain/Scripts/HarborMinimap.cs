using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// North-up minimap drawn with Painter2D around the player: dock roads, the restricted area, flood zones (blue
    /// when wet, amber when deep, red when closed), closed checkpoints, objective and extraction markers, AI cars and
    /// drones, and the player as an arrow rotated to the car's heading. World +Z is map up, +X is map right.
    /// </summary>
    public sealed class HarborMinimap : VisualElement
    {
        public struct Marker { public Vector3 Position; public Color Colour; public float Size; }

        private static readonly Color Background = new(0.015f, 0.025f, 0.04f, 0.78f);
        private static readonly Color Road = new(0.32f, 0.38f, 0.45f, 0.95f);
        private static readonly Color Cyan = new(0.33f, 0.91f, 1f);

        private readonly List<Rect> roads = new();
        private Rect restricted;
        private float range = 240f;
        private Vector3 centre;
        private float heading;
        private readonly List<(Rect area, Color colour)> zones = new();
        private readonly List<Marker> markers = new();
        private Vector3? objective;
        private readonly List<Vector3> route = new();
        private Color routeColour = new(0.33f, 0.91f, 1f, 0.9f);

        /// <summary>The command centre's suggested route (empty to hide). Amber after a reroute.</summary>
        public void SetRoute(IReadOnlyList<Vector3> points, bool rerouted)
        {
            route.Clear();
            if (points != null) route.AddRange(points);
            routeColour = rerouted ? new Color(1f, 0.75f, 0.25f, 0.95f) : new Color(0.33f, 0.91f, 1f, 0.9f);
        }

        public int RoutePoints => route.Count;

        public HarborMinimap()
        {
            style.width = 300;
            style.height = 300;
            style.overflow = Overflow.Hidden;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 2;
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void SetMap(IEnumerable<Rect> roadRects, Rect restrictedArea, float metres)
        {
            roads.Clear();
            roads.AddRange(roadRects);
            restricted = restrictedArea;
            range = metres;
        }

        public void SetFrame(Vector3 player, float headingDegrees, IEnumerable<(Rect, Color)> zoneStates, IEnumerable<Marker> others, Vector3? target)
        {
            centre = player;
            heading = headingDegrees;
            zones.Clear();
            zones.AddRange(zoneStates);
            markers.Clear();
            markers.AddRange(others);
            objective = target;
            MarkDirtyRepaint();
        }

        private Vector2 ToMap(Vector3 world, Vector2 size)
        {
            float scale = size.x * 0.5f / range;
            return new Vector2(size.x * 0.5f + (world.x - centre.x) * scale, size.y * 0.5f - (world.z - centre.z) * scale);
        }

        private void FillRect(Painter2D p, Rect r, Vector2 size, Color c)
        {
            var a = ToMap(new Vector3(r.xMin, 0f, r.yMin), size);
            var b = ToMap(new Vector3(r.xMax, 0f, r.yMax), size);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(a.x, b.y));
            p.LineTo(new Vector2(b.x, b.y));
            p.LineTo(new Vector2(b.x, a.y));
            p.LineTo(new Vector2(a.x, a.y));
            p.ClosePath();
            p.Fill();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var size = contentRect.size;
            if (size.x <= 1f) return;
            var p = ctx.painter2D;
            FillRect(p, new Rect(centre.x - range * 2f, centre.z - range * 2f, range * 4f, range * 4f), size, Background);
            FillRect(p, new Rect(-2000f, 300f, 4000f, 1500f), size, new Color(0.02f, 0.06f, 0.09f, 0.9f));   // the sea
            FillRect(p, restricted, size, new Color(0.6f, 0.15f, 0.3f, 0.35f));
            foreach (var r in roads) FillRect(p, r, size, Road);
            foreach (var (area, colour) in zones) FillRect(p, area, size, colour);

            if (route.Count > 1)
            {
                p.strokeColor = routeColour;
                p.lineWidth = 4f;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                p.MoveTo(ToMap(route[0], size));
                for (int i = 1; i < route.Count; i++) p.LineTo(ToMap(route[i], size));
                p.Stroke();
            }

            foreach (var m in markers)
            {
                var c = ToMap(m.Position, size);
                p.fillColor = m.Colour;
                p.BeginPath();
                p.Arc(c, m.Size, 0f, 360f);
                p.Fill();
            }
            if (objective.HasValue)
            {
                var o = ToMap(objective.Value, size);
                o.x = Mathf.Clamp(o.x, 10f, size.x - 10f);
                o.y = Mathf.Clamp(o.y, 10f, size.y - 10f);
                p.fillColor = Cyan;
                p.BeginPath();
                p.MoveTo(o + new Vector2(0f, -9f));
                p.LineTo(o + new Vector2(9f, 0f));
                p.LineTo(o + new Vector2(0f, 9f));
                p.LineTo(o + new Vector2(-9f, 0f));
                p.ClosePath();
                p.Fill();
            }

            // Player arrow: heading 0 = north (map up), clockwise.
            var centreMap = size * 0.5f;
            float rad = heading * Mathf.Deg2Rad;
            Vector2 R(Vector2 v) => new(v.x * Mathf.Cos(rad) - v.y * Mathf.Sin(rad), v.x * Mathf.Sin(rad) + v.y * Mathf.Cos(rad));
            p.fillColor = Color.white;
            p.BeginPath();
            p.MoveTo(centreMap + R(new Vector2(0f, -11f)));
            p.LineTo(centreMap + R(new Vector2(7f, 8f)));
            p.LineTo(centreMap + R(new Vector2(0f, 4f)));
            p.LineTo(centreMap + R(new Vector2(-7f, 8f)));
            p.ClosePath();
            p.Fill();
        }
    }
}
