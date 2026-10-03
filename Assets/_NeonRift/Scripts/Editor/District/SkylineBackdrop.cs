using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// The metropolis beyond the playable city: three rings of tower silhouettes (1.7, 2.1 and 2.55 km from the centre)
    /// drawn with <c>NeonRift/SkylineBackdrop</c>. Each ring is hazier than the one in front, a few downtown clusters
    /// rise above the rest, and the tallest towers carry blinking aviation lights. About 3 k triangles in one draw,
    /// no colliders, no shadows: it replaces full building models at distances where fog would have erased them.
    /// Deterministic (fixed seed), regenerated with the district.
    /// </summary>
    public static class SkylineBackdrop
    {
        public static readonly Vector2 Centre = CityLayout.WorldBounds.center;

        /// <summary>The camera far plane must reach the outer ring from anywhere in the city.</summary>
        public const float RequiredFarClip = 3500f;

        private readonly struct Ring
        {
            public readonly float Radius, Haze, MinWidth, MaxWidth, MinHeight, MaxHeight;

            public Ring(float radius, float haze, float minWidth, float maxWidth, float minHeight, float maxHeight)
            {
                Radius = radius;
                Haze = haze;
                MinWidth = minWidth;
                MaxWidth = maxWidth;
                MinHeight = minHeight;
                MaxHeight = maxHeight;
            }
        }

        private static readonly Ring[] Rings =
        {
            new(1700f, 0.38f, 28f, 75f, 45f, 170f),
            new(2100f, 0.6f, 35f, 95f, 60f, 230f),
            new(2550f, 0.8f, 45f, 120f, 70f, 300f),
        };

        // Bearings (degrees, 0 = +X, counter-clockwise) of the far downtowns, with how much taller they grow.
        private static readonly (float bearing, float spread, float lift)[] Downtowns =
        {
            (70f, 22f, 2.4f), (160f, 16f, 1.8f), (300f, 26f, 2.1f), (215f, 12f, 1.5f),
        };

        public static GameObject Build(Transform parent, DistrictKit kit, int layer)
        {
            var rng = new System.Random(7);
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var data = new List<Vector2>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            int towers = 0;

            foreach (var ring in Rings)
            {
                float circumference = 2f * Mathf.PI * ring.Radius;
                float s = 0f;
                while (s < circumference)
                {
                    float width = Mathf.Lerp(ring.MinWidth, ring.MaxWidth, (float)rng.NextDouble());
                    float gap = (float)rng.NextDouble() < 0.3f ? Mathf.Lerp(4f, 30f, (float)rng.NextDouble()) : 0f;
                    float mid = (s + width * 0.5f) / ring.Radius * Mathf.Rad2Deg;
                    float lift = Lift(mid);
                    float height = Mathf.Lerp(ring.MinHeight, ring.MaxHeight, Mathf.Pow((float)rng.NextDouble(), 1.8f)) * lift;
                    // Alternate depths inside a ring so neighbouring towers overlap rather than form one wall.
                    float radius = ring.Radius + Mathf.Lerp(-60f, 60f, (float)rng.NextDouble());
                    float seed = (float)rng.NextDouble();
                    float a0 = s / ring.Radius, a1 = (s + width) / ring.Radius;

                    Facade(a0, a1, radius, -20f, height, seed, ring.Haze);
                    // Setback crown on taller towers, sometimes a mast.
                    if (height > 110f && rng.NextDouble() < 0.55)
                    {
                        float inset = (a1 - a0) * Mathf.Lerp(0.18f, 0.32f, (float)rng.NextDouble());
                        float crown = height * Mathf.Lerp(0.08f, 0.2f, (float)rng.NextDouble());
                        Facade(a0 + inset, a1 - inset, radius - 1f, height - 1f, height + crown, seed + 0.37f, ring.Haze);
                        height += crown;
                    }
                    if (height > 180f && rng.NextDouble() < 0.5)
                    {
                        float am = (a0 + a1) * 0.5f, half = 1.6f / radius;
                        float mast = Mathf.Lerp(18f, 55f, (float)rng.NextDouble());
                        Plain(am - half, am + half, radius - 2f, height - 1f, height + mast, ring.Haze);
                        height += mast;
                    }
                    if (height > 170f) Aviation((a0 + a1) * 0.5f, radius - 3f, height, ring.Radius / 520f, seed, ring.Haze);
                    towers++;
                    s += width + gap;
                }
            }

            var mesh = new Mesh { name = "Skyline_Backdrop", indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, data);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = DistrictKit.Renderer("SkylineBackdrop", parent, DistrictKit.SaveMesh(mesh, "Skyline_Backdrop"), kit.SkylineBackdrop, layer, shadows: false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // One huge mesh: it must never occlude or be culled by occlusion; static batching is pointless for one draw.
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            Debug.Log($"[District] skyline backdrop: {towers} towers, {triangles.Count / 3} triangles");
            return go;

            void Facade(float a0, float a1, float radius, float bottom, float top, float seed, float haze)
            {
                float length = (a1 - a0) * radius;
                Quad(a0, a1, radius, bottom, top, new Vector2(0f, bottom), new Vector2(length, top), new Vector2(seed, 0f), haze);
            }

            void Plain(float a0, float a1, float radius, float bottom, float top, float haze) =>
                // Windows need a cell at least a window wide; a mast is narrower than one, so it reads as a dark silhouette.
                Quad(a0, a1, radius, bottom, top, new Vector2(0.2f, 0f), new Vector2(0.4f, 0f), new Vector2(0.5f, 0f), haze);

            void Aviation(float a, float radius, float y, float size, float seed, float haze)
            {
                float half = size * 0.5f / radius;
                Quad(a - half, a + half, radius, y - size * 0.5f, y + size * 0.5f, Vector2.zero, Vector2.one, new Vector2(seed, 1f), haze);
            }

            void Quad(float a0, float a1, float radius, float bottom, float top, Vector2 uvMin, Vector2 uvMax, Vector2 tag, float haze)
            {
                Vector3 p0 = Point(a0, radius), p1 = Point(a1, radius);
                int i = vertices.Count;
                vertices.Add(p0 + Vector3.up * bottom);
                vertices.Add(p1 + Vector3.up * bottom);
                vertices.Add(p1 + Vector3.up * top);
                vertices.Add(p0 + Vector3.up * top);
                uv.Add(new Vector2(uvMin.x, uvMin.y));
                uv.Add(new Vector2(uvMax.x, uvMin.y));
                uv.Add(new Vector2(uvMax.x, uvMax.y));
                uv.Add(new Vector2(uvMin.x, uvMax.y));
                for (int k = 0; k < 4; k++) data.Add(tag);
                // Low haze (light pollution) is thicker: bottoms dissolve more than the tops.
                var low = new Color(0f, 0f, 0f, Mathf.Min(1f, haze + 0.14f));
                var high = new Color(0f, 0f, 0f, haze);
                colours.Add(low);
                colours.Add(low);
                colours.Add(high);
                colours.Add(high);
                triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }

        private static Vector3 Point(float angle, float radius) =>
            new(Centre.x + Mathf.Cos(angle) * radius, 0f, Centre.y + Mathf.Sin(angle) * radius);

        private static float Lift(float bearing)
        {
            float lift = 1f;
            foreach (var (b, spread, gain) in Downtowns)
            {
                float d = Mathf.DeltaAngle(bearing, b) / spread;
                lift = Mathf.Max(lift, 1f + (gain - 1f) * Mathf.Exp(-d * d));
            }
            return lift;
        }
    }
}
