using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NeonRift.Core;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain's two unlockable cars (Ferrari F40 Liberty Walk, Ferrari 296 Speciale A). Both sources are CSR2-style
    /// rigged exports (every part a SkinnedMeshRenderer, some parts posed open), so each is first flattened to a static
    /// bind-pose copy, then run through the project's stock <see cref="VehiclePrefabBuilder"/> (wheel rig, colliders,
    /// lights, audio, LODs). The prefabs, definitions and physics profiles live in Harbor Rain's own folder and are NOT
    /// added to the Night Run vehicle catalog.
    /// </summary>
    public static class HarborRainVehicles
    {
        private const string Root = "Assets/_NeonRift/Levels/HarborRain";
        private const string Out = Root + "/Generated/Vehicles";
        private const string Physics = "Assets/_NeonRift/Data/Vehicles/Physics/";
        private const string AudioData = "Assets/_NeonRift/Data/Vehicles/Audio/";

        private struct Recipe
        {
            public string Id, Name, Maker, Category, Description, Model, Prefab, PhysicsFrom, AudioFrom;
            public string[] Wheel, Static, Hidden, Lights;
            public float Mass, Torque, FrontWeight, WheelDiameter;
            public Vector3 Rotation;
            public AssetLicense License;
        }

        private static Recipe[] Recipes() => new[]
        {
            new Recipe
            {
                Id = "hr_ferrari_f40_lb", Name = "F40 LB-WORKS", Maker = "Ferrari", Category = "Widebody Supercar",
                Description = "Liberty Walk widebody F40: twin-turbo V8, rear-wheel drive, light and twitchy. A Harbor Rain car. Handling values are this game's physics model, not factory figures.",
                Model = Root + "/Imported/Vehicles/Ferrari_F40_LB/ferrari_f40_lb.glb", Prefab = "PF_HR_FerrariF40LB",
                PhysicsFrom = "VehiclePhysics_MercedesSLSGT3", AudioFrom = "VehicleAudio_MercedesSLS",
                Wheel = new[] { "wheel_" }, Static = new[] { "brake", "caliper", "susp", "hub" },
                Hidden = new[] { "slipstream", "Sketchfab_model", "damage" },
                Lights = new[] { "headlight", "taillight", "extralight" },
                Mass = 1180f, Torque = 610f, FrontWeight = 0.42f, WheelDiameter = 0.68f,
                License = new AssetLicense { LicenseId = "UNCONFIRMED", Author = "VTX (glb copyright field)", SourceUrl = "https://sketchfab.com/3d-models/lb-works-ferrari-f40-free-44e64fd0ed6d45188512e8ab08c89272 (probable)", CommercialUseAllowed = false }
            },
            new Recipe
            {
                Id = "hr_ferrari_296_speciale_a", Name = "296 SPECIALE A", Maker = "Ferrari", Category = "Hybrid Spider",
                Description = "Mid-engined V6 plug-in hybrid spider. Strong low-down torque, stable on the brakes. A Harbor Rain car. Handling values are this game's physics model, not factory figures.",
                Model = Root + "/Imported/Vehicles/Ferrari_296_SpecialeA/ferrari_296_speciale_a.glb", Prefab = "PF_HR_Ferrari296SpecialeA",
                PhysicsFrom = "VehiclePhysics_MercedesSLSGT3", AudioFrom = "VehicleAudio_MercedesSLSGT3",
                Wheel = new[] { "Wheel1A_" }, Static = new[] { "Caliper" },
                Hidden = new[] { "GroundShadow", "GlassInside" },
                Lights = new[] { "ChassisLight", "DoorLLight", "DoorRLight" },
                Mass = 1460f, Torque = 760f, FrontWeight = 0.42f, WheelDiameter = 0f,
                // The bind pose is Z-up with the nose on -Y.
                Rotation = new Vector3(-90f, 0f, 0f),
                License = new AssetLicense { LicenseId = "CC-BY-NC-SA-4.0 (unconfirmed)", Author = "OUTPISTON (probable; model and texture from CSR2)", SourceUrl = "https://sketchfab.com/3d-models/2026-ferrari-296-speciale-a-3b7f4d3f48364a5c832ca3b94272f759", CommercialUseAllowed = false }
            }
        };

        [MenuItem("Neon Rift/Harbor Rain/Build Level 2 Cars")]
        public static string Build()
        {
            var log = new StringBuilder("[HarborRain] Level 2 cars\n");
            Directory.CreateDirectory(Out);
            AssetDatabase.Refresh();
            foreach (var r in Recipes())
            {
                try { log.AppendLine(BuildOne(r)); }
                catch (System.Exception e) { log.AppendLine($"  {r.Id}: FAILED {e.Message}"); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/HarborRainVehicles.txt", log.ToString());
            Debug.Log(log.ToString());
            return log.ToString();
        }

        private static string shadowNote = "";

        private static string MeshLods(GameObject src, string folder)
        {
            string dir = $"{folder}/MeshLods";
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder(folder, "MeshLods");
            int meshes = 0, before = 0;
            var done = new Dictionary<Mesh, Mesh>();
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mf.TryGetComponent(out MeshRenderer mr) || !mr.enabled) continue;
                int tris = 0;
                for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)mesh.GetIndexCount(i) / 3;
                if (tris < 1500) continue;
                if (!done.TryGetValue(mesh, out var copy))
                {
                    copy = Object.Instantiate(mesh);
                    copy.name = mesh.name + "_lods";
                    MeshLodUtility.GenerateMeshLods(copy, 5);
                    AssetDatabase.CreateAsset(copy, $"{dir}/{System.IO.Path.GetFileName(copy.name.Replace('/', '_'))}_{meshes}.asset");
                    done[mesh] = copy;
                    meshes++;
                    before += tris;
                }
                mf.sharedMesh = copy;
            }
            return $"{meshes} dense meshes ({before:N0} triangles) given mesh LODs";
        }

        private static string BuildOne(Recipe r)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(r.Model);
            if (model == null) throw new FileNotFoundException(r.Model);
            string folder = $"{Out}/{r.Prefab}";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();

            // 1. Static bind-pose source: skinned parts become mesh renderers using the same meshes and materials.
            var src = (GameObject)Object.Instantiate(model);
            int flattened = 0;
            try
            {
                foreach (var a in src.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                foreach (var a in src.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
                foreach (var smr in src.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh;
                    var mats = smr.sharedMaterials;
                    var go = smr.gameObject;
                    bool on = smr.enabled;
                    Object.DestroyImmediate(smr);
                    if (mesh == null) continue;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    mr.enabled = on;
                    flattened++;
                }
                // Drop zero-size helper meshes and anything matching the hidden keywords outright (keeps the prefab lean).
                foreach (var mr in src.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var path = mr.transform.name;
                    if (r.Hidden.Any(k => path.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) || mr.bounds.size.sqrMagnitude < 1e-6f)
                        mr.enabled = false;
                }
                // The F40's rear mesh grille is a flat 1.7 × 0.3 m panel modelled as 205k triangles: swap it for a dark
                // stand-in panel of the same size (it reads as the same black mesh from the chase camera).
                foreach (var mr in src.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.name != "f40lb_part_5_boot_ng") continue;
                    var b = mr.bounds;
                    mr.enabled = false;
                    var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(panel.GetComponent<Collider>());
                    panel.name = "RearGrille_StandIn";
                    panel.transform.SetParent(mr.transform.parent, true);
                    panel.transform.position = b.center;
                    panel.transform.rotation = Quaternion.identity;
                    panel.transform.localScale = Vector3.one;
                    var ls = panel.transform.lossyScale;
                    panel.transform.localScale = new Vector3(b.size.x / ls.x, b.size.y / ls.y, 0.02f / ls.z);
                    var grille = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Generated/Materials/HR_Rubber.mat");
                    if (grille != null) panel.GetComponent<MeshRenderer>().sharedMaterial = grille;
                }
                // Performance: these exports are hundreds of small meshes. Only body-sized parts cast shadows (the shadow
                // passes were re-drawing every bolt and interior piece), which keeps the silhouette's shadow intact.
                int shadowOff = 0;
                foreach (var mr in src.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var size = mr.bounds.size;
                    if (Mathf.Max(size.x, size.z) < 1.6f || mr.name.IndexOf("interior", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mr.name.IndexOf("seat", System.StringComparison.OrdinalIgnoreCase) >= 0 || mr.name.IndexOf("engine", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        shadowOff++;
                    }
                }
                shadowNote = $"{shadowOff} small/interior parts without shadows";
                // Mesh LODs (Unity 6 Mesh LOD): every dense part gets simplified levels embedded in a Harbor Rain copy of its
                // mesh; the renderer picks a level from its own screen size, so small parts (interior, engine bay, bolts)
                // drop detail even when the car itself is close to the camera. The imported originals are not touched.
                shadowNote += "; " + MeshLods(src, folder);
                PrefabUtility.SaveAsPrefabAsset(src, $"{folder}/{r.Prefab}_Source.prefab");
            }
            finally { Object.DestroyImmediate(src); }

            // 2. Stock vehicle prefab builder (same rig, colliders, lights, audio and LODs as every Night Run car).
            var setup = ScriptableObject.CreateInstance<VehicleModelSetup>();
            setup.sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{r.Prefab}_Source.prefab");
            setup.prefabName = r.Prefab;
            setup.targetWheelDiameter = r.WheelDiameter;
            setup.bodyRotation = r.Rotation;
            setup.wheelKeywords = r.Wheel;
            setup.staticWheelKeywords = r.Static;
            setup.motionBlurWheelKeywords = new string[0];
            setup.hiddenKeywords = r.Hidden;
            setup.lightKeywords = r.Lights;
            var report = VehiclePrefabBuilder.Build(setup);
            Object.DestroyImmediate(setup);

            // Move the outputs out of the shared vehicle folders into Harbor Rain's own.
            string prefabPath = $"{folder}/{r.Prefab}.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
            string moved = AssetDatabase.MoveAsset(report.PrefabPath, prefabPath);
            if (!string.IsNullOrEmpty(moved)) throw new IOException(moved);
            string art = $"{VehiclePrefabBuilder.GeneratedFolder}/{r.Prefab}";
            if (AssetDatabase.IsValidFolder(art))
            {
                AssetDatabase.DeleteAsset($"{folder}/Art");
                moved = AssetDatabase.MoveAsset(art, $"{folder}/Art");
                if (!string.IsNullOrEmpty(moved)) throw new IOException(moved);
            }

            // 3. Physics: a copy of a stock profile with this car's mass, torque and balance (stock assets untouched).
            string physPath = $"{folder}/Physics_{r.Prefab}.asset";
            AssetDatabase.DeleteAsset(physPath);
            AssetDatabase.CopyAsset($"{Physics}{r.PhysicsFrom}.asset", physPath);
            var phys = AssetDatabase.LoadAssetAtPath<VehiclePhysicsProfile>(physPath);
            var pso = new SerializedObject(phys);
            pso.FindProperty("chassis.mass").floatValue = r.Mass;
            pso.FindProperty("chassis.frontWeightDistribution").floatValue = r.FrontWeight;
            pso.FindProperty("engine.peakTorque").floatValue = r.Torque;
            pso.FindProperty("transmission.driveType").intValue = 0;
            var notes = pso.FindProperty("tuningNotes");
            if (notes != null) notes.stringValue = $"Harbor Rain copy of {r.PhysicsFrom}: mass {r.Mass} kg, peak torque {r.Torque} Nm, front weight {r.FrontWeight:0.00}.";
            pso.ApplyModifiedPropertiesWithoutUndo();

            string defPath = $"{folder}/Vehicle_{r.Prefab}.asset";
            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(defPath);
            if (def == null) { def = ScriptableObject.CreateInstance<VehicleDefinition>(); AssetDatabase.CreateAsset(def, defPath); }
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = r.Id;
            so.FindProperty("displayName").stringValue = r.Name;
            so.FindProperty("manufacturer").stringValue = r.Maker;
            so.FindProperty("category").stringValue = r.Category;
            so.FindProperty("description").stringValue = r.Description;
            so.FindProperty("gameplayPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            so.FindProperty("physicsProfile").objectReferenceValue = phys;
            so.FindProperty("audioProfile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VehicleAudioProfile>($"{AudioData}{r.AudioFrom}.asset");
            so.FindProperty("displayStatsProvisional").boolValue = true;
            var lic = so.FindProperty("license");
            lic.FindPropertyRelative("LicenseId").stringValue = r.License.LicenseId;
            lic.FindPropertyRelative("Author").stringValue = r.License.Author;
            lic.FindPropertyRelative("SourceUrl").stringValue = r.License.SourceUrl;
            lic.FindPropertyRelative("CommercialUseAllowed").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return $"  {r.Id}: {flattened} skinned parts flattened, {shadowNote}; {report.Summary}\n    → {prefabPath}";
        }
    }
}
