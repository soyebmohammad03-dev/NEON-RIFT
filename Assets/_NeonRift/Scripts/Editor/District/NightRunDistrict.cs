using System.Collections.Generic;
using System.Linq;
using NeonRift.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Layout and geometry of Sector 7, the Night Run hero district (≈ 340 × 720 m of streets).
    ///
    ///   z  300  ┌──── North Boulevard ────┬──────────────┐  ← London tower closes the W Avenue vista
    ///           │                  Access │              │
    ///      195  │            ┌── Rd ──┐   │              │
    ///           │ W          │ DATA   │   │  E           │
    ///           │ Avenue     │ CORE   │   │  Expressway  │  (checkpoint at z 170)
    ///       60  │            └─┬────┬─┘   │              │
    ///           │        Service Alley (gate z −20)      │
    ///     −100  ├──────── Market Street ──┴──────────────┤
    ///           │           (south block)                │
    ///     −320  └──────── South Street ──────────────────┤
    ///         x 0          160                        320 │ → tunnel to the Rift Gate extraction (z −405…−470)
    ///
    /// Roads are gaps between raised pavement blocks on one asphalt ground plane, so intersections need no special
    /// geometry. Blocks are lined with procedural podium street walls (shopfronts, lit windows, neon, signs) and
    /// filled with the audited building prefabs.
    /// </summary>
    public sealed class NightRunDistrict
    {
        public struct Road
        {
            public string Name;
            public Vector2 A, B;
            public float HalfWidth;
            public int LanesPerDirection;
            public Road(string name, Vector2 a, Vector2 b, float halfWidth, int lanes) { Name = name; A = a; B = b; HalfWidth = halfWidth; LanesPerDirection = lanes; }
        }

        public struct Block
        {
            public string Name;
            public float X0, X1, Z0, Z1;
            /// <summary>Road-facing sides: north, east, south, west.</summary>
            public bool N, E, S, W;
            /// <summary>Pavement width per side (alley sides are narrow).</summary>
            public float PathN, PathE, PathS, PathW;
            public bool Outer;
            /// <summary>Optional XZ window the road-facing sides are clipped to (outer blocks are longer than the roads they face).</summary>
            public Rect Clip;
            public Vector3 Centre => new((X0 + X1) * 0.5f, 0f, (Z0 + Z1) * 0.5f);
        }

        public struct Tower
        {
            public string Id;
            public Vector2 Position;
            public float Yaw;
            public float Scale;
            public Tower(string id, float x, float z, float yaw, float scale = 1f) { Id = id; Position = new Vector2(x, z); Yaw = yaw; Scale = scale; }
        }

        public const float KerbHeight = 0.15f;

        public static readonly Road[] Roads =
        {
            new("W Avenue", new(0, -320), new(0, 300), 7f, 2),
            new("North Boulevard", new(0, 300), new(320, 300), 7f, 2),
            new("East Expressway", new(320, 300), new(320, -468), 9f, 2),
            new("South Street", new(0, -320), new(320, -320), 6f, 1),
            new("Market Street", new(0, -100), new(320, -100), 6f, 1),
            new("Access Road", new(160, 300), new(160, 195), 6f, 1),
            new("Service Alley", new(160, -100), new(160, 60), 5f, 1),
        };

        public static readonly Vector2[] Intersections =
        {
            new(0, -320), new(0, -100), new(0, 300), new(160, 300), new(320, 300), new(320, -100), new(320, -320), new(160, -100)
        };

        public static readonly Block[] Blocks =
        {
            new() { Name = "Block_R1", X0 = 7, X1 = 155, Z0 = -94, Z1 = 60, E = true, S = true, W = true, PathE = 1.5f, PathS = 4, PathW = 4 },
            new() { Name = "Block_R2", X0 = 7, X1 = 105, Z0 = 60, Z1 = 293, N = true, W = true, PathN = 4, PathW = 4 },
            new() { Name = "Block_R3", X0 = 105, X1 = 154, Z0 = 195, Z1 = 293, N = true, E = true, PathN = 4, PathE = 3 },
            new() { Name = "Block_R4", X0 = 166, X1 = 215, Z0 = 195, Z1 = 293, N = true, W = true, PathN = 4, PathW = 3 },
            new() { Name = "Block_R5", X0 = 215, X1 = 311, Z0 = 60, Z1 = 293, N = true, E = true, PathN = 4, PathE = 4 },
            new() { Name = "Block_R6", X0 = 165, X1 = 311, Z0 = -94, Z1 = 60, E = true, S = true, W = true, PathE = 4, PathS = 4, PathW = 1.5f },
            new() { Name = "Block_R7", X0 = 7, X1 = 311, Z0 = -314, Z1 = -106, N = true, E = true, S = true, W = true, PathN = 4, PathE = 4, PathS = 4, PathW = 4 },
            new() { Name = "Block_OuterWest", X0 = -220, X1 = -7, Z0 = -520, Z1 = 520, E = true, PathE = 4, Outer = true, Clip = Rect.MinMaxRect(-220, -326, 0, 307) },
            new() { Name = "Block_OuterNorth", X0 = -7, X1 = 329, Z0 = 307, Z1 = 520, S = true, PathS = 4, Outer = true },
            new() { Name = "Block_OuterEast", X0 = 329, X1 = 540, Z0 = -520, Z1 = 520, W = true, PathW = 4, Outer = true, Clip = Rect.MinMaxRect(320, -405, 540, 307) },
            new() { Name = "Block_OuterSouth", X0 = -7, X1 = 311, Z0 = -520, Z1 = -326, N = true, E = true, PathN = 4, PathE = 3, Outer = true, Clip = Rect.MinMaxRect(-7, -405, 320, -320) },
        };

        /// <summary>Audited catalog buildings: heroes at the pavement line, towers inside blocks, skyline beyond.</summary>
        public static readonly Tower[] Towers =
        {
            // Heroes
            new("singapore_office", 39f, -255f, -90f),   // greets the player at spawn, facing W Avenue
            new("singapore_office", 263f, 261f, 0f),     // North Boulevard, opposite the access road
            new("singapore_office", 361f, -150f, -90f),  // East Expressway, the escape run
            new("london_skyscraper", 10f, 336.5f, 180f), // terminates the W Avenue vista
            // Inner blocks
            new("asian_night_b2_008", 82f, -17f, 0f),
            new("asian_night_b1_008", 58f, 175f, 90f),
            new("asian_night_b2_006", 120f, 232f, 0f),
            new("asian_night_b1_005", 200f, 238f, 0f),
            new("asian_night_b1_003", 258f, 118f, 90f),
            new("asian_night_b2", 232f, -18f, 0f),
            new("asian_night_b1_009", 165f, -212f, 0f),
            new("asian_night_b2_005", 245f, -148f, 0f),
            new("asian_night_b1_004", 245f, -276f, 180f),
            new("asian_night_b1_003", 100f, -165f, 0f, 0.9f),
            // Outer blocks, second row
            new("asian_night_b1", -62f, 160f, 90f),
            new("asian_night_b2_004", -70f, -170f, 90f),
            new("asian_night_b2_008", -66f, 20f, 90f, 0.85f),
            new("asian_night_b2_001", -64f, -330f, 90f),
            new("asian_night_b1_009", 120f, 362f, 180f),
            new("asian_night_b2", 255f, 378f, 180f),
            new("asian_night_b1_008", 393f, 120f, -90f),
            new("asian_night_b2_001", 384f, -20f, -90f),
            new("asian_night_b1", 400f, 260f, -90f),
            new("asian_night_b1_004", 90f, -362f, 0f),
            new("asian_night_b2_005", 220f, -358f, 0f),
            new("asian_night_b2_008", 400f, -330f, -90f),
            // Skyline rows (no colliders, beyond reach)
            new("asian_night_b1_001", -140f, 300f, 90f), new("asian_night_b1_002", -150f, -60f, 90f), new("asian_night_b1_006", -135f, -260f, 90f),
            new("asian_night_b1_007", -160f, 100f, 90f), new("asian_night_b2_007", -150f, 430f, 45f), new("asian_night_b2_002", 40f, 450f, 180f),
            new("asian_night_b2_003", 180f, 470f, 180f), new("asian_night_b1_006", 300f, 450f, 180f), new("asian_night_b1_007", 470f, 380f, -90f),
            new("asian_night_b1_001", 470f, 60f, -90f), new("asian_night_b1_002", 480f, -200f, -90f), new("asian_night_b2_007", 470f, -420f, -90f),
            new("asian_night_b2_002", 60f, -470f, 0f), new("asian_night_b1_005", 200f, -460f, 0f), new("asian_night_b2_003", -120f, -450f, 0f),
            new("night_skyline_cluster", 160f, 820f, 180f), new("night_skyline_cluster", -760f, 40f, 90f),
            new("night_skyline_cluster", 1080f, -60f, -90f), new("night_skyline_cluster", 200f, -980f, 0f),
        };

        /// <summary>Compound and special areas that podiums must leave clear (x0, z0, x1, z1).</summary>
        public static readonly Rect[] Reserved =
        {
            Rect.MinMaxRect(105, 60, 215, 195),     // Data Core compound
        };

        private readonly DistrictKit kit;
        private readonly BuildingCatalog catalog;
        private readonly System.Random rng = new(2077);
        private readonly List<Rect> footprints = new();
        private readonly int drivable, environment;

        public Transform Root { get; private set; }
        public Dictionary<string, Renderer> SecurityStrips { get; } = new();
        public int RealtimeLights { get; private set; }
        public int Podiums { get; private set; }

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
            BuildGround();
            BuildMarkings();
            PlaceTowers();
            foreach (var b in Blocks) BuildBlock(b);
            BuildAlleyDressing();
            BuildNavigationSigns();
            BuildReflectionProbes();
        }

        // ---------------- Ground and roads ----------------

        private void BuildGround()
        {
            var ground = new MeshBuilder();
            ground.Ground(new Vector3(-900f, 0f, -1200f), new Vector3(1250f, 0f, 1100f), 8f);
            var go = DistrictKit.Renderer("Ground_Asphalt", Root, DistrictKit.SaveMesh(ground, "District_Ground"), kit.Asphalt, drivable);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(175f, -0.5f, -50f);
            col.size = new Vector3(2150f, 1f, 2300f);
        }

        private static float TrimAt(Vector2 node)
        {
            float r = 0f;
            foreach (var road in Roads)
                if (DistanceToSegment(node, road.A, road.B) < 0.5f) r = Mathf.Max(r, road.HalfWidth);
            return r + 2f;
        }

        private static bool IsNode(Vector2 p) => Intersections.Any(n => (n - p).sqrMagnitude < 0.25f);

        private void BuildMarkings()
        {
            var lines = new MeshBuilder();
            const float y = 0.012f;
            foreach (var road in Roads)
            {
                Vector2 dir = (road.B - road.A).normalized;
                Vector2 side = new(dir.y, -dir.x);     // right of travel A→B
                // Split the road at every intersection lying on it, trimming around each one.
                var cuts = Intersections.Where(n => DistanceToSegment(n, road.A, road.B) < 0.5f)
                                        .Select(n => Vector2.Dot(n - road.A, dir)).ToList();
                cuts.Add(0f);
                cuts.Add((road.B - road.A).magnitude);
                cuts = cuts.Distinct().OrderBy(c => c).ToList();
                for (int i = 0; i < cuts.Count - 1; i++)
                {
                    float s0 = cuts[i], s1 = cuts[i + 1];
                    Vector2 p0 = road.A + dir * s0, p1 = road.A + dir * s1;
                    if (IsNode(p0)) s0 += TrimAt(p0);
                    if (IsNode(p1)) s1 -= TrimAt(p1);
                    if (s1 - s0 < 4f) continue;
                    Vector3 W(float s, float lateral) { var p = road.A + dir * s + side * lateral; return new Vector3(p.x, y, p.y); }

                    float hw = road.HalfWidth;
                    // Edge lines.
                    lines.Strip(W(s0, -(hw - 0.35f)), W(s1, -(hw - 0.35f)), 0.15f);
                    lines.Strip(W(s0, hw - 0.35f), W(s1, hw - 0.35f), 0.15f);
                    // Centre: double solid on multi-lane roads, dashed otherwise.
                    if (road.LanesPerDirection >= 2)
                    {
                        lines.Strip(W(s0, -0.18f), W(s1, -0.18f), 0.12f);
                        lines.Strip(W(s0, 0.18f), W(s1, 0.18f), 0.12f);
                        float lane = hw / road.LanesPerDirection;
                        for (int l = 1; l < road.LanesPerDirection; l++) { Dashes(lines, W, s0, s1, -lane * l); Dashes(lines, W, s0, s1, lane * l); }
                    }
                    else Dashes(lines, W, s0, s1, 0f);
                    // Zebra crossings at intersection approaches.
                    if (IsNode(p0)) Zebra(lines, W, s0 + 0.5f, hw);
                    if (IsNode(p1)) Zebra(lines, W, s1 - 3.5f, hw);
                }
            }
            var go = DistrictKit.Renderer("RoadMarkings", Root, DistrictKit.SaveMesh(lines, "District_RoadMarkings"), kit.Marking, drivable, shadows: false);
            go.GetComponent<MeshRenderer>().receiveShadows = true;
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

        // ---------------- Blocks ----------------

        private void BuildBlock(Block b)
        {
            var root = new GameObject(b.Name).transform;
            root.SetParent(Root, false);
            root.position = Vector3.zero;

            var slab = new MeshBuilder();
            slab.Cuboid(new Vector3(b.X0, 0f, b.Z0), new Vector3(b.X1, KerbHeight, b.Z1), 2f);
            var slabGo = DistrictKit.Renderer("Pavement", root, DistrictKit.SaveMesh(slab, b.Name + "_Pavement"), kit.Pavement, drivable, shadows: false);
            var slabCol = slabGo.AddComponent<BoxCollider>();
            slabCol.center = new Vector3((b.X0 + b.X1) * 0.5f, KerbHeight * 0.5f, (b.Z0 + b.Z1) * 0.5f);
            slabCol.size = new Vector3(b.X1 - b.X0, KerbHeight, b.Z1 - b.Z0);

            var facades = kit.Facades.Select(_ => new MeshBuilder()).ToArray();
            var shop = new MeshBuilder();
            var roof = new MeshBuilder();
            var security = new MeshBuilder();
            var metal = new MeshBuilder();
            var neon = kit.NeonStrips.Select(_ => new MeshBuilder()).ToArray();
            var signs = kit.Signs.Select(_ => new MeshBuilder()).ToArray();
            var billboards = kit.Billboards.Select(_ => new MeshBuilder()).ToArray();
            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(root, false);
            var props = new GameObject("Props").transform;
            props.SetParent(root, false);

            foreach (var side in Sides(b))
            {
                // Neon kerb line along the road edge (security colour: turns red in a lockdown).
                Vector3 a = side.Start, c = side.End;
                security.OrientedBox((a + c) * 0.5f + Vector3.up * (KerbHeight + 0.025f) - side.Normal * 0.08f,
                                     new Vector3(Vector3.Distance(a, c), 0.05f, 0.14f), Quaternion.LookRotation(side.Normal), 1f);
                BuildStreetWall(b, side, facades, shop, roof, security, metal, neon, signs, billboards, colliders);
                BuildStreetLights(side, props);
            }

            for (int i = 0; i < facades.Length; i++) Emit(root, $"Facade{i}", facades[i], kit.Facades[i], b.Name);
            Emit(root, "Shopfronts", shop, kit.Shopfront, b.Name);
            Emit(root, "Roofs", roof, kit.Roof, b.Name);
            Emit(root, "Metal", metal, kit.Metal, b.Name);
            for (int i = 0; i < neon.Length; i++) Emit(root, $"Neon{DistrictKit.NeonNames[i]}", neon[i], kit.NeonStrips[i], b.Name, shadows: false);
            for (int i = 0; i < signs.Length; i++) Emit(root, $"Signs{DistrictKit.NeonNames[i]}", signs[i], kit.Signs[i], b.Name, shadows: false);
            for (int i = 0; i < billboards.Length; i++) Emit(root, $"Billboard{i}", billboards[i], kit.Billboards[i], b.Name, shadows: false);
            var sec = Emit(root, "SecurityStrips", security, kit.Security, b.Name, shadows: false);
            if (sec != null) SecurityStrips[b.Name] = sec.GetComponent<Renderer>();
        }

        private GameObject Emit(Transform parent, string name, MeshBuilder builder, Material material, string prefix, bool shadows = true)
        {
            if (builder.IsEmpty) return null;
            return DistrictKit.Renderer(name, parent, DistrictKit.SaveMesh(builder, $"{prefix}_{name}"), material, environment, shadows);
        }

        public struct Side
        {
            public Vector3 Start, End;     // along the pavement edge at kerb level
            public Vector3 Normal;         // outward, towards the road
            public float Path;             // pavement width
            public float Depth;            // block depth behind this side
        }

        public static IEnumerable<Side> Sides(Block b)
        {
            foreach (var side in RawSides(b))
            {
                if (b.Clip.width <= 0f) { yield return side; continue; }
                var s = side;
                s.Start = ClampXZ(s.Start, b.Clip);
                s.End = ClampXZ(s.End, b.Clip);
                if (Vector3.Distance(s.Start, s.End) > 1f) yield return s;
            }
        }

        private static Vector3 ClampXZ(Vector3 p, Rect r) => new(Mathf.Clamp(p.x, r.xMin, r.xMax), p.y, Mathf.Clamp(p.z, r.yMin, r.yMax));

        private static IEnumerable<Side> RawSides(Block b)
        {
            float h = KerbHeight;
            if (b.N) yield return new Side { Start = new(b.X1, h, b.Z1), End = new(b.X0, h, b.Z1), Normal = Vector3.forward, Path = b.PathN, Depth = b.Z1 - b.Z0 };
            if (b.E) yield return new Side { Start = new(b.X1, h, b.Z0), End = new(b.X1, h, b.Z1), Normal = Vector3.right, Path = b.PathE, Depth = b.X1 - b.X0 };
            if (b.S) yield return new Side { Start = new(b.X0, h, b.Z0), End = new(b.X1, h, b.Z0), Normal = Vector3.back, Path = b.PathS, Depth = b.Z1 - b.Z0 };
            if (b.W) yield return new Side { Start = new(b.X0, h, b.Z1), End = new(b.X0, h, b.Z0), Normal = Vector3.left, Path = b.PathW, Depth = b.X1 - b.X0 };
        }

        private float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        private bool Chance(float p) => rng.NextDouble() < p;

        private static readonly string[] ShopWords = { "RAMEN", "HOTEL", "ARCADE", "NOODLE BAR", "OPEN 24H", "CLUB VOLT", "KAIJU", "NEON RIFT" };

        /// <summary>Continuous podium street wall along one side: shopfront base, lit upper floors, crown strip, signs.</summary>
        private void BuildStreetWall(Block b, Side side, MeshBuilder[] facades, MeshBuilder shop, MeshBuilder roof, MeshBuilder security,
                                     MeshBuilder metal, MeshBuilder[] neon, MeshBuilder[] signs, MeshBuilder[] billboards, Transform colliders)
        {
            Vector3 along = (side.End - side.Start).normalized;
            float length = Vector3.Distance(side.Start, side.End);
            var rotation = Quaternion.LookRotation(side.Normal);
            float maxDepth = Mathf.Min(24f, side.Depth * 0.42f);
            float s = 0f;
            int index = 0;
            while (s < length - 3f)
            {
                float seg = Range(22f, 46f);
                if (length - (s + seg) < 12f) seg = length - s;
                float centreS = s + seg * 0.5f;
                Vector3 front = side.Start + along * centreS - side.Normal * side.Path;
                float depth = Range(Mathf.Min(14f, maxDepth), maxDepth);
                s += seg;
                index++;
                // Keep the street wall closed: next to a hero building, shrink the podium rather than drop it.
                // Only where the hero itself stands on the pavement line is the segment left to the hero.
                while (depth > 4f && Overlaps(FootprintRect(front, rotation, seg, depth), 1.5f)) depth -= 2f;
                if (Overlaps(FootprintRect(front, rotation, seg, depth), 1.5f)) continue;
                Podiums++;

                bool tall = Chance(0.16f);
                float height = tall ? Range(22f, 36f) : Range(7.5f, 16f);
                float setback = Chance(0.5f) ? Range(0.4f, 1.4f) : 0f;
                var fb = facades[rng.Next(facades.Length)];
                Vector2 offset = new(Range(0f, 1f), 0f);

                // Ground-floor shopfronts, then the upper floors set back a little.
                shop.Box(front, rotation, new Vector3(seg, 4.5f, depth), new Vector2(18f, 4.5f), new Vector2(Range(0f, 1f), 0f), setback > 0f ? roof : null, new Vector2(8f, 8f));
                Vector3 upperFront = front + Vector3.up * 4.5f - side.Normal * setback;
                fb.Box(upperFront, rotation, new Vector3(seg, height - 4.5f, depth - setback), new Vector2(12.8f, 14.4f), offset, roof, new Vector2(8f, 8f));

                // Crown strip in the security colour.
                security.OrientedBox(upperFront + Vector3.up * (height - 4.5f - 0.15f) + side.Normal * 0.1f, new Vector3(seg, 0.18f, 0.18f), rotation, 1f);
                // Awning neon line over the shopfronts.
                neon[rng.Next(neon.Length)].OrientedBox(front + Vector3.up * 4.4f + side.Normal * 0.35f, new Vector3(seg - 1.5f, 0.1f, 0.7f), rotation, 1f);

                // Facade sign (horizontal) or blade sign (vertical, sticking out over the pavement).
                if (Chance(0.55f))
                {
                    string word = ShopWords[rng.Next(ShopWords.Length)];
                    var uv = DistrictTextures.SignRect(word, out float aspect);
                    float hgt = Range(1.3f, 2.2f);
                    float width = Mathf.Min(hgt * aspect, seg - 3f);
                    hgt = width / aspect;
                    Vector3 c = upperFront + Vector3.up * Range(1.2f, Mathf.Max(1.3f, Mathf.Min(6f, height - 7f))) + side.Normal * 0.06f
                                + along * Range(-seg * 0.2f, seg * 0.2f);
                    signs[rng.Next(signs.Length)].Panel(c + Vector3.up * hgt * 0.5f, rotation, width, hgt, uv);
                }
                if (Chance(0.3f) && height > 10f)
                {
                    string word = ShopWords[rng.Next(ShopWords.Length)];
                    var uv = DistrictTextures.SignRect(word, out float aspect);
                    float letter = 1.1f;
                    float len = Mathf.Min(letter * aspect, height - 6f);
                    letter = len / aspect;
                    Vector3 c = upperFront + along * (seg * 0.5f - 1.2f) + side.Normal * (setback + 0.9f) + Vector3.up * (1f + len * 0.5f);
                    metal.OrientedBox(c, new Vector3(0.2f, len + 0.4f, 1.5f), rotation, 1f);
                    int colour = rng.Next(signs.Length);
                    var blade = rotation * Quaternion.Euler(0f, 90f, 0f);
                    signs[colour].Panel(c + blade * Vector3.forward * 0.11f, blade, letter, len, uv, rotateUv: true);
                    signs[colour].Panel(c - blade * Vector3.forward * 0.11f, rotation * Quaternion.Euler(0f, -90f, 0f), letter, len, uv, rotateUv: true);
                }
                // Rooftop billboard on some low podiums, facing the street.
                if (!tall && height < 14f && Chance(0.14f))
                {
                    Vector3 roofFront = upperFront + Vector3.up * (height - 4.5f) - side.Normal * 2f;
                    float bw = Mathf.Min(14f, seg - 4f);
                    metal.OrientedBox(roofFront + Vector3.up * 2f - along * bw * 0.35f, new Vector3(0.3f, 4f, 0.3f), rotation, 1f);
                    metal.OrientedBox(roofFront + Vector3.up * 2f + along * bw * 0.35f, new Vector3(0.3f, 4f, 0.3f), rotation, 1f);
                    billboards[rng.Next(billboards.Length)].Panel(roofFront + Vector3.up * (3f + bw * 0.25f) + side.Normal * 0.2f, rotation, bw, bw * 0.5f,
                                                                  new Rect(0f, 0f, 1f, 1f));
                }
                // Rooftop plant.
                for (int k = rng.Next(0, 3); k > 0; k--)
                    metal.OrientedBox(upperFront + Vector3.up * (height - 4.5f + 0.7f) - side.Normal * Range(3f, depth - 3f) + along * Range(-seg * 0.35f, seg * 0.35f),
                                      new Vector3(Range(1.5f, 3.5f), 1.4f, Range(1.5f, 3f)), rotation, 1f);

                var col = new GameObject($"Podium_{side.Normal.x:0}{side.Normal.z:0}_{index}") { layer = environment };
                col.transform.SetParent(colliders, false);
                col.transform.SetPositionAndRotation(front + Vector3.up * (height * 0.5f) - side.Normal * (depth * 0.5f), rotation);
                col.AddComponent<BoxCollider>().size = new Vector3(seg, height, depth);
                GameObjectUtility.SetStaticEditorFlags(col, StaticEditorFlags.BatchingStatic);
            }
        }

        private void BuildStreetLights(Side side, Transform parent)
        {
            Vector3 along = (side.End - side.Start).normalized;
            float length = Vector3.Distance(side.Start, side.End);
            bool narrow = side.Path < 2f;
            float spacing = narrow ? 30f : 40f;
            int count = 0;
            for (float s = spacing * 0.5f; s < length - 8f; s += spacing)
            {
                Vector3 p = side.Start + along * s - side.Normal * (narrow ? 0.6f : 0.8f);
                Vector2 flat = new(p.x, p.z);
                if (Intersections.Any(n => (n - flat).magnitude < 16f)) continue;
                if (Mathf.Abs(p.x) > 360f || p.z > 340f || p.z < -470f || p.x < -40f) continue;    // only along district roads
                bool lit = count++ % 2 == 0;
                DistrictKit.Place(lit ? kit.StreetLightLit : kit.StreetLight, parent, p, Quaternion.LookRotation(side.Normal));
                if (lit) RealtimeLights++;
            }
        }

        // ---------------- Buildings ----------------

        private void PlaceTowers()
        {
            var parent = new GameObject("Buildings").transform;
            parent.SetParent(Root, false);
            foreach (var t in Towers)
            {
                var def = catalog.Buildings.FirstOrDefault(d => d != null && d.Id == t.Id);
                if (def == null || def.Prefab == null)
                {
                    Debug.LogWarning($"[District] Building '{t.Id}' is not in the catalog.");
                    continue;
                }
                var rotation = Quaternion.Euler(0f, t.Yaw, 0f);
                var go = DistrictKit.Place(def.Prefab, parent, new Vector3(t.Position.x, 0f, t.Position.y), rotation, $"{def.Prefab.name}_{parent.childCount}");
                go.transform.localScale = Vector3.one * t.Scale;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                                         StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                // Only heroes stand on the pavement line and replace the street wall; other towers rise behind it,
                // so the wall stays closed and nobody can drive into a courtyard.
                if (def.Tier == BuildingTier.Hero)
                {
                    Vector3 size = def.Size * t.Scale;
                    var extents = rotation * new Vector3(size.x, 0f, size.z);
                    float w = Mathf.Abs(extents.x), d = Mathf.Abs(extents.z);
                    footprints.Add(new Rect(t.Position.x - w * 0.5f, t.Position.y - d * 0.5f, w, d));
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
            return footprints.Any(f => f.Overlaps(grown)) || Reserved.Any(f => f.Overlaps(grown));
        }

        // ---------------- Dressing ----------------

        private void BuildAlleyDressing()
        {
            var parent = new GameObject("AlleyDressing").transform;
            parent.SetParent(Root, false);
            // A chicane of jersey barriers: the shortcut is short, not easy.
            DistrictKit.Place(kit.JerseyBarrier, parent, new Vector3(156.9f, 0f, -72f), Quaternion.Euler(0f, 0f, 0f), "Chicane_A");
            DistrictKit.Place(kit.JerseyBarrier, parent, new Vector3(163.1f, 0f, -54f), Quaternion.Euler(0f, 0f, 0f), "Chicane_B");
            DistrictKit.Place(kit.JerseyBarrier, parent, new Vector3(156.9f, 0f, 22f), Quaternion.Euler(0f, 8f, 0f), "Chicane_C");

            // Overhead cables and pipes across the alley.
            var pipes = new MeshBuilder();
            for (float z = -88f; z < 56f; z += 13f)
                pipes.OrientedBox(new Vector3(160f, Range(6f, 11f), z), new Vector3(13f, 0.12f, 0.12f), Quaternion.Euler(0f, 0f, Range(-4f, 4f)), 1f);
            DistrictKit.Renderer("CablesAndPipes", parent, DistrictKit.SaveMesh(pipes, "District_AlleyPipes"), kit.Metal, environment, shadows: false);

            // Steam vents (cheap particle systems, additive glow so neon tints them).
            foreach (var p in new[] { new Vector3(155.6f, 0.2f, -80f), new Vector3(164.4f, 0.2f, -38f), new Vector3(155.6f, 0.2f, 35f), new Vector3(9f, 0.2f, -140f) })
                Steam(parent, p);
        }

        private void Steam(Transform parent, Vector3 position)
        {
            var go = new GameObject("SteamVent");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(-90f, 0f, 0f));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            var emission = ps.emission;
            emission.rateOverTime = 7f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.4f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = kit.Steam;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Overhead gantries that make the route choice readable: plates of neon text and arrows.</summary>
        private void BuildNavigationSigns()
        {
            var parent = new GameObject("NavigationSigns").transform;
            parent.SetParent(Root, false);
            var metal = new MeshBuilder();
            var plates = new MeshBuilder();
            var cyan = new MeshBuilder();
            var amber = new MeshBuilder();
            var magenta = new MeshBuilder();

            // Northbound on W Avenue before Market Street: the choice between the long boulevard and the alley shortcut.
            Gantry(metal, plates, new Vector3(0f, 0f, -160f), 0f, 7f, new[] { ("SECTOR 7", "↑", cyan, -3.5f), ("SERVICE ALLEY", "→", amber, 3.5f) });
            // Eastbound on Market Street, before the alley.
            Gantry(metal, plates, new Vector3(110f, 0f, -100f), 90f, 6f, new[] { ("DATA CORE", "←", amber, 0f) });
            // Eastbound on North Boulevard, before the access road.
            Gantry(metal, plates, new Vector3(100f, 0f, 300f), 90f, 7f, new[] { ("DATA CORE", "→", cyan, 3.5f) });
            // Escape guidance.
            Gantry(metal, plates, new Vector3(235f, 0f, 300f), 90f, 7f, new[] { ("EXPRESSWAY", "→", magenta, 3.5f) });
            Gantry(metal, plates, new Vector3(320f, 0f, 90f), 180f, 9f, new[] { ("EXTRACTION", "↑", magenta, 4.5f) });
            Gantry(metal, plates, new Vector3(240f, 0f, -100f), 90f, 6f, new[] { ("EXTRACTION", "→", magenta, 0f) });
            Gantry(metal, plates, new Vector3(320f, 0f, -250f), 180f, 9f, new[] { ("RIFT GATE", "↑", magenta, 0f) });

            DistrictKit.Renderer("GantryFrames", parent, DistrictKit.SaveMesh(metal, "District_GantryFrames"), kit.Metal, environment);
            DistrictKit.Renderer("GantryPlates", parent, DistrictKit.SaveMesh(plates, "District_GantryPlates"), kit.Roof, environment, shadows: false);
            DistrictKit.Renderer("GantryTextCyan", parent, DistrictKit.SaveMesh(cyan, "District_GantryTextCyan"), kit.Signs[0], environment, shadows: false);
            DistrictKit.Renderer("GantryTextAmber", parent, DistrictKit.SaveMesh(amber, "District_GantryTextAmber"), kit.Signs[3], environment, shadows: false);
            DistrictKit.Renderer("GantryTextMagenta", parent, DistrictKit.SaveMesh(magenta, "District_GantryTextMagenta"), kit.Signs[1], environment, shadows: false);
        }

        /// <summary>
        /// A sign gantry across a road at <paramref name="centre"/>; <paramref name="yaw"/> is the travel direction of the
        /// traffic it faces. Each plate: text, arrow, colour builder and lateral offset (right of travel = +).
        /// </summary>
        private void Gantry(MeshBuilder metal, MeshBuilder plates, Vector3 centre, float yaw, float halfWidth, (string text, string arrow, MeshBuilder colour, float lateral)[] items)
        {
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

        private void BuildReflectionProbes()
        {
            var parent = new GameObject("ReflectionProbes").transform;
            parent.SetParent(Root, false);
            var spots = new List<(Vector3 centre, Vector3 size)>();
            foreach (var road in Roads)
            {
                Vector2 dir = (road.B - road.A).normalized;
                float length = (road.B - road.A).magnitude;
                int count = Mathf.Max(1, Mathf.RoundToInt(length / 120f));
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = road.A + dir * (length * (i + 0.5f) / count);
                    float along = length / count + 10f;
                    float across = road.HalfWidth * 2f + 12f;
                    Vector3 size = Mathf.Abs(dir.x) > 0.5f ? new Vector3(along, 40f, across) : new Vector3(across, 40f, along);
                    spots.Add((new Vector3(p.x, 3f, p.y), size));
                }
            }
            spots.Add((new Vector3(160f, 3f, 127f), new Vector3(112f, 40f, 137f)));   // compound
            int n = 0;
            foreach (var (centre, size) in spots)
            {
                var go = new GameObject($"Probe_{n++:00}");
                go.transform.SetParent(parent, false);
                go.transform.position = centre;
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Baked;
                probe.boxProjection = true;
                probe.size = size;
                probe.center = new Vector3(0f, 17f, 0f);
                probe.resolution = 128;
                probe.hdr = true;
                probe.intensity = 1f;
                probe.blendDistance = 6f;
                probe.cullingMask = ~(1 << LayerMask.NameToLayer("Vehicle"));
            }
        }

        // ---------------- Geometry utils ----------------

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }
    }
}
