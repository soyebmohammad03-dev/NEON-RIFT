using System.Collections.Generic;
using System.Linq;
using NeonRift.Core;
using NeonRift.EditorTools.Vehicles;
using NeonRift.EditorTools.World;
using NeonRift.Vehicles;
using NeonRift.World;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools
{
    /// <summary>
    /// Re-runnable recipe for every supplied third-party model: vehicle prefabs + definitions,
    /// building materials + prefabs + definitions, and both catalogs. Audit notes: Docs/ThirdParty.md.
    /// </summary>
    public static class ThirdPartyIntake
    {
        private const string Src = "Assets/ThirdParty/Sketchfab/";
        private const string VehicleData = "Assets/_NeonRift/Data/Vehicles/";
        private const string SetupFolder = "Assets/_NeonRift/Data/Vehicles/ImportSetups/";
        private const string WorldData = "Assets/_NeonRift/Data/World/";

        private static readonly AssetLicense DaveLoveSls = new() { LicenseId = "CC-BY-4.0", Author = "Dave Love SketchFab", SourceUrl = "https://sketchfab.com/3d-models/2010-mercedes-sls-amg-fa3fd5eeea674f37bb03283f2c53d563", CommercialUseAllowed = true };
        private static readonly AssetLicense DaveLoveGt3 = new() { LicenseId = "CC-BY-4.0", Author = "Dave Love SketchFab", SourceUrl = "https://sketchfab.com/3d-models/mercedes-sls-gt3-5ec93bea4816494cbe3e7333bbfca5f6", CommercialUseAllowed = true };
        private static readonly AssetLicense VtxGt3 = new() { LicenseId = "CC-BY-NC-SA-4.0", Author = "VTX", SourceUrl = "https://sketchfab.com/3d-models/mercedes-amg-gt3-red-bull-racing-035f061cb6624effbc483d5f9b5ecaba", CommercialUseAllowed = false };
        private static readonly AssetLicense SdcTerzo = new() { LicenseId = "CC-BY-NC-4.0", Author = "SDC PERFORMANCE", SourceUrl = "https://sketchfab.com/3d-models/free-lamborghini-terzo-millennio-7ad3dffa9d344c3c978eafcc220cb709", CommercialUseAllowed = false };
        private static AssetLicense Miles(string url) => new() { LicenseId = "CC-BY-4.0", Author = "99.Miles", SourceUrl = url, CommercialUseAllowed = true };

        [MenuItem("Neon Rift/Assets/Run Third-Party Intake")]
        public static void RunFromMenu() => Debug.Log(Run());

        [MenuItem("Neon Rift/Assets/Rebuild Vehicle Prefabs And Catalog")]
        public static void RunVehiclesFromMenu()
        {
            var log = new System.Text.StringBuilder();
            BuildVehicles(log);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        public static string Run()
        {
            var log = new System.Text.StringBuilder();
            BuildVehicles(log);
            BuildBuildings(log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // ---------------- Vehicles ----------------

        private struct VehicleRecipe
        {
            public string Id, Name, Maker, Category, Description, Model, Prefab;
            public Vector3 Rotation;
            public float WheelDiameter;
            public string[] Wheel, Static, Blur, Hidden, Lights;
            public VehicleDisplayStats Stats;
            public AssetLicense License;
            /// <summary>Non-null keeps the vehicle out of the player catalog, with the reason.</summary>
            public string ExcludeReason;
        }

        /// <summary>
        /// Re-applies the player-facing copy (name, maker, category, description) from the recipes to the existing
        /// definitions, without rebuilding prefabs or touching measured stats.
        /// </summary>
        [MenuItem("Neon Rift/Vehicles/Apply Vehicle Copy From Recipes")]
        public static string ApplyVehicleCopy()
        {
            var log = new System.Text.StringBuilder("[Vehicle] copy\n");
            foreach (var r in VehicleRecipes())
            {
                var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>($"{VehicleData}Vehicle_{r.Prefab.Replace("PF_Vehicle_", "")}.asset");
                if (def == null) continue;
                var so = new SerializedObject(def);
                so.FindProperty("displayName").stringValue = r.Name;
                so.FindProperty("manufacturer").stringValue = r.Maker;
                so.FindProperty("category").stringValue = r.Category ?? string.Empty;
                so.FindProperty("description").stringValue = r.Description;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                log.AppendLine($"  {r.Id}: {r.Category}");
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static IEnumerable<VehicleRecipe> VehicleRecipes()
        {
            yield return new VehicleRecipe
            {
                Id = "mercedes_sls_amg_2010", Name = "SLS AMG", Maker = "Mercedes-Benz", Category = "Grand Tourer",
                Description = "Front-mid V8 gullwing. Long bonnet, rear-wheel drive, stable at speed and forgiving over kerbs: the car that gets the crew in and out.",
                Model = "Vehicles/Mercedes_SLS_AMG_2010/2010_mercedes_sls_amg.glb", Prefab = "PF_Vehicle_MercedesSLS",
                Wheel = new[] { "Rim", "Tyre", "Brake_" }, Static = new[] { "Caliper" }, Blur = new[] { "Blurred" },
                Hidden = new[] { "DAMAGE_GLASS" }, Lights = new[] { "Lights_Front", "Lights_Rear", "Microluci" },
                Stats = new VehicleDisplayStats { topSpeedKph = 317, zeroToHundredSeconds = 3.8f, powerHp = 571, massKg = 1620, handlingRating = 7 },
                License = DaveLoveSls
            };
            yield return new VehicleRecipe
            {
                Id = "mercedes_sls_gt3", Name = "SLS AMG GT3", Maker = "Mercedes-AMG", Category = "GT3 Race Car",
                Description = "Customer GT3 car on the SLS platform. Lighter, stiffer, big rear wing: the sharpest turn-in and the shortest braking in the garage.",
                Model = "Vehicles/Mercedes_SLS_GT3/mercedes_sls_gt3.glb", Prefab = "PF_Vehicle_MercedesSLSGT3",
                Wheel = new[] { "EXT_Rim", "EXT_Tyre", "EXT_Brake" }, Static = new[] { "Caliper" }, Blur = new[] { "Rim_Blur" },
                Hidden = new[] { "DAMAGE_GLASS" }, Lights = new[] { "EXT_Lights_Front", "EXT_Lights_Rear", "Microluci" },
                Stats = new VehicleDisplayStats { topSpeedKph = 290, zeroToHundredSeconds = 3.5f, powerHp = 580, massKg = 1335, handlingRating = 9 },
                License = DaveLoveGt3
            };
            yield return new VehicleRecipe
            {
                Id = "mercedes_amg_gt3", Name = "AMG GT3", Maker = "Mercedes-AMG", Category = "GT3 Race Car",
                Description = "Modern GT3 race car. Aero-heavy and precise; demands commitment.",
                Model = "Vehicles/Mercedes_AMG_GT3_RedBull/mercedes-amg_gt3_red_bull_racing.glb", Prefab = "PF_Vehicle_MercedesAMGGT3",
                Rotation = new Vector3(0, 90, 0),
                Wheel = new[] { "Rim_Inst", "Tyre_Inst", "Brake_Disc", "Caliper_Inst" }, Static = new[] { "Caliper" }, Blur = new string[0],
                Hidden = new string[0], Lights = new[] { "AMG_GT3_Evo_Lights" },
                Stats = new VehicleDisplayStats { topSpeedKph = 290, zeroToHundredSeconds = 3.4f, powerHp = 550, massKg = 1285, handlingRating = 9.5f },
                License = VtxGt3,
                ExcludeReason = "Window glass renders flat cyan and livery shows white speckle at runtime even with clearcoat remap + Stop NaN; 497k tris; NC-SA licence. Needs material rebuild and LODs."
            };
            yield return new VehicleRecipe
            {
                Id = "lamborghini_terzo_millennio", Name = "Terzo Millennio", Maker = "Lamborghini", Category = "Electric Hypercar Concept",
                Description = "Electric concept with a motor at every wheel. Brutal off the line and quiet until it isn't. The figures shown are from the game's physics model; Lamborghini publishes none.",
                Model = "Vehicles/Lamborghini_Terzo_Millennio/free__lamborghini_terzo_millennio.glb", Prefab = "PF_Vehicle_LamborghiniTerzo",
                WheelDiameter = 0.70f,
                Wheel = new[] { "wheelFR_", "Roues" }, Static = new string[0], Blur = new[] { "motion" },
                // Source ships an animation helper and two animation-rigged panels displaced from the body.
                Hidden = new[] { "<Wheel.001", "detach_03_5_details_orange_accents", "detach_wing_10" },
                Lights = new[] { "Lumire_blanche", "Lumire_rouge", "emissive_ID_rear" },
                Stats = new VehicleDisplayStats { topSpeedKph = 320, zeroToHundredSeconds = 2.8f, powerHp = 800, massKg = 1600, handlingRating = 8 },
                License = SdcTerzo
            };
        }

        private static void BuildVehicles(System.Text.StringBuilder log)
        {
            VehiclePrefabBuilder.EnsureFolder(SetupFolder.TrimEnd('/'));
            var definitions = new List<VehicleDefinition>();
            foreach (var r in VehicleRecipes())
            {
                var setupPath = $"{SetupFolder}Setup_{r.Prefab}.asset";
                var setup = AssetDatabase.LoadAssetAtPath<VehicleModelSetup>(setupPath);
                if (setup == null) { setup = ScriptableObject.CreateInstance<VehicleModelSetup>(); AssetDatabase.CreateAsset(setup, setupPath); }
                setup.sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(Src + r.Model);
                setup.prefabName = r.Prefab;
                setup.bodyRotation = r.Rotation;
                setup.targetWheelDiameter = r.WheelDiameter;
                setup.wheelKeywords = r.Wheel;
                setup.staticWheelKeywords = r.Static;
                setup.motionBlurWheelKeywords = r.Blur;
                setup.hiddenKeywords = r.Hidden;
                setup.lightKeywords = r.Lights;
                EditorUtility.SetDirty(setup);

                var report = VehiclePrefabBuilder.Build(setup);
                log.AppendLine("[Vehicle] " + report.Summary);

                var defPath = $"{VehicleData}Vehicle_{r.Prefab.Replace("PF_Vehicle_", "")}.asset";
                var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(defPath);
                bool isNew = def == null;
                if (isNew) { def = ScriptableObject.CreateInstance<VehicleDefinition>(); AssetDatabase.CreateAsset(def, defPath); }
                var so = new SerializedObject(def);
                so.FindProperty("id").stringValue = r.Id;
                so.FindProperty("displayName").stringValue = r.Name;
                so.FindProperty("manufacturer").stringValue = r.Maker;
                so.FindProperty("description").stringValue = r.Description;
                so.FindProperty("category").stringValue = r.Category ?? string.Empty;
                so.FindProperty("gameplayPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(report.PrefabPath);
                so.FindProperty("showroomPrefab").objectReferenceValue = null;
                // Recipe stats only seed new definitions; afterwards Car Select stats are measured from physics
                // (Neon Rift ▸ Vehicles ▸ Update Car Select Stats From Physics) and must not be overwritten.
                if (isNew)
                {
                    SetStats(so.FindProperty("displayStats"), r.Stats);
                    so.FindProperty("displayStatsProvisional").boolValue = true;
                }
                SetLicense(so.FindProperty("license"), r.License);
                so.ApplyModifiedPropertiesWithoutUndo();
                if (r.ExcludeReason == null) definitions.Add(def);
                else log.AppendLine($"[Vehicle] {r.Id} kept out of catalog: {r.ExcludeReason}");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<VehicleCatalog>(VehicleData + "VehicleCatalog.asset");
            var cso = new SerializedObject(catalog);
            var list = cso.FindProperty("vehicles");
            list.arraySize = definitions.Count;
            for (int i = 0; i < definitions.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            cso.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"[Vehicle] catalog now has {catalog.Count} vehicles");
        }

        private static void SetStats(SerializedProperty p, VehicleDisplayStats s)
        {
            p.FindPropertyRelative("topSpeedKph").floatValue = s.topSpeedKph;
            p.FindPropertyRelative("zeroToHundredSeconds").floatValue = s.zeroToHundredSeconds;
            p.FindPropertyRelative("powerHp").floatValue = s.powerHp;
            p.FindPropertyRelative("massKg").floatValue = s.massKg;
            p.FindPropertyRelative("handlingRating").floatValue = s.handlingRating;
        }

        private static void SetLicense(SerializedProperty p, AssetLicense l)
        {
            p.FindPropertyRelative("LicenseId").stringValue = l.LicenseId;
            p.FindPropertyRelative("Author").stringValue = l.Author;
            p.FindPropertyRelative("SourceUrl").stringValue = l.SourceUrl;
            p.FindPropertyRelative("CommercialUseAllowed").boolValue = l.CommercialUseAllowed;
        }

        // ---------------- Buildings ----------------

        private static void BuildBuildings(System.Text.StringBuilder log)
        {
            VehiclePrefabBuilder.EnsureFolder(WorldData.TrimEnd('/'));
            var defs = new List<BuildingDefinition>();

            var london = BuildingPrefabBuilder.FromModel(AssetDatabase.LoadAssetAtPath<GameObject>(Src + "Buildings/London_Skyscraper/free_london_skyscraper.glb"),
                "PF_Bld_LondonSkyscraper", 1f, "Sidewalk", true);
            defs.Add(Define("london_skyscraper", "London Skyscraper", london, BuildingTier.Hero,
                "Landmark tower with its own sidewalk and lit lobby interior. 143k tris: use once, near the route.",
                Miles("https://sketchfab.com/3d-models/free-london-skyscraper-52b73f6ea18a440cb42734840d5edc72"), true, log));

            var singapore = BuildingPrefabBuilder.FromModel(AssetDatabase.LoadAssetAtPath<GameObject>(Src + "Buildings/Singapore_Office_Skyscraper/singapore_office_skyscraper_free.glb"),
                "PF_Bld_SingaporeOffice", 1f, null, true);
            defs.Add(Define("singapore_office", "Singapore Office Tower", singapore, BuildingTier.Hero,
                "Office tower with podium and interior floors behind glass. Roadside hero.",
                Miles("https://sketchfab.com/3d-models/singapore-office-skyscraper-free-2305f0fe03ba44229a1f768dcb48bd8f"), true, log));

            defs.AddRange(BuildAsianPack(log));
            defs.Add(BuildSkylinePack(log));

            var catPath = WorldData + "BuildingCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(catPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BuildingCatalog>(); AssetDatabase.CreateAsset(catalog, catPath); }
            var so = new SerializedObject(catalog);
            var list = so.FindProperty("buildings");
            list.arraySize = defs.Count;
            for (int i = 0; i < defs.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = defs[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"[Building] catalog: {defs.Count} buildings (hero {defs.Count(d => d.Tier == BuildingTier.Hero)}, mid {defs.Count(d => d.Tier == BuildingTier.Midground)}, skyline {defs.Count(d => d.Tier == BuildingTier.Skyline)})");
        }

        private static List<BuildingDefinition> BuildAsianPack(System.Text.StringBuilder log)
        {
            const string pack = Src + "Buildings/Asian_Night_City_Buildings/";
            const string tex = pack + "Textures/";
            const string art = BuildingPrefabBuilder.ArtFolder + "/AsianNightCity/";
            var license = Miles("https://sketchfab.com/3d-models/asian-themed-low-poly-night-city-buildings-9f0343aff4814b758dc6e905aba5b5e0");

            Texture2D T(string f) => AssetDatabase.LoadAssetAtPath<Texture2D>(tex + f);
            string F(string f) => System.IO.Path.GetFullPath(tex + f);

            var b1 = BuildingPrefabBuilder.LitMaterial(art + "M_AsianNight_B1.mat", T("BACKGROUND_BUILDING_1_DIFFUSE_TEST.png"),
                BuildingPrefabBuilder.PackSmoothness(F("BACKGROUND_BUILDING_1_ROUGHNESS.png"), art + "T_AsianNight_B1_MetallicSmoothness.png"),
                null, T("BACKGROUND_BUILDING_1_EMMISIVE_LIGHTBAKED_.png"), 1.5f, false);
            var b2 = BuildingPrefabBuilder.LitMaterial(art + "M_AsianNight_B2.mat",
                BuildingPrefabBuilder.PackAlpha(F("BACKGROUND_BUILDING_2_DIFFUSE_TEST.png"), F("BACKGROUND_BUILDING_2_OPACITY.png"), art + "T_AsianNight_B2_BaseAlpha.png"),
                BuildingPrefabBuilder.PackSmoothness(F("BACKGROUND_BUILDING_2_ROUGHNESS.png"), art + "T_AsianNight_B2_MetallicSmoothness.png"),
                null, T("BACKGROUND_BUILDING_2_EMMISIVE_LIGHTBAKED_.png"), 1.5f, true);
            var red = BuildingPrefabBuilder.AdditiveMaterial(art + "M_AsianNight_RedFlare.mat",
                BuildingPrefabBuilder.PackAlpha(F("RED_FLARE_DIFFUSE.png"), F("RED_FLARE_OPACITY.png"), art + "T_AsianNight_RedFlare.png"), Color.white * 2f);
            var white = BuildingPrefabBuilder.AdditiveMaterial(art + "M_AsianNight_WhiteFlare.mat",
                BuildingPrefabBuilder.PackAlpha(F("WHITE_FLARE.png"), F("WHITE_FLARE_OPACITY.png"), art + "T_AsianNight_WhiteFlare.png"), Color.white * 1.5f);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(pack + "BACKGROUND_CITY_SKYLINE.fbx");
            var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            var buildings = renderers.Where(r => r.name.StartsWith("BACKGROUND_")).ToList();
            var flares = renderers.Where(r => r.name.IndexOf("FLARE", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var defs = new List<BuildingDefinition>();
            static Bounds AssetBounds(MeshRenderer r) =>
                GeometryUtility.CalculateBounds(r.GetComponent<MeshFilter>().sharedMesh.vertices, r.transform.localToWorldMatrix);

            foreach (var b in buildings)
            {
                var bb = AssetBounds(b);
                var mat = b.sharedMaterial.name.Contains("BUILDINGS_1") ? b1 : b2;
                var parts = new List<(Transform, Material[])> { (b.transform, new[] { mat }) };
                foreach (var f in flares)
                {
                    var c = AssetBounds(f).center;
                    if (c.x >= bb.min.x && c.x <= bb.max.x && c.z >= bb.min.z && c.z <= bb.max.z)
                        parts.Add((f.transform, new[] { f.name.StartsWith("RED") ? red : white }));
                }
                var suffix = b.name.Replace("BACKGROUND_BUILDINGS_1", "B1").Replace("BACKGROUND_BUILDING_2", "B2").Replace(".", "_");
                var tris = (int)b.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3;
                var tier = tris >= 200 ? BuildingTier.Midground : BuildingTier.Skyline;
                var res = BuildingPrefabBuilder.FromMeshes($"PF_Bld_AsianNight_{suffix}", parts, tier == BuildingTier.Midground);
                defs.Add(Define($"asian_night_{suffix.ToLowerInvariant()}", $"Asian Night {suffix}", res, tier,
                    "Low-poly with baked window light. Reads well beyond ~150 m; too flat for the road edge.", license, tier == BuildingTier.Midground, log));
            }
            return defs;
        }

        private static BuildingDefinition BuildSkylinePack(System.Text.StringBuilder log)
        {
            const string pack = Src + "Buildings/Night_City_Building_Skyline/";
            const string art = BuildingPrefabBuilder.ArtFolder + "/NightSkyline/";
            BuildingPrefabBuilder.ConfigureTexture(pack + "Textures/Normal.png", true, true);
            var mat = BuildingPrefabBuilder.LitMaterial(art + "M_NightSkyline.mat",
                AssetDatabase.LoadAssetAtPath<Texture2D>(pack + "Textures/Diffuse.png"),
                BuildingPrefabBuilder.PackSmoothness(System.IO.Path.GetFullPath(pack + "Textures/Roughness.png"), art + "T_NightSkyline_MetallicSmoothness.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(pack + "Textures/Normal.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(pack + "Textures/Emmsive.png"), 1.5f, false);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(pack + "Background_Buildings.fbx");
            var r = model.GetComponentInChildren<MeshRenderer>();
            // Authored at 1/10 scale: ~35 storeys of windows on a 14.8 m mesh. x10 gives ~3.5 m per storey.
            var res = BuildingPrefabBuilder.FromMeshes("PF_Bld_NightSkylineCluster", new List<(Transform, Material[])> { (r.transform, new[] { mat }) }, false, 10f);
            return Define("night_skyline_cluster", "Night Skyline Cluster", res, BuildingTier.Skyline,
                "12 towers merged into one mesh (cannot be separated without splitting). Distant skyline only.",
                Miles("https://sketchfab.com/3d-models/low-poly-night-city-building-skyline-b0035b8713b048bb8ddf311ee67c28c8"), false, log);
        }

        private static BuildingDefinition Define(string id, string name, BuildingPrefabBuilder.Result res, BuildingTier tier, string notes,
                                                 AssetLicense license, bool collider, System.Text.StringBuilder log)
        {
            var path = $"{WorldData}Building_{System.IO.Path.GetFileNameWithoutExtension(res.PrefabPath).Replace("PF_Bld_", "")}.asset";
            var def = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
            if (def == null) { def = ScriptableObject.CreateInstance<BuildingDefinition>(); AssetDatabase.CreateAsset(def, path); }
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = name;
            so.FindProperty("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(res.PrefabPath);
            so.FindProperty("tier").enumValueIndex = (int)tier;
            so.FindProperty("size").vector3Value = res.Size;
            so.FindProperty("hasCollider").boolValue = collider;
            so.FindProperty("triangleCount").intValue = res.Triangles;
            so.FindProperty("notes").stringValue = notes;
            SetLicense(so.FindProperty("license"), license);
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"[Building] {id}: {tier} size={res.Size:F1} tris={res.Triangles}");
            return def;
        }
    }
}
