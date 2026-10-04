using System.Collections.Generic;
using System.Linq;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.EditorTools.District
{
    /// <summary>City zones: each has its own street-wall style, lamps, props, towers and identity on the HUD.</summary>
    public enum Zone { Sector7, Spire, Kowloon, Harbor, Lowtown, Outer }

    public enum LotKind { Plaza, Parking, ContainerYard, Construction, NightMarket }

    /// <summary>
    /// The layout of the Night Run city (≈ 1.1 × 1.4 km of streets around Sector 7). Everything here is plain data:
    /// roads, reserved areas, special lots, hero buildings and districts. <see cref="Decompose"/> turns it into blocks
    /// (cells between road lines merged where no road separates them) and <see cref="BuildNetwork"/> into the runtime
    /// <see cref="RoadNetwork"/> graph. Deterministic: same data, same city.
    ///
    ///   z 700 ┌─────────── Skyline Drive ──────────────────────────────────┐
    ///         │ Spire      │ Spire Av │                       │  Dock Rd   │ E Ring
    ///     480 ├──── Spire Boulevard ──┼───────────────────────┼────────────┤
    ///         │  (plaza)   │          │                       │            │
    ///     300 ├──── North Boulevard ──┼─ Access ──┬───────────┼────────────┤
    ///         │ Kowloon  W │  Sector 7│  DATA CORE│ E Expwy   │  Harbor    │
    ///      60 │ (market) A │          │  Alley    ├─ Freight Ln + SKYWAY ──┤
    ///    −100 ├──── Market Street ────┴───────────┼───────────┼────────────┤
    ///    −320 ├──── South Street ─────────────────┼ Yard Road ┼ (yard)     │
    ///         │ Lowtown (site)         Kiln St    │ ▼ Rift Gate tunnel     │
    ///    −520 ├──── Lowtown Road ─────────────────────────────┼────────────┤
    ///    −700 └──── Southern Perimeter ───────────────────────┴────────────┘
    ///      x −380   −200   0       160           320          560        760
    /// </summary>
    public static class CityLayout
    {
        public struct Road
        {
            public string Name;
            /// <summary>Street-sign text (must be in the sign atlas), or null.</summary>
            public string Sign;
            public Vector3 A, B;
            public float HalfWidth;
            public int Lanes;
            public float Sidewalk;
            public RoadClass Class;
            /// <summary>Half width of a central median (skyway pillars) kept clear of traffic, m.</summary>
            public float Median;
            /// <summary>Ground roads cut the pavement slabs and get markings; graph-only roads (skyway, compound, car park) do not.</summary>
            public bool Ground;
            public string BlockerId;

            public bool IsVertical => Mathf.Abs(A.x - B.x) < 0.01f;
            public float SpeedLimit => Class switch
            {
                RoadClass.Arterial => 38f, RoadClass.Skyway => 40f, RoadClass.Street => 28f, RoadClass.Tunnel => 26f,
                RoadClass.Alley => 16f, _ => 12f
            };
        }

        public struct Lot
        {
            public string Name;
            public LotKind Kind;
            public Rect Area;
        }

        public struct Hero
        {
            public string Id;
            public Vector2 Position;
            public float Yaw;
            public float Scale;
            public Hero(string id, float x, float z, float yaw, float scale = 1f) { Id = id; Position = new Vector2(x, z); Yaw = yaw; Scale = scale; }
        }

        public const float KerbHeight = 0.15f;
        public static readonly Rect CityBounds = Rect.MinMaxRect(-380f, -700f, 760f, 700f);
        public static readonly Rect WorldBounds = Rect.MinMaxRect(-620f, -940f, 1000f, 940f);

        public const string SkywayGateId = "skyway_gate";
        public const string AlleyGateId = "alley_gate";
        public const string CompoundGateId = "compound_gate";
        public const string CheckpointId = "expressway_checkpoint";
        public const string HarborCheckpointId = "harbor_checkpoint";

        private static Road R(string name, string sign, float ax, float az, float bx, float bz, float hw, int lanes, RoadClass c,
                              float sidewalk = 4f, float median = 0f, bool ground = true, float ay = 0f, float by = 0f, string blocker = null) =>
            new() { Name = name, Sign = sign, A = new Vector3(ax, ay, az), B = new Vector3(bx, by, bz), HalfWidth = hw, Lanes = lanes, Class = c,
                    Sidewalk = sidewalk, Median = median, Ground = ground, BlockerId = blocker };

        public static readonly Road[] Roads =
        {
            // North–south.
            R("Western Ring Road", "RING RD", -380, -700, -380, 700, 7f, 2, RoadClass.Arterial),
            R("Lantern Street", "LANTERN ST", -200, -320, -200, 300, 5f, 1, RoadClass.Street, sidewalk: 3f),
            R("W Avenue", "W AVENUE", 0, -700, 0, 700, 7f, 2, RoadClass.Arterial),
            R("Spire Avenue", "SPIRE AVE", 160, 300, 160, 700, 7f, 2, RoadClass.Arterial),
            R("Access Road", null, 160, 195, 160, 300, 6f, 1, RoadClass.Street, sidewalk: 3f),
            R("Service Alley", "SERVICE ALLEY", 160, -100, 160, 60, 5f, 1, RoadClass.Alley, sidewalk: 1.5f),
            R("Kiln Street", "KILN ST", 160, -700, 160, -320, 6f, 1, RoadClass.Street),
            // The expressway has a concrete median barrier between junctions (CityLandmarks): drivers keep to their carriageway.
            R("East Expressway", "EXPRESSWAY", 320, -405, 320, 700, 9f, 2, RoadClass.Arterial, median: 0.8f),
            R("Rift Gate Tunnel", null, 320, -470, 320, -405, 9f, 1, RoadClass.Tunnel, sidewalk: 0f),
            R("Dock Road", "DOCK RD", 560, -700, 560, 700, 7f, 2, RoadClass.Arterial),
            R("Eastern Ring Road", "RING RD", 760, -700, 760, 700, 7f, 2, RoadClass.Arterial),
            // East–west.
            R("Skyline Drive", "SKYLINE DR", -380, 700, 760, 700, 7f, 2, RoadClass.Arterial),
            R("Spire Boulevard", "SPIRE BLVD", -380, 480, 760, 480, 8f, 2, RoadClass.Arterial, sidewalk: 5f),
            R("North Boulevard", "N BOULEVARD", -380, 300, 760, 300, 7f, 2, RoadClass.Arterial),
            R("Lantern Cross", "LANTERN X", -380, 120, 0, 120, 5f, 1, RoadClass.Street, sidewalk: 3f),
            R("Freight Lane", "FREIGHT LN", 320, 60, 760, 60, 12f, 1, RoadClass.Street, median: 4.5f),
            R("Market Street", "MARKET ST", -380, -100, 760, -100, 6f, 1, RoadClass.Street),
            R("Fish Alley", "FISH ALLEY", -380, -210, 0, -210, 4f, 1, RoadClass.Alley, sidewalk: 1.5f),
            R("South Street", "SOUTH ST", -380, -320, 320, -320, 6f, 1, RoadClass.Street),
            R("Yard Road", "YARD RD", 320, -320, 760, -320, 7f, 1, RoadClass.Street),
            R("Lowtown Road", "LOWTOWN RD", -380, -520, 760, -520, 6f, 1, RoadClass.Street),
            R("Southern Perimeter", "RING RD", -380, -700, 760, -700, 7f, 2, RoadClass.Arterial),
            // Graph-only: the Data Core compound, the S7 car park and the elevated Harbor Skyway.
            // The compound drive loops round the west side of the core (the pedestal and uplink ring sit on the axis).
            R("Compound Drive", null, 160, 150, 160, 195, 6f, 1, RoadClass.Service, ground: false, blocker: CompoundGateId),
            R("Compound Loop", null, 146, 150, 160, 150, 5f, 1, RoadClass.Service, ground: false),
            R("Compound Loop", null, 146, 105, 146, 150, 5f, 1, RoadClass.Service, ground: false),
            R("Compound Loop", null, 146, 105, 160, 105, 5f, 1, RoadClass.Service, ground: false),
            R("Compound South", null, 160, 60, 160, 105, 6f, 1, RoadClass.Service, ground: false),
            R("Car Park", "PARKING", 128, 246, 160, 246, 4f, 1, RoadClass.Service, ground: false),
            R("Skyway West Ramp", "SKYWAY", 345, 60, 405, 60, 4.5f, 1, RoadClass.Skyway, ground: false, ay: 0f, by: 8f, blocker: SkywayGateId),
            R("Harbor Skyway", "SKYWAY", 405, 60, 680, 60, 4.5f, 1, RoadClass.Skyway, ground: false, ay: 8f, by: 8f),
            R("Skyway East Ramp", "SKYWAY", 680, 60, 740, 60, 4.5f, 1, RoadClass.Skyway, ground: false, ay: 8f, by: 0f),
        };

        /// <summary>Barriers that close graph edges, by world position (the edge under it gets the id).</summary>
        public static readonly (string id, Vector3 position)[] Blockers =
        {
            (AlleyGateId, new Vector3(160f, 0f, -20f)),
            (CheckpointId, new Vector3(320f, 0f, 170f)),
            (HarborCheckpointId, new Vector3(440f, 0f, -100f)),
        };

        /// <summary>Areas the street walls, slabs and towers leave alone (built by mission code).</summary>
        public static readonly Rect[] Reserved =
        {
            Rect.MinMaxRect(105f, 60f, 215f, 195f),        // Data Core compound
            Rect.MinMaxRect(306f, -477f, 334f, -403f),     // Rift Gate tunnel
            Rect.MinMaxRect(-44f, -271f, -7f, -239f),      // the crew garage on W Avenue (door and apron onto the avenue)
        };

        public static readonly Lot[] Lots =
        {
            new() { Name = "S7 Car Park", Kind = LotKind.Parking, Area = Rect.MinMaxRect(106f, 200f, 154f, 280f) },
            new() { Name = "Spire Plaza", Kind = LotKind.Plaza, Area = Rect.MinMaxRect(8f, 489f, 152f, 612f) },
            new() { Name = "Night Market", Kind = LotKind.NightMarket, Area = Rect.MinMaxRect(-372f, 128f, -206f, 214f) },
            new() { Name = "Harbor Container Yard", Kind = LotKind.ContainerYard, Area = Rect.MinMaxRect(570f, -310f, 750f, -110f) },
            new() { Name = "Lowtown Construction", Kind = LotKind.Construction, Area = Rect.MinMaxRect(-190f, -510f, -11f, -330f) },
        };

        /// <summary>Hand-placed buildings: Sector 7's curated heroes and towers, the Spire landmarks.</summary>
        public static readonly Hero[] Heroes =
        {
            // Sector 7 heroes on the pavement line.
            new("singapore_office", 39f, -255f, -90f),
            new("singapore_office", 263f, 261f, 0f),
            new("singapore_office", 361f, -150f, -90f),
            // Spire Heights: the London tower closes the plaza; Singapore offices line Spire Boulevard.
            new("london_skyscraper", 80f, 652f, 180f),
            new("singapore_office", 239f, 520f, 180f),
            new("singapore_office", -80f, 520f, 180f),
            new("singapore_office", 400f, 440f, 0f),
            new("singapore_office", -270f, 340f, 180f),
            // Sector 7 inner blocks (rise behind the street walls).
            new("asian_night_b2_008", 82f, -17f, 0f),
            new("asian_night_b1_008", 58f, 175f, 90f),
            new("asian_night_b1_005", 200f, 238f, 0f),
            new("asian_night_b1_003", 258f, 118f, 90f),
            new("asian_night_b2", 232f, -18f, 0f),
            new("asian_night_b1_009", 165f, -212f, 0f),
            new("asian_night_b2_005", 245f, -148f, 0f),
            new("asian_night_b1_004", 245f, -276f, 180f),
            new("asian_night_b1_003", 100f, -165f, 0f, 0.9f),
        };

        /// <summary>
        /// Full building models beyond the playable city. Empty since October 2026: the six Sketchfab skyline clusters that
        /// stood here (~1.25 km out, 36 k triangles plus shadow casting) changed under 0.2 % of pixels in street and aerial
        /// A/B captures, which is noise level. <see cref="SkylineBackdrop"/> carries the far city instead. The catalog
        /// entry (night_skyline_cluster) is kept in case a closer placement is wanted.
        /// </summary>
        public static readonly Hero[] Skyline = System.Array.Empty<Hero>();

        public static readonly (string id, string name, Rect area, Color colour)[] Districts =
        {
            ("sector7", "SECTOR 7", Rect.MinMaxRect(0f, -320f, 320f, 300f), new Color(0.3f, 0.9f, 1f)),
            ("kowloon", "KOWLOON MARKET", Rect.MinMaxRect(-400f, -320f, 0f, 300f), new Color(1f, 0.45f, 0.3f)),
            ("spire", "SPIRE HEIGHTS", Rect.MinMaxRect(-400f, 300f, 560f, 720f), new Color(0.65f, 0.75f, 1f)),
            ("lowtown", "LOWTOWN", Rect.MinMaxRect(-400f, -720f, 320f, -320f), new Color(1f, 0.7f, 0.35f)),
            ("harbor", "HARBOR YARDS", Rect.MinMaxRect(320f, -720f, 780f, 720f), new Color(1f, 0.6f, 0.2f)),
        };

        public static Zone ZoneAt(Vector2 p)
        {
            if (!CityBounds.Contains(p)) return Zone.Outer;
            if (p.y >= 300f && p.x < 560f) return Zone.Spire;
            if (p.x >= 320f) return Zone.Harbor;
            if (p.x < 0f && p.y >= -320f) return Zone.Kowloon;
            if (p.y < -320f) return Zone.Lowtown;
            return Zone.Sector7;
        }

        public static string DistrictName(Zone z) => z switch
        {
            Zone.Sector7 => "SECTOR 7", Zone.Spire => "SPIRE HEIGHTS", Zone.Kowloon => "KOWLOON MARKET",
            Zone.Harbor => "HARBOR YARDS", Zone.Lowtown => "LOWTOWN", _ => "OUTSKIRTS"
        };

        // ---------------- Blocks ----------------

        public struct Side
        {
            public Vector3 Start, End;     // along the kerb edge, at kerb height
            public Vector3 Normal;         // outward, towards the road
            public float Path;             // sidewalk width
            public float Depth;            // depth of the cell behind this side
            public int Road;               // index into Roads
        }

        public sealed class Cell
        {
            public Rect Grid;                  // between road centre lines
            public Rect Slab;                  // grid minus the carriageways it faces
            public Zone Zone;
            public int Block;
            public readonly List<Side> Sides = new();
            public bool Outer => Zone == Zone.Outer;
            public Vector2 Centre => Grid.center;
        }

        public sealed class Decomposition
        {
            public readonly List<Cell> Cells = new();
            public readonly Dictionary<int, List<Cell>> Blocks = new();
            public readonly List<Vector3> Nodes = new();
        }

        /// <summary>Cells between consecutive road lines; neighbouring cells without a road between them share a block.</summary>
        public static Decomposition Decompose()
        {
            var d = new Decomposition();
            var ground = Roads.Where(r => r.Ground).ToArray();
            var xs = new SortedSet<float> { WorldBounds.xMin, WorldBounds.xMax };
            var zs = new SortedSet<float> { WorldBounds.yMin, WorldBounds.yMax };
            foreach (var r in ground)
            {
                if (r.IsVertical) { xs.Add(r.A.x); zs.Add(r.A.z); zs.Add(r.B.z); }
                else { zs.Add(r.A.z); xs.Add(r.A.x); xs.Add(r.B.x); }
            }
            var xl = xs.ToList();
            var zl = zs.ToList();
            int nx = xl.Count - 1, nz = zl.Count - 1;
            var grid = new Cell[nx, nz];
            var parent = new int[nx * nz];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var cell = new Cell { Grid = Rect.MinMaxRect(xl[i], zl[j], xl[i + 1], zl[j + 1]) };
                    cell.Zone = ZoneAt(cell.Centre);
                    grid[i, j] = cell;
                }

            int RoadAlong(bool vertical, float line, float from, float to)
            {
                for (int k = 0; k < Roads.Length; k++)
                {
                    var r = Roads[k];
                    if (!r.Ground || r.IsVertical != vertical) continue;
                    float l = vertical ? r.A.x : r.A.z;
                    if (Mathf.Abs(l - line) > 0.5f) continue;
                    float r0 = vertical ? Mathf.Min(r.A.z, r.B.z) : Mathf.Min(r.A.x, r.B.x);
                    float r1 = vertical ? Mathf.Max(r.A.z, r.B.z) : Mathf.Max(r.A.x, r.B.x);
                    if (r0 <= from + 0.5f && r1 >= to - 0.5f) return k;
                }
                return -1;
            }

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var c = grid[i, j];
                    var g = c.Grid;
                    int west = RoadAlong(true, g.xMin, g.yMin, g.yMax), east = RoadAlong(true, g.xMax, g.yMin, g.yMax);
                    int south = RoadAlong(false, g.yMin, g.xMin, g.xMax), north = RoadAlong(false, g.yMax, g.xMin, g.xMax);
                    if (i + 1 < nx && east < 0) Union(i * nz + j, (i + 1) * nz + j);
                    if (j + 1 < nz && north < 0) Union(i * nz + j, i * nz + j + 1);
                    float hwW = west >= 0 ? Roads[west].HalfWidth : 0f, hwE = east >= 0 ? Roads[east].HalfWidth : 0f;
                    float hwS = south >= 0 ? Roads[south].HalfWidth : 0f, hwN = north >= 0 ? Roads[north].HalfWidth : 0f;
                    c.Slab = Rect.MinMaxRect(g.xMin + hwW, g.yMin + hwS, g.xMax - hwE, g.yMax - hwN);
                    var s = c.Slab;
                    float h = KerbHeight;
                    if (north >= 0) c.Sides.Add(new Side { Start = new(s.xMax, h, s.yMax), End = new(s.xMin, h, s.yMax), Normal = Vector3.forward, Path = Roads[north].Sidewalk, Depth = s.height, Road = north });
                    if (east >= 0) c.Sides.Add(new Side { Start = new(s.xMax, h, s.yMin), End = new(s.xMax, h, s.yMax), Normal = Vector3.right, Path = Roads[east].Sidewalk, Depth = s.width, Road = east });
                    if (south >= 0) c.Sides.Add(new Side { Start = new(s.xMin, h, s.yMin), End = new(s.xMax, h, s.yMin), Normal = Vector3.back, Path = Roads[south].Sidewalk, Depth = s.height, Road = south });
                    if (west >= 0) c.Sides.Add(new Side { Start = new(s.xMin, h, s.yMax), End = new(s.xMin, h, s.yMin), Normal = Vector3.left, Path = Roads[west].Sidewalk, Depth = s.width, Road = west });
                    d.Cells.Add(c);
                }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var c = grid[i, j];
                    c.Block = Find(i * nz + j);
                    if (!d.Blocks.TryGetValue(c.Block, out var list)) d.Blocks[c.Block] = list = new List<Cell>();
                    list.Add(c);
                }
            d.Nodes.AddRange(NetworkNodes());
            return d;
        }

        /// <summary>A rectangle minus every carriageway, reserved area and (optionally) lot: the pavement pieces left.</summary>
        public static List<Rect> SlabPieces(Rect slab, bool cutLots)
        {
            var pieces = new List<Rect> { slab };
            var cuts = new List<Rect>();
            foreach (var r in Roads)
            {
                if (!r.Ground) continue;
                float ext = r.HalfWidth;
                cuts.Add(r.IsVertical
                    ? Rect.MinMaxRect(r.A.x - r.HalfWidth, Mathf.Min(r.A.z, r.B.z) - ext, r.A.x + r.HalfWidth, Mathf.Max(r.A.z, r.B.z) + ext)
                    : Rect.MinMaxRect(Mathf.Min(r.A.x, r.B.x) - ext, r.A.z - r.HalfWidth, Mathf.Max(r.A.x, r.B.x) + ext, r.A.z + r.HalfWidth));
            }
            cuts.AddRange(Reserved);
            if (cutLots) cuts.AddRange(Lots.Where(l => l.Kind == LotKind.Parking).Select(l => l.Area));
            foreach (var cut in cuts)
            {
                var next = new List<Rect>();
                foreach (var p in pieces) Subtract(p, cut, next);
                pieces = next;
            }
            pieces.RemoveAll(p => p.width < 0.5f || p.height < 0.5f);
            return pieces;
        }

        private static void Subtract(Rect a, Rect b, List<Rect> output)
        {
            if (!a.Overlaps(b)) { output.Add(a); return; }
            float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
            float z0 = Mathf.Max(a.yMin, b.yMin), z1 = Mathf.Min(a.yMax, b.yMax);
            if (a.yMin < z0) output.Add(Rect.MinMaxRect(a.xMin, a.yMin, a.xMax, z0));
            if (z1 < a.yMax) output.Add(Rect.MinMaxRect(a.xMin, z1, a.xMax, a.yMax));
            if (a.xMin < x0) output.Add(Rect.MinMaxRect(a.xMin, z0, x0, z1));
            if (x1 < a.xMax) output.Add(Rect.MinMaxRect(x1, z0, a.xMax, z1));
        }

        /// <summary>True if the XZ rect overlaps a carriageway (ground or graph-only), a reserved area or a lot.</summary>
        public static bool Blocked(Rect r, bool includeLots = true)
        {
            foreach (var road in Roads)
            {
                if (road.Class == RoadClass.Service && !road.Ground) continue;
                float hw = road.HalfWidth + 1f;
                var c = Rect.MinMaxRect(Mathf.Min(road.A.x, road.B.x) - hw, Mathf.Min(road.A.z, road.B.z) - hw,
                                        Mathf.Max(road.A.x, road.B.x) + hw, Mathf.Max(road.A.z, road.B.z) + hw);
                if (c.Overlaps(r)) return true;
            }
            if (Reserved.Any(x => x.Overlaps(r))) return true;
            return includeLots && Lots.Any(l => l.Area.Overlaps(r));
        }

        public static bool InLot(Vector2 p, out Lot lot)
        {
            foreach (var l in Lots)
                if (l.Area.Contains(p)) { lot = l; return true; }
            lot = default;
            return false;
        }

        // ---------------- Road network ----------------

        private static bool OnSegment(Vector3 p, Road r)
        {
            if (Mathf.Abs(p.y - Mathf.Lerp(r.A.y, r.B.y, RoadNetwork.ProjectXZ(p, r.A, r.B))) > 0.3f) return false;
            return DistanceToSegment(new Vector2(p.x, p.z), new Vector2(r.A.x, r.A.z), new Vector2(r.B.x, r.B.z)) < 0.5f;
        }

        /// <summary>Road ends, crossings and graph junctions (deduplicated).</summary>
        public static List<Vector3> NetworkNodes()
        {
            var nodes = new List<Vector3>();
            void Add(Vector3 p)
            {
                if (nodes.All(n => (n - p).sqrMagnitude > 0.25f)) nodes.Add(p);
            }
            foreach (var r in Roads) { Add(r.A); Add(r.B); }
            // Crossings of perpendicular ground roads.
            foreach (var v in Roads.Where(r => r.Ground && r.IsVertical))
                foreach (var h in Roads.Where(r => r.Ground && !r.IsVertical))
                {
                    var p = new Vector3(v.A.x, 0f, h.A.z);
                    if (OnSegment(p, v) && OnSegment(p, h)) Add(p);
                }
            // Blockers split their edge so a gate closes only its own segment.
            foreach (var (_, position) in Blockers)
            {
                var road = Roads.First(r => OnSegment(position, r));
                Vector3 dir = (road.B - road.A).normalized;
                Add(position - dir * 6f);
                Add(position + dir * 6f);
            }
            return nodes;
        }

        public static RoadNetwork BuildNetwork(RoadNetwork asset)
        {
            var nodes = NetworkNodes();
            var edges = new List<RoadEdge>();
            foreach (var r in Roads)
            {
                float len = Vector3.Distance(r.A, r.B);
                var on = nodes.Select((p, i) => (p, i)).Where(x => OnSegment(x.p, r))
                              .OrderBy(x => Vector3.Dot(x.p - r.A, r.B - r.A) / len).ToList();
                for (int k = 0; k + 1 < on.Count; k++)
                {
                    Vector3 mid = (on[k].p + on[k + 1].p) * 0.5f;
                    string blocker = r.BlockerId;
                    foreach (var (id, position) in Blockers)
                        if (OnSegment(position, r) && Vector3.Distance(mid, position) < 6.5f) blocker = id;
                    edges.Add(new RoadEdge
                    {
                        a = on[k].i, b = on[k + 1].i, halfWidth = r.HalfWidth, lanesPerDirection = r.Lanes, median = r.Median, roadClass = r.Class,
                        street = r.Name, speedLimit = r.SpeedLimit, blockerId = blocker ?? string.Empty
                    });
                }
            }
            var districts = Districts.Select(x => new CityDistrict { id = x.id, displayName = x.name, area = x.area, colour = x.colour }).ToList();
            asset.EditorConfigure("NEON RIFT", nodes.Select(p => new RoadNode { position = p }).ToList(), edges, districts, CityBounds);
            return asset;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }
    }
}
