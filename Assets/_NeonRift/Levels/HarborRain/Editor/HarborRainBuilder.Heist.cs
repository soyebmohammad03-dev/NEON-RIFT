using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// The heist set in the restricted bay (Harbor Rain 2.0): a rail-mounted gantry crane spanning the bay (legs on rails
    /// at x 160 and 290, so it straddles the stacks and all three candidate bays), its trolley and spreader with real
    /// hoist ropes, twistlocks, corner flippers and status lamps; the theft alarm (rotating beacons round the fence, the
    /// bell, a few red lights); and the crew's transfer truck waiting on a cleared pad, which takes KX-7 out of the north
    /// gate after the theft.
    /// </summary>
    public static partial class HarborRainBuilder
    {
        private const float RailX0 = 160f, RailX1 = 290f, RailZ0 = 104f, RailZ1 = 212f, GirderY = 24f;
        private static readonly Vector3 TransferPad = new(196f, 1.45f, 160f);
        private static readonly Rect TransferArea = new(186f, 145f, 27f, 41f);

        private static HarborBayCrane bayCrane;
        private static readonly List<Light> bayCraneFloods = new();
        private static HarborHeistAlarm heistAlarm;
        private static HarborMuleTruck muleTruck;
        private static readonly List<HarborSearchlight> searchlightHeads = new();

        /// <summary>Solids reserved before the restricted yards are stacked: crane rails and the transfer pad.</summary>
        private static void ReserveHeistSpace()
        {
            solids.Add(new Rect(RailX0 - 2f, RailZ0 - 2f, 4f, RailZ1 - RailZ0 + 4f));
            solids.Add(new Rect(RailX1 - 2f, RailZ0 - 2f, 4f, RailZ1 - RailZ0 + 4f));
            solids.Add(TransferArea);
        }

        private static void BuildBayCrane(Transform world, Transform[] boxes, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup sfx)
        {
            var root = new GameObject("BayGantryCrane_RMG07").transform;
            root.SetParent(world, false);
            var blue = Lit("HR_RmgBlue", new Color(0.06f, 0.16f, 0.34f), 0.45f, 0.5f, null);
            var white = Lit("HR_RmgWhite", new Color(0.62f, 0.64f, 0.66f), 0.45f, 0.4f, null);
            var lampMat = Lit("HR_CraneLamp", new Color(0.1f, 0.06f, 0f), 0.6f, 0f, new Color(0.1f, 0.04f, 0f));

            // Rails on concrete beams (static).
            Begin();
            foreach (float x in new[] { RailX0, RailX1 })
            {
                B(concreteDark).Cuboid(new Vector3(x - 0.6f, 0f, RailZ0), new Vector3(x + 0.6f, 0.12f, RailZ1), 2f);
                B(rail).Cuboid(new Vector3(x - 0.08f, 0.12f, RailZ0), new Vector3(x + 0.08f, 0.26f, RailZ1), 1f);
                foreach (float z in new[] { RailZ0, RailZ1 }) B(hazardStripe).Cuboid(new Vector3(x - 0.7f, 0f, z - 0.6f), new Vector3(x + 0.7f, 0.8f, z + 0.6f), 1f);   // end stops
            }
            Flush("CraneRails", root, environment, false);

            // Gantry: legs, sills, bogies, girders, cab — one moving object.
            float cx = (RailX0 + RailX1) * 0.5f, half = (RailX1 - RailX0) * 0.5f;
            var gantry = new GameObject("Gantry") { layer = environment }.transform;
            gantry.SetParent(root, false);
            gantry.position = new Vector3(cx, 0f, 160f);
            Begin();
            var beaconRenderers = new List<Renderer>();
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * half;
                B(white).Cuboid(new Vector3(x - 0.8f, 0.45f, -11.5f), new Vector3(x + 0.8f, 1.7f, 11.5f), 3f);     // sill beam
                foreach (float z in new[] { -10.4f, 10.4f })
                {
                    B(rubber).Cuboid(new Vector3(x - 0.5f, 0f, z - 1f), new Vector3(x + 0.5f, 0.9f, z + 1f), 1f);    // bogie
                    B(hazardStripe).Cuboid(new Vector3(x - 0.85f, 0.45f, z - 1.1f), new Vector3(x + 0.85f, 1.7f, z - 0.9f), 1f);
                }
                foreach (float z in new[] { -9f, 9f })
                    B(blue).Cuboid(new Vector3(x - 0.7f, 1.7f, z - 0.7f), new Vector3(x + 0.7f, GirderY, z + 0.7f), 3f);
                // Knee braces and the top tie between each leg pair.
                foreach (float z in new[] { -9f, 9f })
                {
                    var a = new Vector3(x, 4f, z); var b = new Vector3(x, 14f, 0f);
                    B(blue).OrientedBox((a + b) * 0.5f, new Vector3(0.5f, 0.5f, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a), 2f);
                }
                B(blue).Cuboid(new Vector3(x - 0.9f, GirderY - 1.2f, -9.7f), new Vector3(x + 0.9f, GirderY, 9.7f), 3f);
                B(white).Cuboid(new Vector3(x - 1.2f, GirderY, -5.6f), new Vector3(x + 1.2f, GirderY + 2.6f, 5.6f), 3f);   // girder end carriage
            }
            foreach (float z in new[] { -3.4f, 3.4f })
            {
                B(white).Cuboid(new Vector3(-half - 3f, GirderY, z - 0.9f), new Vector3(half + 3f, GirderY + 2.4f, z + 0.9f), 6f);
                B(metal).Cuboid(new Vector3(-half - 3f, GirderY + 2.4f, z - 0.06f), new Vector3(half + 3f, GirderY + 3.4f, z + 0.06f), 2f);   // handrail
                B(hazardStripe).Cuboid(new Vector3(-half - 3f, GirderY - 0.02f, z - 0.9f), new Vector3(-half + 4f, GirderY + 0.2f, z + 0.9f), 1f);
            }
            // Operator cab hung under the girders near the east leg.
            B(white).Cuboid(new Vector3(half - 16f, GirderY - 4.2f, -2f), new Vector3(half - 12f, GirderY - 0.6f, 2f), 2f);
            B(indWindow).Cuboid(new Vector3(half - 16.02f, GirderY - 3.4f, -1.8f), new Vector3(half - 15.98f, GirderY - 1.4f, 1.8f), 1f);
            B(indWindow).Cuboid(new Vector3(half - 15.8f, GirderY - 3.4f, -2.02f), new Vector3(half - 12.2f, GirderY - 1.4f, -1.98f), 1f);
            Flush("GantryStructure", gantry, environment, true);
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { -9f, 9f })
                    beaconRenderers.Add(Single("GantryBeacon", gantry, CubeMesh("HR_GantryBeacon", new Vector3(0.45f, 0.4f, 0.45f)), lampMat, detail, gantry.position + new Vector3(s * half, GirderY + 2.9f, z)).GetComponent<Renderer>());
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { -9f, 9f })
                    Collider(gantry, "GantryLeg", gantry.position + new Vector3(s * half, GirderY * 0.5f, z), new Vector3(1.4f, GirderY, 1.4f), environment);
            foreach (float s in new[] { -1f, 1f })
                Collider(gantry, "GantrySill", gantry.position + new Vector3(s * half, 0.9f, 0f), new Vector3(1.8f, 1.8f, 23f), environment);
            SignPanel(gantry, "RMG 07  ·  KOWLOON BAY RESTRICTED", Color.white, gantry.position + new Vector3(0f, GirderY + 1.2f, -4.32f), 180f, 18f);
            var floods = new List<Light>();
            foreach (float x in new[] { -34f, -10f, 10f, 34f })
            {
                var go = new GameObject("GantryFlood") { layer = detail };
                go.transform.SetParent(gantry, false);
                go.transform.SetPositionAndRotation(gantry.position + new Vector3(x, GirderY - 0.3f, 0f), Quaternion.Euler(90f, 0f, 0f));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.color = new Color(0.86f, 0.92f, 1f); l.intensity = 3800f; l.range = 36f; l.spotAngle = 96f; l.innerSpotAngle = 50f;
                l.shadows = LightShadows.None; l.enabled = false;
                floods.Add(l);
            }

            // Trolley on the girders: machinery house, wheels, sheave beams, the target work light.
            var trolley = new GameObject("Trolley") { layer = environment }.transform;
            trolley.SetParent(root, false);
            trolley.position = new Vector3(225f, GirderY + 2.4f, 160f);
            Begin();
            B(blue).Cuboid(new Vector3(-3f, 0.3f, -4.4f), new Vector3(3f, 2.6f, 4.4f), 2f);
            B(white).Cuboid(new Vector3(-2f, 2.6f, -2.5f), new Vector3(2f, 3.6f, 2.5f), 2f);
            foreach (float x in new[] { -2.4f, 2.4f }) foreach (float z in new[] { -3.4f, 3.4f }) B(rubber).Cuboid(new Vector3(x - 0.4f, 0f, z - 0.3f), new Vector3(x + 0.4f, 0.5f, z + 0.3f), 1f);
            foreach (float x in new[] { -1.1f, 1.1f }) B(metal).Cuboid(new Vector3(x - 0.2f, -2.9f, -2.5f), new Vector3(x + 0.2f, 0.3f, 2.5f), 1f);   // sheave hangers
            Flush("TrolleyBody", trolley, environment, true);
            var tops = new List<Transform>();
            foreach (float x in new[] { -1.1f, 1.1f })
                foreach (float z in new[] { -2.2f, 2.2f })
                {
                    var t = new GameObject("RopeTop").transform;
                    t.SetParent(trolley, false);
                    t.localPosition = new Vector3(x, -2.8f, z);
                    tops.Add(t);
                }
            var tl = new GameObject("TargetLight") { layer = detail };
            tl.transform.SetParent(trolley, false);
            tl.transform.localPosition = new Vector3(0f, -3.1f, 3.6f);
            tl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var target = tl.AddComponent<Light>();
            target.type = LightType.Spot; target.color = new Color(0.92f, 0.96f, 1f); target.intensity = 9000f; target.range = 40f; target.spotAngle = 26f; target.innerSpotAngle = 12f;
            target.shadows = LightShadows.Soft; target.shadowStrength = 0.8f; target.enabled = false;

            // Spreader: frame, telescopic arms, headblock with sheaves, twistlocks, flippers, lamps, work light.
            var spreader = new GameObject("Spreader") { layer = detail }.transform;
            spreader.SetParent(root, false);
            spreader.position = new Vector3(225f, 14f, 160f);
            Begin();
            B(hazardStripe).Cuboid(new Vector3(-0.4f, 0.2f, -6.1f), new Vector3(0.4f, 0.75f, 6.1f), 1f);
            foreach (float z in new[] { -6.1f, 5.85f }) B(hazardStripe).Cuboid(new Vector3(-1.22f, 0f, z), new Vector3(1.22f, 0.6f, z + 0.25f), 1f);
            foreach (float x in new[] { -0.75f, 0.75f }) B(metal).Cuboid(new Vector3(x - 0.18f, 0.28f, -5.9f), new Vector3(x + 0.18f, 0.62f, 5.9f), 2f);
            B(blue).Cuboid(new Vector3(-1.4f, 0.75f, -2.6f), new Vector3(1.4f, 1.6f, 2.6f), 1f);
            B(metal).Cuboid(new Vector3(-0.6f, 1.6f, -1.2f), new Vector3(0.6f, 2.1f, 1.2f), 1f);   // hydraulics / power pack
            foreach (float x in new[] { -1.1f, 1.1f }) foreach (float z in new[] { -2.2f, 2.2f }) B(metal).Cylinder(new Vector3(x, 1.6f, z), 0.22f, 0.25f, 10, true);   // sheaves
            Flush("SpreaderFrame", spreader, detail, true);
            var bottoms = new List<Transform>();
            foreach (float x in new[] { -1.1f, 1.1f })
                foreach (float z in new[] { -2.2f, 2.2f })
                {
                    var b = new GameObject("RopeBottom").transform;
                    b.SetParent(spreader, false);
                    b.localPosition = new Vector3(x, 1.75f, z);
                    bottoms.Add(b);
                }
            var pinMesh = CubeMesh("HR_TwistlockPin", new Vector3(0.12f, 0.28f, 0.3f));
            var flipMesh = new NeonRift.EditorTools.District.MeshBuilder();
            flipMesh.Cuboid(new Vector3(-0.32f, -0.95f, -0.03f), new Vector3(0.32f, 0f, 0.03f), 1f);
            var flipper = SaveMesh(flipMesh.ToMesh("HR_Flipper"), "HR_Flipper");
            var lampMesh = CubeMesh("HR_CornerLamp", new Vector3(0.2f, 0.16f, 0.2f));
            var locks = new List<Transform>();
            var flippers = new List<Transform>();
            var corners = new List<Renderer>();
            foreach (float z in new[] { -5.94f, 5.94f })
                foreach (float x in new[] { -1.02f, 1.02f })
                {
                    var l = Single("Twistlock", spreader, pinMesh, craneBlue, detail, spreader.position);
                    l.transform.localPosition = new Vector3(x, -0.12f, z);
                    locks.Add(l.transform);
                    var f = Single("Flipper", spreader, flipper, hazardStripe, detail, spreader.position);
                    f.transform.localPosition = new Vector3(x * 1.12f, 0.3f, z + Mathf.Sign(z) * 0.32f);
                    flippers.Add(f.transform);
                    var c = Single("CornerLamp", spreader, lampMesh, lampMat, detail, spreader.position);
                    c.transform.localPosition = new Vector3(x * 1.05f, 0.7f, z - Mathf.Sign(z) * 0.5f);
                    corners.Add(c.GetComponent<Renderer>());
                }
            var status = Single("StatusLamp", spreader, CubeMesh("HR_StatusLamp", new Vector3(0.5f, 0.35f, 0.5f)), lampMat, detail, spreader.position).GetComponent<Renderer>();
            status.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            var wl = new GameObject("WorkLight") { layer = detail };
            wl.transform.SetParent(spreader, false);
            wl.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            wl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var work = wl.AddComponent<Light>();
            work.type = LightType.Spot; work.color = new Color(0.9f, 0.94f, 1f); work.intensity = 2200f; work.range = 22f; work.spotAngle = 115f; work.innerSpotAngle = 60f; work.shadows = LightShadows.None; work.enabled = false;
            floods.Add(work);

            // Hoist ropes: unit-height boxes pivoted at the bottom, stretched between the sheaves every frame.
            var ropeBuilder = new NeonRift.EditorTools.District.MeshBuilder();
            ropeBuilder.Cuboid(new Vector3(-0.035f, 0f, -0.035f), new Vector3(0.035f, 1f, 0.035f), 1f);
            var ropeMesh = SaveMesh(ropeBuilder.ToMesh("HR_RopeUnit"), "HR_RopeUnit");
            var ropes = new List<Transform>();
            for (int i = 0; i < 4; i++) ropes.Add(Single("HoistRope", root, ropeMesh, rail, detail, Vector3.zero).transform);

            var audioGo = new GameObject("CraneAudio");
            audioGo.transform.SetParent(trolley, false);
            var motor = audioGo.AddComponent<AudioSource>();
            motor.clip = clips.CraneMotor; motor.loop = true; motor.playOnAwake = false; motor.volume = 0f; motor.spatialBlend = 1f; motor.minDistance = 12f; motor.maxDistance = 220f; motor.outputAudioMixerGroup = sfx;
            var sfxSource = Spatial(spreader, "SpreaderSfx", null, false, sfx, 1f, 10f, 180f);
            var crane = root.gameObject.AddComponent<HarborBayCrane>();
            crane.EditorConfigure(gantry, trolley, spreader, ropes.ToArray(), tops.ToArray(), bottoms.ToArray(), locks.ToArray(), flippers.ToArray(), corners.ToArray(), status,
                                  beaconRenderers.ToArray(), floods.ToArray(), target, boxes, TransferPad, GirderY + 2.4f, motor, sfxSource, clips.TwistClack, clips.MetalImpact, clips.CableStrain, clips.Hydraulic);
            bayCrane = crane;
            bayCraneFloods.Clear();
            bayCraneFloods.AddRange(floods);
            report.AppendLine($"heist crane: RMG 07 spanning x {RailX0:0}–{RailX1:0} on {RailZ1 - RailZ0:0} m rails, trolley + spreader on 4 ropes, 4 twistlocks / flippers / corner lamps, transfer pad at {TransferPad.x:0},{TransferPad.z:0}");

            BuildMule(world, clips);
            BuildHeistAlarm(world, clips, sfx);
        }

        private static void BuildMule(Transform world, HarborRainAudio.Clips clips)
        {
            // The crew's transfer truck: the shared port truck (graphite livery, empty chassis) waiting under the gantry's
            // transfer pad — the container's underside lands on the deck at TransferPad.y and HarborMuleTruck parents it.
            var rig = PlaceTruck(world, "CrewTransferTruck", new Vector3(TransferPad.x, 0f, TransferPad.z), Quaternion.identity, 3, null, true);
            var root = rig.Root.transform;
            rig.Headlight.enabled = false;
            var lights = new List<Light> { rig.Headlight };
            var beacon = rig.Beacon;
            var dressing = root.gameObject.AddComponent<HarborVehicleDressing>();
            // No body pitch (the stolen box is parented to the root and must stay seated) and no beacon (the mule flashes it).
            dressing.EditorConfigure(rig.Spin.ToArray(), rig.Steer.ToArray(), TractorWheelR, rig.Brakes.ToArray(), null, null);
            var hum = Spatial(root, "Engine", clips.Machinery, true, null, 0.3f, 5f, 60f);
            hum.pitch = 1.3f;
            var mule = root.gameObject.AddComponent<HarborMuleTruck>();
            mule.EditorConfigure(new[]
            {
                new Vector3(196f, 0f, 178f), new Vector3(203f, 0f, 186f), new Vector3(214f, 0f, 191f), new Vector3(224f, 0f, 199f), new Vector3(225f, 0f, 214f),
                new Vector3(222f, 0f, 228f), new Vector3(205f, 0f, 231f), new Vector3(60f, 0f, 231f), new Vector3(-120f, 0f, 231f), new Vector3(-330f, 0f, 231f)
            }, lights.ToArray(), new[] { beacon });
            muleTruck = mule;
        }

        private static Material beaconMat() => beacon;

        private static void BuildHeistAlarm(Transform world, HarborRainAudio.Clips clips, UnityEngine.Audio.AudioMixerGroup sfx)
        {
            var root = new GameObject("TheftAlarm").transform;
            root.SetParent(world, false);
            var alarmLamp = Lit("HR_AlarmBeacon", new Color(0.2f, 0.02f, 0.01f), 0.7f, 0f, new Color(0.05f, 0f, 0f));
            float x0 = Restricted.xMin, x1 = Restricted.xMax, z0 = Restricted.yMin, z1 = Restricted.yMax;
            // Clockwise from the security gate (south side) round the compound.
            var posts = new[]
            {
                new Vector3(240f, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(x1, 0f, 156f), new Vector3(x1, 0f, z1), new Vector3(240f, 0f, z1),
                new Vector3(210f, 0f, z1), new Vector3(x0, 0f, z1), new Vector3(x0, 0f, 156f), new Vector3(x0, 0f, z0), new Vector3(210f, 0f, z0)
            };
            Begin();
            foreach (var p in posts) B(metal).Cylinder(p + new Vector3(0f, 0f, p.z < 156f ? -0.6f : 0.6f), 0.1f, 5.4f, 8, true);
            Flush("AlarmPosts", root, detail, false);
            var lamps = new List<Renderer>();
            var spins = new List<Transform>();
            var domeMesh = new NeonRift.EditorTools.District.MeshBuilder();
            domeMesh.Cylinder(Vector3.zero, 0.26f, 0.42f, 12, true);
            var dome = SaveMesh(domeMesh.ToMesh("HR_AlarmDome"), "HR_AlarmDome");
            foreach (var p in posts)
            {
                var at = p + new Vector3(0f, 5.4f, p.z < 156f ? -0.6f : 0.6f);
                lamps.Add(Single("AlarmBeacon", root, dome, alarmLamp, detail, at).GetComponent<Renderer>());
                var spin = Single("Reflector", root, CubeMesh("HR_AlarmReflector", new Vector3(0.36f, 0.24f, 0.04f)), metal, detail, at + Vector3.up * 0.2f).transform;
                spins.Add(spin);
            }
            var lights = new List<Light>();
            foreach (var p in new[] { new Vector3(225f, 7f, z0 + 4f), new Vector3(225f, 7f, z1 - 4f), new Vector3(225f, 12f, 156f) })
            {
                var go = new GameObject("AlarmRed") { layer = detail };
                go.transform.SetParent(root, false);
                go.transform.position = p;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.1f, 0.06f); l.range = 36f; l.intensity = 0f; l.shadows = LightShadows.None; l.enabled = false;
                lights.Add(l);
            }
            var bellGo = new GameObject("AlarmBell");
            bellGo.transform.SetParent(root, false);
            bellGo.transform.position = new Vector3(225f, 6f, z0 + 2f);
            var bell = bellGo.AddComponent<AudioSource>();
            bell.clip = clips.AlarmBell; bell.loop = true; bell.playOnAwake = false; bell.spatialBlend = 1f; bell.minDistance = 14f; bell.maxDistance = 320f; bell.volume = 0.55f; bell.outputAudioMixerGroup = sfx;
            var alarm = root.gameObject.AddComponent<HarborHeistAlarm>();
            alarm.EditorConfigure(lamps.ToArray(), spins.ToArray(), lights.ToArray(), bayCraneFloods.ToArray(), bell);
            heistAlarm = alarm;
        }

        /// <summary>
        /// Flush marks generated meshes batching-static, which freezes them in Play Mode (static batching ignores the
        /// transform). Everything that moves — both crane types, the bay boxes, the truck, gates, cameras, drones,
        /// searchlights, flood water, alarm reflectors and HarborLife's movers / rockers / spinners — is made dynamic here.
        /// </summary>
        private static int ClearMovingStatics()
        {
            var roots = new HashSet<Transform>();
            void AddRefs(Object c)
            {
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    var o = it.objectReferenceValue;
                    var t = o is Transform tr ? tr : o is GameObject go ? go.transform : o is Component comp && o is not Light && o is not AudioSource ? comp.transform : null;
                    if (t != null && t.gameObject.scene.IsValid()) roots.Add(t);
                }
            }
            foreach (var c in Object.FindObjectsByType<HarborLife>(FindObjectsSortMode.None)) AddRefs(c);
            foreach (var c in Object.FindObjectsByType<HarborCraneCycle>(FindObjectsSortMode.None)) { AddRefs(c); roots.Add(c.transform); }
            foreach (var c in Object.FindObjectsByType<HarborBayCrane>(FindObjectsSortMode.None)) { AddRefs(c); roots.Add(c.transform); }
            foreach (var c in Object.FindObjectsByType<HarborMuleTruck>(FindObjectsSortMode.None)) roots.Add(c.transform);
            foreach (var c in Object.FindObjectsByType<HarborGate>(FindObjectsSortMode.None)) AddRefs(c);
            foreach (var c in Object.FindObjectsByType<HarborSearchlight>(FindObjectsSortMode.None)) roots.Add(c.transform);
            foreach (var c in Object.FindObjectsByType<HarborSecurityCamera>(FindObjectsSortMode.None)) AddRefs(c);
            foreach (var c in Object.FindObjectsByType<HarborDrone>(FindObjectsSortMode.None)) roots.Add(c.transform);
            foreach (var c in Object.FindObjectsByType<FloodZone>(FindObjectsSortMode.None)) AddRefs(c);
            foreach (var c in Object.FindObjectsByType<HarborHeistAlarm>(FindObjectsSortMode.None)) AddRefs(c);
            foreach (var t in bayBoxes) if (t != null) roots.Add(t);
            int n = 0;
            foreach (var r in roots)
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) { GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0); n++; }
            report.AppendLine($"dynamic objects: static flags cleared on {n} objects under {roots.Count} moving roots");
            return n;
        }
    }
}