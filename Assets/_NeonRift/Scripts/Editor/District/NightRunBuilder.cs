using System.Collections.Generic;
using System.Linq;
using System.Text;
using NeonRift.Audio;
using NeonRift.EditorTools.Audio;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Game;
using NeonRift.Gameplay;
using NeonRift.Missions;
using NeonRift.World;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Builds the Night Run vertical slice: the Sector 7 district (via <see cref="NightRunDistrict"/>), the Data Core
    /// compound, security gates, the extraction tunnel, lighting and post-processing, the HUD, the mission data
    /// (Mission_NightRun + interaction definitions + audio set) and the project wiring (GameConfig, build scenes).
    /// Re-runnable: the scene is regenerated from code each time; assets are updated in place so GUIDs stay stable.
    /// </summary>
    public static class NightRunBuilder
    {
        public const string ScenePath = "Assets/_NeonRift/Scenes/NightRun.unity";
        public const string MissionPath = "Assets/_NeonRift/Data/Missions/Mission_NightRun.asset";
        private const string DataFolder = "Assets/_NeonRift/Data/Missions";
        private const string RenderingFolder = "Assets/_NeonRift/Settings/Rendering";

        // Mission ids (shared by data and scene objects).
        public const string CoreZoneId = "core_compound";
        public const string CoreUplinkId = "core_uplink";
        public const string GateTerminalId = "alley_gate_terminal";
        public const string ExtractionId = "extraction";
        public const string EventLockdown = "lockdown";
        public const string EventBreached = "core.breached";
        public const string EventAlleyOpen = "alley.gate.open";
        public const string EventStart = "mission.start";

        public static readonly Vector3 CorePosition = new(160f, 0f, 127.5f);
        public static readonly Vector3 SpawnPosition = new(3.5f, 0f, -292f);

        private sealed class Context
        {
            public Scene Scene;
            public DistrictKit Kit;
            public NightRunDistrict District;
            public AudioMixerConfig Mixer;
            public MissionAudioGenerator.Clips Audio;
            public Transform Gameplay, Security, Lighting;
            public int Environment, Trigger, Drivable;
            public readonly List<Light> MissionLights = new();
            public readonly StringBuilder Log = new();
        }

        [MenuItem("Neon Rift/Night Run/Build Night Run (district + mission)")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(Build(bakeProbes: true));
        }

        public static string Build(bool bakeProbes)
        {
            var c = new Context
            {
                Environment = LayerMask.NameToLayer("Environment"),
                Trigger = LayerMask.NameToLayer("Trigger"),
                Drivable = LayerMask.NameToLayer("Drivable")
            };
            c.Log.AppendLine("[NightRun] build");
            // Open the fresh scene first: switching scenes unloads unreferenced assets loaded before it.
            c.Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            c.Mixer = AssetDatabase.LoadAssetAtPath<AudioMixerConfig>("Assets/_NeonRift/Data/Audio/AudioMixerConfig.asset");
            c.Log.Append(MissionAudioGenerator.Generate(out c.Audio));
            c.Kit = new DistrictKit(DistrictTextures.Generate());
            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:BuildingCatalog")[0]));
            var mission = CreateMissionData(out var hackGate, out var uplink);

            var environment = Root(c, "Environment");
            c.Lighting = Root(c, "Lighting").transform;
            c.Gameplay = Root(c, "Gameplay").transform;
            c.Security = new GameObject("Security").transform;
            c.Security.SetParent(c.Gameplay, false);

            c.District = new NightRunDistrict(c.Kit, catalog);
            c.District.Build(environment.transform);

            var core = BuildCompound(c, uplink, out var coreUplink);
            BuildAlleyGate(c, hackGate);
            BuildBarrier(c, "CompoundGate", "COMPOUND GATE", new Vector3(160f, 0f, 194.4f), 0f, 12f, 2, closedAtStart: false,
                         closeOn: new[] { EventLockdown }, openOn: null, delay: 12f, heatPenalty: 4f, minDelay: 7f, warning: 1.5f, travel: 2f);
            BuildBarrier(c, "ExpresswayCheckpoint", "EXPRESSWAY CHECKPOINT", new Vector3(320f, 0f, 170f), 0f, 18f, 2, closedAtStart: false,
                         closeOn: new[] { EventLockdown }, openOn: null, delay: 30f, heatPenalty: 12f, minDelay: 12f, warning: 2.5f, travel: 3f);
            BuildExtraction(c);
            BuildSecurityGroups(c);
            BuildCityAlarm(c, core);
            var volumes = BuildLighting(c);

            var (entry, director, camera, chase) = BuildMissionRig(c, core, volumes);
            BuildDevTools(c, entry);

            Lightmapping.lightingSettings = LightingSettingsAsset();
            EditorSceneManager.SaveScene(c.Scene, ScenePath);
            if (bakeProbes) BakeProbes(c);
            EditorSceneManager.SaveScene(c.Scene, ScenePath);
            RegisterInProject(mission, c);

            c.Log.AppendLine($"  podiums {c.District.Podiums}, street lights (real-time) {c.District.RealtimeLights}, mission lights {c.MissionLights.Count}");
            c.Log.AppendLine($"  scene saved: {ScenePath}");
            return c.Log.ToString();
        }

        private static GameObject Root(Context c, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, c.Scene);
            return go;
        }

        // ---------------- Mission data ----------------

        private static MissionDefinition CreateMissionData(out InteractionDefinition hackGate, out InteractionDefinition uplink)
        {
            VehiclePrefabBuilder.EnsureFolder(DataFolder);
            hackGate = Asset<InteractionDefinition>($"{DataFolder}/Interaction_HackGate.asset");
            hackGate.EditorConfigure("HACK", 2.5f, 10f, 0.6f, 0.35f, "GATE BREACH LOGGED", "GATE OVERRIDDEN");
            uplink = Asset<InteractionDefinition>($"{DataFolder}/Interaction_DataCoreUplink.asset");
            uplink.EditorConfigure("EXTRACT", 4f, 8f, 0.35f, 0f, "CORE ACCESS LOGGED", "DATA CORE SECURED");
            EditorUtility.SetDirty(hackGate);
            EditorUtility.SetDirty(uplink);

            var mission = Asset<MissionDefinition>(MissionPath);
            mission.EditorConfigure("night_run", "Night Run", "INFILTRATE SECTOR 7  ·  STEAL THE DATA CORE  ·  GET OUT",
                "Sector 7's Data Core sits in a walled compound at the heart of the district. The boulevard route is long " +
                "but clean; the service alley is short, but hacking its gate logs an intrusion. Take the core and the grid " +
                "locks the district down: gates seal, the trace starts. Reach the Rift Gate in the south-east before it finds you.",
                "NightRun",
                new List<ObjectiveDefinition>
                {
                    new("reach_core", ObjectiveKind.Reach, CoreZoneId, "REACH THE DATA CORE",
                        "Boulevard: long and clean.  Service alley: short, but its gate logs intrusions."),
                    new("hack_core", ObjectiveKind.Interact, CoreUplinkId, "BREACH THE DATA CORE",
                        "Stop on the uplink ring and hold E to extract the core.", SecurityLevel.Calm, 0f, null,
                        null, new[] { EventBreached, EventLockdown }),
                    new("escape", ObjectiveKind.Reach, ExtractionId, "ESCAPE TO THE RIFT GATE",
                        "The district is sealing. Beat the checkpoints or hack your way out.", SecurityLevel.Lockdown, 80f,
                        "TRACE COMPLETE — YOU WERE FOUND", new[] { "escape.start" })
                },
                new[] { EventStart }, new[] { "mission.complete" }, new[] { "mission.failed" },
                new List<MissionAnnouncement>
                {
                    new(EventStart, "SECTOR 7 GRID ONLINE  ·  FOLLOW THE CYAN BEACON", MessageTone.Info, 4.5f),
                    new(EventAlleyOpen, "ALLEY GATE OPEN", MessageTone.Success, 0.2f),
                    new(EventBreached, "THEFT DETECTED|SECTOR 7 LOCKDOWN INITIATED", MessageTone.Danger, 0.15f, banner: true),
                    new("escape.start", "EXTRACTION: RIFT GATE, SOUTH-EAST TUNNEL", MessageTone.Info, 4f),
                    new("escape.start", "ALLEY GATE CAN BE RE-HACKED  ·  COSTS HEAT", MessageTone.Warning, 7f),
                },
                timePenalty: 25f, minimumTime: 30f);
            EditorUtility.SetDirty(mission);
            AssetDatabase.SaveAssets();
            return mission;
        }

        private static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        // ---------------- Data Core compound ----------------

        private static Transform BuildCompound(Context c, InteractionDefinition uplinkDefinition, out Interactable uplink)
        {
            var kit = c.Kit;
            var root = new GameObject("DataCoreCompound").transform;
            root.SetParent(c.Gameplay, false);

            var plaza = new MeshBuilder();
            plaza.Ground(new Vector3(106f, 0.02f, 61f), new Vector3(214f, 0.02f, 194f), 4f);
            DistrictKit.Renderer("Plaza", root, DistrictKit.SaveMesh(plaza, "Compound_Plaza"), kit.Plaza, c.Drivable, shadows: false);

            // Walls with openings for the access road (north) and the alley (south).
            var walls = new MeshBuilder();
            var strips = new MeshBuilder();
            var wallBoxes = new[]
            {
                (new Vector3(105f, 0f, 60f), new Vector3(155f, 4.5f, 61.2f)), (new Vector3(165f, 0f, 60f), new Vector3(215f, 4.5f, 61.2f)),
                (new Vector3(105f, 0f, 193.8f), new Vector3(154f, 4.5f, 195f)), (new Vector3(166f, 0f, 193.8f), new Vector3(215f, 4.5f, 195f)),
                (new Vector3(105f, 0f, 60f), new Vector3(106.2f, 4.5f, 195f)), (new Vector3(213.8f, 0f, 60f), new Vector3(215f, 4.5f, 195f)),
            };
            var colliders = new GameObject("WallColliders").transform;
            colliders.SetParent(root, false);
            foreach (var (min, max) in wallBoxes)
            {
                walls.Cuboid(min, max, 2f);
                strips.Cuboid(new Vector3(min.x - 0.05f, max.y - 0.25f, min.z - 0.05f), new Vector3(max.x + 0.05f, max.y - 0.05f, max.z + 0.05f), 1f);
                var col = new GameObject("Wall") { layer = c.Environment };
                col.transform.SetParent(colliders, false);
                var box = col.AddComponent<BoxCollider>();
                box.center = (min + max) * 0.5f;
                box.size = max - min;
            }
            DistrictKit.Renderer("Walls", root, DistrictKit.SaveMesh(walls, "Compound_Walls"), kit.Metal, c.Environment);
            var wallStrips = DistrictKit.Renderer("WallStrips", root, DistrictKit.SaveMesh(strips, "Compound_WallStrips"), kit.Security, c.Environment, shadows: false);
            c.District.SecurityStrips["Compound"] = wallStrips.GetComponent<Renderer>();

            // The core: pedestal, glowing column, frame and three counter-rotating rings.
            var core = new GameObject("DataCore").transform;
            core.SetParent(root, false);
            core.position = CorePosition;
            var pedestal = new MeshBuilder();
            pedestal.Cylinder(Vector3.zero, 6f, 1.2f, 32, true);
            pedestal.Cylinder(Vector3.up * 1.2f, 3.2f, 0.6f, 24, true);
            var pedestalMesh = DistrictKit.SaveMesh(pedestal, "DataCore_Pedestal");
            var pedestalGo = DistrictKit.Renderer("Pedestal", core, pedestalMesh, kit.Metal, c.Environment);
            var pedestalCol = pedestalGo.AddComponent<MeshCollider>();
            pedestalCol.sharedMesh = pedestalMesh;
            pedestalCol.convex = true;
            var column = new MeshBuilder();
            column.Cylinder(Vector3.up * 1.8f, 1.6f, 24f, 20, true);
            var glow = new List<Renderer> { DistrictKit.Renderer("Column", core, DistrictKit.SaveMesh(column, "DataCore_Column"), kit.CoreGlow, c.Environment, shadows: false).GetComponent<Renderer>() };
            var frame = new MeshBuilder();
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward;
                frame.OrientedBox(dir * 3.6f + Vector3.up * 13.5f, new Vector3(0.5f, 24f, 0.5f), Quaternion.LookRotation(dir), 1f);
            }
            frame.Torus(Vector3.up * 25.6f, 3.6f, 0.3f, 32, 8);
            DistrictKit.Renderer("Frame", core, DistrictKit.SaveMesh(frame, "DataCore_Frame"), kit.Metal, c.Environment);
            var rings = new List<Transform>();
            float[] ringHeights = { 7f, 13f, 19f };
            float[] ringRadii = { 5.2f, 6.2f, 4.8f };
            for (int i = 0; i < 3; i++)
            {
                var ring = new MeshBuilder();
                ring.Torus(Vector3.zero, ringRadii[i], 0.2f, 48, 8);
                for (int k = 0; k < 6; k++)
                {
                    var d = Quaternion.Euler(0f, k * 60f, 0f) * Vector3.forward;
                    ring.OrientedBox(d * ringRadii[i], new Vector3(0.8f, 0.5f, 0.8f), Quaternion.LookRotation(d), 1f);
                }
                var ringGo = DistrictKit.Renderer($"Ring{i}", core, DistrictKit.SaveMesh(ring, $"DataCore_Ring{i}"), kit.CoreGlow, c.Environment, shadows: false);
                GameObjectUtility.SetStaticEditorFlags(ringGo, 0);
                ringGo.transform.localPosition = Vector3.up * ringHeights[i];
                ringGo.transform.localRotation = Quaternion.Euler(i == 1 ? 8f : -6f, 0f, i == 2 ? 10f : 0f);
                rings.Add(ringGo.transform);
                glow.Add(ringGo.GetComponent<Renderer>());
            }
            var coreLight = Light(c, core, "CoreLight", LightType.Point, new Vector3(0f, 10f, 0f), Quaternion.identity, new Color(0.4f, 0.9f, 1f), 60f, 45f);

            // Uplink ring: the interaction zone around the core.
            var uplinkGo = new GameObject("CoreUplink") { layer = c.Trigger };
            uplinkGo.transform.SetParent(core, false);
            var ringMesh = new MeshBuilder();
            ringMesh.Ring(Vector3.up * 0.05f, 8.5f, 12.5f, 64);
            var ringRenderer = DistrictKit.Renderer("UplinkRing", uplinkGo.transform, DistrictKit.SaveMesh(ringMesh, "DataCore_UplinkRing"), kit.Indicator, c.Environment, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(ringRenderer, 0);
            var trigger = uplinkGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 3f, 0f);
            trigger.size = new Vector3(30f, 6f, 30f);
            uplink = uplinkGo.AddComponent<Interactable>();
            var uplinkBeacon = Beacon(c, uplinkGo.transform, Vector3.zero, kit.BeaconCyan, 3f);
            uplink.EditorConfigure(CoreUplinkId, uplinkDefinition, "DATA CORE", 9f, true, null, null, null, null,
                                   new[] { ringRenderer.GetComponent<Renderer>() }, uplinkBeacon);

            // Reach zone: the whole compound.
            var zoneGo = new GameObject("CompoundZone") { layer = c.Trigger };
            zoneGo.transform.SetParent(root, false);
            zoneGo.transform.position = CorePosition + Vector3.up * 4f;
            var zoneBox = zoneGo.AddComponent<BoxCollider>();
            zoneBox.isTrigger = true;
            zoneBox.size = new Vector3(106f, 8f, 131f);
            var zone = zoneGo.AddComponent<MissionZone>();
            zone.EditorConfigure(CoreZoneId, "DATA CORE", 12f, Beacon(c, zoneGo.transform, CorePosition, kit.BeaconCyan, 3f));

            var visual = core.gameObject.AddComponent<DataCoreVisual>();
            visual.EditorConfigure(uplink, rings.ToArray(), glow.ToArray(), coreLight);

            // Corner towers with lockdown beacons, and floodlights on the core.
            var alarm = new GameObject("CompoundAlarm").transform;
            alarm.SetParent(root, false);
            var heads = new List<Transform>();
            var beaconLights = new List<Light>();
            var towers = new MeshBuilder();
            foreach (var p in new[] { new Vector3(108f, 0f, 63f), new Vector3(212f, 0f, 63f), new Vector3(108f, 0f, 192f), new Vector3(212f, 0f, 192f) })
            {
                towers.OrientedBox(p + Vector3.up * 5f, new Vector3(0.9f, 10f, 0.9f), Quaternion.identity, 1f);
                var b = DistrictKit.Place(kit.Beacon, alarm, p + Vector3.up * 10f, Quaternion.identity);
                heads.Add(b.transform.Find("Head"));
                beaconLights.AddRange(b.GetComponentsInChildren<Light>(true));
                b.transform.Find("Head").gameObject.SetActive(false);   // lockdown only
            }
            DistrictKit.Renderer("Towers", alarm, DistrictKit.SaveMesh(towers, "Compound_Towers"), kit.Metal, c.Environment);
            var alarmComponent = alarm.gameObject.AddComponent<SecurityAlarm>();
            alarmComponent.EditorConfigure(new AudioSource[0], heads.ToArray(), beaconLights.ToArray());
            c.MissionLights.AddRange(beaconLights);

            Light(c, root, "Flood_NW", LightType.Spot, new Vector3(112f, 9f, 188f), Quaternion.LookRotation(CorePosition + Vector3.up * 6f - new Vector3(112f, 9f, 188f)), new Color(0.7f, 0.9f, 1f), 1800f, 140f, 40f);
            Light(c, root, "Flood_SE", LightType.Spot, new Vector3(208f, 9f, 66f), Quaternion.LookRotation(CorePosition + Vector3.up * 6f - new Vector3(208f, 9f, 66f)), new Color(0.7f, 0.9f, 1f), 1800f, 140f, 40f);
            return core;
        }

        private static GameObject Beacon(Context c, Transform parent, Vector3 worldBase, Material material, float radius)
        {
            var pillar = new MeshBuilder();
            pillar.Cylinder(Vector3.zero, radius, 420f, 24, false);
            pillar.Cylinder(Vector3.zero, radius * 0.35f, 420f, 12, false);
            var mesh = DistrictKit.SaveMesh(pillar, $"Beacon_r{radius:0.0}");
            var go = DistrictKit.Renderer("Beacon", parent, mesh, material, 0, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            go.transform.position = worldBase == Vector3.zero ? parent.position : worldBase;
            go.transform.position = new Vector3(go.transform.position.x, 0f, go.transform.position.z);
            go.SetActive(false);   // shown at runtime only while it is the current objective
            return go;
        }

        private static Light Light(Context c, Transform parent, string name, LightType type, Vector3 position, Quaternion rotation, Color colour,
                                   float intensity, float range, float spotAngle = 60f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = spotAngle;
            l.innerSpotAngle = spotAngle * 0.6f;
            l.shadows = LightShadows.None;
            c.MissionLights.Add(l);
            return l;
        }

        // ---------------- Gates ----------------

        private static void BuildAlleyGate(Context c, InteractionDefinition hackGate)
        {
            var barrier = BuildBarrier(c, "AlleyGate", "ALLEY GATE", new Vector3(160f, 0f, -20f), 0f, 10f, 1, closedAtStart: true,
                                       closeOn: new[] { EventLockdown }, openOn: new[] { EventAlleyOpen }, delay: 0f, heatPenalty: 0f, minDelay: 0f,
                                       warning: 1f, travel: 1.5f);

            // Terminal kiosk on the west pavement, facing the alley.
            var kiosk = new GameObject("GateTerminal");
            kiosk.transform.SetParent(barrier.transform, false);
            kiosk.transform.position = new Vector3(154.3f, KerbY, -27f);
            kiosk.transform.rotation = Quaternion.LookRotation(Vector3.right);
            var body = new MeshBuilder();
            body.OrientedBox(new Vector3(0f, 0.85f, 0f), new Vector3(0.9f, 1.7f, 0.5f), Quaternion.identity, 1f);
            DistrictKit.Renderer("Body", kiosk.transform, DistrictKit.SaveMesh(body, "Prop_Terminal_Body"), c.Kit.Metal, c.Environment);
            var screen = new MeshBuilder();
            screen.OrientedBox(new Vector3(0f, 1.25f, 0.26f), new Vector3(0.7f, 0.5f, 0.03f), Quaternion.Euler(-12f, 0f, 0f), 1f);
            screen.OrientedBox(new Vector3(0f, 1.78f, 0f), new Vector3(0.9f, 0.08f, 0.5f), Quaternion.identity, 1f);
            var screenGo = DistrictKit.Renderer("Screen", kiosk.transform, DistrictKit.SaveMesh(screen, "Prop_Terminal_Screen"), c.Kit.Indicator, c.Environment, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(screenGo, 0);
            Light(c, kiosk.transform, "ScreenGlow", LightType.Point, kiosk.transform.position + Vector3.up * 1.4f + Vector3.right * 0.8f, Quaternion.identity,
                  new Color(0.3f, 0.9f, 1f), 3f, 6f);

            var zoneGo = new GameObject("TerminalZone") { layer = c.Trigger };
            zoneGo.transform.SetParent(barrier.transform, false);
            zoneGo.transform.position = new Vector3(160f, 2.5f, -20f);
            var box = zoneGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(10f, 5f, 46f);
            var interactable = zoneGo.AddComponent<Interactable>();
            interactable.EditorConfigure(GateTerminalId, hackGate, "ALLEY GATE", 3f, true, new[] { EventAlleyOpen }, null, null, new[] { EventLockdown },
                                         new[] { screenGo.GetComponent<Renderer>() }, null);
        }

        private const float KerbY = NightRunDistrict.KerbHeight;

        /// <summary>
        /// A gantry with sliding panels across a road. <paramref name="yaw"/> 0 means the panels span X. Panels slide
        /// sideways into the kerbside structures when opening.
        /// </summary>
        private static SecurityBarrier BuildBarrier(Context c, string name, string label, Vector3 centre, float yaw, float width, int panelCount,
                                                    bool closedAtStart, string[] closeOn, string[] openOn, float delay, float heatPenalty,
                                                    float minDelay, float warning, float travel)
        {
            var kit = c.Kit;
            var root = new GameObject(name).transform;
            root.SetParent(c.Security, false);
            root.SetPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));

            const float gantryHeight = 6.5f;
            const float panelHeight = 3.4f;
            var frame = new MeshBuilder();
            foreach (float s in new[] { -1f, 1f })
                frame.OrientedBox(new Vector3(s * (width * 0.5f + 0.6f), gantryHeight * 0.5f, 0f), new Vector3(0.7f, gantryHeight, 0.9f), Quaternion.identity, 1f);
            frame.OrientedBox(new Vector3(0f, gantryHeight, 0f), new Vector3(width + 2.6f, 0.8f, 1f), Quaternion.identity, 1f);
            var frameGo = DistrictKit.Renderer("Gantry", root, DistrictKit.SaveMesh(frame, $"{name}_Gantry"), kit.Metal, c.Environment);
            foreach (float s in new[] { -1f, 1f })
            {
                var postCol = new GameObject("Post") { layer = c.Environment };
                postCol.transform.SetParent(root, false);
                postCol.transform.localPosition = new Vector3(s * (width * 0.5f + 0.6f), gantryHeight * 0.5f, 0f);
                postCol.AddComponent<BoxCollider>().size = new Vector3(0.7f, gantryHeight, 0.9f);
            }

            var warningStrip = new MeshBuilder();
            warningStrip.OrientedBox(new Vector3(0f, gantryHeight - 0.25f, 0.52f), new Vector3(width + 2.2f, 0.18f, 0.06f), Quaternion.identity, 1f);
            warningStrip.OrientedBox(new Vector3(0f, gantryHeight - 0.25f, -0.52f), new Vector3(width + 2.2f, 0.18f, 0.06f), Quaternion.identity, 1f);
            var renderers = new List<Renderer>
            {
                DistrictKit.Renderer("WarningStrip", root, DistrictKit.SaveMesh(warningStrip, $"{name}_WarningStrip"), kit.BarrierWarning, c.Environment, shadows: false).GetComponent<Renderer>()
            };
            GameObjectUtility.SetStaticEditorFlags(renderers[0].gameObject, 0);

            var panels = new List<SecurityBarrier.Panel>();
            float panelWidth = width / panelCount + 0.2f;
            for (int i = 0; i < panelCount; i++)
            {
                float side = panelCount == 1 ? 1f : i == 0 ? -1f : 1f;
                float closedX = panelCount == 1 ? 0f : side * width * 0.25f;
                var panel = new GameObject($"Panel{i}") { layer = c.Environment };
                panel.transform.SetParent(root, false);
                panel.transform.localPosition = new Vector3(closedX, 0f, 0f);
                var body = new MeshBuilder();
                body.OrientedBox(new Vector3(0f, panelHeight * 0.5f + 0.05f, 0f), new Vector3(panelWidth, panelHeight, 0.45f), Quaternion.identity, 1f);
                for (int k = 0; k < 4; k++)
                    body.OrientedBox(new Vector3(0f, 0.5f + k * 0.8f, 0f), new Vector3(panelWidth + 0.02f, 0.08f, 0.5f), Quaternion.identity, 1f);
                var bodyGo = DistrictKit.Renderer("Body", panel.transform, DistrictKit.SaveMesh(body, $"{name}_Panel"), kit.Metal, c.Environment);
                GameObjectUtility.SetStaticEditorFlags(bodyGo, 0);
                var edge = new MeshBuilder();
                edge.OrientedBox(new Vector3(0f, panelHeight + 0.1f, 0f), new Vector3(panelWidth, 0.12f, 0.5f), Quaternion.identity, 1f);
                edge.OrientedBox(new Vector3(-side * (panelWidth * 0.5f), panelHeight * 0.5f, 0f), new Vector3(0.12f, panelHeight, 0.5f), Quaternion.identity, 1f);
                var edgeGo = DistrictKit.Renderer("Edge", panel.transform, DistrictKit.SaveMesh(edge, $"{name}_PanelEdge{i}"), kit.BarrierWarning, c.Environment, shadows: false);
                GameObjectUtility.SetStaticEditorFlags(edgeGo, 0);
                renderers.Add(edgeGo.GetComponent<Renderer>());
                var col = panel.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, panelHeight * 0.5f + 0.05f, 0f);
                col.size = new Vector3(panelWidth, panelHeight, 0.45f);
                var rb = panel.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                panels.Add(new SecurityBarrier.Panel { body = rb, openOffset = new Vector3(side * (panelWidth + 0.4f), 0f, 0f) });
            }

            var warningLights = new[]
            {
                Light(c, root, "WarningLightA", LightType.Spot, root.TransformPoint(new Vector3(-width * 0.3f, gantryHeight - 0.5f, 1.2f)),
                      root.rotation * Quaternion.Euler(55f, 0f, 0f), new Color(1f, 0.12f, 0.2f), 0f, 22f, 100f),
                Light(c, root, "WarningLightB", LightType.Spot, root.TransformPoint(new Vector3(width * 0.3f, gantryHeight - 0.5f, -1.2f)),
                      root.rotation * Quaternion.Euler(125f, 0f, 0f), new Color(1f, 0.12f, 0.2f), 0f, 22f, 100f),
            };
            foreach (var l in warningLights) l.enabled = false;

            var source = root.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 10f;
            source.maxDistance = 260f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            source.outputAudioMixerGroup = c.Mixer != null ? c.Mixer.Sfx : null;

            var barrier = root.gameObject.AddComponent<SecurityBarrier>();
            barrier.EditorConfigure(label, closedAtStart, closeOn, openOn, delay, heatPenalty, minDelay, warning, travel, panels.ToArray(),
                                    renderers.ToArray(), warningLights, source, c.Audio.Klaxon, c.Audio.Motor, c.Audio.Slam);
            return barrier;
        }

        // ---------------- Extraction ----------------

        private static void BuildExtraction(Context c)
        {
            var kit = c.Kit;
            var root = new GameObject("RiftGateExtraction").transform;
            root.SetParent(c.Gameplay, false);
            var walls = new MeshBuilder();
            var boxes = new[]
            {
                (new Vector3(309.5f, 0f, -472f), new Vector3(311f, 9f, -405f)), (new Vector3(329f, 0f, -472f), new Vector3(330.5f, 9f, -405f)),
                (new Vector3(309.5f, 9f, -472f), new Vector3(330.5f, 10.2f, -405f)), (new Vector3(309.5f, 0f, -473f), new Vector3(330.5f, 10.2f, -471.5f)),
                (new Vector3(307.5f, 0f, -407f), new Vector3(311f, 12f, -403f)), (new Vector3(329f, 0f, -407f), new Vector3(332.5f, 12f, -403f)),
                (new Vector3(307.5f, 10.2f, -407f), new Vector3(332.5f, 13.5f, -403f)),
            };
            foreach (var (min, max) in boxes)
            {
                walls.Cuboid(min, max, 2f);
                var col = new GameObject("TunnelCollider") { layer = c.Environment };
                col.transform.SetParent(root, false);
                var box = col.AddComponent<BoxCollider>();
                box.center = (min + max) * 0.5f;
                box.size = max - min;
            }
            DistrictKit.Renderer("Tunnel", root, DistrictKit.SaveMesh(walls, "Extraction_Tunnel"), kit.TunnelWall, c.Environment);

            var neon = new MeshBuilder();
            foreach (float x in new[] { 311.05f, 328.95f })
                foreach (float y in new[] { 1.1f, 7.6f })
                    neon.OrientedBox(new Vector3(x, y, -438f), new Vector3(0.08f, 0.12f, 64f), Quaternion.identity, 1f);
            // Portal outline.
            neon.OrientedBox(new Vector3(320f, 13.6f, -402.9f), new Vector3(25f, 0.25f, 0.2f), Quaternion.identity, 1f);
            neon.OrientedBox(new Vector3(307.4f, 6.75f, -402.9f), new Vector3(0.25f, 13.5f, 0.2f), Quaternion.identity, 1f);
            neon.OrientedBox(new Vector3(332.6f, 6.75f, -402.9f), new Vector3(0.25f, 13.5f, 0.2f), Quaternion.identity, 1f);
            DistrictKit.Renderer("PortalNeon", root, DistrictKit.SaveMesh(neon, "Extraction_Neon"), kit.NeonStrips[1], c.Environment, shadows: false);

            var sign = new MeshBuilder();
            var uv = DistrictTextures.SignRect("RIFT GATE", out float aspect);
            sign.Panel(new Vector3(320f, 11.85f, -402.85f), Quaternion.LookRotation(Vector3.forward), 2.2f * aspect, 2.2f, uv);
            var uv2 = DistrictTextures.SignRect("EXTRACTION", out float aspect2);
            sign.Panel(new Vector3(320f, 10.6f, -402.85f) + Vector3.down * 0.0f, Quaternion.LookRotation(Vector3.forward), 0.9f * aspect2, 0.9f, uv2);
            DistrictKit.Renderer("PortalSign", root, DistrictKit.SaveMesh(sign, "Extraction_Sign"), kit.Signs[1], c.Environment, shadows: false);

            // The "rift" at the end of the tunnel: a wall of additive glow.
            var rift = new MeshBuilder();
            rift.Panel(new Vector3(320f, 4.5f, -471.3f), Quaternion.LookRotation(Vector3.forward), 17.5f, 9f, new Rect(0f, 0f, 1f, 1f));
            var riftMat = DistrictKit.Lit("District_RiftWall", Color.black, 0.9f, 0f, emission: new Color(2.2f, 0.4f, 2.8f));
            DistrictKit.Renderer("Rift", root, DistrictKit.SaveMesh(rift, "Extraction_Rift"), riftMat, c.Environment, shadows: false);
            for (int i = 0; i < 3; i++)
                Light(c, root, $"TunnelLight{i}", LightType.Point, new Vector3(320f, 6.5f, -415f - i * 20f), Quaternion.identity, new Color(1f, 0.3f, 0.9f), 14f, 18f);

            var zoneGo = new GameObject("ExtractionZone") { layer = c.Trigger };
            zoneGo.transform.SetParent(root, false);
            zoneGo.transform.position = new Vector3(320f, 4f, -435f);
            var zoneBox = zoneGo.AddComponent<BoxCollider>();
            zoneBox.isTrigger = true;
            zoneBox.size = new Vector3(18f, 8f, 40f);
            var zone = zoneGo.AddComponent<MissionZone>();
            zone.EditorConfigure(ExtractionId, "RIFT GATE", 0f, Beacon(c, zoneGo.transform, new Vector3(320f, 0f, -397f), kit.BeaconMagenta, 3.5f));
        }

        // ---------------- Security presentation ----------------

        private static void BuildSecurityGroups(Context c)
        {
            var parent = new GameObject("SecurityLights").transform;
            parent.SetParent(c.Security, false);
            foreach (var pair in c.District.SecurityStrips)
            {
                var go = new GameObject($"Group_{pair.Key}");
                go.transform.SetParent(parent, false);
                var block = NightRunDistrict.Blocks.FirstOrDefault(b => b.Name == pair.Key);
                Vector3 centre = block.Name == null ? CorePosition
                    : block.Clip.width > 0f ? new Vector3(block.Clip.center.x, 0f, block.Clip.center.y) : block.Centre;
                go.transform.position = centre;
                go.AddComponent<SecurityLightGroup>().EditorConfigure(new[] { pair.Value }, null);
            }
        }

        private static void BuildCityAlarm(Context c, Transform core)
        {
            var root = new GameObject("CityAlarm").transform;
            root.SetParent(c.Security, false);
            var sirens = new List<AudioSource>();
            foreach (var p in new[] { CorePosition + Vector3.up * 26f, new Vector3(320f, 18f, 170f), new Vector3(160f, 14f, -20f), new Vector3(0f, 22f, -100f),
                                      new Vector3(320f, 14f, -400f), new Vector3(160f, 30f, 300f) })
            {
                var go = new GameObject("Siren");
                go.transform.SetParent(root, false);
                go.transform.position = p;
                var s = go.AddComponent<AudioSource>();
                s.clip = c.Audio.Siren;
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.minDistance = 40f;
                s.maxDistance = 700f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.dopplerLevel = 0f;
                s.outputAudioMixerGroup = c.Mixer != null ? c.Mixer.Sfx : null;
                sirens.Add(s);
            }
            // Rooftop beacons on the checkpoint and the tunnel portal.
            var heads = new List<Transform>();
            var lights = new List<Light>();
            foreach (var p in new[] { new Vector3(309f, 7.3f, 170f), new Vector3(331f, 7.3f, 170f), new Vector3(308f, 13.6f, -403f), new Vector3(332f, 13.6f, -403f),
                                      new Vector3(153f, 6.9f, 194.4f), new Vector3(167f, 6.9f, 194.4f) })
            {
                var b = DistrictKit.Place(c.Kit.Beacon, root, p, Quaternion.identity);
                heads.Add(b.transform.Find("Head"));
                lights.AddRange(b.GetComponentsInChildren<Light>(true));
                b.transform.Find("Head").gameObject.SetActive(false);   // lockdown only
            }
            c.MissionLights.AddRange(lights);
            root.gameObject.AddComponent<SecurityAlarm>().EditorConfigure(sirens.ToArray(), heads.ToArray(), lights.ToArray());
        }

        // ---------------- Lighting and grading ----------------

        private static (Volume alert, Volume lockdown) BuildLighting(Context c)
        {
            RenderSettings.skybox = c.Kit.NightSky;
            c.Kit.NightSky.SetColor("_HorizonColor", new Color(0.12f, 0.045f, 0.16f));
            c.Kit.NightSky.SetColor("_GlowColor", new Color(0.3f, 0.08f, 0.28f));
            c.Kit.NightSky.SetColor("_ZenithColor", new Color(0.008f, 0.01f, 0.03f));
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.11f, 0.09f, 0.2f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.06f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0021f;
            RenderSettings.fogColor = new Color(0.085f, 0.05f, 0.13f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.8f;

            var moonGo = new GameObject("Moonlight");
            moonGo.transform.SetParent(c.Lighting, false);
            moonGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.62f, 0.95f);
            moon.intensity = 0.28f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.75f;
            RenderSettings.sun = moon;

            var baseProfile = Profile("NightRun_Base", p =>
            {
                var bloom = p.Add<Bloom>(true);
                bloom.threshold.Override(0.95f);
                bloom.intensity.Override(0.85f);
                bloom.scatter.Override(0.72f);
                bloom.highQualityFiltering.Override(true);
                p.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
                var colour = p.Add<ColorAdjustments>(true);
                colour.postExposure.Override(0.45f);
                colour.contrast.Override(14f);
                colour.saturation.Override(12f);
                var vignette = p.Add<Vignette>(true);
                vignette.intensity.Override(0.3f);
                vignette.smoothness.Override(0.45f);
                p.Add<ChromaticAberration>(true).intensity.Override(0.06f);
                var grain = p.Add<FilmGrain>(true);
                grain.type.Override(FilmGrainLookup.Thin1);
                grain.intensity.Override(0.12f);
                p.Add<WhiteBalance>(true).temperature.Override(-8f);
            });
            var alertProfile = Profile("NightRun_Alert", p =>
            {
                p.Add<ColorAdjustments>(true).colorFilter.Override(new Color(1f, 0.92f, 0.8f));
                p.Add<Vignette>(true).intensity.Override(0.36f);
            });
            var lockdownProfile = Profile("NightRun_Lockdown", p =>
            {
                var colour = p.Add<ColorAdjustments>(true);
                colour.colorFilter.Override(new Color(1f, 0.74f, 0.82f));
                colour.saturation.Override(22f);
                var vignette = p.Add<Vignette>(true);
                vignette.intensity.Override(0.44f);
                vignette.color.Override(new Color(0.35f, 0f, 0.06f));
                p.Add<ChromaticAberration>(true).intensity.Override(0.22f);
                p.Add<Bloom>(true).intensity.Override(1.15f);
            });

            Volume Global(string name, VolumeProfile profile, float priority, float weight)
            {
                var go = new GameObject(name);
                go.transform.SetParent(c.Lighting, false);
                var v = go.AddComponent<Volume>();
                v.isGlobal = true;
                v.sharedProfile = profile;
                v.priority = priority;
                v.weight = weight;
                return v;
            }
            Global("PostFX_Night", baseProfile, 0f, 1f);
            var alert = Global("PostFX_Alert", alertProfile, 1f, 0f);
            var lockdown = Global("PostFX_Lockdown", lockdownProfile, 2f, 0f);
            var fx = new GameObject("SecurityPostEffects");
            fx.transform.SetParent(c.Security, false);
            fx.AddComponent<SecurityPostEffects>().EditorConfigure(alert, lockdown);
            return (alert, lockdown);
        }

        private static VolumeProfile Profile(string name, System.Action<VolumeProfile> configure)
        {
            string path = $"{RenderingFolder}/{name}.asset";
            // Updated in place (no delete) so the asset keeps its GUID.
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            foreach (var old in profile.components.ToList()) Object.DestroyImmediate(old, true);
            profile.components.Clear();
            configure(profile);
            foreach (var component in profile.components)
            {
                component.name = component.GetType().Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static LightingSettings LightingSettingsAsset()
        {
            string path = $"{RenderingFolder}/NightRun_Lighting.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null)
            {
                settings = new LightingSettings { name = "NightRun_Lighting" };
                AssetDatabase.CreateAsset(settings, path);
            }
            // Real-time lights + baked reflection probes for now; baked GI comes with the mobile lighting pass.
            settings.bakedGI = false;
            settings.realtimeGI = false;
            settings.autoGenerate = false;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void BakeProbes(Context c)
        {
            int baked = 0;
            foreach (var probe in c.Scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ReflectionProbe>(true)))
            {
                string path = $"Assets/_NeonRift/Scenes/NightRun/{probe.name}.exr";
                VehiclePrefabBuilder.EnsureFolder("Assets/_NeonRift/Scenes/NightRun");
                if (Lightmapping.BakeReflectionProbe(probe, path)) baked++;
            }
            c.Log.AppendLine($"  reflection probes baked: {baked}");
        }

        // ---------------- Mission rig ----------------

        private static (MissionSceneEntry entry, MissionDirector director, Camera camera, VehicleChaseCamera chase) BuildMissionRig(
            Context c, Transform core, (Volume alert, Volume lockdown) volumes)
        {
            var rig = Root(c, "NightRun");
            var entry = rig.AddComponent<MissionSceneEntry>();
            var director = rig.AddComponent<MissionDirector>();

            var spawnGo = new GameObject("PlayerSpawn");
            spawnGo.transform.SetParent(rig.transform, false);
            spawnGo.transform.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
            var spawn = spawnGo.AddComponent<VehicleSpawnPoint>();

            var camGo = new GameObject("MainCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.SetPositionAndRotation(SpawnPosition + new Vector3(0f, 2f, -6f), Quaternion.identity);
            var camera = camGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2600f;
            camera.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CinemachineBrain>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.stopNaN = true;
            data.dithering = true;

            var followGo = new GameObject("FollowCamera");
            followGo.transform.SetParent(rig.transform, false);
            var cm = followGo.AddComponent<CinemachineCamera>();
            cm.Lens.NearClipPlane = 0.1f;
            cm.Lens.FarClipPlane = 2600f;
            cm.Lens.FieldOfView = 60f;
            var chase = followGo.AddComponent<VehicleChaseCamera>();
            Configure(chase, so =>
            {
                // City tuning: a touch higher and further for reading intersections, a wider lens with more speed
                // widening, more look-ahead into corners. Collision includes buildings and pavement blocks.
                so.FindProperty("distance").floatValue = 6.2f;
                so.FindProperty("height").floatValue = 2.05f;
                so.FindProperty("lookHeight").floatValue = 1.05f;
                so.FindProperty("speedDistance").floatValue = 1.5f;
                so.FindProperty("speedHeight").floatValue = -0.3f;
                so.FindProperty("yawSmoothTime").floatValue = 0.2f;
                so.FindProperty("travelHeadingWeight").floatValue = 0.5f;
                so.FindProperty("lookAheadTime").floatValue = 0.26f;
                so.FindProperty("maxLookAhead").floatValue = 8f;
                so.FindProperty("baseFieldOfView").floatValue = 60f;
                so.FindProperty("speedFieldOfView").floatValue = 12f;
                so.FindProperty("collisionRadius").floatValue = 0.3f;
                so.FindProperty("minDistance").floatValue = 2f;
                so.FindProperty("obstacleLayers").intValue = (1 << c.Drivable) | (1 << c.Environment);
            });

            var hudGo = new GameObject("MissionHUD");
            hudGo.transform.SetParent(rig.transform, false);
            var doc = hudGo.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_NeonRift/UI/NeonRiftPanelSettings.asset");
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_NeonRift/UI/MissionHud.uxml");
            doc.sortingOrder = 0;
            var hud = hudGo.AddComponent<MissionHud>();

            var audioGo = new GameObject("MissionAudio");
            audioGo.transform.SetParent(rig.transform, false);
            var missionAudio = audioGo.AddComponent<MissionAudio>();
            missionAudio.EditorConfigure(c.Audio.Set);

            Configure(director, so =>
            {
                so.FindProperty("hud").objectReferenceValue = hud;
                so.FindProperty("missionAudio").objectReferenceValue = missionAudio;
                so.FindProperty("alertOrigin").objectReferenceValue = core;
            });
            Configure(entry, so =>
            {
                so.FindProperty("spawnPoint").objectReferenceValue = spawn;
                so.FindProperty("chaseCamera").objectReferenceValue = chase;
                so.FindProperty("director").objectReferenceValue = director;
                so.FindProperty("viewCamera").objectReferenceValue = camera;
            });
            return (entry, director, camera, chase);
        }

        private static void Configure(Object target, System.Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- Dev tools and validation routes ----------------

        private static void BuildDevTools(Context c, MissionSceneEntry entry)
        {
            var dev = Root(c, "DevTools");
            var hud = dev.AddComponent<VehicleDebugHud>();
            Configure(hud, so => so.FindProperty("visible").boolValue = false);
            Configure(entry, so => so.FindProperty("debugHud").objectReferenceValue = hud);

            var routes = new GameObject("ValidationRoutes").transform;
            routes.SetParent(dev.transform, false);
            // Lane-centre waypoints (right-hand traffic). The alley weaves through the jersey-barrier chicane.
            var alleyNorth = new[] { V(162, -80), V(162, -66), V(158.2f, -58), V(158.2f, -47), V(161.5f, -38), V(161.5f, 12), V(162.3f, 30), V(160.5f, 50), V(160, 66) };
            var alleyRoute = Route(routes, "Route_AlleyIn_ExpresswayOut", new List<Vector2> { V(3.5f, -292), V(3.5f, -103) }
                .Concat(new[] { V(162, -103) }).Concat(alleyNorth)
                .Concat(new[] { V(160, 108), V(168.5f, 117), V(168.5f, 138), V(160, 147), V(160, 296.5f), V(315.5f, 296.5f), V(315.5f, -420), V(320, -455), V(320, -470) }).ToList());
            // Southbound takes the same line: the chicane, not the lanes, decides where the car can be.
            var alleySouth = alleyNorth.Reverse().ToArray();
            var boulevardRoute = Route(routes, "Route_BoulevardIn_AlleyOut", new List<Vector2>
                {
                    V(3.5f, -292), V(3.5f, 296.5f), V(157, 296.5f), V(157, 150), V(151.5f, 138), V(151.5f, 117), V(158, 106)
                }.Concat(alleySouth).Concat(new[] { V(162, -103), V(315.5f, -103), V(315.5f, -420), V(320, -455), V(320, -470) }).ToList());
            dev.AddComponent<NightRunValidator>().EditorConfigure(alleyRoute, boulevardRoute);
        }

        private static Vector2 V(float x, float z) => new(x, z);

        /// <summary>Polyline through corners with rounded turns, resampled every 2 m, advisory speed from curvature.</summary>
        private static DrivingRoute Route(Transform parent, string name, List<Vector2> corners)
        {
            const float spacing = 2f;
            const float maxSpeed = 30f;
            // A validation driver, not a racer: corners at a firm-but-safe 0.72 g.
            const float lateralGrip = 0.72f * 9.81f;
            var raw = new List<Vector2> { corners[0] };
            for (int i = 1; i < corners.Count - 1; i++)
            {
                Vector2 a = corners[i - 1], p = corners[i], b = corners[i + 1];
                float t = Mathf.Min(12f, Mathf.Min((p - a).magnitude, (b - p).magnitude) * 0.45f);
                Vector2 p0 = p + (a - p).normalized * t, p2 = p + (b - p).normalized * t;
                for (int k = 0; k <= 8; k++)
                {
                    float u = k / 8f;
                    raw.Add((1 - u) * (1 - u) * p0 + 2 * (1 - u) * u * p + u * u * p2);
                }
            }
            raw.Add(corners[^1]);

            var points = new List<Vector3>();
            Vector2 last = raw[0];
            points.Add(new Vector3(last.x, 0f, last.y));
            float carry = 0f;
            for (int i = 1; i < raw.Count; i++)
            {
                Vector2 from = raw[i - 1], to = raw[i];
                float len = (to - from).magnitude;
                float s = spacing - carry;
                while (s <= len)
                {
                    var q = Vector2.Lerp(from, to, s / len);
                    points.Add(new Vector3(q.x, 0f, q.y));
                    s += spacing;
                }
                carry = len - (s - spacing);
            }
            var speeds = new List<float>();
            for (int i = 0; i < points.Count; i++)
            {
                int a = Mathf.Max(0, i - 3), b = Mathf.Min(points.Count - 1, i + 3);
                float k = Curvature(points[a], points[i], points[b]);
                speeds.Add(Mathf.Min(maxSpeed, k > 1e-4f ? Mathf.Sqrt(lateralGrip / k) : maxSpeed));
            }
            // Hold the corner speed from a little before the apex to a little after it.
            var smoothed = new List<float>(speeds);
            for (int i = 0; i < speeds.Count; i++)
                for (int k = Mathf.Max(0, i - 4); k <= Mathf.Min(speeds.Count - 1, i + 4); k++)
                    smoothed[i] = Mathf.Min(smoothed[i], speeds[k]);
            speeds = smoothed;
            // Ease off into the uplink ring and the alley gate so the stop is short (the validator brakes on focus).
            int nearestCore = 0;
            for (int i = 1; i < points.Count; i++)
                if ((points[i] - CorePosition).sqrMagnitude < (points[nearestCore] - CorePosition).sqrMagnitude) nearestCore = i;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (i <= nearestCore && new Vector2(p.x - CorePosition.x, p.z - CorePosition.z).magnitude < 24f) speeds[i] = Mathf.Min(speeds[i], 6f);
                if (p.x > 150f && p.x < 170f && p.z > -50f && p.z < 10f) speeds[i] = Mathf.Min(speeds[i], 7f);
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var route = go.AddComponent<DrivingRoute>();
            route.EditorConfigure(points, speeds, new List<DrivingRoute.Section> { new() { name = name, startDistance = 0f } });
            return route;
        }

        private static float Curvature(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector2 p = new(a.x, a.z), q = new(b.x, b.z), r = new(c.x, c.z);
            float area = Mathf.Abs((q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x)) * 0.5f;
            float d = (q - p).magnitude * (r - q).magnitude * (r - p).magnitude;
            return d > 1e-5f ? 4f * area / d : 0f;
        }

        // ---------------- Project wiring ----------------

        private static void RegisterInProject(MissionDefinition mission, Context c)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                c.Log.AppendLine("  added NightRun to the build scenes");
            }
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:GameConfig")[0]));
            Configure(config, so =>
            {
                var list = so.FindProperty("missions");
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == mission) present = true;
                if (!present)
                {
                    list.InsertArrayElementAtIndex(0);
                    list.GetArrayElementAtIndex(0).objectReferenceValue = mission;
                }
                so.FindProperty("defaultMission").objectReferenceValue = mission;
            });
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }
    }
}
