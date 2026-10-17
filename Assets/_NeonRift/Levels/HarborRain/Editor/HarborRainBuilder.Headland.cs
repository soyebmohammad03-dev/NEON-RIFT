using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// The extended coastal route (Harbor Rain 2.0). The sea road no longer ends beside the tank farm: it runs on past
    /// the inland bypass's rejoin, bends north onto a breakwater HEADLAND with the open sea on both sides, and bends
    /// east onto the PIER, where the extraction boat waits under a lighthouse. Both bends are drawn as curves (asphalt
    /// fillets, curved edge lines, curved guardrails and chevrons on the outside); the logical roads stay the
    /// axis-aligned rectangles the route graph, minimap and police use. On the way: an exposed spray zone with warning
    /// beacons (spray bursts over the wall, standing water on the road), a covered storm gallery, a bulk conveyor
    /// crossing overhead and a narrowed one-lane works section. The wave sections on the original sea road are
    /// unchanged; both the sea road and the inland bypass lead here, so the bypass stays a real alternative.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        // Logical roads 24 (headland, along z) and 25 (pier, along x); the coast road (21) now runs to x 1916.
        private const float HeadX0 = 1900f, HeadX1 = 1916f, HeadZ1 = 560f, PierZ0 = 544f, PierZ1 = 560f, PierX1 = 2280f;
        private const float BendRadius = 14f;
        private static readonly Vector3 BendA = new(1894f, 0f, 290f), BendB = new(1922f, 0f, 538f);
        // Land faces of the breakwater walls (7 m beyond the road edges, like the coast road's seawall).
        private const float HeadWallW = 1893f, HeadWallE = 1923f, PierWallS = 537f, PierWallN = 567f, PierWallEnd = 2300f;
        public static readonly Rect SprayZone = new(1898f, 330f, 20f, 56f);
        public static readonly Rect GalleryArea = new(1891f, 404f, 34f, 46f);
        public static readonly Rect NarrowArea = new(2116f, 543f, 60f, 18f);

        /// <summary>Spinners and rockers created here and in the port builders, picked up by BuildLife.</summary>
        private static readonly List<(Transform target, float degreesPerSecond)> extraSpinners = new();
        private static readonly List<HarborLife.Rocker> extraRockers = new();

        private static Material lighthouseRed, sprayBeacon;

        // ---------------- Generic oriented pieces ----------------

        private static Quaternion Facing(Vector3 dir) => Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z).normalized, Vector3.up);

        /// <summary>A W-beam rail run from a to b (any heading); <paramref name="towardRoad"/> is the horizontal normal to the road side.</summary>
        private static void RailRun(Transform root, string name, Vector3 a, Vector3 b, Vector3 towardRoad, Material reflector, bool stained = false, bool collider = true)
        {
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.2f) return;
            Vector3 dir = d / len, n = new Vector3(towardRoad.x, 0f, towardRoad.z).normalized;
            var m = stained ? guardrailStained : guardrail;
            for (float s0 = 0f; s0 < len - 0.01f; s0 += 4f)
            {
                float s1 = Mathf.Min(len, s0 + 4f);
                for (int j = 1; j < WProfile.Length; j++)
                {
                    Vector3 P(float s, int k) => a + dir * s + Vector3.up * WProfile[k].y + n * WProfile[k].x;
                    Vector2 U(float s, int k) => new((a.x + a.z + s) * 0.25f, k * 0.15f);
                    B(m).Quad(P(s0, j - 1), P(s1, j - 1), P(s1, j), P(s0, j), U(s0, j - 1), U(s1, j - 1), U(s1, j), U(s0, j));
                    B(m).Quad(P(s0, j - 1), P(s0, j), P(s1, j), P(s1, j - 1), U(s0, j - 1), U(s0, j), U(s1, j), U(s1, j - 1));
                }
            }
            var rot = Facing(dir);
            int k2 = 0;
            for (float s = 1f; s < len; s += 2f, k2++)
            {
                var p = a + dir * s;
                B(metal).OrientedBox(p - n * 0.24f + Vector3.up * 0.39f, new Vector3(0.15f, 0.78f, 0.1f), rot, 1f);
                B(metal).OrientedBox(p - n * 0.1f + Vector3.up * 0.56f, new Vector3(0.15f, 0.2f, 0.1f), rot, 1f);
                if (k2 % 3 == 0) B(reflector).OrientedBox(p + n * 0.09f + Vector3.up * 0.56f, new Vector3(0.02f, 0.06f, 0.08f), rot, 1f);
            }
            if (collider)
                Collider(root, name, (a + b) * 0.5f - n * 0.08f + Vector3.up * 0.45f, new Vector3(0.36f, 0.9f, len + 0.3f), environment, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
        }

        /// <summary>A recurved wave wall from a to b whose land face is on the a–b line and whose lip turns toward <paramref name="towardSea"/>.</summary>
        private static void WallRun(Transform root, string name, Vector3 a, Vector3 b, Vector3 towardSea, float h, Material m, bool collider = true)
        {
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.2f) return;
            Vector3 dir = d / len, n = new Vector3(towardSea.x, 0f, towardSea.z).normalized;
            var prof = new[] { new Vector2(0f, 0f), new Vector2(0f, h), new Vector2(1f, h), new Vector2(1.55f, h - 0.06f), new Vector2(1.78f, h * 0.84f), new Vector2(1.5f, h * 0.62f), new Vector2(1.42f, 0f) };
            for (int j = 1; j < prof.Length; j++)
            {
                Vector3 P(float s, Vector2 q) => a + dir * s + n * q.x + Vector3.up * q.y;
                Vector2 pa = prof[j - 1], pb = prof[j];
                B(m).Quad(P(0f, pa), P(0f, pb), P(len, pb), P(len, pa), new Vector2(0f, (j - 1) * 0.3f), new Vector2(0f, j * 0.3f), new Vector2(len / 2f, j * 0.3f), new Vector2(len / 2f, (j - 1) * 0.3f));
                B(m).Quad(P(0f, pa), P(len, pa), P(len, pb), P(0f, pb), new Vector2(0f, (j - 1) * 0.3f), new Vector2(len / 2f, (j - 1) * 0.3f), new Vector2(len / 2f, j * 0.3f), new Vector2(0f, j * 0.3f));
            }
            B(hazardStripe).OrientedBox(a + dir * (len * 0.5f) + n * 0.5f + Vector3.up * (h + 0.02f), new Vector3(0.8f, 0.04f, len), Facing(dir), 1f);
            if (collider) Collider(root, name, a + dir * (len * 0.5f) + n * 0.85f + Vector3.up * (h * 0.5f + 0.3f), new Vector3(1.7f, h + 0.6f, len), environment, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
        }

        /// <summary>Points on an arc round <paramref name="c"/> from angle a0 to a1 (degrees, 0 = +x, 90 = +z).</summary>
        private static List<Vector3> Arc(Vector3 c, float r, float a0, float a1, int steps, float y = 0f)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
                pts.Add(new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r));
            }
            return pts;
        }

        /// <summary>A curved bend: asphalt fillet between the inner and outer edge, curved edge lines and centre dashes.</summary>
        private static void BendSurface(Vector3 c, float a0, float a1)
        {
            const int steps = 16;
            float rIn = BendRadius - 8f, rOut = BendRadius + 8f;
            var inner = Arc(c, rIn, a0, a1, steps, 0.052f);
            var outer = Arc(c, rOut, a0, a1, steps, 0.052f);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 Uv(Vector3 p) => new(p.x / 6f, p.z / 6f);
                Vector3 a = inner[i - 1], b = inner[i], cc = outer[i], d = outer[i - 1];
                B(wetAsphalt).Quad(a, d, cc, b, Uv(a), Uv(d), Uv(cc), Uv(b));
                B(wetAsphalt).Quad(a, b, cc, d, Uv(a), Uv(b), Uv(cc), Uv(d));
            }
            foreach (float r in new[] { rIn + 0.6f, rOut - 0.6f })
            {
                var e = Arc(c, r, a0, a1, steps, 0.058f);
                for (int i = 1; i <= steps; i++) B(markingYellow).Strip(e[i - 1], e[i], 0.12f);
            }
            var mid = Arc(c, BendRadius, a0, a1, steps, 0.058f);
            for (int i = 1; i <= steps; i += 2) B(marking).Strip(mid[i - 1], mid[i], 0.16f);
        }

        private static void Chevron(Vector3 c, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            B(chevron).Panel(c + rot * new Vector3(0f, 0f, 0.03f), rot, 1.2f, 0.6f, new Rect(0f, 0f, 1f, 1f));
            B(metal).Panel(c - rot * new Vector3(0f, 0f, 0.03f), rot * Quaternion.Euler(0f, 180f, 0f), 1.2f, 0.6f, new Rect(0f, 0f, 1f, 1f));
            B(metal).Cuboid(new Vector3(c.x - 0.05f, 0f, c.z - 0.05f), new Vector3(c.x + 0.05f, c.y - 0.3f, c.z + 0.05f), 1f);
        }

        // ---------------- The headland and the pier ----------------

        private static void BuildHeadland(Transform world, Random rng, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup amb, Coast coast)
        {
            var root = new GameObject("HeadlandAndPier").transform;
            root.SetParent(world, false);
            float R() => (float)rng.NextDouble();
            lighthouseRed = Lit("HR_LighthouseRed", new Color(0.42f, 0.05f, 0.04f), 0.45f, 0f, null);
            sprayBeacon = Lit("HR_SprayBeacon", new Color(0.12f, 0.06f, 0f), 0.6f, 0f, new Color(0.05f, 0.02f, 0f));

            // Breakwater slabs: concrete deck (drivable), faces down into the sea, armour rock along the toes.
            Begin();
            var head = new Rect(HeadWallW - 1.8f, 291f, HeadWallE - HeadWallW + 3.6f, PierWallN - 291f);
            var pier = new Rect(HeadWallW - 1.8f, PierWallS - 1.8f, PierWallEnd + 1.8f - (HeadWallW - 1.8f), PierWallN - PierWallS + 3.6f);
            foreach (var r in new[] { head, pier })
            {
                B(wetConcrete).Ground(new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMax), 8f);
                B(concreteDark).Cuboid(new Vector3(r.xMin, -9f, r.yMin), new Vector3(r.xMax, -0.03f, r.yMax), 4f);
            }
            void Armour(Vector3 a, Vector3 b, Vector3 outward, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    var p = Vector3.Lerp(a, b, R()) + outward * (0.8f + R() * 7f);
                    p.y = SeaLevel - 1.4f + R() * 1.9f;
                    B(rock).OrientedBox(p, Vector3.one * (1.6f + R() * 1.6f), Quaternion.Euler(rng.Next(360), rng.Next(360), rng.Next(360)), 1f);
                }
            }
            Armour(new Vector3(head.xMin, 0f, 301f), new Vector3(head.xMin, 0f, pier.yMax), Vector3.left, 140);
            Armour(new Vector3(head.xMax, 0f, 301f), new Vector3(head.xMax, 0f, pier.yMin), Vector3.right, 120);
            Armour(new Vector3(head.xMin, 0f, pier.yMax), new Vector3(pier.xMax, 0f, pier.yMax), Vector3.forward, 220);
            Armour(new Vector3(head.xMax, 0f, pier.yMin), new Vector3(pier.xMax, 0f, pier.yMin), Vector3.back, 200);
            Armour(new Vector3(pier.xMax, 0f, pier.yMin), new Vector3(pier.xMax, 0f, pier.yMax), Vector3.right, 40);
            Flush("BreakwaterDeck", root, drivable, false);
            foreach (var (n, r) in new[] { ("HeadlandDeck", head), ("PierDeck", pier) })
            {
                var col = Collider(root, n, new Vector3(r.center.x, -0.5f, r.center.y), new Vector3(r.width, 1f, r.height), drivable);
                col.gameObject.AddComponent<NeonRift.Vehicles.DrivingSurface>().EditorConfigure(0.74f, 1.3f);
            }

            // Walls (recurved, 1.15 m), walkways and kerbs on every sea side.
            Begin();
            WallRun(root, "HeadWallWest", new Vector3(HeadWallW, 0f, 291f), new Vector3(HeadWallW, 0f, PierWallN), Vector3.left, 1.15f, concreteDark);
            WallRun(root, "HeadWallEast", new Vector3(HeadWallE, 0f, 291f), new Vector3(HeadWallE, 0f, PierWallS), Vector3.right, 1.15f, concreteDark);
            WallRun(root, "PierWallNorth", new Vector3(HeadWallW, 0f, PierWallN), new Vector3(PierWallEnd, 0f, PierWallN), Vector3.forward, 1.15f, concreteDark);
            WallRun(root, "PierWallSouth", new Vector3(HeadWallE, 0f, PierWallS), new Vector3(PierWallEnd, 0f, PierWallS), Vector3.back, 1.15f, concreteDark);
            WallRun(root, "PierWallEnd", new Vector3(PierWallEnd, 0f, PierWallS), new Vector3(PierWallEnd, 0f, PierWallN), Vector3.right, 1.15f, concreteDark);
            // The reclaimed land east of the headland meets the sea in a plain quay wall face.
            B(concreteDark).Cuboid(new Vector3(HeadWallE + 1.8f, -6f, 299.8f), new Vector3(2620f, 0f, WallFace), 4f);
            B(walkway).Ground(new Vector3(HeadWallW, 0.02f, 291f), new Vector3(HeadX0 - 1.4f, 0.02f, PierWallN), 2f);
            B(walkway).Ground(new Vector3(HeadX1 + 1.4f, 0.02f, 291f), new Vector3(HeadWallE, 0.02f, PierWallS), 2f);
            B(walkway).Ground(new Vector3(HeadX0 - 1.4f, 0.021f, PierZ1 + 1.4f), new Vector3(PierWallEnd, 0.021f, PierWallN), 2f);
            B(walkway).Ground(new Vector3(HeadX1 + 1.4f, 0.021f, PierWallS), new Vector3(PierWallEnd, 0.021f, PierZ0 - 1.4f), 2f);
            B(walkway).Ground(new Vector3(PierX1 + 1f, 0.022f, PierZ0 - 1.4f), new Vector3(PierWallEnd, 0.022f, PierZ1 + 1.4f), 2f);
            Flush("BreakwaterWalls", root, environment, false);

            // Guardrails: straight runs and the curved bends (inner and outer), plus the coast road's new land-side run.
            Begin();
            RailRun(root, "LandRailEast", new Vector3(1604f, 0f, LandRailZ), new Vector3(BendA.x, 0f, LandRailZ), Vector3.forward, studWhite);
            float rIn = BendRadius - 8f - 1.2f, rOut = BendRadius + 8f + 1.2f;
            void ArcRail(string n, Vector3 c, float r, float a0, float a1, Vector3 centreSide)
            {
                var pts = Arc(c, r, a0, a1, 10);
                for (int i = 1; i < pts.Count; i++)
                {
                    var mid = (pts[i - 1] + pts[i]) * 0.5f;
                    var toward = (centreSide.x > 0f ? (c - mid) : (mid - c)).normalized;   // road side: toward the centre (outer rail) or away (inner rail)
                    RailRun(root, n, pts[i - 1], pts[i], toward, studAmber);
                }
            }
            ArcRail("BendA_Outer", BendA, rOut, -90f, 0f, Vector3.right);
            ArcRail("BendA_Inner", BendA, rIn, -90f, 0f, Vector3.left);
            ArcRail("BendB_Outer", BendB, rOut, 180f, 90f, Vector3.right);
            ArcRail("BendB_Inner", BendB, rIn, 180f, 90f, Vector3.left);
            RailRun(root, "HeadRailWest", new Vector3(HeadX0 - 1.2f, 0f, BendA.z), new Vector3(HeadX0 - 1.2f, 0f, BendB.z), Vector3.right, studAmber);
            RailRun(root, "HeadRailEast", new Vector3(HeadX1 + 1.2f, 0f, BendA.z), new Vector3(HeadX1 + 1.2f, 0f, BendB.z), Vector3.left, studAmber);
            RailRun(root, "PierRailNorth", new Vector3(BendB.x, 0f, PierZ1 + 1.2f), new Vector3(PierX1 + 2f, 0f, PierZ1 + 1.2f), Vector3.back, studAmber);
            RailRun(root, "PierRailSouth", new Vector3(BendB.x, 0f, PierZ0 - 1.2f), new Vector3(PierX1 + 2f, 0f, PierZ0 - 1.2f), Vector3.forward, studAmber);
            RailRun(root, "PierRailEnd", new Vector3(PierX1 + 3f, 0f, PierZ0 - 1.2f), new Vector3(PierX1 + 3f, 0f, PierZ1 + 1.2f), Vector3.left, studAmber);
            Flush("BreakwaterGuardrails", root, environment, true);

            // Road surface details: curved bends, straight edge lines (the generic ones are not drawn for these roads), studs.
            Begin();
            BendSurface(BendA, -90f, 0f);
            BendSurface(BendB, 180f, 90f);
            void Edge(Vector3 a, Vector3 b) => B(markingYellow).Strip(a, b, 0.12f);
            Edge(new Vector3(886f, 0.04f, CoastZ0 + 0.6f), new Vector3(BendA.x, 0.04f, CoastZ0 + 0.6f));
            Edge(new Vector3(886f, 0.04f, CoastZ1 - 0.6f), new Vector3(BendA.x, 0.04f, CoastZ1 - 0.6f));
            Edge(new Vector3(HeadX0 + 0.6f, 0.04f, BendA.z), new Vector3(HeadX0 + 0.6f, 0.04f, BendB.z));
            Edge(new Vector3(HeadX1 - 0.6f, 0.04f, BendA.z), new Vector3(HeadX1 - 0.6f, 0.04f, BendB.z));
            Edge(new Vector3(BendB.x, 0.04f, PierZ0 + 0.6f), new Vector3(PierX1 - 2f, 0.04f, PierZ0 + 0.6f));
            Edge(new Vector3(BendB.x, 0.04f, PierZ1 - 0.6f), new Vector3(PierX1 - 2f, 0.04f, PierZ1 - 0.6f));
            for (float z = BendA.z + 4f; z < BendB.z; z += 8f)
                foreach (float x in new[] { HeadX0 + 0.6f, HeadX1 - 0.6f })
                    B(studAmber).Cuboid(new Vector3(x - 0.05f, 0.04f, z - 0.07f), new Vector3(x + 0.05f, 0.065f, z + 0.07f), 1f);
            for (float x = BendB.x + 4f; x < PierX1; x += 8f)
                foreach (float z in new[] { PierZ0 + 0.6f, PierZ1 - 0.6f })
                    B(studAmber).Cuboid(new Vector3(x - 0.07f, 0.04f, z - 0.05f), new Vector3(x + 0.07f, 0.065f, z + 0.05f), 1f);
            // Turning head at the pier end and the hatched box in front of the extraction.
            for (float z = PierZ0 + 1.5f; z < PierZ1 - 1f; z += 2.2f) B(hazardStripe).Strip(new Vector3(PierX1 - 1f, 0.05f, z), new Vector3(PierX1 + 2f, 0.05f, z + 1.2f), 0.3f);
            // Chevrons on the outside of both bends.
            foreach (float a in new[] { -70f, -45f, -20f })
            {
                float rad = a * Mathf.Deg2Rad;
                var p = new Vector3(BendA.x + Mathf.Cos(rad) * (rOut + 1.4f), 1.5f, BendA.z + Mathf.Sin(rad) * (rOut + 1.4f));
                Chevron(p, Mathf.Atan2(BendA.x - p.x, BendA.z - p.z) * Mathf.Rad2Deg);
            }
            foreach (float a in new[] { 160f, 135f, 110f })
            {
                float rad = a * Mathf.Deg2Rad;
                var p = new Vector3(BendB.x + Mathf.Cos(rad) * (rOut + 1.4f), 1.5f, BendB.z + Mathf.Sin(rad) * (rOut + 1.4f));
                Chevron(p, Mathf.Atan2(BendB.x - p.x, BendB.z - p.z) * Mathf.Rad2Deg);
            }
            Flush("BreakwaterRoadDetail", root, detail, false);
            SignPanel(root, "HEADLAND", new Color(0.33f, 0.91f, 1f), new Vector3(HeadX1 + 3.2f, 5f, 300f), 180f, 6f, true);
            SignPanel(root, "PIER  ·  EXTRACTION", new Color(0.33f, 0.91f, 1f), new Vector3(1930f, 5f, PierZ0 - 3.4f), 270f, 9f, true);
            SignPanel(root, "SHARP BEND", new Color(1f, 0.7f, 0.2f), new Vector3(1846f, 3.6f, CoastZ0 - 3f), 270f, 5.5f, true);
            SignPanel(root, "SHARP BEND", new Color(1f, 0.7f, 0.2f), new Vector3(HeadX1 + 3.2f, 3.6f, 492f), 180f, 5.5f, true);

            BuildSprayZone(root, rng, clips, amb);
            BuildStormGallery(root);
            BuildConveyor(root);
            BuildNarrowSection(root);
            BuildLighthouse(root);
            BuildOilTerminal(root, rng);
            report.AppendLine($"coastal extension: sea road to x {HeadX1:0}, two curved bends (radius {BendRadius:0} m), headland road {HeadZ1 - CoastZ0:0} m, pier road {PierX1 - HeadX0:0} m; spray zone, storm gallery, overhead conveyor, one-lane works section, lighthouse; extraction moved to the pier end");
        }

        /// <summary>The exposed stretch after the first bend: spray bursts over the wall (warned by amber beacons), standing water on the road.</summary>
        private static void BuildSprayZone(Transform root, Random rng, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup amb)
        {
            var zone = new GameObject("SprayZone").transform;
            zone.SetParent(root, false);
            var bursts = new List<ParticleSystem>();
            for (int i = 0; i < 4; i++)
            {
                var ps = SprayParticles("WallSprayBurst", zone, 900, 1.0f, new Vector2(2.4f, 5.5f), 0.26f, 0.06f);
                ps.transform.SetPositionAndRotation(new Vector3(HeadWallW - 0.6f, 1.2f, SprayZone.yMin + 6f + i * 14f), Quaternion.Euler(-62f, 90f, 0f));
                var main = ps.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(11f, 20f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(0.6f, 12f, 0.4f);
                bursts.Add(ps);
            }
            var lampMesh = CubeMesh("HR_SprayBeaconLamp", new Vector3(0.45f, 0.45f, 0.45f));
            var lamps = new List<Renderer>();
            Begin();
            foreach (float z in new[] { SprayZone.yMin - 12f, SprayZone.yMax + 12f })
                foreach (float x in new[] { HeadX0 - 3f, HeadX1 + 3f })
                {
                    B(metal).Cylinder(new Vector3(x, 0f, z), 0.08f, 3.4f, 8, true);
                    lamps.Add(Single("SprayBeacon", zone, lampMesh, sprayBeacon, detail, new Vector3(x, 3.6f, z)).GetComponent<Renderer>());
                }
            Flush("SprayBeaconPoles", zone, detail, false);
            SignPanel(zone, "WAVE SPRAY", new Color(1f, 0.55f, 0.15f), new Vector3(HeadX1 + 3.2f, 4.6f, SprayZone.yMin - 16f), 180f, 6f, true);
            var src = Spatial(zone, "SprayRoar", clips.WaveRoar, false, amb, 0.9f, 15f, 260f);
            src.transform.position = new Vector3(HeadWallW, 1f, SprayZone.center.y);
            var bz = zone.gameObject.AddComponent<HarborSprayBursts>();
            Configure(bz, so =>
            {
                Array(so.FindProperty("bursts"), bursts.ToArray());
                Array(so.FindProperty("beacons"), lamps.ToArray());
                so.FindProperty("roar").objectReferenceValue = src;
            });
        }

        /// <summary>A covered storm gallery on the headland: a roof on columns, solid on the sea side, lit inside.</summary>
        private static void BuildStormGallery(Transform root)
        {
            var g = new GameObject("StormGallery").transform;
            g.SetParent(root, false);
            Begin();
            float z0 = GalleryArea.yMin, z1 = GalleryArea.yMax;
            B(concreteDark).Cuboid(new Vector3(HeadWallW - 1.6f, 6.4f, z0), new Vector3(HeadWallE + 1.6f, 7.4f, z1), 4f);
            B(stainedConcrete).Cuboid(new Vector3(HeadWallW - 0.6f, 1.15f, z0), new Vector3(HeadWallW + 0.4f, 6.4f, z1), 4f);   // sea-side wall on the wave wall
            for (float z = z0 + 5f; z < z1 - 2f; z += 9f) B(indWindow).Cuboid(new Vector3(HeadWallW + 0.41f, 3.6f, z), new Vector3(HeadWallW + 0.44f, 4.1f, z + 4f), 1f);
            for (float z = z0 + 1f; z <= z1 - 0.5f; z += 9f)
            {
                B(concreteDark).Cuboid(new Vector3(HeadWallE - 3.2f, 0f, z - 0.4f), new Vector3(HeadWallE - 2.4f, 6.4f, z + 0.4f), 2f);
                Collider(g, "GalleryColumn", new Vector3(HeadWallE - 2.8f, 3.2f, z), new Vector3(0.8f, 6.4f, 0.8f), environment);
            }
            foreach (float x in new[] { HeadX0 + 2f, HeadX1 - 2f }) B(workLight).Cuboid(new Vector3(x - 0.15f, 6.32f, z0 + 2f), new Vector3(x + 0.15f, 6.4f, z1 - 2f), 2f);
            B(hazardStripe).Cuboid(new Vector3(HeadWallW - 1.6f, 6.0f, z0 - 0.05f), new Vector3(HeadWallE + 1.6f, 6.4f, z0 + 0.3f), 1f);
            B(hazardStripe).Cuboid(new Vector3(HeadWallW - 1.6f, 6.0f, z1 - 0.3f), new Vector3(HeadWallE + 1.6f, 6.4f, z1 + 0.05f), 1f);
            Flush("Gallery", g, environment, true);
            SignPanel(g, "STORM GALLERY", new Color(0.33f, 0.91f, 1f), new Vector3(1908f, 6.9f, z0 - 0.3f), 180f, 8f);
            SignPanel(g, "MAX 3.8 M  ·  LIGHTS ON", Color.white, new Vector3(1908f, 6.9f, z1 + 0.3f), 0f, 7f);
            foreach (float z in new[] { z0 + 12f, z1 - 12f })
            {
                var go = new GameObject("GalleryLight") { layer = detail };
                go.transform.SetParent(g, false);
                go.transform.SetPositionAndRotation(new Vector3(1908f, 6.1f, z), Quaternion.Euler(90f, 0f, 0f));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.color = new Color(0.9f, 0.95f, 1f); l.intensity = 1600f; l.range = 14f; l.spotAngle = 120f; l.innerSpotAngle = 70f; l.shadows = LightShadows.None;
            }
            solids.Add(GalleryArea);
        }

        /// <summary>A bulk conveyor crossing over the pier road to a ship-loader boom out over the water.</summary>
        private static void BuildConveyor(Transform root)
        {
            var c = new GameObject("BulkConveyor").transform;
            c.SetParent(root, false);
            Begin();
            const float x = 2060f, z0 = 522f, z1 = 590f, yb = 9.6f, yt = 12.4f;
            foreach (float z in new[] { z0, z1 })
            {
                B(craneSteel).Cuboid(new Vector3(x - 2.2f, -7f, z - 2.2f), new Vector3(x + 2.2f, yt + 1f, z + 2.2f), 3f);
                B(aviation).Cuboid(new Vector3(x - 0.3f, yt + 1f, z - 0.3f), new Vector3(x + 0.3f, yt + 1.5f, z + 0.3f), 1f);
            }
            foreach (float dx in new[] { -1.5f, 1.5f })
            {
                B(craneSteel).Cuboid(new Vector3(x + dx - 0.15f, yb, z0), new Vector3(x + dx + 0.15f, yb + 0.3f, z1), 4f);
                B(craneSteel).Cuboid(new Vector3(x + dx - 0.15f, yt - 0.3f, z0), new Vector3(x + dx + 0.15f, yt, z1), 4f);
                for (float z = z0; z < z1; z += 4f)
                {
                    var a = new Vector3(x + dx, yb + 0.15f, z);
                    var b = new Vector3(x + dx, yt - 0.15f, z + 4f);
                    B(craneSteel).OrientedBox((a + b) * 0.5f, new Vector3(0.12f, 0.12f, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a), 1f);
                    B(craneSteel).Cuboid(new Vector3(x + dx - 0.08f, yb, z - 0.08f), new Vector3(x + dx + 0.08f, yt, z + 0.08f), 1f);
                }
            }
            B(rubber).Cuboid(new Vector3(x - 0.9f, yb + 0.3f, z0), new Vector3(x + 0.9f, yb + 0.6f, z1), 4f);   // belt
            B(roof).Cuboid(new Vector3(x - 1.7f, yt, z0), new Vector3(x + 1.7f, yt + 0.25f, z1), 4f);
            for (float z = z0 + 6f; z < z1; z += 12f) B(neonAmber).Cuboid(new Vector3(x - 1.4f, yb - 0.05f, z), new Vector3(x + 1.4f, yb, z + 0.8f), 1f);
            // Ship-loader boom out over the water to the north.
            B(craneSteel).Cuboid(new Vector3(x - 1.2f, 15f, z1 - 1f), new Vector3(x + 1.2f, 17f, z1 + 46f), 4f);
            B(craneSteel).Cuboid(new Vector3(x - 0.6f, 4f, z1 + 44f), new Vector3(x + 0.6f, 15f, z1 + 45.2f), 2f);
            B(aviation).Cuboid(new Vector3(x - 0.3f, 17f, z1 + 45f), new Vector3(x + 0.3f, 17.5f, z1 + 45.6f), 1f);
            Flush("Conveyor", c, environment, true);
            SignPanel(c, "CLEARANCE 9.5 M", new Color(1f, 0.7f, 0.2f), new Vector3(x - 1.8f, yb - 0.6f, (PierZ0 + PierZ1) * 0.5f), 270f, 6f);
        }

        /// <summary>A one-lane works section on the pier: water-filled barriers close the north lane, with tapers, lights and signs.</summary>
        private static void BuildNarrowSection(Transform root)
        {
            var n = new GameObject("PierWorks").transform;
            n.SetParent(root, false);
            Begin();
            float x0 = NarrowArea.xMin + 6f, x1 = NarrowArea.xMax - 6f, zc = (PierZ0 + PierZ1) * 0.5f + 0.4f;
            int k = 0;
            for (float x = x0 - 10f; x < x1 + 10f; x += 2.05f, k++)
            {
                // Tapered at both ends (the line swings from the north kerb to the centre line), straight between.
                float taper = x < x0 ? Mathf.InverseLerp(x0 - 10f, x0, x) : x > x1 ? 1f - Mathf.InverseLerp(x1, x1 + 10f, x) : 1f;
                float z = Mathf.Lerp(PierZ1 - 0.8f, zc, taper);
                var mat = k % 2 == 0 ? barrierOrange : barrierWhite;
                B(mat).OrientedBox(new Vector3(x, 0.48f, z), new Vector3(1.96f, 0.95f, 0.56f), Quaternion.identity, 1f);
                B(mat).OrientedBox(new Vector3(x, 1f, z), new Vector3(1.8f, 0.1f, 0.4f), Quaternion.identity, 1f);
            }
            Collider(n, "WorksBarrier", new Vector3((x0 + x1) * 0.5f, 0.5f, zc), new Vector3(x1 - x0, 1f, 0.6f), environment);
            foreach (float s in new[] { -1f, 1f })
            {
                float xa = s < 0 ? x0 - 10f : x1 + 10f, xb = s < 0 ? x0 : x1;
                var a = new Vector3(xa, 0.5f, PierZ1 - 0.8f);
                var b = new Vector3(xb, 0.5f, zc);
                Collider(n, "WorksTaper", (a + b) * 0.5f, new Vector3(0.6f, 1f, Vector3.Distance(a, b)), environment, Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg);
            }
            // Behind the barriers: a light tower, a compressor and pallets of kerb units (the works crew went home).
            B(metal).Cylinder(new Vector3((x0 + x1) * 0.5f, 0f, PierZ1 - 2.5f), 0.12f, 7f, 8, true);
            B(workLight).Cuboid(new Vector3((x0 + x1) * 0.5f - 0.9f, 7f, PierZ1 - 2.8f), new Vector3((x0 + x1) * 0.5f + 0.9f, 7.6f, PierZ1 - 2.4f), 1f);
            for (int i = 0; i < 3; i++) B(concreteDark).Cuboid(new Vector3(x0 + 8f + i * 4f, 0f, PierZ1 - 4f), new Vector3(x0 + 10.4f + i * 4f, 0.9f, PierZ1 - 2.6f), 1f);
            Flush("Works", n, environment, true);
            var lampMesh = CubeMesh("HR_WorksLamp", new Vector3(0.22f, 0.22f, 0.22f));
            for (float x = x0 - 8f; x < x1 + 8f; x += 6f)
            {
                float taper = x < x0 ? Mathf.InverseLerp(x0 - 10f, x0, x) : x > x1 ? 1f - Mathf.InverseLerp(x1, x1 + 10f, x) : 1f;
                var r = Single("WorksLamp", n, lampMesh, sprayBeacon, detail, new Vector3(x, 1.2f, Mathf.Lerp(PierZ1 - 0.8f, zc, taper))).GetComponent<Renderer>();
                var bl = r.gameObject.AddComponent<HarborBlinker>();
                float phase = (x - x0) * 0.03f;
                Configure(bl, so =>
                {
                    so.FindProperty("a").objectReferenceValue = r;
                    so.FindProperty("colourA").colorValue = new Color(3.2f, 1.4f, 0.1f);
                    so.FindProperty("colourB").colorValue = new Color(3.2f, 1.4f, 0.1f);
                    so.FindProperty("rate").floatValue = 1.6f + phase;
                });
            }
            SignPanel(n, "ROAD NARROWS", new Color(1f, 0.7f, 0.2f), new Vector3(x0 - 40f, 3.6f, PierZ0 - 3.2f), 270f, 6f, true);
            SignPanel(n, "ONE LANE  ·  KEEP RIGHT", new Color(1f, 0.7f, 0.2f), new Vector3(x0 - 14f, 2.6f, PierZ1 - 0.8f), 270f, 6f, true);
            solids.Add(new Rect(x0 - 10f, PierZ1 - 4.5f, x1 - x0 + 20f, 4.5f));
        }

        /// <summary>The pier-end lighthouse: banded tower, lantern, and a slowly turning beam (the route's landmark).</summary>
        private static void BuildLighthouse(Transform root)
        {
            var l = new GameObject("Lighthouse").transform;
            l.SetParent(root, false);
            var p = new Vector3(PierWallEnd - 6f, 0f, PierWallN - 6f);
            Begin();
            B(concreteDark).Cylinder(p, 3.4f, 1.2f, 20, true);
            for (int i = 0; i < 6; i++) B(i % 2 == 0 ? shipWhite : lighthouseRed).Cylinder(p + Vector3.up * (1.2f + i * 3.2f), 2.4f - i * 0.12f, 3.2f, 20, false);
            B(metal).Cylinder(p + Vector3.up * 20.4f, 2.6f, 0.3f, 20, true);
            B(glass).Cylinder(p + Vector3.up * 20.7f, 1.4f, 2.2f, 16, false);
            B(lighthouseRed).Cone(p + Vector3.up * 24.4f, 1.6f, 1.5f, 16, 0.1f);
            B(navWhite).Cylinder(p + Vector3.up * 21.1f, 0.6f, 1.2f, 12, true);
            Flush("LighthouseTower", l, environment, true);
            Beacon(l, "Lantern", p + Vector3.up * 21.7f, BeaconMat("Lantern", new Color(5f, 4.7f, 4f), 1.6f, 0.006f));
            Collider(l, "LighthouseBase", p + Vector3.up * 5f, new Vector3(5.6f, 10f, 5.6f), environment);
            var beam = new GameObject("Beam").transform;
            beam.SetParent(l, false);
            beam.position = p + Vector3.up * 21.7f;
            foreach (float yaw in new[] { 0f, 180f })
            {
                var go = new GameObject("BeamLight") { layer = detail };
                go.transform.SetParent(beam, false);
                go.transform.localRotation = Quaternion.Euler(4f, yaw, 0f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot; light.color = new Color(1f, 0.95f, 0.85f); light.intensity = 60000f; light.range = 420f; light.spotAngle = 7f; light.innerSpotAngle = 2f; light.shadows = LightShadows.None;
            }
            var shaft = new NeonRift.EditorTools.District.MeshBuilder();
            shaft.Cone(Vector3.zero, 4.5f, 90f, 14, 0.35f);
            var shaftMesh = SaveMesh(shaft.ToMesh("HR_LighthouseShaft"), "HR_LighthouseShaft");
            foreach (float yaw in new[] { 0f, 180f })
            {
                // Cone apex at the lantern, opening along the beam (the cone is built pointing down: −y turned onto the beam).
                var go = Single("BeamShaft", beam, shaftMesh, cone, detail, beam.position);
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-86f, 0f, 0f);
            }
            extraSpinners.Add((beam, 30f));
            solids.Add(new Rect(p.x - 4f, p.z - 4f, 8f, 8f));
        }

        /// <summary>
        /// Reclaimed land east of the first bend: an oil terminal behind its fence (big floating-roof tanks with
        /// bund walls, a pipe rack to the jetty, a flare stack burning in the rain, aviation lights). Not drivable;
        /// it gives the coast road a skyline and fills what was empty ground.
        /// </summary>
        private static void BuildOilTerminal(Transform root, Random rng)
        {
            var t = new GameObject("OilTerminal").transform;
            t.SetParent(root, false);
            var tankWhite = Lit("HR_TankWhite", new Color(0.5f, 0.52f, 0.53f), 0.42f, 0.3f, null);
            Begin();
            int n = 0;
            for (float x = 2000f; x < 2560f; x += 46f)
                for (float z = 150f; z < 270f; z += 50f, n++)
                {
                    float r = 15f + (n % 3) * 2.5f, h = 14f + (n % 4) * 2f;
                    var c = new Vector3(x + (z > 200f ? 20f : 0f), 0f, z);
                    B(concreteDark).Cylinder(c, r + 3f, 1.2f, 28, true, 6f);                 // bund
                    B(tankWhite).Cylinder(c, r, h, 28, true, 8f);
                    B(hazardStripe).Cylinder(c + Vector3.up * (h - 0.6f), r + 0.05f, 0.6f, 28, false, 8f);
                    for (float y = 1f; y < h; y += 0.9f) B(metal).OrientedBox(c + new Vector3(r + 0.25f, y, 0f), new Vector3(0.4f, 0.06f, 1f), Quaternion.Euler(0f, 0f, 0f), 1f);   // stair
                    B(aviation).Cuboid(c + new Vector3(-0.3f, h, -0.3f), c + new Vector3(0.3f, h + 0.5f, 0.3f), 1f);
                }
            // Pipe rack along the shore to the headland, and a flare stack.
            for (float x = 1960f; x < 2580f; x += 8f)
            {
                foreach (float z in new[] { 284f, 290f }) B(metal).Cuboid(new Vector3(x - 0.2f, 0f, z - 0.2f), new Vector3(x + 0.2f, 5f, z + 0.2f), 1f);
                B(metal).Cuboid(new Vector3(x - 0.25f, 5f, 283.6f), new Vector3(x + 0.25f, 5.4f, 290.4f), 1f);
            }
            foreach (float z in new[] { 285f, 286.5f, 288f, 289.5f }) B(rail).Cylinder(new Vector3(1960f, 5.4f, z), 0.35f, 0.01f, 8, false);
            foreach (float z in new[] { 285f, 286.5f, 288f, 289.5f }) B(metal).OrientedBox(new Vector3(2270f, 5.75f, z), new Vector3(620f, 0.6f, 0.6f), Quaternion.identity, 8f);
            var flare = new Vector3(2590f, 0f, 120f);
            B(metal).Cylinder(flare, 1.2f, 62f, 12, true, 2f);
            for (float y = 6f; y < 60f; y += 6f) B(metal).Cuboid(flare + new Vector3(-2.4f, y, -2.4f), flare + new Vector3(2.4f, y + 0.3f, 2.4f), 1f);
            B(aviation).Cuboid(flare + new Vector3(-0.4f, 62f, -0.4f), flare + new Vector3(0.4f, 62.6f, 0.4f), 1f);
            // Terminal fence along the bypass end and the first bend (keeps the land out of play).
            Fence(t, new Vector3(1934f, 0f, 104f), new Vector3(1934f, 0f, 262f));
            Flush("OilTerminal", t, environment, true);
            var flame = Particles("FlareFlame", t, mistMat, 200, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
                main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.6f));
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var e = ps.emission; e.rateOverTime = 40f;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 8f; shape.radius = 0.6f;
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(-4f, -2f); vel.z = new ParticleSystem.MinMaxCurve(-5f, -3f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0.6f);
            });
            flame.GetComponent<ParticleSystemRenderer>().sharedMaterial = Glow("HR_FlareFlame", new Color(1f, 0.5f, 0.15f), AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_NeonRift/Art/District/Textures/District_GlowSoft.png"));
            flame.transform.SetPositionAndRotation(flare + Vector3.up * 63f, Quaternion.Euler(-90f, 0f, 0f));
            var fl = new GameObject("FlareLight") { layer = detail };
            fl.transform.SetParent(t, false);
            fl.transform.position = flare + Vector3.up * 66f;
            var l = fl.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.2f); l.range = 160f; l.intensity = 900f; l.shadows = LightShadows.None;
            report.AppendLine($"oil terminal: {n} tanks with bunds, 620 m pipe rack, flare stack (east of the first bend; fenced)");
        }
    }
}