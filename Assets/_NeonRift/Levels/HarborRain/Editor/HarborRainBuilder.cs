using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NeonRift.EditorTools.District;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Generates the standalone Harbor Rain level (scene, meshes, materials, audio, particle prefab) into
    /// <c>Assets/_NeonRift/Levels/HarborRain</c>. Night Run assets are only read (materials are copied before being
    /// changed), and the scene is not added to the build list.
    ///
    /// Layout (metres, +Z north = the sea): spawn on the entry road south of the main checkpoint (0, -150); a grid of
    /// dock roads (spine x=0, west x=-250, east x=250, cross road z=0, south road z=-130, quay road z=150); the secure
    /// yard (west of the spine, between the cross and quay roads) holds shipment KX-7 under gantry crane 3; the east
    /// rail gate (300, 70) leads to extraction. Escape A: quay road (floods deep and closes). Escape B: cross road
    /// (shallow water) then the east road.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        public const string Root = "Assets/_NeonRift/Levels/HarborRain";
        public const string ScenePath = Root + "/Scenes/HarborRain.unity";
        private const string Gen = Root + "/Generated";
        private const string District = "Assets/_NeonRift/Art/District/Materials/";


        private static int drivable, environment, detail;
        private static Dictionary<Material, MeshBuilder> batch;
        private static int meshCount;
        private static readonly StringBuilder report = new();

        // Materials
        private static Material wetAsphalt, wetConcrete, concreteDark, metal, roof, roller, warning, marking, markingYellow, lampHead,
                                pool, cone, aviation, craneSteel, neonCyan, neonAmber, sea, flood, puddle, secLamp, gateLamp, beacon, shipGlow, rain, splash, sprayMat, glass,
                                hazardStripe, cladding, claddingDark, indWindow, drain, hullRed, hullBlack, shipWhite, shipWindow, rubber, rail, sleeper, extractGlow, camBody, cameraLed;
        private static Material[] containers;

        [MenuItem("Neon Rift/Harbor Rain/Build Scene")]
        public static void BuildMenu() => Build();

        [MenuItem("Neon Rift/Harbor Rain/Open Scene")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        // Build() and the layout live in HarborRainBuilder.Layout.cs.

        // ---------------- Materials ----------------

        private static void BuildMaterials()
        {
            Material D(string name) => AssetDatabase.LoadAssetAtPath<Material>(District + name + ".mat");
            wetAsphalt = Copy("HR_WetAsphalt", D("District_Asphalt"), m => Wet(m, 0.8f, 0.78f));
            wetConcrete = Copy("HR_WetConcrete", D("District_AsphaltLot") ?? D("District_Concrete"), m => Wet(m, 0.85f, 0.62f));
            concreteDark = D("District_ConcreteDark");
            metal = D("District_Metal");
            roof = D("District_Roof");
            roller = D("District_RollerDoor");
            warning = Lit("HR_Hazard", new Color(0.62f, 0.46f, 0.06f), 0.45f, 0.1f, null);
            craneSteel = Lit("HR_CraneSteel", new Color(0.55f, 0.2f, 0.06f), 0.4f, 0.35f, null);
            marking = D("District_RoadMarking");
            markingYellow = D("District_MarkingYellow");
            lampHead = D("District_LampHeadSodium");
            pool = D("District_YardPoolSodium");
            cone = D("District_LightConeSodium");
            aviation = D("District_AviationRed");
            neonCyan = D("District_NeonCyan");
            neonAmber = D("District_NeonAmber");
            glass = D("District_Glass");
            containers = new Material[5];
            for (int i = 0; i < 5; i++) containers[i] = D($"District_Container{i}");

            var soft = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_NeonRift/Art/District/Textures/District_GlowSoft.png");
            var ripple = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_NeonRift/Art/District/Textures/District_Asphalt_Normal.png");
            sea = Water("HR_Sea", new Color(0.012f, 0.03f, 0.04f, 0.94f), ripple);
            flood = Water("HR_Floodwater", new Color(0.09f, 0.13f, 0.14f, 0.85f), ripple);
            puddle = Lit("HR_Puddle", new Color(0.015f, 0.018f, 0.022f), 0.97f, 0f, null);
            secLamp = Lit("HR_SecurityLamp", Color.black, 0.6f, 0f, new Color(0.1f, 1.6f, 2.2f));
            gateLamp = Lit("HR_GateLamp", Color.black, 0.6f, 0f, new Color(5f, 0.2f, 0.12f));
            beacon = Lit("HR_FloodBeacon", new Color(0.1f, 0.06f, 0f), 0.6f, 0f, new Color(0.05f, 0.02f, 0f));
            shipGlow = Lit("HR_ShipmentGlow", Color.black, 0.5f, 0f, new Color(0.2f, 2.2f, 3f));
            rain = Glow("HR_Rain", new Color(0.32f, 0.38f, 0.46f), soft);
            splash = Glow("HR_Splash", new Color(0.22f, 0.25f, 0.3f), soft);
            sprayMat = Glow("HR_Spray", new Color(0.3f, 0.33f, 0.38f), soft);
            extractGlow = Glow("HR_ExtractRing", new Color(0.1f, 0.9f, 0.6f), soft);
            // Campaign pass: the always-on pools read 40% brighter so lit roads carry in wide and distant views (only the
            // nearest masts have real-time lights); still well under the real lamps' own pools.
            pool = Glow("HR_LightPool", new Color(0.77f, 0.5f, 0.25f), soft);

            // Industrial set: hazard stripes, corrugated cladding (the container corrugation in neutral greys), ribbon
            // windows with a few lit panes, drains, ship paint, rubber fenders, rail steel and sleepers.
            hazardStripe = Lit("HR_HazardStripe", Color.white, 0.35f, 0f, null);
            hazardStripe.SetTexture("_BaseMap", Texture("HR_HazardStripes", 64, 64, (x, y) => ((x + y) / 16) % 2 == 0 ? new Color(0.85f, 0.62f, 0.05f) : new Color(0.05f, 0.05f, 0.05f), false));
            var corrugated = D("District_Container0");
            cladding = Copy("HR_Cladding", corrugated, m => m.SetColor("_BaseColor", new Color(0.36f, 0.4f, 0.43f)));
            claddingDark = Copy("HR_CladdingDark", corrugated, m => m.SetColor("_BaseColor", new Color(0.16f, 0.2f, 0.24f)));
            var windowRng = new Random(5);
            var lit = new bool[8 * 4];
            for (int i = 0; i < lit.Length; i++) lit[i] = windowRng.NextDouble() < 0.35;
            var windowTex = Texture("HR_IndustrialWindows", 128, 32, (x, y) =>
            {
                bool frame = x % 16 < 2 || y % 8 < 1;
                return frame ? Color.black : lit[(x / 16) + (y / 8) * 8] ? new Color(1f, 0.82f, 0.55f) : new Color(0.08f, 0.1f, 0.12f);
            }, true);
            indWindow = Lit("HR_IndustrialWindow", new Color(0.04f, 0.05f, 0.06f), 0.85f, 0f, new Color(1.6f, 1.4f, 1.1f));
            indWindow.SetTexture("_EmissionMap", windowTex);
            indWindow.SetTexture("_BaseMap", windowTex);
            drain = Lit("HR_Drain", new Color(0.02f, 0.02f, 0.022f), 0.6f, 0.8f, null);
            hullRed = Lit("HR_HullRed", new Color(0.32f, 0.06f, 0.04f), 0.35f, 0.2f, null);
            hullBlack = Lit("HR_HullBlack", new Color(0.035f, 0.04f, 0.045f), 0.45f, 0.2f, null);
            shipWhite = Lit("HR_ShipWhite", new Color(0.55f, 0.56f, 0.55f), 0.4f, 0.1f, null);
            shipWindow = Lit("HR_ShipWindow", Color.black, 0.9f, 0f, new Color(1.8f, 1.5f, 1.05f));
            rubber = Lit("HR_Rubber", new Color(0.02f, 0.02f, 0.02f), 0.3f, 0f, null);
            rail = Lit("HR_RailSteel", new Color(0.3f, 0.28f, 0.26f), 0.75f, 0.9f, null);
            sleeper = Lit("HR_Sleeper", new Color(0.09f, 0.07f, 0.05f), 0.2f, 0f, null);
            camBody = Lit("HR_CameraBody", new Color(0.55f, 0.57f, 0.6f), 0.5f, 0.3f, null);
            cameraLed = Lit("HR_CameraLed", Color.black, 0.5f, 0f, new Color(0.2f, 2.5f, 0.8f));
            // Harbor Rain 2.2: a wider shipping palette (faded maroon, carrier orange, ochre, sea green, reefer white) so stacks
            // read as many lines' boxes rather than one grey mass.
            var tints = new[] { new Color(0.12f, 0.36f, 0.4f), new Color(0.62f, 0.62f, 0.6f), new Color(0.1f, 0.14f, 0.32f), new Color(0.42f, 0.22f, 0.12f),
                                new Color(0.36f, 0.08f, 0.07f), new Color(0.62f, 0.27f, 0.06f), new Color(0.56f, 0.44f, 0.12f), new Color(0.13f, 0.3f, 0.17f), new Color(0.78f, 0.78f, 0.74f) };
            var all = new List<Material>(containers);
            for (int i = 0; i < tints.Length; i++)
            {
                var tint = tints[i];
                all.Add(Copy($"HR_Container{i}", containers[i % containers.Length], m => { m.SetColor("_BaseColor", tint); m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.55f)); }));
            }
            containers = all.ToArray();
        }

        private static void Wet(Material m, float darken, float smoothness)
        {
            m.SetColor("_BaseColor", m.GetColor("_BaseColor") * darken);
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
        }

        private static Material Asset(string name, Shader shader)
        {
            string path = $"{Gen}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            return m;
        }

        private static Material Copy(string name, Material source, Action<Material> change)
        {
            var m = Asset(name, source.shader);
            m.CopyPropertiesFromMaterial(source);
            m.shaderKeywords = source.shaderKeywords;
            change(m);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Lit(string name, Color colour, float smoothness, float metallic, Color? emission)
        {
            var m = Asset(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Water(string name, Color colour, Texture2D ripple)
        {
            var m = Lit(name, colour, 0.95f, 0f, null);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BumpMap", ripple);
            m.SetFloat("_BumpScale", 0.35f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTextureScale("_BaseMap", new Vector2(20f, 20f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Writes a small generated texture (PNG) and returns the imported asset.</summary>
        private static Texture2D Texture(string name, int w, int h, Func<int, int, Color> pixel, bool point)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, pixel(x, y));
            string path = $"{Gen}/Materials/{name}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
            importer.mipmapEnabled = !point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static readonly Dictionary<string, Material> signs = new();

        /// <summary>A self-lit sign face: the project's pixel font on a dark panel with a coloured border.</summary>
        private static Material Sign(string text, Color colour)
        {
            if (signs.TryGetValue(text, out var existing) && existing != null) return existing;
            int tw = PixelFont.Measure(text), w = tw + 8, h = PixelFont.Height + 8;
            string file = new string(System.Array.ConvertAll(text.ToCharArray(), ch => char.IsLetterOrDigit(ch) && ch < 128 ? ch : '_'));
            var tex = Texture("HR_Sign_" + file, w, h, (x, y) =>
            {
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) return colour * 0.8f;
                int gx = x - 4, gy = (h - 1 - y) - 4;
                if (gx < 0 || gy < 0 || gx >= tw || gy >= PixelFont.Height) return new Color(0.02f, 0.02f, 0.025f);
                int ci = gx / (PixelFont.Width + 1), cx = gx % (PixelFont.Width + 1);
                return cx < PixelFont.Width && PixelFont.Pixel(text[ci], cx, gy) ? colour : new Color(0.02f, 0.02f, 0.025f);
            }, true);
            var m = Lit("HR_Sign_" + file, Color.white, 0.3f, 0f, Color.white * 1.4f);
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_EmissionMap", tex);
            signs[text] = m;
            return m;
        }

        private static Material Glow(string name, Color colour, Texture2D shape)
        {
            var m = Asset(name, Shader.Find("NeonRift/AdditiveGlow"));
            m.SetColor("_Color", colour);
            m.SetTexture("_MainTex", shape);
            m.SetFloat("_EdgeSoftness", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- Mesh batching ----------------

        private static MeshBuilder B(Material m)
        {
            if (!batch.TryGetValue(m, out var b)) batch[m] = b = new MeshBuilder();
            return b;
        }

        private static void Begin() => batch = new Dictionary<Material, MeshBuilder>();

        /// <summary>Saves one combined mesh per material under <paramref name="parent"/>.</summary>
        private static void Flush(string name, Transform parent, int layer, bool shadows = true)
        {
            var group = new GameObject(name) { layer = layer }.transform;
            group.SetParent(parent, false);
            foreach (var (material, builder) in batch)
            {
                if (builder.IsEmpty) continue;
                string meshName = $"HR_{name}_{material.name}";
                var mesh = SaveMesh(builder.ToMesh(meshName, material.IsKeywordEnabled("_NORMALMAP")), meshName);
                var go = new GameObject(material.name) { layer = layer };
                go.transform.SetParent(group, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccludeeStatic);
            }
            batch = null;
        }

        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            meshCount++;
            string path = $"{Gen}/Meshes/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static BoxCollider Collider(Transform parent, string name, Vector3 centre, Vector3 size, int layer, float yaw = 0f)
        {
            var go = new GameObject(name) { layer = layer, isStatic = true };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
            var c = go.AddComponent<BoxCollider>();
            c.size = size;
            return c;
        }

        private static GameObject Single(string name, Transform parent, Mesh mesh, Material m, int layer, Vector3 position, bool shadows = false)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        // ---------------- Storm ----------------

        private static StormController BuildStorm((AudioClip rain, AudioClip[] thunder, AudioMixerConfigRef mixer) audio, HarborRainAudio.Clips clips)
        {
            var go = new GameObject("Storm");
            var rainPs = Particles("Rain", go.transform, rain, 9000, ps =>
            {
                var main = ps.main;
                main.startLifetime = 1.1f;
                main.startSpeed = 0f;
                main.startSize = 0.04f;
                main.startColor = new Color(1f, 1f, 1f, 0.55f);
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(70f, 1f, 70f);
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(3f, 5f);
                vel.y = new ParticleSystem.MinMaxCurve(-30f, -26f);
                vel.z = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.045f;
                r.lengthScale = 1f;
            });
            var splashPs = Particles("Splashes", go.transform, splash, 1200, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
                main.startColor = new Color(1f, 1f, 1f, 0.5f);
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(46f, 0f, 46f);
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.4f));
                ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            });

            var lightningGo = new GameObject("Lightning");
            lightningGo.transform.SetParent(go.transform, false);
            lightningGo.transform.rotation = Quaternion.Euler(55f, 140f, 0f);
            var lightning = lightningGo.AddComponent<Light>();
            lightning.type = LightType.Directional;
            lightning.color = new Color(0.75f, 0.82f, 1f);
            lightning.intensity = 0f;
            lightning.shadows = LightShadows.None;

            // Storm bed (2D): heavy rain, wind, gust one-shots and thunder; levels are driven by StormController.
            var rainSource = Source(go, "RainAudio", clips.RainHeavy, true, audio.mixer.Ambience, 0.7f);
            var windSource = Source(go, "WindAudio", clips.Wind, true, audio.mixer.Ambience, 0.5f);
            var gustSource = Source(go, "GustAudio", null, false, audio.mixer.Ambience, 1f);
            var thunderSource = Source(go, "ThunderAudio", null, false, audio.mixer.Sfx, 1f);

            var storm = go.AddComponent<StormController>();
            Configure(storm, so =>
            {
                // Heavy but readable: the old 0.009–0.017 hid everything beyond ~100 m at full storm.
                so.FindProperty("fogDensity").vector2Value = new Vector2(0.0045f, 0.0085f);
                so.FindProperty("rain").objectReferenceValue = rainPs;
                so.FindProperty("splashes").objectReferenceValue = splashPs;
                so.FindProperty("lightning").objectReferenceValue = lightning;
                so.FindProperty("rainAudio").objectReferenceValue = rainSource;
                so.FindProperty("thunderAudio").objectReferenceValue = thunderSource;
                so.FindProperty("windAudio").objectReferenceValue = windSource;
                so.FindProperty("gustAudio").objectReferenceValue = gustSource;
                Array(so.FindProperty("thunderClips"), clips.Thunder);
                Array(so.FindProperty("gustClips"), clips.Gusts);
            });
            return storm;
        }

        private static ParticleSystem Particles(string name, Transform parent, Material material, int max, Action<ParticleSystem> configure)
        {
            var go = new GameObject(name) { layer = detail };
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            configure(ps);
            return ps;
        }

        private static ParticleSystem SprayPrefab()
        {
            var go = new GameObject("HR_VehicleSpray");
            var ps = Particles("Spray", go.transform, sprayMat, 500, p =>
            {
                var main = p.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                main.startSpeed = 3f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
                main.startColor = new Color(1f, 1f, 1f, 0.35f);
                main.gravityModifier = 0.8f;
                var em = p.emission;
                em.rateOverTime = 0f;
                var shape = p.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 28f;
                shape.radius = 0.8f;
                var size = p.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
                var col = p.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
            });
            ps.transform.SetParent(null);
            Object.DestroyImmediate(go);
            var prefab = PrefabUtility.SaveAsPrefabAsset(ps.gameObject, $"{Gen}/HR_VehicleSpray.prefab");
            Object.DestroyImmediate(ps.gameObject);
            return prefab.GetComponent<ParticleSystem>();
        }

        private static AudioSource Source(GameObject parent, string name, AudioClip clip, bool loop, UnityEngine.Audio.AudioMixerGroup group, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = loop && clip != null;
            s.spatialBlend = 0f;
            s.volume = volume;
            s.outputAudioMixerGroup = group;
            return s;
        }

        // ---------------- Atmosphere ----------------

        private static void BuildAtmosphere()
        {
            var nightSky = AssetDatabase.LoadAssetAtPath<Material>(District + "District_NightSky.mat");
            var sky = Copy("HR_StormSky", nightSky, m =>
            {
                m.SetColor("_ZenithColor", new Color(0.006f, 0.008f, 0.012f));
                m.SetColor("_HorizonColor", new Color(0.06f, 0.065f, 0.075f));
                m.SetColor("_GlowColor", new Color(0.2f, 0.13f, 0.08f));
                m.SetFloat("_GlowHeight", 0.16f);
                m.SetColor("_MoonColor", Color.black);
                m.SetColor("_MoonHaloColor", Color.black);
                m.SetFloat("_CloudCover", 0.98f);
                m.SetColor("_CloudColor", new Color(0.035f, 0.038f, 0.045f));
                m.SetFloat("_StarDensity", 0f);
                m.SetFloat("_StarBrightness", 0f);
            });
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.17f, 0.18f, 0.21f);
            RenderSettings.ambientEquatorColor = new Color(0.18f, 0.175f, 0.17f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.06f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.078f, 0.082f, 0.092f);   // a touch lighter than the silhouettes, so black buildings separate from the haze   // close to the horizon haze, so the far sea meets the sky instead of a black band
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            var moonGo = new GameObject("StormMoon");
            moonGo.transform.rotation = Quaternion.Euler(38f, -30f, 0f);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            // Cool but not blue: a strongly blue moon spread a blue sheen over every wet surface.
            moon.color = new Color(0.62f, 0.66f, 0.74f);
            moon.intensity = 0.16f;
            moon.shadows = LightShadows.Soft;
            RenderSettings.sun = moon;

            var lighting = AssetDatabase.LoadAssetAtPath<LightingSettings>($"{Gen}/HarborRain_Lighting.lighting");
            if (lighting == null)
            {
                lighting = new LightingSettings { name = "HarborRain_Lighting" };
                AssetDatabase.CreateAsset(lighting, $"{Gen}/HarborRain_Lighting.lighting");
            }
            lighting.bakedGI = false;
            lighting.realtimeGI = false;
            Lightmapping.lightingSettings = lighting;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_NeonRift/Settings/Rendering/NightRun_Base.asset");
            if (profile != null)
            {
                var v = new GameObject("PostVolume").AddComponent<Volume>();
                v.isGlobal = true;
                v.sharedProfile = profile;   // shared look, not modified
            }
            // Harbor Rain's own override on top (the shared Night Run profile is untouched): a little more exposure so
            // roads, barriers and hazards read in the storm, and a touch more contrast for depth.
            string overridePath = $"{Gen}/HR_PostOverrides.asset";
            var over = AssetDatabase.LoadAssetAtPath<VolumeProfile>(overridePath);
            if (over == null) { over = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(over, overridePath); }
            if (!over.TryGet(out ColorAdjustments adjust)) adjust = over.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(0.45f);
            adjust.contrast.Override(8f);
            adjust.saturation.Override(-6f);
            EditorUtility.SetDirty(over);
            var ov = new GameObject("PostVolume_HarborRain").AddComponent<Volume>();
            ov.isGlobal = true;
            ov.priority = 5f;
            ov.sharedProfile = over;
        }

        // ---------------- Audio (generated storm sounds) ----------------

        private sealed class AudioMixerConfigRef
        {
            public UnityEngine.Audio.AudioMixerGroup Sfx, Ambience;
        }

        private static (AudioClip rain, AudioClip[] thunder, AudioMixerConfigRef mixer) BuildAudio()
        {
            var config = AssetDatabase.LoadAssetAtPath<NeonRift.Audio.AudioMixerConfig>("Assets/_NeonRift/Data/Audio/AudioMixerConfig.asset");
            var mixer = new AudioMixerConfigRef { Sfx = config != null ? config.Sfx : null, Ambience = config != null ? config.Ambience : null };
            const int rate = 44100;
            var rng = new Random(11);

            // Rain: two bands of noise (a hiss and a softer body) with a slow swell, plus sparse drips; 8 s seamless loop.
            int n = rate * 8;
            var rain = new float[n];
            double lp1 = 0, lp2 = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                double w = rng.NextDouble() * 2 - 1;
                lp1 += (w - lp1) * 0.35;
                lp2 += (w - lp2) * 0.04;
                hp = 0.97 * (hp + lp1 - prev);
                prev = lp1;
                double swell = 0.85 + 0.15 * Math.Sin(2 * Math.PI * i / n * 2);
                double drip = rng.NextDouble() < 0.0009 ? (rng.NextDouble() * 0.6) : 0;
                rain[i] = (float)((hp * 0.45 + lp2 * 1.6) * swell + drip * w);
            }
            Crossfade(rain, rate / 2);
            var rainClip = Wav("HR_RainLoop", rain, rate);

            var thunder = new AudioClip[3];
            for (int k = 0; k < 3; k++)
            {
                int len = rate * (5 + k);
                var t = new float[len];
                double a = 0, b = 0;
                for (int i = 0; i < len; i++)
                {
                    double time = i / (double)rate;
                    double w = rng.NextDouble() * 2 - 1;
                    a += (w - a) * 0.02;
                    b += (a - b) * 0.08;
                    double crack = time < 0.25 ? Math.Exp(-time * 18) * w * (k == 0 ? 0.6 : 0.25) : 0;
                    double env = Math.Min(1, time * 6) * Math.Exp(-time * (0.7 + k * 0.15)) * (1 + 0.5 * Math.Sin(time * (3 + k)));
                    t[i] = (float)(b * 9 * env + crack);
                }
                thunder[k] = Wav($"HR_Thunder{k}", t, rate);
            }
            return (rainClip, thunder, mixer);
        }

        private static void Crossfade(float[] x, int fade)
        {
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                x[i] = x[i] * t + x[x.Length - fade + i] * (1 - t);
            }
        }

        private static AudioClip Wav(string name, float[] samples, int rate)
        {
            float peak = 0f;
            foreach (var s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            float gain = peak > 0f ? 0.7f / peak : 1f;
            string path = $"{Root}/Generated/Audio/{name}.wav";
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
                w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * gain * 32767f), -32768, 32767));
            }
            AssetDatabase.ImportAsset(path);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // ---------------- Serialized-field helpers ----------------

        private static void Configure(Object target, Action<SerializedObject> configure)
        {
            var so = new SerializedObject(target);
            configure(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Array(SerializedProperty property, params Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
