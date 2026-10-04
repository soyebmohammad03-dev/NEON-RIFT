using System.Collections.Generic;
using System.Linq;
using NeonRift.EditorTools.World;
using NeonRift.Gameplay;
using NeonRift.Intro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.Intro
{
    public static partial class IntroBuilder
    {
        private const string TrafficPrefabFolder = "Assets/_NeonRift/Prefabs/Traffic";

        /// <summary>
        /// The intro's street and sky life (<see cref="IntroCityLife"/>), timed to the shot table: traffic on W Avenue
        /// and Market Street (both directions, clear of the low street-level cameras), patrol drones sweeping those
        /// streets, drones circling the Data Core, sky traffic across the descent, and cars passing the garage.
        /// </summary>
        private static GameObject BuildCityLife(Transform parent, PlayableDirector playable, Vector3 core, Vector3 gate, System.Text.StringBuilder log)
        {
            var root = new GameObject("CityLife");
            root.transform.SetParent(parent, false);
            var prefabs = AssetDatabase.FindAssets("t:Prefab PackCar_", new[] { TrafficPrefabFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p != null).OrderBy(p => p.name).ToArray();
            int next = 0, cars = 0;

            Transform Car(Transform laneRoot)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[next++ % prefabs.Length], laneRoot);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Visual only: no physics, no AI, no sound (the intro has its own mix).
                foreach (var c in go.GetComponentsInChildren<TrafficCar>(true)) Object.DestroyImmediate(c);
                foreach (var c in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(c);
                foreach (var c in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(c);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
                go.SetActive(false);
                cars++;
                return go.transform;
            }

            var lanes = new List<IntroCityLife.Lane>();
            void Lane(string name, Vector3 from, Vector3 to, float speed, int count, float spacing, float start, float end)
            {
                var laneRoot = new GameObject($"Lane_{name}").transform;
                laneRoot.SetParent(root.transform, false);
                var list = new Transform[count];
                for (int i = 0; i < count; i++) list[i] = Car(laneRoot);
                lanes.Add(new IntroCityLife.Lane { from = from, to = to, speed = speed, spacing = spacing, start = start, end = end, cars = list });
            }

            // W Avenue (lanes at x = ±1.75, ±5.25; northbound east of the centre line). 03a: the camera runs up the
            // outer southbound lane at x = -4.5, so southbound traffic uses the inner lane and passes close by.
            Lane("Avenue_NB_Inner", new Vector3(1.75f, 0f, -236f), new Vector3(1.75f, 0f, -40f), 14f, 5, 30f, 15.5f, 21f);
            Lane("Avenue_NB_Outer", new Vector3(5.25f, 0f, -250f), new Vector3(5.25f, 0f, -40f), 11f, 4, 41f, 15.5f, 21f);
            Lane("Avenue_SB_Inner", new Vector3(-1.75f, 0f, -40f), new Vector3(-1.75f, 0f, -280f), 13f, 6, 34f, 15.5f, 21f);
            // Market Street (eastbound z = -103, westbound z = -97). 03b: the camera sits over the eastbound lane at
            // x 8.5→16, so eastbound cars start ahead of it and drive away; westbound cars come towards it.
            Lane("Market_EB", new Vector3(24f, 0f, -103f), new Vector3(170f, 0f, -103f), 12f, 4, 34f, 20f, 25.5f);
            Lane("Market_WB", new Vector3(170f, 0f, -97f), new Vector3(-20f, 0f, -97f), 12f, 5, 36f, 20f, 25.5f);
            // W Avenue at the garage. 06/07a: southbound traffic between the cameras and the door; northbound only in
            // 06 (07a's camera stands in the northbound lanes).
            Lane("Garage_SB_Inner", new Vector3(-1.75f, 0f, -150f), new Vector3(-1.75f, 0f, -340f), 12f, 4, 45f, 39f, 47.5f);
            Lane("Garage_SB_Outer", new Vector3(-5.25f, 0f, -170f), new Vector3(-5.25f, 0f, -340f), 10f, 3, 55f, 39f, 47.5f);
            Lane("Garage_NB", new Vector3(5.25f, 0f, -330f), new Vector3(5.25f, 0f, -160f), 12f, 3, 55f, 39f, 43.5f);
            // 11e/12: the odd car passing between the title camera and the open door.
            Lane("Title_SB", new Vector3(-5.25f, 0f, -150f), new Vector3(-5.25f, 0f, -340f), 9f, 2, 95f, 73.5f, Duration);

            // Drones: Mech Drones with a searchlight and a visible beam (the lockdown drones' beam mesh and material).
            var mech = DroneModels.Mech();
            var security = Object.FindAnyObjectByType<SecurityDrones>();
            var beamSource = security != null ? security.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "Beam") : null;
            int drones = 0;
            (Transform drone, Light light, Transform beam) Drone(string name)
            {
                var d = new GameObject(name).transform;
                d.SetParent(root.transform, false);
                var body = (GameObject)PrefabUtility.InstantiatePrefab(mech, d);
                body.transform.localPosition = new Vector3(0f, -1.3f, 0f);
                var lightGo = new GameObject("Searchlight");
                lightGo.transform.SetParent(d, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.6f, 0.45f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(0.85f, 0.92f, 1f);
                l.intensity = 1300f;
                l.range = 50f;
                l.spotAngle = 30f;
                l.innerSpotAngle = 14f;
                l.shadows = LightShadows.None;
                Transform beam = null;
                if (beamSource != null)
                {
                    var b = new GameObject("Beam");
                    b.transform.SetParent(lightGo.transform, false);
                    b.AddComponent<MeshFilter>().sharedMesh = beamSource.GetComponent<MeshFilter>().sharedMesh;
                    var br = b.AddComponent<MeshRenderer>();
                    br.sharedMaterial = beamSource.sharedMaterial;
                    br.shadowCastingMode = ShadowCastingMode.Off;
                    beam = b.transform;
                }
                d.gameObject.SetActive(false);
                drones++;
                return (d, l, beam);
            }

            var flights = new List<IntroCityLife.Flight>();
            void Flight(string name, Vector3 from, Vector3 to, float start, float end)
            {
                var (d, l, b) = Drone($"Flight_{name}");
                flights.Add(new IntroCityLife.Flight { drone = d, searchlight = l, beam = b, from = from, to = to, start = start, end = end });
            }
            // Sky traffic crossing the descent (02): high patrols lit against the skyline.
            Flight("Sky_A", new Vector3(-220f, 62f, -330f), new Vector3(140f, 56f, -300f), 9f, 16.5f);
            Flight("Sky_B", new Vector3(120f, 78f, -430f), new Vector3(-240f, 84f, -380f), 9f, 16.5f);
            Flight("Sky_C", new Vector3(-160f, 46f, -250f), new Vector3(160f, 52f, -262f), 9f, 16.5f);
            // Street patrols: down W Avenue over the 03a camera, along Market Street towards 03b, across the garage (06).
            Flight("Avenue", new Vector3(3f, 14f, -110f), new Vector3(-1f, 11f, -232f), 15.5f, 21f);
            Flight("Market", new Vector3(140f, 15f, -95f), new Vector3(30f, 12f, -100f), 20f, 25.5f);
            Flight("Garage", new Vector3(-36f, 13f, -300f), new Vector3(22f, 12f, -236f), 39f, 47.5f);
            // Two guards holding over the compound gate (04b), drifting slowly across it.
            Flight("Gate_A", gate + new Vector3(-7f, 7f, 7f), gate + new Vector3(-3f, 7.5f, 5f), 27.5f, 31.5f);
            Flight("Gate_B", gate + new Vector3(6f, 9f, 4f), gate + new Vector3(3f, 8.5f, 8f), 27.5f, 31.5f);

            var orbits = new List<IntroCityLife.Orbit>();
            // The Data Core searched from the air (04b–05b, and the montage).
            for (int i = 0; i < 3; i++)
            {
                var (d, l, b) = Drone($"Orbit_Core_{i}");
                orbits.Add(new IntroCityLife.Orbit
                {
                    drone = d, searchlight = l, beam = b, centre = new Vector3(core.x, 0f, core.z), radius = 30f + i * 6f, height = 18f + i * 3f,
                    period = 20f + i * 4f, phase = i * 2.1f, start = 25f, end = 76f
                });
            }

            var life = root.AddComponent<IntroCityLife>();
            life.EditorConfigure(playable, lanes.ToArray(), flights.ToArray(), orbits.ToArray());
            root.SetActive(false);
            log.AppendLine($"  intro city life: {lanes.Count} traffic lanes ({cars} pack cars, visual only), {drones} Mech Drones ({flights.Count} flights, {orbits.Count} orbiting the core)");
            return root;
        }
    }
}
