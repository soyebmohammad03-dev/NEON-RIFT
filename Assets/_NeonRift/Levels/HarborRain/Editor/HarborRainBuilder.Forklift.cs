using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain 2.2 forklift: an original 2.5 t counterbalance truck (the shortlisted licensed model needs a Sketchfab
    /// login) — lofted body with a rounded counterweight and hazard chevrons, front fenders, overhead guard with angled
    /// posts and roof slats, seat, steering column, levers, LPG bottle, work lights and beacon; a two-stage mast (outer
    /// channels tilting on the chassis with tilt cylinders, inner channels that extend above the free lift on twin lift
    /// cylinders, chains); a carriage with fork bar, load backrest and two tapered forks. Driven by HarborForklift.
    /// Euro pallet units (pallet with fork pockets + wrapped cartons / drums / crate) are the loads it restacks.
    /// Frame: +z forward, ground y 0, front axle z 0.62, rear (steering) axle z −1.03, mast pivot z 1.05.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        public const string ForkliftPrefab = Gen + "/Truck/PF_HR_Forklift.prefab";
        public const string ForkliftParkedPrefab = Gen + "/Truck/PF_HR_ForkliftParked.prefab";
        private const float FlFront = 0.62f, FlRear = -1.03f, FlMastZ = 1.05f, FlFrontR = 0.33f, FlRearR = 0.27f;
        private static Material flPaint, flBlack, flCarton, flWrap, flDrum, flLpg;
        private static GameObject forkliftPrefab, forkliftParked;
        private static Mesh[] palletUnitMeshes;
        private static Material[][] palletUnitMats;

        private static void ForkliftMaterials()
        {
            flPaint = Weathered("HR_ForkliftPaint", new Color(0.78f, 0.55f, 0.06f), 0.25f, 0.15f, 0.62f, 31);
            flBlack = Weathered("HR_ForkliftBlack", new Color(0.05f, 0.05f, 0.055f), 0.15f, 0.3f, 0.5f, 32);
            flCarton = Weathered("HR_Carton", new Color(0.36f, 0.26f, 0.15f), 0f, 0f, 0.25f, 33);
            flWrap = Lit("HR_StretchWrap", new Color(0.24f, 0.26f, 0.28f), 0.45f, 0f, null);
            flDrum = Weathered("HR_DrumBlue", new Color(0.06f, 0.16f, 0.36f), 0.3f, 0.4f, 0.6f, 34);
            flLpg = Weathered("HR_LpgBottle", new Color(0.62f, 0.63f, 0.64f), 0.1f, 0.6f, 0.62f, 35);
            if (palletWood == null) palletWood = Lit("HR_PalletWood", new Color(0.32f, 0.24f, 0.15f), 0.25f, 0f, null);
        }

        // ---------------- Geometry ----------------

        private static void ForkliftBodyGeo(HarborTruckMesh w)
        {
            var paint = flPaint; var black = flBlack;
            // Body: horizontal rounded slices, the big radius at the back is the counterweight.
            var rings = new List<Vector3[]>();
            foreach (var (y, inset) in new[] { (0.3f, 0.02f), (0.38f, 0f), (0.9f, 0f), (1.05f, 0.02f), (1.12f, 0.06f) })
                rings.Add(HarborTruckMesh.RoundRectXZ(y, 0.52f - inset, -1.48f + inset, 0.92f - inset, 0.42f, 0.08f, 5));
            w.Loft(paint, rings, false, true);
            // Counterweight crown (dark), rear hazard chevrons, tow pin, step, fenders.
            var crown = new List<Vector3[]>();
            foreach (var (y, inset) in new[] { (1.1f, 0.05f), (1.3f, 0.05f), (1.42f, 0.12f) })
                crown.Add(HarborTruckMesh.RoundRectXZ(y, 0.52f - inset, -1.48f + inset, -0.72f, 0.38f, 0.06f, 5));
            w.Loft(black, crown, false, true);
            for (int k = 0; k < 5; k++)
            {
                float x = -0.36f + k * 0.18f;
                w.OBox(k % 2 == 0 ? trTapeYellow : black, new Vector3(x, 0.62f, -1.485f), new Vector3(0.17f, 0.2f, 0.01f), Quaternion.Euler(0f, 0f, 35f));
            }
            w.Tube(trGalv, new Vector3(0f, 0.5f, -1.48f), new Vector3(0f, 0.5f, -1.58f), 0.05f, 8);
            w.Box(black, new Vector3(-0.53f, 0.32f, 0.18f), new Vector3(-0.44f, 0.4f, 0.45f));
            foreach (float s in new[] { -1f, 1f })
                w.Revolve(black, new Vector3(0.62f * s, FlFrontR, FlFront), Vector3.right,
                          new[] { new Vector2(-0.15f, 0.37f), new Vector2(-0.15f, 0.4f), new Vector2(0.15f, 0.4f), new Vector2(0.15f, 0.37f), new Vector2(-0.15f, 0.37f) }, 10, 130f, 205f);
            // Operator compartment: floor, seat, hood, steering column and wheel, levers, dash.
            w.Box(black, new Vector3(-0.46f, 1.05f, 0.05f), new Vector3(0.46f, 1.1f, 0.88f));
            w.Box(paint, new Vector3(-0.45f, 1.05f, -0.75f), new Vector3(0.45f, 1.3f, 0.05f));
            w.Box(black, new Vector3(-0.24f, 1.3f, -0.55f), new Vector3(0.24f, 1.42f, -0.05f));
            w.OBox(black, new Vector3(0f, 1.68f, -0.6f), new Vector3(0.46f, 0.55f, 0.1f), Quaternion.Euler(-10f, 0f, 0f));
            w.Box(black, new Vector3(-0.42f, 1.1f, 0.62f), new Vector3(0.42f, 1.38f, 0.88f));
            w.Tube(black, new Vector3(0f, 1.3f, 0.66f), new Vector3(0f, 1.72f, 0.42f), 0.035f, 8);
            var wc = new Vector3(0f, 1.74f, 0.4f);
            var ring = new List<Vector3>();
            for (int k = 0; k <= 16; k++) { float a = k / 16f * Mathf.PI * 2f; ring.Add(wc + Quaternion.Euler(-30f, 0f, 0f) * new Vector3(Mathf.Cos(a) * 0.17f, 0f, Mathf.Sin(a) * 0.17f)); }
            w.Hose(black, ring, 0.016f, 5);
            for (int k = 0; k < 3; k++)
            {
                var b = new Vector3(0.2f + k * 0.06f, 1.38f, 0.72f);
                w.Tube(trGalv, b, b + new Vector3(0f, 0.2f, -0.04f), 0.008f, 5);
                w.Tube(k == 0 ? trTapeYellow : black, b + new Vector3(0f, 0.2f, -0.04f), b + new Vector3(0f, 0.25f, -0.05f), 0.018f, 6);
            }
            // LPG bottle on its cradle behind the seat.
            w.Revolve(flLpg, new Vector3(-0.4f, 1.55f, -1.02f), Vector3.right,
                      new[] { new Vector2(0f, 0f), new Vector2(0.04f, 0.13f), new Vector2(0.1f, 0.17f), new Vector2(0.7f, 0.17f), new Vector2(0.76f, 0.13f), new Vector2(0.8f, 0f) }, 14);
            foreach (float x in new[] { -0.25f, 0.25f }) w.Box(black, new Vector3(x - 0.02f, 1.36f, -1.2f), new Vector3(x + 0.02f, 1.6f, -0.84f));
            w.Tube(trGalv, new Vector3(0.4f, 1.55f, -1.02f), new Vector3(0.48f, 1.6f, -1.02f), 0.025f, 6);
            // Overhead guard: angled front posts, rear posts, roof frame and slats.
            foreach (float s in new[] { -1f, 1f })
            {
                w.Beam(black, new Vector3(0.47f * s, 1.08f, 0.82f), new Vector3(0.47f * s, 2.18f, 0.66f), new Vector2(0.07f, 0.07f));
                w.Beam(black, new Vector3(0.47f * s, 1.3f, -0.82f), new Vector3(0.47f * s, 2.18f, -0.7f), new Vector2(0.07f, 0.07f));
                w.Box(black, new Vector3(0.47f * s - 0.035f, 2.15f, -0.78f), new Vector3(0.47f * s + 0.035f, 2.23f, 0.74f));
            }
            foreach (float z in new[] { -0.74f, 0.7f }) w.Box(black, new Vector3(-0.5f, 2.15f, z - 0.035f), new Vector3(0.5f, 2.23f, z + 0.035f));
            for (float z = -0.55f; z < 0.6f; z += 0.17f) w.Box(black, new Vector3(-0.44f, 2.19f, z - 0.02f), new Vector3(0.44f, 2.22f, z + 0.02f));
            // Lamps: twin work lights on the guard, rear lamps, reverse lamp.
            foreach (float s in new[] { -1f, 1f })
            {
                w.Box(black, new Vector3(0.38f * s - 0.06f, 2.06f, 0.7f), new Vector3(0.38f * s + 0.06f, 2.16f, 0.78f));
                w.Box(tcHeadLens, new Vector3(0.38f * s - 0.05f, 2.07f, 0.78f), new Vector3(0.38f * s + 0.05f, 2.15f, 0.785f), false);
                w.Box(tailLamp, new Vector3(0.36f * s - 0.06f, 1.0f, -1.49f), new Vector3(0.36f * s + 0.06f, 1.08f, -1.47f), false);
            }
            w.Box(trLensWhite, new Vector3(-0.05f, 2.04f, -0.79f), new Vector3(0.05f, 2.12f, -0.77f), false);
            // Front axle housing / mast mount.
            w.Box(black, new Vector3(-0.5f, 0.22f, 0.86f), new Vector3(0.5f, 0.45f, 1.0f));
        }

        private static void ForkliftMastGeo(HarborTruckMesh outer, HarborTruckMesh inner, HarborTruckMesh carriage)
        {
            var steel = flBlack;
            // Outer channels (mast frame, pivots at its foot), cross ties, tilt cylinders back to the chassis, lift cylinder barrels.
            foreach (float s in new[] { -1f, 1f })
            {
                outer.Box(steel, new Vector3(0.36f * s - 0.06f, 0.12f, -0.04f), new Vector3(0.36f * s + 0.06f, 2.3f, 0.08f));
                outer.Box(steel, new Vector3(0.36f * s - 0.06f, 0.12f, -0.06f), new Vector3(0.36f * s + 0.06f, 2.3f, -0.04f), false);
                outer.Tube(trGalv, new Vector3(0.36f * s, 0.95f, -0.04f), new Vector3(0.3f * s, 1.05f, -0.42f), 0.04f, 8);
                outer.Tube(steel, new Vector3(0.3f * s, 1.05f, -0.42f), new Vector3(0.28f * s, 1.08f, -0.55f), 0.055f, 8);
                outer.Tube(steel, new Vector3(0.22f * s, 0.15f, -0.1f), new Vector3(0.22f * s, 1.55f, -0.1f), 0.05f, 10);
            }
            foreach (float y in new[] { 0.2f, 1.2f, 2.22f }) outer.Box(steel, new Vector3(-0.42f, y, -0.05f), new Vector3(0.42f, y + 0.08f, 0.04f));
            // Inner channels (slide up above the free lift), top tie, chain sheaves, lift cylinder rods, chains.
            foreach (float s in new[] { -1f, 1f })
            {
                inner.Box(steel, new Vector3(0.27f * s - 0.05f, 0.18f, 0.0f), new Vector3(0.27f * s + 0.05f, 2.32f, 0.1f));
                inner.Tube(tcChrome, new Vector3(0.22f * s, 1.3f, -0.1f), new Vector3(0.22f * s, 2.3f, -0.1f), 0.028f, 8);
                inner.Tube(trGalv, new Vector3(0.14f * s - 0.03f, 2.28f, 0.05f), new Vector3(0.14f * s + 0.03f, 2.28f, 0.05f), 0.07f, 10);
                inner.Box(trHub, new Vector3(0.14f * s - 0.015f, 0.5f, 0.1f), new Vector3(0.14f * s + 0.015f, 2.26f, 0.12f), false);
            }
            inner.Box(steel, new Vector3(-0.32f, 2.26f, -0.02f), new Vector3(0.32f, 2.36f, 0.1f));
            // Carriage: fork bar, side plates, load backrest grid, forks (shank + tapered blade).
            carriage.Box(steel, new Vector3(-0.55f, 0.08f, 0.12f), new Vector3(0.55f, 0.48f, 0.18f));
            foreach (float y in new[] { 0.1f, 0.4f }) carriage.Box(trGalv, new Vector3(-0.56f, y, 0.17f), new Vector3(0.56f, y + 0.06f, 0.2f));
            carriage.Box(steel, new Vector3(-0.55f, 0.48f, 0.13f), new Vector3(0.55f, 0.53f, 0.17f));
            carriage.Box(steel, new Vector3(-0.55f, 1.18f, 0.13f), new Vector3(0.55f, 1.23f, 0.17f));
            for (float x = -0.5f; x <= 0.51f; x += 0.125f) carriage.Box(steel, new Vector3(x - 0.015f, 0.53f, 0.14f), new Vector3(x + 0.015f, 1.18f, 0.16f), false);
            foreach (float s in new[] { -1f, 1f })
            {
                float x = 0.17f * s;
                carriage.Box(trGalv, new Vector3(x - 0.05f, 0.0f, 0.2f), new Vector3(x + 0.05f, 0.5f, 0.25f));
                carriage.Prism(trGalv, new[] { new Vector2(0.2f, 0f), new Vector2(1.36f, 0f), new Vector2(1.36f, 0.015f), new Vector2(1.18f, 0.045f), new Vector2(0.2f, 0.045f) }, x - 0.05f, x + 0.05f);
            }
        }

        private static HarborTruckMesh ForkWheelGeo(float r, float width, Material rimMat)
        {
            var w = new HarborTruckMesh { Tile = 0.4f };
            float h = width * 0.5f, rim = r * 0.58f;
            var prof = new List<Vector2> { new(-h + 0.01f, rim), new(-h, rim + 0.04f), new(-h, r - 0.04f), new(-h + 0.025f, r - 0.006f), new(-h + 0.05f, r) };
            for (float g = -h + 0.07f; g < h - 0.06f; g += 0.05f) { prof.Add(new Vector2(g, r)); prof.Add(new Vector2(g, r - 0.012f)); prof.Add(new Vector2(g + 0.018f, r - 0.012f)); prof.Add(new Vector2(g + 0.018f, r)); }
            prof.AddRange(new[] { new Vector2(h - 0.05f, r), new Vector2(h - 0.025f, r - 0.006f), new Vector2(h, r - 0.04f), new Vector2(h, rim + 0.04f), new Vector2(h - 0.01f, rim) });
            w.Revolve(trTyre, Vector3.zero, Vector3.right, prof.ToArray(), 28);
            w.Revolve(rimMat, Vector3.zero, Vector3.right, new[] { new Vector2(h - 0.005f, rim + 0.01f), new Vector2(h - 0.04f, rim - 0.01f), new Vector2(h * 0.3f, rim * 0.75f), new Vector2(h * 0.3f, rim * 0.35f), new Vector2(h * 0.45f, rim * 0.28f), new Vector2(h * 0.45f, 0f) }, 22);
            w.Revolve(trHub, Vector3.zero, Vector3.right, new[] { new Vector2(-h * 0.6f, 0f), new Vector2(-h * 0.6f, rim) }, 12);
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f;
                var p = new Vector3(h * 0.3f, Mathf.Cos(a) * rim * 0.55f, Mathf.Sin(a) * rim * 0.55f);
                w.Tube(trGalv, p, p + Vector3.right * 0.025f, 0.013f, 6);
            }
            return w;
        }

        /// <summary>A Euro pallet (1.2 × 0.8 m, fork pockets across) with a load: 0 wrapped cartons, 1 four drums, 2 a timber crate.</summary>
        private static HarborTruckMesh PalletUnitGeo(int kind)
        {
            var w = new HarborTruckMesh();
            var wood = palletWood;
            foreach (float x in new[] { -0.335f, 0f, 0.335f }) w.Box(wood, new Vector3(x - 0.05f, 0f, -0.6f), new Vector3(x + 0.05f, 0.022f, 0.6f));
            foreach (float x in new[] { -0.335f, 0f, 0.335f })
                foreach (float z in new[] { -0.545f, 0f, 0.545f })
                    w.Box(wood, new Vector3(x - 0.0725f, 0.022f, z - 0.055f), new Vector3(x + 0.0725f, 0.122f, z + 0.055f));
            foreach (float z in new[] { -0.545f, 0f, 0.545f }) w.Box(wood, new Vector3(-0.4f, 0.1f, z - 0.05f), new Vector3(0.4f, 0.122f, z + 0.05f));
            for (int k = 0; k < 5; k++) { float x = -0.35f + k * 0.175f; w.Box(wood, new Vector3(x - 0.05f, 0.122f, -0.6f), new Vector3(x + 0.05f, 0.144f, 0.6f)); }
            const float b = 0.144f;
            if (kind == 0)
            {
                // Cartons in three layers under stretch wrap (the wrap is a slightly larger glossy shell).
                for (int layer = 0; layer < 3; layer++)
                    for (int i = 0; i < 2; i++)
                        for (int j = 0; j < 3; j++)
                            w.Box(flCarton, new Vector3(-0.39f + i * 0.39f + 0.005f, b + layer * 0.29f + 0.003f, -0.59f + j * 0.393f + 0.005f),
                                            new Vector3(-0.39f + (i + 1) * 0.39f - 0.005f, b + (layer + 1) * 0.29f - 0.003f, -0.59f + (j + 1) * 0.393f - 0.005f));
                w.Box(flWrap, new Vector3(-0.395f, b + 0.3f, -0.595f), new Vector3(0.395f, b + 0.62f, 0.595f), false);   // a band of wrap; cartons show above and below
            }
            else if (kind == 1)
            {
                foreach (float x in new[] { -0.2f, 0.2f })
                    foreach (float z in new[] { -0.29f, 0.29f })
                        w.Revolve(flDrum, new Vector3(x, b, z), Vector3.up,
                                  new[] { new Vector2(0f, 0f), new Vector2(0f, 0.285f), new Vector2(0.03f, 0.29f), new Vector2(0.28f, 0.29f), new Vector2(0.3f, 0.285f), new Vector2(0.32f, 0.29f),
                                          new Vector2(0.56f, 0.29f), new Vector2(0.58f, 0.285f), new Vector2(0.6f, 0.29f), new Vector2(0.86f, 0.29f), new Vector2(0.88f, 0.28f), new Vector2(0.885f, 0f) }, 16);
            }
            else
            {
                w.Box(palletWood, new Vector3(-0.39f, b, -0.58f), new Vector3(0.39f, b + 0.82f, 0.58f));
                foreach (float y in new[] { b + 0.05f, b + 0.4f, b + 0.75f })
                    w.Box(trSteel, new Vector3(-0.395f, y, -0.585f), new Vector3(0.395f, y + 0.03f, 0.585f), false);
            }
            return w;
        }

        // ---------------- Prefabs ----------------

        private static void BuildForkliftPrefabs()
        {
            ForkliftMaterials();
            var body = new HarborTruckMesh(); ForkliftBodyGeo(body);
            var outer = new HarborTruckMesh(); var inner = new HarborTruckMesh(); var carr = new HarborTruckMesh();
            ForkliftMastGeo(outer, inner, carr);
            var front = ForkWheelGeo(FlFrontR, 0.26f, flPaint); var rear = ForkWheelGeo(FlRearR, 0.2f, flPaint);
            var bodyMesh = SaveTruckMesh(body, "HR_ForkliftBody", 3);
            var outerMesh = SaveTruckMesh(outer, "HR_ForkliftMast", 2);
            var innerMesh = SaveTruckMesh(inner, "HR_ForkliftInnerMast", 2);
            var carrMesh = SaveTruckMesh(carr, "HR_ForkliftCarriage", 2);
            var frontMesh = SaveTruckMesh(front, "HR_ForkliftWheelFront", 2);
            var rearMesh = SaveTruckMesh(rear, "HR_ForkliftWheelRear", 2);
            palletUnitMeshes = new Mesh[3]; palletUnitMats = new Material[3][];
            for (int k = 0; k < 3; k++) { var pu = PalletUnitGeo(k); palletUnitMeshes[k] = SaveTruckMesh(pu, $"HR_PalletUnit{k}", 2); palletUnitMats[k] = Mats(pu); }

            // ---- Working forklift.
            var root = new GameObject("PF_HR_Forklift") { layer = environment };
            Part("Body", root.transform, bodyMesh, Mats(body), environment, true);
            var mast = new GameObject("Mast") { layer = environment }.transform;
            mast.SetParent(root.transform, false);
            mast.localPosition = new Vector3(0f, 0f, FlMastZ);
            Part("Outer", mast, outerMesh, Mats(outer), environment, true);
            var innerT = new GameObject("InnerMast") { layer = environment }.transform;
            innerT.SetParent(mast, false);
            Part("Rails", innerT, innerMesh, Mats(inner), environment, true);
            var carriageT = new GameObject("Carriage") { layer = environment }.transform;
            carriageT.SetParent(mast, false);
            carriageT.localPosition = new Vector3(0f, HarborForklift.Travel, 0f);
            Part("Forks", carriageT, carrMesh, Mats(carr), environment, true);
            foreach (var (z, r, mesh, w, steer) in new[] { (FlFront, FlFrontR, frontMesh, front, false), (FlRear, FlRearR, rearMesh, rear, true) })
                foreach (float s in new[] { -1f, 1f })
                {
                    var pivot = new GameObject(steer ? "RearWheel" : "FrontWheel") { layer = environment }.transform;
                    pivot.SetParent(root.transform, false);
                    pivot.localPosition = new Vector3((steer ? 0.5f : 0.62f) * s, r, z);
                    var spinT = new GameObject("Spin") { layer = environment }.transform;
                    spinT.SetParent(pivot, false);
                    var rr = Part("Mesh", spinT, mesh, Mats(w), environment, true);
                    if (s < 0f) rr.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
            var beaconR = Part("Beacon", root.transform, beaconMesh, new[] { trPlastic, beacon }, detail, false);
            beaconR.transform.localPosition = new Vector3(0f, 2.23f, -0.55f);
            beaconR.transform.localScale = Vector3.one * 0.7f;
            var hl = new GameObject("WorkLight") { layer = detail };
            hl.transform.SetParent(root.transform, false);
            hl.transform.localPosition = new Vector3(0f, 2.1f, 0.85f);
            hl.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
            var l = hl.AddComponent<Light>();
            l.type = LightType.Spot; l.color = new Color(1f, 0.95f, 0.86f); l.intensity = 55f; l.range = 14f; l.spotAngle = 70f; l.innerSpotAngle = 35f; l.shadows = LightShadows.None;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.15f, -0.1f); col.size = new Vector3(1.3f, 2.3f, 2.8f);
            var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true;
            forkliftPrefab = PrefabUtility.SaveAsPrefabAsset(root, ForkliftPrefab);
            Object.DestroyImmediate(root);

            // ---- Parked forklift: everything merged (forks lowered), collider only.
            var merged = new HarborTruckMesh();
            merged.Append(body);
            merged.M = Matrix4x4.Translate(new Vector3(0f, 0f, FlMastZ)); merged.Append(outer); merged.Append(inner);
            merged.M = Matrix4x4.Translate(new Vector3(0f, 0.03f, FlMastZ)); merged.Append(carr);
            foreach (var (z, r, w, x) in new[] { (FlFront, FlFrontR, front, 0.62f), (FlRear, FlRearR, rear, 0.5f) })
                foreach (float s in new[] { -1f, 1f })
                {
                    merged.M = Matrix4x4.TRS(new Vector3(x * s, r, z), s < 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity, Vector3.one);
                    merged.Append(w);
                }
            merged.M = Matrix4x4.identity;
            var proot = new GameObject("PF_HR_ForkliftParked") { layer = environment };
            Part("Forklift", proot.transform, SaveTruckMesh(merged, "HR_ForkliftParked", 3), Mats(merged), environment, false);
            forkliftParked = PrefabUtility.SaveAsPrefabAsset(proot, ForkliftParkedPrefab);
            Object.DestroyImmediate(proot);
            report.AppendLine($"forklift: body {body.Triangles} + mast {outer.Triangles + inner.Triangles} + carriage {carr.Triangles} + wheels {2 * (front.Triangles + rear.Triangles)} tris; pallet units {palletUnitMeshes.Length}");
        }

        private static GameObject PalletUnit(Transform parent, int kind, Vector3 pos, Quaternion rot)
        {
            var r = Part("PalletUnit", parent, palletUnitMeshes[kind % 3], palletUnitMats[kind % 3], environment, true);
            r.transform.SetPositionAndRotation(pos, rot);
            return r.gameObject;
        }

        /// <summary>A parked forklift (static, merged) at <paramref name="p"/>.</summary>
        private static void Forklift(Vector3 p, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(forkliftParked, densityRoot);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            go.name = "ParkedForklift";
        }

        /// <summary>A forklift restacking two pallet units between stacks at <paramref name="a"/> and <paramref name="b"/> (HarborForklift).</summary>
        private static void WorkingForklift(Transform parent, string name, Vector3 a, Vector3 b, AudioClip beep)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(forkliftPrefab, parent);
            go.name = name;
            var dir = (b - a).normalized;
            go.transform.SetPositionAndRotation(a + dir * (HarborForklift.ForkReach + 6f), Quaternion.LookRotation(-dir));
            var t = go.transform;
            var spin = new List<Transform>(); var steer = new List<Transform>();
            foreach (Transform c in t)
            {
                if (c.name == "FrontWheel") spin.Add(c.Find("Spin"));
                else if (c.name == "RearWheel") { spin.Add(c.Find("Spin")); steer.Add(c); }
            }
            var loads = new[] { PalletUnit(parent, name.Length, a, Quaternion.LookRotation(dir)).transform, PalletUnit(parent, name.Length + 1, a + Vector3.up * HarborForklift.UnitHeight, Quaternion.LookRotation(dir)).transform };
            // Bases under both stacks: worn floor marks so the slots read as a working position.
            Begin();
            foreach (var p in new[] { a, b })
            {
                var rot = Quaternion.LookRotation(dir);
                foreach (float s in new[] { -1f, 1f })
                {
                    B(warning).OrientedBox(p + rot * new Vector3(0.55f * s, 0.012f, 0f), new Vector3(0.06f, 0.012f, 1.5f), rot, 1f);
                    B(warning).OrientedBox(p + rot * new Vector3(0f, 0.012f, 0.72f * s), new Vector3(1.16f, 0.012f, 0.06f), rot, 1f);
                }
            }
            // Loading-area floods: a pole beside each stack lights the work zone (shares the mast light budget).
            var side = Vector3.Cross(Vector3.up, dir);
            foreach (var p in new[] { a, b })
            {
                var pole = p + side * 4.6f;
                B(metal).Cylinder(pole, 0.12f, 7.2f, 8, true);
                B(metal).OrientedBox(pole + Vector3.up * 7.1f - side * 0.5f, new Vector3(0.12f, 0.12f, 1.1f), Quaternion.LookRotation(side), 1f);
                B(lampHead).OrientedBox(pole + Vector3.up * 7.0f - side * 1.0f, new Vector3(0.7f, 0.12f, 0.45f), Quaternion.LookRotation(dir), 1f);
                Collider(parent, "FloodPoleCollider", pole + Vector3.up * 2f, new Vector3(0.3f, 4f, 0.3f), environment);
                extraBudgetLights.Add(Downlight(parent, pole + Vector3.up * 6.9f - side * 1.0f, p - side * 0.5f + dir * (p == a ? 2.5f : -2.5f), 850f, 24f, 100f, new Color(1f, 0.93f, 0.82f)));
            }
            Flush(name + "_Slots", parent, detail, false);
            AudioSource beepSrc = null;
            if (beep != null) { beepSrc = Spatial(t, "ReverseBeep", beep, true, null, 0.35f, 4f, 40f); beepSrc.playOnAwake = false; }
            var fl = go.AddComponent<HarborForklift>();
            fl.EditorConfigure(t.Find("Mast/Carriage"), t.Find("Mast"), t.Find("Mast/InnerMast"), spin.ToArray(), steer.ToArray(), t.Find("Beacon").GetComponent<Renderer>(), a, b, loads, beepSrc);
        }
    }
}
