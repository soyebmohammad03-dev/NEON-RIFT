using System.Collections.Generic;
using System.Linq;
using NeonRift.Audio;
using NeonRift.EditorTools.District;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Frontend;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace NeonRift.EditorTools.Frontend
{
    /// <summary>
    /// Generates the Car Select scene: the crew's garage in Sector 7. A 26 × 30 m workshop hall with a polished
    /// concrete floor, steel columns and roof trusses, soft-box strip lights, a turntable under a key light and two
    /// rims, workbenches, tool chests, a tyre rack and a planning wall of screens, two parking bays for the cars not
    /// chosen, and a roller door onto a lit street that opens when the player confirms. Cinemachine cameras for the
    /// hero shot, the swap, the opening close-up and the departure; a showroom grade Volume; a baked reflection
    /// probe. Re-runnable: <b>Neon Rift ▸ Car Select ▸ Build Garage</b>.
    /// </summary>
    public static class GarageBuilder
    {
        public const string ScenePath = "Assets/_NeonRift/Scenes/CarSelect.unity";
        private const string ArtFolder = "Assets/_NeonRift/Art/Garage";
        private const string MeshFolder = ArtFolder + "/Meshes";
        private const string MaterialFolder = ArtFolder + "/Materials";
        private const string ProfilePath = "Assets/_NeonRift/Settings/Rendering/Garage_Volume.asset";

        public const float HallHalfWidth = 13f, HallBack = -16f, HallFront = 14f, HallHeight = 7.5f;
        public const float DoorHalfWidth = 5f, DoorHeight = 5.6f;
        public static readonly Vector3 Focus = new(0f, 0.6f, 0f);
        /// <summary>Car Select's opening shot (a headlight close-up), garage-local. The intro's last move ends exactly here.</summary>
        public static readonly Vector3 OpeningPosition = new(1.05f, 0.72f, 3.2f), OpeningLook = new(0.7f, 0.66f, 2.1f);
        public const float OpeningFov = 34f;

        private static int showroomLayer;

        [MenuItem("Neon Rift/Car Select/Build Garage")]
        public static void BuildFromMenu() => Debug.Log(Build());

        /// <summary>The garage hall without the showroom machinery: the same building is used by Car Select and, in the city, by the intro.</summary>
        public sealed class Shell
        {
            public Transform Root, Turntable, Door;
            public Light Key, RimCool, RimWarm;
            public readonly List<Light> Stage = new();
            public readonly List<Light> All = new();
            public readonly List<Renderer> Strips = new();
            public Transform[] Slots;
        }

        public static string Build()
        {
            var log = new System.Text.StringBuilder("[Garage] build\n");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureRendering();
            var shell = BuildShell(withStreet: true);
            var turntable = shell.Turntable;
            var door = shell.Door;
            var stage = shell.Stage;
            var strips = shell.Strips;
            var slots = shell.Slots;

            // Showroom, engine, cameras.
            var showroomGo = new GameObject("Showroom");
            var engine = turntable.gameObject.AddComponent<ShowroomEngine>();
            var mixerConfig = AssetDatabase.LoadAssetAtPath<AudioMixerConfig>("Assets/_NeonRift/Data/Audio/AudioMixerConfig.asset");
            engine.EditorConfigure(mixerConfig != null ? mixerConfig.Engine : null);
            var focus = new GameObject("ShowroomFocus").transform;
            focus.position = Focus;
            var cams = Cameras(focus);
            var showroom = showroomGo.AddComponent<VehicleShowroom>();
            showroom.EditorConfigure(turntable, stage.ToArray(), strips.ToArray(), slots, engine, door, DoorHeight - 0.2f, Vector3.back,
                                     cams.hero, cams.swap, cams.depart, cams.opening);

            Probe();
            Volume();
            Ui(showroom);

            Lightmapping.lightingDataAsset = null;
            EditorSceneManager.SaveScene(scene, ScenePath);
            BakeProbe();
            EditorSceneManager.SaveScene(scene, ScenePath);
            log.AppendLine($"  scene saved: {ScenePath}");
            log.AppendLine($"  lights {shell.All.Count}, stage lights {stage.Count}, strips {strips.Count}");
            return log.ToString();
        }

        /// <summary>
        /// Builds the hall, workshop, turntable, roller door and lights under one root at the origin (so the caller can
        /// move the whole building). <paramref name="withStreet"/> adds the stand-in street seen through the door in
        /// Car Select; the city version sits on a real street instead.
        /// </summary>
        public static Shell BuildShell(bool withStreet)
        {
            VehiclePrefabBuilder.EnsureFolder(MeshFolder);
            VehiclePrefabBuilder.EnsureFolder(MaterialFolder);
            showroomLayer = LayerMask.NameToLayer("Showroom");
            var shell = new Shell { Root = new GameObject("GarageShell").transform };
            var m = Materials();

            var env = new GameObject("Garage").transform;
            env.SetParent(shell.Root, false);
            var meshes = new Dictionary<Material, MeshBuilder>();
            MeshBuilder B(Material mat) { if (!meshes.TryGetValue(mat, out var b)) meshes[mat] = b = new MeshBuilder(); return b; }
            var strips = shell.Strips;

            Hall(B, m);
            Workshop(B, m);
            var (turntable, ring) = Turntable(m);
            var door = Door(m);
            if (withStreet) Street(B, m, env);
            SoftBoxes(m, env, strips);
            foreach (var pair in meshes)
            {
                var go = Emit(pair.Value, $"Garage_{pair.Key.name.Replace("Garage_", "").Replace("District_", "")}", pair.Key, env);
                if (pair.Key == m.Strip) strips.Add(go.GetComponent<Renderer>());
            }
            ring.transform.SetParent(env, true);
            turntable.SetParent(shell.Root, true);
            door.SetParent(shell.Root, true);
            // Loose parts emitted at the scene root (door frame, beacon).
            foreach (var name in new[] { "Garage_DoorFrame", "Garage_DoorBeacon" })
            {
                var loose = GameObject.Find(name);
                if (loose != null && loose.transform.parent == null) loose.transform.SetParent(env, true);
            }

            // Stage lights: key, two rims, three soft-box fills; dim parking-bay rims.
            var lights = new GameObject("Lights").transform;
            lights.SetParent(shell.Root, false);
            var key = Spot(lights, "Key", new Vector3(1.5f, 6.8f, 4.5f), Focus, new Color(1f, 0.96f, 0.9f), 120f, 16f, 46f, LightShadows.Soft);
            var rimL = Spot(lights, "Rim_Cool", new Vector3(-6.5f, 4.2f, -5.5f), Focus + Vector3.up * 0.3f, new Color(0.62f, 0.85f, 1f), 110f, 16f, 40f, LightShadows.None);
            var rimR = Spot(lights, "Rim_Warm", new Vector3(6.5f, 3.8f, -5f), Focus + Vector3.up * 0.3f, new Color(1f, 0.72f, 0.5f), 90f, 16f, 40f, LightShadows.None);
            var fills = new List<Light>();
            for (int i = -1; i <= 1; i++)
                fills.Add(Spot(lights, $"SoftBox_{i + 1}", new Vector3(i * 2.4f, HallHeight - 0.5f, 0f), new Vector3(i * 2.4f, 0f, 0f), new Color(0.95f, 0.97f, 1f), 26f, 12f, 120f, LightShadows.None));
            Spot(lights, "Bay_Left", new Vector3(-9f, 5.5f, -3f), new Vector3(-8.2f, 0.5f, -8.5f), new Color(0.55f, 0.7f, 1f), 80f, 12f, 50f, LightShadows.None);
            Spot(lights, "Bay_Right", new Vector3(9f, 5.5f, -3f), new Vector3(8.2f, 0.5f, -8.5f), new Color(1f, 0.7f, 0.5f), 70f, 12f, 50f, LightShadows.None);
            Spot(lights, "Workbench", new Vector3(-11.5f, 3.4f, 6f), new Vector3(-12f, 0.9f, 6f), new Color(1f, 0.85f, 0.65f), 14f, 6f, 100f, LightShadows.None);
            // Wall washers: the hall reads as a space (columns, panels, door, sign) without lighting the car.
            foreach (float x in new[] { -9f, 0f, 9f })
                Spot(lights, $"Wash_Back_{x:0}", new Vector3(x, HallHeight - 0.6f, HallBack + 2.2f), new Vector3(x, 1.5f, HallBack), new Color(0.75f, 0.82f, 1f), x == 0f ? 175f : 130f, 19f, 110f, LightShadows.None);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { -9f, 0f, 9f })
                    Spot(lights, $"Wash_Side_{sx:0}_{z:0}", new Vector3(sx * (HallHalfWidth - 2.2f), HallHeight - 0.6f, z), new Vector3(sx * HallHalfWidth, 1.2f, z),
                         sx < 0f ? new Color(1f, 0.82f, 0.62f) : new Color(0.7f, 0.82f, 1f), 90f, 17f, 110f, LightShadows.None);
            if (withStreet)
            {
                // The street outside the door: a sodium wash on the road and the facade across it (seen when the door lifts).
                Spot(lights, "Street_Road", new Vector3(-4f, 7f, HallBack - 9f), new Vector3(0f, 0f, HallBack - 6f), new Color(1f, 0.62f, 0.3f), 160f, 18f, 95f, LightShadows.None);
                Spot(lights, "Street_Facade", new Vector3(6f, 9f, HallBack - 8f), new Vector3(2f, 6f, -36f), new Color(0.75f, 0.6f, 1f), 120f, 25f, 80f, LightShadows.None);
            }
            Spot(lights, "PlanningWall", new Vector3(10.5f, 4f, 7f), new Vector3(12.8f, 2.2f, 7f), new Color(0.6f, 0.85f, 1f), 10f, 6f, 90f, LightShadows.None);
            shell.Key = key;
            shell.RimCool = rimL;
            shell.RimWarm = rimR;
            shell.Stage.AddRange(new[] { key, rimL, rimR });
            shell.Stage.AddRange(fills);
            shell.All.AddRange(lights.GetComponentsInChildren<Light>(true));

            // Parking bays for the cars not chosen.
            shell.Slots = new[] { Slot("ParkedSlot_Left", new Vector3(-8.2f, 0f, -8.5f), 32f), Slot("ParkedSlot_Right", new Vector3(8.2f, 0f, -8.5f), -32f) };
            foreach (var slot in shell.Slots) slot.SetParent(shell.Root, true);
            shell.Turntable = turntable;
            shell.Door = door;
            return shell;
        }

        // ---------------- Materials ----------------

        private sealed class Mats
        {
            public Material Work, Floor, Wall, Panel, Steel, Ceiling, Strip, Ring, Turntable, Hazard, Marking, ToolRed, Rubber, Wood, Screen,
                            Door, DoorFrame, Asphalt, Facade, Sign, Warning, Glass, Neon, LampHead;
            public Material[] Screens;
        }

        private static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{DistrictKit.MaterialFolder}/{name}.mat");

        private static Material Lit(string name, Color colour, float smoothness, float metallic, Texture2D map = null, Texture2D normal = null,
                                    float normalScale = 1f, Color? emission = null)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", colour);
            mat.SetTexture("_BaseMap", map);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", normalScale);
            if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
            bool emissive = emission.HasValue && emission.Value.maxColorComponent > 0f;
            mat.SetColor("_EmissionColor", emission ?? Color.black);
            if (emissive) mat.EnableKeyword("_EMISSION"); else mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Mats Materials()
        {
            var pavement = Load("District_Pavement");
            var pAlbedo = pavement != null ? pavement.GetTexture("_BaseMap") as Texture2D : null;
            var pNormal = pavement != null ? pavement.GetTexture("_BumpMap") as Texture2D : null;
            var m = new Mats
            {
                // Sealed, polished concrete: dark, glossy, faint aggregate in the normal map.
                Floor = Lit("Garage_Floor", new Color(0.26f, 0.265f, 0.28f), 0.84f, 0f, pAlbedo, pNormal, 0.18f),
                Wall = Lit("Garage_Wall", new Color(0.2f, 0.205f, 0.215f), 0.18f, 0f, pAlbedo, pNormal, 0.5f),
                Panel = Lit("Garage_WallPanel", new Color(0.09f, 0.095f, 0.105f), 0.35f, 0.3f),
                Steel = Lit("Garage_Steel", new Color(0.12f, 0.125f, 0.135f), 0.55f, 0.8f),
                Ceiling = Lit("Garage_Ceiling", new Color(0.03f, 0.03f, 0.035f), 0.1f, 0f),
                Strip = Lit("Garage_SoftBox", Color.white, 0.5f, 0f, emission: new Color(2.2f, 2.3f, 2.45f)),
                Ring = Lit("Garage_TurntableRing", Color.black, 0.6f, 0f, emission: new Color(0.06f, 0.32f, 0.42f)),
                Turntable = Lit("Garage_Turntable", new Color(0.05f, 0.052f, 0.058f), 0.78f, 0.6f),
                Hazard = Lit("Garage_Hazard", new Color(0.75f, 0.55f, 0.06f), 0.4f, 0f, emission: new Color(0.06f, 0.04f, 0.0f)),
                Marking = Lit("Garage_FloorMarking", new Color(0.3f, 0.31f, 0.33f), 0.6f, 0f),
                ToolRed = Lit("Garage_ToolChest", new Color(0.42f, 0.04f, 0.035f), 0.7f, 0.35f),
                Rubber = Lit("Garage_Rubber", new Color(0.025f, 0.025f, 0.028f), 0.35f, 0f),
                Wood = Lit("Garage_Bench", new Color(0.22f, 0.15f, 0.09f), 0.3f, 0f),
                Screen = Lit("Garage_ScreenBezel", new Color(0.02f, 0.02f, 0.025f), 0.7f, 0.2f),
                Door = Load("District_RollerDoor"),
                DoorFrame = Lit("Garage_DoorFrame", new Color(0.1f, 0.1f, 0.11f), 0.45f, 0.7f),
                Asphalt = Load("District_Asphalt"),
                Facade = Load("District_Facade1"),
                Sign = Load("District_SignCyan"),
                Warning = Load("District_AviationRed"),
                Glass = Load("District_Glass"),
                Neon = Load("District_NeonMagenta"),
                LampHead = Load("District_LampHeadSodium"),
            };
            m.Work = Lit("Garage_WorkStrip", Color.white, 0.5f, 0f, emission: new Color(2.2f, 2.1f, 1.9f));
            // The planning wall: the crew's map of the city with the route to the core, a core schematic, and two feeds.
            var plan = GaragePlanningWall.Screens();
            var feeds = Enumerable.Range(0, 2).Select(i => Load($"District_Billboard{i}")).Where(x => x != null).ToList();
            m.Screens = new[] { plan[0], plan[1], feeds.Count > 0 ? feeds[0] : plan[1], plan[0] };
            return m;
        }

        // ---------------- Geometry ----------------

        private static void Hall(System.Func<Material, MeshBuilder> B, Mats m)
        {
            float w = HallHalfWidth, back = HallBack, front = HallFront, h = HallHeight;
            B(m.Floor).Ground(new Vector3(-w, 0f, back), new Vector3(w, 0f, front), 3f);
            // Walls: back wall with the door opening, sides, front (behind the camera; it shows in reflections).
            B(m.Wall).Cuboid(new Vector3(-w - 0.4f, 0f, back - 0.4f), new Vector3(-DoorHalfWidth, h, back), 3f);
            B(m.Wall).Cuboid(new Vector3(DoorHalfWidth, 0f, back - 0.4f), new Vector3(w + 0.4f, h, back), 3f);
            B(m.Wall).Cuboid(new Vector3(-DoorHalfWidth, DoorHeight, back - 0.4f), new Vector3(DoorHalfWidth, h, back), 3f);
            B(m.Wall).Cuboid(new Vector3(-w - 0.4f, 0f, back), new Vector3(-w, h, front), 3f);
            B(m.Wall).Cuboid(new Vector3(w, 0f, back), new Vector3(w + 0.4f, h, front), 3f);
            B(m.Wall).Cuboid(new Vector3(-w - 0.4f, 0f, front), new Vector3(w + 0.4f, h, front + 0.4f), 3f);
            B(m.Ceiling).Cuboid(new Vector3(-w - 0.4f, h, back - 0.4f), new Vector3(w + 0.4f, h + 0.3f, front + 0.4f), 4f, bottom: true);
            // Dado panels along the side walls (dark metal), a skirting line.
            foreach (float x in new[] { -w + 0.03f, w - 0.03f })
                B(m.Panel).Cuboid(new Vector3(x - 0.03f, 0f, back), new Vector3(x + 0.03f, 1.2f, front), 2f);
            // Steel columns every 6 m along the sides and back, with knee braces to the trusses.
            for (float z = back + 3f; z < front; z += 6f)
                foreach (float x in new[] { -w + 0.35f, w - 0.35f })
                {
                    IBeam(B(m.Steel), new Vector3(x, 0f, z), h, 0f);
                    B(m.Steel).OrientedBox(new Vector3(x - Mathf.Sign(x) * 0.9f, h - 0.9f, z), new Vector3(0.12f, 2.3f, 0.12f), Quaternion.Euler(0f, 0f, Mathf.Sign(x) * 45f), 1f);
                }
            // Roof trusses across the hall.
            for (float z = back + 3f; z < front; z += 6f)
            {
                B(m.Steel).OrientedBox(new Vector3(0f, h - 0.2f, z), new Vector3(w * 2f, 0.3f, 0.25f), Quaternion.identity, 1f);
                B(m.Steel).OrientedBox(new Vector3(0f, h - 1.3f, z), new Vector3(w * 2f, 0.15f, 0.15f), Quaternion.identity, 1f);
                for (float x = -w + 1.5f; x < w - 1f; x += 2.2f)
                    B(m.Steel).OrientedBox(new Vector3(x, h - 0.75f, z), new Vector3(0.08f, 1.25f, 0.08f), Quaternion.Euler(0f, 0f, (Mathf.RoundToInt(x) % 2 == 0) ? 35f : -35f), 1f);
            }
            // Fluorescent tube fittings on the side walls between the columns (practical lights in frame).
            for (float z = back + 6f; z < front - 1f; z += 6f)
                foreach (float x in new[] { -w + 0.12f, w - 0.12f })
                {
                    B(m.Steel).OrientedBox(new Vector3(x, 3.4f, z), new Vector3(0.1f, 0.12f, 1.7f), Quaternion.identity, 1f);
                    B(m.Work).OrientedBox(new Vector3(x - Mathf.Sign(x) * 0.07f, 3.33f, z), new Vector3(0.05f, 0.05f, 1.55f), Quaternion.identity, 1f);
                }
            // Floor markings: bay lines for the two parked cars, a keep-clear ring round the turntable, safety lines.
            var mk = B(m.Marking);
            foreach (float sx in new[] { -1f, 1f })
            {
                var c = new Vector3(sx * 8.2f, 0.004f, -8.5f);
                var rot = Quaternion.Euler(0f, sx * 32f, 0f);
                foreach (float side in new[] { -1.45f, 1.45f })
                    mk.Strip(c + rot * new Vector3(side, 0f, -2.8f), c + rot * new Vector3(side, 0f, 2.8f), 0.1f);
                mk.Strip(c + rot * new Vector3(-1.45f, 0f, -2.8f), c + rot * new Vector3(1.45f, 0f, -2.8f), 0.1f);
            }
            mk.Ring(new Vector3(0f, 0.004f, 0f), 4.3f, 4.38f, 64);
            var hazard = B(m.Hazard);
            hazard.Strip(new Vector3(-DoorHalfWidth, 0.005f, back + 0.5f), new Vector3(DoorHalfWidth, 0.005f, back + 0.5f), 0.25f);
            for (float x = -DoorHalfWidth + 0.4f; x < DoorHalfWidth; x += 0.8f)
                hazard.Strip(new Vector3(x, 0.006f, back + 0.1f), new Vector3(x + 0.35f, 0.006f, back + 0.9f), 0.2f);
            // Crew sign over the door.
            var uv = DistrictTextures.SignRect("NEON RIFT", out float aspect);
            B(m.Sign).Panel(new Vector3(0f, DoorHeight + 0.85f, back + 0.02f), Quaternion.identity, 0.9f * aspect, 0.9f, uv);
            B(m.Panel).Cuboid(new Vector3(-0.45f * aspect - 0.3f, DoorHeight + 0.25f, back), new Vector3(0.45f * aspect + 0.3f, DoorHeight + 1.45f, back + 0.015f), 1f);
        }

        private static void IBeam(MeshBuilder b, Vector3 foot, float height, float yaw)
        {
            var r = Quaternion.Euler(0f, yaw, 0f);
            b.OrientedBox(foot + Vector3.up * (height * 0.5f), new Vector3(0.08f, height, 0.3f), r, 1f);
            b.OrientedBox(foot + Vector3.up * (height * 0.5f) + r * Vector3.forward * 0.15f, new Vector3(0.32f, height, 0.04f), r, 1f);
            b.OrientedBox(foot + Vector3.up * (height * 0.5f) - r * Vector3.forward * 0.15f, new Vector3(0.32f, height, 0.04f), r, 1f);
            b.OrientedBox(foot + Vector3.up * 0.15f, new Vector3(0.5f, 0.3f, 0.5f), r, 1f);
        }

        /// <summary>Left wall: workbench, tool chests, tyre rack. Right wall: the planning wall of screens, a desk, drums.</summary>
        private static void Workshop(System.Func<Material, MeshBuilder> B, Mats m)
        {
            float w = HallHalfWidth;
            // Workbench with a pegboard of tools.
            var bench = new Vector3(-w + 0.6f, 0f, 6f);
            B(m.Wood).OrientedBox(bench + new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 0.06f, 4f), Quaternion.identity, 1f);
            foreach (float z in new[] { -1.9f, 1.9f })
                B(m.Steel).OrientedBox(bench + new Vector3(0f, 0.45f, z), new Vector3(0.8f, 0.9f, 0.06f), Quaternion.identity, 1f);
            B(m.Panel).OrientedBox(bench + new Vector3(-0.42f, 1.9f, 0f), new Vector3(0.04f, 1.6f, 4f), Quaternion.identity, 1f);
            for (int i = 0; i < 9; i++)
                B(m.Steel).OrientedBox(bench + new Vector3(-0.36f, 1.5f + (i % 3) * 0.4f, -1.6f + i * 0.38f), new Vector3(0.04f, 0.3f, 0.06f), Quaternion.Euler(i * 11f, 0f, 0f), 1f);
            // Tool chests.
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(-w + 0.65f, 0f, 1.2f - i * 1.35f);
                B(m.ToolRed).OrientedBox(p + new Vector3(0f, 0.55f, 0f), new Vector3(0.75f, 1.1f, 1.2f), Quaternion.identity, 1f);
                for (int d = 0; d < 5; d++)
                    B(m.Steel).OrientedBox(p + new Vector3(0.38f, 0.2f + d * 0.2f, 0f), new Vector3(0.02f, 0.025f, 0.9f), Quaternion.identity, 1f);
            }
            // Tyre rack: tyres on edge in a steel frame.
            var rack = new Vector3(-w + 0.7f, 0f, -6f);
            foreach (float y in new[] { 0.05f, 1.2f })
                B(m.Steel).OrientedBox(rack + new Vector3(0f, y + 0.02f, 0f), new Vector3(0.9f, 0.04f, 4.4f), Quaternion.identity, 1f);
            for (int i = 0; i < 12; i++)
            {
                float y = i < 6 ? 0.42f : 1.57f;
                var c = rack + new Vector3(0f, y, -1.9f + (i % 6) * 0.75f);
                // A tyre on its edge: a cylinder lying along X.
                var t = new MeshBuilder();
                B(m.Rubber).OrientedBox(c, new Vector3(0.28f, 0.7f, 0.7f), Quaternion.Euler(45f, 0f, 0f), 1f);
                B(m.Rubber).OrientedBox(c, new Vector3(0.28f, 0.7f, 0.7f), Quaternion.identity, 1f);
            }
            // Planning wall: four screens with the city on them, a desk, a chair.
            float sx = w - 0.08f;
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3(sx, 2.3f + (i / 2) * 1.05f, 6f + (i % 2 - 0.5f) * 1.9f);
                var face = Quaternion.LookRotation(Vector3.left);
                B(m.Screen).OrientedBox(c + Vector3.right * 0.03f, new Vector3(1.85f, 1f, 0.06f), face, 1f);
                if (m.Screens.Length > 0) B(m.Screens[i % m.Screens.Length]).Panel(c - Vector3.right * 0.005f, face, 1.75f, 0.92f, new Rect(0f, 0f, 1f, 1f));
            }
            var desk = new Vector3(w - 0.9f, 0f, 6f);
            B(m.Steel).OrientedBox(desk + new Vector3(0f, 0.75f, 0f), new Vector3(1.2f, 0.05f, 3f), Quaternion.identity, 1f);
            foreach (float z in new[] { -1.4f, 1.4f })
                B(m.Steel).OrientedBox(desk + new Vector3(0f, 0.37f, z), new Vector3(1.1f, 0.74f, 0.05f), Quaternion.identity, 1f);
            // Drums and crates in the back corners.
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(w - 1f - (i % 2) * 0.7f, 0f, HallBack + 1.2f + (i / 2) * 0.7f);
                B(m.ToolRed).Cylinder(p, 0.3f, 0.9f, 12, true);
            }
            for (int i = 0; i < 3; i++)
                B(m.Wood).OrientedBox(new Vector3(-w + 1.2f + i * 1.25f, 0.5f + (i == 1 ? 1f : 0f) * 0f, HallBack + 1f), new Vector3(1.1f, 1f, 1.1f), Quaternion.Euler(0f, i * 7f, 0f), 1f);
        }

        private static (Transform table, GameObject ring) Turntable(Mats m)
        {
            var table = new GameObject("Turntable").transform;
            table.position = Vector3.zero;
            var disc = new MeshBuilder();
            disc.Cylinder(Vector3.zero, 3.3f, 0.09f, 72, true, 8f);
            var discGo = Emit(disc, "Garage_TurntableDisc", m.Turntable, table);
            discGo.layer = showroomLayer;
            // Fine radial lines on the top (rotate with the disc, so the turning reads).
            var lines = new MeshBuilder();
            for (int i = 0; i < 24; i++)
            {
                var d = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
                lines.Strip(d * 0.8f + Vector3.up * 0.092f, d * 3.15f + Vector3.up * 0.092f, 0.02f);
            }
            Emit(lines, "Garage_TurntableLines", m.Steel, table);
            // Static LED ring round the edge (does not turn).
            var ring = new MeshBuilder();
            ring.Ring(new Vector3(0f, 0.093f, 0f), 3.31f, 3.36f, 96);
            var ringGo = Emit(ring, "Garage_TurntableRing", m.Ring, null);
            return (table, ringGo);
        }

        private static Transform Door(Mats m)
        {
            var door = new GameObject("RollerDoor").transform;
            door.position = new Vector3(0f, 0f, HallBack - 0.2f);
            var b = new MeshBuilder();
            b.Cuboid(new Vector3(-DoorHalfWidth, 0f, -0.06f), new Vector3(DoorHalfWidth, DoorHeight, 0.06f), 2f);
            Emit(b, "Garage_RollerDoorPanel", m.Door, door);
            var bar = new MeshBuilder();
            bar.Cuboid(new Vector3(-DoorHalfWidth, 0f, -0.1f), new Vector3(DoorHalfWidth, 0.18f, 0.1f), 1f);
            Emit(bar, "Garage_RollerDoorBar", m.DoorFrame, door);
            // Frame, guide rails and the warning beacon above (not moving).
            var frame = new MeshBuilder();
            foreach (float x in new[] { -DoorHalfWidth - 0.15f, DoorHalfWidth + 0.15f })
                frame.OrientedBox(new Vector3(x, DoorHeight * 0.5f, HallBack + 0.05f), new Vector3(0.3f, DoorHeight, 0.3f), Quaternion.identity, 1f);
            frame.OrientedBox(new Vector3(0f, DoorHeight + 0.2f, HallBack + 0.05f), new Vector3(DoorHalfWidth * 2f + 0.6f, 0.4f, 0.3f), Quaternion.identity, 1f);
            Emit(frame, "Garage_DoorFrame", m.DoorFrame, null);
            if (m.Warning != null)
            {
                var beacon = new MeshBuilder();
                beacon.OrientedBox(new Vector3(DoorHalfWidth + 0.6f, DoorHeight + 0.4f, HallBack + 0.15f), new Vector3(0.16f, 0.16f, 0.16f), Quaternion.identity, 1f);
                Emit(beacon, "Garage_DoorBeacon", m.Warning, null);
            }
            return door;
        }

        /// <summary>Outside the door: wet street, a lit facade across the road, a sodium lamp. Only seen when the door lifts.</summary>
        private static void Street(System.Func<Material, MeshBuilder> B, Mats m, Transform env)
        {
            if (m.Asphalt != null) B(m.Asphalt).Ground(new Vector3(-40f, -0.005f, -70f), new Vector3(40f, -0.005f, HallBack - 0.4f), 8f);
            if (m.Facade != null)
                B(m.Facade).Box(new Vector3(0f, 0f, -36f), Quaternion.identity, new Vector3(70f, 26f, 8f), DistrictTextures.FacadeTile, new Vector2(0.3f, 0.1f), null, Vector2.one);
            if (m.Neon != null) B(m.Neon).OrientedBox(new Vector3(-6f, 4.4f, -35.8f), new Vector3(12f, 0.1f, 0.4f), Quaternion.identity, 1f);
            var lamp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeonRift/Prefabs/District/PF_Prop_StreetLight_Sodium.prefab");
            if (lamp != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(lamp, env);
                go.transform.SetPositionAndRotation(new Vector3(3.5f, 0f, -30f), Quaternion.LookRotation(Vector3.forward));
                foreach (var l in go.GetComponentsInChildren<Light>(true)) { l.enabled = true; l.shadows = LightShadows.None; }
            }
        }

        /// <summary>Three long soft boxes over the turntable and strip lights over the work areas.</summary>
        private static MeshBuilder SoftBoxes(Mats m, Transform env, List<Renderer> strips)
        {
            var b = new MeshBuilder();
            for (int i = -1; i <= 1; i++)
            {
                b.OrientedBox(new Vector3(i * 2.4f, HallHeight - 0.35f, 0f), new Vector3(0.5f, 0.06f, 7.5f), Quaternion.identity, 1f);
            }
            var frame = new MeshBuilder();
            for (int i = -1; i <= 1; i++)
                frame.OrientedBox(new Vector3(i * 2.4f, HallHeight - 0.3f, 0f), new Vector3(0.62f, 0.08f, 7.7f), Quaternion.identity, 1f);
            var go = Emit(b, "Garage_SoftBoxes", m.Strip, env);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            strips.Add(go.GetComponent<Renderer>());
            Emit(frame, "Garage_SoftBoxFrames", m.Steel, env);
            // Work strips (stay lit when the stage dims).
            var work = new MeshBuilder();
            foreach (var (x, z) in new[] { (-10f, 6f), (-10f, -6f), (10f, 6f), (10f, -6f), (-7f, -11f), (7f, -11f) })
                work.OrientedBox(new Vector3(x, HallHeight - 0.9f, z), new Vector3(0.25f, 0.05f, 2.4f), Quaternion.identity, 1f);
            var w = Emit(work, "Garage_WorkStrips", m.Work, env);
            w.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return b;
        }

        private static GameObject Emit(MeshBuilder b, string name, Material material, Transform parent)
        {
            var mesh = b.ToMesh(name, material != null && material.IsKeywordEnabled("_NORMALMAP"));
            string path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(mesh, existing); existing.name = name; Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        // ---------------- Lights, cameras, rendering ----------------

        private static Light Spot(Transform parent, string name, Vector3 position, Vector3 target, Color colour, float intensity, float range, float angle, LightShadows shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.55f;
            l.shadows = shadows;
            l.shadowStrength = 0.85f;
            return l;
        }

        private static Transform Slot(string name, Vector3 position, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return t;
        }

        private static (CinemachineCamera hero, CinemachineCamera swap, CinemachineCamera depart, CinemachineCamera opening) Cameras(Transform focus)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 220f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            camGo.AddComponent<AudioListener>();
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1.1f);
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.IgnoreTimeScale = true;

            var noise = AssetDatabase.FindAssets("Handheld_normal_mild t:NoiseSettings").Select(AssetDatabase.GUIDToAssetPath)
                                     .Select(AssetDatabase.LoadAssetAtPath<NoiseSettings>).FirstOrDefault();
            CinemachineCamera V(string name, Vector3 position, float fov, int priority, float shake, Transform lookAt)
            {
                var go = new GameObject(name);
                go.transform.position = position;
                go.transform.rotation = Quaternion.LookRotation((lookAt != null ? lookAt.position : Focus) - position);
                var v = go.AddComponent<CinemachineCamera>();
                v.Lens = new LensSettings { FieldOfView = fov, NearClipPlane = 0.05f, FarClipPlane = 220f };
                v.Priority = priority;
                if (lookAt != null)
                {
                    v.LookAt = lookAt;
                    var aim = go.AddComponent<CinemachineRotationComposer>();
                    aim.Damping = Vector2.zero;
                }
                if (noise != null && shake > 0f)
                {
                    var n = go.AddComponent<CinemachineBasicMultiChannelPerlin>();
                    n.NoiseProfile = noise;
                    n.AmplitudeGain = shake;
                    n.FrequencyGain = 0.35f;
                }
                return v;
            }
            // Closer and lower than before so the car dominates; framed a little left of centre, clear of the specs panel.
            var hero = V("CM_Hero", new Vector3(4.9f, 1.3f, 6.9f), 27f, 12, 0.35f, focus);
            var heroAim = hero.GetComponent<CinemachineRotationComposer>();
            if (heroAim != null)
            {
                var composition = heroAim.Composition;
                composition.ScreenPosition = new Vector2(-0.07f, 0.04f);
                heroAim.Composition = composition;
            }
            var swap = V("CM_Swap", new Vector3(-7.4f, 0.75f, 4.6f), 26f, 0, 0.25f, focus);
            var departTarget = new GameObject("DepartLook").transform;
            departTarget.position = new Vector3(0f, 1.1f, HallBack - 10f);
            var depart = V("CM_Depart", new Vector3(1.6f, 1.05f, 6.2f), 38f, 0, 0.5f, departTarget);
            var openingLook = new GameObject("OpeningLook").transform;
            openingLook.position = OpeningLook;
            var opening = V("CM_Opening", OpeningPosition, OpeningFov, 0, 0.2f, openingLook);
            // The pull-back from the headlight is a long, slow move.
            var blends = ScriptableObject.CreateInstance<CinemachineBlenderSettings>();
            blends.CustomBlends = new[]
            {
                new CinemachineBlenderSettings.CustomBlend { From = "CM_Opening", To = "CM_Hero", Blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2.4f) },
                new CinemachineBlenderSettings.CustomBlend { From = "CM_Hero", To = "CM_Depart", Blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1.6f) },
                new CinemachineBlenderSettings.CustomBlend { From = "CM_Hero", To = "CM_Swap", Blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.45f) },
                new CinemachineBlenderSettings.CustomBlend { From = "CM_Swap", To = "CM_Hero", Blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1.3f) },
            };
            string blendPath = "Assets/_NeonRift/Settings/Rendering/Garage_CameraBlends.asset";
            AssetDatabase.DeleteAsset(blendPath);
            AssetDatabase.CreateAsset(blends, blendPath);
            brain.CustomBlends = blends;
            return (hero, swap, depart, opening);
        }

        private static void ConfigureRendering()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.17f, 0.21f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.11f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.03f, 0.032f, 0.04f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 1f;
        }

        private static void Probe()
        {
            var go = new GameObject("ReflectionProbe");
            go.transform.position = new Vector3(0f, 2.5f, 0f);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Baked;
            p.boxProjection = true;
            p.size = new Vector3(HallHalfWidth * 2f + 0.5f, HallHeight + 0.5f, HallFront - HallBack + 0.5f);
            p.center = new Vector3(0f, HallHeight * 0.5f - 2.5f, (HallFront + HallBack) * 0.5f);
            p.resolution = 256;
            p.hdr = true;
            p.cullingMask = ~(1 << LayerMask.NameToLayer("Vehicle"));
        }

        private static void BakeProbe()
        {
            var probe = Object.FindAnyObjectByType<ReflectionProbe>();
            if (probe == null) return;
            VehiclePrefabBuilder.EnsureFolder("Assets/_NeonRift/Scenes/CarSelect");
            Lightmapping.BakeReflectionProbe(probe, "Assets/_NeonRift/Scenes/CarSelect/GarageProbe.exr");
            AssetDatabase.ImportAsset("Assets/_NeonRift/Scenes/CarSelect/GarageProbe.exr");
            // Custom mode keeps the cubemap reference in the scene (this scene has no lighting data asset).
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>("Assets/_NeonRift/Scenes/CarSelect/GarageProbe.exr");
        }

        /// <summary>Showroom grade: ACES, bloom on the strips and LEDs, a touch of vignette and grain, shallow far blur.</summary>
        private static void Volume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            foreach (var c in profile.components.ToArray()) { profile.Remove(c.GetType()); Object.DestroyImmediate(c, true); }
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.4f);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(true);
            var colour = Add<ColorAdjustments>();
            colour.postExposure.Override(-0.1f);
            colour.contrast.Override(14f);
            colour.saturation.Override(-6f);
            var wb = Add<WhiteBalance>();
            wb.temperature.Override(-5f);
            var smh = Add<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.96f, 0.99f, 1.06f, 0f));
            smh.highlights.Override(new Vector4(1.03f, 1.0f, 0.97f, 0f));
            var vignette = Add<Vignette>();
            vignette.intensity.Override(0.34f);
            vignette.smoothness.Override(0.45f);
            var dof = Add<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(13f);
            dof.gaussianEnd.Override(32f);
            dof.gaussianMaxRadius.Override(1f);
            dof.highQualitySampling.Override(true);
            var grain = Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.14f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("PostProcess");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.sharedProfile = profile;
        }

        private static void Ui(VehicleShowroom showroom)
        {
            var go = new GameObject("CarSelectScreen");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_NeonRift/UI/NeonRiftPanelSettings.asset");
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_NeonRift/UI/CarSelect.uxml");
            var screen = go.AddComponent<CarSelectScreen>();
            var so = new SerializedObject(screen);
            so.FindProperty("showroom").objectReferenceValue = showroom;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
