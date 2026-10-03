using System.Collections.Generic;
using NeonRift.Gameplay;
using NeonRift.Missions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// The Data Core heist: the interaction data (perimeter security terminal, extraction uplink) and the scene pieces
    /// that perform it — the terminal kiosk, the armoured sleeve, rail-mounted connector and scanner arms, data cable,
    /// pulses, scan rings, holograms, warning strobes and 3D audio, all driven by <see cref="DataCoreChamber"/>.
    /// </summary>
    public static partial class NightRunBuilder
    {
        public const string CoreTerminalId = "core_security_terminal";
        public const string EventCoreArrive = "core.arrive";
        public const string EventCoreSecurityOff = "core.security.off";
        public const string EventCoreAcquired = "core.acquired";
        public const string EventExtractBegin = "core.extract.begin";
        public const string EventGridAnomaly = "grid.anomaly";
        public const string EventExtractTrace = "core.extract.trace";

        /// <summary>Kiosk of the perimeter security terminal, west of the core beside the inner lane.</summary>
        public static readonly Vector3 CoreTerminalPosition = new(145f, 0f, 127.5f);

        private const float ArmUpper = 4f, ArmFore = 4f, RailRadius = 3f, RailHeight = 1.8f;

        private static InteractionDefinition CoreTerminalDefinition()
        {
            var d = Asset<InteractionDefinition>($"{DataFolder}/Interaction_CoreSecurity.asset");
            d.EditorConfigure("HACK", 0f, 8f, 0.5f, 0f, "TERMINAL ACCESS LOGGED", "LOCAL SECURITY OFFLINE");
            d.EditorConfigureStages("S7-CORE // PERIMETER SECURITY NODE", new List<InteractionStep>
            {
                new("CONNECTING", InteractionStepKind.Auto, 1.1f, "Handshake with the perimeter node"),
                new("AUTHENTICATING", InteractionStepKind.Hold, 1.8f, "Spoofing a maintenance credential — hold E"),
                new InteractionStep("BYPASSING", InteractionStepKind.Timing, 1.5f, "Slip the token past the intrusion filter")
                    .WithTiming(0.26f, 0.64f, 3, 0.04f),
                new("SECURITY OVERRIDE", InteractionStepKind.Auto, 1.0f, "Cameras, floods and chamber locks → crew control"),
            }, lockout: 4f, heatOnFail: 0.1f, onFailMessage: "INTRUSION FILTER LOCKOUT · TRACE LOGGED", onFail: null, tensionLevel: 0.25f);
            EditorUtility.SetDirty(d);
            return d;
        }

        private static void ConfigureUplinkDefinition(InteractionDefinition uplink)
        {
            uplink.EditorConfigure("EXTRACT", 0f, 6f, 0.35f, 0f, "CORE ACCESS LOGGED", "EXTRACTION COMPLETE");
            uplink.EditorConfigureStages("S7-CORE // EXTRACTION UPLINK", new List<InteractionStep>
            {
                new("CONNECT EXTRACTION DEVICE", InteractionStepKind.Auto, 2.2f, "Docking arm to the car — stay stopped", checkpoint: true,
                    eventOnStart: "core.extract.connect"),
                new("INITIALIZE EXTRACTION", InteractionStepKind.Auto, 1.6f, "Mapping the core's partition table", eventOnStart: "core.extract.init"),
                new InteractionStep("STABILIZE DATA LINK", InteractionStepKind.Timing, 1.3f, "Lock the carrier as the cursor crosses the window",
                        checkpoint: true, eventOnStart: "core.extract.stabilize")
                    .WithTiming(0.22f, 0.4f, 0, 0.03f),
                new InteractionStep("DATA EXTRACTION", InteractionStepKind.Sustain, 14f, "Pulling the S7 package — answer interference with E", checkpoint: true)
                    .WithInterference(new[] { 0.38f, 0.74f }, 2.6f, 0.12f)
                    .WithCues(new InteractionCue(0.03f, EventExtractBegin), new InteractionCue(0.5f, EventGridAnomaly), new InteractionCue(0.8f, EventExtractTrace)),
                new("EXTRACTION COMPLETE", InteractionStepKind.Auto, 0.9f, "Sealing the package"),
            }, lockout: 3f, heatOnFail: 0f, onFailMessage: "LINK COLLAPSED", onFail: null, tensionLevel: 1f);
            EditorUtility.SetDirty(uplink);
        }

        // ---------------- Terminal ----------------

        private static Interactable BuildCoreTerminal(Context c, Transform compound, InteractionDefinition definition, Transform core)
        {
            var kiosk = new GameObject("SecurityTerminal");
            kiosk.transform.SetParent(compound, false);
            kiosk.transform.SetPositionAndRotation(CoreTerminalPosition, Quaternion.LookRotation(Vector3.right));
            var body = new MeshBuilder();
            body.OrientedBox(new Vector3(0f, 0.15f, 0f), new Vector3(1.6f, 0.3f, 1.1f), Quaternion.identity, 1f);
            body.OrientedBox(new Vector3(0f, 1.1f, -0.1f), new Vector3(1.2f, 1.9f, 0.6f), Quaternion.identity, 1f);
            body.OrientedBox(new Vector3(0f, 2.15f, 0.05f), new Vector3(1.4f, 0.12f, 0.9f), Quaternion.identity, 1f);
            var bodyGo = DistrictKit.Renderer("Body", kiosk.transform, DistrictKit.SaveMesh(body, "Prop_CoreTerminal_Body"), c.Kit.Metal, c.Environment);
            var col = bodyGo.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.1f, -0.1f);
            col.size = new Vector3(1.4f, 2.2f, 0.9f);

            var screens = new List<Renderer>();
            var screenMesh = new MeshBuilder();
            screenMesh.Panel(Vector3.zero, Quaternion.identity, 1.0f, 0.7f, new Rect(0f, 0f, 1f, 1f));
            var mainScreen = Screen(c, "MainScreen", kiosk.transform, DistrictKit.SaveMesh(screenMesh, "Prop_TerminalScreen_Panel"),
                                    new Vector3(0f, 1.45f, 0.21f), Quaternion.Euler(-14f, 180f, 0f), opaque: true);
            screens.Add(mainScreen);
            // A tall status hologram above the kiosk, readable from the car.
            var holoMesh = new MeshBuilder();
            holoMesh.Panel(Vector3.zero, Quaternion.identity, 2.2f, 1.3f, new Rect(0f, 0f, 1f, 1f));
            var holo = Screen(c, "StatusHologram", kiosk.transform, DistrictKit.SaveMesh(holoMesh, "Prop_TerminalHolo_Panel"),
                              new Vector3(0f, 3.2f, 0.1f), Quaternion.Euler(0f, 180f, 0f), opaque: false);
            screens.Add(holo);

            var glow = Light(c, kiosk.transform, "ScreenGlow", LightType.Point, CoreTerminalPosition + new Vector3(1.2f, 1.6f, 0f), Quaternion.identity,
                             new Color(0.3f, 0.9f, 1f), 4f, 7f);
            var speaker = Speaker(c, kiosk.transform, "Speaker", 4f, 60f);

            // The pad where the car stops: a glowing outline on the plaza.
            var pad = new MeshBuilder();
            foreach (var (from, to) in new[]
                     {
                         (new Vector3(147f, 0.04f, 120.5f), new Vector3(147f, 0.04f, 134.5f)), (new Vector3(154f, 0.04f, 120.5f), new Vector3(154f, 0.04f, 134.5f)),
                         (new Vector3(147f, 0.04f, 120.5f), new Vector3(154f, 0.04f, 120.5f)), (new Vector3(147f, 0.04f, 134.5f), new Vector3(154f, 0.04f, 134.5f)),
                     })
                pad.Strip(from, to, 0.18f);
            var padGo = DistrictKit.Renderer("TerminalPad", c.Gameplay, DistrictKit.SaveMesh(pad, "Compound_TerminalPad"), c.Kit.Indicator, c.Environment, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(padGo, 0);

            var zoneGo = new GameObject("TerminalZone") { layer = c.Trigger };
            zoneGo.transform.SetParent(kiosk.transform, false);
            zoneGo.transform.position = new Vector3(150f, 2.5f, 127.5f);
            zoneGo.transform.rotation = Quaternion.identity;
            var box = zoneGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(9f, 5f, 16f);
            var interactable = zoneGo.AddComponent<Interactable>();
            interactable.EditorConfigure(CoreTerminalId, definition, "SECURITY TERMINAL", 3.4f, true, new[] { EventCoreSecurityOff }, null, null, null,
                                         new[] { padGo.GetComponent<Renderer>() }, Beacon(c, zoneGo.transform, CoreTerminalPosition, c.Kit.BeaconCyan, 1.6f));
            kiosk.AddComponent<TerminalDisplay>().EditorConfigure(interactable, screens.ToArray(), glow, speaker, c.Audio.Heist.StepStart, c.Audio.Heist.Miss);
            return interactable;
        }

        private static Material terminalScreenOpaque, terminalScreenHolo;

        private static Renderer Screen(Context c, string name, Transform parent, Mesh mesh, Vector3 localPosition, Quaternion localRotation, bool opaque)
        {
            var material = opaque ? TerminalMaterial(true) : TerminalMaterial(false);
            var go = DistrictKit.Renderer(name, parent, mesh, material, c.Environment, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            return go.GetComponent<Renderer>();
        }

        private static Material TerminalMaterial(bool opaque)
        {
            var m = DistrictKit.Material(opaque ? "District_TerminalScreen" : "District_TerminalHolo", Shader.Find("NeonRift/TerminalScreen"));
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", opaque ? (float)BlendMode.Zero : (float)BlendMode.One);
            m.SetFloat("_ZWrite", opaque ? 1f : 0f);
            m.SetFloat("_Cull", opaque ? (float)CullMode.Back : (float)CullMode.Off);
            m.SetFloat("_Brightness", opaque ? 1f : 0.8f);
            m.renderQueue = opaque ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static AudioSource Speaker(Context c, Transform parent, string name, float minDistance, float maxDistance, AudioClip loop = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = loop != null;
            s.loop = loop != null;
            s.clip = loop;
            s.volume = loop != null ? 0f : 1f;
            s.spatialBlend = 1f;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.dopplerLevel = 0f;
            s.outputAudioMixerGroup = c.Mixer != null ? c.Mixer.Sfx : null;
            return s;
        }

        // ---------------- Chamber ----------------

        private static void BuildChamber(Context c, Transform core, Interactable uplink, Transform[] rings, Renderer[] glow, Light coreLight, Light[] floods)
        {
            var kit = c.Kit;
            var machinery = new GameObject("Machinery").transform;
            machinery.SetParent(core, false);

            // Armoured sleeve: six curved panels around the column with cyan seams.
            var panels = new List<Transform>();
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * 60f + 1.5f, a1 = (i + 1) * 60f - 1.5f, mid = (a0 + a1) * 0.5f;
                Vector3 pivot = Quaternion.Euler(0f, mid, 0f) * Vector3.forward * 2.2f;
                var shell = new MeshBuilder();
                ArcShell(shell, 2.0f, 2.42f, a0, a1, 1.8f, 12f, 8, -pivot);
                var seams = new MeshBuilder();
                foreach (float a in new[] { a0 + 1f, a1 - 1f })
                {
                    Vector3 d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    seams.OrientedBox(d * 2.44f + Vector3.up * 6.9f - pivot, new Vector3(0.06f, 9.6f, 0.04f), Quaternion.LookRotation(d), 1f);
                }
                Vector3 band = Quaternion.Euler(0f, mid, 0f) * Vector3.forward;
                seams.OrientedBox(band * 2.44f + Vector3.up * 11.2f - pivot, new Vector3(1.9f, 0.08f, 0.04f), Quaternion.LookRotation(band), 1f);
                var panel = new GameObject($"SleevePanel{i}").transform;
                panel.SetParent(machinery, false);
                panel.localPosition = pivot;
                var shellGo = DistrictKit.Renderer("Shell", panel, DistrictKit.SaveMesh(shell, $"DataCore_Sleeve{i}"), kit.Metal, c.Environment);
                var seamGo = DistrictKit.Renderer("Seams", panel, DistrictKit.SaveMesh(seams, $"DataCore_SleeveSeams{i}"), kit.Indicator, c.Environment, shadows: false);
                GameObjectUtility.SetStaticEditorFlags(shellGo, 0);
                GameObjectUtility.SetStaticEditorFlags(seamGo, 0);
                panels.Add(panel);
            }

            // Rail round the pedestal top.
            var rail = new MeshBuilder();
            rail.Torus(Vector3.up * (RailHeight + 0.08f), RailRadius, 0.09f, 48, 6);
            DistrictKit.Renderer("Rail", machinery, DistrictKit.SaveMesh(rail, "DataCore_Rail"), kit.Indicator, c.Environment, shadows: false);

            var connector = BuildArm(c, machinery, "ConnectorArm", connectorHead: true);
            var scanner = BuildArm(c, machinery, "ScannerArm", connectorHead: false);

            // Data cable (glowing tether from the connector head to the car's roof).
            var cableGo = new GameObject("DataCable");
            cableGo.transform.SetParent(machinery, false);
            var cable = cableGo.AddComponent<LineRenderer>();
            cable.useWorldSpace = true;
            cable.widthMultiplier = 0.2f;
            cable.numCapVertices = 2;
            cable.shadowCastingMode = ShadowCastingMode.Off;
            cable.sharedMaterial = DistrictKit.Glow("District_DataCable", Color.white, null, 0f);
            cable.positionCount = 2;
            cable.enabled = false;

            // Scanner beam: a thin cone along local +Y (scaled to the distance at runtime).
            var beamMesh = new MeshBuilder();
            beamMesh.Cone(Vector3.up, 0.38f, 1f, 16, 0.04f);
            var beamMat = DistrictKit.Glow("District_ScanBeam", new Color(0.12f, 0.6f, 0.9f), kit.Textures.GlowGradient, 1.4f);
            var beamGo = DistrictKit.Renderer("ScanBeam", machinery, DistrictKit.SaveMesh(beamMesh, "DataCore_ScanBeam"), beamMat, c.Environment, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(beamGo, 0);
            beamGo.SetActive(false);

            // Scan rings that sweep the column.
            var scanMat = DistrictKit.Glow("District_ScanPlane", new Color(1f, 1f, 1f), null, 0f);
            var planes = new List<Transform>();
            var planeRenderers = new List<Renderer>();
            for (int i = 0; i < 2; i++)
            {
                var ring = new MeshBuilder();
                ring.Ring(Vector3.zero, 2.65f, 2.95f, 48);
                var go = DistrictKit.Renderer($"ScanRing{i}", machinery, DistrictKit.SaveMesh(ring, "DataCore_ScanRing"), scanMat, c.Environment, shadows: false);
                GameObjectUtility.SetStaticEditorFlags(go, 0);
                go.SetActive(false);
                planes.Add(go.transform);
                planeRenderers.Add(go.GetComponent<Renderer>());
            }

            // Data pulses (pooled).
            var pulseMesh = new MeshBuilder();
            pulseMesh.OrientedBox(Vector3.zero, new Vector3(0.22f, 0.22f, 0.22f), Quaternion.Euler(45f, 45f, 0f), 1f);
            var pulseMeshAsset = DistrictKit.SaveMesh(pulseMesh, "DataCore_Pulse");
            var pulseMat = DistrictKit.Glow("District_DataPulse", Color.white, null, 0f);
            var pulses = new List<Transform>();
            var pulseRoot = new GameObject("Pulses").transform;
            pulseRoot.SetParent(machinery, false);
            for (int i = 0; i < 28; i++)
            {
                var go = DistrictKit.Renderer($"Pulse{i:00}", pulseRoot, pulseMeshAsset, pulseMat, c.Environment, shadows: false);
                GameObjectUtility.SetStaticEditorFlags(go, 0);
                go.SetActive(false);
                pulses.Add(go.transform);
            }

            // Holographic readouts orbiting inside the rings.
            var holoRoot = new GameObject("Holograms").transform;
            holoRoot.SetParent(machinery, false);
            holoRoot.localPosition = Vector3.up * 10.5f;
            var holoMesh = new MeshBuilder();
            holoMesh.Panel(Vector3.zero, Quaternion.identity, 2.4f, 1.5f, new Rect(0f, 0f, 1f, 1f));
            var holoMeshAsset = DistrictKit.SaveMesh(holoMesh, "DataCore_HoloPanel");
            var holos = new List<Renderer>();
            for (int i = 0; i < 4; i++)
            {
                var d = Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward;
                var r = Screen(c, $"Holo{i}", holoRoot, holoMeshAsset, d * 3.4f, Quaternion.LookRotation(-d), opaque: false);
                holos.Add(r);
            }

            // Amber warning strobes on the frame posts.
            var strobes = new List<Light>();
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward;
                var l = Light(c, machinery, $"Strobe{i}", LightType.Point, core.position + dir * 4.2f + Vector3.up * 6f, Quaternion.identity,
                              new Color(1f, 0.55f, 0.1f), 22f, 16f);
                l.enabled = false;
                strobes.Add(l);
            }

            var heist = c.Audio.Heist;
            var hum = Speaker(c, machinery, "FacilityHum", 12f, 140f, heist.Hum);
            hum.transform.localPosition = Vector3.up * 4f;
            var servo = Speaker(c, machinery, "Servo", 6f, 80f, heist.Servo);
            servo.transform.localPosition = Vector3.up * 3f;
            var stream = Speaker(c, machinery, "DataStream", 6f, 90f, heist.Stream);
            stream.transform.localPosition = Vector3.up * 6f;
            var oneShots = Speaker(c, machinery, "Machinery", 10f, 160f);
            oneShots.transform.localPosition = Vector3.up * 5f;

            var chamber = core.gameObject.AddComponent<DataCoreChamber>();
            chamber.EditorConfigure(new DataCoreChamber.Setup
            {
                uplink = uplink,
                rings = rings,
                glow = glow,
                coreLight = coreLight,
                sleevePanels = panels.ToArray(),
                connector = connector,
                scanner = scanner,
                railRadius = RailRadius,
                railHeight = RailHeight,
                cable = cable,
                scanBeam = beamGo.transform,
                scanBeamRenderer = beamGo.GetComponent<Renderer>(),
                scanPlanes = planes.ToArray(),
                scanPlaneRenderers = planeRenderers.ToArray(),
                pulses = pulses.ToArray(),
                hologramRoot = holoRoot,
                holograms = holos.ToArray(),
                floods = floods,
                strobes = strobes.ToArray(),
                hum = hum,
                servo = servo,
                stream = stream,
                oneShots = oneShots,
                openClip = heist.ChamberOpen,
                dockClip = heist.Dock,
                releaseClip = heist.Release,
            });
            c.Chamber = chamber;
        }

        /// <summary>A rail carriage with a two-segment arm (segments along local +Y from each joint).</summary>
        private static DataCoreChamber.Arm BuildArm(Context c, Transform parent, string name, bool connectorHead)
        {
            var kit = c.Kit;
            var carriage = new GameObject(name).transform;
            carriage.SetParent(parent, false);
            var carriageMesh = new MeshBuilder();
            carriageMesh.OrientedBox(new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.5f, 1.1f), Quaternion.identity, 1f);
            carriageMesh.Cylinder(new Vector3(0f, 0.5f, 0f), 0.32f, 0.25f, 12, true);
            Part("Carriage", carriage, carriageMesh, kit.Metal, c, $"DataCore_{name}_Carriage");

            var shoulder = new GameObject("Shoulder").transform;
            shoulder.SetParent(carriage, false);
            shoulder.localPosition = Vector3.up * 0.65f;
            var upper = new MeshBuilder();
            upper.OrientedBox(new Vector3(0f, ArmUpper * 0.5f, 0f), new Vector3(0.36f, ArmUpper, 0.42f), Quaternion.identity, 1f);
            upper.Cylinder(new Vector3(-0.3f, -0.2f, 0f), 0.28f, 0.6f, 12, true);
            Part("Upper", shoulder, upper, kit.Metal, c, $"DataCore_{name}_Upper");
            var upperStrip = new MeshBuilder();
            upperStrip.OrientedBox(new Vector3(0f, ArmUpper * 0.5f, 0.22f), new Vector3(0.08f, ArmUpper * 0.8f, 0.02f), Quaternion.identity, 1f);
            Part("UpperStrip", shoulder, upperStrip, kit.Indicator, c, $"DataCore_{name}_UpperStrip", shadows: false);

            var elbow = new GameObject("Elbow").transform;
            elbow.SetParent(shoulder, false);
            elbow.localPosition = Vector3.up * ArmUpper;
            var fore = new MeshBuilder();
            fore.OrientedBox(new Vector3(0f, ArmFore * 0.5f, 0f), new Vector3(0.28f, ArmFore, 0.32f), Quaternion.identity, 1f);
            fore.Cylinder(new Vector3(-0.22f, -0.18f, 0f), 0.22f, 0.44f, 12, true);
            Part("Fore", elbow, fore, kit.Metal, c, $"DataCore_{name}_Fore");

            var head = new GameObject("Head").transform;
            head.SetParent(elbow, false);
            head.localPosition = Vector3.up * ArmFore;
            var headMesh = new MeshBuilder();
            if (connectorHead)
            {
                headMesh.OrientedBox(new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.4f, 0.6f), Quaternion.identity, 1f);
                headMesh.Cylinder(new Vector3(0f, 0.4f, 0f), 0.16f, 0.35f, 10, true);
            }
            else
            {
                headMesh.OrientedBox(new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.7f), Quaternion.identity, 1f);
            }
            Part("HeadBody", head, headMesh, kit.DarkPlastic, c, $"DataCore_{name}_Head");
            var lens = new MeshBuilder();
            lens.OrientedBox(new Vector3(0f, connectorHead ? 0.78f : 0.52f, 0f), new Vector3(0.24f, 0.06f, 0.24f), Quaternion.identity, 1f);
            Part("HeadLight", head, lens, kit.Indicator, c, $"DataCore_{name}_Lens", shadows: false);

            return new DataCoreChamber.Arm { carriage = carriage, shoulder = shoulder, elbow = elbow, head = head, upperLength = ArmUpper, foreLength = ArmFore };
        }

        private static void Part(string name, Transform parent, MeshBuilder mesh, Material material, Context c, string meshName, bool shadows = true)
        {
            var go = DistrictKit.Renderer(name, parent, DistrictKit.SaveMesh(mesh, meshName), material, c.Environment, shadows);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
        }

        /// <summary>A curved wall segment (outer, inner, top and side faces) between two angles, offset by <paramref name="offset"/>.</summary>
        private static void ArcShell(MeshBuilder m, float inner, float outer, float a0, float a1, float y0, float y1, int segments, Vector3 offset)
        {
            Vector3 D(float a) => Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            for (int s = 0; s < segments; s++)
            {
                float u0 = Mathf.Lerp(a0, a1, s / (float)segments), u1 = Mathf.Lerp(a0, a1, (s + 1) / (float)segments);
                Vector3 o0 = D(u0) * outer + offset, o1 = D(u1) * outer + offset, i0 = D(u0) * inner + offset, i1 = D(u1) * inner + offset;
                Vector3 b = Vector3.up * y0, t = Vector3.up * y1;
                Vector2 uv0 = new(s / (float)segments, 0f), uv1 = new((s + 1) / (float)segments, 0f);
                m.Quad(o0 + b, o1 + b, o1 + t, o0 + t, uv0, uv1, uv1 + Vector2.up * 4f, uv0 + Vector2.up * 4f);
                m.Quad(i1 + b, i0 + b, i0 + t, i1 + t, uv1, uv0, uv0 + Vector2.up * 4f, uv1 + Vector2.up * 4f);
                m.Quad(o0 + t, o1 + t, i1 + t, i0 + t, uv0, uv1, uv1, uv0);
            }
            Vector3 e0o = D(a0) * outer + offset, e0i = D(a0) * inner + offset, e1o = D(a1) * outer + offset, e1i = D(a1) * inner + offset;
            Vector3 lo = Vector3.up * y0, hi = Vector3.up * y1;
            m.Quad(e0i + lo, e0o + lo, e0o + hi, e0i + hi, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            m.Quad(e1o + lo, e1i + lo, e1i + hi, e1o + hi, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }
    }
}
