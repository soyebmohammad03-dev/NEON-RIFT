using System.Collections.Generic;
using System.Linq;
using NeonRift.EditorTools.World;
using NeonRift.Gameplay;
using NeonRift.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Signs of life: physical city traffic from the car pack (<see cref="CityTraffic"/>: roaming and parked), pedestrian
    /// silhouettes in groups (<see cref="CrowdGroups"/>) and security drones for the lockdown (<see cref="SecurityDrones"/>).
    /// </summary>
    public static partial class NightRunBuilder
    {
        private const int RoamingCars = 14, ParkedCars = 26;

        private static void BuildCityLife(Context c, CityNavigation navigation, Transform core, RivalDirector rivals)
        {
            var root = new GameObject("CityLife").transform;
            root.SetParent(c.Gameplay, false);
            BuildTraffic(c, root, navigation);
            BuildCrowds(c, root, navigation);
            BuildDrones(c, root, core, rivals);
            BuildSentries(c, root, core);
        }

        private static void BuildTraffic(Context c, Transform root, CityNavigation navigation)
        {
            // Real cars from the generic passenger car pack: physical, with engine and impact sounds.
            var engine = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_NeonRift/Audio/Generated/Engine_V8Road/V8Road_On_1700.wav");
            var impacts = new[] { "Light_1", "Light_2", "Medium_1", "Medium_2", "Heavy_1", "Heavy_2" }
                .Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_NeonRift/Audio/Generated/Impacts/Impact_{n}.wav")).Where(x => x != null).ToArray();
            var prefabs = TrafficCarBuilder.Build(c.Log, engine, impacts).Select(p => p.GetComponent<TrafficCar>()).Where(t => t != null).ToArray();
            var spots = c.District.Infill.CarSpots.ToArray();
            var pool = new GameObject("CityTraffic").transform;
            pool.SetParent(root, false);
            pool.gameObject.AddComponent<CityTraffic>().EditorConfigure(navigation, prefabs, spots, RoamingCars, ParkedCars);
            c.Log.AppendLine($"  city traffic: {RoamingCars} roaming + {ParkedCars} parked pack cars (Rigidbody, raycast suspension), {spots.Length} parking bays");
        }

        private static void BuildCrowds(Context c, Transform root, CityNavigation navigation)
        {
            int detail = LayerMask.NameToLayer("Detail");
            var cloth = new[]
            {
                DistrictKit.Lit("Crowd_ClothDark", new Color(0.035f, 0.035f, 0.04f), 0.25f, 0f),
                DistrictKit.Lit("Crowd_ClothWarm", new Color(0.09f, 0.06f, 0.045f), 0.25f, 0f),
                DistrictKit.Lit("Crowd_ClothBlue", new Color(0.04f, 0.05f, 0.08f), 0.3f, 0f),
                DistrictKit.Lit("Crowd_ClothLight", new Color(0.22f, 0.21f, 0.2f), 0.3f, 0f),
            };
            var rng = new System.Random(4242);
            var points = new List<(Vector3 p, float yaw, int count)>();
            void Area(Rect r, int groups)
            {
                for (int i = 0; i < groups; i++)
                    points.Add((new Vector3(Mathf.Lerp(r.xMin + 4f, r.xMax - 4f, (float)rng.NextDouble()), CityLayout.KerbHeight, Mathf.Lerp(r.yMin + 4f, r.yMax - 4f, (float)rng.NextDouble())),
                                (float)rng.NextDouble() * 360f, 3 + rng.Next(5)));
            }
            foreach (var lot in CityLayout.Lots)
            {
                if (lot.Kind == LotKind.Plaza) Area(lot.Area, 14);
                if (lot.Kind == LotKind.NightMarket) Area(lot.Area, 12);
            }
            // Pavements: groups near the building line of streets and avenues, clear of junctions and reserved lots.
            var net = navigation.Network;
            var candidates = Enumerable.Range(0, net.Edges.Count).Where(e => net.Edges[e].roadClass is RoadClass.Street or RoadClass.Arterial).ToList();
            int placed = 0;
            for (int attempt = 0; attempt < 600 && placed < 36; attempt++)
            {
                var edge = net.Edges[candidates[rng.Next(candidates.Count)]];
                Vector3 a = net.NodePosition(edge.a), b = net.NodePosition(edge.b);
                float len = Vector3.Distance(a, b);
                if (len < 70f) continue;
                float t = Mathf.Lerp(30f / len, 1f - 30f / len, (float)rng.NextDouble());
                Vector3 dir = (b - a).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, dir) * (rng.NextDouble() < 0.5 ? 1f : -1f);
                Vector3 p = Vector3.Lerp(a, b, t) + side * (edge.halfWidth + 2.6f);
                var flat = new Vector2(p.x, p.z);
                if (CityLayout.Reserved.Any(r => r.Contains(flat)) || CityLayout.Lots.Any(l => l.Area.Contains(flat))) continue;
                points.Add((new Vector3(p.x, CityLayout.KerbHeight, p.z), Mathf.Atan2(-side.x, -side.z) * Mathf.Rad2Deg + 90f, 2 + rng.Next(4)));
                placed++;
            }

            // Street life along the intro's street-level shots: both pavements of W Avenue north of the garage and of
            // Market Street east of W Avenue (people facing the road, in bigger groups: the city is busy at night).
            // side: +1 when the pavement lies to the right of a→b (away from the road), -1 when to the left.
            void Pavement(Vector3 a, Vector3 b, float side, int groups)
            {
                Vector3 dir = (b - a).normalized;
                Vector3 normal = Vector3.Cross(Vector3.up, dir) * side;
                for (int i = 0; i < groups; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, (i + 0.5f) / groups + ((float)rng.NextDouble() - 0.5f) * 0.4f / groups);
                    var flat = new Vector2(p.x, p.z);
                    if (CityLayout.Reserved.Any(r => r.Contains(flat))) continue;
                    points.Add((new Vector3(p.x, CityLayout.KerbHeight, p.z), Mathf.Atan2(-normal.x, -normal.z) * Mathf.Rad2Deg + 90f, 4 + rng.Next(4)));
                }
            }
            Pavement(new Vector3(-9.8f, 0f, -232f), new Vector3(-9.8f, 0f, -112f), -1f, 7);
            Pavement(new Vector3(9.8f, 0f, -232f), new Vector3(9.8f, 0f, -112f), 1f, 7);
            Pavement(new Vector3(24f, 0f, -91.2f), new Vector3(130f, 0f, -91.2f), -1f, 6);
            Pavement(new Vector3(24f, 0f, -108.8f), new Vector3(130f, 0f, -108.8f), 1f, 6);

            var crowdRoot = new GameObject("Crowds").transform;
            crowdRoot.SetParent(root, false);
            var groups = new List<Transform>();
            int figures = 0;
            for (int g = 0; g < points.Count; g++)
            {
                var (p, yaw, count) = points[g];
                var mb = new MeshBuilder();
                for (int k = 0; k < count; k++)
                {
                    float a = k / (float)count * Mathf.PI * 2f + (float)rng.NextDouble();
                    float r = count == 1 ? 0f : 0.7f + (float)rng.NextDouble() * 1.1f;
                    var o = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    float h = 1.6f + (float)rng.NextDouble() * 0.25f;
                    mb.OrientedBox(o + new Vector3(-0.09f, h * 0.22f, 0f), new Vector3(0.14f, h * 0.44f, 0.16f), Quaternion.identity, 1f);
                    mb.OrientedBox(o + new Vector3(0.09f, h * 0.22f, 0f), new Vector3(0.14f, h * 0.44f, 0.16f), Quaternion.identity, 1f);
                    mb.OrientedBox(o + new Vector3(0f, h * 0.6f, 0f), new Vector3(0.42f, h * 0.34f, 0.24f), Quaternion.Euler(0f, (float)rng.NextDouble() * 60f - 30f, 0f), 1f);
                    mb.Cylinder(o + new Vector3(0f, h * 0.79f, 0f), 0.11f, h * 0.13f, 8, true);
                    figures++;
                }
                var go = DistrictKit.Renderer($"Crowd_{g:00}", crowdRoot, DistrictKit.SaveMesh(mb, $"Crowd_{g:00}"), cloth[rng.Next(cloth.Length)], detail, shadows: false);
                GameObjectUtility.SetStaticEditorFlags(go, 0);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                groups.Add(go.transform);
            }
            crowdRoot.gameObject.AddComponent<CrowdGroups>().EditorConfigure(groups.ToArray());
            c.Log.AppendLine($"  crowds: {groups.Count} groups, {figures} figures (Detail layer, step aside within 16 m)");
        }

        /// <summary>
        /// Five security drones (the Mech Drone model): three tail the player and one locks on to each rival crew once
        /// the counter-intrusion starts. Each carries red/blue strobes and a searchlight with a visible beam that it
        /// holds on its car.
        /// </summary>
        private static void BuildDrones(Context c, Transform root, Transform core, RivalDirector rivals)
        {
            var model = DroneModels.Mech();
            var strobe = new MeshBuilder();
            strobe.OrientedBox(new Vector3(0f, 0f, 0f), new Vector3(0.7f, 0.08f, 0.18f), Quaternion.identity, 1f);
            var strobeMesh = DistrictKit.SaveMesh(strobe, "Drone_Strobe");
            var beamMesh = new MeshBuilder();
            beamMesh.Cone(Vector3.up, 2.6f, 1f, 16, 0.12f);
            var beamAsset = DistrictKit.SaveMesh(beamMesh, "Drone_Beam");
            var strobeMat = DistrictKit.Lit("Drone_Strobe", Color.black, 0.8f, 0f, emission: new Color(0.001f, 0f, 0f));
            var beamMat = DistrictKit.Glow("Drone_Beam", new Color(0.12f, 0.13f, 0.15f), c.Kit.Textures.GlowGradient, 1.6f);

            var rootGo = new GameObject("SecurityDrones");
            rootGo.transform.SetParent(root, false);
            rootGo.transform.position = core.position + Vector3.up * 3f;
            var drones = new List<Transform>();
            var strobes = new List<Renderer>();
            var lights = new List<Light>();
            var beams = new List<Transform>();
            for (int i = 0; i < 5; i++)
            {
                var d = new GameObject($"Drone_{i}").transform;
                d.SetParent(rootGo.transform, false);
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, d);
                body.transform.localPosition = new Vector3(0f, -1.3f, 0f);   // model pivot at its feet: centre it on the drone
                var s = DistrictKit.Renderer("Strobe", d, strobeMesh, strobeMat, 0, shadows: false);
                s.transform.localPosition = new Vector3(0f, 1.25f, -0.1f);
                GameObjectUtility.SetStaticEditorFlags(s, 0);
                strobes.Add(s.GetComponent<Renderer>());
                var lightGo = new GameObject("Searchlight");
                lightGo.transform.SetParent(d, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.6f, 0.45f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(0.85f, 0.92f, 1f);
                l.intensity = 1100f;
                l.range = 45f;
                l.spotAngle = 30f;
                l.innerSpotAngle = 14f;
                l.shadows = LightShadows.None;
                lights.Add(l);
                var beam = DistrictKit.Renderer("Beam", lightGo.transform, beamAsset, beamMat, 0, shadows: false);
                GameObjectUtility.SetStaticEditorFlags(beam, 0);
                beams.Add(beam.transform);
                drones.Add(d);
            }
            rootGo.AddComponent<SecurityDrones>().EditorConfigure(drones.ToArray(), strobes.ToArray(), lights.ToArray(), beams.ToArray(), new[] { EventExtractTrace }, rivals);
            c.Log.AppendLine("  security drones: 5 Mech Drones (3 on the player, 1 per rival; launch on counter-intrusion / lockdown)");
        }

        /// <summary>Two Buster Drone sentries holding station over the Data Core compound (intro and missions).</summary>
        private static void BuildSentries(Context c, Transform root, Transform core)
        {
            var model = DroneModels.Buster();
            var sentryRoot = new GameObject("CompoundSentries").transform;
            sentryRoot.SetParent(root, false);
            var spots = new (Vector3 offset, float yaw)[] { (new Vector3(-26f, 24f, 38f), 150f), (new Vector3(30f, 28f, -24f), -40f) };
            for (int i = 0; i < spots.Length; i++)
            {
                var s = new GameObject($"Sentry_{i}").transform;
                s.SetParent(sentryRoot, false);
                s.SetPositionAndRotation(core.position + spots[i].offset, Quaternion.Euler(0f, spots[i].yaw, 0f));
                PrefabUtility.InstantiatePrefab(model, s);
                var eye = new GameObject("Eye").AddComponent<Light>();
                eye.transform.SetParent(s, false);
                eye.transform.localPosition = new Vector3(0f, -0.4f, 1.6f);
                eye.type = LightType.Point;
                eye.color = new Color(1f, 0.12f, 0.1f);
                eye.intensity = 40f;
                eye.range = 9f;
                eye.shadows = LightShadows.None;
                s.gameObject.AddComponent<HoverSentry>();
            }
            c.Log.AppendLine($"  compound sentries: {spots.Length} Buster Drones (static mesh, hovering)");
        }
    }
}
