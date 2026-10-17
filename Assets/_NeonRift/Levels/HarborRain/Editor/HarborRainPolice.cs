using System.IO;
using System.Linq;
using System.Text;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain's port police car: the project's existing traffic-pack sedan (already imported, used by Night Run's
    /// traffic) in a white police livery with a blue/yellow checker band painted onto its own texture, run through the
    /// stock <see cref="VehiclePrefabBuilder"/> (same wheel rig, colliders, lights, audio and LODs as every drivable car),
    /// then fitted with a roof lightbar (red / blue lenses, two small real lights), a push bar, a pillar searchlight and a
    /// 3D siren (<see cref="HarborPoliceCar"/>). Everything is written to Harbor Rain's own folder; the catalog and the
    /// traffic prefabs are not touched.
    /// </summary>
    public static class HarborRainPolice
    {
        private const string Root = "Assets/_NeonRift/Levels/HarborRain";
        private const string Prefab = "PF_HR_PoliceInterceptor";
        private const string Folder = Root + "/Generated/Vehicles/" + Prefab;
        public const string DefinitionPath = Folder + "/Vehicle_" + Prefab + ".asset";
        private const string PackTexture = "Assets/ThirdParty/Sketchfab/Vehicles/generic-passenger-car-pack/textures/SedanYellow.png";

        [MenuItem("Neon Rift/Harbor Rain/Build Police Car")]
        public static string Build()
        {
            var log = new StringBuilder("[HarborRain] police car\n");
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();

            // 1. Livery: the pack's yellow sedan texture with the paint turned white and a checker band along the doors.
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(PackTexture));
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels();
            int painted = 0;
            bool Paint(Color c) { Color.RGBToHSV(c, out float hu, out float s, out float v); return s > 0.3f && hu > 0.08f && hu < 0.22f && v > 0.12f; }
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (!Paint(c)) continue;
                Color.RGBToHSV(c, out _, out _, out float v);
                float l = Mathf.Pow(v, 0.8f);
                int x = i % w, row = h - 1 - i / w;
                // Side panels (rows 200–400 from the top hold the car's side): a two-row checker band, blue and yellow.
                if (row >= 300 && row < 352)
                {
                    int cell = (x / 26 + (row - 300) / 26) % 2;
                    px[i] = cell == 0 ? new Color(0.03f, 0.12f, 0.55f) * (0.4f + l * 0.7f) : new Color(1f, 0.82f, 0.05f) * (0.45f + l * 0.65f);
                }
                else if (row >= 292 && row < 300 || row >= 352 && row < 358) px[i] = new Color(0.03f, 0.12f, 0.55f) * (0.4f + l * 0.7f);
                else px[i] = new Color(l * 0.94f, l * 0.95f, l * 0.97f);
                painted++;
            }
            tex.SetPixels(px);
            string liveryPath = $"{Folder}/HR_PoliceLivery.png";
            File.WriteAllBytes(liveryPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(liveryPath);
            var livery = AssetDatabase.LoadAssetAtPath<Texture2D>(liveryPath);
            log.AppendLine($"  livery: {painted:N0} paint pixels repainted → {liveryPath}");

            var sedanMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_NeonRift/Art/Traffic/Traffic_PackSedan.mat");
            string bodyMatPath = $"{Folder}/HR_PoliceBody.mat";
            var bodyMat = AssetDatabase.LoadAssetAtPath<Material>(bodyMatPath);
            if (bodyMat == null) { bodyMat = new Material(sedanMat); AssetDatabase.CreateAsset(bodyMat, bodyMatPath); }
            else bodyMat.CopyPropertiesFromMaterial(sedanMat);
            bodyMat.SetTexture("_BaseMap", livery);
            if (bodyMat.HasProperty("_MainTex")) bodyMat.SetTexture("_MainTex", livery);
            bodyMat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(bodyMat);

            // 2. Static source: the traffic sedan's model only (its traffic scripts, body and collider removed).
            var pack = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeonRift/Prefabs/Traffic/PackCar_Sedan.prefab");
            var src = (GameObject)PrefabUtility.InstantiatePrefab(pack);
            PrefabUtility.UnpackPrefabInstance(src, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            src.name = Prefab + "_Source";
            try
            {
                foreach (var mb in src.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
                foreach (var rb in src.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                foreach (var col in src.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                foreach (var a in src.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
                foreach (var mr in src.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] == sedanMat) mats[i] = bodyMat;
                    mr.sharedMaterials = mats;
                }
                src.transform.position = Vector3.zero;
                PrefabUtility.SaveAsPrefabAsset(src, $"{Folder}/{Prefab}_Source.prefab");
            }
            finally { Object.DestroyImmediate(src); }

            // 3. Stock vehicle prefab builder.
            var setup = ScriptableObject.CreateInstance<VehicleModelSetup>();
            setup.sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{Prefab}_Source.prefab");
            setup.prefabName = Prefab;
            setup.targetWheelDiameter = 0f;
            setup.bodyRotation = Vector3.zero;
            setup.wheelKeywords = new[] { "Wheel_A" };
            setup.staticWheelKeywords = new string[0];
            setup.motionBlurWheelKeywords = new string[0];
            setup.hiddenKeywords = new string[0];
            setup.lightKeywords = new[] { "lamp_none" };
            var report = VehiclePrefabBuilder.Build(setup);
            Object.DestroyImmediate(setup);
            string prefabPath = $"{Folder}/{Prefab}.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
            string moved = AssetDatabase.MoveAsset(report.PrefabPath, prefabPath);
            if (!string.IsNullOrEmpty(moved)) throw new IOException(moved);
            string art = $"{VehiclePrefabBuilder.GeneratedFolder}/{Prefab}";
            if (AssetDatabase.IsValidFolder(art))
            {
                AssetDatabase.DeleteAsset($"{Folder}/Art");
                moved = AssetDatabase.MoveAsset(art, $"{Folder}/Art");
                if (!string.IsNullOrEmpty(moved)) throw new IOException(moved);
            }
            log.AppendLine($"  stock builder: {report.Summary}");

            // 4. Police kit on the built prefab.
            AddKit(prefabPath, log);

            // 5. Physics (a copy of a stock profile: a heavier, softer saloon) and the definition (not in any catalog).
            string physPath = $"{Folder}/Physics_{Prefab}.asset";
            AssetDatabase.DeleteAsset(physPath);
            AssetDatabase.CopyAsset("Assets/_NeonRift/Data/Vehicles/Physics/VehiclePhysics_MercedesSLS.asset", physPath);
            var phys = AssetDatabase.LoadAssetAtPath<VehiclePhysicsProfile>(physPath);
            var pso = new SerializedObject(phys);
            pso.FindProperty("chassis.mass").floatValue = 1720f;
            pso.FindProperty("chassis.frontWeightDistribution").floatValue = 0.54f;
            pso.FindProperty("engine.peakTorque").floatValue = 560f;
            var notes = pso.FindProperty("tuningNotes");
            if (notes != null) notes.stringValue = "Harbor Rain police saloon: copy of VehiclePhysics_MercedesSLS with 1720 kg, 560 Nm, 54 % front weight.";
            pso.ApplyModifiedPropertiesWithoutUndo();

            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(DefinitionPath);
            if (def == null) { def = ScriptableObject.CreateInstance<VehicleDefinition>(); AssetDatabase.CreateAsset(def, DefinitionPath); }
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = "hr_police_interceptor";
            so.FindProperty("displayName").stringValue = "PORT POLICE";
            so.FindProperty("manufacturer").stringValue = "Port Police";
            so.FindProperty("category").stringValue = "Police Saloon";
            so.FindProperty("description").stringValue = "Kowloon Bay port police saloon (AI only). Traffic-pack sedan body in a police livery on the stock vehicle rig.";
            so.FindProperty("gameplayPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            so.FindProperty("physicsProfile").objectReferenceValue = phys;
            so.FindProperty("audioProfile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VehicleAudioProfile>("Assets/_NeonRift/Data/Vehicles/Audio/VehicleAudio_MercedesSLS.asset");
            so.FindProperty("displayStatsProvisional").boolValue = true;
            var lic = so.FindProperty("license");
            lic.FindPropertyRelative("LicenseId").stringValue = "NOT RECORDED (generic passenger car pack, as Night Run traffic)";
            lic.FindPropertyRelative("Author").stringValue = "unknown (Sketchfab generic passenger car pack)";
            lic.FindPropertyRelative("SourceUrl").stringValue = "Assets/ThirdParty/Sketchfab/Vehicles/generic-passenger-car-pack";
            lic.FindPropertyRelative("CommercialUseAllowed").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  → {prefabPath}, {DefinitionPath}");
            File.WriteAllText("Logs/HarborRainPolice.txt", log.ToString());
            Debug.Log(log.ToString());
            return log.ToString();
        }

        private static Material Lens(string name, Color baseColour)
        {
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", baseColour);
            m.SetFloat("_Smoothness", 0.85f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void AddKit(string prefabPath, StringBuilder log)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                int layer = LayerMask.NameToLayer("Vehicle");
                var body = root.transform.Find("Body");
                var b = new Bounds(root.transform.position, Vector3.zero);
                bool first = true;
                foreach (var r in body.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.enabled) continue;
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                }
                var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                var dark = Lens("HR_PoliceTrim", new Color(0.04f, 0.04f, 0.045f));
                dark.DisableKeyword("_EMISSION");
                var red = Lens("HR_PoliceLensRed", new Color(0.35f, 0.02f, 0.02f));
                var blue = Lens("HR_PoliceLensBlue", new Color(0.02f, 0.05f, 0.35f));
                var kit = new GameObject("PoliceKit") { layer = layer }.transform;
                kit.SetParent(root.transform, false);
                Renderer Box(string n, Transform parent, Vector3 centre, Vector3 size, Material m)
                {
                    var go = new GameObject(n) { layer = layer };
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = centre;
                    go.transform.localScale = size;
                    go.AddComponent<MeshFilter>().sharedMesh = cube;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = m;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    return mr;
                }
                // Roof height: highest point near the car's middle (the roof, not the boot).
                float roof = b.max.y;
                float zMid = b.center.z - b.size.z * 0.06f;
                Box("LightbarBase", kit, new Vector3(0f, roof + 0.05f, zMid), new Vector3(1.15f, 0.07f, 0.26f), dark);
                var redLenses = new[] { Box("LensRed", kit, new Vector3(-0.3f, roof + 0.14f, zMid), new Vector3(0.52f, 0.12f, 0.22f), red) };
                var blueLenses = new[] { Box("LensBlue", kit, new Vector3(0.3f, roof + 0.14f, zMid), new Vector3(0.52f, 0.12f, 0.22f), blue) };
                // Grille flashers (small, low) and the push bar on the front bumper.
                float front = b.max.z;
                redLenses = redLenses.Append(Box("GrilleRed", kit, new Vector3(-0.32f, 0.62f, front - 0.02f), new Vector3(0.16f, 0.06f, 0.05f), red)).ToArray();
                blueLenses = blueLenses.Append(Box("GrilleBlue", kit, new Vector3(0.32f, 0.62f, front - 0.02f), new Vector3(0.16f, 0.06f, 0.05f), blue)).ToArray();
                Box("PushBar", kit, new Vector3(0f, 0.45f, front + 0.1f), new Vector3(0.9f, 0.32f, 0.06f), dark);
                foreach (float x in new[] { -0.3f, 0.3f }) Box("PushBarArm", kit, new Vector3(x, 0.45f, front - 0.02f), new Vector3(0.06f, 0.3f, 0.26f), dark);
                Light Point(string n, Vector3 at, Color c)
                {
                    var go = new GameObject(n) { layer = layer };
                    go.transform.SetParent(kit, false);
                    go.transform.localPosition = at;
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point; l.color = c; l.range = 24f; l.intensity = 0f; l.shadows = LightShadows.None; l.enabled = false;
                    l.renderMode = LightRenderMode.ForcePixel;
                    return l;
                }
                var rl = Point("BarLightRed", new Vector3(-0.35f, roof + 0.35f, zMid), new Color(1f, 0.08f, 0.05f));
                var bl = Point("BarLightBlue", new Vector3(0.35f, roof + 0.35f, zMid), new Color(0.1f, 0.3f, 1f));
                var sgo = new GameObject("PillarSearchlight") { layer = layer };
                sgo.transform.SetParent(kit, false);
                sgo.transform.localPosition = new Vector3(-b.extents.x - 0.05f, roof - 0.25f, b.center.z + b.size.z * 0.12f);
                sgo.transform.localRotation = Quaternion.Euler(6f, -14f, 0f);
                var search = sgo.AddComponent<Light>();
                search.type = LightType.Spot; search.color = new Color(0.95f, 0.97f, 1f); search.range = 70f; search.spotAngle = 18f; search.innerSpotAngle = 8f;
                search.intensity = 2400f; search.shadows = LightShadows.None; search.enabled = false;
                Box("SearchlightHead", sgo.transform, Vector3.zero, new Vector3(0.14f, 0.14f, 0.2f), dark);

                var sirenGo = new GameObject("Siren") { layer = layer };
                sirenGo.transform.SetParent(kit, false);
                sirenGo.transform.localPosition = new Vector3(0f, 0.6f, front - 0.3f);
                var siren = sirenGo.AddComponent<AudioSource>();
                siren.playOnAwake = false; siren.loop = true; siren.spatialBlend = 1f; siren.volume = 1f;
                // Measured in the mix: at min distance 9 m the siren sat under the storm and music even at 15 m. 20 m keeps the
                // log falloff (distance still reads) but puts a unit on your tail clearly on top.
                siren.rolloffMode = AudioRolloffMode.Logarithmic; siren.minDistance = 20f; siren.maxDistance = 260f; siren.dopplerLevel = 0.7f; siren.priority = 60;
                var mixer = AssetDatabase.LoadAssetAtPath<NeonRift.Audio.AudioMixerConfig>("Assets/_NeonRift/Data/Audio/AudioMixerConfig.asset");
                if (mixer != null) siren.outputAudioMixerGroup = mixer.Sfx;
                var wail = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_NeonRift/Audio/Generated/Mission/Siren_Loop.wav");
                var yelp = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Generated/Audio/HR_PoliceYelp.wav");
                var pc = root.AddComponent<HarborPoliceCar>();
                pc.EditorConfigure(redLenses, blueLenses, rl, bl, search, siren, wail, yelp);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                log.AppendLine($"  kit: lightbar at roof {roof:0.00} m, grille flashers, push bar, pillar searchlight, siren (wail {(wail != null ? "ok" : "MISSING")}, yelp {(yelp != null ? "ok" : "MISSING")}); body {b.size:F2}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
