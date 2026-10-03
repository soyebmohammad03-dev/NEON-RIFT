using System.Collections.Generic;
using System.Linq;
using NeonRift.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Builds the Night Run city from <see cref="CityLayout"/>: one asphalt ground plane, raised pavement blocks with
    /// kerbs, zone-styled podium street walls (shopfronts, lit floors, neon, signage, rooftop plant), hero and generated
    /// towers from the audited building catalog, lamps, street furniture and traffic infrastructure
    /// (<see cref="CityProps"/>), landmarks and special lots (<see cref="CityLandmarks"/>), road markings and baked
    /// reflection probes. Every block is combined into one mesh per material. Deterministic (seeded), re-runnable.
    /// </summary>
    public sealed class NightRunDistrict
    {
        public const float KerbHeight = CityLayout.KerbHeight;

        /// <summary>How a zone's street walls, lamps and towers look.</summary>
        public struct ZoneStyle
        {
            public Vector2 Height, TallHeight;
            public float TallChance;
            public int[] Facades;
            public float ShopChance, SignChance, BladeChance, BillboardChance, NeonChance;
            public Vector2 Segment;
            public float MaxDepth;
            public DistrictKit.LampKind Lamp;
            public float LampSpacing;
            public string[] Towers;
            public float TowerDensity, TowerMaxHeight;
            public string[] Words;
            public bool KerbNeon, Industrial, RoofTanks, FacadeClutter;
        }

        private static readonly string[] S7Words = { "RAMEN", "HOTEL", "ARCADE", "NOODLE BAR", "OPEN 24H", "CLUB VOLT", "KAIJU", "NEON RIFT", "CYBERWARE" };
        private static readonly string[] KowloonWords = { "RAMEN", "NOODLE BAR", "PHARMACY", "PACHINKO", "KARAOKE", "SUSHI", "TATTOO", "BAR", "OPEN 24H", "HOTEL", "KAIJU" };
        private static readonly string[] SpireWords = { "SPIRE", "HOTEL", "CYBERWARE", "NEON RIFT", "24/7" };
        private static readonly string[] HarborWords = { "DOCK 4", "CARGO", "ZONE B", "DANGER" };
        private static readonly string[] LowtownWords = { "PHARMACY", "BAR", "24/7", "RAMEN", "KARAOKE", "TATTOO", "HOTEL" };

        public static ZoneStyle Style(Zone zone) => zone switch
        {
            Zone.Spire => new ZoneStyle
            {
                Height = new(11f, 22f), TallHeight = new(30f, 58f), TallChance = 0.35f, Facades = new[] { 3, 3, 1 }, ShopChance = 0.6f,
                SignChance = 0.25f, BladeChance = 0.05f, BillboardChance = 0.22f, NeonChance = 0.5f, Segment = new(30f, 60f), MaxDepth = 28f,
                Lamp = DistrictKit.LampKind.Led, LampSpacing = 36f, TowerDensity = 0.85f, TowerMaxHeight = 320f, Words = SpireWords, KerbNeon = true,
                Towers = new[] { "asian_night_b1_009", "asian_night_b1_008", "asian_night_b2", "asian_night_b2_008", "asian_night_b1", "asian_night_b1_004", "asian_night_b2_001" }
            },
            Zone.Kowloon => new ZoneStyle
            {
                Height = new(6f, 13f), TallHeight = new(16f, 26f), TallChance = 0.14f, Facades = new[] { 2, 5, 0, 5 }, ShopChance = 1f,
                SignChance = 0.85f, BladeChance = 0.6f, BillboardChance = 0.18f, NeonChance = 1f, Segment = new(11f, 24f), MaxDepth = 18f,
                Lamp = DistrictKit.LampKind.Warm, LampSpacing = 30f, TowerDensity = 0.55f, TowerMaxHeight = 110f, Words = KowloonWords,
                RoofTanks = true, FacadeClutter = true,
                Towers = new[] { "asian_night_b2_006", "asian_night_b2_004", "asian_night_b1_003", "asian_night_b2_001", "asian_night_b2_005" }
            },
            Zone.Harbor => new ZoneStyle
            {
                Height = new(9f, 15f), TallHeight = new(16f, 22f), TallChance = 0.08f, Facades = new[] { 4 }, ShopChance = 0f,
                SignChance = 0.18f, BladeChance = 0f, BillboardChance = 0.05f, NeonChance = 0.12f, Segment = new(40f, 70f), MaxDepth = 40f,
                Lamp = DistrictKit.LampKind.Sodium, LampSpacing = 44f, TowerDensity = 0.12f, TowerMaxHeight = 100f, Words = HarborWords, Industrial = true,
                Towers = new[] { "asian_night_b2_004", "asian_night_b1_003" }
            },
            Zone.Lowtown => new ZoneStyle
            {
                Height = new(9f, 18f), TallHeight = new(20f, 32f), TallChance = 0.2f, Facades = new[] { 5, 0, 2 }, ShopChance = 0.7f,
                SignChance = 0.4f, BladeChance = 0.3f, BillboardChance = 0.1f, NeonChance = 0.55f, Segment = new(18f, 36f), MaxDepth = 22f,
                Lamp = DistrictKit.LampKind.Sodium, LampSpacing = 38f, TowerDensity = 0.45f, TowerMaxHeight = 130f, Words = LowtownWords,
                RoofTanks = true, FacadeClutter = true,
                Towers = new[] { "asian_night_b1_003", "asian_night_b2_001", "asian_night_b2_005", "asian_night_b2_006", "asian_night_b1_004" }
            },
            Zone.Outer => new ZoneStyle
            {
                Height = new(14f, 28f), TallHeight = new(30f, 70f), TallChance = 0.4f, Facades = new[] { 0, 1, 2, 3, 5 }, ShopChance = 0.6f,
                SignChance = 0.3f, BladeChance = 0.2f, BillboardChance = 0.2f, NeonChance = 0.6f, Segment = new(30f, 60f), MaxDepth = 30f,
                Lamp = DistrictKit.LampKind.Led, LampSpacing = 44f, TowerDensity = 0.9f, TowerMaxHeight = 400f, Words = S7Words,
                Towers = new[] { "asian_night_b1_001", "asian_night_b1_002", "asian_night_b1_006", "asian_night_b1_007", "asian_night_b2_002",
                                 "asian_night_b2_003", "asian_night_b2_007", "asian_night_b1_005", "asian_night_b1_008", "asian_night_b2" }
            },
            _ => new ZoneStyle
            {
                Height = new(7.5f, 16f), TallHeight = new(22f, 36f), TallChance = 0.16f, Facades = new[] { 0, 1, 2 }, ShopChance = 1f,
                SignChance = 0.55f, BladeChance = 0.3f, BillboardChance = 0.14f, NeonChance = 1f, Segment = new(22f, 46f), MaxDepth = 24f,
                Lamp = DistrictKit.LampKind.Led, LampSpacing = 40f, TowerDensity = 0f, TowerMaxHeight = 0f, Words = S7Words, KerbNeon = true,
                Towers = new string[0]
            }
        };

        private readonly DistrictKit kit;
        private readonly BuildingCatalog catalog;
        private readonly System.Random rng = new(2077);
        private readonly List<Rect> heroFootprints = new();
        private readonly List<Rect> towerFootprints = new();
        private readonly int drivable, environment;
        private CityLayout.Decomposition layout;

        public Transform Root { get; private set; }
        public TowerDressing Dressing { get; private set; }
        public Dictionary<string, Renderer> SecurityStrips { get; } = new();
        public Dictionary<string, Vector3> BlockCentres { get; } = new();
        /// <summary>Every street/flood lamp light (disabled; the LightBudget switches the nearest on).</summary>
        public List<Light> Lamps { get; } = new();
        /// <summary>Billboards and screens that switch to the warning look in a lockdown.</summary>
        public List<Renderer> Screens { get; } = new();
        public CityProps Props { get; private set; }
        public CityLandmarks Landmarks { get; private set; }
        public int RealtimeLights => Lamps.Count;
        public int Podiums { get; private set; }
        public int Towers { get; private set; }

        public NightRunDistrict(DistrictKit kit, BuildingCatalog catalog)
        {
            this.kit = kit;
            this.catalog = catalog;
            drivable = LayerMask.NameToLayer("Drivable");
            environment = LayerMask.NameToLayer("Environment");
        }

        public void Build(Transform parent)
        {
            Root = new GameObject("District").transform;
            Root.SetParent(parent, false);
            layout = CityLayout.Decompose();
            Props = new CityProps(kit, rng, environment, Lamps);
            Landmarks = new CityLandmarks(kit, rng, drivable, environment, Lamps, Screens);
            Dressing = new TowerDressing(kit);
            BuildGround();
            BuildMarkings();
            PlaceHeroes();
            int index = 0;
            foreach (var pair in layout.Blocks.OrderBy(p => p.Value.Min(c => c.Grid.xMin)).ThenBy(p => p.Value.Min(c => c.Grid.yMin)))
                BuildBlock(pair.Value, index++);
            Dressing.Emit(Root, environment);
            Props.BuildIntersections(Root, layout.Nodes);
            Landmarks.Build(Root);
            SkylineBackdrop.Build(Root, kit, environment);
            BuildNavigationSigns();
            BuildReflectionProbes();
        }

        // ---------------- Ground and roads ----------------

        private void BuildGround()
        {
            var w = CityLayout.WorldBounds;
            var ground = new MeshBuilder();
            ground.Ground(new Vector3(w.xMin - 600f, 0f, w.yMin - 600f), new Vector3(w.xMax + 600f, 0f, w.yMax + 600f), 8f);
            var go = DistrictKit.Renderer("Ground_Asphalt", Root, DistrictKit.SaveMesh(ground, "District_Ground"), kit.Asphalt, drivable);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(w.center.x, -0.5f, w.center.y);
            col.size = new Vector3(w.width + 1200f, 1f, w.height + 1200f);
        }

        private bool IsNode(Vector2 p) => layout.Nodes.Any(n => n.y < 0.5f && (new Vector2(n.x, n.z) - p).sqrMagnitude < 0.25f);

        private static float TrimAt(Vector2 node, CityLayout.Road self)
        {
            float r = 0f;
            foreach (var road in CityLayout.Roads)
            {
                if (!road.Ground || road.IsVertical == self.IsVertical) continue;
                if (CityLayout.DistanceToSegment(node, Flat(road.A), Flat(road.B)) < 0.5f) r = Mathf.Max(r, road.HalfWidth);
            }
            return r + 2f;
        }

        private static Vector2 Flat(Vector3 p) => new(p.x, p.z);

        private void BuildMarkings()
        {
            var meshes = new BlockMeshes();
            var lines = meshes.Unshadowed(kit.Marking);
            var yellow = meshes.Unshadowed(kit.MarkingYellow);
            var manholes = meshes.Unshadowed(kit.Metal);
            var steam = meshes.Unshadowed(kit.Steam);
            const float y = 0.012f;
            foreach (var road in CityLayout.Roads)
            {
                if (!road.Ground || road.Class == RoadClass.Tunnel) continue;
                Vector2 a = Flat(road.A), b = Flat(road.B);
                Vector2 dir = (b - a).normalized;
                Vector2 side = new(dir.y, -dir.x);     // right of travel A→B
                var cuts = layout.Nodes.Where(n => n.y < 0.5f && CityLayout.DistanceToSegment(Flat(n), a, b) < 0.5f)
                                       .Select(n => Vector2.Dot(Flat(n) - a, dir)).ToList();
                cuts.Add(0f);
                cuts.Add((b - a).magnitude);
                cuts = cuts.Distinct().OrderBy(c => c).ToList();
                for (int i = 0; i < cuts.Count - 1; i++)
                {
                    float s0 = cuts[i], s1 = cuts[i + 1];
                    Vector2 p0 = a + dir * s0, p1 = a + dir * s1;
                    // Only trim where a crossing road actually meets this one (blocker split nodes are not junctions).
                    float t0 = TrimAt(p0, road), t1 = TrimAt(p1, road);
                    bool j0 = t0 > 2.01f, j1 = t1 > 2.01f;
                    if (j0) s0 += t0;
                    if (j1) s1 -= t1;
                    if (s1 - s0 < 4f) continue;
                    Vector3 W(float s, float lateral) { var p = a + dir * s + side * lateral; return new Vector3(p.x, y, p.y); }

                    float hw = road.HalfWidth;
                    lines.Strip(W(s0, -(hw - 0.35f)), W(s1, -(hw - 0.35f)), 0.15f);
                    lines.Strip(W(s0, hw - 0.35f), W(s1, hw - 0.35f), 0.15f);
                    if (road.Median > 0f)
                    {
                        // Hatched median under the skyway, solid edges.
                        foreach (float sg in new[] { -1f, 1f }) yellow.Strip(W(s0, sg * road.Median), W(s1, sg * road.Median), 0.15f);
                        for (float s = s0 + 2f; s + 2f < s1; s += 6f) yellow.Strip(W(s, -road.Median + 0.4f), W(s + 3f, road.Median - 0.4f), 0.3f);
                    }
                    else if (road.Lanes >= 2)
                    {
                        lines.Strip(W(s0, -0.18f), W(s1, -0.18f), 0.12f);
                        lines.Strip(W(s0, 0.18f), W(s1, 0.18f), 0.12f);
                        float lane = hw / road.Lanes;
                        for (int l = 1; l < road.Lanes; l++) { Dashes(lines, W, s0, s1, -lane * l); Dashes(lines, W, s0, s1, lane * l); }
                    }
                    else if (road.Class != RoadClass.Alley) Dashes(lines, W, s0, s1, 0f);
                    if (j0) { Zebra(lines, W, s0 + 0.5f, hw); StopLine(lines, W, s0 + 4.2f, hw, -1f); }
                    if (j1) { Zebra(lines, W, s1 - 3.5f, hw); StopLine(lines, W, s1 - 4.2f, hw, 1f); }
                    // Kerbside use on ordinary streets: a loading bay or a run of parking bays on either kerb.
                    var zoneHere = CityLayout.ZoneAt((p0 + p1) * 0.5f);
                    if (road.Class == RoadClass.Street && zoneHere is Zone.Kowloon or Zone.Lowtown or Zone.Harbor && s1 - s0 > 40f)
                        foreach (float sg in new[] { -1f, 1f })
                        {
                            double roll = rng.NextDouble();
                            float edge = hw - 0.35f, inner = hw - 2.6f;
                            if (roll < 0.3)
                            {
                                // Loading bay: yellow box with a diagonal hatch, mid-block.
                                float a0 = (s0 + s1) * 0.5f - 7f, a1 = a0 + 14f;
                                yellow.Strip(W(a0, sg * inner), W(a1, sg * inner), 0.12f);
                                yellow.Strip(W(a0, sg * edge), W(a0, sg * inner), 0.12f);
                                yellow.Strip(W(a1, sg * edge), W(a1, sg * inner), 0.12f);
                                for (float h = a0 + 1f; h + 2f <= a1; h += 2.2f) yellow.Strip(W(h, sg * edge), W(h + 2f, sg * inner), 0.1f);
                                LoadingBays++;
                            }
                            else if (roll < 0.6)
                            {
                                // Parking bays: a lane line and a tick every 6 m.
                                float a0 = s0 + 12f, a1 = s1 - 12f;
                                lines.Strip(W(a0, sg * inner), W(a1, sg * inner), 0.1f);
                                for (float t = a0; t <= a1; t += 6f) lines.Strip(W(t, sg * edge), W(t, sg * inner), 0.1f);
                                ParkingRuns++;
                            }
                        }
                    // Manholes and drain grates; in Kowloon and Lowtown some manholes steam.
                    for (float s = s0 + 17f; s < s1 - 10f; s += 47f + (float)rng.NextDouble() * 30f)
                    {
                        var m = W(s, (float)(rng.NextDouble() - 0.5) * hw);
                        manholes.Cylinder(m + Vector3.down * 0.008f, 0.38f, 0.004f, 14, true);
                        var mz = CityLayout.ZoneAt(new Vector2(m.x, m.z));
                        if (mz is Zone.Kowloon or Zone.Lowtown && rng.NextDouble() < 0.3)
                        {
                            for (int q = 0; q < 2; q++)
                            {
                                var turn = Quaternion.Euler(0f, q * 90f + (float)rng.NextDouble() * 30f, 0f);
                                steam.Panel(m + Vector3.up * 1.4f, turn, 1.8f, 2.8f, new Rect(0f, 0f, 1f, 1f));
                                steam.Panel(m + Vector3.up * 3.6f, turn * Quaternion.Euler(0f, 45f, 0f), 2.8f, 3.6f, new Rect(0f, 0f, 1f, 1f));
                            }
                            SteamVents++;
                        }
                    }
                    for (float s = s0 + 8f; s < s1 - 4f; s += 24f)
                        foreach (float sg in new[] { -1f, 1f })
                            manholes.OrientedBox(W(s, sg * (hw - 0.45f)), new Vector3(0.45f, 0.008f, 0.9f), Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)), 1f);
                }
            }
            meshes.Emit(Root, "District_Markings", drivable);
        }

        private delegate Vector3 RoadPoint(float s, float lateral);

        private static void Dashes(MeshBuilder lines, RoadPoint w, float s0, float s1, float lateral)
        {
            for (float s = s0 + 1f; s + 3f < s1; s += 9f) lines.Strip(w(s, lateral), w(s + 3f, lateral), 0.12f);
        }

        private static void Zebra(MeshBuilder lines, RoadPoint w, float s, float hw)
        {
            for (float l = -hw + 1f; l <= hw - 1f; l += 1.2f) lines.Strip(w(s, l), w(s + 3f, l), 0.55f);
        }

        /// <summary>Stop line across the lanes entering the junction (right-hand traffic).</summary>
        private static void StopLine(MeshBuilder lines, RoadPoint w, float s, float hw, float travel)
        {
            float from = travel > 0f ? 0.2f : -hw + 0.4f, to = travel > 0f ? hw - 0.4f : -0.2f;
            lines.Strip(w(s, from), w(s, to), 0.4f);
        }

        // ---------------- Blocks ----------------

        private void BuildBlock(List<CityLayout.Cell> cells, int index)
        {
            var zone = cells.GroupBy(c => c.Zone).OrderByDescending(g => g.Count()).First().Key;
            var style = Style(zone);
            string name = $"Block_{zone}_{index:00}";
            var root = new GameObject(name).transform;
            root.SetParent(Root, false);
            var meshes = new BlockMeshes();
            // Sidewalk furniture and kerb signs: own meshes on the Detail layer, culled by the camera beyond ~180 m.
            var detail = new BlockMeshes();
            var security = meshes.Unshadowed(kit.Security);
            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(root, false);
            var lamps = new GameObject("Lamps").transform;
            lamps.SetParent(root, false);

            // Pavement slabs (lots are cut out where they sit at road level), with a kerb stone along the road edge.
            Rect bounds = cells[0].Grid;
            foreach (var cell in cells)
            {
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, cell.Grid.xMin), Mathf.Min(bounds.yMin, cell.Grid.yMin),
                                         Mathf.Max(bounds.xMax, cell.Grid.xMax), Mathf.Max(bounds.yMax, cell.Grid.yMax));
                foreach (var piece in CityLayout.SlabPieces(cell.Slab, cutLots: true))
                {
                    meshes[kit.Pavement].Cuboid(new Vector3(piece.xMin, 0f, piece.yMin), new Vector3(piece.xMax, KerbHeight, piece.yMax), 2f);
                    var col = new GameObject("Slab") { layer = drivable };
                    col.transform.SetParent(colliders, false);
                    var box = col.AddComponent<BoxCollider>();
                    box.center = new Vector3(piece.center.x, KerbHeight * 0.5f, piece.center.y);
                    box.size = new Vector3(piece.width, KerbHeight, piece.height);
                    GameObjectUtility.SetStaticEditorFlags(col, StaticEditorFlags.BatchingStatic);
                }
            }

            foreach (var cell in cells)
                foreach (var side in cell.Sides)
                {
                    foreach (var (s0, s1) in OpenSpans(side))
                    {
                        Vector3 along = (side.End - side.Start).normalized;
                        Vector3 a = side.Start + along * s0, c = side.Start + along * s1;
                        var rotation = Quaternion.LookRotation(side.Normal);
                        meshes[kit.Kerb].OrientedBox((a + c) * 0.5f + Vector3.up * (KerbHeight * 0.5f + 0.006f) - side.Normal * 0.14f,
                                                     new Vector3(Vector3.Distance(a, c), KerbHeight + 0.012f, 0.3f), rotation, 1f);
                        if (style.KerbNeon)
                            security.OrientedBox((a + c) * 0.5f + Vector3.up * (KerbHeight + 0.025f) - side.Normal * 0.34f,
                                                 new Vector3(Vector3.Distance(a, c), 0.05f, 0.12f), rotation, 1f);
                    }
                    var sub = side;
                    BuildStreetWall(zone, style, sub, meshes, security, colliders);
                    BuildStreetLights(style, side, lamps, meshes);
                    Props.Sidewalk(zone, side, detail, colliders);
                }

            if (zone != Zone.Sector7) FillTowers(cells, style, root, zone == Zone.Outer);

            meshes.Emit(root, name, environment).ForEach(r =>
            {
                if (r.sharedMaterial == kit.Security) SecurityStrips[name] = r;
                if (kit.Billboards.Contains(r.sharedMaterial)) Screens.Add(r);
                if (r.sharedMaterial == kit.Pavement || r.sharedMaterial == kit.Kerb) r.gameObject.layer = drivable;
            });
            detail.Emit(root, name + "_Detail", LayerMask.NameToLayer("Detail")).ForEach(r =>
            {
                if (kit.Billboards.Contains(r.sharedMaterial)) Screens.Add(r);
            });
            BlockCentres[name] = new Vector3(bounds.center.x, 0f, bounds.center.y);
        }

        /// <summary>Stretches of a side not taken by a lot (lots draw their own edges) or a reserved area.</summary>
        private static IEnumerable<(float, float)> OpenSpans(CityLayout.Side side)
        {
            Vector3 along = (side.End - side.Start).normalized;
            float length = Vector3.Distance(side.Start, side.End);
            float start = -1f;
            for (float s = 0f; s <= length; s += 1f)
            {
                Vector3 p = side.Start + along * Mathf.Min(s, length) - side.Normal * 0.5f;
                bool blocked = CityLayout.Reserved.Any(r => r.Contains(new Vector2(p.x, p.z))) ||
                               CityLayout.Lots.Any(l => l.Kind == LotKind.Parking && l.Area.Contains(new Vector2(p.x, p.z)));
                if (!blocked && start < 0f) start = s;
                if ((blocked || s + 1f > length) && start >= 0f)
                {
                    float end = blocked ? s : length;
                    if (end - start > 1f) yield return (start, end);
                    start = -1f;
                }
            }
        }

        private float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        private float Range(Vector2 r) => Range(r.x, r.y);
        private bool Chance(float p) => rng.NextDouble() < p;
        private T Pick<T>(IReadOnlyList<T> list) => list[rng.Next(list.Count)];

        /// <summary>Continuous podium street wall along one side: ground floor, lit upper floors, crown strip, signs, plant.</summary>
        private enum ShopDetail { None, Awning, Shutter }

        public int Awnings { get; private set; }
        public int LoadingBays { get; private set; }
        public int ParkingRuns { get; private set; }
        public int SteamVents { get; private set; }

        private void BuildStreetWall(Zone zone, ZoneStyle style, CityLayout.Side side, BlockMeshes meshes, MeshBuilder security, Transform colliders)
        {
            Vector3 along = (side.End - side.Start).normalized;
            float length = Vector3.Distance(side.Start, side.End);
            var rotation = Quaternion.LookRotation(side.Normal);
            float maxDepth = Mathf.Min(style.MaxDepth, side.Depth * 0.42f);
            float s = 0f;
            int index = 0;
            var roof = meshes[kit.Roof];
            var metal = meshes[kit.Metal];
            while (s < length - 3f)
            {
                float seg = Range(style.Segment);
                if (length - (s + seg) < 10f) seg = length - s;
                float centreS = s + seg * 0.5f;
                Vector3 front = side.Start + along * centreS - side.Normal * side.Path;
                float depth = Range(Mathf.Min(12f, maxDepth), maxDepth);
                s += seg;
                index++;
                // Keep the street wall closed: next to a hero building, shrink the podium rather than drop it.
                while (depth > 4f && Overlaps(FootprintRect(front, rotation, seg, depth), 1.5f)) depth -= 2f;
                if (Overlaps(FootprintRect(front, rotation, seg, depth), 1.5f) || seg < 4f) continue;
                Podiums++;

                bool tall = Chance(style.TallChance);
                float height = tall ? Range(style.TallHeight) : Range(style.Height);
                float setback = Chance(0.5f) ? Range(0.4f, 1.4f) : 0f;
                var facade = kit.Facades[Pick(style.Facades)];
                var fb = meshes[facade];
                Vector2 offset = new(Range(0f, 1f), Range(0f, 1f));
                var tile = DistrictTextures.FacadeTile;
                const float ground = 4.5f;
                var shopDetail = ShopDetail.None;

                // Ground floor: shopfronts, or plain wall with roller doors and wall packs in industrial zones.
                if (style.Industrial || !Chance(style.ShopChance))
                {
                    fb.Box(front, rotation, new Vector3(seg, ground, depth), tile, offset, setback > 0f ? roof : null, new Vector2(8f, 8f));
                    if (style.Industrial)
                        for (float d = -seg * 0.5f + 5f; d < seg * 0.5f - 4f; d += 11f)
                        {
                            meshes[kit.RollerDoor].OrientedBox(front + along * d + Vector3.up * 2f + side.Normal * 0.04f, new Vector3(4.2f, 4f, 0.1f), rotation, 1f);
                            meshes.Unshadowed(kit.WallPack).OrientedBox(front + along * d + Vector3.up * 4.4f + side.Normal * 0.12f, new Vector3(0.5f, 0.25f, 0.2f), rotation, 1f);
                            meshes[kit.MarkingYellow].OrientedBox(front + along * d + side.Normal * 1.6f + Vector3.up * 0.005f, new Vector3(4.2f, 0.01f, 0.15f), rotation, 1f);
                        }
                    else
                        meshes.Unshadowed(kit.WallPack).OrientedBox(front + Vector3.up * 3.4f + side.Normal * 0.12f, new Vector3(0.4f, 0.2f, 0.16f), rotation, 1f);
                }
                else
                {
                    meshes[kit.Shopfront].Box(front, rotation, new Vector3(seg, ground, depth), new Vector2(18f, 4.5f), new Vector2(Range(0f, 1f), 0f),
                                              setback > 0f ? roof : null, new Vector2(8f, 8f));
                    shopDetail = Chance(0.12f) ? ShopDetail.Shutter : zone != Zone.Spire && Chance(0.38f) ? ShopDetail.Awning : ShopDetail.None;
                    if (shopDetail == ShopDetail.Shutter)
                    {
                        // Closed for the night: shutter down over the glass, one wall pack left on.
                        meshes[kit.RollerDoor].OrientedBox(front + Vector3.up * 1.7f + side.Normal * 0.05f, new Vector3(seg - 1.2f, 3.4f, 0.06f), rotation, 1f);
                        meshes[kit.Metal].OrientedBox(front + Vector3.up * 3.55f + side.Normal * 0.16f, new Vector3(seg - 1f, 0.32f, 0.3f), rotation, 1f);
                    }
                    else if (shopDetail == ShopDetail.Awning)
                    {
                        // Fabric awning, pitched down to the street, with a valance; replaces the neon awning strip.
                        var fabric = kit.Awnings[rng.Next(kit.Awnings.Length)];
                        float w = seg - 1.4f, reach = Range(1.4f, 2f);
                        var pitch = rotation * Quaternion.Euler(20f, 0f, 0f);
                        Vector3 root = front + Vector3.up * 4.1f + side.Normal * 0.05f;
                        meshes[fabric].OrientedBox(root + pitch * new Vector3(0f, 0f, reach * 0.5f), new Vector3(w, 0.05f, reach), pitch, 1f);
                        Vector3 lip = root + pitch * new Vector3(0f, 0f, reach);
                        meshes[fabric].OrientedBox(lip + Vector3.down * 0.17f, new Vector3(w, 0.34f, 0.03f), rotation, 1f);
                        Awnings++;
                    }
                }
                if (zone is Zone.Spire or Zone.Sector7 && Chance(0.22f))
                    Props.Cctv(meshes, front + Vector3.up * 4.9f + along * (seg * 0.5f - 0.8f) + side.Normal * 0.1f,
                               front + side.Normal * 8f + along * (Chance(0.5f) ? 15f : -15f));
                Vector3 upperFront = front + Vector3.up * ground - side.Normal * setback;
                float upper = height - ground;
                fb.Box(upperFront, rotation, new Vector3(seg, upper, depth - setback), tile, offset, roof, new Vector2(8f, 8f), vStart: ground);

                // Crown strip in the security colour (turns red in a lockdown).
                security.OrientedBox(upperFront + Vector3.up * (upper - 0.15f) + side.Normal * 0.1f, new Vector3(seg, 0.18f, 0.18f), rotation, 1f);
                // Awning neon over the shopfronts (not where a fabric awning or a shutter is).
                if (!style.Industrial && shopDetail == ShopDetail.None && Chance(style.NeonChance))
                    meshes.Unshadowed(kit.NeonStrips[rng.Next(kit.NeonStrips.Length)])
                          .OrientedBox(front + Vector3.up * 4.4f + side.Normal * 0.35f, new Vector3(seg - 1.5f, 0.1f, 0.7f), rotation, 1f);

                if (Chance(style.SignChance))
                {
                    string word = Pick(style.Words);
                    var uv = DistrictTextures.SignRect(word, out float aspect);
                    float hgt = Range(1.3f, 2.2f);
                    float width = Mathf.Min(hgt * aspect, seg - 3f);
                    hgt = width / aspect;
                    Vector3 c = upperFront + Vector3.up * Range(1.2f, Mathf.Max(1.3f, Mathf.Min(6f, upper - 2.5f))) + side.Normal * 0.06f
                                + along * Range(-seg * 0.2f, seg * 0.2f);
                    meshes.Unshadowed(kit.Signs[rng.Next(kit.Signs.Length)]).Panel(c + Vector3.up * hgt * 0.5f, rotation, width, hgt, uv);
                }
                if (Chance(style.BladeChance) && height > 10f)
                {
                    string word = Pick(style.Words);
                    var uv = DistrictTextures.SignRect(word, out float aspect);
                    float letter = 1.1f;
                    float len = Mathf.Min(letter * aspect, height - 6f);
                    letter = len / aspect;
                    Vector3 c = upperFront + along * (seg * 0.5f - 1.2f) + side.Normal * (setback + 0.9f) + Vector3.up * (1f + len * 0.5f);
                    metal.OrientedBox(c, new Vector3(0.2f, len + 0.4f, 1.5f), rotation, 1f);
                    var signs = meshes.Unshadowed(kit.Signs[rng.Next(kit.Signs.Length)]);
                    var blade = rotation * Quaternion.Euler(0f, 90f, 0f);
                    signs.Panel(c + blade * Vector3.forward * 0.11f, blade, letter, len, uv, rotateUv: true);
                    signs.Panel(c - blade * Vector3.forward * 0.11f, rotation * Quaternion.Euler(0f, -90f, 0f), letter, len, uv, rotateUv: true);
                }
                if (!tall && height < 16f && Chance(style.BillboardChance))
                {
                    Vector3 roofFront = upperFront + Vector3.up * upper - side.Normal * 2f;
                    float bw = Mathf.Min(14f, seg - 4f);
                    if (bw > 5f)
                    {
                        metal.OrientedBox(roofFront + Vector3.up * 2f - along * bw * 0.35f, new Vector3(0.3f, 4f, 0.3f), rotation, 1f);
                        metal.OrientedBox(roofFront + Vector3.up * 2f + along * bw * 0.35f, new Vector3(0.3f, 4f, 0.3f), rotation, 1f);
                        meshes.Unshadowed(kit.Billboards[rng.Next(kit.Billboards.Length)])
                              .Panel(roofFront + Vector3.up * (3f + bw * 0.25f) + side.Normal * 0.2f, rotation, bw, bw * 0.5f, new Rect(0f, 0f, 1f, 1f));
                    }
                }
                Props.Facade(zone, style, upperFront, along, side.Normal, rotation, seg, upper, depth - setback, meshes);
                Props.Rooftop(zone, style, upperFront + Vector3.up * upper, along, side.Normal, rotation, seg, depth - setback, tall, meshes);

                var col = new GameObject($"Podium_{side.Normal.x:0}{side.Normal.z:0}_{index}") { layer = environment };
                col.transform.SetParent(colliders, false);
                col.transform.SetPositionAndRotation(front + Vector3.up * (height * 0.5f) - side.Normal * (depth * 0.5f), rotation);
                col.AddComponent<BoxCollider>().size = new Vector3(seg, height, depth);
                GameObjectUtility.SetStaticEditorFlags(col, StaticEditorFlags.BatchingStatic);
            }
        }

        private void BuildStreetLights(ZoneStyle style, CityLayout.Side side, Transform parent, BlockMeshes meshes)
        {
            var road = CityLayout.Roads[side.Road];
            if (road.Class == RoadClass.Tunnel) return;
            Vector3 along = (side.End - side.Start).normalized;
            float length = Vector3.Distance(side.Start, side.End);
            bool narrow = side.Path < 2f;
            float spacing = narrow ? 30f : style.LampSpacing;
            // World-aligned spacing, staggered half a spacing between the two sides of a street.
            float offset = Vector3.Dot(side.Normal, Vector3.one) > 0f ? spacing * 0.5f : 0f;
            for (float s = Mathf.Repeat(offset - Vector3.Dot(side.Start, along), spacing); s < length - 8f; s += spacing)
            {
                if (s < 8f) continue;
                Vector3 p = side.Start + along * s - side.Normal * (narrow ? 0.6f : 0.8f);
                Vector2 flat = Flat(p);
                if (layout.Nodes.Any(n => (Flat(n) - flat).magnitude < 14f)) continue;
                if (CityLayout.Reserved.Any(r => r.Contains(flat)) || CityLayout.Lots.Any(l => l.Kind == LotKind.Parking && l.Area.Contains(flat))) continue;
                var go = DistrictKit.Place(kit.StreetLights[(int)style.Lamp], parent, p, Quaternion.LookRotation(side.Normal));
                Lamps.Add(go.GetComponentInChildren<Light>(true));
                CityProps.LampHaze(kit, meshes, style.Lamp, p + side.Normal * 2.9f + Vector3.up * 7.75f, 7.6f);
            }
        }

        // ---------------- Buildings ----------------

        private BuildingDefinition Definition(string id) => catalog.Buildings.FirstOrDefault(d => d != null && d.Id == id);

        private void PlaceHeroes()
        {
            var parent = new GameObject("Buildings").transform;
            parent.SetParent(Root, false);
            foreach (var t in CityLayout.Heroes.Concat(CityLayout.Skyline))
            {
                var def = Definition(t.Id);
                if (def == null || def.Prefab == null)
                {
                    Debug.LogWarning($"[District] Building '{t.Id}' is not in the catalog.");
                    continue;
                }
                var rect = Footprint(def, t.Position, t.Yaw, t.Scale);
                if (def.Tier != BuildingTier.Skyline && CityLayout.Blocked(rect, includeLots: false))
                {
                    Debug.LogWarning($"[District] '{t.Id}' at {t.Position} overlaps a road or reserved area; skipped.");
                    continue;
                }
                Place(def, parent, t.Position, t.Yaw, t.Scale, collider: true);
                towerFootprints.Add(rect);
                if (def.Tier == BuildingTier.Hero) heroFootprints.Add(rect);
            }
        }

        private static Rect Footprint(BuildingDefinition def, Vector2 position, float yaw, float scale)
        {
            Vector3 size = def.Size * scale;
            var e = Quaternion.Euler(0f, yaw, 0f) * new Vector3(size.x, 0f, size.z);
            float w = Mathf.Abs(e.x), d = Mathf.Abs(e.z);
            return new Rect(position.x - w * 0.5f, position.y - d * 0.5f, w, d);
        }

        private void Place(BuildingDefinition def, Transform parent, Vector2 position, float yaw, float scale, bool collider)
        {
            var go = DistrictKit.Place(def.Prefab, parent, new Vector3(position.x, 0f, position.y), Quaternion.Euler(0f, yaw, 0f), $"{def.Prefab.name}_{parent.childCount}");
            go.transform.localScale = Vector3.one * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                                     StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            if (!collider) foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            Dressing.ApplyFacade(go);
            Dressing.Dress(go);
            if (def.Tier == BuildingTier.Hero) HeroLod(go);
            Towers++;
        }

        private static readonly string[] HeroInterior = { "interior", "lobby", "furniture", "curtain", "blinds", "railing", "sidewalk", "floor" };

        /// <summary>
        /// Hero models carry interiors, lobbies and furniture behind their glass. They never cast shadows, and they drop
        /// out below 15 % screen height. The shell stays at every distance: these towers are skyline landmarks.
        /// </summary>
        private void HeroLod(GameObject go)
        {
            var all = go.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled).ToArray();
            var shell = new List<Renderer>();
            foreach (var r in all)
            {
                string mats = string.Join("|", r.sharedMaterials.Where(m => m != null).Select(m => m.name.ToLowerInvariant()));
                if (HeroInterior.Any(mats.Contains)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                else shell.Add(r);
            }
            if (shell.Count == all.Length) return;
            var group = go.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(new[] { new LOD(0.15f, all.Cast<Renderer>().ToArray()), new LOD(0f, shell.ToArray()) });
            group.RecalculateBounds();
            HeroLods++;
        }

        public int HeroLods { get; private set; }

        /// <summary>
        /// Generated towers in the interior of a block: candidates on a 12 m grid in seeded order, each a catalog building
        /// of the zone's set that fits behind the street walls without touching another tower, a road or a lot.
        /// </summary>
        private void FillTowers(List<CityLayout.Cell> cells, ZoneStyle style, Transform blockRoot, bool outer)
        {
            if (style.Towers.Length == 0 || style.TowerDensity <= 0f) return;
            var parent = new GameObject("Towers").transform;
            parent.SetParent(blockRoot, false);
            var defs = style.Towers.Select(Definition).Where(d => d != null && d.Prefab != null && d.Size.y <= style.TowerMaxHeight).ToList();
            if (defs.Count == 0) return;
            foreach (var cell in cells)
            {
                var s = cell.Slab;
                float inset = style.MaxDepth + 3f;
                bool roadN = cell.Sides.Any(x => x.Normal == Vector3.forward), roadS = cell.Sides.Any(x => x.Normal == Vector3.back);
                bool roadE = cell.Sides.Any(x => x.Normal == Vector3.right), roadW = cell.Sides.Any(x => x.Normal == Vector3.left);
                var interior = Rect.MinMaxRect(s.xMin + (roadW ? inset : 4f), s.yMin + (roadS ? inset : 4f), s.xMax - (roadE ? inset : 4f), s.yMax - (roadN ? inset : 4f));
                if (interior.width < 20f || interior.height < 20f) continue;
                int target = Mathf.Max(outer ? 1 : 0, Mathf.RoundToInt(interior.width * interior.height / 3600f * style.TowerDensity));
                var candidates = new List<Vector2>();
                for (float x = interior.xMin + 10f; x < interior.xMax - 10f; x += 12f)
                    for (float z = interior.yMin + 10f; z < interior.yMax - 10f; z += 12f)
                        candidates.Add(new Vector2(x, z));
                for (int i = candidates.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (candidates[i], candidates[j]) = (candidates[j], candidates[i]); }
                int placed = 0;
                foreach (var p in candidates)
                {
                    if (placed >= target) break;
                    var def = Pick(defs);
                    float yaw = 90f * rng.Next(4);
                    float scale = Range(0.85f, 1.05f);
                    var rect = Footprint(def, p, yaw, scale);
                    if (rect.xMin < interior.xMin || rect.xMax > interior.xMax || rect.yMin < interior.yMin || rect.yMax > interior.yMax) continue;
                    var grown = new Rect(rect.x - 4f, rect.y - 4f, rect.width + 8f, rect.height + 8f);
                    if (towerFootprints.Any(f => f.Overlaps(grown)) || CityLayout.Blocked(rect)) continue;
                    Place(def, parent, p, yaw, scale, collider: !outer);
                    towerFootprints.Add(rect);
                    placed++;
                }
            }
        }

        private static Rect FootprintRect(Vector3 front, Quaternion rotation, float width, float depth)
        {
            Vector3 c = front - rotation * Vector3.forward * (depth * 0.5f);
            Vector3 e = rotation * new Vector3(width, 0f, depth);
            float w = Mathf.Abs(e.x), d = Mathf.Abs(e.z);
            return new Rect(c.x - w * 0.5f, c.z - d * 0.5f, w, d);
        }

        private bool Overlaps(Rect r, float margin)
        {
            var grown = new Rect(r.x + margin, r.y + margin, r.width - margin * 2f, r.height - margin * 2f);
            return heroFootprints.Any(f => f.Overlaps(grown)) || CityLayout.Reserved.Any(f => f.Overlaps(grown)) ||
                   CityLayout.Lots.Any(l => l.Area.Overlaps(grown));
        }

        // ---------------- Navigation signs ----------------

        /// <summary>Overhead gantries that make route choices readable: plates of neon text and arrows.</summary>
        private void BuildNavigationSigns()
        {
            var parent = new GameObject("NavigationSigns").transform;
            parent.SetParent(Root, false);
            var meshes = new BlockMeshes();
            var cyan = meshes.Unshadowed(kit.Signs[0]);
            var amber = meshes.Unshadowed(kit.Signs[3]);
            var magenta = meshes.Unshadowed(kit.Signs[1]);
            var white = meshes.Unshadowed(kit.StreetSign);

            // Sector 7 approach: the choice between the long boulevard and the alley shortcut.
            Gantry(meshes, new Vector3(0f, 0f, -160f), 0f, 7f, new[] { ("SECTOR 7", "↑", cyan, -3.5f), ("SERVICE ALLEY", "→", amber, 3.5f) });
            Gantry(meshes, new Vector3(110f, 0f, -100f), 90f, 6f, new[] { ("DATA CORE", "←", amber, 0f) });
            Gantry(meshes, new Vector3(100f, 0f, 300f), 90f, 7f, new[] { ("DATA CORE", "→", cyan, 3.5f) });
            // Escape guidance.
            Gantry(meshes, new Vector3(235f, 0f, 300f), 90f, 7f, new[] { ("EXPRESSWAY", "→", magenta, 3.5f) });
            Gantry(meshes, new Vector3(320f, 0f, 90f), 180f, 9f, new[] { ("EXTRACTION", "↑", magenta, 4.5f), ("SKYWAY", "←", white, -4.5f) });
            Gantry(meshes, new Vector3(240f, 0f, -100f), 90f, 6f, new[] { ("EXTRACTION", "→", magenta, 0f) });
            Gantry(meshes, new Vector3(320f, 0f, -250f), 180f, 9f, new[] { ("RIFT GATE", "↑", magenta, 0f) });
            // District gateways.
            Gantry(meshes, new Vector3(0f, 0f, 380f), 0f, 7f, new[] { ("SPIRE HEIGHTS", "↑", white, -3.5f) });
            Gantry(meshes, new Vector3(-60f, 0f, -100f), 270f, 6f, new[] { ("KOWLOON MARKET", "↑", amber, 0f) });
            Gantry(meshes, new Vector3(400f, 0f, -100f), 90f, 6f, new[] { ("HARBOR YARDS", "↑", white, 0f) });
            Gantry(meshes, new Vector3(0f, 0f, -400f), 180f, 7f, new[] { ("LOWTOWN", "↑", amber, 3.5f) });
            Gantry(meshes, new Vector3(560f, 0f, -200f), 180f, 7f, new[] { ("YARD RD", "↑", white, 3.5f), ("RIFT GATE", "→", magenta, -3.5f) });
            Gantry(meshes, new Vector3(-120f, 0f, -320f), 90f, 6f, new[] { ("RIFT GATE", "↑", magenta, 0f) });
            meshes.Emit(parent, "District_Gantries", environment);
        }

        /// <summary>
        /// A sign gantry across a road at <paramref name="centre"/>; <paramref name="yaw"/> is the travel direction of the
        /// traffic it faces. Each plate: text, arrow, colour builder and lateral offset (right of travel = +).
        /// </summary>
        private void Gantry(BlockMeshes meshes, Vector3 centre, float yaw, float halfWidth, (string text, string arrow, MeshBuilder colour, float lateral)[] items)
        {
            var metal = meshes[kit.Metal];
            var plates = meshes.Unshadowed(kit.Roof);
            var travel = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = travel * Vector3.right;
            var facing = travel * Quaternion.Euler(0f, 180f, 0f);        // plates face oncoming traffic
            const float beamHeight = 6.8f;
            foreach (float sgn in new[] { -1f, 1f })
                metal.OrientedBox(centre + right * (halfWidth + 0.8f) * sgn + Vector3.up * (beamHeight * 0.5f), new Vector3(0.35f, beamHeight, 0.35f), travel, 1f);
            metal.OrientedBox(centre + Vector3.up * beamHeight, new Vector3(0.45f, 0.45f, halfWidth * 2f + 2f), travel * Quaternion.Euler(0f, 90f, 0f), 1f);
            foreach (var (text, arrow, colour, lateral) in items)
            {
                var uv = DistrictTextures.SignRect(text, out float aspect);
                var uvArrow = DistrictTextures.SignRect(arrow, out float arrowAspect);
                const float letter = 0.9f;
                float textWidth = letter * aspect, arrowWidth = letter * arrowAspect;
                float plateWidth = textWidth + arrowWidth + 0.6f;
                Vector3 c = centre + right * lateral + Vector3.up * (beamHeight - 1.1f);
                plates.OrientedBox(c - facing * Vector3.forward * 0.06f, new Vector3(plateWidth, 1.5f, 0.1f), facing, 1f);
                // Seen from the front, +X of the facing rotation is the viewer's left: text left, arrow right.
                Vector3 viewerRight = -(facing * Vector3.right);
                colour.Panel(c + facing * Vector3.forward * 0.01f - viewerRight * (plateWidth * 0.5f - 0.3f - textWidth * 0.5f), facing, textWidth, letter, uv);
                colour.Panel(c + facing * Vector3.forward * 0.01f + viewerRight * (plateWidth * 0.5f - 0.3f - arrowWidth * 0.5f), facing, arrowWidth, letter, uvArrow);
            }
        }

        // ---------------- Reflection probes ----------------

        /// <summary>
        /// Box-projected probes along the arterials and Sector 7 streets (the wet asphalt picks up the neon), coarser in
        /// the outer districts; everything else falls back to the sky probe.
        /// </summary>
        private void BuildReflectionProbes()
        {
            var parent = new GameObject("ReflectionProbes").transform;
            parent.SetParent(Root, false);
            var spots = new List<(Vector3 centre, Vector3 size, int resolution)>();
            foreach (var road in CityLayout.Roads)
            {
                if (!road.Ground || road.Class == RoadClass.Tunnel) continue;
                Vector2 a = Flat(road.A), b = Flat(road.B);
                Vector2 dir = (b - a).normalized;
                float length = (b - a).magnitude;
                bool hero = CityLayout.ZoneAt((a + b) * 0.5f) == Zone.Sector7;
                if (!hero && road.Class != RoadClass.Arterial) continue;
                float spacing = hero ? 120f : 260f;
                int count = Mathf.Max(1, Mathf.RoundToInt(length / spacing));
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = a + dir * (length * (i + 0.5f) / count);
                    float along = length / count + 10f;
                    float across = road.HalfWidth * 2f + 12f;
                    Vector3 size = Mathf.Abs(dir.x) > 0.5f ? new Vector3(along, 40f, across) : new Vector3(across, 40f, along);
                    var zone = CityLayout.ZoneAt(p);
                    spots.Add((new Vector3(p.x, 3f, p.y), size, zone == Zone.Sector7 || zone == Zone.Spire ? 128 : 64));
                }
            }
            spots.Add((new Vector3(160f, 3f, 127f), new Vector3(112f, 40f, 137f), 128));   // compound
            int n = 0;
            foreach (var (centre, size, resolution) in spots)
            {
                var go = new GameObject($"Probe_{n++:00}");
                go.transform.SetParent(parent, false);
                go.transform.position = centre;
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Baked;
                probe.boxProjection = true;
                probe.size = size;
                probe.center = new Vector3(0f, 17f, 0f);
                probe.resolution = resolution;
                probe.hdr = true;
                probe.intensity = 1f;
                probe.blendDistance = 6f;
                probe.cullingMask = ~(1 << LayerMask.NameToLayer("Vehicle"));
            }
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) => CityLayout.DistanceToSegment(p, a, b);
    }
}
