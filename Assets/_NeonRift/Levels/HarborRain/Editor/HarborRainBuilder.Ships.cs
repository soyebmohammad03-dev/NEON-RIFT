using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Vessel and harbour lighting (Harbor Rain 2.0), placed from each imported model's own geometry (its bounds and its
    /// highest structure) in the vessel's frame, bow toward local +x for all three ships:
    /// the container ship at the berth shows working lights (deck floods along the stacks, a lit accommodation block,
    /// white lights fore and aft, red obstruction lights) — a moored ship does not show sidelights; the Liberty ship at
    /// anchor shows its two all-round white anchor lights (forward higher than aft) and deck lights; the cruise ship
    /// under way in the outer roads shows masthead lights, red port and green starboard sidelights and a stern light;
    /// the work boats under way show masthead, sidelights and stern light, a deck work light and a wake. In the harbour:
    /// lit lateral channel markers (red to port, green to starboard on the way in), a flashing buoy, the extraction
    /// boat's deck lights, and the lights of vessels far out on the horizon. Emission stays moderate so nothing blooms
    /// into a blob through the rain; real lights are only a handful, for reflections on the water.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private static Material deckLamp, portholes;

        /// <summary>Bounds of a vessel in its holder's frame, and the extent of its highest structure (top 18 % of height).</summary>
        private static (Bounds hull, Bounds top) VesselFrame(Transform holder) => VesselFrame(holder, holder.name == "LightFrame" ? holder.parent : holder);

        private static (Bounds hull, Bounds top) VesselFrame(Transform holder, Transform meshes)
        {
            var hull = new Bounds();
            var top = new Bounds();
            bool any = false;
            var pts = new List<Vector3>();
            foreach (var mf in meshes.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.TryGetComponent(out MeshRenderer mr) || !mr.enabled) continue;
                var v = mf.sharedMesh.vertices;
                int step = Mathf.Max(1, v.Length / 4000);
                for (int i = 0; i < v.Length; i += step)
                {
                    var p = holder.InverseTransformPoint(mf.transform.TransformPoint(v[i]));
                    pts.Add(p);
                    if (!any) { hull = new Bounds(p, Vector3.zero); any = true; } else hull.Encapsulate(p);
                }
            }
            float cut = hull.max.y - hull.size.y * 0.18f;
            bool t = false;
            foreach (var p in pts)
                if (p.y >= cut) { if (!t) { top = new Bounds(p, Vector3.zero); t = true; } else top.Encapsulate(p); }
            return (hull, top);
        }

        private static readonly Dictionary<string, Material> beaconMats = new();
        private static Mesh beaconQuad;

        /// <summary>A HarborBeacon glow material (camera-facing, minimum on-screen size, pierces the fog).</summary>
        private static Material BeaconMat(string name, Color hdr, float size, float minAngle = 0.0055f)
        {
            string key = $"{name}_{size:0.00}";
            if (beaconMats.TryGetValue(key, out var m)) return m;
            m = Asset("HR_Beacon_" + key.Replace('.', '_'), Shader.Find("NeonRift/HarborBeacon"));
            m.SetColor("_Color", hdr);
            m.SetFloat("_Size", size);
            m.SetFloat("_MinAngle", minAngle);
            m.SetFloat("_FogPierce", 0.12f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            beaconMats[key] = m;
            return m;
        }

        private static Material BeaconFor(Material lit, float size)
        {
            if (lit == navRed) return BeaconMat("Red", new Color(5f, 0.3f, 0.2f), size);
            if (lit == navGreen) return BeaconMat("Green", new Color(0.3f, 5f, 0.9f), size);
            if (lit == aviation) return BeaconMat("Aviation", new Color(4.5f, 0.25f, 0.15f), size);
            if (lit == deckLamp) return BeaconMat("Deck", new Color(3.2f, 2.5f, 1.6f), size, 0.0038f);
            return BeaconMat("White", new Color(4f, 3.9f, 3.6f), size);
        }

        private static Renderer Beacon(Transform parent, string name, Vector3 world, Material mat)
        {
            if (beaconQuad == null)
            {
                var q = new NeonRift.EditorTools.District.MeshBuilder();
                q.Quad(new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                beaconQuad = SaveMesh(q.ToMesh("HR_BeaconQuad", false), "HR_BeaconQuad");
                beaconQuad.bounds = new Bounds(Vector3.zero, Vector3.one * 60f);   // billboard grows with distance: keep it from being culled
            }
            var go = Single(name, parent, beaconQuad, mat, LayerMask.NameToLayer("Default"), world);
            return go.GetComponent<Renderer>();
        }

        private static Renderer Lamp(Transform holder, string name, Vector3 local, float size, Material m)
        {
            if (m == portholes)
            {
                // Window bands stay lit boxes on the superstructure (they are big enough to read at range).
                var box = Single(name, holder, CubeMesh("HR_NavLamp_" + size.ToString("0.0"), Vector3.one * size), m, LayerMask.NameToLayer("Default"), holder.TransformPoint(local));
                box.transform.rotation = holder.rotation;
                return box.GetComponent<Renderer>();
            }
            return Beacon(holder, name, holder.TransformPoint(local), BeaconFor(m, size * 0.6f));
        }

        private static Light PointAt(Transform holder, string name, Vector3 local, Color c, float range, float intensity)
        {
            var go = new GameObject(name) { layer = detail };
            go.transform.SetParent(holder, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>Navigation lights for a vessel under way: masthead(s), sidelights at the bridge, sternlight.</summary>
        private static void UnderwayLights(Transform holder, Bounds hull, Bounds top, float scale, bool twoMastheads)
        {
            float deck = hull.min.y + hull.size.y * 0.45f;
            Lamp(holder, "MastheadFwd", new Vector3(hull.max.x - hull.size.x * 0.3f, top.max.y + 1.2f * scale, hull.center.z), 0.9f * scale, navWhite);
            if (twoMastheads) Lamp(holder, "MastheadAft", new Vector3(top.center.x - hull.size.x * 0.08f, top.max.y + 4f * scale, hull.center.z), 0.9f * scale, navWhite);
            float wingX = Mathf.Clamp(top.max.x, hull.min.x, hull.max.x);
            Lamp(holder, "SidelightPort", new Vector3(wingX, top.min.y + 1f * scale, hull.max.z + 0.2f * scale), 0.8f * scale, navRed);
            Lamp(holder, "SidelightStarboard", new Vector3(wingX, top.min.y + 1f * scale, hull.min.z - 0.2f * scale), 0.8f * scale, navGreen);
            Lamp(holder, "Sternlight", new Vector3(hull.min.x - 0.2f * scale, deck, hull.center.z), 0.7f * scale, navWhite);
        }

        private static void BuildVesselLights(Transform root, Random rng)
        {
            beaconMats.Clear();
            beaconQuad = null;
            deckLamp = Lit("HR_DeckLamp", Color.black, 0.5f, 0f, new Color(2.6f, 2.1f, 1.4f));
            portholes = Lit("HR_Portholes", Color.black, 0.5f, 0f, new Color(1.6f, 1.25f, 0.8f));
            int lamps = 0, lights = 0;

            // Container ship at the berth (bow east, starboard side to the quay).
            var ship = GameObject.Find("ContainerShip_KowloonStar");
            if (ship != null)
            {
                var (hull, top) = VesselFrame(ship.transform);
                var h = ship.transform;
                // Accommodation block: four lit window rows on its faces.
                for (int row = 0; row < 4; row++)
                {
                    float y = top.min.y - 2f - row * 3.1f;
                    if (y < hull.min.y + hull.size.y * 0.4f) break;
                    foreach (float z in new[] { top.min.z - 0.15f, top.max.z + 0.15f })
                        Lamp(h, "WindowRow", new Vector3(top.center.x, y, z), 1f, portholes).transform.localScale = new Vector3(top.size.x * 0.8f, 0.7f, 0.1f);
                    foreach (float x in new[] { top.min.x - 0.15f, top.max.x + 0.15f })
                        Lamp(h, "WindowRow", new Vector3(x, y, top.center.z), 1f, portholes).transform.localScale = new Vector3(0.1f, 0.7f, top.size.z * 0.8f);
                    lamps += 4;
                }
                Lamp(h, "LightFwd", new Vector3(hull.max.x - 4f, hull.max.y - hull.size.y * 0.35f, hull.center.z), 0.9f, navWhite);
                Lamp(h, "LightAft", new Vector3(hull.min.x + 3f, hull.max.y - hull.size.y * 0.4f, hull.center.z), 0.9f, navWhite);
                Lamp(h, "ObstructionTop", new Vector3(top.center.x, top.max.y + 0.6f, top.center.z), 0.8f, aviation);
                // Deck floods along the stacks on both sides (emissive heads), aimed at the cargo.
                for (float x = hull.min.x + 18f; x < hull.max.x - 12f; x += 26f)
                    foreach (float z in new[] { hull.min.z + 0.6f, hull.max.z - 0.6f })
                    {
                        Lamp(h, "DeckFlood", new Vector3(x, hull.max.y - hull.size.y * 0.45f, z), 0.7f, deckLamp);
                        lamps++;
                    }
                // Light spilling onto the water off the seaward side and under the quay side (reflections).
                // Moored alongside: only a slow, small surge and roll against the fenders (no drift).
                extraRockers.Add(new HarborLife.Rocker { target = h.GetChild(0), degrees = new Vector3(0.25f, 0f, 0.12f), heave = 0.12f, period = 14f });
                PointAt(h, "HullSpillSea", new Vector3(hull.center.x + 40f, hull.min.y + hull.size.y * 0.3f, hull.max.z + 6f), new Color(1f, 0.78f, 0.5f), 45f, 180f);
                PointAt(h, "HullSpillSeaAft", new Vector3(hull.center.x - 70f, hull.min.y + hull.size.y * 0.3f, hull.max.z + 6f), new Color(1f, 0.78f, 0.5f), 45f, 180f);
                lights += 2;
            }

            // Liberty ship at anchor: anchor lights (forward higher than aft), deck lights, portholes.
            var lib = GameObject.Find("LibertyShip_AtAnchor");
            if (lib != null)
            {
                var (hull, top) = VesselFrame(lib.transform);
                var h = lib.transform;
                Lamp(h, "AnchorLightFwd", new Vector3(hull.max.x - hull.size.x * 0.12f, hull.max.y - 1f, hull.center.z), 1.2f, navWhite);
                Lamp(h, "AnchorLightAft", new Vector3(hull.min.x + hull.size.x * 0.06f, hull.max.y - hull.size.y * 0.45f, hull.center.z), 1.2f, navWhite);
                foreach (float z in new[] { hull.min.z - 0.1f, hull.max.z + 0.1f })
                    Lamp(h, "Portholes", new Vector3(top.center.x, hull.min.y + hull.size.y * 0.42f, z), 1f, portholes).transform.localScale = new Vector3(hull.size.x * 0.22f, 0.5f, 0.1f);
                for (float x = hull.min.x + 10f; x < hull.max.x - 8f; x += 18f) { Lamp(h, "DeckLamp", new Vector3(x, hull.min.y + hull.size.y * 0.52f, hull.center.z), 0.6f, deckLamp); lamps++; }
                PointAt(h, "DeckGlow", new Vector3(top.center.x, hull.min.y + hull.size.y * 0.55f, hull.max.z + 4f), new Color(1f, 0.82f, 0.6f), 40f, 140f);
                lamps += 4; lights++;
            }

            // Cruise ship under way slowly in the outer roads: full navigation lights.
            var cruise = GameObject.Find("CruiseShip_OuterRoads");
            if (cruise != null)
            {
                var (hull, top) = VesselFrame(cruise.transform);
                UnderwayLights(cruise.transform, hull, top, 2f, true);
                PointAt(cruise.transform, "PromenadeGlow", new Vector3(hull.center.x, hull.min.y + hull.size.y * 0.4f, hull.min.z - 8f), new Color(1f, 0.85f, 0.65f), 70f, 260f);
                lamps += 5; lights++;
            }

            // Work boats (under way in the basin) and the extraction boat (moored at the pier end).
            int b = 0;
            foreach (var boat in lifeBoats)
            {
                if (boat == null) continue;
                // An unscaled frame on the boat whose +x is the bow (the boat's +z), for the lamps and the wake.
                var model = new GameObject("LightFrame").transform;
                model.SetParent(boat.transform, false);
                model.localRotation = Quaternion.Euler(0f, -90f, 0f);
                var (hull, top) = VesselFrame(model);
                bool moored = b == 2;
                if (!moored)
                {
                    UnderwayLights(model, hull, top, 0.6f, false);
                    // Wake: foam left behind while the boat moves.
                    var wake = SprayParticles("Wake", boat.transform, 400, 0f, new Vector2(1.2f, 3.5f), 0.22f);
                    wake.transform.localPosition = boat.transform.InverseTransformPoint(model.TransformPoint(new Vector3(hull.min.x, 0f, hull.center.z)));
                    wake.transform.localPosition = new Vector3(wake.transform.localPosition.x, SeaLevel + 0.15f - boat.transform.position.y, wake.transform.localPosition.z);
                    wake.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    var main = wake.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                    var e = wake.emission; e.rateOverTime = 0f; e.rateOverDistance = 1.6f;
                    lamps += 4;
                }
                else
                {
                    Lamp(model, "ReadyBeacon", new Vector3(top.center.x, top.max.y + 0.4f, top.center.z), 0.5f, navGreen);
                    Lamp(model, "DeckLampFwd", new Vector3(hull.max.x - 2f, hull.min.y + hull.size.y * 0.5f, hull.center.z), 0.4f, deckLamp);
                    Lamp(model, "DeckLampAft", new Vector3(hull.min.x + 2f, hull.min.y + hull.size.y * 0.5f, hull.center.z), 0.4f, deckLamp);
                    lamps += 3;
                }
                var wl = new GameObject("DeckWorkLight") { layer = detail };
                wl.transform.SetParent(model, false);
                wl.transform.localPosition = new Vector3(top.center.x, top.min.y, top.center.z);
                wl.transform.localRotation = Quaternion.Euler(60f, -90f, 0f);
                var l = wl.AddComponent<Light>();
                l.type = LightType.Spot; l.color = new Color(0.95f, 0.92f, 0.85f); l.range = 28f; l.spotAngle = 90f; l.intensity = moored ? 900f : 600f; l.shadows = LightShadows.None;
                lights++;
                b++;
            }

            // Channel markers off the pier end (red to port, green to starboard heading in), each flashing, and a buoy.
            var marker = AssetDatabase.LoadAssetAtPath<GameObject>($"{Imported}/PolyHaven/Models/lateral_sea_marker/lateral_sea_marker_1k.gltf");
            var buoy = AssetDatabase.LoadAssetAtPath<GameObject>($"{Imported}/PolyHaven/Models/ocean_buoy/ocean_buoy_1k.gltf");
            var markers = new GameObject("ChannelMarkers").transform;
            markers.SetParent(root, false);
            int m = 0;
            foreach (var (p, port) in new[] { (new Vector3(2360f, 0f, 520f), true), (new Vector3(2360f, 0f, 600f), false), (new Vector3(2480f, 0f, 500f), true), (new Vector3(2480f, 0f, 620f), false),
                                              (new Vector3(1700f, 0f, 420f), true), (new Vector3(1760f, 0f, 520f), false) })
            {
                if (marker == null) break;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(marker, markers);
                go.name = port ? "PortMarker" : "StarboardMarker";
                go.transform.SetPositionAndRotation(new Vector3(p.x, SeaLevel - 1.2f, p.z), Quaternion.Euler(0f, rng.Next(360), 0f));
                go.transform.localScale = Vector3.one * 3.2f;
                var tint = port ? new Color(0.7f, 0.08f, 0.06f) : new Color(0.08f, 0.45f, 0.18f);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block);
                    block.SetColor("baseColorFactor", tint);
                    r.SetPropertyBlock(block);
                }
                var bnd = Bounds(go);
                var lampR = Beacon(go.transform, "MarkerLight", new Vector3(p.x, bnd.max.y + 0.3f, p.z), BeaconFor(port ? navRed : navGreen, 0.5f));
                var bl = lampR.gameObject.AddComponent<HarborBlinker>();
                var c = port ? new Color(5f, 0.25f, 0.15f) : new Color(0.25f, 5f, 0.8f);
                float rate = 0.33f + m * 0.04f;
                Configure(bl, so => { so.FindProperty("a").objectReferenceValue = lampR; so.FindProperty("colourA").colorValue = c; so.FindProperty("colourB").colorValue = c; so.FindProperty("rate").floatValue = rate; });
                extraRockers.Add(new HarborLife.Rocker { target = go.transform, degrees = new Vector3(5f, 0f, 4f), heave = 0.4f, period = 5f + m * 0.6f, phase = m * 0.23f });
                m++;
            }
            if (buoy != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(buoy, markers);
                go.transform.SetPositionAndRotation(new Vector3(2050f, SeaLevel - 0.5f, 660f), Quaternion.identity);
                go.transform.localScale = Vector3.one * 2.5f;
                extraRockers.Add(new HarborLife.Rocker { target = go.transform, degrees = new Vector3(7f, 0f, 6f), heave = 0.5f, period = 4.4f });
                var bnd = Bounds(go);
                var lampR = Beacon(go.transform, "BuoyLight", new Vector3(2050f, bnd.max.y + 0.3f, 660f), BeaconMat("Amber", new Color(4f, 3f, 0.3f), 0.4f));
                var bl = lampR.gameObject.AddComponent<HarborBlinker>();
                Configure(bl, so => { so.FindProperty("a").objectReferenceValue = lampR; so.FindProperty("colourA").colorValue = new Color(4f, 3f, 0.3f); so.FindProperty("colourB").colorValue = new Color(4f, 3f, 0.3f); so.FindProperty("rate").floatValue = 0.5f; });
            }
            foreach (var t in markers.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(t);

            // Vessels far out on the horizon: masthead, side and deck lights only (they read as ships at night).
            Begin();
            int far = 0;
            foreach (var (x, z, len, heading) in new[] { (900f, 2600f, 220f, 1), (2600f, 2300f, 160f, -1), (-600f, 3000f, 300f, 1), (3200f, 1600f, 180f, -1), (1800f, 3600f, 260f, 1), (-1800f, 2400f, 200f, -1), (4000f, 2800f, 240f, 1), (300f, 4200f, 280f, -1) })
            {
                float y = SeaLevel + 1f;
                B(hullBlack).Cuboid(new Vector3(x - len * 0.5f, y - 6f, z - 14f), new Vector3(x + len * 0.5f, y + 9f, z + 14f), 8f);
                var hz = new GameObject("HorizonVesselLights").transform;
                hz.SetParent(root, false);
                Beacon(hz, "Masthead", new Vector3(x + heading * len * 0.3f, y + 33f, z), BeaconFor(navWhite, 1.4f));
                Beacon(hz, "MastheadAft", new Vector3(x - heading * len * 0.2f, y + 41f, z), BeaconFor(navWhite, 1.4f));
                Beacon(hz, "Sidelight", new Vector3(x, y + 21f, z - 15f), BeaconFor(heading > 0 ? navGreen : navRed, 1.2f));
                for (float dx = -len * 0.4f; dx < len * 0.4f; dx += 22f) Beacon(hz, "Deck", new Vector3(x + dx, y + 11.5f, z - 14.2f), BeaconFor(deckLamp, 0.9f));
                far++;
            }
            Flush("HorizonVessels", root, LayerMask.NameToLayer("Default"), false);
            report.AppendLine($"vessel lighting: {lamps} lamps (nav, deck, windows), {lights} real lights; {m} lit channel markers + buoy; {far} vessels on the horizon");
        }
    }
}
