using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain, coastal realism: the water materials and textures (foam, spray), the seawall (recurved wave wall,
    /// lowered and stained at the overtopping sections), the road's protection and drainage (W-beam guardrails with posts,
    /// blockouts, turned-down terminals and access openings; gutters, grates, slot drains, scuppers; road studs;
    /// chevrons on the bends; storm signs with wigwag beacons; seawall floodlights) and the layered wave sections.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private const float SeaLevel = -2.2f;
        private const float WallFace = 300.6f;
        private const float SeaRailZ = CoastZ1 + 1.2f, LandRailZ = CoastZ0 - 1.2f;
        private static readonly float[] AccessGaps = { 962f, 1152f, 1342f, 1532f };

        private static Material breakerMat, overtopMat, floodSheetMat, puddleSheetMat, guardrail, guardrailStained, stainedConcrete, walkway, kelp,
                                studWhite, studAmber, chevron, grate, sprayLit, dropletMat, barrierOrange, barrierWhite, bollardYellow, ladderSteel;
        private static Texture2D foamTex, sprayTex;

        // ---------------- Textures ----------------

        /// <summary>Shader-style smoothstep (Mathf.SmoothStep interpolates between its first two arguments instead).</summary>
        private static float SStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>
        /// 512² tileable foam: R = bubble lace (edges of two Worley cell scales), G = soft dense patches (tileable value
        /// noise), B = long streaks along V (wind lines, drainage down a wave face, flow streaks).
        /// </summary>
        private static Texture2D FoamTexture()
        {
            const int N = 512;
            var rng = new Random(41);
            Vector2[,] Cells(int n)
            {
                var c = new Vector2[n, n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) c[x, y] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                return c;
            }
            float Edge(Vector2[,] c, int n, float u, float v)
            {
                float px = u * n, py = v * n;
                int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
                float f1 = 9f, f2 = 9f;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx, gy = cy + dy;
                    var o = c[((gx % n) + n) % n, ((gy % n) + n) % n];
                    float d = Vector2.Distance(new Vector2(px, py), new Vector2(gx + o.x, gy + o.y));
                    if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                }
                return f2 - f1;
            }
            float[,] lattice = new float[64, 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) lattice[x, y] = (float)rng.NextDouble();
            float Value(float u, float v, int n)
            {
                float px = u * n, py = v * n;
                int x0 = Mathf.FloorToInt(px), y0 = Mathf.FloorToInt(py);
                float fx = px - x0, fy = py - y0;
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                float L(int x, int y) => lattice[((x % n) + n) % n, ((y % n) + n) % n];
                return Mathf.Lerp(Mathf.Lerp(L(x0, y0), L(x0 + 1, y0), fx), Mathf.Lerp(L(x0, y0 + 1), L(x0 + 1, y0 + 1), fx), fy);
            }
            var a = Cells(14); var b = Cells(31);
            var pixels = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float lace = 1f - SStep(0f, 0.09f, Edge(a, 14, u, v));
                float fine = 1f - SStep(0f, 0.14f, Edge(b, 31, u, v));
                float patchNoise = Value(u, v, 8) * 0.55f + Value(u, v, 16) * 0.3f + Value(u, v, 32) * 0.15f;
                // Bubble lace only where the foam clumps (a second, coarser noise), fading out between clumps.
                float clump = SStep(0.38f, 0.72f, Value(u + 0.31f, v + 0.57f, 8) * 0.6f + Value(u + 0.13f, v + 0.71f, 16) * 0.4f);
                float r = Mathf.Clamp01((lace * 0.55f + fine * 0.6f) * clump + clump * 0.25f * patchNoise);
                float g = Mathf.Clamp01((patchNoise - 0.35f) * 2.4f);
                // Streaks: noise that varies fast across U and slowly along V.
                float streak = Value(u, v, 32) * 0.0f + Value(u * 1f, v / 16f + u * 0.02f, 64) * 0.7f + Value(u, v / 8f, 32) * 0.3f;
                float bb = Mathf.Clamp01((streak - 0.3f) * 1.8f) * (0.6f + fine * 0.4f);
                pixels[y * N + x] = new Color32((byte)(r * 255), (byte)(g * 255), (byte)(bb * 255), 255);
            }
            return SaveTexture("HR_FoamTex", N, N, pixels, false);
        }

        /// <summary>2×2 sheet of spray puffs (256²): ragged billowing cores breaking up into droplets toward the edge.</summary>
        private static Texture2D SprayTexture()
        {
            const int N = 256, T = 128;
            var rng = new Random(77);
            var pixels = new Color32[N * N];
            for (int tile = 0; tile < 4; tile++)
            {
                int ox = tile % 2 * T, oy = tile / 2 * T;
                var lobes = new Vector3[6];
                for (int i = 0; i < lobes.Length; i++)
                    lobes[i] = new Vector3((float)rng.NextDouble() * 0.28f - 0.14f, (float)rng.NextDouble() * 0.28f - 0.14f, 0.09f + (float)rng.NextDouble() * 0.09f);
                var drops = new Vector3[70];
                for (int i = 0; i < drops.Length; i++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f, rad = 0.2f + (float)rng.NextDouble() * 0.24f;
                    drops[i] = new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad, 0.005f + (float)rng.NextDouble() * 0.012f);
                }
                float seed = tile * 17.3f;
                for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    float u = x / (float)T - 0.5f, v = y / (float)T - 0.5f;
                    float noise = Mathf.PerlinNoise(x * 0.07f + seed, y * 0.07f) * 0.6f + Mathf.PerlinNoise(x * 0.21f + seed, y * 0.21f + 5f) * 0.4f;
                    float d = 0f;
                    foreach (var l in lobes) { float r = Mathf.Sqrt((u - l.x) * (u - l.x) + (v - l.y) * (v - l.y)); d = Mathf.Max(d, 1f - SStep(l.z * 0.3f, l.z * 1.6f, r)); }
                    // A round, ragged-edged puff: the radius wanders with the noise; billows inside, holes near the edge.
                    float rr = Mathf.Sqrt(u * u + v * v) + (noise - 0.5f) * 0.16f;
                    float mask = 1f - SStep(0.12f, 0.36f, rr);
                    float core = Mathf.Clamp01(d * 0.8f + (noise - 0.45f) * 0.8f) * mask;
                    core = SStep(0f, 1f, core) * (0.6f + noise * 0.4f);
                    float drop = 0f;
                    foreach (var p in drops) { float r = Mathf.Sqrt((u - p.x) * (u - p.x) + (v - p.y) * (v - p.y)); drop = Mathf.Max(drop, 1f - SStep(p.z * 0.4f, p.z, r)); }
                    float edgeFade = 1f - SStep(0.44f, 0.5f, Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)));
                    float alpha = Mathf.Clamp01(Mathf.Max(core, drop * 0.85f)) * edgeFade;
                    byte shade = (byte)(Mathf.Lerp(0.82f, 1f, noise) * 255);
                    pixels[(oy + y) * N + ox + x] = new Color32(shade, shade, shade, (byte)(alpha * 255));
                }
            }
            return SaveTexture("HR_SprayTex", N, N, pixels, true);
        }

        private static Texture2D SaveTexture(string name, int w, int h, Color32[] pixels, bool alpha)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.SetPixels32(pixels);
            t.Apply();
            string path = $"{Gen}/Materials/{name}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = alpha;
            importer.alphaIsTransparency = alpha;
            importer.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------- Materials ----------------

        private static void BuildWaterMaterials()
        {
            foamTex = FoamTexture();
            sprayTex = SprayTexture();
            ocean.SetTexture("_FoamTex", foamTex);
            EditorUtility.SetDirty(ocean);
            breakerMat = Asset("HR_BreakerWater", Shader.Find("NeonRift/HarborBreaker"));
            breakerMat.SetTexture("_FoamTex", foamTex);
            breakerMat.SetColor("_DeepColor", new Color(0.028f, 0.06f, 0.065f));
            breakerMat.SetColor("_ThinColor", new Color(0.07f, 0.19f, 0.17f));
            EditorUtility.SetDirty(breakerMat);
            Material Sheet(string name, float length, float width, Color water)
            {
                var m = Asset(name, Shader.Find("NeonRift/HarborSheet"));
                m.SetTexture("_FoamTex", foamTex);
                m.SetFloat("_Length", length);
                m.SetFloat("_Width", width);
                m.SetColor("_WaterColor", water);
                m.renderQueue = (int)RenderQueue.Transparent - 20;
                EditorUtility.SetDirty(m);
                return m;
            }
            overtopMat = Sheet("HR_OvertopWater", 18f, WaveHalf * 2f + 6f, new Color(0.08f, 0.11f, 0.11f));
            floodSheetMat = Sheet("HR_FloodSheet", 22f, 76f, new Color(0.05f, 0.07f, 0.075f));
            puddleSheetMat = Sheet("HR_PuddleSheet", 4f, 6f, new Color(0.02f, 0.025f, 0.03f));
            // Every puddle on the port's roads: opaque, mirror-like, rain-ringed.
            puddle.shader = Shader.Find("NeonRift/HarborPuddle");
            EditorUtility.SetDirty(puddle);

            // Spray is unlit with a controlled grey-white: lit particles under the seawall floodlights blew out to a white mass.
            sprayLit = Particle("HR_Spray", new Color(0.66f, 0.7f, 0.74f, 1f), sprayTex);
            dropletMat = Particle("HR_SprayDroplet", new Color(0.75f, 0.8f, 0.86f, 0.55f), AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_NeonRift/Art/District/Textures/District_GlowSoft.png"));

            guardrail = Lit("HR_Guardrail", new Color(0.56f, 0.58f, 0.6f), 0.62f, 0.75f, null);
            guardrailStained = Lit("HR_GuardrailStained", new Color(0.4f, 0.37f, 0.33f), 0.45f, 0.6f, null);
            stainedConcrete = Lit("HR_StainedConcrete", new Color(0.15f, 0.16f, 0.15f), 0.7f, 0f, null);
            walkway = Lit("HR_Walkway", new Color(0.26f, 0.26f, 0.25f), 0.5f, 0f, null);
            kelp = Lit("HR_Kelp", new Color(0.07f, 0.08f, 0.04f), 0.75f, 0f, null);
            studWhite = Lit("HR_StudWhite", new Color(0.6f, 0.6f, 0.6f), 0.8f, 0f, new Color(0.9f, 0.9f, 0.85f));
            studAmber = Lit("HR_StudAmber", new Color(0.6f, 0.4f, 0.1f), 0.8f, 0f, new Color(1.4f, 0.7f, 0.08f));
            barrierOrange = Lit("HR_WaterBarrierOrange", new Color(0.75f, 0.22f, 0.04f), 0.45f, 0f, null);
            barrierWhite = Lit("HR_WaterBarrierWhite", new Color(0.78f, 0.78f, 0.76f), 0.45f, 0f, null);
            bollardYellow = Lit("HR_Bollard", new Color(0.75f, 0.58f, 0.06f), 0.5f, 0.2f, null);
            ladderSteel = Lit("HR_LadderSteel", new Color(0.35f, 0.36f, 0.37f), 0.5f, 0.8f, null);
            chevron = Lit("HR_Chevron", Color.white, 0.35f, 0f, new Color(0.55f, 0.5f, 0.3f));
            var chev = Texture("HR_ChevronTex", 64, 32, (x, y) => ((x + Mathf.Abs(y - 16)) / 8) % 2 == 0 ? new Color(0.95f, 0.75f, 0.05f) : new Color(0.03f, 0.03f, 0.03f), false);
            chevron.SetTexture("_BaseMap", chev);
            chevron.SetTexture("_EmissionMap", chev);
            grate = Lit("HR_Grate", Color.white, 0.55f, 0.85f, null);
            grate.SetTexture("_BaseMap", Texture("HR_GrateTex", 32, 32, (x, y) => x % 4 < 2 || y < 2 || y > 29 ? new Color(0.16f, 0.16f, 0.17f) : new Color(0.004f, 0.004f, 0.005f), false));
        }

        // ---------------- Seawall and road furniture ----------------

        /// <summary>A recurved wave wall (land face, flat top, seaward lip turning the water back) between x0 and x1.</summary>
        private static void WaveWall(Material m, float x0, float x1, float h)
        {
            var prof = new[]
            {
                new Vector2(291.0f, 0f), new Vector2(291.0f, h), new Vector2(292.0f, h), new Vector2(292.55f, h - 0.06f),
                new Vector2(292.78f, h * 0.84f), new Vector2(292.5f, h * 0.62f), new Vector2(292.42f, 0f)
            };
            for (int j = 1; j < prof.Length; j++)
            {
                Vector2 a = prof[j - 1], b = prof[j];
                float v0 = j - 1, v1 = j;
                B(m).Quad(new Vector3(x0, a.y, a.x), new Vector3(x0, b.y, b.x), new Vector3(x1, b.y, b.x), new Vector3(x1, a.y, a.x),
                          new Vector2(x0 / 2f, v0 * 0.3f), new Vector2(x0 / 2f, v1 * 0.3f), new Vector2(x1 / 2f, v1 * 0.3f), new Vector2(x1 / 2f, v0 * 0.3f));
            }
            var centre = new Vector2(291.7f, h * 0.5f);
            for (int j = 1; j < prof.Length; j++)
            {
                Vector2 a = prof[j - 1], b = prof[j];
                B(m).Tri(new Vector3(x0, centre.y, centre.x), new Vector3(x0, a.y, a.x), new Vector3(x0, b.y, b.x), Vector2.zero, Vector2.up, Vector2.one);
                B(m).Tri(new Vector3(x1, centre.y, centre.x), new Vector3(x1, b.y, b.x), new Vector3(x1, a.y, a.x), Vector2.zero, Vector2.up, Vector2.one);
            }
        }

        // W-beam cross-section: (offset toward the road, height).
        private static readonly Vector2[] WProfile =
        {
            new(0f, 0.40f), new(0.055f, 0.43f), new(0.083f, 0.475f), new(0.035f, 0.555f), new(0.083f, 0.635f), new(0.055f, 0.68f), new(0f, 0.71f)
        };

        /// <summary>One rail panel between xa and xb, both faces. <paramref name="dent"/> pushes the middle back (impact
        /// damage); <paramref name="flare"/> turns the end down and away from the road over the span (a terminal).</summary>
        private static void RailPanel(Material m, float xa, float xb, float z, float side, float dent = 0f, int flare = 0)
        {
            int segs = dent > 0f || flare != 0 ? 6 : 1;
            Vector3 P(float x, int j)
            {
                float u = Mathf.InverseLerp(xa, xb, x);
                var p = WProfile[j];
                float back = dent * Mathf.Sin(u * Mathf.PI) * (j >= 2 && j <= 4 ? 1f : 0.6f);
                float drop = 0f, away = 0f;
                if (flare != 0)
                {
                    float f = flare > 0 ? u : 1f - u;
                    drop = f * f;
                    away = f * f * 0.9f;
                }
                return new Vector3(x, p.y * (1f - drop * 0.85f) - dent * 0.25f * Mathf.Sin(u * Mathf.PI), z + side * (p.x - back - away));
            }
            for (int k = 0; k < segs; k++)
            {
                float x0 = Mathf.Lerp(xa, xb, k / (float)segs), x1 = Mathf.Lerp(xa, xb, (k + 1) / (float)segs);
                for (int j = 1; j < WProfile.Length; j++)
                {
                    Vector3 a = P(x0, j - 1), b = P(x1, j - 1), c = P(x1, j), d = P(x0, j);
                    Vector2 ua = new(x0 * 0.25f, (j - 1) * 0.15f), ub = new(x1 * 0.25f, (j - 1) * 0.15f), uc = new(x1 * 0.25f, j * 0.15f), ud = new(x0 * 0.25f, j * 0.15f);
                    B(m).Quad(a, b, c, d, ua, ub, uc, ud);
                    B(m).Quad(a, d, c, b, ua, ud, uc, ub);
                }
            }
        }

        /// <summary>
        /// A W-beam guardrail run on the road verge at <paramref name="z"/> (road on the <paramref name="side"/> side:
        /// −1 = road toward −z): rail panels, a post every 2 m with a blockout, a reflector every third post and turned-down
        /// terminals with anchor blocks at both ends. Rail panels inside a wave section use the stained material.
        /// </summary>
        private static void Guardrail(Transform root, string name, float x0, float x1, float z, float side, Material reflector, float dentAt = -1f)
        {
            for (float xa = x0; xa < x1 - 0.01f; xa += 4f)
            {
                float xb = Mathf.Min(x1, xa + 4f);
                bool stained = WaveCentres.Any(c => Mathf.Abs((xa + xb) * 0.5f - c) < WaveHalf + 6f);
                bool dented = dentAt >= xa && dentAt < xb;
                RailPanel(stained ? guardrailStained : guardrail, xa, xb, z, side, dented ? 0.28f : 0f);
            }
            RailPanel(guardrail, x0 - 4f, x0, z, side, 0f, -1);
            RailPanel(guardrail, x1, x1 + 4f, z, side, 0f, 1);
            foreach (float e in new[] { x0 - 4f, x1 + 4f })
                B(concreteDark).Cuboid(new Vector3(e - 0.4f, 0f, z - side * 0.9f - 0.3f), new Vector3(e + 0.4f, 0.25f, z - side * 0.9f + 0.3f), 1f);
            int n = 0;
            for (float px = x0 + 1f; px < x1; px += 2f, n++)
            {
                float zp = z - side * 0.24f;
                B(metal).Cuboid(new Vector3(px - 0.05f, 0f, zp - 0.075f), new Vector3(px + 0.05f, 0.78f, zp + 0.075f), 1f);
                float zb0 = z - side * 0.02f, zb1 = z - side * 0.17f;
                B(metal).Cuboid(new Vector3(px - 0.05f, 0.46f, Mathf.Min(zb0, zb1)), new Vector3(px + 0.05f, 0.66f, Mathf.Max(zb0, zb1)), 1f);
                if (n % 3 == 0)
                {
                    var rz = z + side * 0.09f;
                    B(reflector).Cuboid(new Vector3(px - 0.04f, 0.53f, rz - 0.01f), new Vector3(px + 0.04f, 0.59f, rz + 0.01f), 1f);
                }
            }
            Collider(root, name, new Vector3((x0 + x1) * 0.5f, 0.45f, z - side * 0.08f), new Vector3(x1 - x0 + 2f, 0.9f, 0.36f), environment);
        }

        private sealed class CoastFurniture
        {
            public readonly List<Renderer>[] Wigwags = { new(), new(), new() };
            public readonly List<Light>[] Floodlights = { new(), new(), new() };
        }

        /// <summary>The seawall, guardrails, drainage, studs, chevrons, access points, stains and debris of the coast road.</summary>
        private static CoastFurniture BuildCoastFurniture(Transform root, Random rng)
        {
            var furniture = new CoastFurniture();
            float R() => (float)rng.NextDouble();

            // Seawall: recurved wave wall (1.15 m) on the seaward edge of the walkway, lowered to 0.6 m and water-stained at
            // the three designed overtopping sections; apron and capping beam; the wall face down into the sea; armour rock.
            Begin();
            float x = 880f;
            while (x < HeadWallW)
            {
                float next = Mathf.Min(HeadWallW, x + 12f);
                bool exposed = WaveCentres.Any(c => Mathf.Abs((x + next) * 0.5f - c) < WaveHalf);
                WaveWall(exposed ? stainedConcrete : concreteDark, x, next - 0.25f, exposed ? 0.6f : 1.15f);
                if (!exposed) B(hazardStripe).Cuboid(new Vector3(x, 1.15f, 291.15f), new Vector3(next - 0.25f, 1.19f, 291.95f), 1f);
                x = next;
            }
            B(concreteDark).Cuboid(new Vector3(880f, -6f, 292.4f), new Vector3(HeadWallW - 1.8f, 0f, WallFace), 4f);
            B(walkway).Cuboid(new Vector3(880f, -0.05f, WallFace - 0.7f), new Vector3(HeadWallW - 1.8f, 0.14f, WallFace + 0.25f), 3f);   // capping beam
            for (int i = 0; i < 360; i++)
            {
                var p = new Vector3(884f + R() * (HeadWallW - 890f), SeaLevel - 1.1f + R() * 1.6f, WallFace + 1.2f + R() * 9f);
                B(rock).OrientedBox(p, Vector3.one * (1.6f + R() * 1.5f), Quaternion.Euler(rng.Next(360), rng.Next(360), rng.Next(360)), 1f);
            }
            // Walkway (non-slip concrete) between the kerb and the wave wall; stained where the sea comes over.
            B(walkway).Ground(new Vector3(904f, 0.02f, 286.1f), new Vector3(HeadWallW, 0.02f, 291f), 2f);
            foreach (float c in WaveCentres) B(stainedConcrete).Ground(new Vector3(c - WaveHalf - 2f, 0.025f, 286.1f), new Vector3(c + WaveHalf + 2f, 0.025f, 291f), 3f);
            B(concreteDark).Cuboid(new Vector3(904f, 0f, 285.7f), new Vector3(BendA.x, 0.15f, 286.1f), 2f);   // kerb
            Flush("Seawall", root, environment, false);
            Collider(root, "SeawallCollider", new Vector3((880f + HeadWallW) * 0.5f, 0.6f, 291.7f), new Vector3(HeadWallW - 880f, 1.2f, 1.6f), environment);

            // Guardrails: the sea side with access openings (removable bollards across each), the land side, the service road.
            Begin();
            float start = 904f;
            for (int g = 0; g <= AccessGaps.Length; g++)
            {
                float end = g < AccessGaps.Length ? AccessGaps[g] - 6f : BendA.x;
                Guardrail(root, "SeaRail", start, end, SeaRailZ, -1f, studAmber, g == 2 ? 1232f : -1f);
                if (g < AccessGaps.Length) start = AccessGaps[g] + 6f;
            }
            Guardrail(root, "LandRail", 904f, 1580f, LandRailZ, 1f, studWhite);
            Guardrail(root, "ServiceRailN", 904f, 1580f, 163.4f, -1f, studWhite);
            Guardrail(root, "ServiceRailS", 904f, 1580f, 148.6f, 1f, studWhite);
            Flush("Guardrails", root, environment, true);
            Begin();
            foreach (float gx in AccessGaps)
            {
                for (int i = -1; i <= 1; i++)
                {
                    var p = new Vector3(gx + i * 1.3f, 0f, SeaRailZ - 0.1f);
                    B(bollardYellow).Cylinder(p, 0.12f, 0.95f, 10, true);
                    B(studWhite).Cylinder(p + Vector3.up * 0.72f, 0.125f, 0.08f, 10, false);
                    Collider(root, "AccessBollard", p + Vector3.up * 0.5f, new Vector3(0.3f, 1f, 0.3f), environment);
                }
                // Steel ladder down the wall face at each access point.
                for (float y = SeaLevel - 1f; y < 0f; y += 0.3f) B(ladderSteel).Cuboid(new Vector3(gx - 0.25f, y, WallFace + 0.12f), new Vector3(gx + 0.25f, y + 0.04f, WallFace + 0.16f), 1f);
                foreach (float s in new[] { -0.27f, 0.27f })
                {
                    B(ladderSteel).Cuboid(new Vector3(gx + s - 0.025f, SeaLevel - 1f, WallFace + 0.1f), new Vector3(gx + s + 0.025f, 1.1f, WallFace + 0.18f), 1f);
                    B(ladderSteel).Cuboid(new Vector3(gx + s - 0.025f, 0.14f, WallFace - 0.6f), new Vector3(gx + s + 0.025f, 1.1f, WallFace - 0.55f), 1f);
                }
                // A gap in the wave wall's hazard stripe would be wrong; instead a small hatch plate on the walkway.
                B(drain).Cuboid(new Vector3(gx - 0.6f, 0.02f, 289.6f), new Vector3(gx + 0.6f, 0.035f, 290.6f), 1f);
            }
            // Temporary repair at the damaged rail panel in the middle section: water-filled barriers on the verge.
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(1226.5f + i * 2.05f, 0f, SeaRailZ - 0.75f);
                B(i % 2 == 0 ? barrierOrange : barrierWhite).Cuboid(p - new Vector3(0.98f, 0f, 0.28f), p + new Vector3(0.98f, 0.95f, 0.28f), 1f);
                B(i % 2 == 0 ? barrierOrange : barrierWhite).Cuboid(p - new Vector3(0.9f, -0.95f, 0.2f), p + new Vector3(0.9f, 1.05f, 0.2f), 1f);
            }
            Collider(root, "RepairBarriers", new Vector3(1229.6f, 0.5f, SeaRailZ - 0.75f), new Vector3(8.2f, 1f, 0.56f), environment);
            Flush("AccessPoints", root, detail, false);

            // Drainage: land-side gutter with grates, a slot drain along the sea-side verge, runoff channels across the
            // walkway to scuppers in the lowered wave wall. Road studs on both edge lines and the centre line.
            Begin();
            B(drain).Ground(new Vector3(904f, 0.012f, 267.2f), new Vector3(1580f, 0.012f, 267.8f), 2f);
            for (float gx = 910f; gx < 1578f; gx += 10f) B(grate).Cuboid(new Vector3(gx - 0.45f, 0.012f, 267.15f), new Vector3(gx + 0.45f, 0.028f, 267.85f), 0.9f);
            B(drain).Ground(new Vector3(904f, 0.046f, 284.35f), new Vector3(BendA.x, 0.046f, 284.47f), 2f);
            foreach (float c in WaveCentres)
                for (float d = -WaveHalf + 3f; d < WaveHalf; d += 6f)
                {
                    B(drain).Ground(new Vector3(c + d - 0.15f, 0.03f, 286.1f), new Vector3(c + d + 0.15f, 0.03f, 291f), 1f);
                    B(grate).Cuboid(new Vector3(c + d - 0.3f, 0f, 290.97f), new Vector3(c + d + 0.3f, 0.22f, 291.0f), 0.6f);   // land face
                    B(drain).Cuboid(new Vector3(c + d - 0.3f, 0f, 292.42f), new Vector3(c + d + 0.3f, 0.22f, 292.45f), 1f);    // sea face
                }
            for (float sx = 906f; sx < BendA.x - 2f; sx += 8f)
            {
                if (sx > 1578f && sx < 1602f) continue;
                foreach (float sz in new[] { CoastZ0 + 0.6f, CoastZ1 - 0.6f })
                    B(studAmber).Cuboid(new Vector3(sx - 0.07f, 0.04f, sz - 0.05f), new Vector3(sx + 0.07f, 0.065f, sz + 0.05f), 1f);
                if (((int)sx / 8) % 3 == 0)
                    B(studWhite).Cuboid(new Vector3(sx + 1.9f, 0.04f, (CoastZ0 + CoastZ1) * 0.5f - 0.05f), new Vector3(sx + 2.04f, 0.065f, (CoastZ0 + CoastZ1) * 0.5f + 0.05f), 1f);
            }
            // Storm debris on the walkway and verge at the overtopping sections: kelp and loose stones.
            foreach (float c in WaveCentres)
                for (int i = 0; i < 26; i++)
                {
                    var p = new Vector3(c + (R() * 2f - 1f) * (WaveHalf + 6f), 0.04f, R() < 0.6f ? 286.4f + R() * 4.3f : 284.6f + R() * 0.8f);
                    if (R() < 0.75f) B(kelp).OrientedBox(p, new Vector3(0.3f + R() * 0.9f, 0.04f, 0.15f + R() * 0.3f), Quaternion.Euler(0f, rng.Next(360), 0f), 1f);
                    else B(rock).OrientedBox(p + Vector3.up * 0.05f, Vector3.one * (0.12f + R() * 0.2f), Quaternion.Euler(rng.Next(360), rng.Next(360), 0f), 1f);
                }
            Flush("Drainage", root, detail, false);

            // Chevrons on the outside of the bends: the coast junction (northbound turning east) and the road's east end.
            Begin();
            void Board(Vector3 c, float yaw)
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                B(chevron).Panel(c + rot * new Vector3(0f, 0f, 0.03f), rot, 1.2f, 0.6f, new Rect(0f, 0f, 1f, 1f));
                B(metal).Panel(c - rot * new Vector3(0f, 0f, 0.03f), rot * Quaternion.Euler(0f, 180f, 0f), 1.2f, 0.6f, new Rect(0f, 0f, 1f, 1f));
                B(metal).Cuboid(new Vector3(c.x - 0.05f, 0f, c.z - 0.05f), new Vector3(c.x + 0.05f, c.y - 0.3f, c.z + 0.05f), 1f);
            }
            foreach (float bx in new[] { 886f, 890f, 894f, 898f }) Board(new Vector3(bx, 1.5f, 286.6f), 180f);
            foreach (float bz in new[] { 271f, 275f, 279f }) Board(new Vector3(881.8f, 1.5f, bz), 90f);
            Flush("Chevrons", root, detail, false);

            // Floodlights on the seawall at each section, aimed out to sea: they light the incoming face.
            for (int k = 0; k < WaveCentres.Length; k++)
            {
                Begin();
                foreach (float dx in new[] { -24f, 24f })
                {
                    var b = new Vector3(WaveCentres[k] + dx, 0f, 290.3f);
                    B(metal).Cylinder(b, 0.16f, 11f, 10, true);
                    B(metal).Cuboid(b + new Vector3(-0.9f, 10.6f, -0.1f), b + new Vector3(0.9f, 11.3f, 0.5f), 1f);
                    B(workLight).Cuboid(b + new Vector3(-0.8f, 10.65f, 0.5f), b + new Vector3(0.8f, 11.25f, 0.52f), 1f);
                    var go = new GameObject("SeawallFloodlight") { layer = detail };
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(b + new Vector3(0f, 10.9f, 0.6f), Quaternion.Euler(15f, 0f, 0f));
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.color = new Color(0.86f, 0.92f, 1f);
                    l.intensity = 9000f;
                    l.range = 190f;
                    l.spotAngle = 78f;
                    l.innerSpotAngle = 40f;
                    l.shadows = LightShadows.None;
                    furniture.Floodlights[k].Add(l);
                }
                Flush("SeawallFloodlights_" + k, root, detail, false);
            }
            report.AppendLine($"coast furniture: recurved wave wall (3 lowered overtopping sections), W-beam guardrails ×4 runs with {AccessGaps.Length} access openings, gutters/grates/slot drain/scuppers, road studs, chevrons, 6 seawall floodlights");
            return furniture;
        }

        // ---------------- Wave sections ----------------

        private static ParticleSystem SprayParticles(string name, Transform parent, int max, float gravity, Vector2 sizeGrow, float alpha, float stretch = 0f)
        {
            var ps = Particles(name, parent, sprayLit, max, p =>
            {
                var main = p.main;
                main.loop = true;
                main.playOnAwake = true;
                main.startSpeed = 0f;
                main.gravityModifier = gravity;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.88f, 0.9f, alpha), new Color(1f, 1f, 1f, alpha * 0.8f));
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var e = p.emission; e.rateOverTime = 0f;
                var shape = p.shape; shape.enabled = false;
                var col = p.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.88f, 0.9f), 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.55f, 0.55f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var size = p.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, sizeGrow.x, 1f, sizeGrow.y));
                // Plain air drag. (Unity's defaults multiply drag by particle size and speed: with 2–7 m spray sprites at
                // 20 m/s that cancelled the launch, so the spray never rose above the wall.)
                var lv = p.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 60f; lv.drag = 0.4f;
                lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
                var rot = p.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
                var tsa = p.textureSheetAnimation;
                tsa.enabled = true;
                tsa.numTilesX = 2; tsa.numTilesY = 2;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
                var r = p.GetComponent<ParticleSystemRenderer>();
                r.sortMode = ParticleSystemSortMode.Distance;
                r.maxParticleSize = 0.4f;   // caps overdraw when spray passes close to the camera
                if (stretch > 0f)
                {
                    // Spray thrown up a wall reads as streaming curtains, not puffs: stretch each sprite along its velocity.
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = stretch;
                    r.lengthScale = 1.6f;
                    var rot2 = p.rotationOverLifetime; rot2.enabled = false;
                }
            });
            return ps;
        }

        /// <summary>Ribbon along the overtopping path (wall face → apron → over the wave wall → onto the road edge).</summary>
        private static Mesh OvertopMesh(float c, int k)
        {
            var path = new[]
            {
                new Vector2(301.2f, -1.6f), new Vector2(300.75f, -0.2f), new Vector2(300.55f, 0.45f), new Vector2(299.0f, 0.42f), new Vector2(296f, 0.34f),
                new Vector2(293.7f, 0.42f), new Vector2(292.95f, 0.95f), new Vector2(292.0f, 1.3f), new Vector2(290.6f, 1.38f), new Vector2(289.3f, 1.05f),
                new Vector2(288.2f, 0.55f), new Vector2(287.2f, 0.14f), new Vector2(286.0f, 0.07f), new Vector2(285.0f, 0.06f)
            };
            var len = new float[path.Length];
            for (int i = 1; i < path.Length; i++) len[i] = len[i - 1] + Vector2.Distance(path[i], path[i - 1]);
            const int rows = 34, cols = 50;
            float x0 = c - WaveHalf - 3f, x1 = c + WaveHalf + 3f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int j = 0; j <= rows; j++)
            {
                float d = j / (float)rows * len[^1];
                int i = 1;
                while (i < path.Length - 1 && len[i] < d) i++;
                float u = Mathf.InverseLerp(len[i - 1], len[i], d);
                var p = Vector2.Lerp(path[i - 1], path[i], u);
                var tan = (path[i] - path[i - 1]).normalized;
                var n = new Vector3(0f, -tan.x, tan.y);   // (z, y) tangent → normal (y = −tz, z = ty): up / away from the wall
                n = new Vector3(0f, -tan.x, tan.y).normalized;
                for (int q = 0; q <= cols; q++)
                {
                    float x = Mathf.Lerp(x0, x1, q / (float)cols);
                    verts.Add(new Vector3(x, p.y, p.x));
                    norms.Add(n);
                    uvs.Add(new Vector2(j / (float)rows, q / (float)cols));
                }
            }
            for (int j = 0; j < rows; j++)
            for (int q = 0; q < cols; q++)
            {
                int a = j * (cols + 1) + q;
                tris.Add(a); tris.Add(a + cols + 1); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(a + cols + 1); tris.Add(a + cols + 2);
            }
            var mesh = new Mesh { name = $"HR_Overtop_{k}" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return SaveMesh(mesh, mesh.name);
        }

        /// <summary>Grid over the road for the flood sheet: uv.x runs inland from the seaward edge (z 287) 22 m.</summary>
        private static Mesh FloodMesh(float c, int k)
        {
            const int cols = 52, rows = 22;
            float x0 = c - 38f, x1 = c + 38f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int j = 0; j <= rows; j++)
            for (int q = 0; q <= cols; q++)
            {
                float u = j / (float)rows;
                verts.Add(new Vector3(Mathf.Lerp(x0, x1, q / (float)cols), 0.05f, 287f - u * 22f));
                norms.Add(Vector3.up);
                uvs.Add(new Vector2(u, q / (float)cols));
            }
            for (int j = 0; j < rows; j++)
            for (int q = 0; q < cols; q++)
            {
                int a = j * (cols + 1) + q;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + cols + 1);
                tris.Add(a + 1); tris.Add(a + cols + 2); tris.Add(a + cols + 1);
            }
            var mesh = new Mesh { name = $"HR_FloodSheet_{k}" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return SaveMesh(mesh, mesh.name);
        }

        private static Renderer MeshObject(string name, Transform parent, Mesh mesh, Material m)
        {
            var go = new GameObject(name) { layer = LayerMask.NameToLayer("Water") };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return r;
        }
    }
}
