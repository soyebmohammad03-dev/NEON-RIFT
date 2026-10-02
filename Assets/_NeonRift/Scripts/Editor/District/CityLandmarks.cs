using System.Collections.Generic;
using System.Linq;
using NeonRift.World;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Hand-composed structures that give each district an identity and the player something to navigate by: the
    /// elevated Harbor Skyway, the Lantern Arcade and Kowloon's overhead cables and lantern strings, the S7 car park,
    /// Spire Plaza and its monument, the night market, the harbor container yard with gantry cranes, the Lowtown
    /// construction site with its tower crane, and the expressway median barrier.
    /// </summary>
    public sealed class CityLandmarks
    {
        public const float SkywayHeight = 8f;
        public static readonly Vector3 SkywayGate = new(352f, 0f, 60f);
        /// <summary>Where rival crews wait in the S7 car park while the player breaches the core.</summary>
        public static readonly Vector3[] CarParkSlots = { new(124f, 0f, 236f), new(124f, 0f, 256f) };

        private readonly DistrictKit kit;
        private readonly System.Random rng;
        private readonly int drivable, environment;
        private readonly List<Light> lamps;
        private readonly List<Renderer> screens;
        private Transform root;

        public CityLandmarks(DistrictKit kit, System.Random rng, int drivable, int environment, List<Light> lamps, List<Renderer> screens)
        {
            this.kit = kit;
            this.rng = rng;
            this.drivable = drivable;
            this.environment = environment;
            this.lamps = lamps;
            this.screens = screens;
        }

        private float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public void Build(Transform parent)
        {
            root = new GameObject("Landmarks").transform;
            root.SetParent(parent, false);
            Skyway();
            ExpresswayMedian();
            LanternArcade();
            KowloonCables();
            foreach (var lot in CityLayout.Lots)
                switch (lot.Kind)
                {
                    case LotKind.Parking: CarPark(lot); break;
                    case LotKind.Plaza: Plaza(lot); break;
                    case LotKind.NightMarket: NightMarket(lot); break;
                    case LotKind.ContainerYard: ContainerYard(lot); break;
                    case LotKind.Construction: Construction(lot); break;
                }
        }

        private (Transform group, BlockMeshes meshes, Transform colliders) Group(string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(root, false);
            var c = new GameObject("Colliders").transform;
            c.SetParent(g, false);
            return (g, new BlockMeshes(), c);
        }

        private void Emit(Transform group, BlockMeshes meshes, string name)
        {
            foreach (var r in meshes.Emit(group, name, environment))
                if (kit.Billboards.Contains(r.sharedMaterial)) screens.Add(r);
        }

        private GameObject Col(Transform parent, Vector3 centre, Vector3 size, Quaternion rotation, int layer)
        {
            var go = new GameObject("Collider") { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.AddComponent<BoxCollider>().size = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        private void Lamp(Transform parent, BlockMeshes meshes, DistrictKit.LampKind kind, Vector3 position, Quaternion rotation)
        {
            var go = DistrictKit.Place(kit.StreetLights[(int)kind], parent, position, rotation);
            lamps.Add(go.GetComponentInChildren<Light>(true));
            CityProps.LampHaze(kit, meshes, kind, position + rotation * new Vector3(0f, 7.75f, 2.9f), 7.6f + position.y);
        }

        private void Flood(Transform parent, BlockMeshes meshes, Vector3 head, Vector3 target, Color colour, float intensity, float range)
        {
            meshes.Unshadowed(kit.WallPack).OrientedBox(head, new Vector3(1.4f, 0.5f, 0.4f), Quaternion.LookRotation(target - head), 1f);
            var go = new GameObject("Flood");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(head, Quaternion.LookRotation(target - head));
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = 75f;
            l.innerSpotAngle = 40f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            lamps.Add(l);
        }

        // ---------------- Harbor Skyway ----------------

        /// <summary>
        /// Elevated two-lane road over the Freight Lane median: ramps 345–405 and 680–740, deck at 8 m over Dock Road.
        /// Sloped box colliders for the ramps, guard walls with neon, pillars, deck lamps and SKYWAY signs.
        /// </summary>
        private void Skyway()
        {
            var (g, m, cols) = Group("HarborSkyway");
            const float z = 60f, half = 4.5f, thick = 1.2f, wall = 0.9f;
            var asphalt = m[kit.Asphalt];
            var concrete = m[kit.ConcreteDark];
            var neon = m.Unshadowed(kit.Indicator);
            var under = m.Unshadowed(kit.WallPack);

            void Span(Vector3 a, Vector3 b, bool ramp)
            {
                Vector3 dir = (b - a).normalized;
                float len = Vector3.Distance(a, b);
                var rot = Quaternion.LookRotation(dir, Vector3.up);
                Vector3 n = rot * Vector3.up;
                Vector3 mid = (a + b) * 0.5f;
                asphalt.OrientedBox(mid - n * (thick * 0.5f) + Vector3.up * 0.005f, new Vector3(half * 2f, thick, len), rot, 8f);
                Col(cols, mid - n * (thick * 0.5f), new Vector3(half * 2f, thick, len), rot, drivable);
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 edge = mid + rot * Vector3.right * (s * (half + 0.25f));
                    concrete.OrientedBox(edge + n * (wall * 0.5f), new Vector3(0.5f, wall, len), rot, 2f);
                    neon.OrientedBox(edge + n * (wall + 0.015f) + rot * Vector3.right * (-s * 0.2f), new Vector3(0.06f, 0.03f, len), rot, 1f);
                    Col(cols, edge + n * (wall * 0.5f), new Vector3(0.5f, wall + 0.4f, len), rot, environment);
                    // Ramp side walls down to the ground so the deck reads as solid.
                    if (ramp) concrete.Quad(a + rot * Vector3.right * s * (half + 0.5f), b + rot * Vector3.right * s * (half + 0.5f),
                                            new Vector3(b.x, 0f, b.z) + rot * Vector3.right * s * (half + 0.5f), new Vector3(a.x, 0f, a.z) + rot * Vector3.right * s * (half + 0.5f),
                                            Vector2.zero, new Vector2(len / 4f, 0f), new Vector2(len / 4f, 1f), new Vector2(0f, 1f));
                }
                // Lane markings.
                m.Unshadowed(kit.Marking).OrientedBox(mid + n * 0.012f, new Vector3(0.14f, 0.01f, len - 2f), rot, 1f);
            }

            Span(new Vector3(345f, 0f, z), new Vector3(405f, SkywayHeight, z), true);
            Span(new Vector3(405f, SkywayHeight, z), new Vector3(680f, SkywayHeight, z), false);
            Span(new Vector3(680f, SkywayHeight, z), new Vector3(740f, 0f, z), true);
            // Close the ramp walls' backs (both faces visible from the frontage road).
            for (float x = 420f; x <= 666f; x += 30f)
            {
                if (Mathf.Abs(x - 560f) < 16f) continue;
                concrete.OrientedBox(new Vector3(x, (SkywayHeight - thick) * 0.5f, z), new Vector3(1.6f, SkywayHeight - thick, 2.4f), Quaternion.identity, 2f);
                Col(cols, new Vector3(x, (SkywayHeight - thick) * 0.5f, z), new Vector3(1.6f, SkywayHeight - thick, 2.4f), Quaternion.identity, environment);
                under.OrientedBox(new Vector3(x, SkywayHeight - thick - 0.1f, z - 3f), new Vector3(0.6f, 0.12f, 0.3f), Quaternion.identity, 1f);
                under.OrientedBox(new Vector3(x, SkywayHeight - thick - 0.1f, z + 3f), new Vector3(0.6f, 0.12f, 0.3f), Quaternion.identity, 1f);
            }
            // Deck lamps on alternating sides, and SKYWAY signs on the deck fascia facing the streets below.
            bool left = true;
            for (float x = 420f; x <= 670f; x += 32f, left = !left)
            {
                float sz = left ? z - half - 0.3f : z + half + 0.3f;
                Lamp(g, m, DistrictKit.LampKind.Led, new Vector3(x, SkywayHeight, sz), Quaternion.LookRotation(left ? Vector3.forward : Vector3.back));
            }
            var uv = DistrictTextures.SignRect("SKYWAY", out float aspect);
            foreach (float x in new[] { 470f, 620f })
                foreach (float s in new[] { -1f, 1f })
                    m.Unshadowed(kit.Signs[0]).Panel(new Vector3(x, SkywayHeight - 0.6f, z + s * (half + 0.53f)), Quaternion.LookRotation(new Vector3(0f, 0f, s)), 1.1f * aspect, 1.1f, uv);
            Emit(g, m, "Landmark_Skyway");
        }

        private void ExpresswayMedian()
        {
            var (g, m, cols) = Group("ExpresswayMedian");
            var road = CityLayout.Roads.First(r => r.Name == "East Expressway");
            // Between junctions only (and clear of the checkpoint so its panels can close).
            float[] stops = { -320f, -100f, 60f, 170f, 300f, 480f, 700f };
            for (int i = 0; i + 1 < stops.Length; i++)
            {
                float z0 = stops[i] + (stops[i] == 170f ? 10f : 16f), z1 = stops[i + 1] - (stops[i + 1] == 170f ? 10f : 16f);
                if (z1 - z0 < 10f) continue;
                var c = new Vector3(road.A.x, 0.45f, (z0 + z1) * 0.5f);
                m[kit.Concrete].OrientedBox(c, new Vector3(0.6f, 0.9f, z1 - z0), Quaternion.identity, 2f);
                m.Unshadowed(kit.Reflector).OrientedBox(c + Vector3.up * 0.3f, new Vector3(0.62f, 0.06f, z1 - z0), Quaternion.identity, 1f);
                Col(cols, c, new Vector3(0.6f, 0.9f, z1 - z0), Quaternion.identity, environment);
            }
            Emit(g, m, "Landmark_ExpresswayMedian");
        }

        // ---------------- Kowloon ----------------

        /// <summary>A covered market street: canopy on posts, skylight strips, rows of red lanterns, gateway arches.</summary>
        private void LanternArcade()
        {
            var (g, m, cols) = Group("LanternArcade");
            const float x = -200f, z0 = -60f, z1 = 80f, height = 5.9f, half = 7.6f;
            m[kit.Metal].OrientedBox(new Vector3(x, height + 0.15f, (z0 + z1) * 0.5f), new Vector3(half * 2f, 0.3f, z1 - z0), Quaternion.identity, 2f);
            for (float z = z0 + 2f; z < z1 - 2f; z += 6f)
                m.Unshadowed(kit.LanternWarm).OrientedBox(new Vector3(x, height - 0.02f, z), new Vector3(half * 1.6f, 0.04f, 0.4f), Quaternion.identity, 1f);
            for (float z = z0 + 1f; z < z1; z += 10f)
                foreach (float s in new[] { -1f, 1f })
                {
                    var p = new Vector3(x + s * 5.45f, CityLayout.KerbHeight, z);
                    m[kit.Metal].OrientedBox(p + Vector3.up * (height * 0.5f), new Vector3(0.22f, height, 0.22f), Quaternion.identity, 1f);
                    Col(cols, p + Vector3.up * (height * 0.5f), new Vector3(0.3f, height, 0.3f), Quaternion.identity, environment);
                }
            foreach (float lx in new[] { -2.8f, 2.8f })
                for (float z = z0 + 1.5f; z < z1; z += 3.2f)
                {
                    float drop = Range(0.6f, 1.3f);
                    m[kit.Metal].OrientedBox(new Vector3(x + lx, height - drop * 0.5f, z), new Vector3(0.02f, drop, 0.02f), Quaternion.identity, 1f);
                    m.Unshadowed(kit.Lantern).Cylinder(new Vector3(x + lx, height - drop - 0.55f, z), 0.28f, 0.55f, 10, true);
                }
            // Hanging signs over the street.
            var words = new[] { "RAMEN", "KARAOKE", "PACHINKO", "SUSHI", "TATTOO", "NOODLE BAR" };
            for (int i = 0; i < 6; i++)
            {
                var uv = DistrictTextures.SignRect(words[i], out float aspect);
                float z = z0 + 12f + i * 21f;
                var sign = m.Unshadowed(kit.Signs[i % kit.Signs.Length]);
                sign.Panel(new Vector3(x, height - 1.3f, z + 0.03f), Quaternion.LookRotation(Vector3.back), 0.8f * aspect, 0.8f, uv);
                sign.Panel(new Vector3(x, height - 1.3f, z - 0.03f), Quaternion.LookRotation(Vector3.forward), 0.8f * aspect, 0.8f, uv);
            }
            foreach (float z in new[] { z0, z1 }) Arch(m, cols, new Vector3(x, 0f, z), Quaternion.identity, 8.6f, 8.2f, "KOWLOON MARKET");
            Emit(g, m, "Landmark_LanternArcade");
        }

        /// <summary>Gateway arch: two red posts, double lintel, a neon name plate on both faces.</summary>
        private void Arch(BlockMeshes m, Transform cols, Vector3 centre, Quaternion rot, float half, float height, string text)
        {
            var red = m[kit.Containers[0]];
            foreach (float s in new[] { -1f, 1f })
            {
                var p = centre + rot * new Vector3(s * half, 0f, 0f);
                red.OrientedBox(p + Vector3.up * (height * 0.5f), new Vector3(0.7f, height, 0.7f), rot, 1f);
                Col(cols, p + Vector3.up * (height * 0.5f), new Vector3(0.7f, height, 0.7f), rot, environment);
            }
            red.OrientedBox(centre + Vector3.up * height, new Vector3(half * 2f + 3f, 0.6f, 0.9f), rot, 1f);
            red.OrientedBox(centre + Vector3.up * (height - 1.4f), new Vector3(half * 2f + 0.6f, 0.35f, 0.5f), rot, 1f);
            var uv = DistrictTextures.SignRect(text, out float aspect);
            float h = 0.85f;
            m.Unshadowed(kit.Signs[3]).Panel(centre + Vector3.up * (height - 0.7f) + rot * Vector3.forward * 0.3f, rot, h * aspect, h, uv);
            m.Unshadowed(kit.Signs[3]).Panel(centre + Vector3.up * (height - 0.7f) - rot * Vector3.forward * 0.3f, rot * Quaternion.Euler(0f, 180f, 0f), h * aspect, h, uv);
        }

        /// <summary>Power and data cables, plus lantern strings, sagging across Kowloon's narrow streets.</summary>
        private void KowloonCables()
        {
            var (g, m, _) = Group("KowloonCables");
            var cable = m.Unshadowed(kit.Metal);
            var lantern = m.Unshadowed(kit.Lantern);
            foreach (var road in CityLayout.Roads.Where(r => r.Ground && r.Class is RoadClass.Street or RoadClass.Alley))
            {
                Vector2 a = new(road.A.x, road.A.z), b = new(road.B.x, road.B.z);
                if (CityLayout.ZoneAt((a + b) * 0.5f) != Zone.Kowloon && !(road.Name == "Market Street" || road.Name == "South Street")) continue;
                Vector2 dir = (b - a).normalized;
                Vector2 side = new(dir.y, -dir.x);
                float len = (b - a).magnitude;
                float span = road.HalfWidth + road.Sidewalk - 0.3f;
                for (float s = 15f; s < len - 15f; s += Range(7f, 14f))
                {
                    Vector2 p = a + dir * s;
                    if (CityLayout.ZoneAt(p) != Zone.Kowloon) continue;
                    if (road.Name == "Lantern Street" && p.y > -62f && p.y < 82f) continue;   // the arcade roof covers it
                    float h = Range(6.5f, 10.5f);
                    bool lanterns = rng.NextDouble() < 0.3;
                    Vector3 from = new(p.x - side.x * span, h, p.y - side.y * span), to = new(p.x + side.x * span, h + Range(-0.8f, 0.8f), p.y + side.y * span);
                    Catenary(cable, lanterns ? lantern : null, from, to, Range(0.6f, 1.4f));
                }
            }
            Emit(g, m, "Landmark_KowloonCables");
        }

        private static void Catenary(MeshBuilder cable, MeshBuilder lanterns, Vector3 from, Vector3 to, float sag, int segments = 10)
        {
            Vector3 Point(float t) => Vector3.Lerp(from, to, t) + Vector3.down * (sag * 4f * t * (1f - t));
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = Point(i / (float)segments), b = Point((i + 1) / (float)segments);
                cable.OrientedBox((a + b) * 0.5f, new Vector3(0.035f, 0.035f, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a), 1f);
                if (lanterns != null && i > 0) lanterns.OrientedBox(a + Vector3.down * 0.25f, new Vector3(0.28f, 0.36f, 0.28f), Quaternion.identity, 1f);
            }
        }

        // ---------------- Lots ----------------

        /// <summary>Open-air car park off the Access Road: bays, a wide manoeuvring area, low walls, lamps, sign.</summary>
        private void CarPark(CityLayout.Lot lot)
        {
            var (g, m, cols) = Group("S7CarPark");
            var a = lot.Area;
            var lines = m.Unshadowed(kit.Marking);
            for (float x = a.xMin + 1f; x <= a.xMax - 3f; x += 2.7f)
            {
                lines.Strip(new Vector3(x, 0.013f, a.yMin + 0.5f), new Vector3(x, 0.013f, a.yMin + 5.7f), 0.12f);
                lines.Strip(new Vector3(x, 0.013f, a.yMax - 5.7f), new Vector3(x, 0.013f, a.yMax - 0.5f), 0.12f);
            }
            lines.Strip(new Vector3(a.xMin + 1f, 0.013f, a.yMin + 5.7f), new Vector3(a.xMax - 3f, 0.013f, a.yMin + 5.7f), 0.12f);
            lines.Strip(new Vector3(a.xMin + 1f, 0.013f, a.yMax - 5.7f), new Vector3(a.xMax - 3f, 0.013f, a.yMax - 5.7f), 0.12f);
            // Low walls on the three inner sides.
            var walls = new[]
            {
                (new Vector3(a.center.x, 0.45f, a.yMin - 0.2f), new Vector3(a.width, 0.9f, 0.4f)),
                (new Vector3(a.center.x, 0.45f, a.yMax + 0.2f), new Vector3(a.width, 0.9f, 0.4f)),
                (new Vector3(a.xMin - 0.2f, 0.45f, a.center.y), new Vector3(0.4f, 0.9f, a.height)),
            };
            foreach (var (c, s) in walls)
            {
                m[kit.Concrete].OrientedBox(c, s, Quaternion.identity, 2f);
                m.Unshadowed(kit.NeonStrips[0]).OrientedBox(c + Vector3.up * 0.47f, s + new Vector3(0.02f, -s.y + 0.04f, 0.02f), Quaternion.identity, 1f);
                Col(cols, c, s + Vector3.up * 0.3f, Quaternion.identity, environment);
            }
            // Bollards along the Access Road frontage, with the entrance gap.
            for (float z = a.yMin + 1f; z < a.yMax; z += 2.2f)
            {
                if (Mathf.Abs(z - 246f) < 7f) continue;
                var p = new Vector3(a.xMax - 0.4f, 0f, z);
                m[kit.Metal].Cylinder(p, 0.12f, 0.95f, 8, true);
                m.Unshadowed(kit.Reflector).Cylinder(p + Vector3.up * 0.75f, 0.125f, 0.06f, 8, false);
                Col(cols, p + Vector3.up * 0.47f, new Vector3(0.25f, 0.95f, 0.25f), Quaternion.identity, environment);
            }
            foreach (var p in new[] { new Vector3(a.xMin + 6f, 0f, a.yMin + 18f), new Vector3(a.xMin + 6f, 0f, a.yMax - 18f), new Vector3(a.xMax - 18f, 0f, a.yMin + 7f) })
                Lamp(g, m, DistrictKit.LampKind.Led, p, Quaternion.LookRotation(Vector3.right));
            var uv = DistrictTextures.SignRect("PARKING", out float aspect);
            m[kit.Metal].Cylinder(new Vector3(a.xMax - 0.6f, 0f, 254f), 0.1f, 5f, 8, true);
            m[kit.Roof].OrientedBox(new Vector3(a.xMax - 0.6f, 4.6f, 254f), new Vector3(0.1f, 0.9f, 0.9f * aspect + 0.3f), Quaternion.identity, 1f);
            m.Unshadowed(kit.Signs[0]).Panel(new Vector3(a.xMax - 0.53f, 4.6f, 254f), Quaternion.LookRotation(Vector3.right), 0.7f * aspect, 0.7f, uv);
            Emit(g, m, "Landmark_CarPark");
        }

        /// <summary>Spire Plaza: data-grid paving, the 72 m Spire monument with a reflecting pool, trees, benches, lamps.</summary>
        private void Plaza(CityLayout.Lot lot)
        {
            var (g, m, cols) = Group("SpirePlaza");
            var a = lot.Area;
            float y = CityLayout.KerbHeight + 0.006f;
            m.Unshadowed(kit.Plaza).Ground(new Vector3(a.xMin, y, a.yMin), new Vector3(a.xMax, y, a.yMax), 4f);
            Vector3 c = new(a.center.x, CityLayout.KerbHeight, a.center.y);
            // Reflecting pool.
            m[kit.ConcreteDark].Ring(c + Vector3.up * 0.45f, 13f, 14f, 48);
            m[kit.ConcreteDark].Cylinder(c, 14f, 0.45f, 48, false);
            m[kit.Glass].Ring(c + Vector3.up * 0.3f, 3.2f, 13f, 48);
            Col(cols, c + Vector3.up * 0.25f, new Vector3(27f, 0.5f, 27f), Quaternion.identity, environment);
            // The monument: tapered glass needle with holo edges, crown ring and a beacon.
            const float h = 72f;
            var glass = m[kit.Glass];
            var holo = m.Unshadowed(kit.Holo);
            m[kit.ConcreteDark].OrientedBox(c + Vector3.up * 1.2f, new Vector3(6.4f, 2.4f, 6.4f), Quaternion.Euler(0f, 45f, 0f), 2f);
            for (int i = 0; i < 4; i++)
            {
                var d0 = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward;
                var d1 = Quaternion.Euler(0f, 135f + i * 90f, 0f) * Vector3.forward;
                Vector3 b0 = c + Vector3.up * 2.4f + d0 * 3f, b1 = c + Vector3.up * 2.4f + d1 * 3f;
                Vector3 t0 = c + Vector3.up * h + d0 * 0.35f, t1 = c + Vector3.up * h + d1 * 0.35f;
                glass.Quad(b0, b1, t1, t0, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 8), new Vector2(0, 8));
                holo.Quad(b0, b0 + Vector3.up * 0.01f + (b1 - b0).normalized * 0.18f, t0 + (t1 - t0).normalized * 0.04f, t0, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            foreach (float ringY in new[] { 24f, 44f, 60f })
                holo.Torus(c + Vector3.up * ringY, Mathf.Lerp(3.2f, 0.6f, ringY / h) + 1.2f, 0.12f, 32, 6);
            m.Unshadowed(kit.AviationRed).OrientedBox(c + Vector3.up * (h + 0.3f), new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, 1f);
            Col(cols, c + Vector3.up * 20f, new Vector3(5f, 40f, 5f), Quaternion.Euler(0f, 45f, 0f), environment);
            var uv = DistrictTextures.SignRect("SPIRE", out float aspect);
            for (int i = 0; i < 4; i++)
            {
                var r = Quaternion.Euler(0f, i * 90f, 0f);
                m.Unshadowed(kit.Signs[0]).Panel(c + Vector3.up * 1.5f + r * Vector3.forward * 4.55f, r, 0.8f * aspect, 0.8f, uv);
            }
            // Trees, benches and lamps around the pool.
            for (int i = 0; i < 12; i++)
            {
                float ang = i / 12f * Mathf.PI * 2f;
                var p = c + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 24f;
                var rot = Quaternion.LookRotation(c - p);
                if (i % 2 == 0)
                {
                    m[kit.ConcreteDark].OrientedBox(p + Vector3.up * 0.3f, new Vector3(1.8f, 0.6f, 1.8f), rot, 1f);
                    m[kit.Metal].Cylinder(p + Vector3.up * 0.6f, 0.12f, 3f, 6, false);
                    m[kit.Foliage].OrientedBox(p + Vector3.up * 4.2f, new Vector3(2.6f, 2.2f, 2.6f), rot * Quaternion.Euler(0f, 45f, 0f), 1f);
                    Col(cols, p + Vector3.up * 0.3f, new Vector3(1.8f, 0.6f, 1.8f), rot, environment);
                }
                else
                {
                    m[kit.Metal].OrientedBox(p + Vector3.up * 0.45f, new Vector3(2.4f, 0.1f, 0.6f), rot * Quaternion.Euler(0f, 90f, 0f), 1f);
                    Col(cols, p + Vector3.up * 0.4f, new Vector3(0.6f, 0.8f, 2.4f), rot, environment);
                }
            }
            foreach (var p in new[] { new Vector3(a.xMin + 6f, 0.15f, a.yMin + 6f), new Vector3(a.xMax - 6f, 0.15f, a.yMin + 6f),
                                      new Vector3(a.xMin + 6f, 0.15f, a.yMax - 6f), new Vector3(a.xMax - 6f, 0.15f, a.yMax - 6f) })
                Lamp(g, m, DistrictKit.LampKind.Led, p, Quaternion.LookRotation(c - p));
            Emit(g, m, "Landmark_SpirePlaza");
        }

        /// <summary>Rows of stalls under coloured canopies, lantern strings overhead, a gateway arch.</summary>
        private void NightMarket(CityLayout.Lot lot)
        {
            var (g, m, cols) = Group("NightMarket");
            var a = lot.Area;
            float y = CityLayout.KerbHeight;
            for (float z = a.yMin + 8f; z < a.yMax - 6f; z += 14f)
                for (float x = a.xMin + 6f; x < a.xMax - 4f; x += 4.2f)
                {
                    if (rng.NextDouble() < 0.15) continue;
                    foreach (float face in new[] { -1f, 1f })
                    {
                        var rot = Quaternion.LookRotation(new Vector3(0f, 0f, face));
                        var p = new Vector3(x, y, z - face * 2.2f);
                        m[kit.Metal].OrientedBox(p + Vector3.up * 0.5f, new Vector3(3.6f, 1f, 1.2f), rot, 1f);
                        m[kit.Containers[rng.Next(kit.Containers.Length)]].OrientedBox(p + Vector3.up * 2.55f + rot * Vector3.forward * 0.4f,
                                                                                         new Vector3(3.9f, 0.08f, 2.4f), rot * Quaternion.Euler(-10f, 0f, 0f), 1f);
                        m.Unshadowed(kit.NeonStrips[rng.Next(kit.NeonStrips.Length)]).OrientedBox(p + Vector3.up * 2.42f + rot * Vector3.forward * 1.55f,
                                                                                                  new Vector3(3.9f, 0.06f, 0.06f), rot, 1f);
                        m.Unshadowed(kit.LanternWarm).OrientedBox(p + Vector3.up * 1.05f + rot * Vector3.forward * 0.1f, new Vector3(3.4f, 0.06f, 0.9f), rot, 1f);
                        foreach (float s in new[] { -1.8f, 1.8f })
                            m[kit.Metal].OrientedBox(p + rot * new Vector3(s, 1.25f, -0.5f), new Vector3(0.06f, 2.5f, 0.06f), rot, 1f);
                        Col(cols, p + Vector3.up * 0.5f, new Vector3(3.6f, 1f, 1.2f), rot, environment);
                    }
                }
            for (float z = a.yMin + 1f; z < a.yMax; z += 7f)
                Catenary(m.Unshadowed(kit.Metal), m.Unshadowed(kit.Lantern), new Vector3(a.xMin, 6f, z), new Vector3(a.xMax, 6.5f, z + Range(-3f, 3f)), 1.2f, 24);
            Arch(m, cols, new Vector3(a.center.x, y, a.yMin - 1f), Quaternion.identity, 8f, 7.6f, "NIGHT MARKET");
            Emit(g, m, "Landmark_NightMarket");
        }

        /// <summary>Stacked containers in bays, two gantry cranes with aviation lights, floodlight masts, a fence.</summary>
        private void ContainerYard(CityLayout.Lot lot)
        {
            var (g, m, cols) = Group("ContainerYard");
            var a = lot.Area;
            float y = CityLayout.KerbHeight;
            const float length = 12.2f, height = 2.6f, width = 2.44f;
            for (float x = a.xMin + 10f; x + length < a.xMax - 8f; x += length + 1.2f)
                for (float z = a.yMin + 10f; z < a.yMax - 10f; z += width + 0.3f)
                {
                    if (Mathf.Repeat((z - a.yMin) / 40f, 1f) > 0.75f) continue;     // aisles
                    int stack = rng.Next(0, 5);
                    if (stack == 0) continue;
                    for (int k = 0; k < stack; k++)
                        m[kit.Containers[rng.Next(kit.Containers.Length)]].OrientedBox(new Vector3(x + length * 0.5f, y + height * (k + 0.5f), z), new Vector3(length, height - 0.04f, width), Quaternion.identity, 4f);
                    Col(cols, new Vector3(x + length * 0.5f, y + height * stack * 0.5f, z), new Vector3(length, height * stack, width), Quaternion.identity, environment);
                }
            foreach (float cx in new[] { a.xMin + 50f, a.xMax - 55f })
                GantryCrane(m, cols, new Vector3(cx, y, a.center.y), 34f, 36f);
            foreach (var p in new[] { new Vector3(a.xMin + 2f, y, a.yMin + 2f), new Vector3(a.xMax - 2f, y, a.yMin + 2f), new Vector3(a.xMin + 2f, y, a.yMax - 2f), new Vector3(a.xMax - 2f, y, a.yMax - 2f) })
            {
                m[kit.Metal].Cylinder(p, 0.35f, 22f, 10, true);
                Col(cols, p + Vector3.up * 11f, new Vector3(0.7f, 22f, 0.7f), Quaternion.identity, environment);
                Flood(g, m, p + Vector3.up * 22f, new Vector3(a.center.x, 0f, a.center.y), DistrictKit.LampColours[1], 900f, 90f);
            }
            Fence(m, cols, a, gapAt: new Vector2(a.xMin, a.center.y));
            Emit(g, m, "Landmark_ContainerYard");
        }

        private void GantryCrane(BlockMeshes m, Transform cols, Vector3 c, float span, float height)
        {
            var yellow = m[kit.Containers[4]];
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var p = c + new Vector3(sx * 8f, 0f, sz * span * 0.5f);
                    yellow.OrientedBox(p + Vector3.up * (height * 0.5f), new Vector3(1.2f, height, 1.2f), Quaternion.identity, 2f);
                    Col(cols, p + Vector3.up * (height * 0.5f), new Vector3(1.2f, height, 1.2f), Quaternion.identity, environment);
                }
            foreach (float sx in new[] { -1f, 1f })
                yellow.OrientedBox(c + new Vector3(sx * 8f, height, 0f), new Vector3(1.6f, 2f, span + 14f), Quaternion.identity, 2f);
            yellow.OrientedBox(c + new Vector3(0f, height + 1.5f, span * 0.15f), new Vector3(18f, 1.2f, 4f), Quaternion.identity, 2f);   // trolley
            m[kit.Metal].OrientedBox(c + new Vector3(0f, height - 3f, span * 0.15f), new Vector3(3f, 2.5f, 3f), Quaternion.identity, 1f); // cab
            m.Unshadowed(kit.WallPack).OrientedBox(c + new Vector3(0f, height - 3f, span * 0.15f + 1.52f), new Vector3(2.2f, 1f, 0.04f), Quaternion.identity, 1f);
            foreach (float sz in new[] { -1f, 1f })
                foreach (float sx in new[] { -1f, 1f })
                    m.Unshadowed(kit.AviationRed).OrientedBox(c + new Vector3(sx * 8f, height + 1.2f, sz * (span * 0.5f + 7f)), new Vector3(0.4f, 0.4f, 0.4f), Quaternion.identity, 1f);
            for (float z = -span * 0.5f; z <= span * 0.5f; z += 4f)
                m[kit.Metal].OrientedBox(c + new Vector3(0f, height - 0.6f, z), new Vector3(16f, 0.12f, 0.12f), Quaternion.identity, 1f);
        }

        private void Fence(BlockMeshes m, Transform cols, Rect a, Vector2 gapAt)
        {
            var metal = m[kit.Metal];
            float y = CityLayout.KerbHeight;
            Vector3[] corners = { new(a.xMin, y, a.yMin), new(a.xMax, y, a.yMin), new(a.xMax, y, a.yMax), new(a.xMin, y, a.yMax) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 p0 = corners[i], p1 = corners[(i + 1) % 4];
                float len = Vector3.Distance(p0, p1);
                Vector3 dir = (p1 - p0) / len;
                var rot = Quaternion.LookRotation(dir);
                float gapS = Vector3.Dot(new Vector3(gapAt.x, y, gapAt.y) - p0, dir);
                bool hasGap = CityLayout.DistanceToSegment(gapAt, new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z)) < 1f;
                for (float s = 0f; s < len; s += 3f)
                {
                    if (hasGap && Mathf.Abs(s - gapS) < 6f) continue;
                    float seg = Mathf.Min(3f, len - s);
                    Vector3 mid = p0 + dir * (s + seg * 0.5f);
                    metal.Cylinder(p0 + dir * s, 0.05f, 2.6f, 6, true);
                    metal.OrientedBox(mid + Vector3.up * 2.5f, new Vector3(0.05f, 0.05f, seg), rot, 1f);
                    metal.OrientedBox(mid + Vector3.up * 1.2f, new Vector3(0.04f, 0.04f, seg), rot, 1f);
                    m.Unshadowed(kit.Glass).OrientedBox(mid + Vector3.up * 1.3f, new Vector3(0.01f, 2.4f, seg), rot, 1f);
                    Col(cols, mid + Vector3.up * 1.3f, new Vector3(0.1f, 2.6f, seg), rot, environment);
                }
            }
        }

        /// <summary>Hoarding with signs and blinkers, a steel skeleton with scaffolding, a 70 m tower crane, site huts, work lights.</summary>
        private void Construction(CityLayout.Lot lot)
        {
            var (g, m, cols) = Group("ConstructionSite");
            var a = lot.Area;
            float y = CityLayout.KerbHeight;
            m.Unshadowed(kit.ConcreteDark).Ground(new Vector3(a.xMin, y + 0.005f, a.yMin), new Vector3(a.xMax, y + 0.005f, a.yMax), 6f);
            // Hoarding on all four sides.
            var uv = DistrictTextures.SignRect("CONSTRUCTION", out float aspect);
            Vector3[] corners = { new(a.xMin, y, a.yMin), new(a.xMax, y, a.yMin), new(a.xMax, y, a.yMax), new(a.xMin, y, a.yMax) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 p0 = corners[i], p1 = corners[(i + 1) % 4];
                float len = Vector3.Distance(p0, p1);
                Vector3 dir = (p1 - p0) / len;
                var rot = Quaternion.LookRotation(dir);
                var outward = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up));
                Vector3 mid = (p0 + p1) * 0.5f;
                m[kit.RollerDoor].OrientedBox(mid + Vector3.up * 1.2f, new Vector3(0.1f, 2.4f, len), rot, 4f);
                m.Unshadowed(kit.MarkingYellow).OrientedBox(mid + Vector3.up * 2.42f, new Vector3(0.12f, 0.08f, len), rot, 1f);
                Col(cols, mid + Vector3.up * 1.2f, new Vector3(0.15f, 2.4f, len), rot, environment);
                for (float s = 12f; s < len - 8f; s += 26f)
                {
                    Vector3 p = p0 + dir * s + Vector3.up * 1.4f + outward * Vector3.forward * 0.07f;
                    m.Unshadowed(kit.Signs[3]).Panel(p, outward, 0.55f * aspect, 0.55f, uv);
                    m.Unshadowed(kit.AviationRed).OrientedBox(p + Vector3.up * 1.15f, new Vector3(0.15f, 0.15f, 0.15f), Quaternion.identity, 1f);
                }
            }
            // Steel skeleton, lower floors slabbed, scaffold on one face.
            Vector3 b = new(a.center.x + 20f, y, a.center.y + 10f);
            var steel = m[kit.Metal];
            for (int ix = 0; ix <= 4; ix++)
                for (int iz = 0; iz <= 3; iz++)
                {
                    var p = b + new Vector3((ix - 2f) * 8f, 0f, (iz - 1.5f) * 8f);
                    float hgt = ix < 3 ? 24f : 16f;
                    steel.OrientedBox(p + Vector3.up * (hgt * 0.5f), new Vector3(0.45f, hgt, 0.45f), Quaternion.identity, 1f);
                    Col(cols, p + Vector3.up * (hgt * 0.5f), new Vector3(0.5f, hgt, 0.5f), Quaternion.identity, environment);
                }
            for (int floor = 1; floor <= 6; floor++)
            {
                float fy = floor * 4f;
                float w = floor <= 4 ? 32f : 16f;
                float ox = floor <= 4 ? 0f : -8f;
                steel.OrientedBox(b + new Vector3(ox, fy, -12f), new Vector3(w, 0.4f, 0.3f), Quaternion.identity, 1f);
                steel.OrientedBox(b + new Vector3(ox, fy, 12f), new Vector3(w, 0.4f, 0.3f), Quaternion.identity, 1f);
                if (floor <= 3) m[kit.ConcreteDark].OrientedBox(b + new Vector3(0f, fy, 0f), new Vector3(32.5f, 0.3f, 24.5f), Quaternion.identity, 4f);
            }
            for (float sy = 2f; sy < 22f; sy += 2f)
                steel.OrientedBox(b + new Vector3(0f, sy, -13.2f), new Vector3(32f, 0.06f, 0.06f), Quaternion.identity, 1f);
            for (float sx = -16f; sx <= 16f; sx += 2f)
                steel.OrientedBox(b + new Vector3(sx, 11f, -13.2f), new Vector3(0.06f, 22f, 0.06f), Quaternion.identity, 1f);
            TowerCrane(m, cols, new Vector3(a.xMin + 40f, y, a.yMin + 45f), 70f, 52f);
            // Site huts (lit windows), materials, cones, work lights.
            for (int i = 0; i < 2; i++)
            {
                var p = new Vector3(a.xMin + 14f, y, a.yMax - 12f - i * 3.2f);
                m[kit.Containers[3]].OrientedBox(p + Vector3.up * 1.3f, new Vector3(6f, 2.6f, 2.44f), Quaternion.identity, 4f);
                m.Unshadowed(kit.WallPack).OrientedBox(p + Vector3.up * 1.6f + Vector3.forward * 1.23f, new Vector3(2.5f, 0.9f, 0.02f), Quaternion.identity, 1f);
                Col(cols, p + Vector3.up * 1.3f, new Vector3(6f, 2.6f, 2.44f), Quaternion.identity, environment);
            }
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(a.xMin + Range(20f, a.width - 20f), y, a.yMin + Range(10f, 40f));
                for (int k = 0; k < 5; k++)
                    m[kit.ConcreteDark].OrientedBox(p + new Vector3(0f, 0.3f + k * 0.62f * 0.86f, 0f), new Vector3(6f, 0.6f, 0.6f), Quaternion.Euler(0f, 0f, 0f), 2f);
                Col(cols, p + Vector3.up * 1.5f, new Vector3(6f, 3f, 0.8f), Quaternion.identity, environment);
            }
            foreach (var p in new[] { new Vector3(a.xMin + 30f, y, a.yMax - 30f), new Vector3(a.xMax - 30f, y, a.yMin + 30f), new Vector3(a.center.x, y, a.yMax - 8f) })
            {
                m[kit.Metal].OrientedBox(p + Vector3.up * 3f, new Vector3(0.2f, 6f, 0.2f), Quaternion.identity, 1f);
                Flood(g, m, p + Vector3.up * 6.2f, b, DistrictKit.LampColours[0], 320f, 60f);
            }
            Emit(g, m, "Landmark_Construction");
        }

        private void TowerCrane(BlockMeshes m, Transform cols, Vector3 c, float height, float jib)
        {
            var yellow = m[kit.Containers[4]];
            foreach (var corner in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f) })
                yellow.OrientedBox(c + corner * 1f + Vector3.up * (height * 0.5f), new Vector3(0.18f, height, 0.18f), Quaternion.identity, 1f);
            for (float h = 0f; h < height; h += 4f)
            {
                yellow.OrientedBox(c + new Vector3(0f, h + 2f, -1f), new Vector3(2.85f, 0.1f, 0.1f), Quaternion.Euler(0f, 0f, 54f), 1f);
                yellow.OrientedBox(c + new Vector3(0f, h + 2f, 1f), new Vector3(2.85f, 0.1f, 0.1f), Quaternion.Euler(0f, 0f, -54f), 1f);
                yellow.OrientedBox(c + new Vector3(-1f, h + 2f, 0f), new Vector3(0.1f, 0.1f, 2.85f), Quaternion.Euler(54f, 0f, 0f), 1f);
                yellow.OrientedBox(c + new Vector3(1f, h + 2f, 0f), new Vector3(0.1f, 0.1f, 2.85f), Quaternion.Euler(-54f, 0f, 0f), 1f);
            }
            Col(cols, c + Vector3.up * (height * 0.5f), new Vector3(2.2f, height, 2.2f), Quaternion.identity, environment);
            var dir = Quaternion.Euler(0f, 35f, 0f);
            yellow.OrientedBox(c + Vector3.up * (height + 1f) + dir * Vector3.forward * (jib * 0.5f - 4f), new Vector3(1.2f, 1.6f, jib), dir, 2f);
            yellow.OrientedBox(c + Vector3.up * (height + 1f) - dir * Vector3.forward * 9f, new Vector3(1.4f, 1.2f, 16f), dir, 2f);
            m[kit.ConcreteDark].OrientedBox(c + Vector3.up * (height - 0.5f) - dir * Vector3.forward * 14f, new Vector3(2.4f, 2.6f, 4f), dir, 1f);
            m[kit.Metal].OrientedBox(c + Vector3.up * (height + 4f), new Vector3(0.3f, 6f, 0.3f), Quaternion.identity, 1f);
            m[kit.Metal].OrientedBox(c + Vector3.up * (height - 1.8f) + dir * Vector3.forward * 2.4f, new Vector3(2f, 2f, 2.4f), dir, 1f);
            m.Unshadowed(kit.WallPack).OrientedBox(c + Vector3.up * (height - 1.8f) + dir * Vector3.forward * 3.62f, new Vector3(1.6f, 1f, 0.02f), dir, 1f);
            Vector3 hook = c + dir * Vector3.forward * (jib * 0.6f);
            m[kit.Metal].OrientedBox(new Vector3(hook.x, height * 0.6f + 0.5f, hook.z), new Vector3(0.04f, height * 0.8f, 0.04f), Quaternion.identity, 1f);
            foreach (var p in new[] { c + Vector3.up * (height + 7.2f), c + Vector3.up * (height + 1.9f) + dir * Vector3.forward * (jib - 4.5f),
                                      c + Vector3.up * (height + 1.7f) - dir * Vector3.forward * 16.5f })
                m.Unshadowed(kit.AviationRed).OrientedBox(p, new Vector3(0.45f, 0.45f, 0.45f), Quaternion.identity, 1f);
        }
    }
}
