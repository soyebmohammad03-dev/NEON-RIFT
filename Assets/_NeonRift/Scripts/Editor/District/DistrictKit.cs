using System.Collections.Generic;
using NeonRift.EditorTools.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Materials, saved meshes and reusable prop prefabs for the district. Every asset is created on first use and
    /// updated in place afterwards, so re-running the builder keeps GUIDs (and scene references) stable.
    /// </summary>
    public sealed class DistrictKit
    {
        public const string MaterialFolder = "Assets/_NeonRift/Art/District/Materials";
        public const string MeshFolder = "Assets/_NeonRift/Art/District/Meshes";
        public const string PrefabFolder = "Assets/_NeonRift/Prefabs/District";

        public static readonly string[] NeonNames = { "Cyan", "Magenta", "Violet", "Amber", "Lime" };
        public static readonly Color[] NeonColours =
        {
            new(0.25f, 2.4f, 3.4f), new(3.4f, 0.35f, 2.6f), new(1.5f, 0.7f, 3.6f), new(3.6f, 1.5f, 0.25f), new(0.7f, 3.2f, 1.1f)
        };

        public readonly DistrictTextures.Set Textures;
        public Material Asphalt, Pavement, Roof, Shopfront, Security, Marking, Metal, Concrete, LampHead, Plaza, CoreGlow,
                        Indicator, BarrierWarning, TunnelWall, NightSky, BeaconCyan, BeaconMagenta, Steam, Reflector;
        public Material[] Facades, NeonStrips, Signs, Billboards;

        public GameObject StreetLight, StreetLightLit, JerseyBarrier, Beacon;

        public DistrictKit(DistrictTextures.Set textures)
        {
            Textures = textures;
            VehiclePrefabBuilder.EnsureFolder(MaterialFolder);
            VehiclePrefabBuilder.EnsureFolder(MeshFolder);
            VehiclePrefabBuilder.EnsureFolder(PrefabFolder);
            CreateMaterials();
            CreatePrefabs();
        }

        // ---------------- Materials ----------------

        private void CreateMaterials()
        {
            var t = Textures;
            Asphalt = Lit("District_Asphalt", Color.white, 1f, 0f, t.AsphaltAlbedo, t.AsphaltNormal, 0.35f, metalSmooth: t.AsphaltMask);
            Pavement = Lit("District_Pavement", Color.white, 0.35f, 0f, t.PavementAlbedo, t.PavementNormal, 0.8f);
            Roof = Lit("District_Roof", new Color(0.05f, 0.05f, 0.06f), 0.25f, 0f);
            Shopfront = Lit("District_Shopfront", Color.white, 0.75f, 0f, t.ShopAlbedo, emissionMap: t.ShopEmission, emission: Color.white * 1.25f);
            Facades = new Material[t.FacadeAlbedo.Length];
            for (int i = 0; i < Facades.Length; i++)
                Facades[i] = Lit($"District_Facade{i}", Color.white, 0.55f, 0f, t.FacadeAlbedo[i], emissionMap: t.FacadeEmission[i], emission: Color.white * 1.8f);
            Security = Lit("District_SecurityStrip", Color.black, 0.5f, 0f, emission: new Color(0.15f, 1.3f, 1.8f));
            Marking = Lit("District_RoadMarking", new Color(0.72f, 0.74f, 0.78f), 0.55f, 0f, emission: new Color(0.12f, 0.12f, 0.14f));
            Metal = Lit("District_Metal", new Color(0.07f, 0.075f, 0.09f), 0.55f, 0.6f);
            Concrete = Lit("District_Concrete", new Color(0.3f, 0.3f, 0.32f), 0.2f, 0f);
            LampHead = Lit("District_LampHead", Color.white, 0.6f, 0f, emission: new Color(2.6f, 2.8f, 3.2f));
            Plaza = Lit("District_Plaza", Color.white, 0.7f, 0f, t.PlazaAlbedo, emissionMap: t.PlazaEmission, emission: Color.white * 1.1f);
            CoreGlow = Lit("District_DataCoreGlow", Color.black, 0.9f, 0f, emission: new Color(0.3f, 2.2f, 3.2f));
            Indicator = Lit("District_Indicator", Color.black, 0.8f, 0f, emission: new Color(0.15f, 1.2f, 1.6f));
            BarrierWarning = Lit("District_BarrierWarning", Color.black, 0.5f, 0f, emission: new Color(0.2f, 1.6f, 2.2f));
            TunnelWall = Lit("District_TunnelWall", new Color(0.12f, 0.12f, 0.14f), 0.35f, 0f, t.PavementAlbedo, t.PavementNormal, 0.6f);
            Reflector = Lit("District_Reflector", new Color(0.4f, 0.25f, 0.05f), 0.6f, 0f, emission: new Color(1.6f, 0.8f, 0.1f));
            NeonStrips = new Material[NeonNames.Length];
            Signs = new Material[NeonNames.Length];
            for (int i = 0; i < NeonNames.Length; i++)
            {
                NeonStrips[i] = Lit($"District_Neon{NeonNames[i]}", Color.black, 0.5f, 0f, emission: NeonColours[i]);
                Signs[i] = Lit($"District_Sign{NeonNames[i]}", new Color(0.02f, 0.02f, 0.03f), 0.6f, 0f, emissionMap: t.Signs, emission: NeonColours[i] * 1.2f);
            }
            Billboards = new Material[t.Billboards.Length];
            for (int i = 0; i < Billboards.Length; i++)
                Billboards[i] = Lit($"District_Billboard{i}", Color.black, 0.7f, 0f, emissionMap: t.Billboards[i], emission: Color.white * 1.7f);

            BeaconCyan = Glow("District_BeaconCyan", new Color(0.25f, 1.6f, 2.6f), t.GlowGradient, 1.2f);
            BeaconMagenta = Glow("District_BeaconMagenta", new Color(2.4f, 0.3f, 2f), t.GlowGradient, 1.2f);
            Steam = Glow("District_Steam", new Color(0.09f, 0.07f, 0.12f), t.GlowSoft, 0f);

            NightSky = Material("District_NightSky", Shader.Find("NeonRift/NightSky"));
        }

        private static Material Material(string name, Shader shader)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        public static Material Lit(string name, Color baseColour, float smoothness, float metallic, Texture2D baseMap = null, Texture2D normal = null,
                                   float normalScale = 1f, Texture2D metalSmooth = null, Texture2D emissionMap = null, Color? emission = null)
        {
            var m = Material(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", baseColour);
            m.SetTexture("_BaseMap", baseMap);
            m.SetFloat("_Smoothness", metalSmooth != null ? 1f : smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetTexture("_MetallicGlossMap", metalSmooth);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            SetKeyword(m, "_METALLICSPECGLOSSMAP", metalSmooth != null);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", normalScale);
            SetKeyword(m, "_NORMALMAP", normal != null);
            bool emissive = emission.HasValue && emission.Value.maxColorComponent > 0f;
            m.SetColor("_EmissionColor", emission ?? Color.black);
            m.SetTexture("_EmissionMap", emissionMap);
            SetKeyword(m, "_EMISSION", emissive);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Glow(string name, Color colour, Texture2D shape, float edgeSoftness)
        {
            var m = Material(name, Shader.Find("NeonRift/AdditiveGlow"));
            m.SetColor("_Color", colour);
            m.SetTexture("_MainTex", shape);
            m.SetFloat("_EdgeSoftness", edgeSoftness);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void SetKeyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        // ---------------- Meshes ----------------

        /// <summary>Saves (or overwrites in place) a generated mesh asset.</summary>
        public static Mesh SaveMesh(MeshBuilder builder, string name)
        {
            var mesh = builder.ToMesh(name);
            string path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>A static renderer for a saved mesh under <paramref name="parent"/>.</summary>
        public static GameObject Renderer(string name, Transform parent, Mesh mesh, Material material, int layer, bool shadows = true)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                                                     StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        // ---------------- Prop prefabs ----------------

        private void CreatePrefabs()
        {
            StreetLight = BuildStreetLight(false);
            StreetLightLit = BuildStreetLight(true);
            JerseyBarrier = BuildJerseyBarrier();
            Beacon = BuildBeacon();
        }

        private GameObject BuildStreetLight(bool lit)
        {
            var root = new GameObject(lit ? "PF_Prop_StreetLightLit" : "PF_Prop_StreetLight");
            try
            {
                var pole = new MeshBuilder();
                pole.Cylinder(Vector3.zero, 0.13f, 8.2f, 10, true);
                pole.OrientedBox(new Vector3(0f, 8.05f, 1.3f), new Vector3(0.16f, 0.16f, 2.8f), Quaternion.identity, 1f);
                pole.OrientedBox(new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.6f, 0.5f), Quaternion.identity, 1f);
                var head = new MeshBuilder();
                head.OrientedBox(new Vector3(0f, 7.92f, 2.75f), new Vector3(0.42f, 0.16f, 1.2f), Quaternion.identity, 1f);
                var poleMesh = SaveMesh(pole, "Prop_StreetLight_Pole");
                var headMesh = SaveMesh(head, "Prop_StreetLight_Head");
                int layer = LayerMask.NameToLayer("Environment");
                var p = Renderer("Pole", root.transform, poleMesh, Metal, layer);
                var h = Renderer("Head", root.transform, headMesh, LampHead, layer, shadows: false);
                var col = root.AddComponent<CapsuleCollider>();
                col.radius = 0.2f;
                col.height = 8f;
                col.center = new Vector3(0f, 4f, 0f);
                root.layer = layer;
                if (lit)
                {
                    var lightGo = new GameObject("Light");
                    lightGo.transform.SetParent(root.transform, false);
                    lightGo.transform.localPosition = new Vector3(0f, 7.7f, 2.9f);
                    lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    var light = lightGo.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.spotAngle = 120f;
                    light.innerSpotAngle = 60f;
                    light.range = 17f;
                    light.intensity = 26f;
                    light.color = new Color(0.78f, 0.88f, 1f);
                    light.shadows = LightShadows.None;
                    light.renderMode = LightRenderMode.Auto;
                }
                AddLod(root, 0.012f, p.GetComponent<Renderer>(), h.GetComponent<Renderer>());
                return SavePrefab(root, root.name);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private GameObject BuildJerseyBarrier()
        {
            var root = new GameObject("PF_Prop_JerseyBarrier");
            try
            {
                var body = new MeshBuilder();
                body.OrientedBox(new Vector3(0f, 0.25f, 0f), new Vector3(3.6f, 0.5f, 0.7f), Quaternion.identity, 1f);
                body.OrientedBox(new Vector3(0f, 0.7f, 0f), new Vector3(3.6f, 0.4f, 0.35f), Quaternion.identity, 1f);
                var refl = new MeshBuilder();
                for (int i = -1; i <= 1; i++)
                {
                    refl.OrientedBox(new Vector3(i * 1.1f, 0.55f, 0.36f), new Vector3(0.5f, 0.12f, 0.02f), Quaternion.identity, 1f);
                    refl.OrientedBox(new Vector3(i * 1.1f, 0.55f, -0.36f), new Vector3(0.5f, 0.12f, 0.02f), Quaternion.identity, 1f);
                }
                int layer = LayerMask.NameToLayer("Environment");
                var b = Renderer("Body", root.transform, SaveMesh(body, "Prop_JerseyBarrier_Body"), Concrete, layer);
                var r = Renderer("Reflectors", root.transform, SaveMesh(refl, "Prop_JerseyBarrier_Reflectors"), Reflector, layer, shadows: false);
                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.45f, 0f);
                col.size = new Vector3(3.6f, 0.9f, 0.7f);
                root.layer = layer;
                AddLod(root, 0.01f, b.GetComponent<Renderer>(), r.GetComponent<Renderer>());
                return SavePrefab(root, root.name);
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>Rotating red security beacon: a head that spins (the SecurityAlarm turns it) and a light.</summary>
        private GameObject BuildBeacon()
        {
            var root = new GameObject("PF_Prop_SecurityBeacon");
            try
            {
                var housing = new MeshBuilder();
                housing.Cylinder(Vector3.zero, 0.22f, 0.15f, 10, true);
                int layer = LayerMask.NameToLayer("Environment");
                Renderer("Housing", root.transform, SaveMesh(housing, "Prop_Beacon_Housing"), Metal, layer, shadows: false);
                var head = new GameObject("Head");
                head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                var lens = new MeshBuilder();
                lens.Cylinder(Vector3.zero, 0.18f, 0.3f, 10, true);
                lens.OrientedBox(new Vector3(0f, 0.15f, 0.16f), new Vector3(0.24f, 0.22f, 0.06f), Quaternion.identity, 1f);
                var lensMat = Lit("District_BeaconLens", Color.black, 0.8f, 0f, emission: new Color(4f, 0.2f, 0.3f));
                Renderer("Lens", head.transform, SaveMesh(lens, "Prop_Beacon_Lens"), lensMat, layer, shadows: false);
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(head.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.15f, 0.25f);
                lightGo.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 70f;
                light.range = 26f;
                light.intensity = 40f;
                light.color = new Color(1f, 0.12f, 0.2f);
                light.shadows = LightShadows.None;
                return SavePrefab(root, root.name);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AddLod(GameObject root, float cullHeight, params Renderer[] renderers)
        {
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cullHeight, renderers) });
            lod.RecalculateBounds();
        }

        private static GameObject SavePrefab(GameObject root, string name)
        {
            string path = $"{PrefabFolder}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            if (!ok) throw new System.InvalidOperationException($"Saving {path} failed.");
            return prefab;
        }

        /// <summary>Instantiates a prop prefab (keeps the prefab link) at a pose.</summary>
        public static GameObject Place(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, string name = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            if (name != null) go.name = name;
            return go;
        }

        public static IEnumerable<Transform> Children(Transform t)
        {
            foreach (Transform c in t) yield return c;
        }
    }
}
