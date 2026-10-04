using System.IO;
using NeonRift.EditorTools.District;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Frontend
{
    /// <summary>
    /// The crew's planning wall: textures generated from the real city data. A map of the streets (from
    /// <see cref="CityLayout.Roads"/>) with the garage, the planned route to the Data Core, the compound and the Rift Gate
    /// marked, and a schematic of the core itself. Deterministic, so rebuilding gives the same images.
    /// </summary>
    public static class GaragePlanningWall
    {
        private const string Folder = "Assets/_NeonRift/Art/Garage/Textures";
        private const int W = 1024, H = 538;
        // World rect shown on the map (aspect matches the 1.75 × 0.92 m screens).
        private static readonly Rect MapRect = Rect.MinMaxRect(-420f, -470f, 1100f, 330f);

        public static Material[] Screens()
        {
            NeonRift.EditorTools.Vehicles.VehiclePrefabBuilder.EnsureFolder(Folder);
            var map = Save("Garage_PlanMap", Map());
            var core = Save("Garage_PlanCore", Core());
            return new[]
            {
                DistrictKit.Lit("Garage_PlanMap", Color.black, 0.75f, 0f, emissionMap: map, emission: new Color(1.3f, 1.3f, 1.3f)),
                DistrictKit.Lit("Garage_PlanCore", Color.black, 0.75f, 0f, emissionMap: core, emission: new Color(1.3f, 1.3f, 1.3f)),
            };
        }

        private static Color32[] Map()
        {
            var px = Fill(new Color(0.012f, 0.02f, 0.03f));
            // 100 m grid.
            for (float x = Mathf.Ceil(MapRect.xMin / 100f) * 100f; x < MapRect.xMax; x += 100f) Line(px, new Vector2(x, MapRect.yMin), new Vector2(x, MapRect.yMax), 1f, new Color(0.03f, 0.07f, 0.09f));
            for (float z = Mathf.Ceil(MapRect.yMin / 100f) * 100f; z < MapRect.yMax; z += 100f) Line(px, new Vector2(MapRect.xMin, z), new Vector2(MapRect.xMax, z), 1f, new Color(0.03f, 0.07f, 0.09f));
            foreach (var r in CityLayout.Roads)
            {
                var c = r.Class switch
                {
                    NeonRift.World.RoadClass.Arterial => new Color(0.1f, 0.42f, 0.55f),
                    NeonRift.World.RoadClass.Skyway => new Color(0.5f, 0.2f, 0.6f),
                    NeonRift.World.RoadClass.Service => new Color(0.55f, 0.12f, 0.35f),
                    NeonRift.World.RoadClass.Alley => new Color(0.06f, 0.22f, 0.3f),
                    _ => new Color(0.08f, 0.3f, 0.4f)
                };
                Line(px, new Vector2(r.A.x, r.A.z), new Vector2(r.B.x, r.B.z), Mathf.Max(2f, r.HalfWidth * 2f * Scale), c);
            }
            // The compound, the route in (garage → W Avenue → North Boulevard → Access Road), the target and the exit.
            var compound = Rect.MinMaxRect(105f, 60f, 215f, 195f);
            Box(px, compound, new Color(0.9f, 0.15f, 0.45f));
            Vector2[] route = { new(-11f, -255f), new(3f, -255f), new(3f, 296f), new(157f, 296f), new(157f, 150f) };
            for (int i = 1; i < route.Length; i++) Dashed(px, route[i - 1], route[i], 3f, new Color(1f, 0.72f, 0.16f));
            Ring(px, new Vector2(160f, 127.5f), 16f, 3f, new Color(1f, 0.25f, 0.55f));
            Ring(px, new Vector2(160f, 127.5f), 6f, 6f, new Color(1f, 0.25f, 0.55f));
            Ring(px, new Vector2(-27f, -255f), 7f, 4f, new Color(0.3f, 1f, 0.9f));
            Ring(px, new Vector2(320f, -440f), 10f, 3f, new Color(0.8f, 0.3f, 1f));
            return px;
        }

        private static Color32[] Core()
        {
            var px = Fill(new Color(0.012f, 0.018f, 0.028f));
            var c = new Vector2(W * 0.32f, H * 0.5f);
            for (int i = 0; i < 6; i++) RingPx(px, c, 40f + i * 34f, i == 3 ? 5f : 2f, i == 3 ? new Color(1f, 0.3f, 0.6f) : new Color(0.15f, 0.55f, 0.75f));
            for (int k = 0; k < 12; k++)
            {
                float a = k * 30f * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                LinePx(px, c + d * 40f, c + d * 210f, 1.5f, new Color(0.1f, 0.35f, 0.5f));
            }
            // Readout bars on the right: partition table, link margin, trace risk.
            for (int b = 0; b < 9; b++)
            {
                float y = H * 0.18f + b * 38f;
                float len = 120f + (b * 53 % 7) * 32f;
                LinePx(px, new Vector2(W * 0.62f, y), new Vector2(W * 0.62f + 330f, y), 12f, new Color(0.04f, 0.09f, 0.12f));
                LinePx(px, new Vector2(W * 0.62f, y), new Vector2(W * 0.62f + len, y), 12f, b == 6 ? new Color(1f, 0.45f, 0.15f) : new Color(0.2f, 0.75f, 0.95f));
            }
            return px;
        }

        // ---------------- Raster helpers ----------------

        private static float Scale => W / MapRect.width;

        private static Vector2 ToPx(Vector2 world) => new((world.x - MapRect.xMin) / MapRect.width * W, (world.y - MapRect.yMin) / MapRect.height * H);

        private static Color32[] Fill(Color c)
        {
            var px = new Color32[W * H];
            Color32 c32 = c;
            for (int i = 0; i < px.Length; i++) px[i] = c32;
            return px;
        }

        private static void Line(Color32[] px, Vector2 a, Vector2 b, float width, Color c) => LinePx(px, ToPx(a), ToPx(b), width, c);

        private static void LinePx(Color32[] px, Vector2 a, Vector2 b, float width, Color c)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len));
            for (int s = 0; s <= steps; s++) Dot(px, Vector2.Lerp(a, b, s / (float)steps), width * 0.5f, c);
        }

        private static void Dashed(Color32[] px, Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 pa = ToPx(a), pb = ToPx(b);
            float len = Vector2.Distance(pa, pb);
            for (float t = 0f; t < len; t += 14f) LinePx(px, Vector2.Lerp(pa, pb, t / len), Vector2.Lerp(pa, pb, Mathf.Min(1f, (t + 8f) / len)), width, c);
        }

        private static void Box(Color32[] px, Rect r, Color c)
        {
            Line(px, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), 2f, c);
            Line(px, new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), 2f, c);
            Line(px, new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), 2f, c);
            Line(px, new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), 2f, c);
        }

        private static void Ring(Color32[] px, Vector2 world, float radiusPx, float width, Color c) => RingPx(px, ToPx(world), radiusPx, width, c);

        private static void RingPx(Color32[] px, Vector2 centre, float radius, float width, Color c)
        {
            int n = Mathf.Max(24, Mathf.CeilToInt(radius * 6.3f));
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                Dot(px, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, width * 0.5f, c);
            }
        }

        private static void Dot(Color32[] px, Vector2 p, float r, Color c)
        {
            int x0 = Mathf.FloorToInt(p.x - r), x1 = Mathf.CeilToInt(p.x + r), y0 = Mathf.FloorToInt(p.y - r), y1 = Mathf.CeilToInt(p.y + r);
            Color32 c32 = c;
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(H - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(W - 1, x1); x++)
                    if ((new Vector2(x, y) - p).sqrMagnitude <= r * r + 0.5f) px[y * W + x] = c32;
        }

        private static Texture2D Save(string name, Color32[] pixels)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            string path = $"{Folder}/{name}.png";
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
