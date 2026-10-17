using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain 2.2 port truck: the original cab-over tractor (HarborRainBuilder.Tractor.cs) coupled to the skeletal
    /// container trailer (HarborRainBuilder.Trailer.cs). Two shared prefabs in Generated/Truck:
    /// <list type="bullet">
    /// <item>PF_HR_PortTruck — the working truck: root (movers drive it; kinematic body + box collider) → Body (pitch
    /// pivot for HarborVehicleDressing: tractor, trailer, livery shell, glass, lamps, beacon, headlight) + wheel pivots on
    /// the root (all spin; the front pair steers) + Body/Cargo (the container slot on the deck). Used by the moving
    /// terminal trucks (HarborLife) and the crew's transfer truck (HarborMuleTruck).</item>
    /// <item>PF_HR_PortTruckParked — the held trucks: the same geometry merged into one mesh per material group, no lights
    /// or scripts, mesh LODs; just a collider.</item>
    /// </list>
    /// Frame: +z forward, ground y 0, container centre at z 0 with its underside on the deck (<see cref="DeckY"/>).
    /// </summary>
    public static partial class HarborRainBuilder
    {
        public const string PortTruckPrefab = Gen + "/Truck/PF_HR_PortTruck.prefab";
        public const string ParkedTruckPrefab = Gen + "/Truck/PF_HR_PortTruckParked.prefab";
        public static readonly Vector3 TruckColliderCentre = new(0f, 2.05f, 1.9f), TruckColliderSize = new(2.6f, 4.1f, 16.7f);

        private static Mesh trailerMesh, trailerLampMesh, trailerWheelMesh, tractorMesh, tractorShellMesh, tractorGlassMesh, tractorLampMesh, beaconMesh;
        private static Material[] trailerMats, wheelMats, tractorMats;
        private static GameObject portTruck, parkedTruck;
        private static int markingN;

        private static void TrailerAssets()
        {
            environment = LayerMask.NameToLayer("Environment");
            detail = LayerMask.NameToLayer("Detail");
            if (metal == null || rubber == null || containers == null) BuildMaterials();
            TrailerMaterials();
            var w = new HarborTruckMesh();
            var lamps = new HarborTruckMesh();
            TrailerGeo(w, lamps);
            trailerMesh = SaveTruckMesh(w, "HR_TrailerChassis", 4);
            trailerMats = Mats(w);
            var wheel = TrailerWheelGeo();
            trailerWheelMesh = SaveTruckMesh(wheel, "HR_TruckWheel", 3);
            wheelMats = Mats(wheel);

            TractorMaterials();
            var t = new HarborTruckMesh();
            var shell = new HarborTruckMesh { Tile = 2f };
            var glass = new HarborTruckMesh();
            TractorGeo(t, shell, glass, lamps);
            trailerLampMesh = SaveTruckMesh(lamps, "HR_TruckTailLamps", 1);
            tractorMesh = SaveTruckMesh(t, "HR_TractorChassis", 4);
            tractorMats = Mats(t);
            tractorShellMesh = SaveTruckMesh(shell, "HR_TractorCab", 3);
            tractorGlassMesh = SaveTruckMesh(glass, "HR_TractorGlass", 1);
            var beaconW = new HarborTruckMesh();
            beaconW.Box(trPlastic, new Vector3(-0.12f, 0f, -0.12f), new Vector3(0.12f, 0.05f, 0.12f));
            beaconW.Revolve(beacon, new Vector3(0f, 0.05f, 0f), Vector3.up, new[] { new Vector2(0f, 0.1f), new Vector2(0.14f, 0.09f), new Vector2(0.17f, 0.05f), new Vector2(0.18f, 0f) }, 14);
            beaconMesh = SaveTruckMesh(beaconW, "HR_TruckBeacon", 1);
            report.AppendLine($"port truck: tractor {t.Triangles + shell.Triangles + glass.Triangles} tris ({tractorMats.Length} + 2 materials), trailer {w.Triangles} tris ({trailerMats.Length} materials), wheel {wheel.Triangles} tris × 16");
        }

        private static MeshRenderer Part(string name, Transform parent, Mesh mesh, Material[] mats, int layer, bool shadows)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return r;
        }

        /// <summary>Every wheel: (local position, steers, mirrored so the rim faces the other way).</summary>
        private static IEnumerable<(Vector3 pos, bool steer, bool flip, string name)> TruckWheelSpots()
        {
            foreach (var (z, x, steer, inner) in TractorWheels)
                foreach (float s in new[] { -1f, 1f })
                    yield return (new Vector3(s * x, TractorWheelR, z), steer, inner ? s > 0f : s < 0f, steer ? "FrontWheel" : "DriveWheel");
            foreach (float z in TrailerAxles)
                foreach (float s in new[] { -1f, 1f })
                    yield return (new Vector3(s * TrailerWheelX, TrailerWheelR, z), false, s < 0f, "TrailerWheel");
        }

        /// <summary>Builds and saves both truck prefabs (call after the materials exist).</summary>
        private static void BuildTruckPrefabs()
        {
            EnsureTruckDir();
            TrailerAssets();

            // ---- Working truck.
            var root = new GameObject("PF_HR_PortTruck") { layer = environment };
            var body = new GameObject("Body") { layer = environment }.transform;
            body.SetParent(root.transform, false);
            Part("Tractor", body, tractorMesh, tractorMats, environment, true);
            Part("CabShell", body, tractorShellMesh, new[] { tcPaints[0] }, environment, true);
            Part("CabGlass", body, tractorGlassMesh, new[] { tcGlass }, environment, false);
            Part("Trailer", body, trailerMesh, trailerMats, environment, true);
            Part("TailLamps", body, trailerLampMesh, new[] { tailLamp }, detail, false);
            var beaconR = Part("Beacon", body, beaconMesh, new[] { trPlastic, beacon }, detail, false);
            beaconR.transform.localPosition = new Vector3(0f, 3.57f, 9.0f);
            var cargo = new GameObject("Cargo") { layer = environment }.transform;
            cargo.SetParent(body, false);
            cargo.localPosition = new Vector3(0f, DeckY, 0f);
            foreach (var (pos, steer, flip, name) in TruckWheelSpots())
            {
                var pivot = new GameObject(name) { layer = environment }.transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = pos;
                var spinT = pivot;
                if (steer)
                {
                    spinT = new GameObject("Spin") { layer = environment }.transform;
                    spinT.SetParent(pivot, false);
                }
                var r = Part("Mesh", spinT, trailerWheelMesh, wheelMats, environment, true);
                if (flip) r.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            var hl = new GameObject("Headlight") { layer = detail };
            hl.transform.SetParent(body, false);
            hl.transform.localPosition = new Vector3(0f, 0.95f, CabFront + 0.3f);
            hl.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
            var l = hl.AddComponent<Light>();
            l.type = LightType.Spot; l.color = new Color(1f, 0.95f, 0.86f); l.intensity = 650f; l.range = 38f; l.spotAngle = 72f; l.innerSpotAngle = 38f; l.shadows = LightShadows.None;
            var c = root.AddComponent<BoxCollider>();
            c.center = TruckColliderCentre; c.size = TruckColliderSize;
            var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
            portTruck = PrefabUtility.SaveAsPrefabAsset(root, PortTruckPrefab);
            Object.DestroyImmediate(root);

            // ---- Parked truck: one merged mesh (chassis, trailer, wheels, lamps) + livery + glass, LODs, collider only.
            var merged = new HarborTruckMesh();
            var tw = new HarborTruckMesh(); var tl = new HarborTruckMesh(); var dummyShell = new HarborTruckMesh(); var dummyGlass = new HarborTruckMesh();
            TrailerGeo(merged, tl);
            TractorGeo(merged, dummyShell, dummyGlass, tl);
            merged.Append(tl);
            var wheelGeo = TrailerWheelGeo();
            foreach (var (pos, _, flip, _) in TruckWheelSpots())
            {
                merged.M = Matrix4x4.TRS(pos, flip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity, Vector3.one);
                merged.Append(wheelGeo);
            }
            merged.M = Matrix4x4.identity;
            var mergedMesh = SaveTruckMesh(merged, "HR_TruckParked", 4);
            var proot = new GameObject("PF_HR_PortTruckParked") { layer = environment };
            Part("Truck", proot.transform, mergedMesh, Mats(merged), environment, true);
            Part("CabShell", proot.transform, tractorShellMesh, new[] { tcPaints[0] }, environment, true);
            Part("CabGlass", proot.transform, tractorGlassMesh, new[] { tcGlass }, environment, false);
            var pcargo = new GameObject("Cargo") { layer = environment }.transform;
            pcargo.SetParent(proot.transform, false);
            pcargo.localPosition = new Vector3(0f, DeckY, 0f);
            var pc = proot.AddComponent<BoxCollider>();
            pc.center = TruckColliderCentre; pc.size = TruckColliderSize;
            parkedTruck = PrefabUtility.SaveAsPrefabAsset(proot, ParkedTruckPrefab);
            Object.DestroyImmediate(proot);
            report.AppendLine($"port truck prefabs: {PortTruckPrefab} (working), {ParkedTruckPrefab} (merged {merged.Triangles} tris, {merged.Materials.Count} materials)");
        }

        /// <summary>The working-truck parts an instance's scripts need.</summary>
        internal sealed class TruckRig
        {
            public GameObject Root;
            public Transform Body, Cargo;
            public readonly List<Transform> Spin = new(), Steer = new();
            public readonly List<Renderer> Brakes = new();
            public Renderer Beacon;
            public Light Headlight;
        }

        /// <summary>
        /// Places a truck instance. <paramref name="paint"/> picks the cab livery (index into the paint set), <paramref name="box"/>
        /// a container on the deck (null for an empty chassis). Working trucks get a <see cref="TruckRig"/>; parked ones don't.
        /// </summary>
        private static TruckRig PlaceTruck(Transform parent, string name, Vector3 pos, Quaternion rot, int paint, Material box, bool working)
        {
            var prefab = working ? portTruck : parkedTruck;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(pos, rot);
            var livery = tcPaints[Mathf.Abs(paint) % tcPaints.Length];
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name == "CabShell") r.sharedMaterial = livery;
            var cargo = go.transform.Find(working ? "Body/Cargo" : "Cargo");
            if (box != null) TruckContainer(cargo, box);
            if (!working) { GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic); return null; }
            var rig = new TruckRig { Root = go, Body = go.transform.Find("Body"), Cargo = cargo };
            foreach (Transform t in go.transform)
            {
                if (t.name == "FrontWheel") { rig.Steer.Add(t); rig.Spin.Add(t.Find("Spin")); }
                else if (t.name == "DriveWheel" || t.name == "TrailerWheel") rig.Spin.Add(t);
            }
            rig.Brakes.Add(rig.Body.Find("TailLamps").GetComponent<Renderer>());
            rig.Beacon = rig.Body.Find("Beacon").GetComponent<Renderer>();
            rig.Headlight = rig.Body.Find("Headlight").GetComponent<Light>();
            return rig;
        }

        /// <summary>A detailed ISO container on the deck slot, in <paramref name="paint"/>, with markings.</summary>
        private static GameObject TruckContainer(Transform cargoSlot, Material paint)
        {
            var mesh = ContainerMesh(paint);
            var r = Part("Container", cargoSlot, mesh, containerMats[paint], environment, true);
            ContainerMarkings(r.transform, 7 + markingN++ % 12);
            return r.gameObject;
        }

        /// <summary>Preview build of a working truck into <paramref name="scene"/> (used by HarborRainTruckPreview).</summary>
        public static GameObject PreviewTruck(Scene scene, Vector3 at, float yaw, StringBuilder log)
        {
            report.Clear();
            BuildTruckPrefabs();
            var rig = PlaceTruck(null, "TruckPreview", at, Quaternion.Euler(0f, yaw, 0f), 0, containers[6 % containers.Length], true);
            SceneManager.MoveGameObjectToScene(rig.Root, scene);
            log.Append(report);
            return rig.Root;
        }
    }
}
