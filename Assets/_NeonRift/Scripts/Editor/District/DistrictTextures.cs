using System;
using System.Collections.Generic;
using System.IO;
using NeonRift.EditorTools.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Procedural, tileable textures for the Night Run district (wet asphalt, pavement, facades, shopfronts, neon
    /// signage, glow shapes). Deterministic, generated in-house, saved as PNG so they import like any other texture
    /// and can be replaced by authored art later without touching materials.
    /// </summary>
    public static class DistrictTextures
    {
        public const string Folder = "Assets/_NeonRift/Art/District/Textures";

        /// <summary>Rows of the signage atlas. Index = row; signs pick their row by text.</summary>
        public static readonly string[] SignRows =
        {
            "DATA CORE", "SECTOR 7", "SERVICE ALLEY", "EXTRACTION", "EXPRESSWAY", "NEON RIFT", "RAMEN", "HOTEL",
            "ARCADE", "NOODLE BAR", "OPEN 24H", "CLUB VOLT", "KAIJU", "RIFT GATE", "→", "↑", "←"
        };
        public const int SignAtlasWidth = 2048;
        public const int SignAtlasHeight = 1024;
        public const int SignRowHeight = 56;
        public const int SignPixel = 7;

        public struct Set
        {
            public Texture2D AsphaltAlbedo, AsphaltMask, AsphaltNormal;
            public Texture2D PavementAlbedo, PavementNormal;
            public Texture2D[] FacadeAlbedo, FacadeEmission;
            public Texture2D ShopAlbedo, ShopEmission;
            public Texture2D Signs;
            public Texture2D GlowGradient, GlowSoft;
            public Texture2D PlazaAlbedo, PlazaEmission;
            public Texture2D[] Billboards;
        }

        public static Set Generate()
        {
            VehiclePrefabBuilder.EnsureFolder(Folder);
            var set = new Set();
            Asphalt(out set.AsphaltAlbedo, out set.AsphaltMask, out set.AsphaltNormal);
            Pavement(out set.PavementAlbedo, out set.PavementNormal);
            set.FacadeAlbedo = new Texture2D[3];
            set.FacadeEmission = new Texture2D[3];
            for (int i = 0; i < 3; i++) Facade(i, out set.FacadeAlbedo[i], out set.FacadeEmission[i]);
            Shopfronts(out set.ShopAlbedo, out set.ShopEmission);
            set.Signs = Signs();
            set.GlowGradient = GlowGradient();
            set.GlowSoft = GlowSoft();
            Plaza(out set.PlazaAlbedo, out set.PlazaEmission);
            set.Billboards = new[] { Billboard(0), Billboard(1), Billboard(2) };
            return set;
        }

        /// <summary>UV rect of a sign row in the atlas, trimmed to the text width.</summary>
        public static Rect SignRect(string text, out float aspect)
        {
            int row = Array.IndexOf(SignRows, text);
            if (row < 0) throw new ArgumentException($"'{text}' is not in the sign atlas.");
            int width = PixelFont.Measure(text) * SignPixel + 2 * SignPixel * 2;
            aspect = width / (float)SignRowHeight;
            float v0 = 1f - (row + 1) * SignRowHeight / (float)SignAtlasHeight;
            return new Rect(0f, v0, width / (float)SignAtlasWidth, SignRowHeight / (float)SignAtlasHeight);
        }

        // ---------------- Noise ----------------

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        /// <summary>Tileable value noise: lattice wraps every <paramref name="period"/> cells.</summary>
        private static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Tileable fBm over a unit square sampled at (u, v) ∈ [0,1).</summary>
        private static float Fbm(float u, float v, int baseCells, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Value(u * cells, v * cells, cells, seed + o * 31);
                norm += amp;
                amp *= 0.5f;
                cells *= 2;
            }
            return sum / norm;
        }

        // ---------------- Surfaces ----------------

        private static void Asphalt(out Texture2D albedo, out Texture2D mask, out Texture2D normal)
        {
            const int n = 1024;
            var height = new float[n * n];
            var a = new Color[n * n];
            var m = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float grain = Fbm(u, v, 96, 2, 11);
                    float stones = Hash(x, y, 5) > 0.99f ? 0.35f : 0f;
                    float patch = Fbm(u, v, 4, 4, 23);
                    // Puddles: low-frequency blobs over a threshold, soft edges.
                    float puddle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.66f, Fbm(u, v, 3, 4, 41)));
                    float tone = 0.04f + 0.025f * patch + 0.018f * grain + 0.03f * stones;
                    tone *= Mathf.Lerp(1f, 0.55f, puddle);
                    a[y * n + x] = new Color(tone * 0.95f, tone * 0.97f, tone * 1.05f, 1f);
                    // Damp asphalt is fairly rough (it must not mirror the sky at grazing angles); puddles are glassy.
                    float smooth = Mathf.Lerp(0.3f + 0.12f * patch - 0.08f * grain, 0.94f, puddle);
                    m[y * n + x] = new Color(0f, 1f, 0f, smooth);
                    height[y * n + x] = (grain * 0.6f + stones * 0.5f) * (1f - puddle);
                }
            albedo = Save("District_Asphalt_Albedo", n, n, a, srgb: true);
            mask = Save("District_Asphalt_MetalSmooth", n, n, m, srgb: false);
            normal = SaveNormal("District_Asphalt_Normal", n, height, 1.1f);
        }

        private static void Pavement(out Texture2D albedo, out Texture2D normal)
        {
            const int n = 512;            // texture = 2 m × 2 m, 0.5 m slabs
            const int slab = n / 4;
            var a = new Color[n * n];
            var height = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int sx = x / slab, sy = y / slab;
                    int lx = x % slab, ly = y % slab;
                    bool joint = lx < 3 || ly < 3;
                    float u = x / (float)n, v = y / (float)n;
                    float tone = 0.11f + 0.04f * Hash(sx, sy, 3) + 0.03f * Fbm(u, v, 16, 3, 9);
                    if (joint) tone *= 0.45f;
                    a[y * n + x] = new Color(tone, tone * 1.01f, tone * 1.06f, 1f);
                    height[y * n + x] = joint ? 0f : 0.6f + 0.2f * Fbm(u, v, 32, 2, 13);
                }
            albedo = Save("District_Pavement_Albedo", n, n, a, srgb: true);
            normal = SaveNormal("District_Pavement_Normal", n, height, 3f);
        }

        /// <summary>Window grid: 4 bays × 4 floors per tile; ~40 % of windows lit in varied colour temperatures.</summary>
        private static void Facade(int variant, out Texture2D albedo, out Texture2D emission)
        {
            const int n = 1024;
            const int cells = 4;
            int cell = n / cells;
            var a = new Color[n * n];
            var e = new Color[n * n];
            Color[] wallTones = { new(0.075f, 0.08f, 0.11f), new(0.1f, 0.085f, 0.09f), new(0.06f, 0.07f, 0.075f) };
            Color[] warm = { new(1f, 0.72f, 0.42f), new(1f, 0.85f, 0.6f), new(0.95f, 0.6f, 0.35f) };
            Color[] cool = { new(0.55f, 0.8f, 1f), new(0.75f, 0.9f, 1f) };
            Color[] neon = { new(0.2f, 0.95f, 1f), new(1f, 0.25f, 0.85f), new(0.6f, 0.35f, 1f) };
            float litChance = variant switch { 0 => 0.5f, 1 => 0.4f, _ => 0.58f };
            for (int cy = 0; cy < cells; cy++)
                for (int cx = 0; cx < cells; cx++)
                {
                    float r = Hash(cx, cy, 100 + variant);
                    bool lit = r < litChance;
                    float pick = Hash(cx, cy, 200 + variant);
                    Color light = pick < 0.55f ? warm[(int)(pick * 100) % warm.Length] : pick < 0.88f ? cool[(int)(pick * 100) % cool.Length] : neon[(int)(pick * 100) % neon.Length];
                    float brightness = 0.35f + 0.65f * Hash(cx, cy, 300 + variant);
                    float blinds = Hash(cx, cy, 400 + variant);
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            int px = cx * cell + x, py = cy * cell + y;
                            float fx = x / (float)cell, fy = y / (float)cell;
                            bool slab = fy < 0.16f;                                    // floor slab band
                            bool mullion = fx < 0.07f || fx > 0.93f || (variant == 1 && Mathf.Abs(fx - 0.5f) < 0.02f);
                            bool window = !slab && !mullion && fy < 0.92f;
                            float grime = 0.85f + 0.3f * Fbm(px / (float)n, py / (float)n, 8, 3, 50 + variant);
                            Color wall = wallTones[variant] * grime;
                            if (slab) wall *= 1.25f;
                            if (!window)
                            {
                                a[py * n + px] = wall;
                                e[py * n + px] = Color.black;
                                continue;
                            }
                            // Interior: brighter at the window centre; blinds hide the top part.
                            float glow = 0f;
                            if (lit)
                            {
                                float centre = 1f - Mathf.Abs(fx - 0.5f) * 1.2f;
                                glow = brightness * Mathf.Clamp01(centre) * (fy > 0.9f - blinds * 0.4f ? 0.25f : 1f);
                                if (Hash(px / 40, py / 40, 600 + variant) > 0.8f && fy < 0.5f) glow *= 0.55f; // furniture silhouettes
                            }
                            Color glass = new Color(0.03f, 0.045f, 0.07f) * grime;
                            a[py * n + px] = Color.Lerp(glass, light * 0.6f, glow * 0.5f);
                            e[py * n + px] = light * glow;
                        }
                }
            albedo = Save($"District_Facade{variant}_Albedo", n, n, a, srgb: true);
            emission = Save($"District_Facade{variant}_Emission", n, n, e, srgb: true);
        }

        /// <summary>
        /// Ground-floor shopfronts: 4 bays per tile (18 m), each a framed glass front with a sign band, a lit interior
        /// that is brighter towards the ceiling, shelf/counter silhouettes and a dark kick plate. One bay is shuttered.
        /// </summary>
        private static void Shopfronts(out Texture2D albedo, out Texture2D emission)
        {
            const int w = 1024, h = 256;
            int bay = w / 4;
            var a = new Color[w * h];
            var e = new Color[w * h];
            Color[] interiors = { new(1f, 0.72f, 0.45f), new(0.45f, 0.9f, 1f), new(1f, 0.45f, 0.85f), new(0.75f, 0.6f, 1f) };
            Color[] bands = { new(0.2f, 0.9f, 1f), new(1f, 0.2f, 0.7f), new(1f, 0.6f, 0.15f), new(0.55f, 0.3f, 1f) };
            for (int b = 0; b < 4; b++)
            {
                bool shutter = b == 2;
                Color interior = interiors[b];
                Color band = bands[(b + 1) % 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < bay; x++)
                    {
                        int px = b * bay + x;
                        float fx = x / (float)bay, fy = y / (float)h;
                        Color albedoColour = new(0.045f, 0.045f, 0.055f);
                        Color glow = Color.black;
                        bool pier = fx < 0.04f || fx > 0.96f;
                        if (pier) { a[y * w + px] = new Color(0.06f, 0.06f, 0.07f); e[y * w + px] = Color.black; continue; }
                        if (fy > 0.83f && fy < 0.96f)
                        {
                            // Sign band: dark fascia, a lit bottom edge and a row of light blocks.
                            albedoColour = new Color(0.03f, 0.03f, 0.04f);
                            if (fy < 0.845f) glow = band * 1.2f;
                            else if (Hash(x / 12, b, 70) > 0.5f && fy > 0.87f && fy < 0.93f && fx > 0.1f && fx < 0.9f) glow = band * 0.55f;
                        }
                        else if (fy > 0.1f && fy < 0.8f)
                        {
                            if (shutter)
                            {
                                float rib = Mathf.Repeat(fy * 46f, 1f) < 0.3f ? 0.55f : 1f;
                                albedoColour = new Color(0.14f, 0.14f, 0.15f) * rib;
                                if (fx > 0.3f && fx < 0.7f && fy > 0.55f && fy < 0.62f) glow = new Color(1f, 0.25f, 0.2f) * 0.5f;   // graffiti tag
                            }
                            else
                            {
                                bool door = fx > 0.7f && fx < 0.9f;
                                float lx = Mathf.Repeat(fx * 3f, 1f);
                                bool mullion = lx < 0.025f || lx > 0.975f || Mathf.Abs(fy - 0.62f) < 0.008f;
                                float ceiling = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(0.1f, 0.8f, fy));
                                float shelves = (Mathf.Repeat(fy * 9f, 1f) < 0.12f && fy < 0.55f && !door) ? 0.35f : 1f;
                                float silhouette = 1f;
                                float lamp = Mathf.Exp(-Mathf.Pow((Mathf.Repeat(fx * 3f, 1f) - 0.5f) * 3f, 2f)) * Mathf.InverseLerp(0.6f, 0.8f, fy);
                                albedoColour = mullion ? new Color(0.02f, 0.02f, 0.02f) : new Color(0.035f, 0.045f, 0.06f);
                                glow = mullion ? Color.black : interior * (0.55f * ceiling * shelves * silhouette + 0.6f * lamp) * (door ? 0.7f : 1f);
                            }
                        }
                        else if (fy <= 0.1f) albedoColour = new Color(0.05f, 0.05f, 0.06f);   // kick plate
                        a[y * w + px] = albedoColour;
                        e[y * w + px] = glow;
                    }
            }
            albedo = Save("District_Shopfront_Albedo", w, h, a, srgb: true);
            emission = Save("District_Shopfront_Emission", w, h, e, srgb: true);
        }

        /// <summary>White neon text rows (tube core + soft halo) for sign materials to tint.</summary>
        private static Texture2D Signs()
        {
            int w = SignAtlasWidth, h = SignAtlasHeight;
            var mask = new float[w * h];
            for (int row = 0; row < SignRows.Length; row++)
            {
                string text = SignRows[row];
                int top = row * SignRowHeight + (SignRowHeight - PixelFont.Height * SignPixel) / 2;
                int left = SignPixel * 2;
                for (int c = 0; c < text.Length; c++)
                    for (int gy = 0; gy < PixelFont.Height; gy++)
                        for (int gx = 0; gx < PixelFont.Width; gx++)
                        {
                            if (!PixelFont.Pixel(text[c], gx, gy)) continue;
                            int x0 = left + (c * (PixelFont.Width + 1) + gx) * SignPixel;
                            int y0 = top + gy * SignPixel;
                            // Rounded "tube" dot: brightest in the middle of each font pixel.
                            for (int y = 0; y < SignPixel; y++)
                                for (int x = 0; x < SignPixel; x++)
                                {
                                    float dx = (x + 0.5f) / SignPixel - 0.5f, dy = (y + 0.5f) / SignPixel - 0.5f;
                                    float v = Mathf.Clamp01(1.25f - Mathf.Sqrt(dx * dx + dy * dy) * 1.6f);
                                    int i = (h - 1 - (y0 + y)) * w + x0 + x;
                                    mask[i] = Mathf.Max(mask[i], v);
                                }
                        }
            }
            var halo = Blur(mask, w, h, 6);
            var colours = new Color[w * h];
            for (int i = 0; i < colours.Length; i++)
            {
                float v = Mathf.Clamp01(mask[i] + halo[i] * 0.7f);
                colours[i] = new Color(v, v, v, v);
            }
            return Save("District_SignAtlas", w, h, colours, srgb: true, alpha: true);
        }

        private static Texture2D GlowGradient()
        {
            const int w = 64, h = 256;
            var c = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = y / (float)(h - 1);
                    float fade = Mathf.Pow(1f - v, 1.6f) * Mathf.Clamp01(v * 20f);
                    float band = 0.85f + 0.15f * Mathf.Sin(v * 60f);
                    float a = fade * band;
                    c[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            return Save("District_GlowGradient", w, h, c, srgb: false, alpha: true, clamp: true);
        }

        private static Texture2D GlowSoft()
        {
            const int n = 128;
            var c = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
                    c[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            return Save("District_GlowSoft", n, n, c, srgb: false, alpha: true, clamp: true);
        }

        /// <summary>Compound plaza: dark tiles with a faint emissive data grid (4 m tile).</summary>
        private static void Plaza(out Texture2D albedo, out Texture2D emission)
        {
            const int n = 512;
            var a = new Color[n * n];
            var e = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x / (float)n, fy = y / (float)n;
                    bool line = Mathf.Repeat(fx * 4f, 1f) < 0.02f || Mathf.Repeat(fy * 4f, 1f) < 0.02f;
                    bool major = fx < 0.008f || fy < 0.008f;
                    float tone = 0.07f + 0.03f * Fbm(fx, fy, 8, 3, 91);
                    a[y * n + x] = line ? new Color(0.02f, 0.03f, 0.04f) : new Color(tone, tone, tone * 1.15f);
                    e[y * n + x] = major ? new Color(0.18f, 0.65f, 0.9f) : line ? new Color(0.02f, 0.09f, 0.13f) : Color.black;
                }
            albedo = Save("District_Plaza_Albedo", n, n, a, srgb: true);
            emission = Save("District_Plaza_Emission", n, n, e, srgb: true);
        }

        /// <summary>Abstract animated-looking ad panels (gradient, shapes, a text line).</summary>
        private static Texture2D Billboard(int variant)
        {
            const int w = 512, h = 256;
            var c = new Color[w * h];
            Color[] from = { new(1f, 0.15f, 0.6f), new(0.15f, 0.8f, 1f), new(0.55f, 0.2f, 1f) };
            Color[] to = { new(0.2f, 0.05f, 0.5f), new(0.05f, 0.1f, 0.4f), new(1f, 0.45f, 0.1f) };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fx = x / (float)w, fy = y / (float)h;
                    Color col = Color.Lerp(to[variant], from[variant], fy + 0.2f * Mathf.Sin(fx * 6f + variant));
                    float circle = Mathf.Abs(Vector2.Distance(new Vector2(fx * 2f, fy), new Vector2(1.45f, 0.55f)) - 0.3f) < 0.02f ? 1f : 0f;
                    float stripes = Mathf.Repeat((fx + fy) * 12f, 1f) < 0.08f && fx < 0.5f ? 0.4f : 0f;
                    float scan = Mathf.Repeat(fy * 128f, 1f) < 0.5f ? 0.85f : 1f;
                    col = (col * (0.55f + stripes) + Color.white * circle) * scan;
                    if (x < 6 || x > w - 7 || y < 6 || y > h - 7) col = Color.white * 0.9f;
                    c[y * w + x] = new Color(col.r, col.g, col.b, 1f);
                }
            return Save($"District_Billboard{variant}", w, h, c, srgb: true);
        }

        // ---------------- Helpers ----------------

        private static float[] Blur(float[] src, int w, int h, int radius)
        {
            var tmp = new float[w * h];
            var dst = new float[w * h];
            float norm = 1f / (2 * radius + 1);
            for (int y = 0; y < h; y++)
            {
                float acc = 0f;
                for (int x = -radius; x <= radius; x++) acc += src[y * w + Mathf.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[y * w + x] = acc * norm;
                    acc += src[y * w + Mathf.Min(x + radius + 1, w - 1)] - src[y * w + Mathf.Max(x - radius, 0)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float acc = 0f;
                for (int y = -radius; y <= radius; y++) acc += tmp[Mathf.Clamp(y, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = acc * norm;
                    acc += tmp[Mathf.Min(y + radius + 1, h - 1) * w + x] - tmp[Mathf.Max(y - radius, 0) * w + x];
                }
            }
            return dst;
        }

        private static Texture2D SaveNormal(string name, int n, float[] height, float strength)
        {
            var c = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float l = height[y * n + (x + n - 1) % n], r = height[y * n + (x + 1) % n];
                    float d = height[((y + n - 1) % n) * n + x], u = height[((y + 1) % n) * n + x];
                    var nrm = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    c[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
                }
            return Save(name, n, n, c, srgb: false, normalMap: true);
        }

        private static Texture2D Save(string name, int w, int h, Color[] pixels, bool srgb, bool alpha = false, bool normalMap = false, bool clamp = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
            tex.SetPixels(pixels);
            tex.Apply();
            string path = $"{Folder}/{name}.png";
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = alpha;
            importer.mipmapEnabled = true;
            importer.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.anisoLevel = normalMap || name.Contains("Asphalt") || name.Contains("Pavement") ? 8 : 2;
            importer.maxTextureSize = Mathf.Max(w, h);
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
