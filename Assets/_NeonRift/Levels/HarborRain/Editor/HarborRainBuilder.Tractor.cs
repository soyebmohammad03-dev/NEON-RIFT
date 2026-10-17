using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain 2.2 tractor unit: an original European cab-over 4×2 tractor (no third-party model — the shortlisted
    /// licensed download was unavailable), built to real proportions in the shared truck frame (+z forward, ground y 0,
    /// trailer kingpin at z 5.65): 3.6 m wheelbase, 2.49 m wide cab with rounded corners, a raked windscreen with rubber
    /// surround and parked wipers, sun visor with roof marker lamps, slatted grille, three-piece bumper with headlight,
    /// indicator and fog lamp clusters, corner steps, door shut lines and handles, side and sleeper windows, main / wide-
    /// angle / kerb mirrors on arms, aero side extenders, wheel-arch flares and flaps, ladder-frame chassis, leaf-sprung
    /// front axle, air-sprung drive axle with differential, fuel tank with straps, battery box, air tanks, exhaust stack,
    /// air intake snorkel, catwalk, fifth wheel, rear lamp bar and an interior silhouette (dash, wheel, seats) behind
    /// tinted glass. The painted shell is its own mesh so each truck can carry a different livery.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private const float FrontAxleZ = 8.65f, RearAxleZ = 5.05f, CabBack = 7.8f, CabFront = 10.05f, CabHW = 1.245f, CabFloor = 1.12f;
        private static Material tcGlass, tcGrille, tcChrome, tcBumper, tcInterior, tcHeadLens, tcDrl, tcBadge;
        private static Material[] tcPaints;

        private static void TractorMaterials()
        {
            tcPaints = new[]
            {
                Weathered("HR_CabPaintRed", new Color(0.46f, 0.06f, 0.045f), 0.15f, 0.45f, 0.72f, 21),
                Weathered("HR_CabPaintBlue", new Color(0.05f, 0.13f, 0.3f), 0.15f, 0.45f, 0.72f, 22),
                Weathered("HR_CabPaintWhite", new Color(0.72f, 0.72f, 0.7f), 0.2f, 0.25f, 0.7f, 23),
                Weathered("HR_CabPaintGraphite", new Color(0.15f, 0.165f, 0.18f), 0.1f, 0.55f, 0.72f, 24),
            };
            tcGlass = Lit("HR_CabGlass", new Color(0.02f, 0.028f, 0.034f, 0.62f), 0.96f, 0f, null);
            tcGlass.SetFloat("_Surface", 1f);
            tcGlass.SetFloat("_Blend", 0f);
            tcGlass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            tcGlass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            tcGlass.SetFloat("_ZWrite", 0f);
            tcGlass.SetFloat("_AlphaPremultiply", 1f);
            tcGlass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            tcGlass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            tcGlass.SetOverrideTag("RenderType", "Transparent");
            tcGlass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(tcGlass);
            tcGrille = Weathered("HR_CabGrille", new Color(0.05f, 0.052f, 0.056f), 0f, 0.6f, 0.6f, 25);
            tcChrome = Lit("HR_CabChrome", new Color(0.62f, 0.63f, 0.65f), 0.86f, 1f, null);
            tcBumper = Weathered("HR_CabBumper", new Color(0.12f, 0.125f, 0.13f), 0.05f, 0.1f, 0.56f, 26);
            tcInterior = Lit("HR_CabInterior", new Color(0.03f, 0.03f, 0.033f), 0.3f, 0f, null);
            tcHeadLens = Lit("HR_CabHeadLens", new Color(0.6f, 0.62f, 0.65f), 0.97f, 0f, new Color(2.6f, 2.5f, 2.2f));
            tcDrl = Lit("HR_CabDrl", new Color(0.6f, 0.62f, 0.65f), 0.95f, 0f, new Color(1.4f, 1.45f, 1.5f));
            tcBadge = Lit("HR_CabBadge", new Color(0.7f, 0.7f, 0.72f), 0.85f, 1f, null);
        }

        /// <summary>Front-face z at height <paramref name="y"/>: vertical up to the screen base, then raked back.</summary>
        private static float CabFrontZ(float y) => y <= 2.02f ? CabFront : CabFront - (y - 2.02f) * 0.15f;

        /// <summary>The tractor: painted shell into <paramref name="paint"/>, glass into <paramref name="glass"/>, the rest into <paramref name="w"/>.</summary>
        private static void TractorGeo(HarborTruckMesh w, HarborTruckMesh paint, HarborTruckMesh glass, HarborTruckMesh brake)
        {
            var steel = trSteel; var galv = trGalv; var plastic = trPlastic;
            Material shell = tcPaints[0];

            // ---- Cab shell: horizontal rounded-rect slices from the floor to a rounded roof, raked above the screen line.
            var rings = new List<Vector3[]>();
            foreach (float y in new[] { CabFloor, 1.35f, 1.7f, 2.02f, 2.4f, 2.8f, 3.1f, 3.32f, 3.44f, 3.52f, 3.57f })
            {
                float inset = y <= 3.32f ? 0f : y switch { <= 3.44f => 0.03f, <= 3.52f => 0.09f, _ => 0.19f };
                float zf = CabFrontZ(Mathf.Min(y, 3.1f)) - (y > 3.1f ? (y - 3.1f) * 0.25f : 0f) - inset;
                rings.Add(HarborTruckMesh.RoundRectXZ(y, CabHW - inset, CabBack + inset, zf, 0.08f + inset, 0.24f + inset * 0.5f, 4));
            }
            paint.Loft(shell, rings, false, true);
            // Underside closing panel (seen from low cameras).
            w.Box(plastic, new Vector3(-CabHW + 0.05f, CabFloor - 0.04f, CabBack + 0.05f), new Vector3(CabHW - 0.05f, CabFloor, CabFront - 0.05f));
            // Aero side extenders behind the doors and a roof deflector lip.
            foreach (float s in new[] { -1f, 1f })
                w.Box(shell, new Vector3(s > 0 ? CabHW - 0.02f : -CabHW, 1.35f, CabBack - 0.32f), new Vector3(s > 0 ? CabHW : -CabHW + 0.02f, 3.35f, CabBack + 0.05f));

            // ---- Front: grille with slats, badge bar, lower panel.
            const float gz = CabFront + 0.004f;
            w.Box(tcGrille, new Vector3(-0.88f, 1.22f, CabFront - 0.04f), new Vector3(0.88f, 1.98f, gz));
            for (float y = 1.27f; y < 1.95f; y += 0.09f)
                w.Box(tcBumper, new Vector3(-0.85f, y, gz), new Vector3(0.85f, y + 0.035f, gz + 0.03f), false);
            w.Box(tcChrome, new Vector3(-0.9f, 1.97f, gz), new Vector3(0.9f, 2.0f, gz + 0.025f), false);
            w.Box(tcChrome, new Vector3(-0.9f, 1.2f, gz), new Vector3(0.9f, 1.23f, gz + 0.025f), false);
            w.Box(tcBadge, new Vector3(-0.22f, 1.68f, gz + 0.03f), new Vector3(0.22f, 1.78f, gz + 0.045f), false);
            foreach (float s in new[] { -1f, 1f })
                w.Box(tcGrille, new Vector3(s > 0 ? 0.9f : -1.12f, 1.25f, gz - 0.01f), new Vector3(s > 0 ? 1.12f : -0.9f, 1.36f, gz + 0.01f), false);   // side intake slots

            // ---- Bumper: rounded section across the front, wrapped corners, lamp clusters, plate, step treads.
            w.Prism(tcBumper, new[] { new Vector2(CabFront - 0.1f, 0.42f), new Vector2(CabFront + 0.1f, 0.42f), new Vector2(CabFront + 0.16f, 0.52f),
                                      new Vector2(CabFront + 0.16f, 1.0f), new Vector2(CabFront + 0.1f, 1.1f), new Vector2(CabFront - 0.1f, 1.1f) }, -1.1f, 1.1f);
            foreach (float s in new[] { -1f, 1f })
            {
                w.OBox(tcBumper, new Vector3(1.16f * s, 0.76f, CabFront + 0.02f), new Vector3(0.18f, 0.68f, 0.24f), Quaternion.Euler(0f, 35f * s, 0f));
                float c = 0.86f * s;
                w.Box(plastic, new Vector3(c - 0.25f, 0.76f, CabFront + 0.155f), new Vector3(c + 0.25f, 0.98f, CabFront + 0.165f), false);
                w.Tube(tcHeadLens, new Vector3(c - 0.09f * s, 0.87f, CabFront + 0.16f), new Vector3(c - 0.09f * s, 0.87f, CabFront + 0.185f), 0.075f, 14);
                w.Tube(tcHeadLens, new Vector3(c + 0.08f * s, 0.87f, CabFront + 0.16f), new Vector3(c + 0.08f * s, 0.87f, CabFront + 0.185f), 0.06f, 14);
                w.Box(trLensAmber, new Vector3(Mathf.Min(c + 0.17f * s, c + 0.24f * s), 0.8f, CabFront + 0.16f), new Vector3(Mathf.Max(c + 0.17f * s, c + 0.24f * s), 0.95f, CabFront + 0.175f), false);
                w.Box(tcDrl, new Vector3(Mathf.Min(c - 0.22f * s, c + 0.15f * s), 0.775f, CabFront + 0.162f), new Vector3(Mathf.Max(c - 0.22f * s, c + 0.15f * s), 0.79f, CabFront + 0.175f), false);
                w.Tube(trLensWhite, new Vector3(0.78f * s, 0.56f, CabFront + 0.15f), new Vector3(0.78f * s, 0.56f, CabFront + 0.172f), 0.055f, 12);
                // Corner steps (two treads) ahead of the front wheel, under the door.
                float x0 = s > 0 ? 0.9f : -1.24f, x1 = s > 0 ? 1.24f : -0.9f;
                w.Box(plastic, new Vector3(x0, 0.4f, FrontAxleZ + 0.6f), new Vector3(x1, 1.12f, FrontAxleZ + 1.3f));
                foreach (float y in new[] { 0.52f, 0.84f })
                    w.Box(galv, new Vector3(s > 0 ? 1.0f : -1.25f, y, FrontAxleZ + 0.66f), new Vector3(s > 0 ? 1.25f : -1.0f, y + 0.035f, FrontAxleZ + 1.24f), false);
            }
            w.Box(trPlate, new Vector3(-0.26f, 0.5f, CabFront + 0.162f), new Vector3(0.26f, 0.61f, CabFront + 0.168f), false);
            w.Box(trTapeWhite, new Vector3(-0.12f, 0.43f, CabFront + 0.1f), new Vector3(0.12f, 0.47f, CabFront + 0.14f), false);

            // ---- Windscreen (raked plane), rubber surround, wipers, sun visor, roof marker lamps, kerb mirror.
            Vector3 S(float x, float y, float off = 0.008f) => new(x, y, CabFrontZ(y) + off);
            var rake = new Vector3(0f, 0.15f, 1f).normalized;
            glass.Quad(tcGlass, S(-1.03f, 2.1f), S(1.03f, 2.1f), S(1.03f, 3.0f), S(-1.03f, 3.0f), rake);
            w.Quad(rubber, S(-1.08f, 2.05f, 0.006f), S(1.08f, 2.05f, 0.006f), S(1.08f, 2.1f, 0.006f), S(-1.08f, 2.1f, 0.006f), rake);
            w.Quad(rubber, S(-1.08f, 3.0f, 0.006f), S(1.08f, 3.0f, 0.006f), S(1.08f, 3.05f, 0.006f), S(-1.08f, 3.05f, 0.006f), rake);
            foreach (float s in new[] { -1f, 1f })
                w.Quad(rubber, S(1.03f * s, 2.05f, 0.006f), S(1.08f * s, 2.05f, 0.006f), S(1.08f * s, 3.05f, 0.006f), S(1.03f * s, 3.05f, 0.006f), rake);
            foreach (float x in new[] { -0.55f, 0.35f })
            {
                w.Beam(plastic, S(x, 2.13f, 0.03f), S(x + 0.72f, 2.17f, 0.03f), new Vector2(0.025f, 0.02f));
                w.Beam(plastic, S(x + 0.05f, 2.06f, 0.03f), S(x + 0.32f, 2.15f, 0.04f), new Vector2(0.02f, 0.02f));
            }
            w.Prism(plastic, new[] { new Vector2(CabFrontZ(3.08f), 3.06f), new Vector2(CabFrontZ(3.08f) + 0.24f, 3.07f), new Vector2(CabFrontZ(3.08f) + 0.24f, 3.11f), new Vector2(CabFrontZ(3.08f), 3.18f) }, -1.16f, 1.16f);
            for (int k = 0; k < 5; k++)
            {
                float x = -0.8f + k * 0.4f;
                w.Box(trMarker, new Vector3(x - 0.045f, 3.11f, CabFrontZ(3.08f) + 0.16f), new Vector3(x + 0.045f, 3.14f, CabFrontZ(3.08f) + 0.22f));
            }
            w.Tube(plastic, new Vector3(0.9f, 3.3f, CabFrontZ(3.3f) - 0.05f), new Vector3(0.9f, 3.22f, CabFrontZ(3.3f) + 0.22f), 0.015f, 6);
            w.OBox(plastic, new Vector3(0.9f, 3.16f, CabFrontZ(3.3f) + 0.26f), new Vector3(0.3f, 0.2f, 0.06f), Quaternion.Euler(25f, 0f, 0f));

            // ---- Sides: windows, door shut lines, handles, mirrors, cab-back details.
            foreach (float s in new[] { -1f, 1f })
            {
                float xs = CabHW * s, xo = xs + 0.006f * s;
                var side = Vector3.right * s;
                // Door glass follows the rake at its front edge.
                glass.Quad(tcGlass, new Vector3(xo, 2.14f, 8.7f), new Vector3(xo, 2.14f, CabFrontZ(2.14f) - 0.32f), new Vector3(xo, 2.86f, CabFrontZ(2.86f) - 0.3f), new Vector3(xo, 2.86f, 8.7f), side);
                glass.Quad(tcGlass, new Vector3(xo, 2.3f, 8.0f), new Vector3(xo, 2.3f, 8.5f), new Vector3(xo, 2.76f, 8.5f), new Vector3(xo, 2.76f, 8.0f), side);
                float xr = xs + 0.004f * s;
                void Line(Vector3 a, Vector3 b) => w.Quad(rubber, a + Vector3.up * 0.006f, b + Vector3.up * 0.006f, b - Vector3.up * 0.006f, a - Vector3.up * 0.006f, side);
                void VLine(float z, float y0, float y1) => w.Quad(rubber, new Vector3(xr, y0, z - 0.006f), new Vector3(xr, y0, z + 0.006f), new Vector3(xr, y1, z + 0.006f), new Vector3(xr, y1, z - 0.006f), side);
                VLine(8.6f, 1.16f, 2.95f);
                VLine(CabFrontZ(1.5f) - 0.26f, 1.16f, 2.1f);
                Line(new Vector3(xr, 2.95f, 8.6f), new Vector3(xr, 2.95f, CabFrontZ(2.95f) - 0.26f));
                Line(new Vector3(xr, 1.16f, 8.6f), new Vector3(xr, 1.16f, CabFrontZ(1.16f) - 0.26f));
                foreach (float y in new[] { 2.1f, 2.9f }) Line(new Vector3(xr, y, 8.66f), new Vector3(xr, y, CabFrontZ(y) - 0.3f));
                w.Box(tcChrome, new Vector3(s > 0 ? xs : xs - 0.03f, 1.98f, 8.72f), new Vector3(s > 0 ? xs + 0.03f : xs, 2.02f, 8.92f), false);
                w.Box(trMarker, new Vector3(s > 0 ? xs : xs - 0.015f, 1.3f, 9.3f), new Vector3(s > 0 ? xs + 0.015f : xs, 1.36f, 9.45f), false);   // side repeater
                // Mirrors: arm from the A-pillar, main + wide-angle heads (dark housings, glass faces rearward).
                var arm0 = new Vector3(xs + 0.02f * s, 2.72f, CabFrontZ(2.72f) - 0.18f);
                var arm1 = new Vector3(xs + 0.3f * s, 2.82f, CabFrontZ(2.72f) - 0.12f);
                w.Hose(plastic, new[] { arm0, arm0 + new Vector3(0.12f * s, 0.12f, 0f), arm1 }, 0.022f, 6);
                w.Tube(plastic, new Vector3(xs + 0.02f * s, 1.78f, CabFrontZ(1.78f) - 0.15f), new Vector3(xs + 0.32f * s, 1.95f, CabFrontZ(1.78f) - 0.1f), 0.02f, 6);
                var mc = new Vector3(xs + 0.36f * s, 2.42f, CabFrontZ(2.72f) - 0.1f);
                w.OBox(plastic, mc, new Vector3(0.08f, 0.66f, 0.24f), Quaternion.Euler(0f, 12f * s, 0f));
                w.OBox(tcChrome, mc + new Vector3(0.0f, 0f, -0.125f), new Vector3(0.06f, 0.6f, 0.01f), Quaternion.Euler(0f, 12f * s, 0f));
                var wc = new Vector3(xs + 0.36f * s, 1.92f, CabFrontZ(1.9f) - 0.08f);
                w.OBox(plastic, wc, new Vector3(0.08f, 0.26f, 0.22f), Quaternion.Euler(0f, 12f * s, 0f));
                w.OBox(tcChrome, wc + new Vector3(0f, 0f, -0.115f), new Vector3(0.06f, 0.22f, 0.01f), Quaternion.Euler(0f, 12f * s, 0f));
                // Grab handles beside the door.
                w.Tube(tcChrome, new Vector3(xs + 0.03f * s, 1.3f, 8.52f), new Vector3(xs + 0.03f * s, 2.3f, 8.52f), 0.017f, 6);
            }

            // ---- Interior silhouette behind the glass (left-hand drive).
            w.Box(tcInterior, new Vector3(-1.15f, 1.85f, 9.55f), new Vector3(1.15f, 2.15f, 9.95f));
            w.Box(tcInterior, new Vector3(-1.2f, CabFloor, CabBack + 0.08f), new Vector3(1.2f, 3.3f, CabBack + 0.12f));
            foreach (float x in new[] { -0.62f, 0.62f })
            {
                w.Box(tcInterior, new Vector3(x - 0.26f, 1.5f, 8.45f), new Vector3(x + 0.26f, 1.65f, 8.95f));
                w.OBox(tcInterior, new Vector3(x, 2.05f, 8.45f), new Vector3(0.5f, 0.75f, 0.12f), Quaternion.Euler(-12f, 0f, 0f));
            }
            var wheelC = new Vector3(-0.62f, 2.15f, 9.35f);
            var ringPts = new List<Vector3>();
            for (int k = 0; k <= 16; k++) { float a = k / 16f * Mathf.PI * 2f; ringPts.Add(wheelC + Quaternion.Euler(-55f, 0f, 0f) * new Vector3(Mathf.Cos(a) * 0.22f, 0f, Mathf.Sin(a) * 0.22f)); }
            w.Hose(tcInterior, ringPts, 0.02f, 5);
            w.Tube(tcInterior, wheelC, wheelC + new Vector3(0f, -0.3f, 0.3f), 0.035f, 6);

            // ---- Chassis: ladder frame, cross-members, front axle on leaf springs, drive axle with diff and air springs.
            foreach (float s in new[] { -1f, 1f })
            {
                float x = 0.39f * s;
                w.Box(steel, new Vector3(x - 0.045f, 0.78f, 4.25f), new Vector3(x + 0.045f, 1.05f, CabFront - 0.05f));
                for (int k = 0; k < 4; k++) w.Box(steel, new Vector3(x - 0.06f, 0.56f + k * 0.03f, FrontAxleZ - 0.65f + k * 0.12f), new Vector3(x + 0.06f, 0.59f + k * 0.03f, FrontAxleZ + 0.65f - k * 0.12f));
                w.Box(steel, new Vector3(x - 0.05f, 0.56f, FrontAxleZ - 0.74f), new Vector3(x + 0.05f, 0.8f, FrontAxleZ - 0.62f));
                w.Box(steel, new Vector3(x - 0.05f, 0.56f, FrontAxleZ + 0.62f), new Vector3(x + 0.05f, 0.8f, FrontAxleZ + 0.74f));
                // Drive axle air springs + trailing arms + dampers.
                w.Beam(steel, new Vector3(x, 0.75f, RearAxleZ + 0.75f), new Vector3(x, 0.5f, RearAxleZ - 0.35f), new Vector2(0.09f, 0.08f));
                w.Revolve(plastic, new Vector3(x, 0.62f, RearAxleZ - 0.38f), Vector3.up,
                          new[] { new Vector2(0f, 0.11f), new Vector2(0.07f, 0.15f), new Vector2(0.14f, 0.12f), new Vector2(0.2f, 0.14f), new Vector2(0.24f, 0.11f), new Vector2(0.26f, 0f) }, 12);
                w.Tube(plastic, new Vector3(x - 0.08f * s, 0.6f, RearAxleZ + 0.2f), new Vector3(x - 0.06f * s, 0.98f, RearAxleZ + 0.45f), 0.035f, 8);
            }
            foreach (float z in new[] { 4.3f, 5.7f, 6.9f, 7.9f, 9.2f, 9.95f })
                w.Box(steel, new Vector3(-0.35f, 0.82f, z - 0.05f), new Vector3(0.35f, 1.0f, z + 0.05f));
            w.Box(steel, new Vector3(-0.85f, 0.43f, FrontAxleZ - 0.06f), new Vector3(0.85f, 0.56f, FrontAxleZ + 0.06f));                    // I-beam axle
            w.Box(galv, new Vector3(-0.3f, 0.75f, FrontAxleZ + 0.75f), new Vector3(-0.1f, 0.98f, FrontAxleZ + 1.0f));                       // steering box
            w.Tube(steel, new Vector3(-0.85f, 0.5f, FrontAxleZ + 0.18f), new Vector3(0.85f, 0.5f, FrontAxleZ + 0.18f), 0.025f, 6);           // track rod
            w.Tube(steel, new Vector3(-0.85f, 0.53f, RearAxleZ), new Vector3(0.85f, 0.53f, RearAxleZ), 0.075f, 10);
            w.Revolve(steel, new Vector3(0f, 0.53f, RearAxleZ - 0.05f), Vector3.forward,
                      new[] { new Vector2(-0.22f, 0f), new Vector2(-0.18f, 0.16f), new Vector2(0.0f, 0.24f), new Vector2(0.18f, 0.18f), new Vector2(0.32f, 0.08f), new Vector2(0.45f, 0.06f), new Vector2(0.5f, 0f) }, 14);
            w.Tube(steel, new Vector3(0f, 0.6f, RearAxleZ + 0.45f), new Vector3(0f, 0.72f, FrontAxleZ - 0.9f), 0.05f, 8);                    // prop shaft
            foreach (float s in new[] { -1f, 1f })
            {
                w.Tube(trHub, new Vector3(0.72f * s, 0.53f, FrontAxleZ), new Vector3(0.88f * s, 0.53f, FrontAxleZ), 0.2f, 14);
                w.Tube(trHub, new Vector3(0.5f * s, 0.53f, RearAxleZ), new Vector3(0.6f * s, 0.53f, RearAxleZ), 0.2f, 14);
            }

            // ---- Wheel-arch flares and flaps (front), rear mudguards with lamp bar.
            foreach (float s in new[] { -1f, 1f })
            {
                w.Revolve(plastic, new Vector3(1.08f * s, 0.53f, FrontAxleZ), Vector3.right,
                          new[] { new Vector2(-0.23f, 0.6f), new Vector2(-0.23f, 0.64f), new Vector2(0.21f, 0.64f), new Vector2(0.24f, 0.6f), new Vector2(-0.23f, 0.6f) }, 12, 150f, 195f);
                w.Box(rubber, new Vector3(s > 0 ? 0.88f : -1.27f, 0.25f, FrontAxleZ - 0.66f), new Vector3(s > 0 ? 1.27f : -0.88f, 0.95f, FrontAxleZ - 0.65f));
                float x0 = s > 0 ? 0.55f : -1.3f, x1 = s > 0 ? 1.3f : -0.55f;
                w.Box(plastic, new Vector3(x0, 1.1f, RearAxleZ - 0.62f), new Vector3(x1, 1.13f, RearAxleZ + 0.62f));
                w.Box(plastic, new Vector3(x0, 0.9f, RearAxleZ + 0.6f), new Vector3(x1, 1.13f, RearAxleZ + 0.63f));
                w.Box(rubber, new Vector3(x0 + 0.02f, 0.25f, RearAxleZ - 0.66f), new Vector3(x1 - 0.02f, 1.1f, RearAxleZ - 0.65f));
                w.Beam(galv, new Vector3(0.43f * s, 1.0f, RearAxleZ - 0.4f), new Vector3(0.8f * s, 1.1f, RearAxleZ - 0.4f), new Vector2(0.04f, 0.04f));
            }
            w.Box(steel, new Vector3(-1.1f, 0.78f, 4.22f), new Vector3(1.1f, 0.84f, 4.3f));
            foreach (float s in new[] { -1f, 1f })
            {
                w.Box(plastic, new Vector3(0.75f * s - 0.18f, 0.84f, 4.2f), new Vector3(0.75f * s + 0.18f, 0.98f, 4.28f));
                brake.Box(tailLamp, new Vector3(0.75f * s - 0.06f, 0.86f, 4.19f), new Vector3(0.75f * s + 0.08f, 0.96f, 4.2f), false);
                w.Box(trLensAmber, new Vector3(0.75f * s + (s > 0 ? 0.09f : -0.16f), 0.86f, 4.19f), new Vector3(0.75f * s + (s > 0 ? 0.16f : -0.09f), 0.96f, 4.2f), false);
            }

            // ---- Tanks and equipment: fuel tank (right), battery box + air tanks (left), exhaust stack, intake, catwalk.
            w.Revolve(galv, new Vector3(0.82f, 0.72f, 6.1f), Vector3.forward,
                      new[] { new Vector2(0f, 0f), new Vector2(0.01f, 0.25f), new Vector2(0.05f, 0.3f), new Vector2(1.5f, 0.3f), new Vector2(1.54f, 0.25f), new Vector2(1.55f, 0f) }, 18);
            foreach (float z in new[] { 6.35f, 7.35f }) w.Revolve(trPlastic, new Vector3(0.82f, 0.72f, z), Vector3.forward, new[] { new Vector2(0f, 0.305f), new Vector2(0.05f, 0.305f) }, 18);
            w.Box(plastic, new Vector3(0.75f, 0.8f, 7.75f), new Vector3(0.9f, 0.9f, 7.85f));
            w.Box(plastic, new Vector3(-1.15f, 0.45f, 6.4f), new Vector3(-0.48f, 1.0f, 7.5f));
            w.Box(galv, new Vector3(-1.16f, 0.46f, 6.42f), new Vector3(-1.15f, 0.98f, 7.48f), false);
            w.Revolve(steel, new Vector3(-0.72f, 0.66f, 5.75f), Vector3.forward, new[] { new Vector2(0f, 0f), new Vector2(0.04f, 0.13f), new Vector2(0.56f, 0.13f), new Vector2(0.6f, 0f) }, 12);
            w.Tube(tcChrome, new Vector3(1.0f, 1.0f, CabBack - 0.2f), new Vector3(1.0f, 3.95f, CabBack - 0.2f), 0.085f, 14);
            w.Tube(galv, new Vector3(1.0f, 1.9f, CabBack - 0.2f), new Vector3(1.0f, 3.1f, CabBack - 0.2f), 0.105f, 14, false);
            w.Tube(plastic, new Vector3(1.0f, 3.95f, CabBack - 0.2f), new Vector3(1.0f, 4.0f, CabBack - 0.14f), 0.085f, 14);
            w.Tube(plastic, new Vector3(-1.0f, 1.15f, CabBack - 0.22f), new Vector3(-1.0f, 3.25f, CabBack - 0.22f), 0.15f, 14);
            w.Revolve(plastic, new Vector3(-1.0f, 3.25f, CabBack - 0.22f), Vector3.up, new[] { new Vector2(0f, 0.15f), new Vector2(0.06f, 0.2f), new Vector2(0.16f, 0.2f), new Vector2(0.2f, 0f) }, 14);
            w.Box(galv, new Vector3(-0.42f, 1.06f, 6.95f), new Vector3(0.42f, 1.08f, CabBack - 0.05f));
            foreach (float z in new[] { 7.05f, 7.3f, 7.55f }) w.Box(galv, new Vector3(-0.42f, 1.08f, z), new Vector3(0.42f, 1.09f, z + 0.02f), false);
            w.Box(plastic, new Vector3(-0.5f, 1.08f, CabBack - 0.1f), new Vector3(0.5f, 2.0f, CabBack - 0.02f));                       // hose / cable rack
            // Trailer lines from the rack, sagging to the trailer couplings.
            Vector3 Sag(Vector3 a, Vector3 b, float t, float sag) => Vector3.Lerp(a, b, t) + Vector3.down * Mathf.Sin(t * Mathf.PI) * sag;
            foreach (var (m, x0, x1) in new[] { (trHoseRed, -0.27f, -0.32f), (trHoseYellow, -0.12f, -0.12f), (trPlastic, 0.15f, 0.15f) })
            {
                var a = new Vector3(x0, 1.85f, CabBack - 0.12f); var b = new Vector3(x1, 1.3f, TrailerFront + 0.06f);
                var pts = new List<Vector3>();
                for (int k = 0; k <= 12; k++) pts.Add(Sag(a, b, k / 12f, 0.25f) + new Vector3(Mathf.Sin(k * 1.9f) * 0.03f, 0f, 0f));
                w.Hose(m, pts, 0.016f, 6);
            }

            // ---- Fifth wheel: mounting plate, top plate split by the kingpin slot, release handle.
            w.Box(steel, new Vector3(-0.5f, 1.05f, 5.15f), new Vector3(0.5f, 1.1f, 6.2f));
            w.Box(trHub, new Vector3(-0.44f, 1.1f, 5.62f), new Vector3(0.44f, 1.15f, 6.12f));
            foreach (float s in new[] { -1f, 1f })
                w.Box(trHub, new Vector3(s > 0 ? 0.07f : -0.44f, 1.1f, 5.2f), new Vector3(s > 0 ? 0.44f : -0.07f, 1.15f, 5.62f));
            w.Tube(trHoseRed, new Vector3(0.44f, 1.12f, 5.75f), new Vector3(0.62f, 1.12f, 5.75f), 0.015f, 6);
        }

        /// <summary>Front-axle wheel (same super-single as the trailer): radius, track.</summary>
        private const float TractorWheelR = 0.535f;
        private static readonly (float z, float x, bool steer, bool inner)[] TractorWheels =
        {
            (FrontAxleZ, 1.07f, true, false),
            (RearAxleZ, 1.07f, false, false),
            (RearAxleZ, 0.7f, false, true),
        };
    }
}
