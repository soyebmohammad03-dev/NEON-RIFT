using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeonRift.EditorTools.District;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain layout (metres, +Z north = the sea, quay edge at z = 300). Six connected zones inside a 1320 × 630 m
    /// security perimeter: the SECURITY PERIMETER (south fence, main checkpoint at (0, -330), guard towers with
    /// searchlights, the east extraction gate at (660, 230)); the INDUSTRIAL PROCESSING zone (south-west: tank farm,
    /// warehouses, substation, generators); the FREIGHT YARD (south-east: rail sidings, truck staging, parking, depot);
    /// the CONTAINER TERMINAL (centre: container blocks between lanes, inspection checkpoint on lane 80, the manifest
    /// office, and the fenced RESTRICTED CARGO AREA); the CARGO SHIP BERTH (north-west: a 320 m vessel and five
    /// ship-to-shore cranes) and the FLOODED QUAYSIDE (north-east low dock road, which floods deep and closes).
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private const float QuayEdge = 300f;
        private static readonly Vector3 Spawn = new(0f, 0.05f, -470f);
        private static readonly Vector3 Extraction = new(2250f, 0f, 552f);   // pier end (Harbor Rain 2.0; was the coast road's end at x 1640)
        private static readonly Rect Restricted = new(112f, 96f, 226f, 120f);
        private static readonly Vector3[] Bays = { new(196f, 0f, 125f), new(254f, 0f, 160f), new(196f, 0f, 195f) };
        private static readonly Vector3[] BayPoints = { new(205f, 0f, 125f), new(245f, 0f, 160f), new(205f, 0f, 195f) };

        private static readonly List<Rect> solids = new();
        private static bool shipModel;

        /// <summary>Drivable roads (min, max on XZ). Containers, buildings and props never overlap these.</summary>
        private static readonly (Vector2 min, Vector2 max)[] Roads =
        {
            (new(-9f, -640f), new(9f, -330f)),          // 0 entry road
            (new(-650f, -312f), new(650f, -298f)),      // 1 south perimeter road
            (new(-8f, -330f), new(8f, 238f)),           // 2 spine
            (new(-648f, -312f), new(-634f, 238f)),      // 3 west road
            (new(634f, -312f), new(648f, 238f)),        // 4 east road
            (new(-648f, -187f), new(648f, -173f)),      // 5 cross road
            (new(-648f, -57f), new(648f, -43f)),        // 6 terminal road
            (new(-648f, 74f), new(648f, 86f)),          // 7 lane 80
            (new(-648f, 222f), new(900f, 238f)),        // 8 quay road → east gate → coast
            (new(-456f, -43f), new(-444f, 222f)),       // 9 lane W450
            (new(-156f, -43f), new(-144f, 222f)),       // 10 lane W150
            (new(84f, -43f), new(96f, 222f)),           // 11 lane E90
            (new(444f, -43f), new(456f, 222f)),         // 12 lane E450
            (new(374f, -298f), new(386f, -187f)),       // 13 freight connector
            (new(-386f, -298f), new(-374f, -187f)),     // 14 industrial connector
            (new(444f, -173f), new(456f, -57f)),        // 15 freight north exit
            (new(150f, -246f), new(634f, -234f)),       // 16 freight service road
            (new(-634f, -126f), new(-60f, -114f)),      // 17 industrial service road
            (new(213f, 96f), new(237f, 222f)),          // 18 restricted-area lane
            (new(-256f, -173f), new(-244f, -57f)),      // 19 industrial north exit
            (new(884f, 150f), new(900f, 284f)),         // 20 coast junction (quay road ↔ coast road ↔ service road)
            (new(884f, 268f), new(1916f, 284f)),        // 21 COAST ROAD (sea side, wave sections) → the first bend

            (new(884f, 150f), new(1600f, 162f)),        // 22 inland service road (dry bypass)
            (new(1584f, 150f), new(1600f, 284f)),       // 23 east link (the bypass rejoins the sea road)
            (new(1900f, 268f), new(1916f, 560f)),       // 24 HEADLAND ROAD (breakwater, sea both sides)
            (new(1900f, 544f), new(2280f, 560f)),       // 25 PIER ROAD → extraction
        };

        public static string Build()
        {
            report.Clear();
            solids.Clear();
            extraSpinners.Clear();
            extraRockers.Clear();
            searchlightHeads.Clear();
            bayCrane = null; heistAlarm = null; muleTruck = null;
            signs.Clear();
            meshCount = 0;
            foreach (var d in new[] { "Scenes", "Generated", "Generated/Meshes", "Generated/Materials", "Generated/Audio" })
                Directory.CreateDirectory($"{Root}/{d}");
            foreach (var old in Directory.GetFiles($"{Gen}/Meshes", "*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            drivable = LayerMask.NameToLayer("Drivable");
            environment = LayerMask.NameToLayer("Environment");
            detail = LayerMask.NameToLayer("Detail");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.Refresh();
            BuildMaterials();
            BuildCoastMaterials();
            containerMeshes.Clear(); containerMats.Clear(); markingMats.Clear(); markingN = 0;
            BuildTruckPrefabs();       // Harbor Rain 2.2: shared port truck prefabs (tractor + trailer)
            BuildForkliftPrefabs();    // Harbor Rain 2.2: working / parked forklift prefabs + pallet units
            var audio = BuildAudio();
            var clips = HarborRainAudio.Build(report);
            reverseBeepClip = clips.ReverseBeep;
            var mixerConfig = AssetDatabase.LoadAssetAtPath<NeonRift.Audio.AudioMixerConfig>("Assets/_NeonRift/Data/Audio/AudioMixerConfig.asset");
            var sfxGroup = mixerConfig != null ? mixerConfig.Sfx : null;
            var ambGroup = mixerConfig != null ? mixerConfig.Ambience : null;
            var horns = new List<AudioSource>();

            var world = new GameObject("World").transform;
            var rng = new Random(7);
            BuildGround(world);
            var gates = BuildPerimeter(world);
            BuildTerminal(world, rng, out var glows);
            BuildRestricted(world, rng, gates);
            BuildIndustrial(world, rng);
            BuildFreightYard(world, rng);
            shipModel = BuildShips(world, rng, clips, ambGroup, horns);
            BuildBerth(world, rng);
            BuildBackdrop(world, rng);
            var security = BuildSecurity(world);
            var coast = BuildCoast(world, rng, clips, sfxGroup, ambGroup);
            BuildBlockades(world, coast, rng, clips, sfxGroup);
            BuildDensity(world, new Random(31));   // own seed: the rest of the layout stays as it was
            BuildGateApproach(world, new Random(37));
            var lamps = new List<Renderer>();
            var alarms = new List<Light>();
            var budget = BuildLighting(world, lamps, alarms);
            BuildWetDetails(world, rng);
            var floods = BuildFloods(world, clips.WaterRush);
            var storm = BuildStorm(audio, clips);
            BuildOcean(world, storm, coast.Waves);
            BuildIndustrialAmbience(world);
            coast.Life = BuildLife(world, rng, clips, ambGroup, horns);
            BuildSoundscape(world, clips, ambGroup);
            BuildRig(gates, floods, storm, lamps, alarms, glows, security, budget, audio, coast, clips, sfxGroup);
            BuildAtmosphere();
            ClearMovingStatics();

            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.position = new Vector3(60f, 14f, -60f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(1700f, 90f, 1000f);
            probe.resolution = 256;
            // Local probes where reflections read most (wet apron under the cranes, the terminal, the coast road).
            var local = new List<ReflectionProbe>();
            // 2.2: + the gate approach / truck holding area, which sat outside every probe and fell back to Unity's default
            // (daylight) environment cubemap — the pale blue sheen across that ground.
            foreach (var (n, c, size) in new[] { ("Berth", new Vector3(-300f, 10f, 250f), new Vector3(420f, 40f, 120f)), ("Terminal", new Vector3(100f, 8f, 60f), new Vector3(500f, 30f, 340f)),
                                                  ("Gate", new Vector3(-30f, 8f, -500f), new Vector3(300f, 30f, 300f)),
                                                  ("Coast", new Vector3(1400f, 8f, 230f), new Vector3(1060f, 30f, 180f)),
                                                  ("Headland", new Vector3(2080f, 8f, 452f), new Vector3(440f, 30f, 340f)) })
            {
                var g = new GameObject("ReflectionProbe_" + n);
                g.transform.position = c;
                var lp = g.AddComponent<ReflectionProbe>();
                lp.mode = ReflectionProbeMode.Baked;
                lp.size = size;
                lp.resolution = 128;
                lp.importance = 2;
                lp.blendDistance = 20f;
                local.Add(lp);
            }
            EditorSceneManager.SaveScene(scene, ScenePath);
            Lightmapping.BakeReflectionProbe(probe, $"{Gen}/HarborRain_Reflection.exr");
            foreach (var lp in local) Lightmapping.BakeReflectionProbe(lp, $"{Gen}/HarborRain_Reflection_{lp.name.Replace("ReflectionProbe_", "")}.exr");
            // Anything outside the probes reflects the level's own storm capture, never Unity's default daylight cubemap.
            var fallback = AssetDatabase.LoadAssetAtPath<Cubemap>($"{Gen}/HarborRain_Reflection.exr");
            if (fallback != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = fallback;
                RenderSettings.reflectionIntensity = 0.8f;
            }
            EditorSceneManager.SaveScene(scene, ScenePath);

            Validate();
            report.Insert(0, $"[HarborRain] built {ScenePath}: {meshCount} generated meshes\n");
            File.WriteAllText("Logs/HarborRainBuild.txt", report.ToString());
            Debug.Log(report.ToString());
            return report.ToString();
        }

        // ---------------- Ground and roads ----------------

        private static void BuildGround(Transform world)
        {
            Begin();
            B(wetConcrete).Ground(new Vector3(-720f, 0f, -660f), new Vector3(880f, 0f, QuayEdge), 8f);
            for (int i = 0; i < Roads.Length; i++)
            {
                var (min, max) = Roads[i];
                float y = 0.01f + i * 0.0015f;
                B(wetAsphalt).Ground(new Vector3(min.x, y, min.y), new Vector3(max.x, y, max.y), 6f);
                bool alongZ = max.y - min.y > max.x - min.x;
                float len = alongZ ? max.y - min.y : max.x - min.x, width = alongZ ? max.x - min.x : max.y - min.y;
                // Centre dashes, solid edge lines, and a stop bar at every junction this road runs into.
                for (float s = 4f; s < len - 4f; s += 10f)
                {
                    if (NearJunction(i, alongZ ? new Vector3((min.x + max.x) * 0.5f, 0f, min.y + s) : new Vector3(min.x + s, 0f, (min.y + max.y) * 0.5f))) continue;
                    Vector3 a = alongZ ? new Vector3((min.x + max.x) * 0.5f, 0.04f, min.y + s) : new Vector3(min.x + s, 0.04f, (min.y + max.y) * 0.5f);
                    B(i == 8 ? markingYellow : marking).Strip(a, a + (alongZ ? Vector3.forward : Vector3.right) * 4f, 0.16f);
                }
                // The curved coastal roads draw their own edge lines (HarborRainBuilder.Headland).
                foreach (float side in i is 21 or 24 or 25 ? new float[0] : new[] { -1f, 1f })
                {
                    float off = width * 0.5f - 0.6f;
                    Vector3 c = new((min.x + max.x) * 0.5f, 0.04f, (min.y + max.y) * 0.5f);
                    Vector3 a = alongZ ? new Vector3(c.x + side * off, 0.04f, min.y + 2f) : new Vector3(min.x + 2f, 0.04f, c.z + side * off);
                    Vector3 b = alongZ ? new Vector3(c.x + side * off, 0.04f, max.y - 2f) : new Vector3(max.x - 2f, 0.04f, c.z + side * off);
                    B(markingYellow).Strip(a, b, 0.12f);
                }
            }
            // Quay: wall into the sea, edge line, fenders and bollards.
            B(concreteDark).Cuboid(new Vector3(-720f, -4f, QuayEdge - 0.5f), new Vector3(880f, 0f, QuayEdge + 0.8f), 4f);
            B(hazardStripe).Strip(new Vector3(-720f, 0.035f, QuayEdge - 1f), new Vector3(880f, 0.035f, QuayEdge - 1f), 0.6f);
            for (float x = -710f; x < 870f; x += 18f)
            {
                B(metal).Cylinder(new Vector3(x, 0f, QuayEdge - 1.6f), 0.32f, 0.75f, 10, true);
                B(rubber).OrientedBox(new Vector3(x + 9f, -1.9f, QuayEdge + 1.1f), new Vector3(2.4f, 3.2f, 0.6f), Quaternion.identity, 2f);
            }
            Flush("Ground", world, drivable, false);

            var ground = Collider(world, "GroundCollider", new Vector3(80f, -0.5f, -180f), new Vector3(1600f, 1f, 960f), drivable);
            ground.gameObject.AddComponent<DrivingSurface>().EditorConfigure(0.78f, 1.2f);
            Collider(world, "QuayEdgeWall", new Vector3(80f, 0.8f, QuayEdge), new Vector3(1600f, 1.6f, 0.6f), environment);
            Collider(world, "BoundWest", new Vector3(-720f, 2f, -180f), new Vector3(1f, 4f, 960f), environment);
            Collider(world, "BoundEast", new Vector3(880f, 2f, -280f), new Vector3(1f, 4f, 760f), environment);
            Collider(world, "BoundSouth", new Vector3(80f, 2f, -660f), new Vector3(1600f, 4f, 1f), environment);

            // The sea is the GPU ocean (HarborRainBuilder.Coast BuildOcean); no flat plane.
        }

        private static bool OnRoad(Vector3 p, float margin, int except = -1)
        {
            for (int i = 0; i < Roads.Length; i++)
            {
                if (i == except) continue;
                var (min, max) = Roads[i];
                if (p.x > min.x - margin && p.x < max.x + margin && p.z > min.y - margin && p.z < max.y + margin) return true;
            }
            return false;
        }

        private static bool NearJunction(int road, Vector3 p) => OnRoad(p, 2f, road);

        private static bool Blocked(Rect r, float margin = 2f)
        {
            var e = new Rect(r.x - margin, r.y - margin, r.width + margin * 2f, r.height + margin * 2f);
            foreach (var (min, max) in Roads) if (e.Overlaps(new Rect(min, max - min))) return true;
            foreach (var s in solids) if (e.Overlaps(s)) return true;
            return false;
        }

        // ---------------- Security perimeter, gates ----------------

        private sealed class Gates { public HarborGate Main, Security, North, Extraction, Lane; }

        private static Gates BuildPerimeter(Transform world)
        {
            var root = new GameObject("SecurityPerimeter").transform;
            root.SetParent(world, false);
            Begin();
            Fence(root, new Vector3(-660f, 0f, -330f), new Vector3(-13f, 0f, -330f));
            Fence(root, new Vector3(13f, 0f, -330f), new Vector3(660f, 0f, -330f));
            Fence(root, new Vector3(-660f, 0f, -330f), new Vector3(-660f, 0f, QuayEdge - 2f));
            Fence(root, new Vector3(660f, 0f, -330f), new Vector3(660f, 0f, 220f));
            Fence(root, new Vector3(660f, 0f, 240f), new Vector3(660f, 0f, QuayEdge - 2f));
            for (int i = 0; i < 7; i++)
            {
                Jersey(new Vector3(-16f - i * 3.4f, 0f, -342f), 0f);
                Jersey(new Vector3(16f + i * 3.4f, 0f, -342f), 0f);
            }
            // East gate approach: hazard bar on the road; the extraction compound is at the far end of the coast road.
            B(hazardStripe).Strip(new Vector3(672f, 0.045f, 222f), new Vector3(672f, 0.045f, 238f), 1.2f);
            Flush("PerimeterMeshes", root, environment, false);
            Collider(root, "CheckpointBarriersW", new Vector3(-26f, 0.55f, -342f), new Vector3(24f, 1.1f, 0.8f), environment);
            Collider(root, "CheckpointBarriersE", new Vector3(26f, 0.55f, -342f), new Vector3(24f, 1.1f, 0.8f), environment);
            SignPanel(root, "EAST GATE", new Color(1f, 0.7f, 0.2f), new Vector3(668f, 10.4f, 230f), 90f, 8f);
            SignPanel(root, "KOWLOON BAY TERMINAL", new Color(0.33f, 0.91f, 1f), new Vector3(0f, 9.2f, -323f), 180f, 14f);

            // Guard towers with sweeping searchlights (a scanning yaw, no shadows).
            foreach (float x in new[] { -300f, 300f })
            {
                var p = new Vector3(x, 0f, -322f);
                Begin();
                foreach (var o in new[] { new Vector3(-1.6f, 0, -1.6f), new Vector3(1.6f, 0, -1.6f), new Vector3(-1.6f, 0, 1.6f), new Vector3(1.6f, 0, 1.6f) })
                    B(metal).Cuboid(p + o - new Vector3(0.15f, 0f, 0.15f), p + o + new Vector3(0.15f, 12f, 0.15f), 2f);
                B(concreteDark).Cuboid(p + new Vector3(-2.4f, 12f, -2.4f), p + new Vector3(2.4f, 15f, 2.4f), 2f);
                B(indWindow).Cuboid(p + new Vector3(-2.45f, 13f, -2.45f), p + new Vector3(2.45f, 14.2f, 2.45f), 4f);
                B(roof).Cuboid(p + new Vector3(-2.8f, 15f, -2.8f), p + new Vector3(2.8f, 15.4f, 2.8f), 2f);
                Flush("GuardTower", root, environment);
                Collider(root, "TowerCollider", p + Vector3.up * 7.5f, new Vector3(4f, 15f, 4f), environment);
                var pivot = new GameObject("Searchlight") { layer = detail };
                pivot.transform.SetParent(root, false);
                pivot.transform.SetPositionAndRotation(p + Vector3.up * 15.8f, Quaternion.Euler(0f, x < 0 ? 30f : -30f, 0f));
                var lightGo = new GameObject("Beam");
                lightGo.transform.SetParent(pivot.transform, false);
                lightGo.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(0.85f, 0.92f, 1f);
                l.intensity = 9000f;
                l.range = 140f;
                l.spotAngle = 16f;
                l.innerSpotAngle = 8f;
                l.shadows = LightShadows.None;
                var sentry = pivot.AddComponent<HoverSentry>();
                Configure(sentry, so => { so.FindProperty("bob").floatValue = 0f; so.FindProperty("scan").vector2Value = new Vector2(60f, 11f); });
            }

            return new Gates
            {
                Main = BuildGate(root, "MainGate", new Vector3(0f, 0f, -330f), 0f, 22f),
                Extraction = BuildGate(root, "EastExtractionGate", new Vector3(660f, 0f, 230f), 90f, 18f),
                Lane = BuildGate(root, "Lane80Checkpoint", new Vector3(520f, 0f, 80f), 90f, 14f, startOpen: true),
            };
        }

        private static void Fence(Transform root, Vector3 a, Vector3 b)
        {
            Vector3 dir = (b - a).normalized;
            float len = Vector3.Distance(a, b);
            for (float s = 0f; s <= len + 0.01f; s += 4f) B(metal).Cylinder(a + dir * s, 0.06f, 2.9f, 6, true);
            var rot = Quaternion.LookRotation(dir);
            foreach (float h in new[] { 0.15f, 1.4f, 2.8f }) B(metal).OrientedBox((a + b) * 0.5f + Vector3.up * h, new Vector3(0.05f, 0.06f, len), rot, 4f);
            B(concreteDark).OrientedBox((a + b) * 0.5f + Vector3.up * 1.45f, new Vector3(0.02f, 2.5f, len), rot, 4f);
            for (float s = 2f; s < len; s += 3f)   // barbed-wire coil read as a beaded line
                B(metal).OrientedBox(a + dir * s + Vector3.up * 3.1f, new Vector3(0.35f, 0.35f, 0.35f), Quaternion.Euler(45f, 45f, 0f), 1f);
            Collider(root, "FenceCollider", (a + b) * 0.5f + Vector3.up * 1.5f, new Vector3(0.3f, 3f, len), environment, rot.eulerAngles.y);
            var r = new Rect(Mathf.Min(a.x, b.x) - 0.3f, Mathf.Min(a.z, b.z) - 0.3f, Mathf.Abs(b.x - a.x) + 0.6f, Mathf.Abs(b.z - a.z) + 0.6f);
            solids.Add(r);
        }

        private static void Jersey(Vector3 p, float yaw)
        {
            var r = Quaternion.Euler(0f, yaw, 0f);
            B(concreteDark).OrientedBox(p + Vector3.up * 0.35f, new Vector3(3.2f, 0.7f, 0.7f), r, 1.6f);
            B(hazardStripe).OrientedBox(p + Vector3.up * 0.85f, new Vector3(3.2f, 0.3f, 0.45f), r, 1.2f);
        }

        private static HarborGate BuildGate(Transform world, string name, Vector3 position, float yaw, float laneWidth, bool startOpen = false)
        {
            var root = new GameObject(name).transform;
            root.SetParent(world, false);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            float half = laneWidth * 0.5f;
            Begin();
            B(concreteDark).Cuboid(new Vector3(half + 1f, 0f, -3f), new Vector3(half + 6f, 3.2f, 3f), 2f);
            B(indWindow).Cuboid(new Vector3(half + 0.95f, 1.2f, -2.4f), new Vector3(half + 6.05f, 2.4f, 2.4f), 4f);
            B(roof).Cuboid(new Vector3(half + 0.6f, 3.2f, -3.4f), new Vector3(half + 6.4f, 3.5f, 3.4f), 2f);
            B(hazardStripe).Cuboid(new Vector3(-half - 0.5f, 0f, -0.5f), new Vector3(-half + 0.5f, 1.3f, 0.5f), 1f);
            foreach (float x in new[] { -half - 1.4f, half + 0.6f })
                B(metal).Cuboid(new Vector3(x, 0f, 5.6f), new Vector3(x + 0.4f, 7.5f, 6f), 2f);
            B(metal).Cuboid(new Vector3(-half - 1.4f, 7.5f, -7f), new Vector3(half + 1f, 8.1f, 7f), 4f);
            B(neonAmber).Cuboid(new Vector3(-half - 1.4f, 7.4f, -7.05f), new Vector3(half + 1f, 7.5f, -6.95f), 4f);
            Flush("Structure", root, environment);
            Collider(root, "GatehouseCollider", position + Quaternion.Euler(0f, yaw, 0f) * new Vector3(half + 3.5f, 1.6f, 0f), new Vector3(5f, 3.2f, 6f), environment, yaw);

            var pivot = new GameObject("ArmPivot").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(-half, 1.1f, 0f);
            Begin();
            B(hazardStripe).Cuboid(new Vector3(0f, -0.12f, -0.12f), new Vector3(laneWidth, 0.12f, 0.12f), 1.2f);
            Flush("Arm", pivot, detail, false);
            var blocker = Collider(root, "Blocker", Vector3.zero, new Vector3(laneWidth, 3f, 0.8f), environment);
            blocker.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            blocker.transform.localRotation = Quaternion.identity;

            var lampMesh = CubeMesh("HR_LampBox", new Vector3(0.5f, 0.35f, 0.5f));
            var lampA = Single("LampA", root, lampMesh, gateLamp, detail, Vector3.zero);
            lampA.transform.localPosition = new Vector3(-half, 1.6f, 0f);
            var lampB = Single("LampB", root, lampMesh, gateLamp, detail, Vector3.zero);
            lampB.transform.localPosition = new Vector3(half + 1f, 3.9f, 3.2f);
            var gate = root.gameObject.AddComponent<HarborGate>();
            Configure(gate, so =>
            {
                so.FindProperty("arm").objectReferenceValue = pivot;
                so.FindProperty("blocker").objectReferenceValue = blocker;
                Array(so.FindProperty("lamps"), lampA.GetComponent<Renderer>(), lampB.GetComponent<Renderer>());
                so.FindProperty("startOpen").boolValue = startOpen;
            });
            return gate;
        }

        private static Mesh CubeMesh(string name, Vector3 size)
        {
            string path = $"{Gen}/Meshes/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var b = new MeshBuilder();
            b.Cuboid(-size * 0.5f, size * 0.5f, 1f, true);
            return SaveMesh(b.ToMesh(name, false), name);
        }

        /// <summary>A sign board on two posts (or wall-mounted when <paramref name="posts"/> is false).</summary>
        private static void SignPanel(Transform root, string text, Color colour, Vector3 centre, float yaw, float width, bool posts = false)
        {
            var mat = Sign(text, colour);
            var tex = mat.GetTexture("_BaseMap");
            float height = width * tex.height / tex.width;
            Begin();
            var rot = Quaternion.Euler(0f, yaw, 0f);
            B(mat).Panel(centre + rot * new Vector3(0f, 0f, 0.06f), rot, width, height, new Rect(0f, 0f, 1f, 1f));
            B(mat).Panel(centre + rot * new Vector3(0f, 0f, -0.06f), rot * Quaternion.Euler(0f, 180f, 0f), width, height, new Rect(0f, 0f, 1f, 1f));
            if (posts)
                foreach (float s in new[] { -0.4f, 0.4f })
                {
                    var p = centre + rot * new Vector3(s * width, 0f, 0f);
                    B(metal).Cuboid(new Vector3(p.x - 0.08f, 0f, p.z - 0.08f), new Vector3(p.x + 0.08f, centre.y - height * 0.5f, p.z + 0.08f), 2f);
                }
            Flush("Sign_" + new string(System.Array.ConvertAll(text.ToCharArray(), ch => char.IsLetterOrDigit(ch) && ch < 128 ? ch : '_')), root, detail, false);
        }

        // ---------------- Container terminal ----------------

        private static void BuildTerminal(Transform world, Random rng, out Renderer[] glows)
        {
            var root = new GameObject("ContainerTerminal").transform;
            root.SetParent(world, false);
            // Manifest office by the spine / terminal road junction, with its kiosk on the road.
            Begin();
            Building(new Rect(18f, -36f, 56f, 24f), 9f, cladding, true);
            B(concreteDark).Cuboid(new Vector3(44f, 0f, -42f), new Vector3(48f, 1.6f, -40f), 2f);
            B(neonCyan).Cuboid(new Vector3(44.2f, 1.1f, -42.05f), new Vector3(47.8f, 1.5f, -42f), 2f);
            Flush("ManifestOffice", root, environment);
            Collider(root, "Kiosk", new Vector3(46f, 0.8f, -41f), new Vector3(4f, 1.6f, 2f), environment);
            solids.Add(new Rect(44f, -42f, 4f, 2f));
            SignPanel(root, "TERMINAL OFFICE", new Color(0.33f, 0.91f, 1f), new Vector3(46f, 7.5f, -11.8f), 0f, 10f);
            SignPanel(root, "MANIFEST", new Color(0.33f, 0.91f, 1f), new Vector3(46f, 2.6f, -41f), 180f, 3.2f);

            // Cargo handling: rubber-tyred gantries over two blocks, straddle carriers parked in aisles.
            Begin();
            Gantry(new Vector3(-300f, 0f, 150f), 120f, 30f, 22f);
            Gantry(new Vector3(540f, 0f, 10f), 160f, 26f, 20f);
            foreach (var p in new[] { new Vector3(-90f, 0f, 30f), new Vector3(-520f, 0f, 150f), new Vector3(560f, 0f, 160f), new Vector3(-300f, 0f, 30f) })
                Straddle(root, p);
            Flush("TerminalMachines", root, environment);
            // Container blocks between the lanes (band A: terminal road → lane 80, band B: lane 80 → quay road).
            Begin();
            var columns = new List<(float, float)> { (-626f, -462f), (-438f, -162f), (-138f, -14f), (104f, 436f), (462f, 626f) };
            foreach (var (x0, x1) in columns)
            {
                Yard(root, rng, x0, x1, -36f, 62f, 4);
                if (x0 == 104f) Yard(root, rng, 346f, 436f, 92f, 214f, 4);
                else Yard(root, rng, x0, x1, 92f, 214f, 5);
            }
            Yard(root, rng, 14f, 78f, 92f, 214f, 3);
            Flush("TerminalContainers", root, environment);

            SignPanel(root, "INSPECTION", new Color(1f, 0.7f, 0.2f), new Vector3(520f, 9.4f, 80f), 270f, 8f);
            SignPanel(root, "LANE 80", new Color(1f, 1f, 1f), new Vector3(470f, 5f, 90f), 270f, 4f, true);
            SignPanel(root, "BERTH 4", new Color(1f, 1f, 1f), new Vector3(-300f, 5f, 246f), 180f, 5f, true);

            glows = new Renderer[Bays.Length];
        }

        /// <summary>Blocks of 3×5 container columns with 8 m aisles and an open turning square every few groups.</summary>
        private static void Yard(Transform root, Random rng, float x0, float x1, float z0, float z1, int maxTiers)
        {
            const float L = 12.2f, H = 2.6f, W = 2.44f, gap = 0.3f, aisle = 9f;
            float groupX = 3 * (L + gap), groupZ = 5 * (W + gap);
            int gi = 0;
            for (float gx = x0; gx + groupX <= x1; gx += groupX + aisle)
            for (float gz = z0; gz + groupZ <= z1; gz += groupZ + aisle)
            {
                gi++;
                var footprint = new Rect(gx, gz, groupX, groupZ);
                if (gi % 7 == 3 || rng.NextDouble() < 0.14 || Blocked(footprint, 1.5f)) continue;
                for (int row = 0; row < 5; row++)
                {
                    float z = gz + row * (W + gap) + W * 0.5f;
                    int rowMax = 0;
                    for (int col = 0; col < 3; col++)
                    {
                        float x = gx + col * (L + gap) + L * 0.5f;
                        int tiers = 1 + rng.Next(maxTiers);
                        rowMax = Mathf.Max(rowMax, tiers);
                        for (int k = 0; k < tiers; k++)
                            StackBox(containers[rng.Next(containers.Length)], new Vector3(x, H * (k + 0.5f), z), new Vector3(L, H - 0.04f, W), Quaternion.identity);
                    }
                    Collider(root, "Stack", new Vector3(gx + groupX * 0.5f, rowMax * H * 0.5f, z), new Vector3(groupX, rowMax * H, W), environment);
                }
                solids.Add(footprint);
            }
        }

        private static void Gantry(Vector3 centre, float span, float depth, float height)
        {
            foreach (float dx in new[] { -span * 0.5f, span * 0.5f })
            foreach (float dz in new[] { -depth * 0.5f, depth * 0.5f })
            {
                var p = centre + new Vector3(dx, 0f, dz);
                var r = new Rect(p.x - 0.8f, p.z - 0.8f, 1.6f, 1.6f);
                if (Blocked(r, 0.2f)) continue;
                B(craneSteel).Cuboid(p + new Vector3(-0.7f, 0f, -0.7f), p + new Vector3(0.7f, height, 0.7f), 3f);
                Collider(null, "GantryLeg", p + Vector3.up * height * 0.5f, new Vector3(1.4f, height, 1.4f), environment);
                solids.Add(r);
            }
            foreach (float dz in new[] { -depth * 0.5f, depth * 0.5f })
                B(craneSteel).Cuboid(centre + new Vector3(-span * 0.5f - 1f, height, dz - 1f), centre + new Vector3(span * 0.5f + 1f, height + 2f, dz + 1f), 4f);
            B(metal).Cuboid(centre + new Vector3(-4f, height - 2f, -depth * 0.5f), centre + new Vector3(4f, height, depth * 0.5f), 4f);
            B(aviation).Cuboid(centre + new Vector3(-0.4f, height + 2f, -0.4f), centre + new Vector3(0.4f, height + 2.6f, 0.4f), 1f);
        }

        private static void Straddle(Transform root, Vector3 p)
        {
            var r = new Rect(p.x - 3f, p.z - 5f, 6f, 10f);
            if (Blocked(r, 0.5f)) return;
            foreach (float dx in new[] { -2.6f, 2.6f })
            foreach (float dz in new[] { -4.2f, 4.2f })
            {
                B(craneSteel).Cuboid(p + new Vector3(dx - 0.3f, 0.6f, dz - 0.3f), p + new Vector3(dx + 0.3f, 12f, dz + 0.3f), 2f);
                B(rubber).Cylinder(p + new Vector3(dx - 0.35f, 0f, dz), 0.6f, 0.7f, 10, true);
            }
            B(craneSteel).Cuboid(p + new Vector3(-3f, 12f, -4.6f), p + new Vector3(3f, 13.2f, 4.6f), 2f);
            B(shipWindow).Cuboid(p + new Vector3(2f, 9f, 3.6f), p + new Vector3(3.1f, 11.2f, 4.6f), 1f);
            Collider(root, "Straddle", p + Vector3.up * 6.6f, new Vector3(6f, 13.2f, 9.2f), environment);
            solids.Add(r);
        }

        /// <summary>Corrugated industrial building: plinth, cladding, ribbon windows, parapet, roof vents; optional loading bays on the north face.</summary>
        private static void Building(Rect r, float height, Material wall, bool office = false, bool bays = false)
        {
            Vector3 min = new(r.xMin, 0f, r.yMin), max = new(r.xMax, height, r.yMax);
            B(concreteDark).Cuboid(min, new Vector3(max.x, 1.2f, max.z), 4f);
            B(wall).Cuboid(new Vector3(min.x + 0.05f, 1.2f, min.z + 0.05f), new Vector3(max.x - 0.05f, height, max.z - 0.05f), 4f);
            B(roof).Cuboid(new Vector3(min.x - 0.3f, height, min.z - 0.3f), new Vector3(max.x + 0.3f, height + 0.6f, max.z + 0.3f), 6f);
            float band = office ? height * 0.45f : height - 3.2f;
            B(indWindow).Cuboid(new Vector3(min.x - 0.02f, band, min.z - 0.02f), new Vector3(max.x + 0.02f, band + 1.4f, max.z + 0.02f), 8f);
            if (office) B(indWindow).Cuboid(new Vector3(min.x - 0.02f, band + 2.6f, min.z - 0.02f), new Vector3(max.x + 0.02f, band + 4f, max.z + 0.02f), 8f);
            for (float x = min.x + 6f; x < max.x - 4f; x += 14f)
            {
                B(metal).Cylinder(new Vector3(x, height + 0.6f, (min.z + max.z) * 0.5f), 0.8f, 1.6f, 10, true);
                B(metal).Cuboid(new Vector3(x + 4f, height + 0.6f, min.z + 3f), new Vector3(x + 7f, height + 2.2f, min.z + 6f), 2f);
            }
            // Downpipes and a wall ladder.
            for (float x = min.x + 1f; x < max.x; x += 20f) B(metal).Cylinder(new Vector3(x, 0f, max.z + 0.2f), 0.12f, height, 6, false);
            B(metal).Cuboid(new Vector3(max.x + 0.05f, 0.5f, min.z + 3f), new Vector3(max.x + 0.12f, height + 1f, min.z + 3.1f), 1f);
            B(metal).Cuboid(new Vector3(max.x + 0.05f, 0.5f, min.z + 3.6f), new Vector3(max.x + 0.12f, height + 1f, min.z + 3.7f), 1f);
            for (float y = 0.8f; y < height; y += 0.4f) B(metal).Cuboid(new Vector3(max.x + 0.05f, y, min.z + 3f), new Vector3(max.x + 0.1f, y + 0.05f, min.z + 3.7f), 1f);
            if (bays)
                for (float x = min.x + 6f; x < max.x - 8f; x += 11f)
                {
                    B(roller).Cuboid(new Vector3(x, 1.2f, max.z), new Vector3(x + 5f, 6f, max.z + 0.1f), 6f);
                    B(rubber).Cuboid(new Vector3(x - 0.4f, 1f, max.z), new Vector3(x, 1.6f, max.z + 0.4f), 1f);
                    B(rubber).Cuboid(new Vector3(x + 5f, 1f, max.z), new Vector3(x + 5.4f, 1.6f, max.z + 0.4f), 1f);
                    B(neonAmber).Cuboid(new Vector3(x + 1f, 6.4f, max.z), new Vector3(x + 4f, 6.55f, max.z + 0.15f), 6f);
                    // Wall-pack lamp over the door and its glow on the wet apron (no real light: the masts carry the budget).
                    B(lampHead).Cuboid(new Vector3(x + 2f, 7.1f, max.z), new Vector3(x + 3f, 7.45f, max.z + 0.35f), 1f);
                    B(pool).Decal(new Vector3(x + 2.5f, 0.055f, max.z + 4.5f), 12f, 10f);
                    B(hazardStripe).Strip(new Vector3(x, 0.045f, max.z + 1f), new Vector3(x + 5f, 0.045f, max.z + 1f), 0.4f);
                }
            Collider(null, "BuildingCollider", new Vector3(r.center.x, height * 0.5f, r.center.y), new Vector3(r.width, height, r.height), environment);
            solids.Add(r);
        }

        // ---------------- Restricted cargo area ----------------

        private static void BuildRestricted(Transform world, Random rng, Gates gates)
        {
            var root = new GameObject("RestrictedCargoArea").transform;
            root.SetParent(world, false);
            Begin();
            float x0 = Restricted.xMin, x1 = Restricted.xMax, z0 = Restricted.yMin, z1 = Restricted.yMax;
            Fence(root, new Vector3(x0, 0f, z0), new Vector3(212f, 0f, z0));
            Fence(root, new Vector3(238f, 0f, z0), new Vector3(x1, 0f, z0));
            Fence(root, new Vector3(x0, 0f, z1), new Vector3(212f, 0f, z1));
            Fence(root, new Vector3(238f, 0f, z1), new Vector3(x1, 0f, z1));
            Fence(root, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1));
            Fence(root, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1));
            // Red hazard floor marking along the inside of the fence.
            B(neonRed()).Strip(new Vector3(x0 + 2f, 0.045f, z0 + 2f), new Vector3(x1 - 2f, 0.045f, z0 + 2f), 0.25f);
            Flush("RestrictedFence", root, environment, false);
            SignPanel(root, "RESTRICTED AREA", new Color(1f, 0.3f, 0.25f), new Vector3(225f, 9.6f, z0), 180f, 12f);
            SignPanel(root, "NO ENTRY", new Color(1f, 0.3f, 0.25f), new Vector3(150f, 2.2f, z0 - 0.3f), 180f, 4f);
            SignPanel(root, "NO ENTRY", new Color(1f, 0.3f, 0.25f), new Vector3(300f, 2.2f, z0 - 0.3f), 180f, 4f);

            gates.Security = BuildGate(root, "SecurityGate", new Vector3(225f, 0f, z0), 0f, 26f);
            gates.North = BuildGate(root, "RestrictedNorthGate", new Vector3(225f, 0f, z1), 0f, 26f);

            // Stacks either side, then the three candidate bays beside the internal lane (one is KX-7).
            foreach (var b in Bays) solids.Add(new Rect(b.x - 2f, b.z - 7f, 4f, 14f));
            ReserveHeistSpace();   // the gantry crane's rails and the transfer pad (HarborRainBuilder.Heist)
            Begin();
            Yard(root, rng, x0 + 6f, 202f, z0 + 6f, z1 - 6f, 4);
            Yard(root, rng, 248f, x1 - 6f, z0 + 6f, z1 - 6f, 4);
            Flush("RestrictedCargo", root, environment);   // the three bay containers are separate, liftable objects (BuildStations)
            for (int i = 0; i < Bays.Length; i++)
            {
                SignPanel(root, i == 0 ? "R-1" : i == 1 ? "R-2" : "R-3", Color.white, Bays[i] + new Vector3(0f, 4.4f, 0f), Bays[i].x < 225f ? 90f : 270f, 2.4f);
            }
        }

        private static Material neonRed() => AssetDatabase.LoadAssetAtPath<Material>(District + "District_NeonRed.mat");

        private static Renderer[] BuildBayGlows(Transform world)
        {
            var root = new GameObject("ShipmentGlows").transform;
            root.SetParent(world, false);
            var result = new Renderer[Bays.Length];
            for (int i = 0; i < Bays.Length; i++)
            {
                Begin();
                var s = Bays[i];
                foreach (float y in new[] { 0.15f, 2.55f })
                foreach (float x in new[] { -1.25f, 1.25f })
                    B(shipGlow).Cuboid(s + new Vector3(x - 0.06f, y - 0.06f, -6.15f), s + new Vector3(x + 0.06f, y + 0.06f, 6.15f), 1f);
                Flush($"Bay{i}", root, detail, false);
                result[i] = root.Find($"Bay{i}").GetComponentInChildren<Renderer>();
            }
            return result;
        }

        // ---------------- Industrial processing zone ----------------

        private static void BuildIndustrial(Transform world, Random rng)
        {
            var root = new GameObject("IndustrialZone").transform;
            root.SetParent(world, false);
            Begin();
            // Tank farm with ladders, railings and pipe runs.
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(-600f + (i % 3) * 64f, 0f, -270f + (i / 3) * 52f);
                float h = 16f + (i % 2) * 4f;
                B(metal).Cylinder(p, 12f, h, 28, true, 8f);
                B(hazardStripe).Cylinder(p + Vector3.up * (h - 1.2f), 12.05f, 0.6f, 28, false, 24f);
                for (float y = 0.5f; y < h; y += 0.45f) B(metal).Cuboid(p + new Vector3(12.05f, y, -0.4f), p + new Vector3(12.15f, y + 0.05f, 0.4f), 1f);
                B(metal).Cuboid(p + new Vector3(12.05f, 0.5f, -0.45f), p + new Vector3(12.2f, h + 1f, -0.4f), 1f);
                B(metal).Cuboid(p + new Vector3(12.05f, 0.5f, 0.4f), p + new Vector3(12.2f, h + 1f, 0.45f), 1f);
                B(metal).Torus(p + Vector3.up * (h + 1f), 11.8f, 0.05f, 28, 4);
                Collider(root, "Tank", p + Vector3.up * h * 0.5f, new Vector3(21f, h, 21f), environment, 22.5f);
                solids.Add(new Rect(p.x - 12.5f, p.z - 12.5f, 25f, 25f));
            }
            for (int k = 0; k < 3; k++)
                B(metal).OrientedBox(new Vector3(-500f, 3f + k * 0.9f, -197f), new Vector3(0.7f, 0.7f, 220f), Quaternion.Euler(0f, 90f, 90f), 4f);
            for (float x = -600f; x <= -400f; x += 25f) B(metal).Cuboid(new Vector3(x - 0.2f, 0f, -197.4f), new Vector3(x + 0.2f, 5.5f, -196.6f), 2f);

            // Warehouses with loading bays facing the roads.
            Building(new Rect(-352f, -282f, 200f, 78f), 16f, cladding, bays: true);
            Building(new Rect(-234f, -166f, 200f, 34f), 12f, claddingDark, bays: false);
            Building(new Rect(-620f, -108f, 330f, 42f), 14f, cladding, bays: true);
            Building(new Rect(-130f, -280f, 110f, 70f), 18f, claddingDark, bays: true);

            // Substation (fenced) and generators with exhaust stacks.
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(-610f + i * 12f, 0f, -160f);
                B(concreteDark).Cuboid(p, p + new Vector3(8f, 3.2f, 5f), 2f);
                B(metal).Cylinder(p + new Vector3(6.5f, 3.2f, 2.5f), 0.4f, 4f, 8, false);
                B(neonAmber).Cuboid(p + new Vector3(0.5f, 2.6f, 5f), p + new Vector3(2f, 2.8f, 5.05f), 1f);
            }
            Collider(root, "Generators", new Vector3(-576f, 1.6f, -157.5f), new Vector3(72f, 3.2f, 5f), environment);
            solids.Add(new Rect(-612f, -162f, 74f, 9f));
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(-500f + i * 18f, 0f, -150f);
                B(metal).Cuboid(p, p + new Vector3(6f, 5f, 6f), 2f);
                for (int f = 0; f < 6; f++) B(metal).Cuboid(p + new Vector3(-0.3f, 0.6f + f * 0.7f, 0.5f), p + new Vector3(0f, 0.9f + f * 0.7f, 5.5f), 1f);
                B(rubber).Cylinder(p + new Vector3(3f, 5f, 3f), 0.3f, 3f, 6, true);
            }
            Flush("IndustrialMeshes", root, environment);
            Begin();
            Fence(root, new Vector3(-508f, 0f, -156f), new Vector3(-420f, 0f, -156f));
            Fence(root, new Vector3(-508f, 0f, -138f), new Vector3(-420f, 0f, -138f));
            Fence(root, new Vector3(-508f, 0f, -156f), new Vector3(-508f, 0f, -138f));
            Fence(root, new Vector3(-420f, 0f, -156f), new Vector3(-420f, 0f, -138f));
            Flush("SubstationFence", root, environment, false);
            SignPanel(root, "PROCESSING", new Color(1f, 0.7f, 0.2f), new Vector3(-252f, 10f, -203.6f), 0f, 12f);
            SignPanel(root, "DANGER HV", new Color(1f, 0.3f, 0.25f), new Vector3(-464f, 2f, -156.4f), 180f, 4f);
        }

        // ---------------- Freight yard ----------------

        private static void BuildFreightYard(Transform world, Random rng)
        {
            var root = new GameObject("FreightYard").transform;
            root.SetParent(world, false);
            Begin();
            // Rail sidings: two tracks with sleepers and flat wagons carrying containers.
            foreach (float z in new[] { -272f, -260f })
            {
                foreach (float o in new[] { -0.72f, 0.72f })
                    B(rail).Cuboid(new Vector3(20f, 0.05f, z + o - 0.05f), new Vector3(620f, 0.2f, z + o + 0.05f), 4f);
                for (float x = 21f; x < 620f; x += 1.2f) B(sleeper).Cuboid(new Vector3(x, 0.02f, z - 1.3f), new Vector3(x + 0.3f, 0.09f, z + 1.3f), 1f);
                for (float x = 60f; x < 560f; x += 17f)
                {
                    if (x > 360f && x < 400f) continue;   // the connector road crosses here
                    B(metal).Cuboid(new Vector3(x, 0.8f, z - 1.4f), new Vector3(x + 15.5f, 1.3f, z + 1.4f), 2f);
                    foreach (float wx in new[] { 1.5f, 3.3f, 12.2f, 14f }) B(rubber).Cylinder(new Vector3(x + wx, 0.2f, z - 1.45f), 0.45f, 2.9f, 8, false);
                    if (rng.NextDouble() < 0.8)
                        StackBox(containers[rng.Next(containers.Length)], new Vector3(x + 7.75f, 2.6f, z), new Vector3(12.2f, 2.56f, 2.44f), Quaternion.identity);
                }
            }
            foreach (var (a, b) in new[] { (20f, 365f), (395f, 620f) })
            {
                Collider(root, "RailCars", new Vector3((a + b) * 0.5f, 2f, -266f), new Vector3(b - a, 4f, 16f), environment);
                solids.Add(new Rect(a, -275f, b - a, 18f));
            }
            // Level crossing on the connector: hazard boards and a sign.
            B(hazardStripe).Strip(new Vector3(374f, 0.05f, -278f), new Vector3(386f, 0.05f, -278f), 0.6f);
            B(hazardStripe).Strip(new Vector3(374f, 0.05f, -254f), new Vector3(386f, 0.05f, -254f), 0.6f);

            // Truck staging: tractors with container trailers in marked bays.
            for (int i = 0; i < 14; i++)
            {
                float x = 160f + i * 16f;
                if (x > 360f && x < 400f) continue;
                var p = new Vector3(x, 0f, -214f);
                B(marking).Strip(new Vector3(x - 7f, 0.045f, -230f), new Vector3(x - 7f, 0.045f, -194f), 0.15f);
                if (rng.NextDouble() < 0.25) continue;
                B(shipWhite).Cuboid(p + new Vector3(-1.3f, 0.6f, 9f), p + new Vector3(1.3f, 3.8f, 13f), 2f);
                B(shipWindow).Cuboid(p + new Vector3(-1.2f, 2.6f, 13f), p + new Vector3(1.2f, 3.4f, 13.05f), 1f);
                B(metal).Cuboid(p + new Vector3(-1.2f, 0.9f, -4f), p + new Vector3(1.2f, 1.3f, 9f), 2f);
                StackBox(containers[rng.Next(containers.Length)], p + new Vector3(0f, 2.6f, 2.5f), new Vector3(2.44f, 2.56f, 12.2f), Quaternion.identity);
                foreach (float wz in new[] { -2.5f, -1f, 10.5f, 12.2f })
                foreach (float wx in new[] { -1.3f, 1.3f })
                    B(rubber).Cylinder(p + new Vector3(wx, 0.5f, wz), 0.5f, 0.0f, 8, false);
                Collider(root, "Truck", p + new Vector3(0f, 2f, 4.5f), new Vector3(2.6f, 4f, 17f), environment);
                solids.Add(new Rect(x - 1.5f, -218.5f, 3f, 18f));
            }
            // Depot and maintenance shed, reach stackers, parking.
            Building(new Rect(480f, -160f, 140f, 70f), 13f, claddingDark, bays: true);
            Building(new Rect(150f, -160f, 60f, 30f), 8f, cladding, office: true);
            Flush("FreightMeshes", root, environment);
            SignPanel(root, "FREIGHT YARD", new Color(1f, 0.7f, 0.2f), new Vector3(550f, 10f, -89.6f), 0f, 12f);

            // Parked cars: the project's licensed pack-car models, static here (their traffic scripts removed).
            var packs = AssetDatabase.FindAssets("PackCar_ t:Prefab", new[] { "Assets/_NeonRift/Prefabs/Traffic" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g => g != null).ToArray();
            var parking = new GameObject("Parking").transform;
            parking.SetParent(root, false);
            Begin();
            int parked = 0;
            for (int row = 0; row < 2; row++)
            for (int i = 0; i < 22; i++)
            {
                float x = 236f + i * 3.2f, z = row == 0 ? -128f : -108f;
                B(marking).Strip(new Vector3(x - 1.6f, 0.045f, z - 2.8f), new Vector3(x - 1.6f, 0.045f, z + 2.8f), 0.12f);
                if (packs.Length == 0 || rng.NextDouble() < 0.4) continue;
                var car = (GameObject)PrefabUtility.InstantiatePrefab(packs[rng.Next(packs.Length)], parking);
                car.transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, row == 0 ? 0f : 180f, 0f));
                StripScripts(car);
                foreach (var rb in car.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                GameObjectUtility.SetStaticEditorFlags(car, StaticEditorFlags.BatchingStatic);
                parked++;
            }
            Flush("ParkingMarks", parking, detail, false);
            Collider(root, "ParkedRowA", new Vector3(270f, 0.8f, -128f), new Vector3(72f, 1.6f, 4.6f), environment);
            Collider(root, "ParkedRowB", new Vector3(270f, 0.8f, -108f), new Vector3(72f, 1.6f, 4.6f), environment);
            solids.Add(new Rect(234f, -131f, 72f, 26f));
            report.AppendLine($"freight yard: {parked} parked pack cars");
        }

        // ---------------- Ship berth ----------------

        private static void BuildBerth(Transform world, Random rng)
        {
            var root = new GameObject("CargoBerth").transform;
            root.SetParent(world, false);
            Begin();
            // The vessel: 320 m hull (red below the boot-top, black above), raked bow, stern superstructure,
            // containers on deck, a funnel and masts.
            float x0 = -480f, x1 = -160f, z0 = 308f, z1 = 352f, zc = (z0 + z1) * 0.5f;
            if (!shipModel)
            {
            B(hullRed).Cuboid(new Vector3(x0, -8f, z0), new Vector3(x1, -1.5f, z1), 6f);
            B(hullBlack).Cuboid(new Vector3(x0, -1.5f, z0), new Vector3(x1, 14f, z1), 6f);
            var bow = new Vector3(x1 + 34f, 14f, zc);
            B(hullBlack).Tri(new Vector3(x1, 14f, z0), new Vector3(x1, 14f, z1), bow, Vector2.zero, Vector2.one, Vector2.right);
            B(hullBlack).Quad(new Vector3(x1, -8f, z0), new Vector3(x1, 14f, z0), bow, new Vector3(x1 + 18f, -8f, zc), Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            B(hullBlack).Quad(new Vector3(x1 + 18f, -8f, zc), bow, new Vector3(x1, 14f, z1), new Vector3(x1, -8f, z1), Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            B(shipWhite).Cuboid(new Vector3(x0 + 6f, 14f, z0 + 3f), new Vector3(x0 + 30f, 46f, z1 - 3f), 4f);
            for (float y = 18f; y < 45f; y += 3.2f) B(shipWindow).Cuboid(new Vector3(x0 + 29.95f, y, z0 + 5f), new Vector3(x0 + 30.05f, y + 1f, z1 - 5f), 4f);
            B(shipWhite).Cuboid(new Vector3(x0 + 26f, 42f, z0 - 2f), new Vector3(x0 + 32f, 45f, z1 + 2f), 4f);   // bridge wings
            B(hullRed).Cuboid(new Vector3(x0 + 10f, 46f, zc - 4f), new Vector3(x0 + 18f, 58f, zc + 4f), 4f);       // funnel
            B(metal).Cylinder(new Vector3(x0 + 20f, 46f, zc), 0.4f, 16f, 8, true);
            B(aviation).Cuboid(new Vector3(x0 + 19.6f, 62f, zc - 0.4f), new Vector3(x0 + 20.4f, 62.8f, zc + 0.4f), 1f);
            for (float x = x0 + 36f; x < x1 - 14f; x += 13f)
            for (float z = z0 + 2f; z < z1 - 3f; z += 2.7f)
            {
                int tiers = 2 + rng.Next(5);
                for (int k = 0; k < tiers; k++)
                    StackBox(containers[rng.Next(containers.Length)], new Vector3(x + 6.1f, 14f + 2.6f * (k + 0.5f), z + 1.22f), new Vector3(12.2f, 2.56f, 2.44f), Quaternion.identity);
            }
            }
            // Mooring lines from bollards to the hull.
            foreach (float x in new[] { x0 + 20f, x0 + 60f, x1 - 40f, x1 - 5f })
            {
                Vector3 a = new(x, 0.6f, QuayEdge - 1.6f), b = new(x + 8f, 12f, z0);
                B(rubber).OrientedBox((a + b) * 0.5f, new Vector3(0.12f, 0.12f, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a), 1f);
            }
            Flush("Vessel", root, environment);

            // Ship-to-shore cranes along the apron, booms out over the ship and the water.
            Begin();
            craneLights.Clear();
            foreach (float x in new[] { -440f, -350f, -260f, -170f, 60f }) StsCrane(root, x);
            // Apron cargo: hatch covers, lashing cages, a reach stacker.
            for (int i = 0; i < 10; i++)
            {
                var p = new Vector3(-470f + i * 30f, 0f, 268f);
                B(hullBlack).Cuboid(p, p + new Vector3(12f, 1.2f, 14f), 4f);
                Collider(root, "HatchCover", p + new Vector3(6f, 0.6f, 7f), new Vector3(12f, 1.2f, 14f), environment);
                solids.Add(new Rect(p.x, p.z, 12f, 14f));
            }
            Flush("BerthCranes", root, environment);
            // Flood barriers along the low quayside east of the berth (gaps where the roads meet the apron).
            Begin();
            for (float x = 300f; x < 620f; x += 14f)
            {
                if (OnRoad(new Vector3(x, 0f, 246f), 4f)) continue;
                B(concreteDark).Cuboid(new Vector3(x, 0f, 245f), new Vector3(x + 11f, 1.1f, 246.2f), 2f);
                B(hazardStripe).Cuboid(new Vector3(x, 1.1f, 245f), new Vector3(x + 11f, 1.35f, 246.2f), 1f);
            }
            Flush("FloodBarriers", root, environment, false);
            Collider(root, "FloodBarrierLine", new Vector3(460f, 0.7f, 245.6f), new Vector3(320f, 1.4f, 1.2f), environment);
            SignPanel(root, "FLOOD RISK", new Color(1f, 0.7f, 0.2f), new Vector3(320f, 3.4f, 244f), 180f, 5f, true);
            SignPanel(root, "LOW DOCK ROAD", new Color(1f, 0.7f, 0.2f), new Vector3(300f, 3.4f, 216f), 180f, 6f, true);
        }

        // ---------------- Backdrop ----------------

        private static void BuildBackdrop(Transform world, Random rng)
        {
            var root = new GameObject("Backdrop").transform;
            root.SetParent(world, false);
            Begin();
            // Distant ships at anchor and the far shore's cranes: low-detail silhouettes with aviation lights.
            foreach (var (x, z, len) in new[] { (200f, 1150f, 260f), (-200f, 1500f, 300f), (1300f, 1250f, 220f) })
            {
                B(hullBlack).Cuboid(new Vector3(x, -6f, z), new Vector3(x + len, 12f, z + 34f), 8f);
                B(shipWhite).Cuboid(new Vector3(x + 6f, 12f, z + 4f), new Vector3(x + 26f, 36f, z + 30f), 8f);
                B(shipWindow).Cuboid(new Vector3(x + 25.9f, 28f, z + 6f), new Vector3(x + 26.1f, 30f, z + 28f), 4f);
                B(aviation).Cuboid(new Vector3(x + 15f, 40f, z + 16f), new Vector3(x + 16.5f, 41.5f, z + 17.5f), 1f);
                B(aviation).Cuboid(new Vector3(x + len - 2f, 14f, z + 16f), new Vector3(x + len - 0.5f, 15.5f, z + 17.5f), 1f);
            }
            for (int i = 0; i < 9; i++)
            {
                float x = -900f + i * 230f, z = 1900f;
                B(hullBlack).Cuboid(new Vector3(x - 8f, 0f, z), new Vector3(x + 8f, 70f, z + 4f), 8f);
                B(hullBlack).Cuboid(new Vector3(x - 8f, 70f, z - 60f), new Vector3(x + 8f, 76f, z + 120f), 8f);
                B(aviation).Cuboid(new Vector3(x - 1f, 77f, z), new Vector3(x + 1f, 79f, z + 2f), 1f);
            }
            Flush("FarHarbour", root, LayerMask.NameToLayer("Default"), false);
            // The city across the bay: the project's building prefabs, far out and fogged into silhouettes.
            var buildings = AssetDatabase.FindAssets("PF_Bld_ t:Prefab", new[] { "Assets/_NeonRift/Prefabs/Buildings" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g => g != null).ToArray();
            for (int i = 0; i < 12 && buildings.Length > 0; i++)
            {
                var b = (GameObject)PrefabUtility.InstantiatePrefab(buildings[i % buildings.Length], root);
                b.transform.SetPositionAndRotation(new Vector3(-1200f + i * 220f + rng.Next(-60, 60), 0f, 2300f + rng.Next(0, 300)), Quaternion.Euler(0f, rng.Next(0, 4) * 90f, 0f));
                foreach (var c in b.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }
        }

        // ---------------- Security: cameras and drones ----------------

        private sealed class Security { public List<HarborSecurityCamera> Cameras = new(); public List<HarborDrone> Drones = new(); }

        private static Security BuildSecurity(Transform world)
        {
            var root = new GameObject("Security").transform;
            root.SetParent(world, false);
            var result = new Security();
            var bodyMesh = CubeMesh("HR_CameraBody", new Vector3(0.35f, 0.35f, 0.9f));
            var ledMesh = CubeMesh("HR_CameraLed", new Vector3(0.12f, 0.12f, 0.05f));
            void Camera(Vector3 p, float yaw, bool restricted)
            {
                var go = new GameObject("SecurityCamera");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                Begin();
                B(metal).Cylinder(p, 0.1f, 6f, 6, true);
                Flush("Pole", go.transform, detail, false);
                var head = new GameObject("Head").transform;
                head.SetParent(go.transform, false);
                head.SetPositionAndRotation(p + Vector3.up * 6.1f, Quaternion.Euler(28f, yaw, 0f));
                Single("Body", head, bodyMesh, camBody, detail, Vector3.zero).transform.localPosition = new Vector3(0f, 0f, 0.2f);
                var led = Single("Led", head, ledMesh, cameraLed, detail, Vector3.zero);
                led.transform.localPosition = new Vector3(0f, 0f, 0.68f);
                led.transform.localRotation = Quaternion.identity;
                var cam = go.AddComponent<HarborSecurityCamera>();
                Configure(cam, so =>
                {
                    so.FindProperty("head").objectReferenceValue = head;
                    so.FindProperty("led").objectReferenceValue = led.GetComponent<Renderer>();
                    so.FindProperty("restricted").boolValue = restricted;
                    so.FindProperty("blocking").intValue = 1 << environment;
                });
                result.Cameras.Add(cam);
            }
            float x0 = Restricted.xMin, x1 = Restricted.xMax, z0 = Restricted.yMin, z1 = Restricted.yMax;
            Camera(new Vector3(x0 + 1f, 0f, z0 + 1f), 45f, true);
            Camera(new Vector3(x1 - 1f, 0f, z0 + 1f), -45f, true);
            Camera(new Vector3(x0 + 1f, 0f, z1 - 1f), 135f, true);
            Camera(new Vector3(x1 - 1f, 0f, z1 - 1f), -135f, true);
            Camera(new Vector3(207f, 0f, 145f), 90f, true);
            Camera(new Vector3(243f, 0f, 180f), -90f, true);
            Camera(new Vector3(204f, 0f, 92f), 180f, true);
            Camera(new Vector3(-14f, 0f, -336f), 160f, false);
            Camera(new Vector3(527f, 0f, 92f), -90f, false);
            Camera(new Vector3(652f, 0f, 214f), -60f, false);
            Camera(new Vector3(56f, 0f, -40f), 200f, false);

            // Drones: the project's licensed Buster drone model with a searchlight; patrols over the restricted area,
            // the berth and the freight yard.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeonRift/Prefabs/Drones/PF_Drone_Buster.prefab");
            foreach (var (centre, radius, height) in new[] { (new Vector3(225f, 0f, 156f), 48f, 24f), (new Vector3(-300f, 0f, 262f), 90f, 30f), (new Vector3(380f, 0f, -200f), 80f, 26f) })
            {
                var go = new GameObject("SecurityDrone");
                go.transform.SetParent(root, false);
                go.transform.position = centre + new Vector3(radius, height, 0f);
                if (model != null)
                {
                    var m = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
                    m.transform.localPosition = Vector3.zero;
                    foreach (var c in m.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                }
                var lightGo = new GameObject("Searchlight");
                lightGo.transform.SetParent(go.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.6f, 0f);
                lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.intensity = 4000f;
                l.range = 60f;
                l.spotAngle = 32f;
                l.innerSpotAngle = 14f;
                l.shadows = LightShadows.None;
                var drone = go.AddComponent<HarborDrone>();
                Configure(drone, so =>
                {
                    so.FindProperty("searchlight").objectReferenceValue = l;
                    so.FindProperty("patrolCentre").vector3Value = centre;
                    so.FindProperty("patrolRadius").floatValue = radius;
                    so.FindProperty("patrolHeight").floatValue = height;
                });
                result.Drones.Add(drone);
            }
            report.AppendLine($"security: {result.Cameras.Count} cameras, {result.Drones.Count} drones");
            return result;
        }

        // ---------------- Lighting ----------------

        private static LightBudget BuildLighting(Transform world, List<Renderer> securityLamps, List<Light> alarmLights)
        {
            var root = new GameObject("Lighting").transform;
            root.SetParent(world, false);
            // Floodlight masts every ~85 m along every road, on alternating sides, clear of junctions and buildings.
            var masts = new List<Vector3>();
            for (int i = 0; i < Roads.Length; i++)
            {
                var (min, max) = Roads[i];
                bool alongZ = max.y - min.y > max.x - min.x;
                float len = alongZ ? max.y - min.y : max.x - min.x;
                for (float s = 30f, k = 0; s < len - 20f; s += 85f, k++)
                {
                    float side = k % 2 == 0 ? 1f : -1f;
                    Vector3 p = alongZ ? new Vector3(side > 0 ? max.x + 3.5f : min.x - 3.5f, 0f, min.y + s)
                                       : new Vector3(min.x + s, 0f, side > 0 ? max.y + 3.5f : min.y - 3.5f);
                    var foot = new Rect(p.x - 1f, p.z - 1f, 2f, 2f);
                    if (OnRoad(p, 1.5f) || solids.Any(r => r.Overlaps(foot)) || masts.Any(m => (m - p).sqrMagnitude < 40f * 40f)) continue;
                    masts.Add(p);
                    solids.Add(foot);
                }
            }
            var lights = new List<Light>();
            Begin();
            foreach (var m in masts)
            {
                B(metal).Cylinder(m, 0.35f, 24f, 8, true);
                B(lampHead).Cuboid(m + new Vector3(-1.8f, 23.6f, -0.6f), m + new Vector3(1.8f, 24.4f, 0.6f), 1f);
                B(pool).Decal(m + Vector3.up * 0.06f, 40f, 40f);
                B(cone).Cone(m + Vector3.up * 23.5f, 7f, 23.4f, 16, 0.8f);
                Collider(root, "MastCollider", m + Vector3.up * 3f, new Vector3(0.8f, 6f, 0.8f), environment);
                var go = new GameObject("MastLight") { layer = detail };
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(m + Vector3.up * 23f, Quaternion.Euler(90f, 0f, 0f));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(1f, 0.72f, 0.42f);
                // 2.2: wider, longer throw so neighbouring masts (85 m apart) overlap on the road — no black gaps between them.
                l.intensity = 11500f;
                l.range = 78f;
                l.spotAngle = 135f;
                l.innerSpotAngle = 70f;
                l.shadows = LightShadows.None;
                l.enabled = false;   // LightBudget switches on the nearest few
                lights.Add(l);
            }
            Flush("Masts", root, detail, false);
            var budget = root.gameObject.AddComponent<LightBudget>();
            lights.AddRange(craneLights);   // crane downlights share the budget with the masts
            lights.AddRange(extraBudgetLights);   // light towers on the gate approach
            budget.EditorConfigure(lights.ToArray(), null, 24);
            Configure(budget, so =>
            {
                so.FindProperty("maxDistance").floatValue = 190f;
                so.FindProperty("maxShadowed").intValue = 1;
            });
            report.AppendLine($"lighting: {masts.Count} masts with emissive heads, pools and rain cones; real-time spots budgeted to the nearest 22 (1 shadowed)");

            var lampMesh = CubeMesh("HR_SecLampBox", new Vector3(0.6f, 0.3f, 0.6f));
            var lampPositions = new List<Vector3>
            {
                new(-14f, 3f, -332f), new(14f, 3f, -332f), new(210f, 3f, 94f), new(240f, 3f, 94f), new(210f, 3f, 218f), new(240f, 3f, 218f),
                new(112f, 3f, 156f), new(338f, 3f, 156f), new(658f, 3f, 218f), new(658f, 3f, 242f), new(512f, 3f, 92f), new(528f, 3f, 68f)
            };
            Begin();
            foreach (var p in lampPositions) B(metal).Cylinder(new Vector3(p.x, 0f, p.z), 0.08f, p.y - 0.15f, 6, true);
            Flush("SecurityPoles", root, detail, false);
            foreach (var p in lampPositions) securityLamps.Add(Single("SecurityLamp", root, lampMesh, secLamp, detail, p).GetComponent<Renderer>());
            foreach (var p in new[] { new Vector3(0f, 6f, -326f), new Vector3(225f, 6f, 102f), new Vector3(225f, 6f, 210f), new Vector3(654f, 6f, 230f), new Vector3(520f, 6f, 92f) })
            {
                var go = new GameObject("AlarmLight") { layer = detail };
                go.transform.SetParent(root, false);
                go.transform.position = p;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.12f, 0.08f);
                l.range = 34f;
                l.intensity = 8f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                alarmLights.Add(l);
            }
            return budget;
        }

        // ---------------- Wet details: puddles, drains, runoff ----------------

        private static void BuildWetDetails(Transform world, Random rng)
        {
            Begin();
            int n = 0, drains = 0;
            foreach (var (min, max) in Roads)
            {
                bool alongZ = max.y - min.y > max.x - min.x;
                for (int i = 0; i < Mathf.CeilToInt((max - min).magnitude / 18f); i++)
                {
                    var c = new Vector3(Mathf.Lerp(min.x + 2f, max.x - 2f, (float)rng.NextDouble()), 0.035f + n % 5 * 0.0008f,
                                        Mathf.Lerp(min.y + 2f, max.y - 2f, (float)rng.NextDouble()));
                    float rx = 1.2f + (float)rng.NextDouble() * 3.5f, rz = 0.8f + (float)rng.NextDouble() * 2.2f;
                    const int seg = 12;
                    for (int s = 0; s < seg; s++)
                    {
                        float a0 = s / (float)seg * Mathf.PI * 2f, a1 = (s - 1) / (float)seg * Mathf.PI * 2f;
                        float j0 = 0.8f + 0.2f * Mathf.Sin(a0 * 3f + n), j1 = 0.8f + 0.2f * Mathf.Sin(a1 * 3f + n);
                        B(puddle).Tri(c, c + new Vector3(Mathf.Cos(a0) * rx * j0, 0f, Mathf.Sin(a0) * rz * j0),
                                      c + new Vector3(Mathf.Cos(a1) * rx * j1, 0f, Mathf.Sin(a1) * rz * j1), Vector2.zero, Vector2.zero, Vector2.zero);
                    }
                    n++;
                }
                // Kerb drains with a strip of runoff water running into each.
                float len = alongZ ? max.y - min.y : max.x - min.x;
                for (float s = 12f; s < len - 6f; s += 32f)
                {
                    Vector3 p = alongZ ? new Vector3(max.x - 0.5f, 0.045f, min.y + s) : new Vector3(min.x + s, 0.045f, max.y - 0.5f);
                    if (NearJunction(System.Array.IndexOf(Roads, (min, max)), p)) continue;
                    var along = alongZ ? Vector3.forward : Vector3.right;
                    B(drain).Strip(p - along * 0.5f, p + along * 0.5f, 0.6f);
                    B(flood).Strip(p - along * 8f + Vector3.down * 0.003f, p, 0.5f);
                    drains++;
                }
            }
            Flush("WetGround", world, detail, false);
            report.AppendLine($"wet ground: {n} puddles, {drains} drains with runoff");
        }

        // ---------------- Flooding ----------------

        private sealed class Floods { public List<FloodZone> Deep = new(); public List<FloodZone> Shallow = new(); }

        private static Floods BuildFloods(Transform world, AudioClip rush)
        {
            var root = new GameObject("FloodZones").transform;
            root.SetParent(world, false);
            var f = new Floods();
            f.Deep.Add(Flood(root, "Flood_QuaysideWest", new Vector3(400f, 0f, 230f), new Vector2(140f, 18f), "QUAYSIDE WEST", true, rush));
            f.Deep.Add(Flood(root, "Flood_QuaysideEast", new Vector3(560f, 0f, 230f), new Vector2(100f, 18f), "QUAYSIDE EAST", true, rush));
            f.Shallow.Add(Flood(root, "Flood_EastRoad", new Vector3(641f, 0f, 165f), new Vector2(16f, 70f), "EAST ROAD", false, rush));
            f.Shallow.Add(Flood(root, "Flood_Lane80East", new Vector3(585f, 0f, 80f), new Vector2(80f, 14f), "LANE 80 EAST", false, rush));
            f.Shallow.Add(Flood(root, "Flood_CrossRoadWest", new Vector3(-420f, 0f, -180f), new Vector2(120f, 16f), "PROCESSING ROAD", false, rush));
            f.Shallow.Add(Flood(root, "Flood_HeadlandSpray", new Vector3(SprayZone.center.x, 0f, SprayZone.center.y), new Vector2(18f, SprayZone.height), "HEADLAND SPRAY", false, rush));
            return f;
        }

        private static FloodZone Flood(Transform root, string name, Vector3 centre, Vector2 size, string label, bool canClose, AudioClip rushClip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = centre;
            var waterMesh = new MeshBuilder();
            waterMesh.Ground(new Vector3(-size.x * 0.5f - 2f, 0f, -size.y * 0.5f - 2f), new Vector3(size.x * 0.5f + 2f, 0f, size.y * 0.5f + 2f), 6f);
            var water = Single("Water", go.transform, SaveMesh(waterMesh.ToMesh($"HR_{name}_Water"), $"HR_{name}_Water"), flood, LayerMask.NameToLayer("Water"), centre);
            var surface = Collider(go.transform, "WetSurface", centre + new Vector3(0f, -0.47f, 0f), new Vector3(size.x, 1f, size.y), drivable);
            surface.gameObject.AddComponent<DrivingSurface>().EditorConfigure(0.5f, 9f);

            var beacons = new List<Renderer>();
            var beaconMesh = CubeMesh("HR_BeaconBox", new Vector3(0.5f, 0.5f, 0.5f));
            bool alongX = size.x > size.y;
            Begin();
            foreach (int end in new[] { -1, 1 })
            foreach (int side in new[] { -1, 1 })
            {
                var p = centre + (alongX ? new Vector3(end * (size.x * 0.5f + 3f), 0f, side * (size.y * 0.5f + 1.5f))
                                         : new Vector3(side * (size.x * 0.5f + 1.5f), 0f, end * (size.y * 0.5f + 3f)));
                B(metal).Cylinder(p, 0.07f, 2.6f, 6, true);
                B(hazardStripe).OrientedBox(p + Vector3.up * 1.8f, new Vector3(1f, 0.7f, 0.05f), Quaternion.Euler(0f, alongX ? 90f : 0f, 0f), 1f);
                beacons.Add(Single("Beacon", go.transform, beaconMesh, beacon, detail, p + Vector3.up * 2.9f).GetComponent<Renderer>());
            }
            Flush("WarningPoles", go.transform, detail, false);

            GameObject closure = null;
            if (canClose)
            {
                closure = new GameObject("Closure");
                closure.transform.SetParent(go.transform, false);
                Begin();
                foreach (int end in new[] { -1, 1 })
                {
                    var p = centre + new Vector3(end * (size.x * 0.5f + 1f), 0f, 0f);
                    for (float z = -size.y * 0.5f; z < size.y * 0.5f; z += 3.4f) Jersey(p + new Vector3(0f, 0f, z + 1.7f), 90f);
                    Collider(closure.transform, "ClosureBlocker", p + Vector3.up * 0.8f, new Vector3(0.8f, 1.6f, size.y), environment);
                }
                Flush("Barricades", closure.transform, environment, false);
            }

            var rushGo = new GameObject("WaterRush");
            rushGo.transform.SetParent(go.transform, false);
            rushGo.transform.position = centre;
            var rush = rushGo.AddComponent<AudioSource>();
            rush.clip = rushClip;
            rush.loop = true;
            rush.playOnAwake = false;
            rush.pitch = 0.55f;
            rush.volume = 0f;
            rush.spatialBlend = 1f;
            rush.rolloffMode = AudioRolloffMode.Linear;
            rush.minDistance = 8f;
            rush.maxDistance = Mathf.Max(size.x, size.y) + 60f;

            var zone = go.AddComponent<FloodZone>();
            Configure(zone, so =>
            {
                so.FindProperty("label").stringValue = label;
                so.FindProperty("size").vector2Value = size;
                so.FindProperty("water").objectReferenceValue = water.transform;
                so.FindProperty("waterRenderer").objectReferenceValue = water.GetComponent<Renderer>();
                so.FindProperty("wetSurface").objectReferenceValue = surface;
                so.FindProperty("closure").objectReferenceValue = closure;
                so.FindProperty("rush").objectReferenceValue = rush;
                Array(so.FindProperty("beacons"), beacons.ToArray());
            });
            if (canClose) SignPanel(go.transform, "FLOOD RISK", new Color(1f, 0.7f, 0.2f), centre + new Vector3(-size.x * 0.5f - 6f, 3.2f, -size.y * 0.5f - 2f), 270f, 4f, true);
            return zone;
        }

        private static void BuildIndustrialAmbience(Transform world)
        {
            var hum = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_NeonRift/Audio/Generated/Mission/Heist_FacilityHum_Loop.wav");
            foreach (var p in new[] { new Vector3(-464f, 3f, -147f), new Vector3(-540f, 4f, -240f), new Vector3(560f, 4f, -125f), new Vector3(46f, 3f, -24f) })
            {
                var go = new GameObject("IndustrialHum");
                go.transform.SetParent(world, false);
                go.transform.position = p;
                var s = go.AddComponent<AudioSource>();
                s.clip = hum;
                s.loop = true;
                s.playOnAwake = true;
                s.volume = 0.35f;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 6f;
                s.maxDistance = 70f;
            }
        }

        // ---------------- Rig: entry, camera, HUD, mission ----------------

        private static void BuildRig(Gates gates, Floods floods, StormController storm, List<Renderer> lamps, List<Light> alarms, Renderer[] _,
                                     Security security, LightBudget budget, (AudioClip rain, AudioClip[] thunder, AudioMixerConfigRef mixer) audio,
                                     Coast coast, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup sfxGroup)
        {
            var rig = new GameObject("HarborRain");
            var points = new GameObject("RoutePoints").transform;
            points.SetParent(rig.transform, false);
            Transform Point(string n, Vector3 p, float yaw = 0f)
            {
                var t = new GameObject(n).transform;
                t.SetParent(points, false);
                t.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                return t;
            }

            var spawnGo = new GameObject("SpawnPoint");
            spawnGo.transform.SetParent(rig.transform, false);
            spawnGo.transform.SetPositionAndRotation(Spawn, Quaternion.identity);
            var spawn = spawnGo.AddComponent<VehicleSpawnPoint>();

            var camGo = new GameObject("MainCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.SetPositionAndRotation(Spawn + new Vector3(0f, 2f, -6f), Quaternion.identity);
            var camera = camGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 3200f;
            camera.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<NeonRift.Audio.MasterLimiter>();
            camGo.AddComponent<CameraCullDistances>().EditorConfigure(new[] { new CameraCullDistances.Entry { layer = "Detail", distance = 240f } });
            camGo.AddComponent<CinemachineBrain>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.stopNaN = true;
            data.dithering = true;
            Configure(budget, so => so.FindProperty("viewer").objectReferenceValue = camGo.transform);

            var followGo = new GameObject("FollowCamera");
            followGo.transform.SetParent(rig.transform, false);
            var cm = followGo.AddComponent<CinemachineCamera>();
            cm.Lens.NearClipPlane = 0.1f;
            cm.Lens.FarClipPlane = 3200f;
            cm.Lens.FieldOfView = 60f;
            var chase = followGo.AddComponent<VehicleChaseCamera>();
            Configure(chase, so =>
            {
                so.FindProperty("distance").floatValue = 6.2f;
                so.FindProperty("height").floatValue = 2.05f;
                so.FindProperty("lookHeight").floatValue = 1.05f;
                so.FindProperty("speedDistance").floatValue = 1.5f;
                so.FindProperty("lookAheadTime").floatValue = 0.26f;
                so.FindProperty("maxLookAhead").floatValue = 8f;
                so.FindProperty("baseFieldOfView").floatValue = 60f;
                so.FindProperty("speedFieldOfView").floatValue = 12f;
                so.FindProperty("obstacleLayers").intValue = (1 << drivable) | (1 << environment);
            });

            var hudGo = new GameObject("HarborRainHUD");
            hudGo.transform.SetParent(rig.transform, false);
            var doc = hudGo.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_NeonRift/UI/NeonRiftPanelSettings.asset");
            var hud = hudGo.AddComponent<HarborRainHud>();

            var drivingGo = new GameObject("Driving");
            drivingGo.transform.SetParent(rig.transform, false);
            var driving = drivingGo.AddComponent<MissionSceneEntry>();
            Configure(driving, so =>
            {
                so.FindProperty("spawnPoint").objectReferenceValue = spawn;
                so.FindProperty("chaseCamera").objectReferenceValue = chase;
                so.FindProperty("viewCamera").objectReferenceValue = camera;
                so.FindProperty("outOfBoundsHeight").floatValue = -20f;
            });

            var audioGo = new GameObject("MissionAudio");
            audioGo.transform.SetParent(rig.transform, false);
            AudioClip M(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_NeonRift/Audio/Generated/Mission/{n}.wav");
            var cues = Source(audioGo, "Cues", null, false, audio.mixer.Sfx, 0.9f);
            var hack = Source(audioGo, "HackLoop", M("Hack_Loop"), true, audio.mixer.Sfx, 0.6f);
            hack.playOnAwake = false;
            var alarm = Source(audioGo, "AlarmLoop", M("Klaxon_Loop"), true, audio.mixer.Sfx, 0.3f);
            alarm.playOnAwake = false;
            var siren = Source(audioGo, "SirenLoop", M("Siren_Loop"), true, audio.mixer.Sfx, 0.07f);   // distant sirens only: the units carry their own 3D sirens
            siren.playOnAwake = false;
            var (radio, cinematics) = BuildCommand(rig, hud, clips, sfxGroup);
            var stations = BuildStations(GameObject.Find("World").transform, points);
            BuildBayCrane(GameObject.Find("World").transform, bayBoxes, clips, sfxGroup);

            var glowRoot = GameObject.Find("World").transform;
            var glows = BuildBayGlows(glowRoot);
            for (int i = 0; i < glows.Length && i < bayBoxes.Length; i++) glows[i].transform.parent.SetParent(bayBoxes[i], true);   // glow edges lift with the box
            var missionGo = new GameObject("Mission");
            missionGo.transform.SetParent(rig.transform, false);
            var mission = missionGo.AddComponent<HarborRainMission>();
            var coastCam = missionGo.AddComponent<HarborCoastCamera>();
            Configure(coastCam, so => so.FindProperty("waves").objectReferenceValue = coast.Waves);
            var spray = SprayPrefab();
            var police = BuildPolice(rig.transform, coast);
            // Music: the game's existing gameplay track and level (from Night Run's mission audio set, not modified).
            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(rig.transform, false);
            var nightRunAudio = AssetDatabase.LoadAssetAtPath<NeonRift.Gameplay.MissionAudioSet>("Assets/_NeonRift/Data/Missions/MissionAudio_NightRun.asset");
            var mixerAll = AssetDatabase.LoadAssetAtPath<NeonRift.Audio.AudioMixerConfig>("Assets/_NeonRift/Data/Audio/AudioMixerConfig.asset");
            var musicSource = musicGo.AddComponent<AudioSource>();
            musicSource.clip = nightRunAudio != null ? nightRunAudio.MusicTrack : null;
            musicSource.outputAudioMixerGroup = mixerAll != null ? mixerAll.Music : null;
            musicSource.loop = true; musicSource.playOnAwake = false; musicSource.spatialBlend = 0f; musicSource.volume = 0f; musicSource.priority = 0;
            var music = musicGo.AddComponent<HarborMusic>();
            music.EditorConfigure(musicSource, nightRunAudio != null ? nightRunAudio.MusicTrackVolume : 0.3f);
            report.AppendLine($"music: {(musicSource.clip != null ? musicSource.clip.name : "MISSING")} (the existing gameplay track) at {(nightRunAudio != null ? nightRunAudio.MusicTrackVolume : 0.3f):0.00} on the {(musicSource.outputAudioMixerGroup != null ? musicSource.outputAudioMixerGroup.name : "MISSING")} group; ducks under the radio");
            var mapRoads = Roads.Select(r => new Rect(r.min, r.max - r.min)).ToArray();
            Configure(mission, so =>
            {
                so.FindProperty("gatePoint").objectReferenceValue = Point("GatePoint", new Vector3(0f, 0f, -318f));
                so.FindProperty("manifestPoint").objectReferenceValue = Point("ManifestPoint", new Vector3(46f, 0f, -50f), 90f);
                so.FindProperty("securityGatePoint").objectReferenceValue = Point("SecurityGatePoint", new Vector3(225f, 0f, 86f));
                Array(so.FindProperty("shipmentBays"), BayPoints.Select((p, i) => (Object)Point($"Bay{i}", p)).ToArray());
                so.FindProperty("extractionPoint").objectReferenceValue = Point("ExtractionPoint", Extraction, 90f);
                so.FindProperty("overridePoint").objectReferenceValue = stations.overridePoint;
                so.FindProperty("relayPoint").objectReferenceValue = stations.relayPoint;
                so.FindProperty("coastPoint").objectReferenceValue = Point("CoastPoint", new Vector3(930f, 0f, 276f), 90f);
                so.FindProperty("coastArea").rectValue = new Rect(900f, 140f, 1420f, 440f);
                // The coastal extension adds ~870 m of road (both routes): the lockdown clock gets the same 30 s back.
                so.FindProperty("escapeSeconds").floatValue = 330f;
                so.FindProperty("police").objectReferenceValue = police;
                so.FindProperty("music").objectReferenceValue = music;
                Array(so.FindProperty("spreaders"), stations.spreaders);
                Array(so.FindProperty("bayContainers"), bayBoxes);
                so.FindProperty("sealSparks").objectReferenceValue = sealSparksPs;
                so.FindProperty("liftRunoff").objectReferenceValue = liftRunoffPs;
                so.FindProperty("crane").objectReferenceValue = bayCrane;
                so.FindProperty("heistAlarm").objectReferenceValue = heistAlarm;
                so.FindProperty("mule").objectReferenceValue = muleTruck;
                Array(so.FindProperty("searchlights"), searchlightHeads.ToArray());
                so.FindProperty("craneStrain").objectReferenceValue = clips.CableStrain;
                so.FindProperty("hydraulicClip").objectReferenceValue = clips.Hydraulic;
                so.FindProperty("metalImpactClip").objectReferenceValue = clips.MetalImpact;
                so.FindProperty("gateSparks").objectReferenceValue = stations.sparks;
                so.FindProperty("waves").objectReferenceValue = coast.Waves;
                so.FindProperty("radio").objectReferenceValue = radio;
                so.FindProperty("cinematics").objectReferenceValue = cinematics;
                Array(so.FindProperty("blockades"), coast.Blockades.ToArray());
                var areas = so.FindProperty("blockadeAreas");
                areas.arraySize = coast.BlockadeAreas.Count;
                for (int i = 0; i < coast.BlockadeAreas.Count; i++) areas.GetArrayElementAtIndex(i).rectValue = coast.BlockadeAreas[i];
                var bn = so.FindProperty("blockadeNames");
                bn.arraySize = coast.BlockadeNames.Count;
                for (int i = 0; i < coast.BlockadeNames.Count; i++) bn.GetArrayElementAtIndex(i).stringValue = coast.BlockadeNames[i];
                so.FindProperty("sirenLoop").objectReferenceValue = siren;
                so.FindProperty("sfxGroup").objectReferenceValue = sfxGroup;
                so.FindProperty("uiTick").objectReferenceValue = clips.UiTick;
                so.FindProperty("uiConfirm").objectReferenceValue = clips.UiConfirm;
                so.FindProperty("uiError").objectReferenceValue = clips.UiError;
                so.FindProperty("uiSuccess").objectReferenceValue = clips.UiSuccess;
                so.FindProperty("sparkClip").objectReferenceValue = clips.Spark;
                so.FindProperty("gateClunk").objectReferenceValue = clips.GateClunk;
                so.FindProperty("lockRelease").objectReferenceValue = clips.LockRelease;
                so.FindProperty("deviceNotify").objectReferenceValue = clips.DeviceNotify;
                so.FindProperty("deviceCritical").objectReferenceValue = clips.DeviceCritical;
                so.FindProperty("sprayClip").objectReferenceValue = clips.TyreSpray;
                var unlock = so.FindProperty("garage.unlockables");
                var f40 = AssetDatabase.LoadAssetAtPath<VehicleDefinition>($"{Gen}/Vehicles/PF_HR_FerrariF40LB/Vehicle_PF_HR_FerrariF40LB.asset");
                var f296 = AssetDatabase.LoadAssetAtPath<VehicleDefinition>($"{Gen}/Vehicles/PF_HR_Ferrari296SpecialeA/Vehicle_PF_HR_Ferrari296SpecialeA.asset");
                unlock.arraySize = 2;
                unlock.GetArrayElementAtIndex(0).FindPropertyRelative("vehicle").objectReferenceValue = f40;
                unlock.GetArrayElementAtIndex(0).FindPropertyRelative("requirement").intValue = (int)HarborGarage.Requirement.CompleteOperation;
                unlock.GetArrayElementAtIndex(1).FindPropertyRelative("vehicle").objectReferenceValue = f296;
                unlock.GetArrayElementAtIndex(1).FindPropertyRelative("requirement").intValue = (int)HarborGarage.Requirement.GradeAOrBetter;
                report.AppendLine($"garage: Level 2 cars {(f40 != null ? "F40 LB-WORKS" : "MISSING F40")} (complete the operation), {(f296 != null ? "296 SPECIALE A" : "MISSING 296")} (grade A+)");
                Array(so.FindProperty("routeProbes"), Point("ProbeQuayside", new Vector3(400f, 0f, 230f)), Point("ProbeLane80", new Vector3(450f, 0f, 80f)),
                      Point("ProbeTerminalRoad", new Vector3(450f, 0f, -50f)));
                var names = so.FindProperty("routeNames");
                names.arraySize = 3;
                names.GetArrayElementAtIndex(0).stringValue = "FLOODED QUAYSIDE";
                names.GetArrayElementAtIndex(1).stringValue = "CONTAINER LANES";
                names.GetArrayElementAtIndex(2).stringValue = "TERMINAL ROAD";
                so.FindProperty("restrictedArea").rectValue = Restricted;
                so.FindProperty("mainGate").objectReferenceValue = gates.Main;
                so.FindProperty("securityGate").objectReferenceValue = gates.Security;
                so.FindProperty("restrictedNorthGate").objectReferenceValue = gates.North;
                so.FindProperty("extractionGate").objectReferenceValue = gates.Extraction;
                so.FindProperty("laneCheckpoint").objectReferenceValue = gates.Lane;
                Array(so.FindProperty("deepFloods"), floods.Deep.ToArray());
                Array(so.FindProperty("shallowFloods"), floods.Shallow.ToArray());
                so.FindProperty("storm").objectReferenceValue = storm;
                Array(so.FindProperty("securityLamps"), lamps.ToArray());
                Array(so.FindProperty("alarmLights"), alarms.ToArray());
                Array(so.FindProperty("shipmentGlows"), glows);
                Array(so.FindProperty("cameras"), security.Cameras.ToArray());
                Array(so.FindProperty("drones"), security.Drones.ToArray());
                so.FindProperty("sprayPrefab").objectReferenceValue = spray;
                so.FindProperty("hud").objectReferenceValue = hud;
                var roads = so.FindProperty("mapRoads");
                roads.arraySize = mapRoads.Length;
                for (int i = 0; i < mapRoads.Length; i++) roads.GetArrayElementAtIndex(i).rectValue = mapRoads[i];
                Vectors(so.FindProperty("rivalRoute"), new(560f, 0.05f, -240f), new(380f, 0f, -240f), new(380f, 0f, -180f), new(450f, 0f, -180f),
                        new(450f, 0f, -50f), new(450f, 0f, 80f), new(320f, 0f, 80f), new(300f, 0f, 90f));
                Vectors(so.FindProperty("rivalEscapeRoute"), new(330f, 0f, 80f), new(450f, 0f, 80f), new(641f, 0f, 80f), new(641f, 0f, 225f), new(630f, 0f, 233f));
                Vectors(so.FindProperty("rivalCoastRoute"), new(650f, 0f, 230f), new(892f, 0f, 230f), new(892f, 0f, 156f), new(1592f, 0f, 156f), new(1592f, 0f, 270f), new(1880f, 0f, 276f), new(1904f, 0f, 286f),
                        new(1908f, 0f, 310f), new(1908f, 0f, 528f), new(1918f, 0f, 549f), new(1940f, 0f, 553f), new(2236f, 0f, 549f));
                Vectors(so.FindProperty("patrolLoop"), new(100f, 0.05f, -50f), new(450f, 0f, -50f), new(450f, 0f, 80f), new(90f, 0f, 80f), new(90f, 0f, -40f));
                so.FindProperty("cues").objectReferenceValue = cues;
                so.FindProperty("hackLoop").objectReferenceValue = hack;
                so.FindProperty("alarmLoop").objectReferenceValue = alarm;
                so.FindProperty("objectiveClip").objectReferenceValue = M("Cue_Objective");
                so.FindProperty("stepClip").objectReferenceValue = M("Heist_StepDone");
                so.FindProperty("deniedClip").objectReferenceValue = M("Heist_Denied");
                so.FindProperty("acquiredClip").objectReferenceValue = M("Heist_DataAcquired");
                so.FindProperty("lockdownClip").objectReferenceValue = M("Cue_Lockdown");
                so.FindProperty("warningClip").objectReferenceValue = M("Cue_Heat");
                so.FindProperty("successClip").objectReferenceValue = M("Cue_Success");
                so.FindProperty("failureClip").objectReferenceValue = M("Cue_Failure");
            });

            var entry = rig.AddComponent<HarborRainEntry>();
            Configure(entry, so =>
            {
                so.FindProperty("driving").objectReferenceValue = driving;
                so.FindProperty("mission").objectReferenceValue = mission;
            });
        }

        private static void Vectors(SerializedProperty property, params Vector3[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).vector3Value = values[i];
        }

        // ---------------- Validation (edit mode) ----------------

        private static void Validate()
        {
            Physics.SyncTransforms();
            int mask = 1 << drivable, hits = 0, samples = 0, blocked = 0;
            foreach (var (min, max) in Roads)
                for (int i = 0; i <= 12; i++)
                {
                    var p = new Vector3(Mathf.Lerp(min.x, max.x, i / 12f), 5f, Mathf.Lerp(min.y, max.y, i / 12f));
                    samples++;
                    if (Physics.Raycast(p, Vector3.down, out var hit, 10f, mask) && Mathf.Abs(hit.point.y) < 0.05f) hits++;
                    // Nothing solid may stand on a road (gate blockers excepted: they sit across lanes on purpose).
                    var cols = Physics.OverlapBox(new Vector3(p.x, 1.2f, p.z), new Vector3(0.8f, 0.9f, 0.8f), Quaternion.identity, 1 << environment);
                    // Terminal tractors are movers that drive the service roads and yield to the player.
                    bool Fixed(Collider c) => c.name != "Blocker" && !c.name.StartsWith("TerminalTractor");
                    // Designed: the square corners outside the two curved coastal bends (behind the bend guardrail), and the
                    // pier works section (one lane is checked separately below).
                    bool designed = (Vector3.Distance(new Vector3(p.x, 0f, p.z), BendA) > BendRadius + 8.5f && Mathf.Abs(p.x - 1908f) < 14f && Mathf.Abs(p.z - 276f) < 14f)
                                    || (Vector3.Distance(new Vector3(p.x, 0f, p.z), BendB) > BendRadius + 8.5f && Mathf.Abs(p.x - 1908f) < 14f && Mathf.Abs(p.z - 552f) < 14f)
                                    || NarrowArea.Contains(new Vector2(p.x, p.z));
                    if (!designed && cols.Any(Fixed)) { blocked++; report.AppendLine($"  obstacle on road at {p}: {cols.First(Fixed).name}"); }
                }
            report.AppendLine($"roads: {hits}/{samples} samples on drivable ground {(hits == samples ? "PASS" : "FAIL")}; {blocked} blocked samples {(blocked == 0 ? "PASS" : "FAIL")}");
            // The pier works section must leave one clear lane (≥ 6 m) along its whole length.
            int laneBlocked = 0;
            for (float x = NarrowArea.xMin - 6f; x <= NarrowArea.xMax + 6f; x += 2f)
                if (Physics.CheckBox(new Vector3(x, 1.2f, PierZ0 + 3.5f), new Vector3(0.9f, 0.9f, 3f), Quaternion.identity, 1 << environment)) laneBlocked++;
            report.AppendLine($"pier works: south lane 7 m clear past the barriers {(laneBlocked == 0 ? "PASS" : $"FAIL ({laneBlocked} samples blocked)")}");
            // The curved bends: the centre line of each bend arc must be clear of the bend guardrails.
            int bendBlocked = 0;
            foreach (var (c, a0, a1) in new[] { (BendA, -90f, 0f), (BendB, 180f, 90f) })
                foreach (var q in Arc(c, BendRadius, a0, a1, 12, 1.2f))
                    if (Physics.CheckBox(q, new Vector3(1.6f, 0.9f, 1.6f), Quaternion.identity, 1 << environment)) bendBlocked++;
            report.AppendLine($"coastal bends: curved centre lines clear {(bendBlocked == 0 ? "PASS" : $"FAIL ({bendBlocked})")}");

            // Connectivity: roads that touch form one graph from the entry road to the extraction road.
            var adj = new List<int>[Roads.Length];
            for (int i = 0; i < Roads.Length; i++)
            {
                adj[i] = new List<int>();
                var a = new Rect(Roads[i].min - Vector2.one, Roads[i].max - Roads[i].min + Vector2.one * 2f);
                for (int j = 0; j < Roads.Length; j++)
                    if (i != j && a.Overlaps(new Rect(Roads[j].min, Roads[j].max - Roads[j].min))) adj[i].Add(j);
            }
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<int>(new[] { 0 });
            while (queue.Count > 0) foreach (int j in adj[queue.Dequeue()]) if (seen.Add(j)) queue.Enqueue(j);
            report.AppendLine($"road network: {seen.Count}/{Roads.Length} roads connected to the entry road {(seen.Count == Roads.Length ? "PASS" : "FAIL")}");

            bool Clear(Vector3 p) => Physics.Raycast(p + Vector3.up * 3f, Vector3.down, 10f, mask) &&
                                     !Physics.CheckBox(p + Vector3.up * 1.2f, new Vector3(1.2f, 0.9f, 2.6f), Quaternion.identity, 1 << environment);
            report.AppendLine($"spawn: {(Clear(Spawn) ? "PASS" : "FAIL")}");
            foreach (var (name, p) in new[] { ("gate", new Vector3(0f, 0f, -318f)), ("manifest", new Vector3(46f, 0f, -50f)), ("security gate", new Vector3(225f, 0f, 86f)),
                                              ("bay R-1", BayPoints[0]), ("bay R-2", BayPoints[1]), ("bay R-3", BayPoints[2]), ("extraction", Extraction),
                                              ("gate junction box", new Vector3(646f, 0f, 231f)), ("drone relay", new Vector3(-120f, 0f, 232f)), ("coast road", new Vector3(930f, 0f, 276f)) })
                report.AppendLine($"route point {name}: {(Clear(p) ? "PASS" : "FAIL")}");
            int env = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Count(c => c.gameObject.layer == environment);
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            long tris = renderers.Sum(r => { var f = r.GetComponent<MeshFilter>(); return f != null && f.sharedMesh != null ? (long)f.sharedMesh.triangles.Length / 3 : 0; });
            report.AppendLine($"scene: {env} environment colliders, {renderers.Length} mesh renderers, {tris:N0} triangles, {Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length} lights");
            report.AppendLine($"footprint: security perimeter 1320 × 628 m plus the coast strip to x 1934 and the headland / pier to x 2302 (playable 3020 × 1230 m bounds)");
            var graph = new HarborRoutes(Roads.Select(r => new Rect(r.min, r.max - r.min)).ToArray());
            var path = new List<Vector3>();
            bool toExtraction = graph.Find(Spawn, Extraction, path, out float metres, out _);
            report.AppendLine($"route graph: {graph.NodeCount} junctions, {graph.EdgeCount} edges; spawn → extraction {(toExtraction ? $"PASS ({metres:0} m)" : "FAIL")}");
        }
    }
}
