using System.Collections.Generic;
using System.Linq;
using NeonRift.EditorTools.Frontend;
using NeonRift.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// The crew's garage on W Avenue: the Car Select hall (<see cref="GarageBuilder.BuildShell"/>) placed in the city with
    /// its roller door onto the avenue, walls and door that collide, a turntable you can stand a car on, its own baked
    /// reflection probe, and a <see cref="CrewGarage"/> that the intro lights and opens. The mission starts just outside.
    /// </summary>
    public static partial class NightRunBuilder
    {
        /// <summary>Garage root (hall origin = the turntable). Yaw −90: the hall's door wall faces east onto W Avenue.</summary>
        public static readonly Vector3 GaragePosition = new(-27.2f, 0.02f, -255f);
        public static readonly Quaternion GarageRotation = Quaternion.Euler(0f, -90f, 0f);

        /// <summary>Garage-local point to world.</summary>
        public static Vector3 GarageToWorld(Vector3 local) => GaragePosition + GarageRotation * local;

        private static CrewGarage BuildCrewGarage(Context c)
        {
            var shell = GarageBuilder.BuildShell(withStreet: false);
            var root = shell.Root;
            root.name = "CrewGarage";
            root.SetParent(c.Gameplay, false);
            root.SetPositionAndRotation(GaragePosition, GarageRotation);

            float w = GarageBuilder.HallHalfWidth, back = GarageBuilder.HallBack, front = GarageBuilder.HallFront, h = GarageBuilder.HallHeight;
            float door = GarageBuilder.DoorHalfWidth, doorH = GarageBuilder.DoorHeight;
            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(root, false);
            void Box(Vector3 min, Vector3 max)
            {
                var go = new GameObject("Wall") { layer = c.Environment };
                go.transform.SetParent(colliders, false);
                var b = go.AddComponent<BoxCollider>();
                b.center = (min + max) * 0.5f;
                b.size = max - min;
            }
            Box(new Vector3(-w - 0.4f, 0f, back - 0.4f), new Vector3(-door, h, back));
            Box(new Vector3(door, 0f, back - 0.4f), new Vector3(w + 0.4f, h, back));
            Box(new Vector3(-door, doorH, back - 0.4f), new Vector3(door, h, back));
            Box(new Vector3(-w - 0.4f, 0f, back), new Vector3(-w, h, front));
            Box(new Vector3(w, 0f, back), new Vector3(w + 0.4f, h, front));
            Box(new Vector3(-w - 0.4f, 0f, front), new Vector3(w + 0.4f, h, front + 0.4f));
            Box(new Vector3(-w - 0.4f, h, back - 0.4f), new Vector3(w + 0.4f, h + 0.3f, front + 0.4f));
            // The roller door blocks the way in while it is down (the collider rides up with it).
            var doorCol = new GameObject("DoorCollider") { layer = c.Environment };
            doorCol.transform.SetParent(shell.Door, false);
            var dc = doorCol.AddComponent<BoxCollider>();
            dc.center = new Vector3(0f, doorH * 0.5f, 0f);
            dc.size = new Vector3(door * 2f, doorH, 0.25f);
            // A car can stand on the turntable.
            var disc = shell.Turntable.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name.Contains("Disc"));
            if (disc != null)
            {
                disc.gameObject.layer = c.Environment;
                var mc = disc.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = disc.sharedMesh;
                mc.convex = true;
            }

            // Its own box-projected reflection probe (baked with the hall lit, like Car Select).
            var probeGo = new GameObject("GarageProbe");
            probeGo.transform.SetParent(root, false);
            probeGo.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.boxProjection = true;
            probe.size = new Vector3(w * 2f + 0.5f, h + 0.5f, front - back + 0.5f);
            probe.center = new Vector3(0f, h * 0.5f - 2.5f, (front + back) * 0.5f);
            probe.resolution = 256;
            probe.hdr = true;
            probe.importance = 2;
            probe.cullingMask = ~(1 << LayerMask.NameToLayer("Vehicle"));

            // Light leaks through walls without shadows: the washers aimed at the door wall cast shadows so the street
            // outside is not lit through a closed door.
            foreach (var l in shell.All.Where(l => l.name.StartsWith("Wash_Back"))) l.shadows = LightShadows.Soft;

            // Intro slots: the hero on the turntable (facing into the hall, as in Car Select), two cars in the bays
            // facing the door, ready to roll out.
            var slots = new List<Transform>();
            Transform Slot(string name, Vector3 local, float yaw)
            {
                var t = new GameObject(name).transform;
                t.SetParent(root, false);
                t.localPosition = local;
                t.localRotation = Quaternion.Euler(0f, yaw, 0f);
                slots.Add(t);
                return t;
            }
            Slot("IntroSlot_Hero", new Vector3(0f, 0.12f, 0f), 0f);
            // Facing the door and angled in towards its centre.
            Slot("IntroSlot_BayLeft", new Vector3(-8.2f, 0f, -8.5f), 180f - 28f);
            Slot("IntroSlot_BayRight", new Vector3(8.2f, 0f, -8.5f), 180f + 28f);

            // Light banks in the order they strike: work strips and fills, then the washers, then the stage.
            var fills = shell.All.Where(l => l.name.StartsWith("SoftBox") || l.name.StartsWith("Workbench") || l.name.StartsWith("PlanningWall")).ToArray();
            var washers = shell.All.Where(l => l.name.StartsWith("Wash") || l.name.StartsWith("Bay")).ToArray();
            var stage = new[] { shell.Key, shell.RimCool, shell.RimWarm };
            var speaker = Speaker(c, root, "GarageAudio", 8f, 120f);
            speaker.transform.localPosition = new Vector3(0f, 4f, -10f);
            var garage = root.gameObject.AddComponent<CrewGarage>();
            garage.EditorConfigure(shell.Turntable, shell.Door, GarageBuilder.DoorHeight - 0.2f,
                new[] { new CrewGarage.Bank { lights = fills }, new CrewGarage.Bank { lights = washers }, new CrewGarage.Bank { lights = stage } },
                shell.Strips.ToArray(), slots.ToArray(), speaker, c.Audio.Heist.Dock, c.Audio.Motor);
            c.GarageLights.AddRange(shell.All);

            // Outside: a caged sodium lamp over the door and a dim number plate, so the anonymous unit reads at night
            // (always on; one unshadowed light).
            var exterior = new GameObject("Exterior").transform;
            exterior.SetParent(root, false);
            var fixture = new MeshBuilder();
            fixture.OrientedBox(new Vector3(0f, GarageBuilder.DoorHeight + 0.75f, back - 0.75f), new Vector3(0.7f, 0.18f, 0.5f), Quaternion.identity, 1f);
            var fixtureGo = DistrictKit.Renderer("DoorLamp", exterior, DistrictKit.SaveMesh(fixture, "Garage_DoorLamp"), c.Kit.LampHeads[(int)DistrictKit.LampKind.Sodium], c.Environment, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(fixtureGo, 0);
            var plate = new MeshBuilder();
            var uv = DistrictTextures.SignRect("UNIT 7", out float aspect);
            plate.Panel(new Vector3(GarageBuilder.DoorHalfWidth + 1.6f, 2.4f, back - 0.42f), Quaternion.Euler(0f, 180f, 0f), 0.42f * aspect, 0.42f, uv);
            var plateGo = DistrictKit.Renderer("UnitSign", exterior, DistrictKit.SaveMesh(plate, "Garage_UnitSign"), c.Kit.Signs[0], c.Environment, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(plateGo, 0);
            var lamp = new GameObject("DoorLampLight").AddComponent<Light>();
            lamp.transform.SetParent(exterior, false);
            lamp.transform.localPosition = new Vector3(0f, GarageBuilder.DoorHeight + 0.55f, back - 0.9f);
            lamp.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -1f, -0.55f));
            lamp.type = LightType.Spot;
            lamp.color = DistrictKit.LampColours[(int)DistrictKit.LampKind.Sodium];
            lamp.intensity = 90f;
            lamp.range = 16f;
            lamp.spotAngle = 110f;
            lamp.innerSpotAngle = 60f;
            lamp.shadows = LightShadows.None;
            // Two sodium street lamps flank the apron (the avenue's own lamps skip the garage frontage).
            var sodium = AssetDatabase.LoadAssetAtPath<GameObject>($"{DistrictKit.PrefabFolder}/PF_Prop_StreetLight_Sodium.prefab");
            if (sodium != null)
                foreach (float side in new[] { -1f, 1f })
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(sodium, exterior);
                    go.transform.localPosition = new Vector3(side * (GarageBuilder.DoorHalfWidth + 6f), 0f, back - 3.2f);
                    go.transform.localRotation = Quaternion.LookRotation(Vector3.back);
                    foreach (var l in go.GetComponentsInChildren<Light>(true)) { l.enabled = true; l.shadows = LightShadows.None; }
                }
            c.Log.AppendLine($"  crew garage on W Avenue: door at {GarageToWorld(new Vector3(0f, 0f, back)):F1}, {shell.All.Count} lights (intro only)");
            return garage;
        }
    }
}
