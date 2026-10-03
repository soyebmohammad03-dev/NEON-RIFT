using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Fills the inside of a block (everything behind the podium street walls that no tower, lot or sidewalk uses) with
    /// urban fabric that has a reason to be there: back buildings, service yards, surface and multi-storey car parks,
    /// pocket parks, courtyards, fenced utility compounds, and in the harbor warehouses with loading docks and storage
    /// yards. Free space is found on a 2 m grid, packed into parcels (largest free rectangle, split to parcel size) and
    /// each parcel gets a use from the zone's mix. Masses go into the block's combined meshes; small furniture, fences
    /// and parked vehicles go to the Clutter layer, which the camera culls beyond 90 m (they sit inside blocks, seen
    /// through gaps and from above, never on the racing line). Interior light is emissive only (lamp
    /// heads and ground pools): no real-time lights, so the street <see cref="NeonRift.Gameplay.LightBudget"/> is not
    /// diluted. Seeded per block, so changing this class never reshuffles the street walls.
    /// </summary>
    public sealed class CityInfill
    {
        public enum Use { Building, ServiceYard, Parking, Garage, Park, Courtyard, Utility, Warehouse, Storage }

        private const float Cell = 2f;
        private static readonly float Kerb = CityLayout.KerbHeight;

        private readonly DistrictKit kit;
        private readonly int environment;
        private System.Random rng;
        private BlockMeshes meshes, detail;
        /// <summary>Clutter meshes per 64 m cell, so the layer's distance cull works on small bounds.</summary>
        private readonly Dictionary<Vector2Int, BlockMeshes> clutterCells = new();
        private const float ClutterCell = 64f;

        private BlockMeshes ClutterAt(Vector2 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / ClutterCell), Mathf.FloorToInt(p.y / ClutterCell));
            if (!clutterCells.TryGetValue(key, out var m)) clutterCells[key] = m = new BlockMeshes();
            return m;
        }
        private Transform colliders;
        private bool solid;

        public Dictionary<Use, int> Counts { get; } = System.Enum.GetValues(typeof(Use)).Cast<Use>().ToDictionary(u => u, _ => 0);
        public int Vehicles { get; private set; }
        public int YardLamps { get; private set; }
        public float FilledArea { get; private set; }
        public float FreeAreaBefore { get; private set; }
        /// <summary>Every parcel built, with its use (review shots, docs, tests).</summary>
        public List<(Use use, Rect area)> Parcels { get; } = new();

        public CityInfill(DistrictKit kit, int environment)
        {
            this.kit = kit;
            this.environment = environment;
        }

        private float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        private float Range(Vector2 r) => Range(r.x, r.y);
        private bool Chance(float p) => rng.NextDouble() < p;
        private T Pick<T>(IReadOnlyList<T> list) => list[rng.Next(list.Count)];

        // ---------------- Free space ----------------

        /// <summary>
        /// Fills one block. <paramref name="occupied"/> holds every footprint already standing there (podiums, towers,
        /// heroes). Outer (backdrop) blocks get buildings only, without colliders.
        /// </summary>
        public void Fill(int blockIndex, Zone zone, NightRunDistrict.ZoneStyle style, List<CityLayout.Cell> cells, IReadOnlyList<Rect> occupied,
                         BlockMeshes blockMeshes, Transform blockRoot, string blockName, Transform blockColliders)
        {
            clutterCells.Clear();
            rng = new System.Random(9100 + blockIndex * 37);
            meshes = blockMeshes;
            colliders = blockColliders;
            solid = zone != Zone.Outer;

            Rect bounds = cells[0].Grid;
            foreach (var c in cells)
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, c.Grid.xMin), Mathf.Min(bounds.yMin, c.Grid.yMin),
                                         Mathf.Max(bounds.xMax, c.Grid.xMax), Mathf.Max(bounds.yMax, c.Grid.yMax));
            int nx = Mathf.CeilToInt(bounds.width / Cell), nz = Mathf.CeilToInt(bounds.height / Cell);
            var free = new bool[nx, nz];
            var slabs = cells.SelectMany(c => CityLayout.SlabPieces(c.Slab, cutLots: true)).ToList();
            var sides = cells.SelectMany(c => c.Sides).ToList();
            var blocked = occupied.Where(r => r.Overlaps(bounds)).Select(r => Grow(r, 1.5f)).ToList();
            blocked.AddRange(CityLayout.Reserved.Select(r => Grow(r, 2f)));
            blocked.AddRange(CityLayout.Lots.Select(l => Grow(l.Area, 2f)));
            int freeCells = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var p = new Vector2(bounds.xMin + (i + 0.5f) * Cell, bounds.yMin + (j + 0.5f) * Cell);
                    if (!slabs.Any(s => Grow(s, -1f).Contains(p))) continue;
                    if (blocked.Any(r => r.Contains(p))) continue;
                    // Keep the sidewalks (and a margin behind them) clear.
                    if (sides.Any(s => CityLayout.DistanceToSegment(p, Flat(s.Start), Flat(s.End)) < s.Path + 2f)) continue;
                    free[i, j] = true;
                    freeCells++;
                }
            FreeAreaBefore += freeCells * Cell * Cell;

            Rect ToWorld((int x, int z, int w, int h) r) =>
                new(bounds.xMin + r.x * Cell, bounds.yMin + r.z * Cell, r.w * Cell, r.h * Cell);
            void Mark((int x, int z, int w, int h) r)
            {
                for (int i = r.x; i < r.x + r.w; i++)
                    for (int j = r.z; j < r.z + r.h; j++) free[i, j] = false;
            }

            // Parcels: the largest free rectangle each time, split down to parcel size.
            var parcels = new List<Rect>();
            for (int guard = 0; guard < 200; guard++)
            {
                var r = Largest(free, nx, nz, 4);
                if (r.w == 0 || r.w * r.h * Cell * Cell < 120f) break;
                Mark(r);
                Split(ToWorld(r), zone == Zone.Harbor ? 70f : 58f, parcels);
            }
            foreach (var parcel in parcels.OrderBy(p => p.x).ThenBy(p => p.y))
            {
                detail = ClutterAt(parcel.center);
                var use = Choose(zone, parcel);
                Counts[use]++;
                Parcels.Add((use, parcel));
                FilledArea += parcel.width * parcel.height;
                Build(use, zone, style, parcel);
            }
            // Slivers left behind the street walls: low annexes and back-of-house clutter.
            for (int guard = 0; guard < 60; guard++)
            {
                var r = Largest(free, nx, nz, 2);
                if (r.w == 0 || r.w * r.h * Cell * Cell < 24f) break;
                Mark(r);
                var rect = ToWorld(r);
                detail = ClutterAt(rect.center);
                FilledArea += rect.width * rect.height;
                if (Mathf.Min(rect.width, rect.height) >= 6f && Chance(0.75f)) Annex(style, rect);
                else if (solid) BackOfHouse(rect);
            }
            int clutterLayer = LayerMask.NameToLayer("Clutter");
            foreach (var pair in clutterCells.OrderBy(c => c.Key.x).ThenBy(c => c.Key.y))
            {
                var cell = new GameObject($"Clutter_{pair.Key.x}_{pair.Key.y}") { layer = clutterLayer };
                cell.transform.SetParent(blockRoot, false);
                pair.Value.Emit(cell.transform, $"{blockName}_Clutter_{pair.Key.x}_{pair.Key.y}", clutterLayer);
            }
        }

        private static Vector2 Flat(Vector3 p) => new(p.x, p.z);
        private static Rect Grow(Rect r, float m) => Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);

        /// <summary>Largest all-free rectangle (cells) with both sides at least <paramref name="min"/> cells; w = 0 if none.</summary>
        private static (int x, int z, int w, int h) Largest(bool[,] free, int nx, int nz, int min)
        {
            var heights = new int[nx];
            (int x, int z, int w, int h) best = (0, 0, 0, 0);
            int bestArea = 0;
            var stack = new Stack<int>();
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++) heights[i] = free[i, j] ? heights[i] + 1 : 0;
                stack.Clear();
                for (int i = 0; i <= nx; i++)
                {
                    int h = i == nx ? 0 : heights[i];
                    while (stack.Count > 0 && heights[stack.Peek()] >= h)
                    {
                        int top = stack.Pop();
                        int height = heights[top];
                        int left = stack.Count == 0 ? 0 : stack.Peek() + 1;
                        int width = i - left;
                        // The tallest rectangle under the bar may be too thin: also try it capped to the minimum width.
                        if (height >= min && width >= min && width * height > bestArea)
                        {
                            bestArea = width * height;
                            best = (left, j - height + 1, width, height);
                        }
                    }
                    stack.Push(i);
                }
            }
            return best;
        }

        private void Split(Rect r, float max, List<Rect> output)
        {
            if (r.width <= max && r.height <= max) { output.Add(r); return; }
            float t = Range(0.38f, 0.62f);
            if (r.width >= r.height)
            {
                float x = Mathf.Round((r.xMin + r.width * t) / Cell) * Cell;
                Split(Rect.MinMaxRect(r.xMin, r.yMin, x, r.yMax), max, output);
                Split(Rect.MinMaxRect(x, r.yMin, r.xMax, r.yMax), max, output);
            }
            else
            {
                float z = Mathf.Round((r.yMin + r.height * t) / Cell) * Cell;
                Split(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, z), max, output);
                Split(Rect.MinMaxRect(r.xMin, z, r.xMax, r.yMax), max, output);
            }
        }

        // ---------------- Uses ----------------

        private Use Choose(Zone zone, Rect r)
        {
            float shortSide = Mathf.Min(r.width, r.height), area = r.width * r.height;
            (Use use, float weight)[] mix = zone switch
            {
                Zone.Spire => new[] { (Use.Building, 0.42f), (Use.Courtyard, 0.16f), (Use.Garage, 0.18f), (Use.Park, 0.14f), (Use.Parking, 0.06f), (Use.Utility, 0.04f) },
                Zone.Kowloon => new[] { (Use.Building, 0.62f), (Use.ServiceYard, 0.18f), (Use.Courtyard, 0.08f), (Use.Parking, 0.06f), (Use.Utility, 0.06f) },
                Zone.Harbor => new[] { (Use.Warehouse, 0.42f), (Use.Storage, 0.24f), (Use.Parking, 0.12f), (Use.ServiceYard, 0.12f), (Use.Utility, 0.1f) },
                Zone.Lowtown => new[] { (Use.Building, 0.48f), (Use.Park, 0.14f), (Use.Parking, 0.14f), (Use.ServiceYard, 0.16f), (Use.Utility, 0.08f) },
                Zone.Outer => new[] { (Use.Building, 1f) },
                _ => new[] { (Use.Building, 0.46f), (Use.ServiceYard, 0.18f), (Use.Parking, 0.12f), (Use.Garage, 0.1f), (Use.Courtyard, 0.08f), (Use.Utility, 0.06f) },
            };
            bool Fits(Use u) => u switch
            {
                Use.Parking => shortSide >= 18f,
                Use.Garage => shortSide >= 20f && Mathf.Max(r.width, r.height) >= 28f,
                Use.Park => shortSide >= 16f,
                Use.Courtyard => shortSide >= 12f && area <= 2200f,
                Use.Utility => area <= 1400f,
                Use.Warehouse => shortSide >= 18f,
                Use.Storage => shortSide >= 16f,
                Use.ServiceYard => shortSide >= 10f,
                _ => true
            };
            var options = mix.Where(m => Fits(m.use)).ToArray();
            if (options.Length == 0) return zone == Zone.Harbor ? Use.ServiceYard : Use.Building;
            float total = options.Sum(o => o.weight), pick = Range(0f, total);
            foreach (var (use, weight) in options)
            {
                if (pick < weight) return use;
                pick -= weight;
            }
            return options[^1].use;
        }

        private void Build(Use use, Zone zone, NightRunDistrict.ZoneStyle style, Rect r)
        {
            switch (use)
            {
                case Use.Building: Buildings(zone, style, r); break;
                case Use.ServiceYard: ServiceYard(zone, r); break;
                case Use.Parking: Parking(r); break;
                case Use.Garage: Garage(r); break;
                case Use.Park: Park(r); break;
                case Use.Courtyard: Courtyard(r); break;
                case Use.Utility: Utility(r); break;
                case Use.Warehouse: Warehouse(r); break;
                case Use.Storage: Storage(r); break;
            }
        }

        private void Collider(Vector3 centre, Vector3 size, Quaternion? rotation = null)
        {
            if (!solid) return;
            var go = new GameObject("Infill") { layer = environment };
            go.transform.SetParent(colliders, false);
            go.transform.SetPositionAndRotation(centre, rotation ?? Quaternion.identity);
            go.AddComponent<BoxCollider>().size = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // ---------------- Buildings ----------------

        /// <summary>Back buildings: the parcel split into masses with service passages, taller than the street wall.</summary>
        private void Buildings(Zone zone, NightRunDistrict.ZoneStyle style, Rect parcel)
        {
            var masses = new List<Rect>();
            Split(parcel, zone == Zone.Outer ? 44f : 34f, masses);
            foreach (var m in masses)
            {
                var r = Grow(m, -1.6f);
                if (r.width < 5f || r.height < 5f) continue;
                Vector2 range = zone switch
                {
                    Zone.Spire => new(20f, 46f), Zone.Kowloon => new(12f, 26f), Zone.Lowtown => new(12f, 28f),
                    Zone.Outer => new(18f, 44f), Zone.Harbor => new(10f, 16f), _ => new(14f, 32f)
                };
                Mass(style, r, Range(range), allowTier: zone != Zone.Harbor);
            }
        }

        private void Mass(NightRunDistrict.ZoneStyle style, Rect r, float height, bool allowTier)
        {
            var facade = kit.Facades[Pick(style.Facades)];
            var fb = meshes[facade];
            var roof = meshes[kit.Roof];
            var tile = DistrictTextures.FacadeTile;
            var offset = new Vector2(Range(0f, 1f), Range(0f, 1f));
            fb.Box(new Vector3(r.center.x, Kerb, r.yMax), Quaternion.identity, new Vector3(r.width, height, r.height), tile, offset, roof, new Vector2(8f, 8f));
            Parapet(r, Kerb + height);
            Collider(new Vector3(r.center.x, Kerb + height * 0.5f, r.center.y), new Vector3(r.width, height, r.height));
            float top = height;
            if (allowTier && height > 20f && Mathf.Min(r.width, r.height) > 16f && Chance(0.5f))
            {
                var t = Grow(r, -Range(3f, Mathf.Min(r.width, r.height) * 0.25f));
                float h2 = height * Range(0.25f, 0.6f);
                fb.Box(new Vector3(t.center.x, Kerb + height, t.yMax), Quaternion.identity, new Vector3(t.width, h2, t.height), tile, offset, roof, new Vector2(8f, 8f), vStart: height);
                Parapet(t, Kerb + height + h2);
                top += h2;
                r = t;
            }
            Rooftop(style, r, Kerb + top, top > 30f);
            // A lit service door at the base (back-of-house), on the Detail layer.
            var door = Chance(0.5f) ? new Vector3(r.center.x + Range(-r.width * 0.3f, r.width * 0.3f), Kerb, r.yMin) : new Vector3(r.xMin, Kerb, r.center.y + Range(-r.height * 0.3f, r.height * 0.3f));
            var facing = door.z == r.yMin ? Quaternion.LookRotation(Vector3.back) : Quaternion.LookRotation(Vector3.left);
            detail[kit.RollerDoor].OrientedBox(door + Vector3.up * 1.1f + facing * Vector3.forward * 0.04f, new Vector3(1.2f, 2.2f, 0.06f), facing, 1f);
            detail.Unshadowed(kit.WallPack).OrientedBox(door + Vector3.up * 2.6f + facing * Vector3.forward * 0.12f, new Vector3(0.4f, 0.2f, 0.16f), facing, 1f);
        }

        private void Parapet(Rect r, float y)
        {
            var c = meshes[kit.Concrete];
            const float h = 0.7f, t = 0.25f;
            c.OrientedBox(new Vector3(r.center.x, y + h * 0.5f, r.yMax - t * 0.5f), new Vector3(r.width, h, t), Quaternion.identity, 2f);
            c.OrientedBox(new Vector3(r.center.x, y + h * 0.5f, r.yMin + t * 0.5f), new Vector3(r.width, h, t), Quaternion.identity, 2f);
            c.OrientedBox(new Vector3(r.xMin + t * 0.5f, y + h * 0.5f, r.center.y), new Vector3(t, h, r.height - t * 2f), Quaternion.identity, 2f);
            c.OrientedBox(new Vector3(r.xMax - t * 0.5f, y + h * 0.5f, r.center.y), new Vector3(t, h, r.height - t * 2f), Quaternion.identity, 2f);
        }

        private void Rooftop(NightRunDistrict.ZoneStyle style, Rect r, float y, bool tall)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(r.width * r.height / 260f), 1, 5);
            for (int k = 0; k < n; k++)
            {
                var p = new Vector3(Range(r.xMin + 2.5f, r.xMax - 2.5f), y, Range(r.yMin + 2.5f, r.yMax - 2.5f));
                meshes[kit.Metal].OrientedBox(p + Vector3.up * 0.75f, new Vector3(Range(1.6f, 4f), 1.5f, Range(1.4f, 3f)), Quaternion.Euler(0f, 90f * rng.Next(2), 0f), 1f);
                if (Chance(0.5f)) meshes[kit.DarkPlastic].Cylinder(p + Vector3.up * 1.5f, 0.55f, 0.15f, 10, true);
            }
            if (style.RoofTanks && Chance(0.4f))
            {
                var p = new Vector3(Range(r.xMin + 3f, r.xMax - 3f), y, Range(r.yMin + 3f, r.yMax - 3f));
                foreach (var leg in new[] { new Vector3(-0.9f, 0, -0.9f), new Vector3(0.9f, 0, -0.9f), new Vector3(-0.9f, 0, 0.9f), new Vector3(0.9f, 0, 0.9f) })
                    meshes[kit.Metal].OrientedBox(p + leg + Vector3.up * 1f, new Vector3(0.12f, 2f, 0.12f), Quaternion.identity, 1f);
                meshes[kit.RollerDoor].Cylinder(p + Vector3.up * 2f, 1.3f, 2.4f, 12, true);
            }
            if (tall || Chance(0.15f))
            {
                var p = new Vector3(Range(r.xMin + 2f, r.xMax - 2f), y, Range(r.yMin + 2f, r.yMax - 2f));
                float h = Range(4f, 10f);
                meshes[kit.Metal].Cylinder(p, 0.08f, h, 6, false);
                meshes.Unshadowed(kit.AviationRed).OrientedBox(p + Vector3.up * h, new Vector3(0.22f, 0.22f, 0.22f), Quaternion.identity, 1f);
            }
        }

        /// <summary>Two- or three-storey annex filling a sliver behind the street wall.</summary>
        private void Annex(NightRunDistrict.ZoneStyle style, Rect sliver)
        {
            var r = Grow(sliver, -0.8f);
            if (r.width < 4f || r.height < 4f) return;
            float h = Range(5f, 9f);
            var fb = meshes[kit.Facades[Pick(style.Facades)]];
            fb.Box(new Vector3(r.center.x, Kerb, r.yMax), Quaternion.identity, new Vector3(r.width, h, r.height), DistrictTextures.FacadeTile,
                   new Vector2(Range(0f, 1f), Range(0f, 1f)), meshes[kit.Roof], new Vector2(8f, 8f));
            Collider(new Vector3(r.center.x, Kerb + h * 0.5f, r.center.y), new Vector3(r.width, h, r.height));
            if (Chance(0.6f))
                meshes[kit.Metal].OrientedBox(new Vector3(r.center.x, Kerb + h + 0.7f, r.center.y), new Vector3(Mathf.Min(3f, r.width - 1f), 1.4f, Mathf.Min(2.4f, r.height - 1f)), Quaternion.identity, 1f);
        }

        /// <summary>Clutter in a small leftover gap: bins, crates, a lit wall pack on a pole.</summary>
        private void BackOfHouse(Rect r)
        {
            var c = new Vector3(r.center.x, Kerb, r.center.y);
            var rot = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
            if (Chance(0.6f)) Dumpster(c, rot);
            else Crates(c, rot);
            if (Chance(0.3f)) YardLamp(c + rot * Vector3.right * 1.6f, 4.2f, DistrictKit.LampKind.Sodium);
        }

        // ---------------- Yards and lots ----------------

        private void Surface(Material material, Rect r, float lift = 0.006f, float tile = 6f) =>
            meshes.Unshadowed(material).Ground(new Vector3(r.xMin, Kerb + lift, r.yMin), new Vector3(r.xMax, Kerb + lift, r.yMax), tile);

        /// <summary>Back yard behind the shops: dumpsters, pallets, crates, a van or box truck, wall-pack lighting, loading bay lines.</summary>
        private void ServiceYard(Zone zone, Rect parcel)
        {
            var r = Grow(parcel, -1f);
            Surface(kit.Asphalt2, r);
            Fence(r, gapSide: rng.Next(4), height: 2.2f);
            // Bins and pallets along the long edges.
            bool alongX = r.width >= r.height;
            Vector3 dir = alongX ? Vector3.right : Vector3.forward, across = alongX ? Vector3.forward : Vector3.right;
            float len = alongX ? r.width : r.height, wid = alongX ? r.height : r.width;
            Vector3 origin = new(r.xMin, Kerb, r.yMin);
            var face = Quaternion.LookRotation(across);
            for (float s = 3f; s < len - 3f; s += Range(4f, 7f))
            {
                Vector3 p = origin + dir * s + across * 1.4f;
                switch (rng.Next(4))
                {
                    case 0: Dumpster(p, face); break;
                    case 1: Pallets(p, face); break;
                    case 2: Crates(p, face); break;
                    default: Barrels(p, face); break;
                }
            }
            // Loading bays on the far edge, a vehicle in some.
            var lines = detail.Unshadowed(kit.MarkingYellow);
            for (float s = 4f; s + 4f < len - 3f; s += 4.2f)
            {
                Vector3 a = origin + dir * s + across * (wid - 0.4f);
                lines.Strip(a + Vector3.up * 0.012f, a - across * Mathf.Min(8f, wid * 0.5f) + Vector3.up * 0.012f, 0.14f);
                if (Chance(0.35f) && wid > 14f)
                    Vehicle(a + dir * 2.1f - across * 4f, Quaternion.LookRotation(-across), zone == Zone.Harbor || Chance(0.4f) ? Kind.BoxTruck : Kind.Van);
            }
            YardLamp(origin + dir * (len * 0.3f) + across * (wid * 0.5f), 6f, DistrictKit.LampKind.Sodium);
            if (len > 30f) YardLamp(origin + dir * (len * 0.75f) + across * (wid * 0.5f), 6f, DistrictKit.LampKind.Sodium);
        }

        /// <summary>Surface car park: rows of bays either side of aisles, parked cars, lamp posts, pay station.</summary>
        private void Parking(Rect parcel)
        {
            var r = Grow(parcel, -1f);
            Surface(kit.Asphalt2, r);
            bool alongX = r.width >= r.height;
            Vector3 dir = alongX ? Vector3.right : Vector3.forward, across = alongX ? Vector3.forward : Vector3.right;
            float len = alongX ? r.width : r.height, wid = alongX ? r.height : r.width;
            Vector3 origin = new(r.xMin, Kerb, r.yMin);
            var lines = detail.Unshadowed(kit.Marking);
            const float bay = 2.6f, depth = 5f, aisle = 6.5f;
            float group = depth * 2f + aisle;
            int groups = Mathf.Max(1, Mathf.FloorToInt((wid - 1f) / group));
            float used = groups * group, start = (wid - used) * 0.5f;
            for (int g = 0; g < groups; g++)
            {
                float a0 = start + g * group;
                foreach (var (rowStart, facing) in new[] { (a0, 1f), (a0 + depth + aisle, -1f) })
                {
                    for (float s = 1.5f; s + bay < len - 1.5f; s += bay)
                    {
                        Vector3 b0 = origin + dir * s + across * rowStart, b1 = b0 + across * depth;
                        lines.Strip(b0 + Vector3.up * 0.012f, b1 + Vector3.up * 0.012f, 0.1f);
                        if (Chance(0.58f))
                        {
                            Vector3 centre = origin + dir * (s + bay * 0.5f) + across * (rowStart + depth * 0.5f);
                            // Nose in: the car faces away from the aisle.
                            var rot = Quaternion.LookRotation(across * (facing > 0f ? -1f : 1f));
                            Vehicle(centre, rot, Chance(0.75f) ? Kind.Sedan : Kind.Hatch);
                        }
                    }
                }
                for (float s = 10f; s < len - 6f; s += 22f) YardLamp(origin + dir * s + across * (a0 + depth + aisle * 0.5f), 7f, DistrictKit.LampKind.Led);
            }
            // Pay station and a PARKING sign at one corner.
            var corner = origin + across * 1.2f + dir * 1.2f;
            detail[kit.Metal].OrientedBox(corner + Vector3.up * 0.8f, new Vector3(0.6f, 1.6f, 0.4f), Quaternion.LookRotation(across), 1f);
            detail.Unshadowed(kit.CameraLed).OrientedBox(corner + Vector3.up * 1.25f + across * 0.21f, new Vector3(0.3f, 0.2f, 0.01f), Quaternion.LookRotation(across), 1f);
            Sign(corner + dir * 1.5f, Quaternion.LookRotation(across), "PARKING", kit.Signs[0]);
        }

        /// <summary>Multi-storey car park: open concrete decks on columns, lit soffits, cars on every deck, a lit sign.</summary>
        private void Garage(Rect parcel)
        {
            var r = Grow(parcel, -1.5f);
            int levels = rng.Next(2, 5);
            const float storey = 3.2f, slab = 0.35f;
            var concrete = meshes[kit.Concrete];
            var dark = meshes[kit.ConcreteDark];
            var soffit = meshes.Unshadowed(kit.YardLamp);
            for (float x = r.xMin + 0.3f; x <= r.xMax - 0.2f; x += Mathf.Max(7.5f, (r.width - 0.6f) / Mathf.Max(1, Mathf.Round((r.width - 0.6f) / 8f))))
                for (float z = r.yMin + 0.3f; z <= r.yMax - 0.2f; z += Mathf.Max(7.5f, (r.height - 0.6f) / Mathf.Max(1, Mathf.Round((r.height - 0.6f) / 8f))))
                    concrete.OrientedBox(new Vector3(x, Kerb + levels * storey * 0.5f, z), new Vector3(0.5f, levels * storey, 0.5f), Quaternion.identity, 2f);
            for (int l = 1; l <= levels; l++)
            {
                float y = Kerb + l * storey;
                concrete.Cuboid(new Vector3(r.xMin, y - slab, r.yMin), new Vector3(r.xMax, y, r.yMax), 4f, bottom: true);
                // Upstand wall on every deck edge (the top deck's is the roof parapet).
                foreach (var (c, s) in EdgeWalls(r, y, 1.05f)) dark.OrientedBox(c, s, Quaternion.identity, 2f);
                // Lit soffit strips under this deck, lighting the level below (they read from the street at night).
                for (float z = r.yMin + 4f; z < r.yMax - 3f; z += 8f)
                    soffit.OrientedBox(new Vector3(r.center.x, y - slab - 0.04f, z), new Vector3(r.width - 3f, 0.05f, 0.18f), Quaternion.identity, 1f);
            }
            // Parked cars on each level (ground and decks).
            bool alongX = r.width >= r.height;
            for (int l = 0; l <= levels; l++)
            {
                float y = l == 0 ? Kerb : Kerb + l * storey;
                for (float s = 2f; s < (alongX ? r.width : r.height) - 3f; s += 2.6f)
                    foreach (float edge in new[] { 3f, (alongX ? r.height : r.width) - 3f })
                    {
                        if (!Chance(l == levels ? 0.35f : 0.55f)) continue;
                        var p = alongX ? new Vector3(r.xMin + s, y, r.yMin + edge) : new Vector3(r.xMin + edge, y, r.yMin + s);
                        Vector3 inward = alongX ? (edge < 5f ? Vector3.forward : Vector3.back) : (edge < 5f ? Vector3.right : Vector3.left);
                        Vehicle(p, Quaternion.LookRotation(-inward), Chance(0.7f) ? Kind.Sedan : Kind.Hatch);
                    }
            }
            float topY = Kerb + levels * storey;
            for (float s = 6f; s < (alongX ? r.width : r.height) - 4f; s += 16f)
            {
                var p = alongX ? new Vector3(r.xMin + s, topY, r.center.y) : new Vector3(r.center.x, topY, r.yMin + s);
                YardLamp(p, 5f, DistrictKit.LampKind.Led);
            }
            Collider(new Vector3(r.center.x, Kerb + levels * storey * 0.5f, r.center.y), new Vector3(r.width, levels * storey + 1f, r.height));
            // Sign high on one corner.
            var uv = DistrictTextures.SignRect("PARKING", out float aspect);
            var face = Quaternion.LookRotation(Vector3.back);
            meshes.Unshadowed(kit.Signs[0]).Panel(new Vector3(r.xMin + 0.8f * aspect + 1f, topY + 0.5f, r.yMin - 0.05f), face, 1.4f * aspect, 1.4f, uv);
        }

        private static IEnumerable<(Vector3 centre, Vector3 size)> EdgeWalls(Rect r, float y, float h)
        {
            const float t = 0.2f;
            yield return (new Vector3(r.center.x, y + h * 0.5f, r.yMin + t * 0.5f), new Vector3(r.width, h, t));
            yield return (new Vector3(r.center.x, y + h * 0.5f, r.yMax - t * 0.5f), new Vector3(r.width, h, t));
            yield return (new Vector3(r.xMin + t * 0.5f, y + h * 0.5f, r.center.y), new Vector3(t, h, r.height - t * 2f));
            yield return (new Vector3(r.xMax - t * 0.5f, y + h * 0.5f, r.center.y), new Vector3(t, h, r.height - t * 2f));
        }

        /// <summary>Pocket park: lawn inside a clipped hedge, crossing paths, trees, benches, low path lights.</summary>
        private void Park(Rect parcel)
        {
            var r = Grow(parcel, -1f);
            meshes.Unshadowed(kit.Grass).Ground(new Vector3(r.xMin, Kerb + 0.02f, r.yMin), new Vector3(r.xMax, Kerb + 0.02f, r.yMax), 3f);
            // Hedge with a gap in the middle of each side.
            var hedge = meshes[kit.Foliage];
            foreach (var (c, s) in EdgeWalls(r, Kerb, 0.9f))
            {
                bool alongX = s.x > s.z;
                float l = alongX ? s.x : s.z;
                float half = (l - 3f) * 0.5f;
                Vector3 axis = alongX ? Vector3.right : Vector3.forward;
                Vector3 size = alongX ? new Vector3(half, 0.9f, 0.7f) : new Vector3(0.7f, 0.9f, half);
                hedge.OrientedBox(c - axis * (half * 0.5f + 1.5f), size, Quaternion.identity, 1f);
                hedge.OrientedBox(c + axis * (half * 0.5f + 1.5f), size, Quaternion.identity, 1f);
            }
            // Paths: a cross through the gaps.
            var path = meshes.Unshadowed(kit.Pavement);
            path.Strip(new Vector3(r.xMin, Kerb + 0.03f, r.center.y), new Vector3(r.xMax, Kerb + 0.03f, r.center.y), 2.2f);
            path.Strip(new Vector3(r.center.x, Kerb + 0.03f, r.yMin), new Vector3(r.center.x, Kerb + 0.03f, r.yMax), 2.2f);
            // Trees in the four quarters.
            int trees = Mathf.Clamp(Mathf.RoundToInt(r.width * r.height / 80f), 4, 26);
            for (int i = 0, tries = 0; i < trees && tries < trees * 4; tries++)
            {
                var p = new Vector3(Range(r.xMin + 2.5f, r.xMax - 2.5f), Kerb, Range(r.yMin + 2.5f, r.yMax - 2.5f));
                if (Mathf.Abs(p.x - r.center.x) < 2.6f || Mathf.Abs(p.z - r.center.y) < 2.6f) continue;
                Tree(p);
                i++;
            }
            // Benches and path lights along the paths.
            foreach (var (p, rot) in new[]
                     {
                         (new Vector3(r.center.x + 4f, Kerb, r.center.y + 1.8f), Quaternion.LookRotation(Vector3.back)),
                         (new Vector3(r.center.x - 4f, Kerb, r.center.y - 1.8f), Quaternion.LookRotation(Vector3.forward)),
                         (new Vector3(r.center.x + 1.8f, Kerb, r.center.y - 5f), Quaternion.LookRotation(Vector3.left)),
                     })
                Bench(p, rot);
            for (float x = r.xMin + 4f; x < r.xMax - 3f; x += 7f) Bollard(new Vector3(x, Kerb, r.center.y + 1.6f));
            for (float z = r.yMin + 4f; z < r.yMax - 3f; z += 7f) Bollard(new Vector3(r.center.x - 1.6f, Kerb, z));
            YardLamp(new Vector3(r.center.x + 2f, Kerb, r.center.y + 2f), 5f, DistrictKit.LampKind.Warm);
        }

        /// <summary>Paved courtyard between buildings: planters with trees, benches, a small lit fountain.</summary>
        private void Courtyard(Rect parcel)
        {
            var r = Grow(parcel, -1f);
            Surface(kit.ConcreteDark, r, 0.01f, 3f);
            var c = new Vector3(r.center.x, Kerb, r.center.y);
            float radius = Mathf.Min(r.width, r.height) * 0.18f;
            meshes[kit.Concrete].Ring(c + Vector3.up * 0.5f, radius - 0.4f, radius, 24);
            meshes[kit.Concrete].Cylinder(c, radius, 0.5f, 24, false);
            meshes[kit.Glass].Ring(c + Vector3.up * 0.38f, 0f, radius - 0.4f, 24);
            meshes.Unshadowed(kit.NeonStrips[2]).Ring(c + Vector3.up * 0.51f, radius - 0.42f, radius - 0.34f, 24);
            Collider(c + Vector3.up * 0.25f, new Vector3(radius * 2f, 0.5f, radius * 2f));
            for (float x = r.xMin + 3f; x < r.xMax - 2f; x += 7f)
                foreach (float z in new[] { r.yMin + 2.5f, r.yMax - 2.5f })
                {
                    var p = new Vector3(x, Kerb, z);
                    meshes[kit.ConcreteDark].OrientedBox(p + Vector3.up * 0.3f, new Vector3(1.6f, 0.6f, 1.6f), Quaternion.identity, 1f);
                    Tree(p + Vector3.up * 0.6f, small: true);
                }
            Bench(c + new Vector3(radius + 2f, 0f, 0f), Quaternion.LookRotation(Vector3.left));
            Bench(c - new Vector3(radius + 2f, 0f, 0f), Quaternion.LookRotation(Vector3.right));
            for (float x = r.xMin + 5f; x < r.xMax - 4f; x += 8f) Bollard(new Vector3(x, Kerb, r.center.y + radius + 2.5f));
        }

        /// <summary>Fenced substation / utility yard: transformers, cabinets, a cable tray, warning signs, a pole light.</summary>
        private void Utility(Rect parcel)
        {
            var r = Grow(parcel, -1.2f);
            Surface(kit.ConcreteDark, r, 0.01f, 2f);
            Fence(r, gapSide: -1, height: 2.6f);
            int n = Mathf.Clamp(Mathf.RoundToInt(r.width * r.height / 90f), 1, 4);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(Mathf.Lerp(r.xMin + 3f, r.xMax - 3f, (i + 0.5f) / n), Kerb, r.center.y + Range(-1f, 1f));
                meshes[kit.ConcreteDark].OrientedBox(p + Vector3.up * 0.15f, new Vector3(3.2f, 0.3f, 2.6f), Quaternion.identity, 1f);
                meshes[kit.Metal].OrientedBox(p + Vector3.up * 1.3f, new Vector3(2.4f, 2f, 1.8f), Quaternion.identity, 1f);
                for (float f = -1f; f <= 1f; f += 0.5f)
                    meshes[kit.Metal].OrientedBox(p + new Vector3(f, 1.2f, 1f), new Vector3(0.05f, 1.6f, 0.35f), Quaternion.identity, 1f);
                for (int k = -1; k <= 1; k++)
                    detail[kit.Containers[3]].Cylinder(p + new Vector3(k * 0.6f, 2.3f, 0f), 0.09f, 0.6f, 6, true);
                Collider(p + Vector3.up * 1.2f, new Vector3(3.2f, 2.4f, 2.6f));
            }
            for (float x = r.xMin + 2f; x < r.xMax - 2f; x += 3.5f)
            {
                var p = new Vector3(x, Kerb, r.yMax - 1.2f);
                detail[kit.ConcreteDark].OrientedBox(p + Vector3.up * 0.75f, new Vector3(1f, 1.5f, 0.5f), Quaternion.identity, 1f);
                detail.Unshadowed(kit.CameraLed).OrientedBox(p + new Vector3(0.3f, 1.3f, -0.26f), new Vector3(0.06f, 0.04f, 0.01f), Quaternion.identity, 1f);
            }
            meshes[kit.Metal].OrientedBox(new Vector3(r.center.x, Kerb + 3.2f, r.yMax - 2.2f), new Vector3(r.width - 3f, 0.12f, 0.5f), Quaternion.identity, 1f);
            Sign(new Vector3(r.xMin + 2f, Kerb, r.yMin - 0.1f), Quaternion.LookRotation(Vector3.back), "DANGER", kit.Signs[4], 1.6f);
            YardLamp(new Vector3(r.xMax - 1.5f, Kerb, r.yMin + 1.5f), 6f, DistrictKit.LampKind.Sodium);
        }

        /// <summary>Harbor warehouse: corrugated shed with skylights, a loading dock with roller doors, wall packs and trucks.</summary>
        private void Warehouse(Rect parcel)
        {
            var r = Grow(parcel, -1.5f);
            bool alongX = r.width >= r.height;
            // Dock apron on one long side.
            float apron = Mathf.Min(14f, (alongX ? r.height : r.width) * 0.35f);
            Rect shed = alongX ? Rect.MinMaxRect(r.xMin, r.yMin + apron, r.xMax, r.yMax) : Rect.MinMaxRect(r.xMin + apron, r.yMin, r.xMax, r.yMax);
            Rect yard = alongX ? Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + apron) : Rect.MinMaxRect(r.xMin, r.yMin, r.xMin + apron, r.yMax);
            float h = Range(8f, 13f);
            meshes[kit.RollerDoor].Box(new Vector3(shed.center.x, Kerb, shed.yMax), Quaternion.identity, new Vector3(shed.width, h, shed.height),
                                       new Vector2(6f, 6f), Vector2.zero, meshes[kit.Roof], new Vector2(8f, 8f));
            Parapet(shed, Kerb + h);
            for (float s = 6f; s < (alongX ? shed.width : shed.height) - 4f; s += 9f)
            {
                var p = alongX ? new Vector3(shed.xMin + s, Kerb + h + 0.05f, shed.center.y) : new Vector3(shed.center.x, Kerb + h + 0.05f, shed.yMin + s);
                meshes.Unshadowed(kit.Glass).OrientedBox(p, alongX ? new Vector3(2.4f, 0.1f, shed.height - 4f) : new Vector3(shed.width - 4f, 0.1f, 2.4f), Quaternion.identity, 1f);
            }
            Collider(new Vector3(shed.center.x, Kerb + h * 0.5f, shed.center.y), new Vector3(shed.width, h, shed.height));
            Surface(kit.Asphalt2, yard);
            Vector3 dockFaceDir = alongX ? Vector3.back : Vector3.left;
            var face = Quaternion.LookRotation(dockFaceDir);
            float dockLen = alongX ? shed.width : shed.height;
            Vector3 dockStart = alongX ? new Vector3(shed.xMin, Kerb, shed.yMin) : new Vector3(shed.xMin, Kerb, shed.yMin);
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            // Dock platform.
            meshes[kit.Concrete].OrientedBox(dockStart + along * (dockLen * 0.5f) + dockFaceDir * 1.5f + Vector3.up * 0.6f, alongX ? new Vector3(dockLen, 1.2f, 3f) : new Vector3(3f, 1.2f, dockLen), Quaternion.identity, 2f);
            Collider(dockStart + along * (dockLen * 0.5f) + dockFaceDir * 1.5f + Vector3.up * 0.6f, alongX ? new Vector3(dockLen, 1.2f, 3f) : new Vector3(3f, 1.2f, dockLen));
            for (float s = 5f; s < dockLen - 4f; s += 7f)
            {
                Vector3 door = dockStart + along * s + dockFaceDir * 0.05f;
                meshes[kit.Containers[3]].OrientedBox(door + Vector3.up * (1.2f + 1.8f), new Vector3(3.6f, 3.6f, 0.08f), face, 1f);
                meshes.Unshadowed(kit.WallPack).OrientedBox(door + Vector3.up * 5.4f + dockFaceDir * 0.15f, new Vector3(0.6f, 0.3f, 0.25f), face, 1f);
                detail.Unshadowed(kit.MarkingYellow).Strip(door + dockFaceDir * 3.2f + along * 2.1f + Vector3.up * 0.012f, door + dockFaceDir * (apron - 0.5f) + along * 2.1f + Vector3.up * 0.012f, 0.15f);
                if (Chance(0.4f) && apron > 11f) Vehicle(door + dockFaceDir * 6.6f, Quaternion.LookRotation(dockFaceDir), Kind.BoxTruck);
            }
            var yardLight = (alongX ? new Vector3(yard.center.x, Kerb, yard.yMin + 1f) : new Vector3(yard.xMin + 1f, Kerb, yard.center.y));
            YardLamp(yardLight, 8f, DistrictKit.LampKind.Sodium);
        }

        /// <summary>Open storage yard: container stacks in rows, pallet stacks, a flood mast, fence.</summary>
        private void Storage(Rect parcel)
        {
            var r = Grow(parcel, -1.2f);
            Surface(kit.Asphalt2, r);
            Fence(r, gapSide: rng.Next(4), height: 2.6f);
            const float length = 6.1f, height = 2.6f, width = 2.44f;
            bool alongX = r.width >= r.height;
            float len = alongX ? r.width : r.height, wid = alongX ? r.height : r.width;
            for (float a = 3f; a + width < wid - 3f; a += width + 0.3f)
            {
                if (Mathf.Repeat(a, 18f) > 12f) continue;   // aisles
                for (float s = 3f; s + length < len - 3f; s += length + 0.6f)
                {
                    int stack = rng.Next(0, 4);
                    if (stack == 0) continue;
                    var c = alongX ? new Vector3(r.xMin + s + length * 0.5f, Kerb, r.yMin + a + width * 0.5f) : new Vector3(r.xMin + a + width * 0.5f, Kerb, r.yMin + s + length * 0.5f);
                    var size = alongX ? new Vector3(length, height - 0.04f, width) : new Vector3(width, height - 0.04f, length);
                    for (int k = 0; k < stack; k++)
                        meshes[kit.Containers[rng.Next(kit.Containers.Length)]].OrientedBox(c + Vector3.up * (height * (k + 0.5f)), size, Quaternion.identity, 4f);
                    Collider(c + Vector3.up * (height * stack * 0.5f), new Vector3(size.x, height * stack, size.z));
                }
            }
            YardLamp(new Vector3(r.xMin + 1.5f, Kerb, r.yMin + 1.5f), 12f, DistrictKit.LampKind.Sodium);
            YardLamp(new Vector3(r.xMax - 1.5f, Kerb, r.yMax - 1.5f), 12f, DistrictKit.LampKind.Sodium);
        }

        // ---------------- Furniture ----------------

        /// <summary>
        /// Mesh fence: one panel and one top rail per run, posts every 3 m (Detail layer, no shadows), and one box collider
        /// per run. A gap of 6 m in the middle of side <paramref name="gapSide"/> (−1 = closed).
        /// </summary>
        private void Fence(Rect r, int gapSide, float height)
        {
            var metal = detail.Unshadowed(kit.Metal);
            var mesh = detail.Unshadowed(kit.Glass);
            Vector3[] corners = { new(r.xMin, Kerb, r.yMin), new(r.xMax, Kerb, r.yMin), new(r.xMax, Kerb, r.yMax), new(r.xMin, Kerb, r.yMax) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 p0 = corners[i], p1 = corners[(i + 1) % 4];
                float len = Vector3.Distance(p0, p1);
                Vector3 dir = (p1 - p0) / len;
                var rot = Quaternion.LookRotation(dir);
                var runs = i == gapSide ? new[] { (0f, len * 0.5f - 3f), (len * 0.5f + 3f, len) } : new[] { (0f, len) };
                foreach (var (a, b) in runs)
                {
                    float run = b - a;
                    if (run < 0.5f) continue;
                    Vector3 mid = p0 + dir * ((a + b) * 0.5f);
                    mesh.OrientedBox(mid + Vector3.up * (height * 0.5f), new Vector3(0.01f, height - 0.1f, run), rot, 1f);
                    metal.OrientedBox(mid + Vector3.up * (height - 0.03f), new Vector3(0.05f, 0.05f, run), rot, 1f);
                    for (float s = a; s <= b + 0.01f; s += Mathf.Max(2.5f, run / Mathf.Max(1, Mathf.Round(run / 3f))))
                        metal.OrientedBox(p0 + dir * s + Vector3.up * (height * 0.5f), new Vector3(0.07f, height, 0.07f), rot, 1f);
                    Collider(mid + Vector3.up * (height * 0.5f), new Vector3(0.1f, height, run), rot);
                }
            }
        }

        /// <summary>Emissive pole lamp with a ground pool (no real-time light).</summary>
        private void YardLamp(Vector3 p, float height, DistrictKit.LampKind kind)
        {
            meshes[kit.Metal].Cylinder(p, 0.07f, height, 6, true);
            meshes.Unshadowed(kind == DistrictKit.LampKind.Led ? kit.YardLamp : kit.LampHeads[(int)kind])
                  .OrientedBox(p + Vector3.up * (height + 0.1f), new Vector3(0.6f, 0.18f, 0.35f), Quaternion.identity, 1f);
            meshes.Unshadowed(kit.YardPools[(int)kind]).Decal(new Vector3(p.x, p.y + 0.03f, p.z), height * 2.2f, height * 2.2f);
            YardLamps++;
        }

        private void Bollard(Vector3 p)
        {
            detail[kit.Metal].Cylinder(p, 0.09f, 0.8f, 8, true);
            detail.Unshadowed(kit.YardLamp).Cylinder(p + Vector3.up * 0.62f, 0.095f, 0.1f, 8, false);
            detail.Unshadowed(kit.LightPools[2]).Decal(new Vector3(p.x, p.y + 0.035f, p.z), 2.4f, 2.4f);
        }

        private void Bench(Vector3 p, Quaternion rot)
        {
            var side = rot * Quaternion.Euler(0f, 90f, 0f);
            detail[kit.Metal].OrientedBox(p + Vector3.up * 0.45f, new Vector3(2f, 0.08f, 0.55f), side, 1f);
            detail[kit.Metal].OrientedBox(p + Vector3.up * 0.75f - rot * Vector3.forward * 0.25f, new Vector3(2f, 0.5f, 0.06f), side, 1f);
        }

        private void Tree(Vector3 p, bool small = false)
        {
            float scale = small ? 0.75f : Range(0.9f, 1.35f);
            float trunk = Range(2.2f, 3f) * scale;
            meshes[kit.Metal].Cylinder(p, 0.12f * scale, trunk, 6, false);
            Vector3 fork = p + Vector3.up * trunk;
            int clumps = rng.Next(5, 8);
            float spread = Range(1.1f, 1.6f) * scale;
            for (int i = 0; i < clumps; i++)
            {
                float a = i / (float)clumps * 360f + Range(-20f, 20f);
                float rr = i == 0 ? 0f : Range(0.45f, 1f) * spread;
                Vector3 c = fork + Vector3.up * Range(0.9f, 1.9f) * scale + Quaternion.Euler(0f, a, 0f) * Vector3.forward * rr;
                float size = Range(1.1f, 1.8f) * scale * (i == 0 ? 1.25f : 1f);
                meshes[kit.Canopy].OrientedBox(c, new Vector3(size, size * Range(0.6f, 0.85f), size * Range(0.8f, 1.1f)),
                                               Quaternion.Euler(Range(-15f, 15f), Range(0f, 360f), Range(-15f, 15f)), 1f);
            }
            Collider(p + Vector3.up * 1.2f, new Vector3(0.4f, 2.4f, 0.4f));
        }

        private void Dumpster(Vector3 p, Quaternion rot)
        {
            var side = rot * Quaternion.Euler(0f, 90f, 0f);
            detail[kit.Containers[rng.Next(kit.Containers.Length)]].OrientedBox(p + Vector3.up * 0.65f, new Vector3(1.9f, 1.1f, 1.1f), side, 1f);
            detail[kit.DarkPlastic].OrientedBox(p + Vector3.up * 1.25f, new Vector3(1.95f, 0.08f, 1.15f), side * Quaternion.Euler(Range(-8f, 0f), 0f, 0f), 1f);
            Collider(p + Vector3.up * 0.65f, new Vector3(1.1f, 1.3f, 1.9f), rot);
        }

        private void Crates(Vector3 p, Quaternion rot)
        {
            int n = rng.Next(1, 5);
            for (int i = 0; i < n; i++)
                detail[kit.RollerDoor].OrientedBox(p + Vector3.up * (0.3f + 0.6f * (i / 2)) + rot * new Vector3((i % 2) * 0.62f - 0.3f, 0f, 0f), new Vector3(0.6f, 0.6f, 0.6f),
                                                   rot * Quaternion.Euler(0f, Range(-12f, 12f), 0f), 1f);
        }

        private void Pallets(Vector3 p, Quaternion rot)
        {
            int n = rng.Next(2, 7);
            for (int i = 0; i < n; i++)
                detail[kit.Metal].OrientedBox(p + Vector3.up * (0.07f + i * 0.15f), new Vector3(1.2f, 0.13f, 1f), rot * Quaternion.Euler(0f, Range(-5f, 5f), 0f), 1f);
        }

        private void Barrels(Vector3 p, Quaternion rot)
        {
            var mat = kit.Containers[rng.Next(kit.Containers.Length)];
            detail[mat].Cylinder(p, 0.3f, 0.9f, 10, true);
            detail[mat].Cylinder(p + rot * Vector3.right * 0.65f, 0.3f, 0.9f, 10, true);
        }

        private void Sign(Vector3 foot, Quaternion facing, string text, Material colour, float height = 3.2f)
        {
            var uv = DistrictTextures.SignRect(text, out float aspect);
            float h = 0.55f, w = h * aspect;
            detail[kit.Metal].Cylinder(foot, 0.06f, height, 6, true);
            detail[kit.DarkPlastic].OrientedBox(foot + Vector3.up * height, new Vector3(w + 0.2f, h + 0.2f, 0.05f), facing, 1f);
            detail.Unshadowed(colour).Panel(foot + Vector3.up * height + facing * Vector3.forward * 0.03f, facing, w, h, uv);
        }

        // ---------------- Parked vehicles ----------------

        public enum Kind { Sedan, Hatch, Van, BoxTruck }

        /// <summary>
        /// A parked vehicle for lots and yards (Detail layer, culled with the street furniture): shaped body, glasshouse,
        /// wheels, unlit lamps. Background fill only; the three catalog cars are the only driven vehicles.
        /// </summary>
        private void Vehicle(Vector3 p, Quaternion rot, Kind kind)
        {
            // Cars and vans are below the shadow budget's interest (soft moon shadows, Detail layer); trucks still cast.
            bool shadows = kind == Kind.BoxTruck;
            MeshBuilder Mat(Material m) => shadows ? detail[m] : detail.Unshadowed(m);
            var paint = Mat(Pick(kit.CarPaints));
            var glass = Mat(kit.Glass);
            var dark = Mat(kit.DarkPlastic);
            Vector3 P(float x, float y, float z) => p + rot * new Vector3(x, y, z);
            float length, width, wheelbase;
            switch (kind)
            {
                case Kind.Van:
                    length = 5.1f; width = 2f; wheelbase = 3.2f;
                    paint.OrientedBox(P(0f, 1.3f, -0.5f), new Vector3(width, 1.9f, 4.1f), rot, 1f);          // load box, z −2.55..1.55
                    paint.OrientedBox(P(0f, 0.75f, 2.05f), new Vector3(width, 0.8f, 1f), rot, 1f);            // bonnet, z 1.55..2.55
                    Prism(glass, paint, P(0f, 0f, 0f), rot, width - 0.05f, 1.15f, 2.2f, 2.45f, 1.56f, 1.62f, 1.56f);
                    break;
                case Kind.BoxTruck:
                    length = 7.6f; width = 2.4f; wheelbase = 4.4f;
                    Mat(kit.CarPaints[5]).OrientedBox(P(0f, 2.35f, -0.9f), new Vector3(width, 2.9f, 5.6f), rot, 1f);
                    paint.OrientedBox(P(0f, 1.35f, 2.75f), new Vector3(width - 0.1f, 1.9f, 2f), rot, 1f);
                    glass.OrientedBox(P(0f, 1.85f, 3.76f), new Vector3(width - 0.4f, 0.8f, 0.03f), rot, 1f);
                    dark.OrientedBox(P(0f, 0.55f, -0.2f), new Vector3(width - 0.3f, 0.4f, length - 0.6f), rot, 1f);
                    break;
                case Kind.Hatch:
                    length = 4.1f; width = 1.8f; wheelbase = 2.55f;
                    paint.OrientedBox(P(0f, 0.62f, 0f), new Vector3(width, 0.6f, length), rot, 1f);
                    Prism(glass, paint, P(0f, 0f, 0f), rot, width - 0.12f, 0.92f, 1.48f, 0.95f, -1.95f, -0.2f, -1.9f);
                    break;
                default:
                    length = 4.7f; width = 1.86f; wheelbase = 2.85f;
                    paint.OrientedBox(P(0f, 0.6f, 0f), new Vector3(width, 0.56f, length), rot, 1f);
                    Prism(glass, paint, P(0f, 0f, 0f), rot, width - 0.14f, 0.88f, 1.42f, 0.95f, -1.45f, 0.3f, -0.95f);
                    break;
            }
            // Wheels as one dark slab per axle (reads as tyres under the body at lot distances).
            float r = kind == Kind.BoxTruck ? 0.5f : 0.34f;
            foreach (float sz in new[] { -0.5f, 0.5f })
                dark.OrientedBox(P(0f, r, sz * wheelbase), new Vector3(width - 0.02f, r * 2f, r * 2f), rot, 1f);
            // Unlit tail-lamp bar and headlamp glass, one strip each.
            detail.Unshadowed(kit.TailLampOff).OrientedBox(P(0f, kind == Kind.BoxTruck ? 0.9f : 0.78f, -length * 0.5f - 0.01f), new Vector3(width - 0.2f, 0.12f, 0.03f), rot, 1f);
            glass.OrientedBox(P(0f, 0.72f, length * 0.5f + 0.005f), new Vector3(width - 0.3f, 0.14f, 0.03f), rot, 1f);
            Collider(P(0f, kind == Kind.BoxTruck ? 1.9f : 0.8f, 0f), new Vector3(width, kind == Kind.BoxTruck ? 3.8f : 1.6f, length), rot);
            Vehicles++;
        }

        /// <summary>
        /// Glasshouse: a tapered prism from y0 (base, front at z0f, rear at z0r) to y1 (roof, front at z1f, rear at z1r).
        /// Sides and screens go to <paramref name="glass"/>, the roof to <paramref name="roof"/>.
        /// </summary>
        private static void Prism(MeshBuilder glass, MeshBuilder roof, Vector3 origin, Quaternion rot, float width, float y0, float y1,
                                  float z0f, float z0r, float z1f, float z1r)
        {
            float hw = width * 0.5f, tw = hw * 0.82f;
            Vector3 P(float x, float y, float z) => origin + rot * new Vector3(x, y, z);
            Vector3 bfl = P(-hw, y0, z0f), bfr = P(hw, y0, z0f), brl = P(-hw, y0, z0r), brr = P(hw, y0, z0r);
            Vector3 tfl = P(-tw, y1, z1f), tfr = P(tw, y1, z1f), trl = P(-tw, y1, z1r), trr = P(tw, y1, z1r);
            Vector2 u0 = Vector2.zero, u1 = Vector2.right, u2 = Vector2.one, u3 = Vector2.up;
            glass.Quad(bfl, bfr, tfr, tfl, u0, u1, u2, u3);   // windscreen
            glass.Quad(brr, brl, trl, trr, u0, u1, u2, u3);   // rear screen
            glass.Quad(bfr, brr, trr, tfr, u0, u1, u2, u3);   // right side
            glass.Quad(brl, bfl, tfl, trl, u0, u1, u2, u3);   // left side
            roof.Quad(tfl, tfr, trr, trl, u0, u1, u2, u3);
        }
    }
}
