using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain, port activity with a purpose: crane trolleys, spreaders and hoist ropes that really work the ship,
    /// searchlights sweeping the restricted yard, and wet ground whose roughness varies instead of one uniform gloss.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private static readonly float[] CraneXs = { -440f, -350f, -260f, -170f, 60f };

        /// <summary>Height of the ship's deck stack below (x, z), probing the imported hull with temporary colliders.</summary>
        private static float DeckHeight(GameObject ship, float x, float z, float fallback)
        {
            if (ship == null) return fallback;
            var added = new List<MeshCollider>();
            foreach (var mf in ship.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) { var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; added.Add(c); }
            Physics.SyncTransforms();
            float best = -100f;
            // The highest hit over the box's footprint, so the box rests on the stack rather than in it.
            for (int i = -2; i <= 2; i++)
            for (int j = -1; j <= 1; j++)
                if (Physics.Raycast(new Vector3(x + i * 3f, 120f, z + j * 1.1f), Vector3.down, out var hit, 200f) && added.Contains(hit.collider as MeshCollider))
                    best = Mathf.Max(best, hit.point.y);
            foreach (var c in added) Object.DestroyImmediate(c);
            return best > -50f ? best : fallback;
        }

        /// <summary>Trolley, spreader, ropes and (for cranes over the ship) a working cycle with a box.</summary>
        private static List<HarborCraneCycle> BuildCraneRigs(Transform root, Random rng)
        {
            var cycles = new List<HarborCraneCycle>();
            var ship = GameObject.Find("ContainerShip_KowloonStar");
            var shipBounds = ship != null ? Bounds(ship) : new Bounds(new Vector3(-318f, 0f, 332f), new Vector3(300f, 40f, 42f));
            var ropeMesh = new NeonRift.EditorTools.District.MeshBuilder();
            ropeMesh.Cuboid(new Vector3(-0.035f, -1f, -0.035f), new Vector3(0.035f, 0f, 0.035f), 1f);
            var rope = SaveMesh(ropeMesh.ToMesh("HR_HoistRope", false), "HR_HoistRope");
            var lampMesh = CubeMesh("HR_SpreaderLampBox", new Vector3(0.4f, 0.3f, 0.4f));
            int n = 0;
            foreach (float x in CraneXs)
            {
                var trolley = new GameObject("CraneTrolley").transform;
                trolley.SetParent(root, false);
                trolley.position = new Vector3(x, 44.2f, 268f);
                Begin();
                // Trolley frame between the boom girders: machinery box, wheel trucks on the rails, sheave beams, walkway.
                B(craneBlue).Cuboid(new Vector3(-3.1f, 0.2f, -3.2f), new Vector3(3.1f, 2.4f, 3.2f), 2f);
                B(cladding).Cuboid(new Vector3(-2.2f, 2.4f, -2f), new Vector3(2.2f, 3.4f, 1.6f), 2f);
                foreach (float wx in new[] { -3.3f, 3.3f })
                foreach (float wz in new[] { -2.6f, 2.6f })
                    B(metal).Cylinder(new Vector3(wx, 2.6f, wz) + Vector3.left * 0.2f, 0.45f, 0.4f, 10, true);
                B(metal).Cuboid(new Vector3(-1.2f, -0.2f, -0.9f), new Vector3(1.2f, 0.2f, 0.9f), 1f);
                B(hazardStripe).Cuboid(new Vector3(-3.15f, 2.4f, 3.15f), new Vector3(3.15f, 2.6f, 3.25f), 1f);
                B(workLight).Cuboid(new Vector3(-0.6f, -0.05f, -3.25f), new Vector3(0.6f, 0.15f, -3.2f), 1f);
                Flush("TrolleyMesh", trolley, detail, false);
                var ropes = new List<Transform>();
                foreach (float rx in new[] { -0.55f, 0.55f })
                foreach (float rz in new[] { -0.45f, 0.45f })
                {
                    var r = Single("HoistRope", trolley, rope, metal, detail, trolley.position + new Vector3(rx, -0.2f, rz));
                    ropes.Add(r.transform);
                }

                var spreader = new GameObject("Spreader").transform;
                spreader.SetParent(root, false);
                spreader.position = new Vector3(x, 36f, 268f);
                Begin();
                // Spreader telescoped to 40 ft along the ship (x): main beam, end beams, headblock, twistlock housings.
                B(hazardStripe).Cuboid(new Vector3(-6.1f, -1.35f, -0.3f), new Vector3(6.1f, -0.9f, 0.3f), 1f);
                foreach (float ex in new[] { -6.1f, 5.6f }) B(hazardStripe).Cuboid(new Vector3(ex, -1.35f, -1.22f), new Vector3(ex + 0.5f, -0.95f, 1.22f), 1f);
                B(craneBlue).Cuboid(new Vector3(-1.2f, -0.9f, -0.8f), new Vector3(1.2f, 0f, 0.8f), 1f);
                foreach (float cx in new[] { -5.95f, 5.95f })
                foreach (float cz in new[] { -1.05f, 1.05f })
                    B(metal).Cuboid(new Vector3(cx - 0.16f, -1.5f, cz - 0.12f), new Vector3(cx + 0.16f, -1.35f, cz + 0.12f), 1f);
                Flush("SpreaderMesh", spreader, detail, false);
                var lamp = Single("SpreaderLamp", spreader, lampMesh, beacon, detail, spreader.position + new Vector3(0f, 0.15f, 0f)).GetComponent<Renderer>();

                bool overShip = x > shipBounds.min.x + 8f && x < shipBounds.max.x - 8f;
                if (!overShip) { n++; continue; }   // parked: trolley in over the backreach, spreader stowed high
                float zA = shipBounds.center.z - 9f, zB = shipBounds.center.z + 9f;
                float deckA = DeckHeight(ship, x, zA, 18f), deckB = DeckHeight(ship, x, zB, 18f);
                var box = new GameObject("CraneBox") { layer = environment }.transform;
                box.SetParent(root, false);
                Begin();
                B(containers[rng.Next(containers.Length)]).Cuboid(new Vector3(-6.1f, -1.3f, -1.22f), new Vector3(6.1f, 1.3f, 1.22f), 4f);
                Flush("BoxMesh", box, environment, false);
                var cycle = trolley.gameObject.AddComponent<HarborCraneCycle>();
                Configure(cycle, so =>
                {
                    so.FindProperty("trolley").objectReferenceValue = trolley;
                    so.FindProperty("spreader").objectReferenceValue = spreader;
                    so.FindProperty("box").objectReferenceValue = box;
                    Array(so.FindProperty("ropes"), ropes.Cast<Object>().ToArray());
                    so.FindProperty("lamp").objectReferenceValue = lamp;
                    so.FindProperty("bayAZ").floatValue = zA;
                    so.FindProperty("bayBZ").floatValue = zB;
                    so.FindProperty("apronZ").floatValue = 270f;
                    // Spreader height with the box seated on the stack: box bottom on the deck, spreader 2.65 m above its centre.
                    so.FindProperty("deckAY").floatValue = deckA + 1.3f + 2.65f;
                    so.FindProperty("deckBY").floatValue = deckB + 1.3f + 2.65f;
                    so.FindProperty("travelY").floatValue = Mathf.Max(deckA, deckB) + 12f;
                    so.FindProperty("startDelay").floatValue = n * 9f;
                });
                cycles.Add(cycle);
                n++;
            }
            report.AppendLine($"cranes: {CraneXs.Length} trolleys with spreaders and hoist ropes, {cycles.Count} working the ship (deck probe), box never below 7 m over the apron");
            return cycles;
        }

        /// <summary>Two searchlight towers at the restricted yard: lamp heads sweeping round with a visible beam in the rain.</summary>
        private static List<Transform> BuildSearchlights(Transform root)
        {
            var heads = new List<Transform>();
            var beamMat = Glow("HR_SearchBeam", new Color(0.16f, 0.18f, 0.2f), AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_NeonRift/Art/District/Textures/District_GlowSoft.png"));
            var beamBuilder = new NeonRift.EditorTools.District.MeshBuilder();
            beamBuilder.Cone(Vector3.zero, 5.5f, 46f, 14, 0.25f);
            var beamMesh = SaveMesh(beamBuilder.ToMesh("HR_SearchBeamCone", false), "HR_SearchBeamCone");
            foreach (var p in new[] { new Vector3(Restricted.xMin - 4f, 0f, Restricted.yMin - 4f), new Vector3(Restricted.xMax + 4f, 0f, Restricted.yMax - 4f) })
            {
                Begin();
                B(metal).Cylinder(p, 0.3f, 14f, 10, true);
                B(metal).Cuboid(p + new Vector3(-1.2f, 13.6f, -1.2f), p + new Vector3(1.2f, 14f, 1.2f), 1f);
                Flush("SearchlightTower", root, environment, false);
                Collider(root, "SearchlightTowerCollider", p + Vector3.up * 7f, new Vector3(0.7f, 14f, 0.7f), environment);
                var head = new GameObject("SearchlightHead") { layer = detail }.transform;
                head.SetParent(root, false);
                head.position = p + Vector3.up * 14.6f;
                Begin();
                B(metal).Cylinder(Vector3.zero + new Vector3(0f, -0.3f, 0f), 0.45f, 0.9f, 12, true);
                Flush("Lamp", head, detail, false);
                var aim = new GameObject("Aim").transform;
                aim.SetParent(head, false);
                aim.localRotation = Quaternion.Euler(24f, 0f, 0f);
                var lens = Single("Lens", aim, CubeMesh("HR_SearchLens", new Vector3(0.7f, 0.7f, 0.1f)), workLight, detail, head.position);
                lens.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                var light = aim.gameObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = new Color(0.85f, 0.92f, 1f);
                light.intensity = 26000f;
                light.range = 130f;
                light.spotAngle = 15f;
                light.innerSpotAngle = 6f;
                light.shadows = LightShadows.None;
                // The beam: a long faint cone along the light's axis (cone mesh points −Y; turn it to +Z).
                var beam = Single("Beam", aim, beamMesh, beamMat, detail, head.position);
                beam.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                beam.transform.localPosition = Vector3.zero;
                var sl = head.gameObject.AddComponent<HarborSearchlight>();
                sl.EditorConfigure(aim, heads.Count == 0 ? 16f : -13f);
                searchlightHeads.Add(sl);
                heads.Add(head);
            }
            return heads;
        }

        /// <summary>Varied wetness: a tiling gloss mask (patches of standing water and drier, rougher asphalt).</summary>
        private static void WetVariation(params Material[] targets)
        {
            var rng = new Random(13);
            float[,] lattice = new float[32, 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) lattice[x, y] = (float)rng.NextDouble();
            float V(float u, float v, int cells)
            {
                float px = u * cells, py = v * cells;
                int x0 = Mathf.FloorToInt(px), y0 = Mathf.FloorToInt(py);
                float fx = px - x0, fy = py - y0;
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                float L(int x, int y) => lattice[((x % cells) + cells) % cells, ((y % cells) + cells) % cells];
                return Mathf.Lerp(Mathf.Lerp(L(x0, y0), L(x0 + 1, y0), fx), Mathf.Lerp(L(x0, y0 + 1), L(x0 + 1, y0 + 1), fx), fy);
            }
            var gloss = Texture("HR_WetGloss", 256, 256, (x, y) =>
            {
                float u = x / 256f, v = y / 256f;
                float n = V(u, v, 4) * 0.55f + V(u, v, 8) * 0.3f + V(u, v, 32) * 0.15f;
                float a = Mathf.Lerp(0.62f, 1f, SStep(0.3f, 0.75f, n));   // smoothness multiplier
                return new Color(0f, 0f, 0f, a);
            }, false);
            foreach (var m in targets)
            {
                if (m == null) continue;
                m.SetTexture("_MetallicGlossMap", gloss);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", Mathf.Min(0.92f, m.GetFloat("_Smoothness") + 0.1f));
                m.SetFloat("_SmoothnessTextureChannel", 0f);
                m.SetTextureScale("_MetallicGlossMap", Vector2.one);
                EditorUtility.SetDirty(m);
            }
        }
    }
}
