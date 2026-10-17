using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// The terminal gate approach (Harbor Rain 2.1): a restricted port entrance that reads from the chase camera.
    /// An overhead gantry at the real sign height (board 5.6–8 m, under the chase camera's upper frame) carries a
    /// reflective green two-line sign lit by its own downlights, lane-control signals over each lane and a CCTV camera;
    /// the inspection canopy over the lanes is lit underneath (lights in the light budget) with booths, amber warning
    /// beacons and lane fascia signs; the road gets a trench drain, rumble bars, painted lane arrows and a speed sign.
    /// Pool decals under every fixture give the wet ground its reflected glow without extra real lights.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        /// <summary>A road-sign board: white letters on a reflective green (or any) ground, lit slightly from within.</summary>
        private static Material SignBoardMat(string key, string text, Color ink, Color ground)
        {
            int tw = Mathf.Max(1, NeonRift.EditorTools.District.PixelFont.Measure(text)), w = tw + 8, h = NeonRift.EditorTools.District.PixelFont.Height + 6;
            var tex = Texture("HR_Board_" + key, w, h, (x, y) =>
            {
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) return ink * 0.9f;   // border line
                int gx = x - 4, gy = (h - 1 - y) - 3;
                if (gx < 0 || gy < 0 || gx >= tw || gy >= NeonRift.EditorTools.District.PixelFont.Height) return ground;
                int ci = gx / (NeonRift.EditorTools.District.PixelFont.Width + 1), cx = gx % (NeonRift.EditorTools.District.PixelFont.Width + 1);
                return cx < NeonRift.EditorTools.District.PixelFont.Width && ci < text.Length && NeonRift.EditorTools.District.PixelFont.Pixel(text[ci], cx, gy) ? ink : ground;
            }, true);
            var m = Lit("HR_Board_" + key, Color.white, 0.45f, 0f, Color.white * 0.55f);   // retroreflective: a little self-light, the rest from the downlights
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_EmissionMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>One sign board (both faces), letters <paramref name="letter"/> m tall, centred at <paramref name="centre"/>.</summary>
        private static Vector2 SignBoard(Transform root, string key, string text, Color ink, Color ground, Vector3 centre, float yaw, float letter)
        {
            var mat = SignBoardMat(key, text, ink, ground);
            var tex = mat.GetTexture("_BaseMap");
            float px = letter / NeonRift.EditorTools.District.PixelFont.Height;
            float width = tex.width * px, height = tex.height * px;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            B(mat).Panel(centre + rot * new Vector3(0f, 0f, 0.05f), rot, width, height, new Rect(0f, 0f, 1f, 1f));
            B(mat).Panel(centre + rot * new Vector3(0f, 0f, -0.05f), rot * Quaternion.Euler(0f, 180f, 0f), width, height, new Rect(0f, 0f, 1f, 1f));
            B(metal).OrientedBox(centre, new Vector3(width + 0.2f, height + 0.2f, 0.08f), rot, 1f);
            return new Vector2(width, height);
        }

        private static Light Downlight(Transform root, Vector3 at, Vector3 target, float intensity, float range, float angle, Color colour)
        {
            var go = new GameObject("Downlight") { layer = detail };
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(target - at));
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.color = colour; l.intensity = intensity; l.range = range; l.spotAngle = angle; l.innerSpotAngle = angle * 0.5f; l.shadows = LightShadows.None;
            return l;
        }

        private static void BuildGateApproach(Transform world, Random rng)
        {
            var root = new GameObject("TerminalGate").transform;
            root.SetParent(world, false);
            var green = new Color(0.02f, 0.22f, 0.11f);
            var amberLamp = Lit("HR_GateAmber", new Color(0.12f, 0.06f, 0f), 0.6f, 0f, new Color(0.05f, 0.02f, 0f));

            // ---- Sign gantry 70 m before the gate: posts, truss, the board (5.6–8 m up), lane signals, CCTV.
            const float gz = -400f;
            Begin();
            foreach (float x in new[] { -12f, 12f })
            {
                B(metal).Cuboid(new Vector3(x - 0.35f, 0f, gz - 0.35f), new Vector3(x + 0.35f, 9.2f, gz + 0.35f), 2f);
                B(concreteDark).Cuboid(new Vector3(x - 0.9f, 0f, gz - 0.9f), new Vector3(x + 0.9f, 0.6f, gz + 0.9f), 1f);
                B(hazardStripe).Cuboid(new Vector3(x - 0.37f, 0.6f, gz - 0.37f), new Vector3(x + 0.37f, 2f, gz + 0.37f), 1f);
            }
            foreach (float y in new[] { 8.2f, 9.0f })
                B(metal).Cuboid(new Vector3(-12.3f, y, gz - 0.25f), new Vector3(12.3f, y + 0.25f, gz + 0.25f), 3f);
            for (float x = -11.5f; x < 11.5f; x += 1.5f)
                B(metal).OrientedBox(new Vector3(x + 0.75f, 8.7f, gz), new Vector3(0.08f, 0.08f, 1.15f), Quaternion.Euler(0f, 0f, 45f) * Quaternion.Euler(0f, 90f, 0f), 1f);
            var top = SignBoard(root, "GateTop", "KOWLOON BAY TERMINAL", Color.white, green, new Vector3(-1f, 7.35f, gz - 0.4f), 0f, 0.62f);
            SignBoard(root, "GateLine2", "GATE 3 · ALL TRUCKS STOP", new Color(1f, 0.85f, 0.3f), green, new Vector3(-1f, 6.15f, gz - 0.4f), 0f, 0.5f);
            // Lane-control signals over each lane (green arrow in, red cross out) and a CCTV camera on the east post.
            SignBoard(root, "LaneIn", "↓", new Color(0.3f, 1f, 0.45f), new Color(0.01f, 0.01f, 0.012f), new Vector3(-4.5f, 8.0f + 0.5f, gz - 0.45f), 0f, 0.7f);
            SignBoard(root, "LaneOut", "X", new Color(1f, 0.25f, 0.2f), new Color(0.01f, 0.01f, 0.012f), new Vector3(4.5f, 8.0f + 0.5f, gz - 0.45f), 0f, 0.7f);
            B(pool).Decal(new Vector3(0f, 0.06f, gz - 3f), 22f, 12f);
            Flush("SignGantry", root, environment, true);
            Collider(root, "GantryPostW", new Vector3(-12f, 4.6f, gz), new Vector3(0.8f, 9.2f, 0.8f), environment);
            Collider(root, "GantryPostE", new Vector3(12f, 4.6f, gz), new Vector3(0.8f, 9.2f, 0.8f), environment);
            Prop(root, "security_camera_02", new Vector3(11.6f, 9.3f, gz - 0.4f), 200f, 1.4f);
            // The board's own downlights (on the truss, aimed back at the face the drivers see).
            foreach (float x in new[] { -6.5f, 4.5f })
                Downlight(root, new Vector3(x, 9.6f, gz - 3.2f), new Vector3(x, 6.6f, gz - 0.4f), 260f, 11f, 95f, new Color(1f, 0.97f, 0.9f));   // even wash, no hotspot

            // ---- Inspection canopy over the gate lanes: columns, roof, lit underside, booths, beacons, fascia signs.
            const float c0 = -372f, c1 = -352f;
            Begin();
            foreach (float x in new[] { -11f, 11f })
                foreach (float z in new[] { c0 + 1f, c1 - 1f })
                {
                    B(concreteDark).Cuboid(new Vector3(x - 0.45f, 0f, z - 0.45f), new Vector3(x + 0.45f, 6.6f, z + 0.45f), 2f);
                    B(hazardStripe).Cuboid(new Vector3(x - 0.47f, 0f, z - 0.47f), new Vector3(x + 0.47f, 1.6f, z + 0.47f), 1f);
                }
            B(claddingWhite).Cuboid(new Vector3(-13f, 6.6f, c0), new Vector3(13f, 7.8f, c1), 2f);
            B(roof).Cuboid(new Vector3(-13.2f, 7.8f, c0 - 0.2f), new Vector3(13.2f, 8.1f, c1 + 0.2f), 4f);
            foreach (float x in new[] { -6f, -2f, 2f, 6f }) B(workLight).Cuboid(new Vector3(x - 0.18f, 6.5f, c0 + 1.5f), new Vector3(x + 0.18f, 6.6f, c1 - 1.5f), 2f);
            // Booths either side, lit windows facing the lanes.
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * 12.6f;
                B(claddingWhite).Cuboid(new Vector3(x - 1.4f, 0f, -367f), new Vector3(x + 1.4f, 3f, -361f), 2f);
                B(indWindow).Cuboid(new Vector3(x - s * 1.41f - 0.02f, 1.2f, -366.4f), new Vector3(x - s * 1.41f + 0.02f, 2.4f, -361.6f), 1f);
                B(roof).Cuboid(new Vector3(x - 1.6f, 3f, -367.2f), new Vector3(x + 1.6f, 3.25f, -360.8f), 2f);
                B(pool).Decal(new Vector3(x - s * 3f, 0.06f, -364f), 10f, 10f);
                Collider(root, "Booth", new Vector3(x, 1.5f, -364f), new Vector3(2.8f, 3f, 6f), environment);
            }
            B(pool).Decal(new Vector3(0f, 0.065f, (c0 + c1) * 0.5f), 26f, 24f);
            // Trench drain across the road, rumble bars, kerb grates.
            B(drain).Cuboid(new Vector3(-9f, 0.01f, -381.4f), new Vector3(9f, 0.03f, -380.6f), 1f);
            for (float x = -8.8f; x < 8.8f; x += 0.9f) B(grate).Cuboid(new Vector3(x, 0.02f, -381.35f), new Vector3(x + 0.7f, 0.04f, -380.65f), 0.7f);
            for (int i = 0; i < 6; i++) B(markingYellow).Strip(new Vector3(-8.5f, 0.045f, -392f - i * 1.4f), new Vector3(8.5f, 0.045f, -392f - i * 1.4f), 0.35f);
            for (float z = -630f; z < -345f; z += 12f)
                foreach (float x in new[] { -8.6f, 8.6f }) B(grate).Cuboid(new Vector3(x - 0.3f, 0.012f, z - 0.5f), new Vector3(x + 0.3f, 0.03f, z + 0.5f), 0.6f);
            // Painted lane arrows (shaft + head) in both lanes.
            foreach (float z in new[] { -540f, -470f, -430f })
                foreach (float x in new[] { -4.5f })
                {
                    B(marking).Strip(new Vector3(x, 0.046f, z), new Vector3(x, 0.046f, z + 4f), 0.3f);
                    B(marking).Tri(new Vector3(x - 0.9f, 0.046f, z + 4f), new Vector3(x, 0.046f, z + 6f), new Vector3(x + 0.9f, 0.046f, z + 4f), Vector2.zero, Vector2.up, Vector2.one);
                }
            SignBoard(root, "CanopyIn", "INSPECTION LANE 1 ↓", Color.white, new Color(0.04f, 0.1f, 0.22f), new Vector3(-5f, 7.2f, c0 - 0.05f), 0f, 0.42f);
            Flush("InspectionCanopy", root, environment, true);
            foreach (float x in new[] { -11f, 11f }) foreach (float z in new[] { c0 + 1f, c1 - 1f })
                Collider(root, "CanopyColumn", new Vector3(x, 3.3f, z), new Vector3(0.9f, 6.6f, 0.9f), environment);
            foreach (float x in new[] { -6f, 6f })
                extraBudgetLights.Add(Downlight(root, new Vector3(x, 6.4f, (c0 + c1) * 0.5f), new Vector3(x, 0f, (c0 + c1) * 0.5f), 5000f, 18f, 120f, new Color(0.95f, 0.97f, 1f)));
            var beaconMesh = CubeMesh("HR_GateBeacon", new Vector3(0.35f, 0.3f, 0.35f));
            foreach (float x in new[] { -13f, 13f })
            {
                var r = Single("GateBeacon", root, beaconMesh, amberLamp, detail, new Vector3(x, 8.3f, c0)).GetComponent<Renderer>();
                var bl = r.gameObject.AddComponent<HarborBlinker>();
                Configure(bl, so => { so.FindProperty("a").objectReferenceValue = r; so.FindProperty("colourA").colorValue = new Color(4f, 1.8f, 0.1f); so.FindProperty("colourB").colorValue = new Color(4f, 1.8f, 0.1f); so.FindProperty("rate").floatValue = 1.1f; });
            }
            SignPanel(root, "20 KM/H", Color.white, new Vector3(13.5f, 2.6f, -480f), 0f, 2.2f, true);
            SignPanel(root, "CHECKPOINT 100 M", new Color(1f, 0.7f, 0.2f), new Vector3(-13.5f, 2.6f, -470f), 0f, 4f, true);
            report.AppendLine($"terminal gate: sign gantry (board {top.x:0.0} m wide, letters 0.62 / 0.5 m, 5.6–8 m up, 2 downlights), lane signals, CCTV, lit inspection canopy with booths, trench drain, rumble bars, lane arrows");
        }
    }
}
