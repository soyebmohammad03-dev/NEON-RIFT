using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Night dressing for the kit towers so they stop reading as black masses from the street and the air:
    /// <list type="bullet">
    /// <item>swaps the kit materials for <c>NeonRift/TowerFacade</c> (procedural office windows, lobby band, uplight wash);</item>
    /// <item>crown light strips that follow each tower's actual roof edge (found with raycasts against the tower's own mesh);</item>
    /// <item>rooftop plant (AC units, tanks, lift overruns) with small status LEDs, only where the roof is flat;</item>
    /// <item>blinking aviation lights on the tallest point of towers over 110 m (corners too above 180 m).</item>
    /// </list>
    /// Restrained by design: most crowns are warm or cool white, colour accents are rare and only on tall towers, and
    /// some towers stay dark. Geometry is merged per 400 m cell and per material, so it culls and costs a handful of draws.
    /// Deterministic (fixed seed); regenerated with the district.
    /// </summary>
    public sealed class TowerDressing
    {
        private const float CellSize = 400f;
        private const float AviationHeight = 110f;
        private const float CornerAviationHeight = 180f;

        private readonly DistrictKit kit;
        private readonly System.Random rng = new(4242);
        private readonly Dictionary<Material, Material> facadeSwap = new();
        private readonly Material crownWarm, crownCool;
        private readonly Material[] crownAccents;
        /// <summary>Additive facade wash under each crown strip, keyed by the strip material.</summary>
        private readonly Dictionary<Material, Material> washes = new();
        private const float WashHeight = 14f;
        private readonly Dictionary<Vector2Int, BlockMeshes> cells = new();
        private readonly List<Vector3> aviationVertices = new();
        private readonly List<Vector2> aviationData = new();
        private readonly List<int> aviationTriangles = new();

        public int Dressed { get; private set; }
        public int CrownStrips { get; private set; }
        public int RooftopUnits { get; private set; }
        public int AviationLights { get; private set; }

        public TowerDressing(DistrictKit kit)
        {
            this.kit = kit;
            const string art = "Assets/_NeonRift/Art/Buildings/AsianNightCity/";
            foreach (string id in new[] { "B1", "B2" })
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>($"{art}M_AsianNight_{id}.mat");
                if (source == null) continue;
                var facade = DistrictKit.Material($"District_TowerFacade{id}", Shader.Find("NeonRift/TowerFacade"));
                facade.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                facade.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
                bool clip = source.IsKeywordEnabled("_ALPHATEST_ON");
                facade.SetFloat("_AlphaClip", clip ? 1f : 0f);
                if (clip) facade.EnableKeyword("_ALPHATEST_ON"); else facade.DisableKeyword("_ALPHATEST_ON");
                // Tuned look (kept here, not in the shader defaults, so a rebuild always restores it).
                facade.SetColor("_BaseColor", new Color(0.62f, 0.64f, 0.7f));
                facade.SetFloat("_KitEmission", 0.55f);
                facade.SetFloat("_LitFraction", 0.3f);
                facade.SetFloat("_WindowStrength", 1f);
                facade.SetColor("_CityBounce", new Color(0.55f, 0.45f, 0.38f));
                facade.SetFloat("_BounceHeight", 130f);
                facade.SetColor("_UplightColor", new Color(1.1f, 0.8f, 0.5f));
                facade.enableInstancing = true;
                EditorUtility.SetDirty(facade);
                facadeSwap[source] = facade;
            }
            crownWarm = DistrictKit.Lit("District_CrownWarm", Color.black, 0.5f, 0f, emission: new Color(2.3f, 1.8f, 1.25f));
            crownCool = DistrictKit.Lit("District_CrownCool", Color.black, 0.5f, 0f, emission: new Color(1.35f, 1.75f, 2.3f));
            // Accents reuse the street neon (cyan, magenta, amber) so the palette stays one system.
            crownAccents = new[] { kit.NeonStrips[0], kit.NeonStrips[1], kit.NeonStrips[3] };
            var gradient = kit.Textures.GlowGradient;
            washes[crownWarm] = DistrictKit.Glow("District_CrownWashWarm", new Color(0.3f, 0.22f, 0.14f), gradient, 0.6f);
            washes[crownCool] = DistrictKit.Glow("District_CrownWashCool", new Color(0.16f, 0.21f, 0.28f), gradient, 0.6f);
            washes[crownAccents[0]] = DistrictKit.Glow("District_CrownWashCyan", new Color(0.03f, 0.2f, 0.3f), gradient, 0.6f);
            washes[crownAccents[1]] = DistrictKit.Glow("District_CrownWashMagenta", new Color(0.26f, 0.04f, 0.2f), gradient, 0.6f);
            washes[crownAccents[2]] = DistrictKit.Glow("District_CrownWashAmber", new Color(0.3f, 0.16f, 0.03f), gradient, 0.6f);
        }

        /// <summary>Replaces kit tower materials with the facade shader on one placed tower.</summary>
        public void ApplyFacade(GameObject tower)
        {
            foreach (var r in tower.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && facadeSwap.TryGetValue(mats[i], out var swap)) { mats[i] = swap; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>Crown, rooftop and aviation dressing for one tower (call after it is placed).</summary>
        public void Dress(GameObject tower)
        {
            var renderers = tower.GetComponentsInChildren<MeshRenderer>(true)
                                 .Where(r => r.sharedMaterials.Any(m => m != null && facadeSwap.ContainsValue(m))).ToList();
            if (renderers.Count == 0) return;

            // Temporary colliders on the tower's own meshes: every probe below asks only this tower.
            var probes = new List<MeshCollider>();
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var c = r.gameObject.AddComponent<MeshCollider>();
                c.sharedMesh = filter.sharedMesh;
                probes.Add(c);
            }
            Physics.SyncTransforms();
            try
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                DressTower(bounds, probes);
                Dressed++;
            }
            finally
            {
                foreach (var c in probes) Object.DestroyImmediate(c);
            }
        }

        private static bool Cast(List<MeshCollider> probes, Ray ray, float distance, out RaycastHit best)
        {
            best = default;
            bool any = false;
            foreach (var c in probes)
                if (c.Raycast(ray, out var hit, distance) && (!any || hit.distance < best.distance)) { best = hit; any = true; }
            return any;
        }

        private BlockMeshes Cell(Vector3 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
            if (!cells.TryGetValue(key, out var meshes)) cells[key] = meshes = new BlockMeshes();
            return meshes;
        }

        private void DressTower(Bounds b, List<MeshCollider> probes)
        {
            float top = b.max.y;
            float seed = (float)rng.NextDouble();
            var meshes = Cell(b.center);

            // Roof heights on a grid: the flat main roof is the most common height among upward-facing hits.
            var roofHits = new List<RaycastHit>();
            const int grid = 7;
            for (int i = 0; i < grid; i++)
                for (int j = 0; j < grid; j++)
                {
                    float x = Mathf.Lerp(b.min.x + 1f, b.max.x - 1f, (i + 0.5f) / grid);
                    float z = Mathf.Lerp(b.min.z + 1f, b.max.z - 1f, (j + 0.5f) / grid);
                    if (Cast(probes, new Ray(new Vector3(x, top + 5f, z), Vector3.down), top + 10f, out var hit) && hit.normal.y > 0.9f)
                        roofHits.Add(hit);
                }
            if (roofHits.Count == 0) return;
            float roof = roofHits.GroupBy(h => Mathf.Round(h.point.y)).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            var highest = roofHits.OrderByDescending(h => h.point.y).First();

            // Crown: mostly warm or cool white; accents are rare and reserved for tall towers; some crowns stay dark.
            Material crown = null;
            double pick = rng.NextDouble();
            if (pick < 0.5) crown = crownWarm;
            else if (pick < 0.75) crown = crownCool;
            else if (pick < 0.86 && top > 120f) crown = crownAccents[rng.Next(crownAccents.Length)];
            if (crown != null) CrownStrips += Crown(meshes.Unshadowed(crown), meshes.Unshadowed(washes[crown]), b, probes, roof);

            Rooftop(meshes, roofHits.Where(h => Mathf.Abs(h.point.y - roof) < 0.6f).ToList(), b);

            if (top > AviationHeight)
            {
                Aviation(highest.point + Vector3.up * 0.6f, seed);
                if (top > CornerAviationHeight)
                {
                    var corners = roofHits.Where(h => Mathf.Abs(h.point.y - roof) < 0.6f)
                                          .OrderBy(h => h.point.x + h.point.z).ToList();
                    if (corners.Count >= 2)
                    {
                        Aviation(corners[0].point + Vector3.up * 0.5f, seed + 0.31f);
                        Aviation(corners[^1].point + Vector3.up * 0.5f, seed + 0.62f);
                    }
                }
            }
        }

        /// <summary>
        /// Walks each side of the bounds 2 m at a time, finds the facade just under the parapet with an inward ray, and
        /// lays a 0.35 m light band over consecutive hits that line up. Returns the number of strip segments.
        /// </summary>
        private int Crown(MeshBuilder strip, MeshBuilder wash, Bounds b, List<MeshCollider> probes, float roof)
        {
            int segments = 0;
            float y = roof - 0.55f;
            if (y < 8f) return 0;
            Vector3[] outward = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            foreach (var n in outward)
            {
                Vector3 along = Vector3.Cross(Vector3.up, n);   // Quad front side faces n when a→b runs along this
                float half = Mathf.Abs(Vector3.Dot(b.extents, along));
                float reach = Mathf.Abs(Vector3.Dot(b.extents, n));
                Vector3 centre = new(b.center.x, y, b.center.z);
                Vector3? last = null;
                for (float s = -half + 1f; s <= half - 1f; s += 2f)
                {
                    Vector3 origin = centre + along * s + n * (reach + 3f);
                    Vector3? point = null;
                    if (Cast(probes, new Ray(origin, -n), reach + 3f, out var hit) && Vector3.Dot(hit.normal, n) > 0.85f)
                        point = hit.point + n * 0.06f;
                    if (point.HasValue && last.HasValue && Mathf.Abs(Vector3.Dot(point.Value - last.Value, n)) < 0.4f)
                    {
                        Vector3 a = last.Value, c = point.Value;
                        strip.Quad(a, c, c + Vector3.up * 0.35f, a + Vector3.up * 0.35f, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                        // Floodlit top floors: the gradient is brightest at v = 0 (under the strip) and gone by v = 1.
                        Vector3 down = Vector3.down * WashHeight, off = n * 0.08f;
                        wash.Quad(a + off + down, c + off + down, c + off, a + off, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f));
                        segments++;
                    }
                    last = point;
                }
            }
            return segments;
        }

        /// <summary>Two to four plant items on flat roof samples away from the edge, each with a small status LED.</summary>
        private void Rooftop(BlockMeshes meshes, List<RaycastHit> flat, Bounds b)
        {
            var inner = flat.Where(h => h.point.x > b.min.x + 4f && h.point.x < b.max.x - 4f && h.point.z > b.min.z + 4f && h.point.z < b.max.z - 4f)
                            .OrderBy(_ => rng.Next()).ToList();
            int count = Mathf.Min(inner.Count, 2 + rng.Next(3));
            var used = new List<Vector3>();
            foreach (var h in inner)
            {
                if (used.Count >= count) break;
                if (used.Any(u => (u - h.point).sqrMagnitude < 36f)) continue;
                used.Add(h.point);
                Vector3 p = h.point;
                int kind = rng.Next(3);
                Vector3 ledAt;
                if (kind == 0)
                {
                    meshes[kit.Metal].Cuboid(p + new Vector3(-1.3f, 0f, -0.8f), p + new Vector3(1.3f, 1.5f, 0.8f), 1f);   // AC unit
                    meshes[kit.DarkPlastic].Cylinder(p + new Vector3(0f, 1.5f, 0f), 0.55f, 0.12f, 10, true);           // fan shroud
                    ledAt = p + new Vector3(1.32f, 1.2f, 0f);
                }
                else if (kind == 1)
                {
                    meshes[kit.ConcreteDark].Cylinder(p, 1.3f, 2.6f, 12, true);                                         // water tank
                    ledAt = p + new Vector3(1.32f, 2.2f, 0f);
                }
                else
                {
                    meshes[kit.ConcreteDark].Cuboid(p + new Vector3(-2.5f, 0f, -2f), p + new Vector3(2.5f, 3.4f, 2f), 2f);  // lift overrun
                    meshes.Unshadowed(kit.WallPack).Cuboid(p + new Vector3(-0.3f, 2.5f, 2f), p + new Vector3(0.3f, 2.8f, 2.12f), 1f); // door lamp
                    ledAt = p + new Vector3(2.52f, 3.0f, 1.2f);
                }
                var led = rng.NextDouble() < 0.7 ? kit.CameraLed : kit.AviationRed;
                meshes.Unshadowed(led).Cuboid(ledAt - Vector3.one * 0.08f, ledAt + Vector3.one * 0.08f, 1f);
                RooftopUnits++;
            }
        }

        /// <summary>A 0.9 m cube drawn with NeonRift/SkylineBackdrop's aviation mode (blinks, unfogged).</summary>
        private void Aviation(Vector3 p, float seed)
        {
            const float h = 0.45f;
            int start = aviationVertices.Count;
            Vector3[] c =
            {
                p + new Vector3(-h, -h, -h), p + new Vector3(h, -h, -h), p + new Vector3(h, h, -h), p + new Vector3(-h, h, -h),
                p + new Vector3(-h, -h, h), p + new Vector3(h, -h, h), p + new Vector3(h, h, h), p + new Vector3(-h, h, h)
            };
            aviationVertices.AddRange(c);
            for (int i = 0; i < 8; i++) aviationData.Add(new Vector2(seed, 1f));
            int[] faces = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
            foreach (int f in faces) aviationTriangles.Add(start + f);
            AviationLights++;
        }

        /// <summary>Emits every cell's merged meshes and the aviation light mesh under one parent.</summary>
        public GameObject Emit(Transform parent, int layer)
        {
            var root = new GameObject("TowerDressing");
            root.transform.SetParent(parent, false);
            foreach (var pair in cells.OrderBy(p => p.Key.x).ThenBy(p => p.Key.y))
            {
                var cell = new GameObject($"Cell_{pair.Key.x}_{pair.Key.y}").transform;
                cell.SetParent(root.transform, false);
                pair.Value.Emit(cell, $"District_TowerDressing_{pair.Key.x}_{pair.Key.y}", layer);
            }
            if (aviationVertices.Count > 0)
            {
                var mesh = new Mesh { name = "District_TowerAviation", indexFormat = aviationVertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(aviationVertices);
                mesh.SetUVs(0, Enumerable.Repeat(Vector2.zero, aviationVertices.Count).ToList());
                mesh.SetUVs(1, aviationData);
                mesh.SetColors(Enumerable.Repeat(new Color(0f, 0f, 0f, 0f), aviationVertices.Count).ToList());
                mesh.SetTriangles(aviationTriangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                DistrictKit.Renderer("AviationLights", root.transform, DistrictKit.SaveMesh(mesh, "District_TowerAviation"), kit.SkylineBackdrop, layer, shadows: false);
            }
            Debug.Log($"[District] Tower dressing: {Dressed} towers, {CrownStrips} crown segments, {RooftopUnits} rooftop units, {AviationLights} aviation lights, {cells.Count} cells.");
            return root;
        }
    }
}
