using System.Collections.Generic;
using System.IO;
using NeonRift.EditorTools.District;
using UnityEditor;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain 2.2 container trailer: a 40 ft skeletal (gooseneck) chassis built to real dimensions — I-beam main rails
    /// with a raised gooseneck and coupler plate, kingpin, tapered outriggers carrying 40 ft and 2 × 20 ft twistlocks,
    /// cross-members, landing legs with gearbox, crank and bracing, a tri-axle air-suspension bogie (hangers, trailing arms,
    /// air bags, dampers, brake chambers) on super-single wheels, mudguards and flaps, side under-run guards with
    /// conspicuity tape and amber marker lamps, rear under-run bar, lamp clusters (indicator / tail-brake / reverse / fog),
    /// triangle reflectors, plate, air tank, toolbox and EBS unit — plus a detailed ISO container (corner posts and castings,
    /// rails, door leaves, locking bars, cam keepers, handles, hinges) for the boxes trucks carry. Materials are weathered
    /// by generated grime / rust maps with wet smoothness.
    /// Frame: +z forward, ground y 0, container centred at z 0 with its underside on <see cref="DeckY"/>.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        public const float DeckY = 1.45f;
        private const float KingpinZ = 5.65f, TrailerFront = 6.25f, TrailerRear = -6.25f;
        private const float TrailerWheelR = 0.535f, TrailerWheelX = 1.08f;
        private static readonly float[] TrailerAxles = { -2.95f, -4.26f, -5.57f };
        public static readonly Vector3 ContainerSize = new(2.438f, 2.591f, 12.192f);

        private static Material trSteel, trGalv, trPlastic, trTyre, trRim, trHub, trLensAmber, trLensWhite, trReflRed, trMarker, trFrontMarker,
                                trTapeWhite, trTapeRed, trTapeYellow, trHoseRed, trHoseYellow, trPlate, trCorner;

        private static string TruckDir => $"{Gen}/Truck";

        private static void EnsureTruckDir()
        {
            if (!AssetDatabase.IsValidFolder(TruckDir)) AssetDatabase.CreateFolder(Gen, "Truck");
        }

        // ---------------- Materials ----------------

        /// <summary>Base map (colour × grime / rust) and a linear metallic-smoothness map (wet steel, duller where dirty).</summary>
        private static (Texture2D albedo, Texture2D mask) GrimeMaps(string name, Color baseColour, float rust, float metallic, float wetSmooth, int seed)
        {
            const int S = 256;
            var albedo = new Texture2D(S, S, TextureFormat.RGBA32, true);
            var mask = new Texture2D(S, S, TextureFormat.RGBA32, true, true);
            var rng = new System.Random(seed);
            float ox = rng.Next(1000), oy = rng.Next(1000);
            var rustTint = new Color(0.32f, 0.13f, 0.05f);
            var dirtTint = new Color(0.12f, 0.1f, 0.08f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    // Tileable fBm: sample a torus-wrapped domain via periodic coordinates.
                    float Noise(float f) => (Mathf.PerlinNoise(ox + Mathf.Cos(u * 6.2832f) * f, oy + Mathf.Sin(u * 6.2832f) * f + Mathf.Cos(v * 6.2832f) * f * 0.7f)
                                           + Mathf.PerlinNoise(ox + 50f + Mathf.Sin(v * 6.2832f) * f, oy + Mathf.Cos(v * 6.2832f) * f)) * 0.5f;
                    float dirt = Mathf.Clamp01((Noise(1.2f) * 0.6f + Noise(3.5f) * 0.3f + Noise(9f) * 0.1f - 0.42f) * 2.2f);
                    // Run-off streaks: thin vertical bands, stronger toward the bottom of each tile.
                    float streak = Mathf.Pow(Mathf.PerlinNoise(ox + Mathf.Cos(u * 6.2832f) * 9f, oy + Mathf.Sin(u * 6.2832f) * 9f), 3f) * (1f - v) * 1.6f;
                    float spots = Mathf.Clamp01((Noise(6f) - 0.6f) * 5f) * rust;
                    var c = Color.Lerp(baseColour, dirtTint, Mathf.Clamp01(dirt * 0.45f + streak * 0.35f));
                    c = Color.Lerp(c, rustTint, Mathf.Clamp01(spots + streak * rust * 0.6f));
                    c *= 0.94f + Mathf.PerlinNoise(x * 0.35f, y * 0.35f) * 0.12f;
                    c.a = 1f;
                    albedo.SetPixel(x, y, c);
                    float smooth = Mathf.Clamp01(wetSmooth - dirt * 0.22f - spots * 0.35f + streak * 0.05f);
                    float metal = Mathf.Clamp01(metallic * (1f - spots));
                    mask.SetPixel(x, y, new Color(metal, 0f, 0f, smooth));
                }
            albedo.Apply();
            mask.Apply();
            Texture2D Save(Texture2D t, string file, bool linear)
            {
                string path = $"{TruckDir}/{file}.png";
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.sRGBTexture = !linear;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.maxTextureSize = 256;
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return (Save(albedo, name + "_Albedo", false), Save(mask, name + "_MetalSmooth", true));
        }

        private static Material Weathered(string name, Color colour, float rust, float metallic, float wetSmooth, int seed)
        {
            var (albedo, mask) = GrimeMaps(name, Color.white, rust, metallic, wetSmooth, seed);
            var m = Lit(name, colour, wetSmooth, metallic, null);
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MetallicGlossMap", mask);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void TrailerMaterials()
        {
            EnsureTruckDir();
            VehicleMaterials();
            trSteel = Weathered("HR_TrlSteel", new Color(0.13f, 0.135f, 0.14f), 0.55f, 0.35f, 0.62f, 11);
            trGalv = Weathered("HR_TrlGalv", new Color(0.46f, 0.47f, 0.47f), 0.2f, 0.85f, 0.55f, 12);
            trPlastic = Weathered("HR_TrlPlastic", new Color(0.035f, 0.035f, 0.038f), 0f, 0f, 0.58f, 13);
            trRim = Weathered("HR_TrlRim", new Color(0.58f, 0.59f, 0.6f), 0.25f, 0.7f, 0.6f, 14);
            trCorner = Weathered("HR_ContainerCastings", new Color(0.1f, 0.1f, 0.1f), 0.8f, 0.5f, 0.55f, 15);
            trHub = Lit("HR_TrlHub", new Color(0.06f, 0.06f, 0.065f), 0.4f, 0.6f, null);
            // Tyre: a dark, slightly rough rubber with a lateral-sipe tread texture (u runs round the tyre).
            trTyre = Lit("HR_TrlTyre", new Color(0.05f, 0.05f, 0.052f), 0.42f, 0f, null);
            var tread = Texture("HR_TrlTread", 8, 64, (x, y) => (y % 8) < 2 ? new Color(0.55f, 0.55f, 0.55f) : Color.white, false);
            trTyre.SetTexture("_BaseMap", tread);
            trTyre.SetTextureScale("_BaseMap", new Vector2(6f, 1f));
            trLensAmber = Lit("HR_TrlLensAmber", new Color(0.35f, 0.15f, 0.01f), 0.92f, 0f, new Color(0.08f, 0.03f, 0f));
            trLensWhite = Lit("HR_TrlLensWhite", new Color(0.55f, 0.56f, 0.58f), 0.95f, 0f, null);
            trReflRed = Lit("HR_TrlReflector", new Color(0.4f, 0.02f, 0.02f), 0.9f, 0f, new Color(0.35f, 0.01f, 0.01f));
            trMarker = Lit("HR_TrlMarker", new Color(0.4f, 0.18f, 0.01f), 0.9f, 0f, new Color(1.1f, 0.42f, 0.02f));
            trFrontMarker = Lit("HR_TrlFrontMarker", new Color(0.6f, 0.6f, 0.6f), 0.9f, 0f, new Color(0.9f, 0.88f, 0.8f));
            trTapeWhite = Lit("HR_TrlTapeWhite", new Color(0.8f, 0.8f, 0.78f), 0.8f, 0f, new Color(0.05f, 0.05f, 0.05f));
            trTapeRed = Lit("HR_TrlTapeRed", new Color(0.5f, 0.03f, 0.03f), 0.8f, 0f, new Color(0.04f, 0f, 0f));
            trTapeYellow = Lit("HR_TrlTapeYellow", new Color(0.75f, 0.55f, 0.04f), 0.8f, 0f, new Color(0.04f, 0.03f, 0f));
            trHoseRed = Lit("HR_TrlHoseRed", new Color(0.45f, 0.04f, 0.03f), 0.55f, 0f, null);
            trHoseYellow = Lit("HR_TrlHoseYellow", new Color(0.62f, 0.48f, 0.04f), 0.55f, 0f, null);
            trPlate = Lit("HR_TrlPlate", new Color(0.72f, 0.62f, 0.12f), 0.6f, 0f, new Color(0.18f, 0.15f, 0.03f));
        }

        // ---------------- Trailer chassis ----------------

        /// <summary>The trailer chassis (everything that does not turn) into <paramref name="w"/>; red tail / brake lenses into <paramref name="brake"/>.</summary>
        private static void TrailerGeo(HarborTruckMesh w, HarborTruckMesh brake)
        {
            var steel = trSteel; var galv = trGalv;
            float Sgn(int i) => i == 0 ? -1f : 1f;

            // Main rails: I-section (top flange, web, bottom flange), dropped behind a raised gooseneck; web stiffeners.
            for (int i = 0; i < 2; i++)
            {
                float x = 0.52f * Sgn(i);
                w.Box(steel, new Vector3(x - 0.08f, 1.37f, TrailerRear), new Vector3(x + 0.08f, 1.40f, TrailerFront));
                w.Prism(steel, new[] { new Vector2(TrailerRear, 1.0f), new Vector2(4.3f, 1.0f), new Vector2(4.9f, 1.19f), new Vector2(4.9f, 1.37f), new Vector2(TrailerRear, 1.37f) }, x - 0.011f, x + 0.011f);
                w.Box(steel, new Vector3(x - 0.011f, 1.19f, 4.9f), new Vector3(x + 0.011f, 1.37f, TrailerFront));
                w.Box(steel, new Vector3(x - 0.08f, 0.97f, TrailerRear), new Vector3(x + 0.08f, 1.0f, 4.3f));
                w.Beam(steel, new Vector3(x, 0.985f, 4.28f), new Vector3(x, 1.18f, 4.92f), new Vector2(0.16f, 0.03f));
                for (float z = -5.8f; z < 4.2f; z += 1.0f) w.Box(steel, new Vector3(x - 0.075f, 1.0f, z - 0.006f), new Vector3(x + 0.075f, 1.37f, z + 0.006f), false);
            }
            // Cross-members between the rails.
            for (float z = -5.3f; z < 4.4f; z += 0.95f)
                if (Mathf.Abs(z) > 0.3f && Mathf.Abs(z - 3.7f) > 0.2f) w.Box(steel, new Vector3(-0.51f, 1.22f, z - 0.04f), new Vector3(0.51f, 1.34f, z + 0.04f));
            // Gooseneck coupler plate + kingpin.
            w.Box(steel, new Vector3(-0.98f, 1.15f, 4.75f), new Vector3(0.98f, 1.19f, TrailerFront));
            w.Box(steel, new Vector3(-0.98f, 1.19f, 6.15f), new Vector3(0.98f, 1.37f, TrailerFront));
            w.Tube(galv, new Vector3(0f, 1.15f, KingpinZ), new Vector3(0f, 1.12f, KingpinZ), 0.065f, 12);
            w.Tube(galv, new Vector3(0f, 1.12f, KingpinZ), new Vector3(0f, 1.05f, KingpinZ), 0.045f, 12);

            // Outriggers (bolsters): front and rear carry the 40 ft twistlocks, the centre pair the 20 ft inner ones; two plain ones.
            void Bolster(float z, float depth, bool locks, float lockZ)
            {
                w.Box(steel, new Vector3(-0.6f, 1.19f, z - depth * 0.5f), new Vector3(0.6f, 1.43f, z + depth * 0.5f));
                var save = w.M;
                w.M = save * Matrix4x4.TRS(new Vector3(0f, 0f, z), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
                foreach (float s in new[] { -1f, 1f })
                    w.Prism(steel, new[] { new Vector2(0.6f * s, 1.19f), new Vector2(1.22f * s, 1.33f), new Vector2(1.22f * s, 1.43f), new Vector2(0.6f * s, 1.43f) }, -depth * 0.5f, depth * 0.5f);
                w.M = save;
                if (!locks) return;
                foreach (float s in new[] { -1f, 1f })
                {
                    float x = 1.13f * s;
                    w.Box(galv, new Vector3(x - 0.09f, 1.43f, lockZ - 0.09f), new Vector3(x + 0.09f, DeckY, lockZ + 0.09f));         // seat
                    w.Box(galv, new Vector3(x - 0.07f, 1.3f, lockZ - 0.07f), new Vector3(x + 0.07f, 1.43f, lockZ + 0.07f));           // pocket
                    w.Beam(galv, new Vector3(x + 0.07f * s, 1.37f, lockZ), new Vector3(x + 0.17f * s, 1.27f, lockZ), new Vector2(0.025f, 0.025f));
                    w.Tube(trPlastic, new Vector3(x + 0.17f * s, 1.27f, lockZ), new Vector3(x + 0.22f * s, 1.22f, lockZ), 0.02f, 6);
                }
            }
            Bolster(6.04f, 0.22f, true, 6.007f);
            Bolster(-6.04f, 0.22f, true, -6.007f);
            Bolster(0f, 0.32f, true, 0.13f);
            foreach (float s in new[] { -1f, 1f })
                foreach (float t in new[] { -1f, 1f })
                {
                    float x = 1.13f * s, z = -0.13f;
                    if (t > 0f) continue;
                    w.Box(galv, new Vector3(x - 0.09f, 1.43f, z - 0.09f), new Vector3(x + 0.09f, DeckY, z + 0.09f));
                    w.Beam(galv, new Vector3(x + 0.07f * s, 1.37f, z), new Vector3(x + 0.17f * s, 1.27f, z), new Vector2(0.025f, 0.025f));
                }
            Bolster(3.05f, 0.12f, false, 0f);
            Bolster(-3.05f, 0.12f, false, 0f);

            // Landing legs (raised: coupled), cross shaft, gearbox + crank on the right, bracing.
            const float legZ = 3.7f;
            foreach (float s in new[] { -1f, 1f })
            {
                float x = 0.82f * s;
                w.Box(steel, new Vector3(Mathf.Min(0.5f * s, 0.96f * s), 1.19f, legZ - 0.16f), new Vector3(Mathf.Max(0.5f * s, 0.96f * s), 1.24f, legZ + 0.16f));
                w.Box(galv, new Vector3(x - 0.06f, 0.62f, legZ - 0.06f), new Vector3(x + 0.06f, 1.19f, legZ + 0.06f));
                w.Box(galv, new Vector3(x - 0.045f, 0.34f, legZ - 0.045f), new Vector3(x + 0.045f, 0.66f, legZ + 0.045f));
                w.Box(steel, new Vector3(x - 0.14f, 0.27f, legZ - 0.14f), new Vector3(x + 0.14f, 0.33f, legZ + 0.14f));
                w.Tube(galv, new Vector3(x - 0.06f, 0.36f, legZ), new Vector3(x + 0.06f, 0.36f, legZ), 0.03f, 8);
                w.Beam(steel, new Vector3(x, 0.72f, legZ - 0.05f), new Vector3(0.52f * s, 1.0f, legZ - 1.15f), new Vector2(0.05f, 0.05f));
            }
            w.Tube(galv, new Vector3(-0.76f, 1.02f, legZ), new Vector3(0.76f, 1.02f, legZ), 0.022f, 8);
            w.Beam(steel, new Vector3(-0.77f, 0.72f, legZ + 0.07f), new Vector3(0.77f, 1.15f, legZ + 0.07f), new Vector2(0.04f, 0.04f));
            w.Beam(steel, new Vector3(0.77f, 0.72f, legZ + 0.07f), new Vector3(-0.77f, 1.15f, legZ + 0.07f), new Vector2(0.04f, 0.04f));
            w.Box(galv, new Vector3(0.88f, 0.95f, legZ - 0.08f), new Vector3(1.0f, 1.13f, legZ + 0.08f));
            w.Tube(galv, new Vector3(1.0f, 1.04f, legZ), new Vector3(1.1f, 1.04f, legZ), 0.016f, 6);
            w.Tube(galv, new Vector3(1.1f, 1.04f, legZ), new Vector3(1.1f, 0.74f, legZ + 0.02f), 0.014f, 6);
            w.Tube(trPlastic, new Vector3(1.1f, 0.74f, legZ + 0.02f), new Vector3(1.1f, 0.74f, legZ + 0.14f), 0.018f, 6);

            // Air tank, toolbox, EBS modulator, cable / air runs along the left rail.
            w.Revolve(steel, new Vector3(0f, 0.86f, 0.6f), Vector3.forward,
                      new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0.1f), new Vector2(0.09f, 0.14f), new Vector2(1.21f, 0.14f), new Vector2(1.27f, 0.1f), new Vector2(1.3f, 0f) }, 14);
            foreach (float z in new[] { 0.85f, 1.65f })
            {
                w.Box(galv, new Vector3(-0.15f, 0.71f, z - 0.025f), new Vector3(0.15f, 0.73f, z + 0.025f));
                foreach (float s in new[] { -1f, 1f }) w.Box(galv, new Vector3(0.14f * s - 0.01f, 0.72f, z - 0.025f), new Vector3(0.14f * s + 0.01f, 1.22f, z + 0.025f));
            }
            w.Box(galv, new Vector3(-1.12f, 0.76f, 1.75f), new Vector3(-0.64f, 1.18f, 2.6f));
            w.Box(trPlastic, new Vector3(-1.125f, 1.1f, 1.75f), new Vector3(-1.12f, 1.12f, 2.6f), false);
            w.Box(trPlastic, new Vector3(-1.13f, 0.95f, 2.12f), new Vector3(-1.12f, 1.0f, 2.22f), false);
            w.Box(trPlastic, new Vector3(-0.16f, 0.98f, -2.42f), new Vector3(0.16f, 1.2f, -2.12f));
            w.Hose(trPlastic, new[] { new Vector3(-0.42f, 1.3f, 6.1f), new Vector3(-0.42f, 1.26f, 4.5f), new Vector3(-0.42f, 1.2f, 0f), new Vector3(-0.4f, 1.18f, -2.2f), new Vector3(-0.2f, 1.1f, -2.3f) }, 0.012f, 5);
            w.Hose(trHoseRed, new[] { new Vector3(-0.38f, 1.3f, 6.1f), new Vector3(-0.38f, 1.25f, 4.5f), new Vector3(-0.38f, 1.19f, 0f), new Vector3(-0.36f, 1.17f, -2.2f), new Vector3(-0.1f, 1.08f, -2.3f) }, 0.009f, 5);

            // Tri-axle bogie: axle beams, hangers, trailing arms, air springs, dampers, brake chambers, drums.
            foreach (float za in TrailerAxles)
            {
                w.Tube(steel, new Vector3(-0.92f, TrailerWheelR, za), new Vector3(0.92f, TrailerWheelR, za), 0.07f, 10);
                foreach (float s in new[] { -1f, 1f })
                {
                    float xr = 0.52f * s, xa = 0.64f * s;
                    w.Prism(steel, new[] { new Vector2(za + 0.38f, 1.0f), new Vector2(za + 0.74f, 1.0f), new Vector2(za + 0.64f, 0.72f), new Vector2(za + 0.48f, 0.72f) }, xr - 0.06f, xr + 0.06f);
                    w.Beam(steel, new Vector3(xa, 0.76f, za + 0.56f), new Vector3(xa, 0.56f, za - 0.46f), new Vector2(0.1f, 0.08f));
                    w.Tube(steel, new Vector3(xa, 0.6f, za - 0.42f), new Vector3(xa, 0.68f, za - 0.42f), 0.1f, 12);
                    w.Revolve(trPlastic, new Vector3(xa, 0.68f, za - 0.42f), Vector3.up,
                              new[] { new Vector2(0f, 0.11f), new Vector2(0.07f, 0.15f), new Vector2(0.14f, 0.125f), new Vector2(0.21f, 0.15f), new Vector2(0.27f, 0.12f), new Vector2(0.3f, 0f) }, 12);
                    w.Box(steel, new Vector3(xa - 0.14f, 0.97f, za - 0.56f), new Vector3(xa + 0.14f, 1.0f, za - 0.28f));
                    w.Tube(trPlastic, new Vector3(xa + 0.08f * s, 0.6f, za + 0.12f), new Vector3(xr + 0.05f * s, 0.98f, za + 0.34f), 0.035f, 8);
                    w.Tube(galv, new Vector3(0.38f * s, 0.6f, za - 0.06f), new Vector3(0.38f * s, 0.6f, za - 0.3f), 0.08f, 10);
                    w.Tube(trHub, new Vector3(0.74f * s, TrailerWheelR, za), new Vector3(0.9f * s, TrailerWheelR, za), 0.2f, 14);
                }
            }

            // Mudguards with ribs, side valance, front lip, struts; rubber mud flaps behind the last axle.
            float mz0 = TrailerAxles[0] + 0.6f, mz1 = TrailerAxles[2] - 0.6f;
            foreach (float s in new[] { -1f, 1f })
            {
                float xi = 0.85f * s, xo = 1.31f * s, x0 = Mathf.Min(xi, xo), x1 = Mathf.Max(xi, xo);
                w.Box(trPlastic, new Vector3(x0, 1.12f, mz1), new Vector3(x1, 1.15f, mz0));
                w.Box(trPlastic, new Vector3(s > 0 ? xo - 0.012f : xo, 0.98f, mz1), new Vector3(s > 0 ? xo : xo + 0.012f, 1.15f, mz0));
                w.Beam(trPlastic, new Vector3((x0 + x1) * 0.5f, 1.135f, mz0), new Vector3((x0 + x1) * 0.5f, 0.9f, mz0 + 0.22f), new Vector2(x1 - x0, 0.025f));
                for (float z = mz1 + 0.5f; z < mz0 - 0.3f; z += 0.65f) w.Box(trPlastic, new Vector3(x0 + 0.02f, 1.15f, z - 0.015f), new Vector3(x1 - 0.02f, 1.17f, z + 0.015f), false);
                foreach (float za in new[] { TrailerAxles[0] - 0.65f, TrailerAxles[1] - 0.65f })
                    w.Beam(galv, new Vector3(0.58f * s, 1.2f, za), new Vector3(0.9f * s, 1.12f, za), new Vector2(0.04f, 0.04f));
                w.Box(rubber, new Vector3(x0 + 0.01f, 0.22f, mz1 - 0.025f), new Vector3(x1 - 0.01f, 1.12f, mz1 - 0.013f));
                w.Box(trTapeWhite, new Vector3(x0 + 0.05f, 0.3f, mz1 - 0.028f), new Vector3(x1 - 0.05f, 0.34f, mz1 - 0.025f), false);
                w.Box(trMarker, new Vector3(xo < 0 ? xo - 0.02f : xo, 1.03f, TrailerAxles[1] - 0.04f), new Vector3(xo < 0 ? xo : xo + 0.02f, 1.08f, TrailerAxles[1] + 0.04f), false);
            }

            // Side under-run guards: two rails, brackets back to the rails, segmented conspicuity tape, amber side markers.
            const float sgFront = 3.35f, sgRear = -2.2f;
            foreach (float s in new[] { -1f, 1f })
            {
                float xo = 1.23f * s, xi = 1.19f * s, x0 = Mathf.Min(xi, xo), x1 = Mathf.Max(xi, xo);
                w.Box(galv, new Vector3(x0, 0.5f, sgRear), new Vector3(x1, 0.6f, sgFront));
                w.Box(galv, new Vector3(x0, 0.88f, sgRear), new Vector3(x1, 0.98f, sgFront));
                foreach (float z in new[] { 3.25f, 1.25f, -0.75f, -2.12f })
                {
                    float bi = 1.15f * s;
                    w.Box(steel, new Vector3(Mathf.Min(bi, xi), 0.48f, z - 0.03f), new Vector3(Mathf.Max(bi, xi), 1.24f, z + 0.03f));
                    w.Box(steel, new Vector3(Mathf.Min(0.6f * s, bi), 1.19f, z - 0.03f), new Vector3(Mathf.Max(0.6f * s, bi), 1.24f, z + 0.03f));
                }
                float tx0 = s > 0 ? x1 : x0 - 0.003f, tx1 = s > 0 ? x1 + 0.003f : x0;
                for (float z = sgRear + 0.08f; z + 0.42f < sgFront; z += 0.56f)
                    w.Box(trTapeYellow, new Vector3(tx0, 0.9f, z), new Vector3(tx1, 0.96f, z + 0.42f), false);
                foreach (float z in new[] { 3.25f, -0.75f })
                    w.Box(trMarker, new Vector3(tx0, 0.7f, z - 0.045f), new Vector3(tx1 + 0.015f * s, 0.77f, z + 0.045f), false);
                // Contour tape along the outrigger ends (upper outline).
                foreach (var (za, zb) in new[] { (5.93f, 6.15f), (-6.15f, -5.93f), (-0.16f, 0.16f) })
                    w.Box(trTapeYellow, new Vector3(s > 0 ? 1.22f : -1.223f, 1.35f, za), new Vector3(s > 0 ? 1.223f : -1.22f, 1.41f, zb), false);
            }

            // Rear: end cross-member, under-run bar with red / white tape, lamp carrier, lamp clusters, triangles, plate.
            w.Box(steel, new Vector3(-1.0f, 1.0f, TrailerRear), new Vector3(1.0f, 1.25f, TrailerRear + 0.1f));
            w.Box(galv, new Vector3(-1.1f, 0.4f, TrailerRear - 0.13f), new Vector3(1.1f, 0.55f, TrailerRear - 0.01f));
            for (int k = 0; k < 11; k++)
            {
                float x = -1.1f + k * 0.2f;
                w.Box(k % 2 == 0 ? trTapeRed : trTapeWhite, new Vector3(x, 0.42f, TrailerRear - 0.134f), new Vector3(x + 0.2f, 0.53f, TrailerRear - 0.13f), false);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                w.Beam(steel, new Vector3(0.52f * s, 1.0f, TrailerRear + 0.05f), new Vector3(0.52f * s, 0.55f, TrailerRear - 0.07f), new Vector2(0.08f, 0.08f));
                w.Beam(steel, new Vector3(0.52f * s, 1.0f, TrailerRear + 0.6f), new Vector3(0.62f * s, 0.55f, TrailerRear - 0.05f), new Vector2(0.06f, 0.06f));
            }
            w.Box(steel, new Vector3(-1.22f, 0.72f, TrailerRear - 0.02f), new Vector3(1.22f, 0.76f, TrailerRear + 0.05f));
            for (int k = 0; k < 13; k++)
            {
                float x = -1.2f + k * (2.4f / 13f);
                w.Box(k % 2 == 0 ? trTapeRed : trTapeWhite, new Vector3(x, 1.33f, -6.157f), new Vector3(x + 2.4f / 13f, 1.4f, -6.153f), false);
            }
            const float lz = TrailerRear - 0.06f;
            foreach (float s in new[] { -1f, 1f })
            {
                float c = 0.95f * s;
                w.Box(trPlastic, new Vector3(c - 0.25f, 0.76f, lz - 0.02f), new Vector3(c + 0.25f, 0.93f, lz + 0.06f));
                // outer → inner: indicator, tail/brake, reverse (right) or fog (left).
                float xo = c + 0.24f * s;
                Vector3 L(float a, float b) => new(Mathf.Min(c + a * s, c + b * s), 0.785f, lz - 0.03f);
                Vector3 H(float a, float b) => new(Mathf.Max(c + a * s, c + b * s), 0.905f, lz - 0.02f);
                w.Box(trLensAmber, L(0.11f, 0.23f), H(0.11f, 0.23f), false);
                brake.Box(tailLamp, L(-0.07f, 0.1f), H(-0.07f, 0.1f), false);
                if (s > 0f) w.Box(trLensWhite, L(-0.23f, -0.08f), H(-0.23f, -0.08f), false);
                else brake.Box(tailLamp, L(-0.23f, -0.08f), H(-0.23f, -0.08f), false);
                w.Box(steel, new Vector3(c - 0.09f, 0.57f, lz), new Vector3(c + 0.09f, 0.72f, lz + 0.02f));
                Vector3 T0 = new(c - 0.075f, 0.59f, lz - 0.006f), T1 = new(c + 0.075f, 0.59f, lz - 0.006f), T2 = new(c, 0.7f, lz - 0.006f);
                w.Tri(trReflRed, T0, T1, T2, Vector3.back);
            }
            w.Box(steel, new Vector3(-0.3f, 0.58f, lz), new Vector3(0.3f, 0.72f, lz + 0.03f));
            w.Box(trPlate, new Vector3(-0.26f, 0.6f, lz - 0.006f), new Vector3(0.26f, 0.7f, lz), false);

            // Front: white position markers on the gooseneck corners, air / electric couplings.
            foreach (float s in new[] { -1f, 1f })
                w.Box(trFrontMarker, new Vector3(1.14f * s - 0.04f, 1.3f, TrailerFront - 0.02f), new Vector3(1.14f * s + 0.04f, 1.36f, TrailerFront + 0.01f), false);
            w.Box(trHoseRed, new Vector3(-0.32f, 1.24f, TrailerFront), new Vector3(-0.22f, 1.31f, TrailerFront + 0.06f));
            w.Box(trHoseYellow, new Vector3(-0.17f, 1.24f, TrailerFront), new Vector3(-0.07f, 1.31f, TrailerFront + 0.06f));
            w.Tube(trPlastic, new Vector3(0.15f, 1.28f, TrailerFront), new Vector3(0.15f, 1.28f, TrailerFront + 0.07f), 0.045f, 10);
        }

        // ---------------- Wheels ----------------

        /// <summary>
        /// A super-single 385/65 R22.5 on a steel disc wheel, axis x, outer (rim) face toward +x. Tyre with shoulders,
        /// sidewall bulge and four circumferential grooves; rim flange, dish, centre bore, hub cap, ten wheel nuts.
        /// </summary>
        private static HarborTruckMesh TrailerWheelGeo()
        {
            var w = new HarborTruckMesh { Tile = 0.5f };
            float R = TrailerWheelR;
            var prof = new List<Vector2> { new(-0.15f, 0.29f), new(-0.172f, 0.33f), new(-0.19f, 0.4f), new(-0.19f, 0.47f), new(-0.178f, 0.505f), new(-0.158f, R - 0.008f), new(-0.13f, R) };
            foreach (float g in new[] { -0.07f, 0f, 0.07f })
            {
                prof.Add(new Vector2(g - 0.006f, R)); prof.Add(new Vector2(g - 0.006f, R - 0.014f)); prof.Add(new Vector2(g + 0.006f, R - 0.014f)); prof.Add(new Vector2(g + 0.006f, R));
            }
            prof.AddRange(new[] { new Vector2(0.13f, R), new Vector2(0.158f, R - 0.008f), new Vector2(0.178f, 0.505f), new Vector2(0.19f, 0.47f), new Vector2(0.19f, 0.4f), new Vector2(0.172f, 0.33f), new Vector2(0.15f, 0.29f) });
            w.Revolve(trTyre, Vector3.zero, Vector3.right, prof.ToArray(), 36);
            w.Revolve(trRim, Vector3.zero, Vector3.right, new[]
            {
                new Vector2(0.16f, 0.302f), new Vector2(0.15f, 0.286f), new Vector2(0.085f, 0.272f), new Vector2(0.062f, 0.225f), new Vector2(0.062f, 0.13f),
                new Vector2(0.076f, 0.112f), new Vector2(0.076f, 0.068f)
            }, 32);
            w.Revolve(trHub, Vector3.zero, Vector3.right, new[] { new Vector2(0.076f, 0.068f), new Vector2(0.115f, 0.055f), new Vector2(0.13f, 0.03f), new Vector2(0.132f, 0f) }, 16);
            w.Revolve(trHub, Vector3.zero, Vector3.right, new[] { new Vector2(-0.12f, 0f), new Vector2(-0.12f, 0.29f) }, 16);
            for (int k = 0; k < 10; k++)
            {
                float a = k / 10f * Mathf.PI * 2f;
                var p = new Vector3(0.062f, Mathf.Cos(a) * 0.17f, Mathf.Sin(a) * 0.17f);
                w.Tube(trGalv, p, p + Vector3.right * 0.032f, 0.017f, 6);
            }
            // Hand holes: dark recesses in the dish between the nuts.
            for (int k = 0; k < 5; k++)
            {
                float a = (k + 0.5f) / 5f * Mathf.PI * 2f;
                var p = new Vector3(0.0635f, Mathf.Cos(a) * 0.2f, Mathf.Sin(a) * 0.2f);
                var r = Quaternion.LookRotation(Vector3.right, new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)));
                w.OBox(trHub, p, new Vector3(0.05f, 0.03f, 0.002f), r);
            }
            return w;
        }

        // ---------------- Container ----------------

        /// <summary>
        /// A 40 ft ISO container in <paramref name="paint"/> (the project's corrugated container material, 4 m tile) with its
        /// structure: corner posts and castings, top and bottom side rails, a door end (two leaves, four locking bars with cam
        /// keepers and handles, hinges) at −z and a plain front end. Underside at y 0, centred on x / z.
        /// </summary>
        private static HarborTruckMesh ContainerGeo(Material paint)
        {
            var w = new HarborTruckMesh { Tile = 4f };
            float hx = ContainerSize.x * 0.5f, h = ContainerSize.y, hz = ContainerSize.z * 0.5f;
            w.Box(paint, new Vector3(-hx + 0.03f, 0.03f, -hz + 0.05f), new Vector3(hx - 0.03f, h - 0.03f, hz - 0.05f));
            w.Tile = 1f;
            var frame = paint;
            // Rails and posts (proud of the corrugated panels).
            foreach (float s in new[] { -1f, 1f })
            {
                w.Box(frame, new Vector3(s * hx - 0.04f, 0f, -hz), new Vector3(s * hx + 0.0f, 0.16f, hz));
                w.Box(frame, new Vector3(s * hx - 0.04f, h - 0.1f, -hz), new Vector3(s * hx + 0.0f, h, hz));
                foreach (float t in new[] { -1f, 1f })
                {
                    float px0 = s > 0 ? hx - 0.13f : -hx, pz0 = t > 0 ? hz - 0.12f : -hz;
                    w.Box(frame, new Vector3(px0, 0f, pz0), new Vector3(px0 + 0.13f, h, pz0 + 0.12f));
                }
            }
            w.Box(frame, new Vector3(-hx, h - 0.12f, hz - 0.12f), new Vector3(hx, h, hz));
            w.Box(frame, new Vector3(-hx, 0f, hz - 0.12f), new Vector3(hx, 0.2f, hz));
            w.Box(frame, new Vector3(-hx, h - 0.16f, -hz), new Vector3(hx, h, -hz + 0.12f));
            w.Box(frame, new Vector3(-hx, 0f, -hz), new Vector3(hx, 0.24f, -hz + 0.12f));
            // Corner castings (dark, weathered).
            foreach (float s in new[] { -1f, 1f })
                foreach (float t in new[] { -1f, 1f })
                    foreach (float y in new[] { 0f, h - 0.118f })
                    {
                        var c = new Vector3(s * (hx - 0.089f), y + 0.059f, t * (hz - 0.089f));
                        w.OBox(trCorner, c, new Vector3(0.18f, 0.118f, 0.18f) + Vector3.one * 0.004f, Quaternion.identity);
                    }
            // Door end at −z: centre seam, four locking bars with cam keepers and handles, hinges.
            float dz = -hz - 0.005f;
            w.Box(trCorner, new Vector3(-0.012f, 0.24f, dz - 0.003f), new Vector3(0.012f, h - 0.16f, dz + 0.004f), false);
            foreach (float x in new[] { -0.95f, -0.32f, 0.32f, 0.95f })
            {
                w.Tube(trGalv, new Vector3(x, 0.2f, dz - 0.035f), new Vector3(x, h - 0.12f, dz - 0.035f), 0.019f, 8);
                foreach (float y in new[] { 0.2f, h - 0.14f }) w.Box(trGalv, new Vector3(x - 0.04f, y - 0.03f, dz - 0.06f), new Vector3(x + 0.04f, y + 0.03f, dz));
                foreach (float y in new[] { 0.75f, 1.6f, 2.2f }) w.Box(trGalv, new Vector3(x - 0.03f, y - 0.02f, dz - 0.045f), new Vector3(x + 0.03f, y + 0.02f, dz));
                float hxs = x > 0f ? -1f : 1f;
                w.Beam(trGalv, new Vector3(x, 1.2f, dz - 0.05f), new Vector3(x + hxs * 0.32f, 1.2f, dz - 0.05f), new Vector2(0.03f, 0.03f));
                w.Box(trGalv, new Vector3(Mathf.Min(x + hxs * 0.32f, x + hxs * 0.36f), 1.15f, dz - 0.03f), new Vector3(Mathf.Max(x + hxs * 0.32f, x + hxs * 0.36f), 1.25f, dz));
            }
            foreach (float s in new[] { -1f, 1f })
                foreach (float y in new[] { 0.4f, 1.0f, 1.6f, 2.2f })
                    w.Box(trCorner, new Vector3(s * hx - (s > 0 ? 0.1f : 0.02f), y - 0.05f, dz - 0.03f), new Vector3(s * hx + (s > 0 ? 0.02f : 0.1f), y + 0.05f, dz));
            return w;
        }

        // ---------------- Yard containers ----------------

        /// <summary>
        /// A stacked / loose container for the batched yard meshes: the corrugated box with end frames and a dark door-gear
        /// panel at the +long-axis end, so stacks read as individual containers. <paramref name="size"/> is the box size in
        /// its local frame (the long side is x or z).
        /// </summary>
        private static void StackBox(Material paint, Vector3 centre, Vector3 size, Quaternion rot)
        {
            // Budget version (thousands of these are static-batched without LODs): the box, a slightly proud end frame at
            // each end (posts + rails in one, so stacks read as separate boxes with dark seams between them) and the door
            // end's locking-bar panel in dark steel — five boxes, ~60 triangles.
            bool alongX = size.x > size.z;
            float hl = (alongX ? size.x : size.z) * 0.5f, hw = (alongX ? size.z : size.x) * 0.5f;
            Vector3 L(float l, float y, float w) => centre + rot * (alongX ? new Vector3(l, y, w) : new Vector3(w, y, l));
            Vector3 S(float l, float y, float w) => alongX ? new Vector3(l, y, w) : new Vector3(w, y, l);
            var dark = trCorner != null ? trCorner : rubber;
            B(paint).OrientedBox(centre, S(hl * 2f - 0.3f, size.y - 0.08f, hw * 2f - 0.06f), rot, 4f);
            foreach (float e in new[] { -1f, 1f })
                B(paint).OrientedBox(L(e * (hl - 0.075f), 0f, 0f), S(0.15f, size.y, hw * 2f), rot, 4f);
            B(dark).OrientedBox(L(hl + 0.02f, 0f, 0f), S(0.03f, size.y - 0.4f, hw * 2f - 0.5f), rot, 1f);
            B(dark).OrientedBox(L(-hl + 0.12f, -size.y * 0.5f + 0.06f, 0f), S(0.22f, 0.12f, hw * 2f + 0.01f), rot, 1f);
        }

        // ---------------- Assets ----------------

        private static Mesh SaveTruckMesh(HarborTruckMesh w, string name, int lods)
        {
            EnsureTruckDir();
            var mesh = w.ToMesh(name);
            if (lods > 1) MeshLodUtility.GenerateMeshLods(mesh, lods);
            string path = $"{TruckDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static readonly Dictionary<Material, Mesh> containerMeshes = new();
        private static readonly Dictionary<Material, Material[]> containerMats = new();

        private static Mesh ContainerMesh(Material paint)
        {
            if (containerMeshes.TryGetValue(paint, out var m) && m != null) return m;
            var w = ContainerGeo(paint);
            m = SaveTruckMesh(w, $"HR_Container40_{paint.name}", 3);
            containerMeshes[paint] = m;
            containerMats[paint] = Mats(w);
            return m;
        }

        // ---------------- Container markings ----------------

        private static readonly string[] OwnerCodes = { "NRXU", "KWBU", "TLHU", "SKRU", "HBCU", "VRGU" };
        private static readonly Dictionary<string, Material> markingMats = new();

        /// <summary>White stencil text (the project's pixel font, scaled ×4) on a transparent texture; alpha-clipped Lit material.</summary>
        private static Material MarkingMaterial(string key, string[] lines, Color ink)
        {
            if (markingMats.TryGetValue(key, out var mm) && mm != null) return mm;
            const int sc = 4;
            int tw = 0;
            foreach (var l in lines) tw = Mathf.Max(tw, PixelFont.Measure(l));
            int w = (tw + 4) * sc, lh = PixelFont.Height + 3, h = (lines.Length * lh + 2) * sc;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var clear = new Color(ink.r, ink.g, ink.b, 0f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, clear);
            for (int li = 0; li < lines.Length; li++)
                for (int gy = 0; gy < PixelFont.Height; gy++)
                    for (int gx = 0; gx < PixelFont.Measure(lines[li]); gx++)
                    {
                        int ci = gx / (PixelFont.Width + 1), cx = gx % (PixelFont.Width + 1);
                        if (cx >= PixelFont.Width || !PixelFont.Pixel(lines[li][ci], cx, gy)) continue;
                        int px = (gx + 2) * sc, py = h - (2 + li * lh + gy + 1) * sc;
                        for (int a = 0; a < sc; a++) for (int b = 0; b < sc; b++) tex.SetPixel(px + a, py + b, ink);
                    }
            tex.Apply();
            string file = new string(System.Array.ConvertAll(key.ToCharArray(), ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            string path = $"{TruckDir}/HR_Mark_{file}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.SaveAndReimport();
            var m = Lit("HR_Mark_" + file, Color.white, 0.45f, 0f, null);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.45f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            markingMats[key] = m;
            return m;
        }

        /// <summary>
        /// Owner code + serial + check digit on the doors and both sides, size-type code (45G1) and gross / tare plate on the
        /// doors, and a small orange hazard placard — on the container mesh object <paramref name="box"/> (local container frame).
        /// </summary>
        private static void ContainerMarkings(Transform box, int seed)
        {
            var rng = new System.Random(seed);
            string code = $"{OwnerCodes[rng.Next(OwnerCodes.Length)]} {rng.Next(100000, 999999)} {rng.Next(10)}";
            var ink = new Color(0.86f, 0.86f, 0.82f, 1f);
            var main = MarkingMaterial(code, new[] { code }, ink);
            var plate = MarkingMaterial("doorplate", new[] { "45G1", "MAX GROSS 30480 KG", "TARE 3750 KG" }, ink);
            var hazard = MarkingMaterial("hazard3", new[] { " 3 " }, new Color(0.9f, 0.45f, 0.05f, 1f));
            var w = new HarborTruckMesh();
            float hx = ContainerSize.x * 0.5f, hz = ContainerSize.z * 0.5f;
            void Label(Material m, Vector3 centre, Vector3 right, Vector3 up, float width, float height, Vector3 normal)
            {
                var a = centre - right * width * 0.5f - up * height * 0.5f;
                w.QuadUV(m, a, a + right * width, a + right * width + up * height, a + up * height, normal, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            }
            float dz = -hz - 0.012f;
            Label(main, new Vector3(0.62f, 2.28f, dz), Vector3.right, Vector3.up, 0.95f, 0.11f, Vector3.back);
            Label(plate, new Vector3(0.62f, 1.95f, dz), Vector3.right, Vector3.up, 0.6f, 0.17f, Vector3.back);
            foreach (float s in new[] { -1f, 1f })
                Label(main, new Vector3(s * (hx + 0.004f), 2.3f, -hz + 1.6f), s > 0 ? Vector3.forward : Vector3.back, Vector3.up, 1.6f, 0.19f, Vector3.right * s);
            var hzC = new Vector3(hx + 0.004f, 1.2f, -hz + 0.6f);
            w.QuadUV(hazard, hzC + Vector3.down * 0.13f, hzC + Vector3.back * 0.13f, hzC + Vector3.up * 0.13f, hzC + Vector3.forward * 0.13f, Vector3.right,
                     new Vector2(0.5f, 0f), new Vector2(1f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f));
            var mesh = w.ToMesh("HR_ContainerMarks_" + code.Replace(' ', '_'));
            SaveTruckMesh(w, "HR_ContainerMarks_" + code.Replace(' ', '_'), 1);
            Part("Markings", box, AssetDatabase.LoadAssetAtPath<Mesh>($"{TruckDir}/HR_ContainerMarks_{code.Replace(' ', '_')}.asset"), Mats(w), detail, false);
            Object.DestroyImmediate(mesh);
        }

        private static Material[] Mats(HarborTruckMesh w) => new List<Material>(w.Materials).ToArray();
    }
}
