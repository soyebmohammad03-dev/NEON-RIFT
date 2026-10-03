using System.Collections.Generic;
using System.Linq;
using NeonRift.World;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Street furniture and infrastructure the city is made believable with: sidewalk furniture per zone (bins,
    /// benches, hydrants, utility cabinets, vending machines, planters, dumpsters, pallets, bus shelters), facade
    /// clutter (AC units, pipes, fire escapes), rooftop plant (tanks, HVAC, antennas with aviation lights), lamp haze,
    /// and intersections (traffic signals on mast arms, street-name blades, corner bollards). Geometry goes into the
    /// owning block's combined meshes; solid props get simple box colliders.
    /// </summary>
    public sealed class CityProps
    {
        private readonly DistrictKit kit;
        private readonly System.Random rng;
        private readonly int environment;
        private readonly List<Light> lamps;

        /// <summary>Signal lens renderers, index as <see cref="DistrictKit.SignalLenses"/>.</summary>
        public Renderer[] SignalRenderers { get; private set; } = new Renderer[0];
        public int Signals { get; private set; }

        public CityProps(DistrictKit kit, System.Random rng, int environment, List<Light> lamps)
        {
            this.kit = kit;
            this.rng = rng;
            this.environment = environment;
            this.lamps = lamps;
        }

        private float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        private bool Chance(float p) => rng.NextDouble() < p;

        private void Box(Transform colliders, Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var go = new GameObject("Prop") { layer = environment };
            go.transform.SetParent(colliders, false);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.AddComponent<BoxCollider>().size = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        private static bool NearNode(Vector3 p, float radius)
        {
            foreach (var r in CityLayout.Roads)
                if (r.Ground && ((new Vector2(r.A.x, r.A.z) - new Vector2(p.x, p.z)).magnitude < radius || (new Vector2(r.B.x, r.B.z) - new Vector2(p.x, p.z)).magnitude < radius))
                    return true;
            foreach (var v in CityLayout.Roads.Where(r => r.Ground && r.IsVertical))
                if (Mathf.Abs(p.x - v.A.x) < v.HalfWidth + radius)
                    foreach (var h in CityLayout.Roads.Where(r => r.Ground && !r.IsVertical))
                        if (Mathf.Abs(p.z - h.A.z) < h.HalfWidth + radius && p.x > Mathf.Min(h.A.x, h.B.x) - 1f && p.x < Mathf.Max(h.A.x, h.B.x) + 1f &&
                            p.z > Mathf.Min(v.A.z, v.B.z) - 1f && p.z < Mathf.Max(v.A.z, v.B.z) + 1f)
                            return true;
            return false;
        }

        /// <summary>True inside a ground carriageway or the tunnel.</summary>
        public static bool OnCarriageway(Vector2 p)
        {
            foreach (var r in CityLayout.Roads)
            {
                if (!r.Ground) continue;
                if (CityLayout.DistanceToSegment(p, new Vector2(r.A.x, r.A.z), new Vector2(r.B.x, r.B.z)) < r.HalfWidth) return true;
            }
            return CityLayout.Reserved.Any(x => x.Contains(p));
        }

        private static bool Free(Vector3 p) =>
            !CityLayout.Reserved.Any(r => r.Contains(new Vector2(p.x, p.z))) && !CityLayout.InLot(new Vector2(p.x, p.z), out _);

        // ---------------- Sidewalks ----------------

        /// <summary>Furniture along one side of a block. Kerb zone (0.9 m from the kerb) and wall zone (against the podiums).</summary>
        public void Sidewalk(Zone zone, CityLayout.Side side, BlockMeshes m, Transform colliders)
        {
            var road = CityLayout.Roads[side.Road];
            if (road.Class == RoadClass.Tunnel || side.Path < 1f) return;
            Vector3 along = (side.End - side.Start).normalized;
            float length = Vector3.Distance(side.Start, side.End);
            var rot = Quaternion.LookRotation(side.Normal);
            bool alley = road.Class == RoadClass.Alley || side.Path < 2f;
            Vector3 Kerbside(float s) => side.Start + along * s - side.Normal * 0.9f;
            Vector3 Wallside(float s) => side.Start + along * s - side.Normal * (side.Path - 0.6f);

            void Every(float spacing, float phase, System.Action<Vector3, float> place, bool wall = false)
            {
                for (float s = phase; s < length - 6f; s += spacing)
                {
                    var p = wall ? Wallside(s) : Kerbside(s);
                    if (s < 6f || NearNode(p, 9f) || !Free(p)) continue;
                    place(p, s);
                }
            }

            if (alley)
            {
                Every(26f, 7f, (p, _) => Dumpster(m, colliders, Wallside(Vector3.Dot(p - side.Start, along)), rot));
                Every(19f, 13f, (p, _) => Crates(m, colliders, p - side.Normal * 0.2f, rot));
                AlleyWall(side, road, along, length, rot, m);
                return;
            }
            KerbSigns(zone, road, side, along, length, m, colliders, Kerbside);
            switch (zone)
            {
                case Zone.Spire:
                    Every(24f, 12f, (p, _) => Planter(m, colliders, p, rot, tree: true));
                    Every(48f, 30f, (p, _) => Bench(m, colliders, p - side.Normal * 0.2f, rot));
                    Every(50f, 5f, (p, _) => Bin(m, colliders, p, rot));
                    if (road.Class == RoadClass.Arterial) Every(240f, 120f, (p, _) => Shelter(m, colliders, p, rot));
                    break;
                case Zone.Kowloon:
                    Every(28f, 9f, (p, _) => Vending(m, colliders, p, rot), wall: true);
                    Every(31f, 21f, (p, _) => Bin(m, colliders, p, rot));
                    Every(23f, 4f, (p, _) => Crates(m, colliders, p, rot), wall: true);
                    Every(64f, 40f, (p, _) => FoodCart(m, colliders, p, rot));
                    break;
                case Zone.Harbor:
                    Every(52f, 20f, (p, s) => Dumpster(m, colliders, Wallside(s), rot));
                    Every(33f, 9f, (p, _) => Pallets(m, colliders, p, rot), wall: true);
                    Every(42f, 31f, (p, _) => Barrels(m, colliders, p, rot));
                    Every(90f, 60f, (p, _) => Utility(m, colliders, p, rot));
                    break;
                case Zone.Lowtown:
                    Every(36f, 11f, (p, _) => Bin(m, colliders, p, rot));
                    Every(70f, 45f, (p, s) => Dumpster(m, colliders, Wallside(s), rot));
                    Every(82f, 30f, (p, _) => Hydrant(m, colliders, p));
                    Every(88f, 66f, (p, _) => Bench(m, colliders, p - side.Normal * 0.2f, rot));
                    Every(73f, 18f, (p, _) => Utility(m, colliders, p, rot));
                    if (road.Class == RoadClass.Arterial) Every(260f, 150f, (p, _) => Shelter(m, colliders, p, rot));
                    break;
                case Zone.Outer:
                    break;
                default:
                    Every(35f, 10f, (p, _) => Bin(m, colliders, p, rot));
                    Every(72f, 46f, (p, _) => Bench(m, colliders, p - side.Normal * 0.2f, rot));
                    Every(91f, 27f, (p, _) => Hydrant(m, colliders, p));
                    Every(83f, 63f, (p, _) => Utility(m, colliders, p, rot));
                    Every(120f, 85f, (p, _) => Vending(m, colliders, p, rot), wall: true);
                    if (road.Class == RoadClass.Arterial) Every(260f, 130f, (p, _) => Shelter(m, colliders, p, rot));
                    break;
            }
        }

        public int AlleyFixtures { get; private set; }

        /// <summary>
        /// Back-of-house on an alley wall: drain and service pipes, extract vents, caged security lights with their pool,
        /// fire-exit doors under a green sign, and (from one side only) power and data cables slung across the alley.
        /// </summary>
        private void AlleyWall(CityLayout.Side side, CityLayout.Road road, Vector3 along, float length, Quaternion rot, BlockMeshes m)
        {
            Vector3 Wall(float s) => side.Start + along * s - side.Normal * (side.Path - 0.02f);
            bool Clear(Vector3 p) => !NearNode(p, 8f) && Free(p);
            for (float s = 4f; s < length - 4f; s += Range(7f, 11f))
            {
                var p = Wall(s);
                if (!Clear(p)) continue;
                m[kit.Metal].Cylinder(p + side.Normal * 0.14f, Range(0.06f, 0.11f), Range(6f, 10f), 6, true);
                AlleyFixtures++;
            }
            for (float s = 9f; s < length - 6f; s += Range(13f, 18f))
            {
                var p = Wall(s);
                if (!Clear(p)) continue;
                // Caged wall light and its pool on the alley floor.
                m.Unshadowed(kit.WallPack).OrientedBox(p + Vector3.up * 4.2f + side.Normal * 0.14f, new Vector3(0.45f, 0.25f, 0.2f), rot, 1f);
                m[kit.Metal].OrientedBox(p + Vector3.up * 4.2f + side.Normal * 0.2f, new Vector3(0.52f, 0.32f, 0.04f), rot, 1f);
                m.Unshadowed(kit.LightPools[1]).Decal(new Vector3(p.x, 0.04f, p.z) + side.Normal * 2.4f, 7f, 7f);
                AlleyFixtures++;
            }
            for (float s = 15f; s < length - 8f; s += Range(22f, 30f))
            {
                var p = Wall(s);
                if (!Clear(p)) continue;
                m[kit.DarkPlastic].OrientedBox(p + Vector3.up * 1.1f + side.Normal * 0.04f, new Vector3(1.1f, 2.2f, 0.08f), rot, 1f);
                m.Unshadowed(kit.CameraLed).OrientedBox(p + Vector3.up * 2.5f + side.Normal * 0.06f, new Vector3(0.42f, 0.16f, 0.04f), rot, 1f);
                m[kit.Concrete].OrientedBox(p + Vector3.up * 0.08f + side.Normal * 0.45f, new Vector3(1.6f, 0.16f, 0.9f), rot, 1f);
                AlleyFixtures++;
            }
            for (float s = 6f; s < length - 5f; s += Range(12f, 20f))
            {
                var p = Wall(s);
                if (!Clear(p)) continue;
                float y = Range(2.8f, 5.5f);
                m[kit.Metal].OrientedBox(p + Vector3.up * y + side.Normal * 0.3f, new Vector3(0.9f, 0.7f, 0.6f), rot, 1f);
                m[kit.DarkPlastic].OrientedBox(p + Vector3.up * y + side.Normal * 0.61f, new Vector3(0.7f, 0.5f, 0.02f), rot, 1f);
                AlleyFixtures++;
            }
            if (Vector3.Dot(side.Normal, Vector3.one) <= 0f) return;    // cables from one side of the alley only
            float span = road.HalfWidth * 2f + side.Path * 2f - 0.1f;
            for (float s = 10f; s < length - 6f; s += Range(9f, 15f))
            {
                var a = Wall(s) + Vector3.up * Range(6f, 8f);
                if (!Clear(a)) continue;
                var b = a + side.Normal * span + along * Range(-3f, 3f) + Vector3.up * Range(-0.8f, 0.8f);
                const int segments = 6;
                Vector3 prev = a;
                for (int i = 1; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    Vector3 q = Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * span * 0.06f);
                    m[kit.Metal].OrientedBox((prev + q) * 0.5f, new Vector3(0.035f, 0.035f, Vector3.Distance(prev, q)), Quaternion.LookRotation(q - prev), 1f);
                    prev = q;
                }
                AlleyFixtures++;
            }
        }

        private void Bin(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            m[kit.DarkPlastic].Cylinder(p, 0.32f, 0.95f, 10, true);
            m[kit.Metal].Cylinder(p + Vector3.up * 0.95f, 0.34f, 0.06f, 10, true);
            Box(col, p + Vector3.up * 0.5f, new Vector3(0.66f, 1f, 0.66f), rot);
        }

        private void Bench(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            var side = rot * Quaternion.Euler(0f, 90f, 0f);
            m[kit.Metal].OrientedBox(p + Vector3.up * 0.45f, new Vector3(2f, 0.08f, 0.55f), side, 1f);
            m[kit.Metal].OrientedBox(p + Vector3.up * 0.75f - rot * Vector3.forward * 0.25f, new Vector3(2f, 0.5f, 0.06f), side, 1f);
            foreach (float s in new[] { -0.85f, 0.85f })
                m[kit.Metal].OrientedBox(p + side * Vector3.right * s + Vector3.up * 0.22f, new Vector3(0.08f, 0.44f, 0.5f), side, 1f);
            Box(col, p + Vector3.up * 0.5f, new Vector3(0.6f, 1f, 2f), rot);
        }

        private void Hydrant(BlockMeshes m, Transform col, Vector3 p)
        {
            m[kit.Reflector].Cylinder(p, 0.15f, 0.7f, 8, true);
            m[kit.Reflector].OrientedBox(p + Vector3.up * 0.5f, new Vector3(0.5f, 0.12f, 0.12f), Quaternion.identity, 1f);
            Box(col, p + Vector3.up * 0.35f, new Vector3(0.4f, 0.7f, 0.4f), Quaternion.identity);
        }

        private void Utility(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            m[kit.ConcreteDark].OrientedBox(p + Vector3.up * 0.7f, new Vector3(1.1f, 1.4f, 0.55f), rot, 1f);
            m.Unshadowed(kit.CameraLed).OrientedBox(p + Vector3.up * 1.2f + rot * Vector3.forward * 0.28f, new Vector3(0.08f, 0.04f, 0.02f), rot, 1f);
            Box(col, p + Vector3.up * 0.7f, new Vector3(1.1f, 1.4f, 0.55f), rot);
        }

        private void Vending(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            m[kit.DarkPlastic].OrientedBox(p + Vector3.up * 0.95f, new Vector3(1f, 1.9f, 0.8f), rot, 1f);
            var front = m.Unshadowed(kit.NeonStrips[rng.Next(kit.NeonStrips.Length)]);
            front.OrientedBox(p + Vector3.up * 1.15f + rot * Vector3.forward * 0.41f, new Vector3(0.75f, 1.1f, 0.02f), rot, 1f);
            Box(col, p + Vector3.up * 0.95f, new Vector3(1f, 1.9f, 0.8f), rot);
        }

        private void Planter(BlockMeshes m, Transform col, Vector3 p, Quaternion rot, bool tree)
        {
            m[kit.ConcreteDark].OrientedBox(p + Vector3.up * 0.3f, new Vector3(1.6f, 0.6f, 1.6f), rot, 1f);
            m.Unshadowed(kit.NeonStrips[0]).OrientedBox(p + Vector3.up * 0.62f, new Vector3(1.62f, 0.03f, 1.62f), rot, 1f);
            if (tree)
            {
                // Trunk with two limbs, then an irregular canopy of overlapping clumps (no two trees alike).
                float trunk = Range(2.4f, 3f);
                m[kit.Metal].Cylinder(p + Vector3.up * 0.6f, 0.11f, trunk, 6, false);
                Vector3 fork = p + Vector3.up * (0.6f + trunk);
                for (int limb = 0; limb < 2; limb++)
                    m[kit.Metal].OrientedBox(fork + Vector3.up * 0.4f, new Vector3(0.08f, 1.1f, 0.08f),
                                             Quaternion.Euler(Range(-30f, 30f), Range(0f, 360f), Range(18f, 32f)), 1f);
                int clumps = rng.Next(5, 8);
                float spread = Range(1.1f, 1.5f);
                for (int i = 0; i < clumps; i++)
                {
                    float a = i / (float)clumps * 360f + Range(-20f, 20f);
                    float r = i == 0 ? 0f : Range(0.45f, 1f) * spread;
                    Vector3 c = fork + Vector3.up * Range(0.9f, 1.9f) + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                    float size = Range(1.1f, 1.8f) * (i == 0 ? 1.25f : 1f);
                    m[kit.Canopy].OrientedBox(c, new Vector3(size, size * Range(0.6f, 0.85f), size * Range(0.8f, 1.1f)),
                                              Quaternion.Euler(Range(-15f, 15f), Range(0f, 360f), Range(-15f, 15f)), 1f);
                }
            }
            Box(col, p + Vector3.up * 0.3f, new Vector3(1.6f, 0.6f, 1.6f), rot);
        }

        private void Dumpster(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            var side = rot * Quaternion.Euler(0f, 90f, 0f);
            var mat = kit.Containers[rng.Next(kit.Containers.Length)];
            m[mat].OrientedBox(p + Vector3.up * 0.65f, new Vector3(1.9f, 1.1f, 1.1f), side, 1f);
            m[kit.DarkPlastic].OrientedBox(p + Vector3.up * 1.25f, new Vector3(1.95f, 0.08f, 1.15f), side * Quaternion.Euler(Range(-8f, 0f), 0f, 0f), 1f);
            Box(col, p + Vector3.up * 0.65f, new Vector3(1.1f, 1.3f, 1.9f), rot);
        }

        private void Crates(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            int n = rng.Next(1, 4);
            for (int i = 0; i < n; i++)
                m[kit.RollerDoor].OrientedBox(p + Vector3.up * (0.3f + 0.6f * (i / 2)) + rot * new Vector3((i % 2) * 0.62f - 0.3f, 0f, 0f), new Vector3(0.6f, 0.6f, 0.6f),
                                              rot * Quaternion.Euler(0f, Range(-12f, 12f), 0f), 1f);
            Box(col, p + Vector3.up * 0.5f, new Vector3(1.3f, 1f, 0.7f), rot);
        }

        private void Pallets(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            int n = rng.Next(2, 6);
            for (int i = 0; i < n; i++)
                m[kit.Metal].OrientedBox(p + Vector3.up * (0.07f + i * 0.15f), new Vector3(1.2f, 0.13f, 1f), rot * Quaternion.Euler(0f, Range(-5f, 5f), 0f), 1f);
            Box(col, p + Vector3.up * (n * 0.075f), new Vector3(1.2f, n * 0.15f, 1f), rot);
        }

        private void Barrels(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            var mat = kit.Containers[rng.Next(kit.Containers.Length)];
            m[mat].Cylinder(p, 0.3f, 0.9f, 10, true);
            m[mat].Cylinder(p + rot * Vector3.right * 0.65f, 0.3f, 0.9f, 10, true);
            Box(col, p + rot * Vector3.right * 0.32f + Vector3.up * 0.45f, new Vector3(1.3f, 0.9f, 0.62f), rot);
        }

        private void FoodCart(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            var side = rot * Quaternion.Euler(0f, 90f, 0f);
            m[kit.Metal].OrientedBox(p + Vector3.up * 0.55f, new Vector3(2f, 0.9f, 1f), side, 1f);
            m.Unshadowed(kit.LanternWarm).OrientedBox(p + Vector3.up * 1.05f, new Vector3(2f, 0.06f, 1f), side, 1f);
            m[kit.Containers[0]].OrientedBox(p + Vector3.up * 2.1f, new Vector3(2.4f, 0.08f, 1.5f), side, 1f);
            foreach (float s in new[] { -1f, 1f })
                m[kit.Metal].OrientedBox(p + side * Vector3.right * s * 1f + Vector3.up * 1.55f, new Vector3(0.05f, 1.1f, 0.05f), side, 1f);
            for (int i = 0; i < 4; i++)
                m.Unshadowed(kit.Lantern).Cylinder(p + side * Vector3.right * (-0.9f + i * 0.6f) + Vector3.up * 1.75f, 0.13f, 0.28f, 8, true);
            Box(col, p + Vector3.up * 0.55f, new Vector3(1f, 1.1f, 2f), rot);
            Box(col, p + Vector3.up * 2.1f, new Vector3(1.5f, 0.2f, 2.4f), rot);   // canopy (stops the chase camera)
        }

        /// <summary>Bus shelter: glass box, roof, lit advert panel (a screen that turns into a warning in a lockdown).</summary>
        private void Shelter(BlockMeshes m, Transform col, Vector3 p, Quaternion rot)
        {
            var side = rot * Quaternion.Euler(0f, 90f, 0f);
            Vector3 back = -(rot * Vector3.forward);
            m[kit.Metal].OrientedBox(p + Vector3.up * 2.55f + back * 0.2f, new Vector3(4.2f, 0.12f, 1.8f), side, 1f);
            foreach (float s in new[] { -2f, 2f })
                m[kit.Metal].OrientedBox(p + side * Vector3.right * s + Vector3.up * 1.25f + back * 0.9f, new Vector3(0.08f, 2.5f, 0.08f), side, 1f);
            m.Unshadowed(kit.Glass).OrientedBox(p + Vector3.up * 1.35f + back * 0.95f, new Vector3(4f, 2.2f, 0.04f), side, 1f);
            m.Unshadowed(kit.Billboards[rng.Next(kit.Billboards.Length)]).Panel(p + side * Vector3.right * 2.05f + Vector3.up * 1.3f + back * 0.2f,
                                                                               side, 1.4f, 2f, new Rect(0f, 0f, 1f, 1f));
            m[kit.Metal].OrientedBox(p + Vector3.up * 0.45f + back * 0.7f, new Vector3(2.6f, 0.06f, 0.4f), side, 1f);
            Box(col, p + Vector3.up * 1.3f + back * 0.9f, new Vector3(4.2f, 2.6f, 0.2f), side);
        }

        /// <summary>
        /// Regulatory signs on kerb poles, facing the traffic in the lane beside the kerb (right-hand traffic): speed
        /// limits on arterials and streets, no-parking along most blocks, loading in the harbor and a bus sign near shelters.
        /// </summary>
        private void KerbSigns(Zone zone, CityLayout.Road road, CityLayout.Side side, Vector3 along, float length, BlockMeshes m, Transform colliders,
                               System.Func<float, Vector3> kerbside)
        {
            if (zone == Zone.Outer) return;
            Vector3 travel = Vector3.Cross(Vector3.up, side.Normal);   // lane beside this kerb
            var facing = Quaternion.LookRotation(-travel);
            string limit = road.Class == RoadClass.Arterial ? "60" : "30";
            float s0 = 14f + Range(0f, 10f);
            int n = 0;
            for (float s = s0; s < length - 10f; s += Range(58f, 84f), n++)
            {
                var p = kerbside(s) + side.Normal * 0.45f;
                if (NearNode(p, 14f) || !Free(p)) continue;
                string text = n % 3 == 0 ? limit : zone == Zone.Harbor ? "LOADING" : "NO PARKING";
                RegulatorySign(m, colliders, p, facing, text);
            }
        }

        private void RegulatorySign(BlockMeshes m, Transform colliders, Vector3 p, Quaternion facing, string text)
        {
            var uv = DistrictTextures.SignRect(text, out float aspect);
            float height = 0.42f, width = Mathf.Clamp(height * aspect, 0.42f, 1.5f);
            height = Mathf.Min(height, width / aspect * 1.15f);
            m[kit.Metal].Cylinder(p, 0.045f, 2.75f, 6, true);
            Vector3 plate = p + Vector3.up * 2.45f;
            m[kit.DarkPlastic].OrientedBox(plate, new Vector3(width + 0.12f, height + 0.12f, 0.03f), facing, 1f);
            m.Unshadowed(kit.StreetSign).Panel(plate + facing * Vector3.forward * 0.02f, facing, width, height, uv);
            Box(colliders, p + Vector3.up * 1.3f, new Vector3(0.12f, 2.6f, 0.12f), Quaternion.identity);
            KerbSignCount++;
        }

        /// <summary>A box CCTV camera on a short bracket, tilted down towards <paramref name="look"/>, with a red record LED.</summary>
        public void Cctv(BlockMeshes m, Vector3 mount, Vector3 look)
        {
            Vector3 dir = look - mount;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            var aim = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(22f, 0f, 0f);
            m[kit.Metal].OrientedBox(mount + aim * Vector3.forward * 0.2f, new Vector3(0.06f, 0.06f, 0.4f), Quaternion.LookRotation(dir.normalized), 1f);
            Vector3 body = mount + aim * Vector3.forward * 0.55f + Vector3.down * 0.08f;
            m[kit.DarkPlastic].OrientedBox(body, new Vector3(0.22f, 0.2f, 0.48f), aim, 1f);
            m[kit.DarkPlastic].OrientedBox(body + aim * new Vector3(0f, 0.13f, 0.05f), new Vector3(0.3f, 0.03f, 0.58f), aim, 1f);   // sun hood
            m.Unshadowed(kit.AviationRed).OrientedBox(body + aim * new Vector3(0.07f, 0.06f, 0.245f), new Vector3(0.03f, 0.03f, 0.01f), aim, 1f);
            CctvCount++;
        }

        public int KerbSignCount { get; private set; }
        public int CctvCount { get; private set; }

        // ---------------- Facades and rooftops ----------------

        /// <summary>Clutter on an upper facade: AC units and drain pipes; fire escapes in Lowtown; LED fins in Spire.</summary>
        public void Facade(Zone zone, NightRunDistrict.ZoneStyle style, Vector3 upperFront, Vector3 along, Vector3 normal, Quaternion rot,
                           float width, float height, float depth, BlockMeshes m)
        {
            if (style.FacadeClutter)
            {
                int units = Mathf.RoundToInt(width * height / 60f);
                for (int i = 0; i < units; i++)
                {
                    float floor = Mathf.Floor(Range(0f, height / 3.6f - 0.5f)) * 3.6f + 0.4f;
                    Vector3 p = upperFront + along * Range(-width * 0.45f, width * 0.45f) + Vector3.up * floor + normal * 0.32f;
                    m[kit.Metal].OrientedBox(p, new Vector3(0.9f, 0.6f, 0.6f), rot, 1f);
                    m[kit.DarkPlastic].OrientedBox(p + normal * 0.31f, new Vector3(0.6f, 0.45f, 0.02f), rot, 1f);
                }
                m[kit.Metal].Cylinder(upperFront + along * (width * 0.5f - 0.3f) + normal * 0.12f + Vector3.down * 4.5f, 0.07f, height + 4.5f, 6, false);
                if (zone == Zone.Lowtown && height > 8f && Chance(0.35f))
                {
                    Vector3 c = upperFront + along * Range(-width * 0.25f, width * 0.25f) + normal * 0.6f;
                    for (float y = 3f; y < height - 1f; y += 3.6f)
                    {
                        m[kit.Metal].OrientedBox(c + Vector3.up * y, new Vector3(3.2f, 0.06f, 1.1f), rot, 1f);
                        m[kit.Metal].OrientedBox(c + Vector3.up * (y + 0.5f) + normal * 0.55f, new Vector3(3.2f, 0.04f, 0.04f), rot, 1f);
                        m[kit.Metal].OrientedBox(c + Vector3.up * (y - 1.8f) + along * 1.1f, new Vector3(0.5f, 3.9f, 0.06f), rot * Quaternion.Euler(0f, 0f, 22f), 1f);
                    }
                }
            }
            if (zone == Zone.Spire && height > 14f && Chance(0.4f))
            {
                var fin = m.Unshadowed(kit.NeonStrips[Chance(0.5f) ? 0 : 2]);
                for (float d = -width * 0.4f; d <= width * 0.4f; d += Mathf.Max(3.2f, width / 6f))
                    fin.OrientedBox(upperFront + along * d + normal * 0.08f + Vector3.up * (height * 0.5f), new Vector3(0.06f, height - 1.5f, 0.06f), rot, 1f);
            }
            if (zone == Zone.Harbor && Chance(0.5f))
            {
                Vector3 p = upperFront + along * Range(-width * 0.4f, width * 0.4f) + normal * 0.3f;
                m[kit.Metal].Cylinder(p + Vector3.down * 4.5f, 0.22f, height + 4.5f + 2f, 8, true);
            }
        }

        public void Rooftop(Zone zone, NightRunDistrict.ZoneStyle style, Vector3 roofFront, Vector3 along, Vector3 normal, Quaternion rot,
                            float width, float depth, bool tall, BlockMeshes m)
        {
            for (int k = rng.Next(1, 4); k > 0; k--)
            {
                Vector3 p = roofFront - normal * Range(3f, Mathf.Max(3.5f, depth - 3f)) + along * Range(-width * 0.35f, width * 0.35f);
                m[kit.Metal].OrientedBox(p + Vector3.up * 0.7f, new Vector3(Range(1.5f, 3.5f), 1.4f, Range(1.5f, 3f)), rot, 1f);
                if (Chance(0.5f)) m[kit.DarkPlastic].Cylinder(p + Vector3.up * 1.4f, 0.5f, 0.15f, 10, true);  // fan
            }
            if (style.RoofTanks && Chance(0.45f))
            {
                Vector3 p = roofFront - normal * Mathf.Min(depth - 2.5f, Range(4f, 8f)) + along * Range(-width * 0.3f, width * 0.3f);
                foreach (var leg in new[] { new Vector3(-0.9f, 0, -0.9f), new Vector3(0.9f, 0, -0.9f), new Vector3(-0.9f, 0, 0.9f), new Vector3(0.9f, 0, 0.9f) })
                    m[kit.Metal].OrientedBox(p + leg + Vector3.up * 1f, new Vector3(0.12f, 2f, 0.12f), Quaternion.identity, 1f);
                m[kit.RollerDoor].Cylinder(p + Vector3.up * 2f, 1.3f, 2.4f, 12, true);
            }
            if (tall || Chance(0.12f))
            {
                Vector3 p = roofFront - normal * Mathf.Min(depth * 0.5f, 6f) + along * Range(-width * 0.3f, width * 0.3f);
                float h = Range(5f, 12f);
                m[kit.Metal].Cylinder(p, 0.08f, h, 6, false);
                m.Unshadowed(kit.AviationRed).OrientedBox(p + Vector3.up * h, new Vector3(0.22f, 0.22f, 0.22f), Quaternion.identity, 1f);
            }
        }

        /// <summary>Faint ground pool and haze cone under a lamp head (all lamps, lit by a real light or not).</summary>
        public static void LampHaze(DistrictKit kit, BlockMeshes m, DistrictKit.LampKind kind, Vector3 head, float height)
        {
            m.Unshadowed(kit.LightPools[(int)kind]).Decal(new Vector3(head.x, 0.03f, head.z), 13f, 13f);
            m.Unshadowed(kit.LightCones[(int)kind]).Cone(head, 3.6f, height - 0.3f, 10, 0.25f);
        }

        // ---------------- Intersections ----------------

        /// <summary>
        /// Traffic signals (far-side right corner of each approach, mast arm over its lanes), street-name blades and
        /// corner bollards at every junction of two ground streets. Signal lenses share six materials so one
        /// component can run the whole city's cycle (and flash red in a lockdown).
        /// </summary>
        public void BuildIntersections(Transform root, List<Vector3> nodes)
        {
            var parent = new GameObject("Intersections").transform;
            parent.SetParent(root, false);
            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(parent, false);
            var m = new BlockMeshes();
            var lenses = kit.SignalLenses.Select(l => m.Unshadowed(l)).ToArray();
            var cells = new Dictionary<Vector2Int, BlockMeshes>();
            foreach (var node in nodes)
            {
                if (node.y > 0.5f) continue;
                // Structure goes into 400 m cells so it culls (and only nearby cells draw into shadow maps); the lenses
                // stay city-wide because TrafficSignalNetwork drives all signals through six shared materials.
                var key = new Vector2Int(Mathf.FloorToInt(node.x / 400f), Mathf.FloorToInt(node.z / 400f));
                if (!cells.TryGetValue(key, out var cm)) cells[key] = cm = new BlockMeshes();
                var p2 = new Vector2(node.x, node.z);
                CityLayout.Road? v = null, h = null;
                foreach (var r in CityLayout.Roads)
                {
                    if (!r.Ground || CityLayout.DistanceToSegment(p2, new Vector2(r.A.x, r.A.z), new Vector2(r.B.x, r.B.z)) > 0.5f) continue;
                    if (r.IsVertical) v = r; else h = r;
                }
                if (v == null || h == null) continue;
                var vr = v.Value;
                var hr = h.Value;
                bool n = Mathf.Max(vr.A.z, vr.B.z) > node.z + 1f, s = Mathf.Min(vr.A.z, vr.B.z) < node.z - 1f;
                bool e = Mathf.Max(hr.A.x, hr.B.x) > node.x + 1f, w = Mathf.Min(hr.A.x, hr.B.x) < node.x - 1f;
                int arms = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);
                bool signalled = arms >= 3 && vr.Class is RoadClass.Arterial or RoadClass.Street && hr.Class is RoadClass.Arterial or RoadClass.Street;
                float hv = vr.HalfWidth, hh = hr.HalfWidth;

                // Corner furniture stands back from the kerb tip: cars that clip a corner ride over the kerb, not into a pole.
                Vector3 Corner(float sx, float sz) => new(node.x + sx * (hv + 1.6f), CityLayout.KerbHeight, node.z + sz * (hh + 1.6f));
                bool Sidewalk(Vector3 c) => !OnCarriageway(new Vector2(c.x, c.z)) &&
                                            !(CityLayout.InLot(new Vector2(c.x, c.z), out var lot) && lot.Kind == LotKind.Parking);

                // Corner bollards on every sidewalk corner.
                foreach (var (sx, sz) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                {
                    var c = new Vector3(node.x + sx * (hv + 0.45f), CityLayout.KerbHeight, node.z + sz * (hh + 0.45f));
                    if (!Sidewalk(c) || !Sidewalk(Corner(sx, sz))) continue;
                    for (int i = 0; i < 4; i++)
                    {
                        // Two along each kerb, starting 2.5 m from the tip.
                        var b = c + (i < 2 ? new Vector3(sx * (2.5f + i * 1.6f), 0f, 0f) : new Vector3(0f, 0f, sz * (2.5f + (i - 2) * 1.6f)));
                        cm[kit.Metal].Cylinder(b, 0.12f, 0.95f, 8, true);
                        cm.Unshadowed(kit.Reflector).Cylinder(b + Vector3.up * 0.75f, 0.125f, 0.06f, 8, false);
                        Box(colliders, b + Vector3.up * 0.47f, new Vector3(0.25f, 0.95f, 0.25f), Quaternion.identity);
                    }
                }

                if (signalled)
                {
                    // (corner sign x, corner sign z, approach exists, travel direction, ns?)
                    var approaches = new[]
                    {
                        (1f, 1f, s, Vector3.forward, true),     // northbound, far-side right = NE
                        (-1f, -1f, n, Vector3.back, true),      // southbound = SW
                        (1f, -1f, w, Vector3.right, false),     // eastbound = SE
                        (-1f, 1f, e, Vector3.left, false),      // westbound = NW
                    };
                    foreach (var (sx, sz, exists, travel, ns) in approaches)
                    {
                        var c = Corner(sx, sz);
                        if (!exists || !Sidewalk(c)) continue;
                        float reach = (ns ? hv : hh) * 0.75f;
                        Vector3 armDir = ns ? new Vector3(-sx, 0f, 0f) : new Vector3(0f, 0f, -sz);
                        var face = Quaternion.LookRotation(-travel);
                        cm[kit.Metal].Cylinder(c, 0.16f, 6.4f, 10, true);
                        cm[kit.Metal].OrientedBox(c + Vector3.up * 6.2f + armDir * (reach * 0.5f + 0.5f), new Vector3(0.14f, 0.14f, reach + 1f), Quaternion.LookRotation(armDir), 1f);
                        Vector3 head = c + Vector3.up * 5.6f + armDir * (reach + 0.3f);
                        cm[kit.DarkPlastic].OrientedBox(head, new Vector3(0.45f, 1.25f, 0.32f), face, 1f);
                        cm[kit.DarkPlastic].OrientedBox(head + travel * 0.18f, new Vector3(0.75f, 1.45f, 0.03f), face, 1f);   // back plate
                        int set = ns ? 0 : 3;
                        for (int k = 0; k < 3; k++)
                            lenses[set + k].OrientedBox(head + Vector3.up * (0.38f - k * 0.38f) - travel * 0.17f, new Vector3(0.24f, 0.24f, 0.03f), face, 1f);
                        // Pedestrian-height head on the pole too.
                        Vector3 low = c + Vector3.up * 2.9f - travel * 0.25f;
                        cm[kit.DarkPlastic].OrientedBox(low, new Vector3(0.35f, 0.95f, 0.25f), face, 1f);
                        for (int k = 0; k < 3; k++)
                            lenses[set + k].OrientedBox(low + Vector3.up * (0.28f - k * 0.28f) - travel * 0.13f, new Vector3(0.18f, 0.18f, 0.03f), face, 1f);
                        Box(colliders, c + Vector3.up * 3.2f, new Vector3(0.35f, 6.4f, 0.35f), Quaternion.identity);
                        // Junction CCTV on every other signal pole, watching the middle of the junction.
                        if (Signals % 2 == 0) Cctv(cm, c + Vector3.up * 5.1f - armDir * 0.2f, node);
                        Signals++;
                    }
                }

                // Street-name blades on the NE (or first free) corner.
                foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, -1f), (1f, -1f), (-1f, 1f) })
                {
                    var c = Corner(sx, sz) + new Vector3(sx * 0.6f, 0f, sz * 0.6f);
                    if (!Sidewalk(c)) continue;
                    if (vr.Sign == null && hr.Sign == null) break;
                    cm[kit.Metal].Cylinder(c, 0.06f, 3.4f, 6, true);
                    if (vr.Sign != null) Blade(cm, c + Vector3.up * 3.15f, Quaternion.LookRotation(Vector3.right), vr.Sign);
                    if (hr.Sign != null) Blade(cm, c + Vector3.up * 2.8f, Quaternion.LookRotation(Vector3.forward), hr.Sign);
                    break;
                }
            }
            foreach (var pair in cells)
            {
                var cell = new GameObject($"Cell_{pair.Key.x}_{pair.Key.y}").transform;
                cell.SetParent(parent, false);
                pair.Value.Emit(cell, $"District_Intersections_{pair.Key.x}_{pair.Key.y}", environment);
            }
            var renderers = m.Emit(parent, "District_Intersections", environment);
            SignalRenderers = kit.SignalLenses.Select(l => renderers.FirstOrDefault(r => r.sharedMaterial == l)).ToArray();
        }

        /// <summary>A two-sided street-name plate, readable from both directions of the street it names.</summary>
        private void Blade(BlockMeshes m, Vector3 centre, Quaternion facing, string text)
        {
            var uv = DistrictTextures.SignRect(text, out float aspect);
            const float h = 0.3f;
            float w = h * aspect;
            var plate = facing * Quaternion.Euler(0f, 90f, 0f);
            m[kit.Roof].OrientedBox(centre, new Vector3(w + 0.1f, h + 0.06f, 0.04f), plate, 1f);
            m.Unshadowed(kit.StreetSign).Panel(centre + plate * Vector3.forward * 0.025f, plate, w, h, uv);
            var back = plate * Quaternion.Euler(0f, 180f, 0f);
            m.Unshadowed(kit.StreetSign).Panel(centre + back * Vector3.forward * 0.025f, back, w, h, uv);
        }
    }
}
