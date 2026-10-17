using System.Collections.Generic;
using System.Linq;
using NeonRift.Vehicles;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Harbor Rain, operation overhaul: the storm-surge COAST ROAD east of the perimeter (seawall, three wave sections,
    /// the dry inland service road, the extraction compound), the imported ships (container ship at the berth, Liberty
    /// ship, cruise ship and work boats in the bay), patrol roadblocks, the gate junction box / drone relay / bay
    /// spreaders, background activity, the spatial soundscape, the command centre radio and the arrival camera.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private const float CoastZ0 = 268f, CoastZ1 = 284f, CoastX1 = 1660f;
        private static readonly float[] WaveCentres = { 1030f, 1235f, 1440f };
        private const float WaveHalf = 34f;
        private const string Imported = Root + "/Imported";

        private static Material ocean, craneBlue, workLight, swellWater, surge, plumeMat, mistMat, rock, foam, liberty, navGreen, navRed, navWhite, strobe, junction;

        private sealed class Coast
        {
            public HarborWaves Waves;
            public readonly List<GameObject> Blockades = new();
            public readonly List<Rect> BlockadeAreas = new();
            public readonly List<string> BlockadeNames = new();
            public Transform Spreaders0, Spreaders1, Spreaders2;
            public ParticleSystem GateSparks;
            public HarborLife Life;
            public readonly List<Renderer> CoastLamps = new();
        }

        private static void BuildCoastMaterials()
        {
            var soft = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_NeonRift/Art/District/Textures/District_GlowSoft.png");
            var ripple = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_NeonRift/Art/District/Textures/District_Asphalt_Normal.png");
            surge = Water("HR_SurgeWater", new Color(0.28f, 0.34f, 0.36f, 0.82f), ripple);
            foam = Lit("HR_Foam", new Color(0.62f, 0.68f, 0.7f), 0.7f, 0f, new Color(0.03f, 0.035f, 0.04f));
            rock = Lit("HR_Armour", new Color(0.2f, 0.2f, 0.19f), 0.55f, 0f, null);
            navGreen = Lit("HR_NavGreen", Color.black, 0.5f, 0f, new Color(0.2f, 4f, 0.6f));
            navRed = Lit("HR_NavRed", Color.black, 0.5f, 0f, new Color(4f, 0.2f, 0.15f));
            navWhite = Lit("HR_NavWhite", Color.black, 0.5f, 0f, new Color(3.5f, 3.4f, 3.1f));
            strobe = Lit("HR_Strobe", Color.black, 0.5f, 0f, new Color(4f, 0.2f, 0.15f));
            junction = Lit("HR_JunctionBox", new Color(0.32f, 0.36f, 0.3f), 0.45f, 0.4f, null);
            plumeMat = Particle("HR_SprayPlume", new Color(0.9f, 0.94f, 1f, 0.85f), soft);
            craneBlue = Lit("HR_CraneBlue", new Color(0.07f, 0.2f, 0.38f), 0.5f, 0.55f, null);
            workLight = Lit("HR_CraneWorkLight", Color.black, 0.5f, 0f, new Color(7f, 7.4f, 8f));
            ocean = Asset("HR_Ocean", Shader.Find("NeonRift/HarborOcean"));
            ocean.SetFloat("_Roughness", 0.2f);
            EditorUtility.SetDirty(ocean);
            swellWater = Water("HR_SwellWater", new Color(0.06f, 0.09f, 0.1f, 0.96f), ripple);
            mistMat = Particle("HR_SeaMist", new Color(0.55f, 0.6f, 0.66f, 0.18f), soft);
            liberty = Lit("HR_LibertyShip", Color.white, 0.35f, 0.35f, null);
            liberty.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Imported}/Ships/LibertyShip/textures/DefaultMaterial_Base_Color.png"));
            var n = $"{Imported}/Ships/LibertyShip/textures/DefaultMaterial_Normal_OpenGL.png";
            if (AssetImporter.GetAtPath(n) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
            liberty.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(n));
            liberty.EnableKeyword("_NORMALMAP");
            foreach (var t in new[] { "Hull_Normal_DirectX_Raw", "Bridge_Normal_DirectX_Raw", "Containers_Normal_DirectX_Raw" })
                if (AssetImporter.GetAtPath($"{Imported}/Ships/ContainerShip/textures/{t}.png") is TextureImporter ci && ci.textureType != TextureImporterType.NormalMap)
                { ci.textureType = TextureImporterType.NormalMap; ci.flipGreenChannel = true; ci.SaveAndReimport(); }
            BuildWaterMaterials();
            BuildSurfaceMaterials();   // textured road, yard, cladding and concrete (replaces the flat wet gloss)
        }

        private static Material Particle(string name, Color colour, Texture2D tex)
        {
            var m = Asset(name, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetColor("_BaseColor", colour);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- Coast road ----------------

        private static Coast BuildCoast(Transform world, Random rng, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup sfx, UnityEngine.Audio.AudioMixerGroup amb)
        {
            var coast = new Coast();
            var root = new GameObject("CoastRoad").transform;
            root.SetParent(world, false);

            // Land east of the port: coastal strip to the seawall, an embankment rising south of the service road.
            Begin();
            B(wetConcrete).Ground(new Vector3(880f, 0f, 100f), new Vector3(2620f, 0f, QuayEdge), 8f);
            B(rock).Quad(new Vector3(880f, 0f, 100f), new Vector3(2620f, 0f, 100f), new Vector3(2620f, 26f, 30f), new Vector3(880f, 26f, 30f),
                         Vector2.zero, new Vector2(40f, 0f), new Vector2(40f, 4f), new Vector2(0f, 4f));
            B(rock).Quad(new Vector3(2620f, 0f, 100f), new Vector3(2620f, 0f, QuayEdge), new Vector3(2690f, 30f, QuayEdge), new Vector3(2690f, 26f, 30f),
                         Vector2.zero, new Vector2(10f, 0f), new Vector2(10f, 4f), new Vector2(0f, 4f));
            Flush("CoastGround", root, drivable, false);
            var ground = Collider(root, "CoastGroundCollider", new Vector3(1407f, -0.5f, 200f), new Vector3(1054f, 1f, 200f), drivable);
            ground.gameObject.AddComponent<DrivingSurface>().EditorConfigure(0.74f, 1.3f);
            Collider(root, "CoastBoundEast", new Vector3(1932f, 2f, 200f), new Vector3(1f, 4f, 200f), environment);
            Collider(root, "CoastBoundSouth", new Vector3(1407f, 2f, 104f), new Vector3(1054f, 4f, 1f), environment);
            Collider(root, "CoastSeaEdge", new Vector3(1386f, 0.8f, QuayEdge), new Vector3(1012f, 1.6f, 0.6f), environment);

            var furniture = BuildCoastFurniture(root, rng);
            foreach (float c in WaveCentres)
            {
                SignPanel(root, "STORM SURGE", new Color(1f, 0.55f, 0.15f), new Vector3(c - WaveHalf - 14f, 4.2f, CoastZ0 - 3f), 180f, 7f, true);
                SignPanel(root, "WAVE ZONE", new Color(1f, 0.3f, 0.25f), new Vector3(c + WaveHalf + 14f, 4.2f, CoastZ0 - 3f), 0f, 6f, true);
                // Wigwag beacons on both signs: dark when calm, alternating amber while the section is live.
                int k = System.Array.IndexOf(WaveCentres, c);
                var wig = CubeMesh("HR_WigwagLamp", new Vector3(0.42f, 0.42f, 0.3f));
                foreach (float sx in new[] { c - WaveHalf - 14f, c + WaveHalf + 14f })
                    foreach (float o in new[] { -1f, 1f })
                        furniture.Wigwags[k].Add(Single("Wigwag", root, wig, beacon, detail, new Vector3(sx + o * 1.1f, 5.9f, CoastZ0 - 3f)).GetComponent<Renderer>());
            }
            SignPanel(root, "COAST ROAD", new Color(0.33f, 0.91f, 1f), new Vector3(900f, 6f, 288f), 270f, 8f, true);
            SignPanel(root, "SERVICE ROAD", new Color(0.33f, 0.91f, 1f), new Vector3(906f, 4.5f, 146f), 270f, 7f, true);

            // Roadside structures between the two roads: pump station, storage sheds and a small fuel depot (landmarks).
            Begin();
            Building(new Rect(1000f, 180f, 60f, 40f), 9f, claddingDark, bays: true);
            Building(new Rect(1300f, 182f, 80f, 36f), 11f, cladding, office: true);
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(1480f + i * 26f, 0f, 205f);
                B(metal).Cylinder(p, 9f, 14f, 24, true, 4f);
                B(hazardStripe).Cylinder(p + Vector3.up * 13f, 9.05f, 0.6f, 24, false, 4f);
                Collider(root, "CoastTank", p + Vector3.up * 7f, new Vector3(18f, 14f, 18f), environment);
            }
            Flush("CoastBuildings", root, environment);

            BuildWaves(root, coast, clips, amb, furniture);
            BuildHeadland(world, rng, clips, amb, coast);
            BuildExtractionCompound(root, coast);
            report.AppendLine($"coast road: {1916f - 884f:0} m sea road with {WaveCentres.Length} wave sections, {1600f - 884f:0} m dry service road, seawall {HeadWallW - 880f:0} m + breakwater");
            return coast;
        }

        private static void BuildWaves(Transform root, Coast coast, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup amb, CoastFurniture furniture)
        {
            var wavesGo = new GameObject("Waves");
            wavesGo.transform.SetParent(root, false);
            var waves = wavesGo.AddComponent<HarborWaves>();
            var sections = new List<HarborWaves.Section>();
            var beaconMesh = CubeMesh("HR_BeaconBox", new Vector3(0.5f, 0.5f, 0.5f));
            string[] labels = { "SEAWALL WEST", "SEAWALL MID", "SEAWALL EAST" };
            var wind = new Vector3(-0.6f, 0f, -0.8f).normalized;
            for (int k = 0; k < WaveCentres.Length; k++)
            {
                float c = WaveCentres[k];
                var sec = new GameObject($"WaveSection_{k}").transform;
                sec.SetParent(wavesGo.transform, false);
                sec.position = new Vector3(c, 0f, QuayEdge);

                // A. + B. + C. The breaking wave itself (swell → face → curling lip → plunge → collapse), with crest spray.
                var crestSpray = SprayParticles("CrestSpray", sec, 600, 0.45f, new Vector2(0.8f, 2.0f), 0.3f, 0.12f);
                var plunge = SprayParticles("PlungeSplash", sec, 600, 0.9f, new Vector2(0.8f, 1.8f), 0.35f, 0.08f);
                var breakerGo = new GameObject("Breaker") { layer = LayerMask.NameToLayer("Water") };
                breakerGo.transform.SetParent(sec, false);
                breakerGo.AddComponent<MeshFilter>();
                breakerGo.AddComponent<MeshRenderer>().sharedMaterial = breakerMat;
                var breaker = breakerGo.AddComponent<HarborBreaker>();
                breaker.EditorConfigure(c, WallFace - 0.2f, 64f, crestSpray, plunge, null, 3f + k * 5.7f);

                // D. Impact spray up the wall face and the mist that drifts downwind over the road.
                var plume = SprayParticles("WallSpray", sec, 1600, 0.9f, new Vector2(0.8f, 1.7f), 0.3f, 0.11f);
                var mist = Particles("Mist", sec, mistMat, 160, ps =>
                {
                    var main = ps.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(7f, 15f);
                    main.startColor = new Color(1f, 1f, 1f, 0.35f);
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    var shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(WaveHalf * 2f, 4f, 6f);
                    var vel = ps.velocityOverLifetime;
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.World;
                    vel.x = new ParticleSystem.MinMaxCurve(wind.x * 7f, wind.x * 4f);
                    vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
                    vel.z = new ParticleSystem.MinMaxCurve(wind.z * 7f, wind.z * 4f);
                    var col = ps.colorOverLifetime; col.enabled = true;
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.2f), new GradientAlphaKey(0f, 1f) });
                    col.color = g;
                    var e = ps.emission; e.rateOverTime = 0.6f;
                    ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.25f;
                });
                mist.transform.localPosition = new Vector3(0f, 3f, 1f);

                // E. Green water over the wave wall, F. the sheet sweeping the road, G. puddles left behind.
                var overtop = MeshObject("Overtop", sec, OvertopMesh(c, k), overtopMat);
                var flood = MeshObject("FloodSheet", sec, FloodMesh(c, k), floodSheetMat);
                var puddles = new List<Renderer>();
                var quad = new NeonRift.EditorTools.District.MeshBuilder();
                quad.Decal(Vector3.zero, 1f, 1f);
                var quadMesh = SaveMesh(quad.ToMesh("HR_PuddleQuad", false), "HR_PuddleQuad");
                for (int i = 0; i < 6; i++)
                {
                    var r = MeshObject("Puddle", sec, quadMesh, puddleSheetMat);
                    float px = c + (i - 2.5f) * 11f + (float)(i * 37 % 5), pz = i % 2 == 0 ? CoastZ1 - 1.6f : CoastZ0 + 1.4f;
                    r.transform.SetPositionAndRotation(new Vector3(px, 0.048f, pz), Quaternion.Euler(0f, i * 23f, 0f));
                    r.transform.localScale = new Vector3(4f + i % 3 * 1.5f, 1f, 2.4f + i % 2 * 1.2f);
                    puddles.Add(r);
                }
                // Droplets raining out of the spray onto the road; scuppers pouring back into the sea as it drains.
                var droplets = Particles("Droplets", sec, dropletMat, 900, ps =>
                {
                    var main = ps.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
                    main.gravityModifier = 1f;
                    var shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(WaveHalf * 2f + 6f, 3f, 5f);
                    var vel = ps.velocityOverLifetime;
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.World;
                    vel.x = new ParticleSystem.MinMaxCurve(wind.x * 4f, wind.x * 1f);
                    vel.y = new ParticleSystem.MinMaxCurve(1f, 5f);
                    vel.z = new ParticleSystem.MinMaxCurve(-12f, -5f);
                    var e = ps.emission; e.rateOverTime = 0f;
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = 0.05f;
                    r.lengthScale = 1.5f;
                });
                droplets.transform.position = new Vector3(c, 4f, 291f);
                var spouts = Particles("ScupperSpouts", sec, dropletMat, 300, ps =>
                {
                    var main = ps.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
                    main.gravityModifier = 1f;
                    main.startColor = new Color(1f, 1f, 1f, 0.7f);
                    var shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(WaveHalf * 2f, 0.2f, 0.1f);
                    var vel = ps.velocityOverLifetime;
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.World;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                    vel.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.4f);
                    vel.z = new ParticleSystem.MinMaxCurve(1.5f, 3f);
                    var e = ps.emission; e.rateOverTime = 0f;
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = 0.08f;
                });
                spouts.transform.position = new Vector3(c, -0.1f, WallFace + 0.15f);

                // Grip: a deep low-grip surface under the moving flood, a milder one while it drains (resized at runtime).
                var slick = Collider(sec, "Slick", new Vector3(c, -0.47f, (CoastZ0 + CoastZ1) * 0.5f), new Vector3(WaveHalf * 2f + 8f, 1f, CoastZ1 - CoastZ0 + 2f), drivable);
                slick.gameObject.AddComponent<DrivingSurface>().EditorConfigure(0.42f, 8f);
                slick.enabled = false;
                var shallow = Collider(sec, "SlickShallow", new Vector3(c, -0.475f, (CoastZ0 + CoastZ1) * 0.5f), new Vector3(WaveHalf * 2f + 8f, 1f, CoastZ1 - CoastZ0 + 2f), drivable);
                shallow.gameObject.AddComponent<DrivingSurface>().EditorConfigure(0.6f, 3f);
                shallow.enabled = false;

                var flashGo = new GameObject("ImpactFlash") { layer = detail };
                flashGo.transform.SetParent(sec, false);
                flashGo.transform.localPosition = new Vector3(0f, 12f, -6f);
                var flash = flashGo.AddComponent<Light>();
                flash.type = LightType.Point; flash.color = new Color(0.75f, 0.85f, 1f); flash.range = 70f; flash.intensity = 0f; flash.shadows = LightShadows.None; flash.enabled = false;
                var impact = Spatial(sec, "Impact", null, false, amb, 1f, 25f, 420f);
                impact.transform.localPosition = new Vector3(0f, 2f, 2f);
                var rush = Spatial(sec, "Rush", clips.WaterRush, true, amb, 0f, 10f, 140f);
                rush.playOnAwake = false;
                rush.transform.localPosition = new Vector3(0f, 0.5f, -16f);
                var horn = Spatial(sec, "SurgeHorn", null, false, amb, 1f, 30f, 600f);
                horn.transform.localPosition = new Vector3(-WaveHalf - 14f, 6f, CoastZ0 - 3f - QuayEdge);
                var drainLoop = Spatial(sec, "Drain", clips.Drain, true, amb, 0.5f, 6f, 60f);
                drainLoop.playOnAwake = false;
                drainLoop.transform.localPosition = new Vector3(0f, 0f, CoastZ0 - QuayEdge);

                var beacons = new List<Renderer>();
                Begin();
                foreach (float e in new[] { -WaveHalf - 3f, WaveHalf + 3f })
                {
                    var p = new Vector3(c + e, 0f, CoastZ0 - 2.5f);
                    B(metal).Cylinder(p, 0.1f, 4f, 6, true);
                    beacons.Add(Single("WaveBeacon", sec, beaconMesh, beacon, detail, p + Vector3.up * 4.3f).GetComponent<Renderer>());
                    var q = new Vector3(c + e, 0f, 290.4f);
                    beacons.Add(Single("WaveBeacon", sec, beaconMesh, beacon, detail, q + Vector3.up * 1.8f).GetComponent<Renderer>());
                    B(metal).Cylinder(q, 0.08f, 1.6f, 6, true);
                    // Hazard paint across the road at each end of the section.
                    B(hazardStripe).Strip(new Vector3(c + e + (e < 0 ? 3f : -3f), 0.05f, CoastZ0 + 0.5f), new Vector3(c + e + (e < 0 ? 3f : -3f), 0.05f, CoastZ1 - 0.5f), 1.4f);
                }
                Flush("BeaconPoles_" + k, root, detail, false);
                coast.CoastLamps.AddRange(beacons);

                sections.Add(new HarborWaves.Section
                {
                    label = labels[k], road = new Rect(c - WaveHalf, CoastZ0 - 1f, WaveHalf * 2f, CoastZ1 - CoastZ0 + 2f),
                    breaker = breaker, overtop = overtop, flood = flood, puddles = puddles.ToArray(), plume = plume, mist = mist, droplets = droplets, spouts = spouts,
                    impact = impact, rush = rush, horn = horn, slickSurface = slick, slickShallow = shallow, beacons = beacons.ToArray(),
                    wigwags = furniture.Wigwags[k].ToArray(), flash = flash, floodlights = furniture.Floodlights[k].ToArray()
                });
                drainLoops.Add(drainLoop);
            }
            Configure(waves, so =>
            {
                var arr = so.FindProperty("sections");
                arr.arraySize = sections.Count;
                for (int i = 0; i < sections.Count; i++)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    var s = sections[i];
                    e.FindPropertyRelative("label").stringValue = s.label;
                    e.FindPropertyRelative("road").rectValue = s.road;
                    e.FindPropertyRelative("breaker").objectReferenceValue = s.breaker;
                    e.FindPropertyRelative("overtop").objectReferenceValue = s.overtop;
                    e.FindPropertyRelative("flood").objectReferenceValue = s.flood;
                    Array(e.FindPropertyRelative("puddles"), s.puddles);
                    e.FindPropertyRelative("plume").objectReferenceValue = s.plume;
                    e.FindPropertyRelative("mist").objectReferenceValue = s.mist;
                    e.FindPropertyRelative("droplets").objectReferenceValue = s.droplets;
                    e.FindPropertyRelative("spouts").objectReferenceValue = s.spouts;
                    e.FindPropertyRelative("impact").objectReferenceValue = s.impact;
                    e.FindPropertyRelative("rush").objectReferenceValue = s.rush;
                    e.FindPropertyRelative("horn").objectReferenceValue = s.horn;
                    e.FindPropertyRelative("drain").objectReferenceValue = drainLoops[i];
                    e.FindPropertyRelative("slickSurface").objectReferenceValue = s.slickSurface;
                    e.FindPropertyRelative("slickShallow").objectReferenceValue = s.slickShallow;
                    Array(e.FindPropertyRelative("beacons"), s.beacons);
                    Array(e.FindPropertyRelative("wigwags"), s.wigwags);
                    e.FindPropertyRelative("flash").objectReferenceValue = s.flash;
                    Array(e.FindPropertyRelative("floodlights"), s.floodlights);
                }
                Array(so.FindProperty("impactClips"), clips.WaveImpacts);
                so.FindProperty("approachClip").objectReferenceValue = clips.WaveRoar;
                so.FindProperty("hornClip").objectReferenceValue = clips.SurgeHorn;
            });
            drainLoops.Clear();
            coast.Waves = waves;
            report.AppendLine($"waves: {WaveCentres.Length} sections, breaker mesh {sections[0].breaker.GetComponent<HarborBreaker>() != null} (64×112 grid), overtop ribbon + flood sheet + 6 puddles each, 5 particle layers (crest spray, plunge, wall spray, mist, droplets) + scupper spouts");
        }

        private static readonly List<AudioSource> drainLoops = new();

        private static float wallZ() => 291f;

        private static void BuildExtractionCompound(Transform root, Coast coast)
        {
            // A memorable extraction gate: twin lit pylons with a beacon arch over the coast road, a lit pickup ring and a
            // freight container "safe box" with its doors open, waiting.
            Begin();
            float ax = Extraction.x - 22f;
            foreach (float z in new[] { PierZ0 - 2.6f, PierZ1 + 2.6f })
                B(metal).Cuboid(new Vector3(ax - 1f, 0f, z - 1f), new Vector3(ax + 1f, 12f, z + 1f), 2f);
            B(metal).Cuboid(new Vector3(ax - 1.2f, 12f, PierZ0 - 3.6f), new Vector3(ax + 1.2f, 13.6f, PierZ1 + 3.6f), 2f);
            B(neonCyan).Cuboid(new Vector3(ax - 1.25f, 11.6f, PierZ0 - 3.6f), new Vector3(ax - 1.2f, 12f, PierZ1 + 3.6f), 2f);
            B(extractGlow).Decal(Extraction + Vector3.up * 0.06f, 26f, 26f);
            B(hazardStripe).Strip(new Vector3(ax + 4f, 0.06f, PierZ0 + 1f), new Vector3(ax + 4f, 0.06f, PierZ1 - 1f), 1.2f);
            StackBox(containers[3], new Vector3(Extraction.x + 14f, 1.3f, PierZ1 - 6f), new Vector3(12.2f, 2.6f, 2.44f), Quaternion.Euler(0f, 90f, 0f));
            Flush("ExtractionCompound", root, environment, false);
            SignPanel(root, "EXTRACTION", new Color(0.3f, 1f, 0.6f), new Vector3(ax, 14.8f, (PierZ0 + PierZ1) * 0.5f), 90f, 9f);
            var beaconMesh = CubeMesh("HR_StrobeBox", new Vector3(0.6f, 0.6f, 0.6f));
            foreach (float z in new[] { PierZ0 - 2.6f, PierZ1 + 2.6f })
            {
                var r = Single("ExtractionStrobe", root, beaconMesh, strobe, detail, new Vector3(ax, 12.6f, z)).GetComponent<Renderer>();
                var bl = r.gameObject.AddComponent<HarborBlinker>();
                Configure(bl, so => { so.FindProperty("a").objectReferenceValue = r; so.FindProperty("colourA").colorValue = new Color(0.3f, 5f, 1.4f); so.FindProperty("colourB").colorValue = new Color(0.3f, 5f, 1.4f); so.FindProperty("rate").floatValue = 1.2f; });
            }
            var l = new GameObject("ExtractionLight") { layer = detail };
            l.transform.SetParent(root, false);
            l.transform.SetPositionAndRotation(Extraction + Vector3.up * 16f, Quaternion.Euler(90f, 0f, 0f));
            var light = l.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(0.5f, 1f, 0.75f);
            light.intensity = 6000f;
            light.range = 40f;
            light.spotAngle = 80f;
            light.shadows = LightShadows.None;
        }

        // ---------------- Ocean ----------------

        private static void BuildOcean(Transform world, StormController storm, HarborWaves waves)
        {
            var go = new GameObject("Ocean") { layer = LayerMask.NameToLayer("Water") };
            go.transform.SetParent(world, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = ocean;
            var o = go.AddComponent<HarborOcean>();
            Configure(o, so =>
            {
                so.FindProperty("storm").objectReferenceValue = storm;
                so.FindProperty("waves").objectReferenceValue = waves;
                so.FindProperty("level").floatValue = SeaLevel;
                so.FindProperty("shoreZ").floatValue = QuayEdge;
            });
            Configure(waves, so => so.FindProperty("storm").objectReferenceValue = storm);
            foreach (var sb in Object.FindObjectsByType<HarborSprayBursts>(FindObjectsSortMode.None)) Configure(sb, so => so.FindProperty("storm").objectReferenceValue = storm);
            foreach (var s in waves.Sections)
                if (s.breaker != null) Configure(s.breaker, so => so.FindProperty("storm").objectReferenceValue = storm);
            report.AppendLine($"ocean: GPU Gerstner sea (4 swells damped toward the wall + wall chop + foam left by the breakers), 241×241 camera-following grid, 6 km radius, level {SeaLevel} m");
        }

        // ---------------- Ship-to-shore cranes ----------------

        public static readonly List<Light> craneLights = new();

        /// <summary>A ship-to-shore container crane: braced portal, A-frame with stays, twin box-girder boom over the
        /// ship, machinery house, operator cab, rows of cool-white work lights and two real downlights.</summary>
        private static void StsCrane(Transform root, float x)
        {
            Quaternion Look(Vector3 a, Vector3 b) => Quaternion.LookRotation(b - a, Vector3.up);
            void Beam(Material m, Vector3 a, Vector3 b, float w) => B(m).OrientedBox((a + b) * 0.5f, new Vector3(w, w, Vector3.Distance(a, b)), Look(a, b), 2f);
            foreach (float dx in new[] { -10f, 10f })
            {
                foreach (float z in new[] { 252f, 292f })
                {
                    B(craneBlue).Cuboid(new Vector3(x + dx - 0.9f, 1.6f, z - 0.9f), new Vector3(x + dx + 0.9f, 44f, z + 0.9f), 3f);
                    B(craneSteel).Cuboid(new Vector3(x + dx - 1.4f, 0f, z - 3f), new Vector3(x + dx + 1.4f, 1.6f, z + 3f), 2f);   // bogies
                    Collider(root, "CraneLeg", new Vector3(x + dx, 22f, z), new Vector3(1.8f, 44f, 1.8f), environment);
                    solids.Add(new Rect(x + dx - 1.2f, z - 3.2f, 2.4f, 6.4f));
                }
                // Portal: sill beam, cross brace and knee braces on each side.
                Beam(craneBlue, new Vector3(x + dx, 16f, 252f), new Vector3(x + dx, 16f, 292f), 1.2f);
                Beam(craneBlue, new Vector3(x + dx, 16f, 252f), new Vector3(x + dx, 42f, 292f), 0.8f);
                Beam(craneBlue, new Vector3(x + dx, 16f, 292f), new Vector3(x + dx, 42f, 252f), 0.8f);
                // A-frame up to the apex, and the stays out to the boom.
                Beam(craneBlue, new Vector3(x + dx, 44f, 252f), new Vector3(x + dx * 0.35f, 72f, 270f), 1.1f);
                Beam(craneBlue, new Vector3(x + dx, 44f, 292f), new Vector3(x + dx * 0.35f, 72f, 270f), 1.1f);
                foreach (float z in new[] { 320f, 352f, 384f, 410f }) Beam(metal, new Vector3(x + dx * 0.35f, 72f, 270f), new Vector3(x + dx * 0.4f, 48f, z), 0.18f);
                Beam(metal, new Vector3(x + dx * 0.35f, 72f, 270f), new Vector3(x + dx * 0.4f, 48f, 214f), 0.2f);
            }
            Beam(craneBlue, new Vector3(x - 10f, 44f, 252f), new Vector3(x + 10f, 44f, 252f), 1.4f);
            Beam(craneBlue, new Vector3(x - 10f, 44f, 292f), new Vector3(x + 10f, 44f, 292f), 1.4f);
            B(craneBlue).Cuboid(new Vector3(x - 4.5f, 71f, 268f), new Vector3(x + 4.5f, 73f, 272f), 2f);
            // Boom: two box girders with lattice diagonals, from the backreach to the outreach over the ship.
            foreach (float gx in new[] { -4f, 4f })
            {
                B(craneBlue).Cuboid(new Vector3(x + gx - 0.7f, 45f, 210f), new Vector3(x + gx + 0.7f, 48f, 412f), 4f);
                for (float z = 214f; z < 410f; z += 8f)
                    Beam(craneBlue, new Vector3(x + gx * 1.15f, 45f, z), new Vector3(x + gx * 1.15f, 48f, z + 8f), 0.2f);
                for (float z = 236f; z < 410f; z += 12f)
                    B(workLight).Cuboid(new Vector3(x + gx - 0.35f, 44.6f, z), new Vector3(x + gx + 0.35f, 44.95f, z + 0.7f), 1f);
            }
            for (float z = 214f; z < 410f; z += 16f) B(craneBlue).Cuboid(new Vector3(x - 4.7f, 47.6f, z), new Vector3(x + 4.7f, 48f, z + 0.6f), 2f);
            // Machinery house on the backreach, operator cab under the boom, aviation lights.
            B(cladding).Cuboid(new Vector3(x - 6f, 48f, 212f), new Vector3(x + 6f, 55f, 234f), 4f);
            B(indWindow).Cuboid(new Vector3(x - 6.05f, 50f, 214f), new Vector3(x + 6.05f, 52f, 232f), 4f);
            B(cladding).Cuboid(new Vector3(x - 1.8f, 40f, 300f), new Vector3(x + 1.8f, 44.6f, 304f), 1f);
            B(shipWindow).Cuboid(new Vector3(x - 1.85f, 41f, 300.5f), new Vector3(x + 1.85f, 43.5f, 303.5f), 1f);
            B(aviation).Cuboid(new Vector3(x - 0.4f, 73f, 269.6f), new Vector3(x + 0.4f, 73.8f, 270.4f), 1f);
            B(aviation).Cuboid(new Vector3(x - 0.4f, 48f, 411f), new Vector3(x + 0.4f, 48.8f, 412f), 1f);
            B(neonAmber).Cuboid(new Vector3(x - 10.9f, 15.4f, 251f), new Vector3(x - 9.1f, 15.6f, 293f), 2f);
            B(neonAmber).Cuboid(new Vector3(x + 9.1f, 15.4f, 251f), new Vector3(x + 10.9f, 15.6f, 293f), 2f);
            foreach (float z in new[] { 262f, 330f })
            {
                var go = new GameObject("CraneWorkLight") { layer = detail };
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(new Vector3(x, 44f, z), Quaternion.Euler(90f, 0f, 0f));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(0.82f, 0.9f, 1f);
                l.intensity = 14000f;
                l.range = 70f;
                l.spotAngle = 95f;
                l.innerSpotAngle = 45f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                craneLights.Add(l);
            }
        }

        // ---------------- Imported ships ----------------

        private static GameObject PlaceModel(string path, Transform parent, string name, float length, Vector3 centre, float yaw, float bottomY, Material overrideMaterial = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { report.AppendLine($"ships: MISSING {path}"); return null; }
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, holder);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            if (overrideMaterial != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(overrideMaterial, r.sharedMaterials.Length).ToArray();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float longest = Mathf.Max(b.size.x, b.size.z);
            float scale = length / Mathf.Max(0.0001f, longest);
            go.transform.localScale *= scale;
            // Align the hull's length with +X before the yaw.
            if (b.size.z > b.size.x) go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.localRotation;
            b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            go.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            holder.SetPositionAndRotation(new Vector3(centre.x, bottomY, centre.z), Quaternion.Euler(0f, yaw, 0f));
            foreach (var r in renderers) { r.shadowCastingMode = ShadowCastingMode.Off; GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic); }
            report.AppendLine($"ships: {name} ← {System.IO.Path.GetFileName(path)}, scaled ×{scale:0.##} to {length:0} m, {renderers.Length} renderers, size {b.size * 1f:F0}");
            return holder.gameObject;
        }

        /// <summary>The berth's container ship (replaces the old box silhouette) and real vessels in the bay.</summary>
        private static bool BuildShips(Transform world, Random rng, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup amb, List<AudioSource> horns)
        {
            var root = new GameObject("Ships").transform;
            root.SetParent(world, false);
            var main = PlaceModel($"{Imported}/Ships/ContainerShip/ContainerShip.fbx", root, "ContainerShip_KowloonStar", 300f, new Vector3(-318f, 0f, 332f), 0f, -11f + SeaLevel + 1f);
            if (main != null)
            {
                // Moored: no sidelights (a ship alongside shows deck and anchor-style white lights; see BuildVesselLights).
                Begin();
                var b = Bounds(main);
                float top = b.max.y;
                B(navWhite).Cuboid(new Vector3(b.center.x - 0.6f, top + 0.2f, b.center.z - 0.6f), new Vector3(b.center.x + 0.6f, top + 1.4f, b.center.z + 0.6f), 1f);
                B(navWhite).Cuboid(new Vector3(b.min.x - 0.3f, 16f, b.center.z - 0.6f), new Vector3(b.min.x, 17.4f, b.center.z + 0.6f), 1f);
                for (float x = b.min.x + 20f; x < b.max.x - 10f; x += 40f) B(aviation).Cuboid(new Vector3(x, top - 8f, b.min.z - 0.3f), new Vector3(x + 0.8f, top - 7.2f, b.min.z), 1f);
                Flush("ShipNavLights", root, detail, false);
                foreach (float x in new[] { -400f, -240f })
                {
                    var l = new GameObject("DeckFlood") { layer = detail };
                    l.transform.SetParent(root, false);
                    l.transform.SetPositionAndRotation(new Vector3(x, 44f, 300f), Quaternion.Euler(55f, 0f, 0f));
                    var light = l.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.color = new Color(1f, 0.78f, 0.5f);
                    light.intensity = 30000f;
                    light.range = 90f;
                    light.spotAngle = 70f;
                    light.shadows = LightShadows.None;
                }
                horns.Add(Spatial(main.transform, "ShipHorn", null, false, amb, 1f, 60f, 1600f));
            }
            var lib = PlaceModel($"{Imported}/Ships/LibertyShip/LibertyShip.fbx", root, "LibertyShip_AtAnchor", 135f, new Vector3(420f, 0f, 820f), 64f, -8f + SeaLevel + 1f, liberty);
            if (lib != null) horns.Add(Spatial(lib.transform, "ShipHorn", null, false, amb, 1f, 80f, 2200f));
            var cruise = PlaceModel("Assets/ThirdParty/Sketchfab/Ships/cruise_ship.glb", root, "CruiseShip_OuterRoads", 270f, new Vector3(-980f, 0f, 1350f), 18f, -7f + SeaLevel + 1f);
            if (cruise != null)
            {
                horns.Add(Spatial(cruise.transform, "ShipHorn", null, false, amb, 1f, 120f, 2600f));
                Begin();
                var b = Bounds(cruise);
                for (int deck = 0; deck < 5; deck++)
                    B(shipWindow).OrientedBox(new Vector3(b.center.x, b.min.y + 14f + deck * 3.2f, b.center.z), new Vector3(b.size.x * 0.82f, 0.7f, b.size.z * 1.02f), Quaternion.identity, 8f);
                Flush("CruiseDeckLights", root, LayerMask.NameToLayer("Default"), false);
            }
            // Work boats: two moving in the harbour basin, one moored at the coast road jetty.
            var boats = new List<GameObject>();
            foreach (var (pos, yaw) in new[] { (new Vector3(100f, 0f, 430f), 90f), (new Vector3(700f, 0f, 520f), -90f), (new Vector3(2262f, 0f, 579.5f), 90f) })
            {
                var boat = PlaceModel($"{Imported}/Ships/DivingBoat/DivingBoat.fbx", root, "WorkBoat", 18f, pos, yaw, -3f + SeaLevel + 1f);
                if (boat != null)
                {
                    // The movers steer a body's +z along its path: turn the hull (placed along +x, bow at −x) so the bow is +z.
                    var model = boat.transform.GetChild(0);
                    var turn = Quaternion.Euler(0f, 90f, 0f);
                    model.localPosition = turn * model.localPosition;
                    model.localRotation = turn * model.localRotation;
                    boat.transform.rotation = Quaternion.Euler(0f, yaw - 90f, 0f);
                    boats.Add(boat);
                }
            }
            lifeBoats = boats;
            BuildVesselLights(root, rng);
            return main != null;
        }

        private static List<GameObject> lifeBoats = new();
        private static Transform[] bayBoxes = new Transform[0];
        private static ParticleSystem sealSparksPs, liftRunoffPs;

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ---------------- Roadblocks, stations, spreaders ----------------

        private static void BuildBlockades(Transform world, Coast coast, Random rng, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup sfx)
        {
            var root = new GameObject("Roadblocks").transform;
            root.SetParent(world, false);
            var packs = AssetDatabase.FindAssets("PackCar_ t:Prefab", new[] { "Assets/_NeonRift/Prefabs/Traffic" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g => g != null).ToArray();
            var siren = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_NeonRift/Audio/Generated/Mission/Siren_Loop.wav");
            var strobeMesh = CubeMesh("HR_LightBar", new Vector3(0.7f, 0.18f, 0.3f));
            // Sites: across the quay road, lane 80 and the terminal road, each west of the east road (which stays open).
            foreach (var (name, centre, alongX, width) in new[] { ("THE QUAY ROAD", new Vector3(622f, 0f, 230f), true, 16f), ("LANE 80", new Vector3(600f, 0f, 80f), true, 12f), ("THE TERMINAL ROAD", new Vector3(600f, 0f, -50f), true, 14f) })
            {
                var site = new GameObject("Roadblock_" + name.Replace(' ', '_')).transform;
                site.SetParent(root, false);
                site.position = centre;
                Begin();
                for (float z = -width * 0.5f + 1.7f; z < width * 0.5f; z += 3.4f) Jersey(centre + new Vector3(2.5f, 0f, z), 90f);
                Flush("Barriers", site, environment, false);
                Collider(site, "RoadblockBarrier", centre + new Vector3(2.5f, 0.8f, 0f), new Vector3(0.8f, 1.6f, width), environment);
                foreach (float dz in new[] { -width * 0.25f, width * 0.25f })
                {
                    if (packs.Length == 0) break;
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(packs[rng.Next(packs.Length)], site);
                    car.transform.SetPositionAndRotation(centre + new Vector3(-4f, 0f, dz), Quaternion.Euler(0f, dz > 0 ? 70f : 110f, 0f));
                    StripScripts(car);
                    foreach (var rb in car.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                    var top = Bounds(car).max.y;
                    var a = Single("BarRed", car.transform, strobeMesh, strobe, detail, car.transform.position + car.transform.right * -0.4f + Vector3.up * (top + 0.1f)).GetComponent<Renderer>();
                    var b = Single("BarBlue", car.transform, strobeMesh, strobe, detail, car.transform.position + car.transform.right * 0.4f + Vector3.up * (top + 0.1f)).GetComponent<Renderer>();
                    var lg = new GameObject("BarLight") { layer = detail };
                    lg.transform.SetParent(car.transform, false);
                    lg.transform.position = car.transform.position + Vector3.up * (top + 0.6f);
                    var light = lg.AddComponent<Light>();
                    light.type = LightType.Point; light.range = 34f; light.intensity = 160f; light.shadows = LightShadows.None;
                    var bl = car.AddComponent<HarborBlinker>();
                    Configure(bl, so => { so.FindProperty("a").objectReferenceValue = a; so.FindProperty("b").objectReferenceValue = b; so.FindProperty("lightSource").objectReferenceValue = light; });
                }
                Collider(site, "PatrolCars", centre + new Vector3(-4f, 0.8f, 0f), new Vector3(5f, 1.6f, width - 2f), environment);
                var s = Spatial(site, "Siren", siren, true, sfx, 0.55f, 8f, 160f);
                s.playOnAwake = true;
                site.gameObject.SetActive(false);
                coast.Blockades.Add(site.gameObject);
                coast.BlockadeAreas.Add(new Rect(centre.x - 6f, centre.z - width * 0.5f - 1f, 12f, width + 2f));
                coast.BlockadeNames.Add(name);
            }
        }

        private static (Transform overridePoint, Transform relayPoint, Transform[] spreaders, ParticleSystem sparks) BuildStations(Transform world, Transform points)
        {
            var root = new GameObject("Stations").transform;
            root.SetParent(world, false);
            Begin();
            // Gate junction box beside the east gate, north kerb of the quay road.
            var jb = new Vector3(646f, 0f, 241.5f);
            B(junction).Cuboid(jb + new Vector3(-1.2f, 0f, -0.5f), jb + new Vector3(1.2f, 2.2f, 0.5f), 1f);
            B(hazardStripe).Cuboid(jb + new Vector3(-1.25f, 2.2f, -0.55f), jb + new Vector3(1.25f, 2.4f, 0.55f), 1f);
            B(metal).Cylinder(jb + new Vector3(0.8f, 2.4f, 0f), 0.12f, 3.5f, 6, true);
            // Drone relay mast on the berth apron.
            var rl = new Vector3(-120f, 0f, 242.5f);
            B(junction).Cuboid(rl + new Vector3(-1f, 0f, -0.5f), rl + new Vector3(1f, 1.8f, 0.5f), 1f);
            B(metal).Cylinder(rl, 0.18f, 9f, 8, true);
            B(metal).Cuboid(rl + new Vector3(-1.5f, 8.5f, -0.1f), rl + new Vector3(1.5f, 8.8f, 0.1f), 1f);
            Flush("StationMeshes", root, environment, false);
            Collider(root, "JunctionBox", jb + Vector3.up * 1.1f, new Vector3(2.4f, 2.2f, 1f), environment);
            Collider(root, "RelayBox", rl + Vector3.up * 0.9f, new Vector3(2f, 1.8f, 1f), environment);
            var led = CubeMesh("HR_StationLed", new Vector3(0.25f, 0.25f, 0.08f));
            Single("JunctionLed", root, led, gateLamp, detail, jb + new Vector3(0f, 1.6f, -0.55f));
            Single("RelayLed", root, led, secLamp, detail, rl + new Vector3(0f, 9.1f, 0f));
            SignPanel(root, "GATE POWER", new Color(1f, 0.7f, 0.2f), jb + new Vector3(0f, 3.4f, -0.6f), 180f, 3.6f);
            var sparks = Particles("GateSparks", root, splash, 200, ps =>
            {
                var main = ps.main;
                main.loop = false; main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
                main.startColor = new Color(1f, 0.75f, 0.3f);
                main.gravityModifier = 1.2f;
                var e = ps.emission; e.rateOverTime = 0f;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.2f;
            });
            sparks.transform.position = jb + new Vector3(0f, 2f, -0.6f);
            Transform P(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(points, false); t.position = p; return t; }

            // Bay containers (separate objects so KX-7 can be lifted) and their spreaders: a lifting frame with corner
            // twistlocks, a status lamp and hoist cables, hanging from the bay gantry (24 m) at rest 11 m above the bay.
            var spreaders = new Transform[0];   // one spreader on the bay gantry crane (HarborRainBuilder.Heist)
            var boxes = new Transform[Bays.Length];
            for (int i = 0; i < Bays.Length; i++)
            {
                var box = new GameObject($"BayContainer_{i}") { layer = environment }.transform;
                box.SetParent(root, false);
                box.position = Bays[i];
                // 2.2: the detailed ISO container (posts, castings, door bars, markings) — same footprint, pivot and collider.
                var detailed = Part("ContainerMesh", box, ContainerMesh(containers[3]), containerMats[containers[3]], environment, true);
                ContainerMarkings(detailed.transform, 101 + i);
                var bc = box.gameObject.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 1.3f, 0f); bc.size = new Vector3(2.44f, 2.6f, 12.2f);
                boxes[i] = box;

            }
            bayBoxes = boxes;
            sealSparksPs = Particles("SealSparks", root, splash, 200, ps =>
            {
                var main = ps.main;
                main.loop = false; main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 1f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 7f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
                main.startColor = new Color(1f, 0.8f, 0.4f);
                main.gravityModifier = 1.4f;
                var e = ps.emission; e.rateOverTime = 0f;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;
            });
            liftRunoffPs = Particles("LiftRunoff", root, dropletMat, 400, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                main.gravityModifier = 1f;
                main.startColor = new Color(1f, 1f, 1f, 0.6f);
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(12f, 0.1f, 2.4f);
                var e = ps.emission; e.rateOverTime = 0f;
                var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f;
            });
            liftRunoffPs.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            return (P("OverridePoint", new Vector3(646f, 0f, 231f)), P("RelayPoint", new Vector3(-120f, 0f, 232f)), spreaders, sparks);
        }

        // ---------------- Activity ----------------

        private static HarborLife BuildLife(Transform world, Random rng, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup amb, List<AudioSource> horns)
        {
            var root = new GameObject("PortActivity").transform;
            root.SetParent(world, false);
            var packs = AssetDatabase.FindAssets("PackCar_ t:Prefab", new[] { "Assets/_NeonRift/Prefabs/Traffic" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g => g != null).ToArray();
            var movers = new List<HarborLife.Mover>();

            // Terminal tractors: generated cab + chassis + a container, on the freight service road and the industrial service road.
            int tractorN = 0;
            GameObject Tractor(string name, Material box, Vector3 a, Vector3 b)
            {
                var go = TerminalTractor(root, name, box, clips.Machinery, amb, tractorN++);
                go.transform.SetPositionAndRotation(a, Quaternion.LookRotation(b - a));
                return go;
            }
            // One-way runs with an unloading stop at the far end; they re-enter out of view (HarborLife.Mover.recycle).
            var freightA = new Vector3(170f, 0f, -240f); var freightB = new Vector3(615f, 0f, -240f);
            var indA = new Vector3(-620f, 0f, -120f); var indB = new Vector3(-80f, 0f, -120f);
            var coastA = new Vector3(930f, 0f, 156f); var coastB = new Vector3(1560f, 0f, 156f);
            movers.Add(new HarborLife.Mover { body = Tractor("TerminalTractor_Freight", containers[6 % containers.Length], freightA, freightB).transform, speed = 7f, pause = 9f, recycle = true,
                path = new[] { freightA, freightB } });
            movers.Add(new HarborLife.Mover { body = Tractor("TerminalTractor_Industrial", containers[5], indA, indB).transform, speed = 6.5f, pause = 10f, recycle = true,
                path = new[] { indA, indB } });
            movers.Add(new HarborLife.Mover { body = Tractor("TerminalTractor_Coast", containers[0], coastA, coastB).transform, speed = 8f, pause = 12f, recycle = true,
                path = new[] { coastA, coastB } });
            foreach (var boat in lifeBoats.Take(2))
            {
                var p = boat.transform.position;
                movers.Add(new HarborLife.Mover { body = boat.transform, speed = 4f, pause = 2f, path = new[] { p, p + new Vector3(260f, 0f, 60f), p + new Vector3(420f, 0f, 220f), p + new Vector3(80f, 0f, 260f) } });
            }

            // Ship-to-shore cranes working the ship (trolley, spreader, ropes, a box being re-stowed), searchlights sweeping
            // the restricted yard, and the vessels at anchor riding the swell.
            var oscillators = new List<HarborLife.Oscillator>();
            var cranes = BuildCraneRigs(root, rng);
            var searchlights = BuildSearchlights(root);
            var rockers = new List<HarborLife.Rocker>();
            foreach (var (name, deg, heave, period) in new[] { ("LibertyShip_AtAnchor", new Vector3(1.6f, 0f, 0.5f), 0.35f, 10f), ("CruiseShip_OuterRoads", new Vector3(0.6f, 0f, 0.25f), 0.25f, 13f) })
            {
                var ship = GameObject.Find(name);
                if (ship != null && ship.transform.childCount > 0) rockers.Add(new HarborLife.Rocker { target = ship.transform.GetChild(0), degrees = deg, heave = heave, period = period, phase = rockers.Count * 0.37f });
            }
            rockers.AddRange(extraRockers);
            foreach (var boat in lifeBoats)
                if (boat != null && boat.transform.childCount > 0) rockers.Add(new HarborLife.Rocker { target = boat.transform.GetChild(0), degrees = new Vector3(4f, 0f, 2f), heave = 0.35f, period = 4.5f, phase = rockers.Count * 0.29f });

            // Steam and exhaust: vents on the tank farm, generators and warehouse roofs.
            var steam = new List<ParticleSystem>();
            foreach (var p in new[] { new Vector3(-464f, 14f, -147f), new Vector3(-540f, 18f, -240f), new Vector3(-300f, 17f, -240f), new Vector3(-200f, 13f, -150f),
                                      new Vector3(560f, 14f, -125f), new Vector3(1010f, 10f, 200f), new Vector3(-80f, 19f, -245f), new Vector3(-420f, 15f, -90f) })
            {
                var ps = Particles("Steam", root, mistMat, 160, s =>
                {
                    var main = s.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startColor = new Color(0.8f, 0.82f, 0.85f, 0.5f);
                    var e = s.emission; e.rateOverTime = 1.5f;
                    var shape = s.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 12f; shape.radius = 0.4f;
                    var vel = s.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                    vel.x = new ParticleSystem.MinMaxCurve(-3f, -1.5f); vel.y = new ParticleSystem.MinMaxCurve(0.5f, 1f); vel.z = new ParticleSystem.MinMaxCurve(-4f, -2f);
                    var size = s.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
                });
                ps.transform.position = p;
                ps.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                steam.Add(ps);
            }

            foreach (var m in movers) if (m.body != null && m.path.Length > 0)
            {
                m.body.position = m.path[0];
                if (m.path.Length > 1) m.body.rotation = Quaternion.LookRotation(m.path[1] - m.path[0]);
            }
            var life = root.gameObject.AddComponent<HarborLife>();
            Configure(life, so =>
            {
                var m = so.FindProperty("movers");
                m.arraySize = movers.Count;
                for (int i = 0; i < movers.Count; i++)
                {
                    var e = m.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("body").objectReferenceValue = movers[i].body;
                    e.FindPropertyRelative("speed").floatValue = movers[i].speed;
                    e.FindPropertyRelative("pause").floatValue = movers[i].pause;
                    e.FindPropertyRelative("recycle").boolValue = movers[i].recycle;
                    Vectors(e.FindPropertyRelative("path"), movers[i].path);
                }
                var o = so.FindProperty("oscillators");
                o.arraySize = oscillators.Count;
                for (int i = 0; i < oscillators.Count; i++)
                {
                    var e = o.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("target").objectReferenceValue = oscillators[i].target;
                    e.FindPropertyRelative("offset").vector3Value = oscillators[i].offset;
                    e.FindPropertyRelative("period").floatValue = oscillators[i].period;
                    e.FindPropertyRelative("phase").floatValue = oscillators[i].phase;
                }
                var sp = so.FindProperty("spinners");
                // Searchlight heads turn themselves (HarborSearchlight: sweep, or converge on the theft); the rest are plain spinners.
                sp.arraySize = extraSpinners.Count;
                for (int i = 0; i < extraSpinners.Count; i++)
                {
                    sp.GetArrayElementAtIndex(i).FindPropertyRelative("target").objectReferenceValue = extraSpinners[i].target;
                    sp.GetArrayElementAtIndex(i).FindPropertyRelative("degreesPerSecond").floatValue = extraSpinners[i].degreesPerSecond;
                }
                var rk = so.FindProperty("rockers");
                rk.arraySize = rockers.Count;
                for (int i = 0; i < rockers.Count; i++)
                {
                    var e = rk.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("target").objectReferenceValue = rockers[i].target;
                    e.FindPropertyRelative("degrees").vector3Value = rockers[i].degrees;
                    e.FindPropertyRelative("heave").floatValue = rockers[i].heave;
                    e.FindPropertyRelative("period").floatValue = rockers[i].period;
                    e.FindPropertyRelative("phase").floatValue = rockers[i].phase;
                }
                Array(so.FindProperty("steam"), steam.ToArray());
                Array(so.FindProperty("horns"), horns.ToArray());
                so.FindProperty("hornClip").objectReferenceValue = clips.ShipHorn;
            });
            report.AppendLine($"activity: {movers.Count} movers (3 terminal tractors, {Mathf.Min(2, lifeBoats.Count)} work boats), {cranes.Count} working crane cycles, {searchlights.Count} searchlights, {rockers.Count} vessels rolling, {steam.Count} steam vents, {horns.Count} ship horns");
            return life;
        }

        // ---------------- Soundscape ----------------

        private static AudioSource Spatial(Transform parent, string name, AudioClip clip, bool loop, UnityEngine.Audio.AudioMixerGroup group, float volume, float min, float max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = loop && clip != null;
            s.volume = volume;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = min;
            s.maxDistance = max;
            s.dopplerLevel = 0f;
            s.outputAudioMixerGroup = group;
            return s;
        }

        private static int BuildSoundscape(Transform world, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup amb)
        {
            var root = new GameObject("Soundscape").transform;
            root.SetParent(world, false);
            int n = 0;
            void At(Vector3 p, AudioClip clip, float volume, float min, float max)
            {
                var s = Spatial(root, clip.name, clip, true, amb, volume, min, max);
                s.transform.position = p;
                s.time = 0f;
                n++;
            }
            // Rain drumming on metal roofs and container stacks.
            foreach (var p in new[] { new Vector3(-250f, 6f, -248f), new Vector3(-455f, 6f, -87f), new Vector3(-75f, 6f, -245f), new Vector3(550f, 6f, -125f), new Vector3(46f, 4f, -24f),
                                      new Vector3(-300f, 8f, 120f), new Vector3(200f, 8f, 20f), new Vector3(-100f, 8f, 170f), new Vector3(400f, 8f, 150f), new Vector3(1030f, 6f, 200f), new Vector3(1340f, 6f, 200f) })
                At(p, clips.RainMetal, 0.75f, 8f, 55f);
            // Water slapping the quay wall and the seawall.
            for (float x = -660f; x <= 1700f; x += 160f) At(new Vector3(x, 0f, QuayEdge + 2f), clips.WaterLap, 0.85f, 10f, 75f);
            // Cranes and conveyors, generators.
            foreach (var p in new[] { new Vector3(-440f, 4f, 270f), new Vector3(-260f, 4f, 270f), new Vector3(60f, 4f, 270f), new Vector3(-300f, 4f, 150f), new Vector3(540f, 4f, 10f) })
                At(p, clips.Machinery, 0.6f, 8f, 70f);
            foreach (var p in new[] { new Vector3(-560f, 2f, -150f), new Vector3(1320f, 2f, 190f), new Vector3(225f, 2f, 100f) })
                At(p, clips.Generator, 0.55f, 5f, 45f);
            report.AppendLine($"soundscape: {n} spatial loops (rain on metal, water lap, machinery, generators)");
            return n;
        }

        // ---------------- Command centre and camera ----------------

        private static (HarborRadio radio, HarborCinematics cinematics) BuildCommand(GameObject rig, HarborRainHud hud, HarborRainAudio.Clips clips,
                                                                                   UnityEngine.Audio.AudioMixerGroup sfx)
        {
            var go = new GameObject("CommandCentre");
            go.transform.SetParent(rig.transform, false);
            var voice = Source(go, "RadioVoice", null, false, sfx, 1f);
            voice.playOnAwake = false;
            var fx = Source(go, "RadioFx", null, false, sfx, 0.6f);
            var radio = go.AddComponent<HarborRadio>();
            Configure(radio, so =>
            {
                var lines = so.FindProperty("lines");
                lines.arraySize = HarborRainAudio.Lines.Length;
                for (int i = 0; i < HarborRainAudio.Lines.Length; i++)
                {
                    var l = HarborRainAudio.Lines[i];
                    var e = lines.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("id").stringValue = l.Id;
                    e.FindPropertyRelative("speaker").stringValue = l.Speaker;
                    e.FindPropertyRelative("text").stringValue = l.Text;
                    e.FindPropertyRelative("priority").intValue = (int)l.Priority;
                    e.FindPropertyRelative("cooldown").floatValue = l.Cooldown;
                    e.FindPropertyRelative("clip").objectReferenceValue = clips.Voice.TryGetValue(l.Id, out var c) ? c : null;
                }
                so.FindProperty("voice").objectReferenceValue = voice;
                so.FindProperty("fx").objectReferenceValue = fx;
                so.FindProperty("squelchIn").objectReferenceValue = clips.SquelchIn;
                so.FindProperty("squelchOut").objectReferenceValue = clips.SquelchOut;
                so.FindProperty("staticBurst").objectReferenceValue = clips.Static;
                so.FindProperty("hud").objectReferenceValue = hud;
            });

            var camGo = new GameObject("ShotCamera");
            camGo.transform.SetParent(rig.transform, false);
            var cm = camGo.AddComponent<CinemachineCamera>();
            cm.Lens.NearClipPlane = 0.2f;
            cm.Lens.FarClipPlane = 3200f;
            cm.Lens.FieldOfView = 50f;
            var cine = go.AddComponent<HarborCinematics>();
            Configure(cine, so =>
            {
                so.FindProperty("shotCamera").objectReferenceValue = cm;
                var shots = so.FindProperty("shots");
                shots.arraySize = 1;
                var shot = shots.GetArrayElementAtIndex(0);
                shot.FindPropertyRelative("name").stringValue = "arrival";
                shot.FindPropertyRelative("seconds").floatValue = 11f;
                var keys = shot.FindPropertyRelative("keys");
                var poses = new[]
                {
                    (new Vector3(-430f, 34f, 410f), new Vector3(-330f, 22f, 300f)),     // low over the water: the lit container ship and cranes
                    (new Vector3(-150f, 80f, 150f), new Vector3(-300f, 25f, 300f)),     // up across the terminal stacks
                    (new Vector3(60f, 60f, -230f), new Vector3(0f, 6f, -330f)),         // down to the checkpoint
                    (new Vector3(6f, 4.5f, -486f), new Vector3(0f, 1.2f, -468f)),       // the car
                };
                keys.arraySize = poses.Length;
                for (int i = 0; i < poses.Length; i++)
                {
                    keys.GetArrayElementAtIndex(i).FindPropertyRelative("position").vector3Value = poses[i].Item1;
                    keys.GetArrayElementAtIndex(i).FindPropertyRelative("lookAt").vector3Value = poses[i].Item2;
                }
            });
            return (radio, cine);
        }
    }
}
