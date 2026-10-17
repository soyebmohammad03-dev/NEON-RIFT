using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Environmental density (Harbor Rain 2.0): purposeful set-pieces instead of scattered props, all kept off the roads
    /// (checked against the road rectangles and the reserved solids, so routes, puzzle stations and alternate routes stay
    /// clear). The gate approach gets a truck holding area, a logistics yard with warehouses, staff parking, a sign gantry
    /// and light towers; the port police post sits by the gate with parked units; warehouses get loading-bay life
    /// (pallets, barrels, forklifts, crew cars); the industrial zone gets pipe racks, utility cabinets, generators and gas
    /// cages; the berth gets tyre fenders and lifebuoys; the flooded quayside shows storm damage (a toppled stack, loose
    /// barriers and crates) with closure and evacuation signs; CCTV poles watch the junctions; a welder works late in the
    /// depot. Props from Imported/PolyHaven (CC0) and the project's existing pack cars; small props are on the Detail layer
    /// (culled beyond 240 m), and only large ones have colliders.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private static readonly Dictionary<string, GameObject> propAssets = new();
        private static int propCount, propTris;
        private static Material forkliftYellow, palletWood, sandbag;
        private static readonly List<HarborPoliceCar> parkedPoliceKits = new();
        private static readonly List<Light> extraBudgetLights = new();
        private static AudioClip reverseBeepClip;

        private static GameObject PropAsset(string id)
        {
            if (propAssets.TryGetValue(id, out var a)) return a;
            a = AssetDatabase.LoadAssetAtPath<GameObject>($"{Imported}/PolyHaven/Models/{id}/{id}_1k.gltf");
            propAssets[id] = a;
            return a;
        }

        /// <summary>One Poly Haven prop at <paramref name="p"/> (ground contact), Detail layer, optional box collider.</summary>
        private static GameObject Prop(Transform parent, string id, Vector3 p, float yaw, float scale = 1f, bool solid = false, bool shadows = false)
        {
            var asset = PropAsset(id);
            if (asset == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            // Not batching-static: static batching merges meshes and drops their mesh LODs (every prop then draws full
            // detail at any range). Left dynamic, the SRP batcher keeps their draw cost low and the LODs work.
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = detail; GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccludeeStatic); }
            // Dense meshes get Unity mesh LODs (one simplified copy per source mesh, cached), so far props cost a fraction.
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) mf.sharedMesh = PropLod(mf.sharedMesh);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) propTris += (int)mf.sharedMesh.GetIndexCount(s) / 3;
            }
            if (solid)
            {
                var b = Bounds(go);
                Collider(parent, "PropCollider", b.center, b.size, environment);
                solids.Add(new Rect(b.min.x, b.min.z, b.size.x, b.size.z));
            }
            propCount++;
            return go;
        }

        private static readonly Dictionary<Mesh, Mesh> propLods = new();

        private static Mesh PropLod(Mesh mesh)
        {
            if (propLods.TryGetValue(mesh, out var done)) return done;
            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)mesh.GetIndexCount(i) / 3;
            if (tris < 2500) { propLods[mesh] = mesh; return mesh; }
            string dir = $"{Gen}/PropLods";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Gen, "PropLods");
            string path = $"{dir}/{mesh.name.Replace('/', '_')}_lods.asset";
            var copy = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (copy == null)
            {
                copy = Object.Instantiate(mesh);
                copy.name = mesh.name + "_lods";
                MeshLodUtility.GenerateMeshLods(copy, 4);
                AssetDatabase.CreateAsset(copy, path);
            }
            propLods[mesh] = copy;
            return copy;
        }

        private static bool Clear(Vector3 p, float half) => !Blocked(new Rect(p.x - half, p.z - half, half * 2f, half * 2f), 0.5f);

        /// <summary>A static parked pack car (traffic scripts and bodies removed).</summary>
        private static GameObject ParkedCar(Transform parent, GameObject[] packs, Random rng, Vector3 p, float yaw)
        {
            if (packs.Length == 0) return null;
            var car = (GameObject)PrefabUtility.InstantiatePrefab(packs[rng.Next(packs.Length)], parent);
            car.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            StripScripts(car);
            foreach (var rb in car.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var c in car.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            GameObjectUtility.SetStaticEditorFlags(car, StaticEditorFlags.BatchingStatic);
            foreach (var r in car.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return car;
        }

        private static Transform densityRoot;

        /// <summary>Removes a placed car's scripts, dependants first (RequireComponent targets were added earlier, so later
        /// components go first) — destroying in list order logged "Can't remove VehicleRig … VehicleController depends on it".</summary>
        private static void StripScripts(GameObject go, System.Func<MonoBehaviour, bool> keep = null)
        {
            var mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = mbs.Length - 1; i >= 0; i--)
                if (mbs[i] != null && (keep == null || !keep(mbs[i]))) Object.DestroyImmediate(mbs[i]);
        }

        private static void Pallet(Vector3 p, float yaw, Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            B(palletWood).OrientedBox(p + rot * new Vector3(0f, 0.07f, 0f), new Vector3(1.2f, 0.14f, 1f), rot, 1f);
            int kind = rng.Next(3);
            if (kind == 0) B(cladding).OrientedBox(p + rot * new Vector3(0f, 0.6f, 0f), new Vector3(1.1f, 0.9f, 0.95f), rot, 1f);
            else if (kind == 1) B(sandbag).OrientedBox(p + rot * new Vector3(0f, 0.45f, 0f), new Vector3(1.15f, 0.6f, 0.95f), rot, 1f);
        }

        private static void BuildDensity(Transform world, Random rng)
        {
            var root = new GameObject("PortDensity").transform;
            root.SetParent(world, false);
            densityRoot = root;
            propCount = propTris = 0;
            propAssets.Clear();
            propLods.Clear();
            parkedPoliceKits.Clear();
            extraBudgetLights.Clear();
            VehicleMaterials();
            forkliftYellow = Lit("HR_ForkliftYellow", new Color(0.75f, 0.52f, 0.05f), 0.45f, 0.2f, null);
            palletWood = Lit("HR_PalletWood", new Color(0.32f, 0.24f, 0.15f), 0.25f, 0f, null);
            sandbag = Lit("HR_Sandbag", new Color(0.33f, 0.31f, 0.24f), 0.2f, 0f, null);
            var packs = AssetDatabase.FindAssets("PackCar_ t:Prefab", new[] { "Assets/_NeonRift/Prefabs/Traffic" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g => g != null).ToArray();
            int trucks = 0, cars = 0, forklifts = 0;

            // ---- Gate approach (outside the south fence): truck holding lanes, logistics yard, staff parking, gantry sign.
            var approach = new GameObject("GateApproach").transform;
            approach.SetParent(root, false);
            Begin();
            foreach (float x in new[] { -42f, -60f, -78f, -96f })
            {
                B(markingYellow).Strip(new Vector3(x - 8.5f, 0.04f, -585f), new Vector3(x - 8.5f, 0.04f, -385f), 0.14f);
                for (float z = -570f; z < -400f; z += 22f)
                {
                    if (rng.NextDouble() < 0.18) continue;
                    // Held trucks (shared parked prefab): container centre 2.4 m behind the old origin keeps the queue spacing.
                    PlaceTruck(approach, "HeldTruck", new Vector3(x, 0f, z - 3.3f), Quaternion.identity, rng.Next(4), rng.NextDouble() < 0.8 ? containers[rng.Next(containers.Length)] : null, false);
                    trucks++;
                }
            }
            B(markingYellow).Strip(new Vector3(-104.5f, 0.04f, -585f), new Vector3(-104.5f, 0.04f, -385f), 0.14f);
            solids.Add(new Rect(-106f, -590f, 74f, 210f));
            Flush("TruckHolding", approach, environment, false);   // dim moonlight: their shadows were most of the gate view's shadow-pass cost
            SignPanel(approach, "TRUCK HOLDING  ·  WAIT FOR CALL", new Color(1f, 0.7f, 0.2f), new Vector3(-70f, 3.4f, -592f), 180f, 9f, true);
            // Logistics warehouses east of the road, loading bays facing west / north, staff parking between them.
            Begin();
            Building(new Rect(60f, -610f, 150f, 56f), 14f, claddingRed, bays: true);
            Building(new Rect(60f, -520f, 110f, 46f), 11f, claddingWhite, office: true, bays: true);
            Building(new Rect(220f, -600f, 90f, 120f), 16f, claddingGreen, bays: true);
            Flush("LogisticsWarehouses", approach, environment, true);
            SignPanel(approach, "HARBOURLINK LOGISTICS", Color.white, new Vector3(135f, 12f, -554.3f), 0f, 14f);
            SignPanel(approach, "TRANSIT SHED 7", Color.white, new Vector3(265f, 13.5f, -479.7f), 0f, 10f);
            var staff = new GameObject("StaffParking").transform;
            staff.SetParent(approach, false);
            Begin();
            for (int i = 0; i < 26; i++)
            {
                float x = 70f + i * 3f, z = -455f;
                B(marking).Strip(new Vector3(x - 1.5f, 0.045f, z - 2.6f), new Vector3(x - 1.5f, 0.045f, z + 2.6f), 0.12f);
                if (rng.NextDouble() < 0.45) continue;
                if (ParkedCar(staff, packs, rng, new Vector3(x, 0f, z), rng.NextDouble() < 0.5 ? 0f : 180f) != null) cars++;
            }
            Flush("StaffBays", staff, detail, false);
            Collider(approach, "StaffRow", new Vector3(108f, 0.8f, -455f), new Vector3(80f, 1.6f, 4.6f), environment);
            solids.Add(new Rect(66f, -460f, 82f, 10f));
            // Empty-container depot far west (stacks of empties waiting to be called in).
            Begin();
            Yard(approach, rng, -420f, -200f, -620f, -470f, 5);
            Flush("EmptiesDepot", approach, environment, false);
            // Light towers over the holding lanes and the depot (emissive heads and pools; the masts' real lights stay budgeted).
            Begin();
            foreach (var t in new[] { new Vector3(-118f, 0f, -520f), new Vector3(-118f, 0f, -430f), new Vector3(-20f, 0f, -560f), new Vector3(-190f, 0f, -540f), new Vector3(190f, 0f, -470f) })
            {
                B(metal).Cylinder(t, 0.25f, 18f, 8, true);
                B(lampHead).Cuboid(t + new Vector3(-1.4f, 17.6f, -0.5f), t + new Vector3(1.4f, 18.3f, 0.5f), 1f);
                B(pool).Decal(t + Vector3.up * 0.06f, 34f, 34f);
                B(cone).Cone(t + Vector3.up * 17.5f, 6f, 17.4f, 14, 0.7f);
                Collider(approach, "LightTower", t + Vector3.up * 3f, new Vector3(0.6f, 6f, 0.6f), environment);
                var lg = new GameObject("TowerLight") { layer = detail };
                lg.transform.SetParent(approach, false);
                lg.transform.SetPositionAndRotation(t + Vector3.up * 17.6f, Quaternion.Euler(90f, 0f, 0f));
                var tl = lg.AddComponent<Light>();
                tl.type = LightType.Spot; tl.color = new Color(1f, 0.74f, 0.45f); tl.intensity = 8000f; tl.range = 55f; tl.spotAngle = 115f; tl.innerSpotAngle = 60f;
                tl.shadows = LightShadows.None; tl.enabled = false;
                extraBudgetLights.Add(tl);
                solids.Add(new Rect(t.x - 1f, t.z - 1f, 2f, 2f));
            }
            Flush("LightTowers", approach, detail, false);

            // ---- Port police post by the gate (inside the fence, east of the spine): building, parked units, CCTV mast.
            var post = new GameObject("PolicePost").transform;
            post.SetParent(root, false);
            Begin();
            Building(new Rect(24f, -296f, 46f, 22f), 7f, claddingNavy(), office: true);
            Flush("PolicePostBuilding", post, environment, true);
            SignPanel(post, "PORT POLICE", new Color(0.4f, 0.7f, 1f), new Vector3(47f, 6f, -273.7f), 0f, 8f);
            var def = AssetDatabase.LoadAssetAtPath<NeonRift.Vehicles.VehicleDefinition>(HarborRainPolice.DefinitionPath);
            if (def != null && def.GameplayPrefab != null)
                foreach (var (p, yaw) in new[] { (new Vector3(30f, 0f, -268f), 90f), (new Vector3(38f, 0f, -268f), 90f), (new Vector3(-30f, 0f, -350f), 200f) })
                {
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(def.GameplayPrefab, post);
                    car.name = "ParkedPoliceUnit";
                    car.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                    StripScripts(car, mb => mb is HarborPoliceCar);
                    foreach (var rb in car.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                    foreach (var a in car.GetComponentsInChildren<AudioSource>(true)) if (a.name != "Siren") Object.DestroyImmediate(a);
                    var kit = car.GetComponent<HarborPoliceCar>();
                    if (kit != null) parkedPoliceKits.Add(kit);
                    foreach (var t in car.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = environment;
                    solids.Add(new Rect(p.x - 3f, p.z - 3f, 6f, 6f));
                }

            // ---- Warehouse loading bays: pallets, barrels, forklifts, crew cars (industrial and freight zones).
            var bays = new GameObject("LoadingBays").transform;
            bays.SetParent(root, false);
            Begin();
            foreach (var (x0, x1, z, face) in new[] { (-340f, -170f, -198f, 1f), (-610f, -300f, -61f, 1f), (-118f, -30f, -206f, 1f), (490f, 610f, -86f, 1f), (66f, 205f, -549f, 1f) })
            {
                for (float x = x0; x < x1; x += 11f)
                {
                    var p = new Vector3(x + 2.5f, 0f, z);
                    if (!Clear(p, 1.6f)) continue;
                    double roll = rng.NextDouble();
                    if (roll < 0.35) { Pallet(p, rng.Next(4) * 90f, rng); Pallet(p + new Vector3(1.4f, 0f, 0f), rng.Next(4) * 90f, rng); }
                    else if (roll < 0.55) { Forklift(p + new Vector3(0f, 0f, face * 1.5f), face > 0 ? 180f + rng.Next(-20, 20) : rng.Next(-20, 20)); forklifts++; }
                    else if (roll < 0.7)
                    {
                        for (int k = 0; k < 4; k++) Prop(bays, k % 2 == 0 ? "barrel_03" : "Barrel_01", p + new Vector3((k % 2) * 0.75f, 0f, (k / 2) * 0.75f), rng.Next(360));
                    }
                }
            }
            Flush("BayClutter", bays, detail, true);

            // ---- Industrial: pipe rack along the service road, gas cages, generators and cabinets at the substation.
            var ind = new GameObject("IndustrialKit").transform;
            ind.SetParent(root, false);
            for (float x = -600f; x < -420f; x += 7.6f)
            {
                var p = new Vector3(x, 0f, -131f);
                if (Clear(p, 2f)) Prop(ind, "modular_industrial_pipes_01", p, 0f, 1f);
            }
            foreach (var p in new[] { new Vector3(-530f, 0f, -150f), new Vector3(-522f, 0f, -150f), new Vector3(-514f, 0f, -150f) })
                if (Clear(p, 2f)) Prop(ind, "portable_generator", p, 90f, 1.6f, true, true);
            for (int i = 0; i < 10; i++)
            {
                var p = new Vector3(-600f + i * 1.3f, 0f, -170f);
                if (Clear(p, 0.6f)) Prop(ind, "propane_tank", p, rng.Next(360), 1.1f);
            }
            foreach (var p in new[] { new Vector3(-610f, 0f, -150f), new Vector3(-604f, 0f, -150f), new Vector3(-150f, 0f, -195f), new Vector3(-160f, 0f, -195f), new Vector3(470f, 0f, -195f), new Vector3(20f, 0f, -195f), new Vector3(200f, 0f, 92f), new Vector3(250f, 0f, 92f) })
                if (Clear(p, 1f)) Prop(ind, rng.NextDouble() < 0.5 ? "utility_box_01" : "utility_box_02", p, 0f, 1f, true, true);
            foreach (var p in new[] { new Vector3(40f, 4f, -296.3f), new Vector3(56f, 4f, -296.3f), new Vector3(-300f, 6f, -282.3f), new Vector3(-200f, 6f, -282.3f) })
                Prop(ind, "exterior_aircon_unit", p, 180f, 1f);

            // ---- Berth: tyre fenders hung on the quay face and lifebuoys on posts along the edge.
            var berth = new GameObject("BerthKit").transform;
            berth.SetParent(root, false);
            for (float x = -560f; x < -60f; x += 9f)
            {
                var tyre = Prop(berth, "old_tyre", new Vector3(x + 4.5f, -1.1f, QuayEdge + 0.95f), 0f, 1.8f);
                if (tyre != null) tyre.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            }
            Begin();
            for (float x = -680f; x < 860f; x += 60f)
            {
                var p = new Vector3(x, 0f, QuayEdge - 2.4f);
                if (!Clear(p, 0.5f)) continue;
                B(metal).Cylinder(p, 0.06f, 1.5f, 6, true);
                Prop(berth, "lifebuoy", p + new Vector3(0f, 0.8f, 0.07f), 0f, 1f);
            }
            Flush("BuoyPosts", berth, detail, false);

            // ---- Storm damage on the flooded quayside: a toppled stack, loose crates and barriers, closure signs.
            var storm = new GameObject("StormDamage").transform;
            storm.SetParent(root, false);
            Begin();
            StackBox(containers[2], new Vector3(400f, 1.22f, 262f), new Vector3(12.2f, 2.44f, 2.6f), Quaternion.Euler(0f, 18f, 90f));
            StackBox(containers[5 % containers.Length], new Vector3(414f, 1.3f, 270f), new Vector3(12.2f, 2.6f, 2.44f), Quaternion.Euler(0f, -32f, 0f));
            StackBox(containers[1], new Vector3(392f, 3.6f, 270f), new Vector3(12.2f, 2.6f, 2.44f), Quaternion.Euler(0f, 8f, 14f));
            for (int i = 0; i < 6; i++) Jersey(new Vector3(440f + i * 4.5f + (float)rng.NextDouble(), 0f, 252f + (float)rng.NextDouble() * 4f), rng.Next(360));
            for (int i = 0; i < 8; i++) Pallet(new Vector3(430f + (float)rng.NextDouble() * 40f, 0f, 258f + (float)rng.NextDouble() * 20f), rng.Next(360), rng);
            Flush("StormDebris", storm, environment, true);
            foreach (var (p, r) in new[] { (new Vector3(400f, 1.3f, 263f), new Vector3(12.6f, 2.6f, 2.8f)), (new Vector3(414f, 1.3f, 270f), new Vector3(12.2f, 2.6f, 4f)) })
                Collider(storm, "ToppledBox", p, r, environment);
            for (int i = 0; i < 6; i++) Prop(storm, i % 2 == 0 ? "wooden_crate_02" : "plastic_crate_02", new Vector3(455f + i * 3.1f, 0f, 272f + (i % 3) * 2.2f), rng.Next(360), 1.2f);
            SignPanel(storm, "QUAY CLOSED  ·  STORM", new Color(1f, 0.3f, 0.25f), new Vector3(470f, 3.2f, 244f), 180f, 7f, true);
            SignPanel(root, "ASSEMBLY POINT B", new Color(0.3f, 1f, 0.5f), new Vector3(150f, 3.2f, -145f), 180f, 6f, true);
            SignPanel(root, "STORM WARNING  ·  KEEP OFF THE QUAY", new Color(1f, 0.55f, 0.15f), new Vector3(-150f, 3.4f, 216f), 180f, 10f, true);
            SignPanel(root, "FLOOD ROUTE ↑", new Color(0.33f, 0.91f, 1f), new Vector3(458f, 3.2f, 40f), 270f, 5f, true);
            SignPanel(root, "EVACUATION ROUTE ←", new Color(0.3f, 1f, 0.5f), new Vector3(-12f, 3.2f, -120f), 90f, 6f, true);

            // ---- CCTV poles at the main junctions (decor; the working cameras are HarborSecurityCamera).
            Begin();
            int cctv = 0;
            foreach (var p in new[] { new Vector3(-12f, 0f, -180f), new Vector3(12f, 0f, -60f), new Vector3(100f, 0f, 70f), new Vector3(440f, 0f, 70f), new Vector3(652f, 0f, -40f), new Vector3(-160f, 0f, 90f), new Vector3(-440f, 0f, -40f), new Vector3(630f, 0f, 216f) })
            {
                if (!Clear(p, 0.5f)) continue;
                B(metal).Cylinder(p, 0.1f, 6f, 8, true);
                Prop(root, "security_camera_02", p + new Vector3(0f, 5.6f, 0f), rng.Next(360), 1.4f);
                cctv++;
            }
            Flush("CctvPoles", root, detail, false);

            // ---- Depot welder: sparks and a flickering arc light at a maintenance bay.
            var weld = new GameObject("DepotWelder").transform;
            weld.SetParent(root, false);
            weld.position = new Vector3(522f, 1.2f, -88f);
            var sparks = Particles("WeldSparks", weld, splash, 150, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                main.startColor = new Color(1f, 0.8f, 0.45f);
                main.gravityModifier = 1.4f;
                var e = ps.emission; e.rateOverTime = 0f;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.1f;
            });
            sparks.transform.localPosition = Vector3.zero;
            var arcGo = new GameObject("ArcLight") { layer = detail };
            arcGo.transform.SetParent(weld, false);
            var arc = arcGo.AddComponent<Light>();
            arc.type = LightType.Point; arc.color = new Color(0.75f, 0.85f, 1f); arc.range = 14f; arc.intensity = 0f; arc.shadows = LightShadows.None;
            var welder = weld.gameObject.AddComponent<HarborWelder>();
            Configure(welder, so => { so.FindProperty("sparks").objectReferenceValue = sparks; so.FindProperty("arc").objectReferenceValue = arc; });
            Begin();
            Forklift(new Vector3(530f, 0f, -84f), 200f);
            Flush("WelderForklift", weld, detail, true);

            // Two forklifts working loading bays (drive, set down, back out, lift, turn, return).
            WorkingForklift(root, "Forklift_Depot", new Vector3(545f, 0f, -81.5f), new Vector3(600f, 0f, -81.5f), reverseBeepClip);
            WorkingForklift(root, "Forklift_Logistics", new Vector3(82f, 0f, -544.5f), new Vector3(140f, 0f, -544.5f), reverseBeepClip);
            report.AppendLine($"density: {propCount} Poly Haven props ({propTris:N0} triangles), {trucks} held trucks, {cars} staff cars, {forklifts + 1} forklifts, {parkedPoliceKits.Count} parked police units, {cctv} CCTV poles, storm damage on the quayside, 3 logistics warehouses + police post");
        }

        private static Material claddingNavy() => claddingDark;
    }
}
