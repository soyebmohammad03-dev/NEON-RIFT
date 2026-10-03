using System;
using System.Collections.Generic;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Presentation of the Data Core heist. Reads world events and the uplink's <see cref="InteractionRun"/>; owns no
    /// gameplay rules. Phases:
    /// <list type="bullet">
    /// <item>Dormant: armoured sleeve closed, core dim, arms folded, holograms on standby.</item>
    /// <item>Alerted (intruder in the facility): floodlights sweep, holograms turn amber, a red scan plane sweeps the
    ///   core, the facility hum starts.</item>
    /// <item>Unlocked (local security disabled): floods stand down, the sleeve sinks into the pedestal panel by panel,
    ///   internal light floods out, rings spin up, scan planes run, the arms unfold.</item>
    /// <item>Extraction: the arm carriages ride the pedestal rail to face the car; the connector arm docks and drops a
    ///   data cable onto the roof; the scanner arm holds a beam on it; data pulses run core → arm → cable → car at a
    ///   rate that climbs with progress; the core shifts from cyan through white to a hot magenta; warning strobes
    ///   start and speed up; interference stalls the pulses, turns the link red and tears the holograms.</item>
    /// <item>Acquired: the cable releases and the arm pulls back. Breached: the core goes to the alarm colour.</item>
    /// </list>
    /// </summary>
    public sealed class DataCoreChamber : MonoBehaviour, IMissionWorldComponent
    {
        [Serializable]
        public struct Arm
        {
            [Tooltip("Carriage on the pedestal rail (moves round the core, yaws to face the target).")]
            public Transform carriage;
            [Tooltip("Shoulder joint (pitches about local X). Segments point along local +Y.")]
            public Transform shoulder;
            public Transform elbow;
            public Transform head;
            public float upperLength, foreLength;
        }

        [SerializeField] private Interactable uplink;
        [Header("Events")]
        [SerializeField] private string alertEvent = "core.arrive";
        [SerializeField] private string unlockEvent = "core.security.off";
        [SerializeField] private string acquiredEvent = "core.acquired";
        [SerializeField] private string breachedEvent = "core.breached";

        [Header("Core")]
        [SerializeField] private Transform[] rings = Array.Empty<Transform>();
        [SerializeField] private Renderer[] glowRenderers = Array.Empty<Renderer>();
        [SerializeField] private Light coreLight;
        [SerializeField, ColorUsage(false, true)] private Color dormantColor = new(0.06f, 0.5f, 0.8f);
        [SerializeField, ColorUsage(false, true)] private Color idleColor = new(0.3f, 2.2f, 3.2f);
        [SerializeField, ColorUsage(false, true)] private Color hotColor = new(1.3f, 2.7f, 3.5f);
        [SerializeField, ColorUsage(false, true)] private Color peakColor = new(3.2f, 0.9f, 2.7f);
        [SerializeField, ColorUsage(false, true)] private Color breachedColor = new(5f, 0.25f, 0.6f);

        [Header("Sleeve")]
        [SerializeField] private Transform[] sleevePanels = Array.Empty<Transform>();
        [SerializeField] private float sleeveDrop = 9.6f;
        [SerializeField] private float sleeveOut = 0.45f;
        [SerializeField] private float sleeveSeconds = 3.2f;

        [Header("Machinery")]
        [SerializeField] private Arm connector;
        [SerializeField] private Arm scanner;
        [SerializeField] private float railRadius = 3f;
        [SerializeField] private float railHeight = 1.8f;
        [SerializeField] private LineRenderer cable;
        [SerializeField] private Transform scanBeam;
        [SerializeField] private Renderer scanBeamRenderer;
        [SerializeField] private Transform[] scanPlanes = Array.Empty<Transform>();
        [SerializeField] private Renderer[] scanPlaneRenderers = Array.Empty<Renderer>();
        [SerializeField] private Transform[] pulses = Array.Empty<Transform>();
        [SerializeField] private Transform hologramRoot;
        [SerializeField] private Renderer[] holograms = Array.Empty<Renderer>();

        [Header("Facility")]
        [SerializeField] private Light[] floods = Array.Empty<Light>();
        [SerializeField] private Light[] strobes = Array.Empty<Light>();
        [SerializeField] private VehicleChaseCamera chaseCamera;

        [Header("Audio (3D)")]
        [SerializeField] private AudioSource hum;
        [SerializeField] private AudioSource servo;
        [SerializeField] private AudioSource stream;
        [SerializeField] private AudioSource oneShots;
        [SerializeField] private AudioClip openClip;
        [SerializeField] private AudioClip dockClip;
        [SerializeField] private AudioClip releaseClip;

        private enum Phase { Dormant, Alerted, Unlocked, Acquired, Breached }

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int StepsId = Shader.PropertyToID("_Steps");
        private static readonly int StepId = Shader.PropertyToID("_Step");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int GlitchId = Shader.PropertyToID("_Glitch");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        private MissionWorld world;
        private Material glowMaterial;
        private MaterialPropertyBlock block;
        private Phase phase;
        private float phaseTime, spin, glitch, flash;
        private Vector3[] sleeveClosed = Array.Empty<Vector3>();
        private Quaternion[] floodRest = Array.Empty<Quaternion>();
        private float[] floodIntensity = Array.Empty<float>();
        private float[] strobeIntensity = Array.Empty<float>();
        private readonly List<Vector3> path = new();
        private readonly List<float> pathLength = new();
        private float[] pulseT = Array.Empty<float>();
        private Renderer[] pulseRenderers = Array.Empty<Renderer>();
        private float pulseSpawn;
        private Vector3 connectorTarget, scannerTarget;
        private float connectorAngle, scannerAngle;
        private bool docked;
        private float dockedAt = -1f;
        private int lastStep = -1;
        private readonly Vector3[] cablePoints = new Vector3[14];

        private Vector3 Centre => transform.position;
        private InteractionRun Run => uplink != null ? uplink.Run : null;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (glowRenderers.Length > 0 && glowRenderers[0] != null)
            {
                glowMaterial = new Material(glowRenderers[0].sharedMaterial) { name = glowRenderers[0].sharedMaterial.name + " (runtime)" };
                foreach (var r in glowRenderers) if (r != null) r.sharedMaterial = glowMaterial;
            }
            sleeveClosed = new Vector3[sleevePanels.Length];
            for (int i = 0; i < sleevePanels.Length; i++) if (sleevePanels[i] != null) sleeveClosed[i] = sleevePanels[i].localPosition;
            floodRest = new Quaternion[floods.Length];
            floodIntensity = new float[floods.Length];
            for (int i = 0; i < floods.Length; i++)
                if (floods[i] != null) { floodRest[i] = floods[i].transform.rotation; floodIntensity[i] = floods[i].intensity; }
            strobeIntensity = new float[strobes.Length];
            for (int i = 0; i < strobes.Length; i++) if (strobes[i] != null) strobeIntensity[i] = strobes[i].intensity;
            pulseT = new float[pulses.Length];
            pulseRenderers = new Renderer[pulses.Length];
            for (int i = 0; i < pulses.Length; i++) if (pulses[i] != null) pulseRenderers[i] = pulses[i].GetComponent<Renderer>();
            ResetPresentation();
        }

        private void OnDestroy()
        {
            if (glowMaterial != null) Destroy(glowMaterial);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.EventRaised += OnWorldEvent;
            ResetPresentation();
        }

        public void Unbind()
        {
            if (world != null) world.EventRaised -= OnWorldEvent;
            world = null;
            if (chaseCamera != null && framing) chaseCamera.SetFraming(false, Centre);
            framing = false;
        }

        private void ResetPresentation()
        {
            phase = Phase.Dormant;
            phaseTime = 0f;
            docked = false;
            dockedAt = -1f;
            lastStep = -1;
            for (int i = 0; i < sleevePanels.Length; i++) if (sleevePanels[i] != null) sleevePanels[i].localPosition = sleeveClosed[i];
            for (int i = 0; i < pulses.Length; i++) { pulseT[i] = -1f; if (pulses[i] != null) pulses[i].gameObject.SetActive(false); }
            if (cable != null) cable.enabled = false;
            if (scanBeam != null) scanBeam.gameObject.SetActive(false);
            foreach (var p in scanPlanes) if (p != null) p.gameObject.SetActive(false);
            foreach (var s in strobes) if (s != null) s.enabled = false;
            for (int i = 0; i < floods.Length; i++)
                if (floods[i] != null) { floods[i].transform.rotation = floodRest[i]; floods[i].intensity = floodIntensity[i]; }
            connectorAngle = 200f;
            scannerAngle = 250f;
            connectorTarget = FoldedTarget(connector, connectorAngle);
            scannerTarget = FoldedTarget(scanner, scannerAngle);
            if (hum != null) hum.volume = 0f;
            if (stream != null) stream.volume = 0f;
            if (servo != null) servo.volume = 0f;
        }

        private void OnWorldEvent(string eventId)
        {
            if (eventId == alertEvent && phase == Phase.Dormant) Enter(Phase.Alerted);
            else if (eventId == unlockEvent && phase < Phase.Unlocked)
            {
                Enter(Phase.Unlocked);
                if (oneShots != null && openClip != null) oneShots.PlayOneShot(openClip);

            }
            else if (eventId == acquiredEvent)
            {
                Enter(Phase.Acquired);
                flash = 1f;
                if (oneShots != null && releaseClip != null) oneShots.PlayOneShot(releaseClip);
            }
            else if (eventId == breachedEvent) Enter(Phase.Breached);
        }

        private void Enter(Phase next)
        {
            phase = next;
            phaseTime = 0f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            phaseTime += dt;
            var run = Run;
            bool extracting = phase == Phase.Unlocked && run != null && run.Running;
            float progress = phase >= Phase.Acquired ? 1f : uplink != null && phase == Phase.Unlocked ? uplink.Progress : 0f;
            int step = extracting ? run.StepIndex : -1;
            bool interference = extracting && run.InterferencePending;
            if (step != lastStep && extracting && step >= 0) OnStep(run, step);
            lastStep = step;
            if (interference) glitch = Mathf.Max(glitch, 0.6f + 0.4f * Mathf.Sin(Time.time * 25f));
            glitch = Mathf.MoveTowards(glitch, 0f, dt * 2f);
            flash = Mathf.MoveTowards(flash, 0f, dt * 1.5f);

            UpdateSleeve();
            UpdateCore(progress, extracting, interference, dt);
            UpdateFacility(progress, extracting, dt);
            UpdateArms(run, extracting, dt);
            UpdateLink(progress, extracting, interference, dt);
            UpdateHolograms(run, progress, interference);
            UpdateAudio(progress, extracting, interference, dt);
            UpdateCamera(extracting);
        }

        /// <summary>Holds a wide shot of the car and the core while the chamber opens and while data is pulled.</summary>
        private void UpdateCamera(bool extracting)
        {
            if (chaseCamera == null) return;
            var player = world != null ? world.PlayerBody : null;
            bool near = player != null && (player.position - Centre).sqrMagnitude < 32f * 32f;
            bool opening = phase == Phase.Unlocked && phaseTime < sleeveSeconds + 0.8f;
            bool on = near && (extracting || opening);
            if (on != framing)
            {
                framing = on;
                chaseCamera.SetFraming(on, Centre + Vector3.up * 7f);
            }
        }

        private bool framing;

        private void OnStep(InteractionRun run, int step)
        {
            if (chaseCamera != null && run.Current != null && run.Current.Kind == InteractionStepKind.Sustain) chaseCamera.Kick(0.08f);
        }

        // ---------------- Sleeve and core ----------------

        private void UpdateSleeve()
        {
            float t = phase >= Phase.Unlocked ? phaseTime : 0f;
            if (phase >= Phase.Acquired) t = 99f;
            for (int i = 0; i < sleevePanels.Length; i++)
            {
                if (sleevePanels[i] == null) continue;
                // Staggered: each panel unlocks (a small outward kick), then sinks into the pedestal.
                float local = Mathf.Clamp01((t - i * 0.18f) / sleeveSeconds);
                float kick = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(local * 4f));
                float sink = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((local - 0.2f) / 0.8f));
                Vector3 closed = sleeveClosed[i];
                Vector3 outward = new Vector3(closed.x, 0f, closed.z).normalized;
                sleevePanels[i].localPosition = closed + outward * (sleeveOut * kick) + Vector3.down * (sleeveDrop * sink);
            }
        }

        private void UpdateCore(float progress, bool extracting, bool interference, float dt)
        {
            Color colour;
            float targetSpin;
            switch (phase)
            {
                case Phase.Dormant:
                case Phase.Alerted:
                    colour = dormantColor * (phase == Phase.Alerted ? 1.3f : 1f);
                    targetSpin = 18f;
                    break;
                case Phase.Breached:
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 9f);
                    colour = Color.Lerp(breachedColor, Color.white * 6f, Mathf.Clamp01(1f - phaseTime * 1.5f)) * pulse;
                    targetSpin = 60f;
                    break;
                case Phase.Acquired:
                    // Drained: the core collapses to a dull ember before the alarm takes it.
                    colour = Color.Lerp(peakColor, dormantColor * 0.6f, Mathf.Clamp01(phaseTime / 1.2f)) + Color.white * (flash * 4f);
                    targetSpin = Mathf.Lerp(420f, 30f, Mathf.Clamp01(phaseTime / 1.5f));
                    break;
                default:
                    float open = Mathf.Clamp01(phaseTime / sleeveSeconds);
                    colour = Color.Lerp(dormantColor, idleColor, open);
                    if (progress > 0f)
                        colour = progress < 0.6f ? Color.Lerp(idleColor, hotColor, progress / 0.6f) : Color.Lerp(hotColor, peakColor, (progress - 0.6f) / 0.4f);
                    if (extracting) colour *= 0.88f + 0.12f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 22f, progress));
                    if (interference) colour = Color.Lerp(colour, breachedColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 30f));
                    targetSpin = Mathf.Lerp(Mathf.Lerp(18f, 60f, open), 460f, progress * progress);
                    break;
            }
            spin = Mathf.MoveTowards(spin, targetSpin, 500f * dt);
            for (int i = 0; i < rings.Length; i++)
                if (rings[i] != null) rings[i].Rotate(0f, spin * dt * (i % 2 == 0 ? 1f : -1.4f), 0f, Space.Self);
            if (glowMaterial != null) glowMaterial.SetColor(EmissionColor, colour);
            if (coreLight != null)
            {
                float max = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
                coreLight.color = max > 0f ? colour / max : Color.black;
                coreLight.intensity = 2f + max * 2.4f;
            }
        }

        private void UpdateFacility(float progress, bool extracting, float dt)
        {
            // Floods: sweep while the facility hunts for the intruder, stand down once its security is ours.
            for (int i = 0; i < floods.Length; i++)
            {
                var f = floods[i];
                if (f == null) continue;
                if (phase == Phase.Alerted)
                {
                    float yaw = Mathf.Sin(Time.time * 0.7f + i * 1.9f) * 28f;
                    f.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * floodRest[i];
                    f.color = new Color(1f, 0.92f, 0.82f);
                    f.intensity = floodIntensity[i] * 1.3f;
                }
                else if (phase == Phase.Unlocked || phase == Phase.Acquired)
                {
                    f.transform.rotation = Quaternion.Slerp(f.transform.rotation, floodRest[i], dt * 2f);
                    f.color = new Color(0.55f, 0.8f, 1f);
                    f.intensity = Mathf.MoveTowards(f.intensity, floodIntensity[i] * 0.35f, floodIntensity[i] * dt);
                }
                else if (phase == Phase.Breached)
                {
                    f.color = new Color(1f, 0.2f, 0.25f);
                    f.intensity = floodIntensity[i] * (0.7f + 0.3f * Mathf.Sin(Time.time * 6f));
                }
            }

            // Warning strobes: start once a third of the data is out and speed up towards the end.
            bool strobeOn = (extracting && progress > 0.3f) || phase == Phase.Acquired || phase == Phase.Breached;
            float hz = phase == Phase.Breached ? 3f : Mathf.Lerp(0.7f, 4f, Mathf.InverseLerp(0.3f, 1f, progress));
            for (int i = 0; i < strobes.Length; i++)
            {
                var s = strobes[i];
                if (s == null) continue;
                s.enabled = strobeOn;
                if (!strobeOn) continue;
                float w = Mathf.Repeat(Time.time * hz + i * 0.25f, 1f);
                s.intensity = strobeIntensity[i] * (w < 0.35f ? 1f : 0.05f);
            }

            // Scan planes: red and fast while hunting, cyan and slow while the chamber is ours.
            bool scanning = phase == Phase.Alerted || phase == Phase.Unlocked;
            for (int i = 0; i < scanPlanes.Length; i++)
            {
                var plane = scanPlanes[i];
                if (plane == null) continue;
                plane.gameObject.SetActive(scanning);
                if (!scanning) continue;
                float speed = phase == Phase.Alerted ? 0.9f : Mathf.Lerp(0.25f, 0.8f, progress);
                float y = Mathf.Lerp(2.2f, 22f, Mathf.PingPong(Time.time * speed + i * 0.5f, 1f));
                plane.position = Centre + Vector3.up * y;
                if (i < scanPlaneRenderers.Length && scanPlaneRenderers[i] != null)
                {
                    block.Clear();
                    block.SetColor(ColorId, phase == Phase.Alerted ? new Color(2.4f, 0.25f, 0.3f) : new Color(0.3f, 1.4f, 2.2f) * (1f + glitch));
                    scanPlaneRenderers[i].SetPropertyBlock(block);
                }
            }
        }

        // ---------------- Arms ----------------

        private void UpdateArms(InteractionRun run, bool extracting, float dt)
        {
            var player = world != null ? world.PlayerBody : null;
            bool open = phase == Phase.Unlocked && phaseTime > sleeveSeconds * 0.6f;
            bool engage = extracting && player != null;
            Vector3 carPoint = player != null ? player.position + Vector3.up * 1.25f : Centre;
            float carAngle = player != null ? Mathf.Atan2(player.position.x - Centre.x, player.position.z - Centre.z) * Mathf.Rad2Deg : connectorAngle;

            // Carriages ride the rail to the car's side (scanner offset so it sees the car past the connector).
            float wantConnector = engage ? carAngle : open ? 200f : connectorAngle;
            float wantScanner = engage ? carAngle + 55f : open ? 250f : scannerAngle;
            float before = connectorAngle + scannerAngle;
            connectorAngle = Mathf.MoveTowardsAngle(connectorAngle, wantConnector, 70f * dt);
            scannerAngle = Mathf.MoveTowardsAngle(scannerAngle, wantScanner, 70f * dt);
            float railMotion = Mathf.Abs(Mathf.DeltaAngle(before, connectorAngle + scannerAngle));

            // Connector: folded → ready → reaching for the roof during CONNECT → docked over the car.
            Vector3 wantC;
            int step = extracting ? run.StepIndex : -1;
            if (engage && step >= 0)
            {
                Vector3 shoulder = connector.shoulder.position;
                Vector3 toCar = carPoint + Vector3.up * 1.6f - shoulder;
                float reach = (connector.upperLength + connector.foreLength) * 0.94f;
                wantC = shoulder + Vector3.ClampMagnitude(toCar, reach);
                if (step == 0) wantC = Vector3.Lerp(ReadyTarget(connector, connectorAngle), wantC, Mathf.SmoothStep(0f, 1f, run.StepProgress * 1.4f));
            }
            else if (open || phase == Phase.Acquired) wantC = ReadyTarget(connector, connectorAngle);
            else wantC = FoldedTarget(connector, connectorAngle);
            Vector3 wantS = engage ? scanner.shoulder.position + Vector3.ClampMagnitude(carPoint + Vector3.up * 5f - scanner.shoulder.position, (scanner.upperLength + scanner.foreLength) * 0.8f)
                          : open ? ReadyTarget(scanner, scannerAngle) : FoldedTarget(scanner, scannerAngle);

            float speedC = (wantC - connectorTarget).magnitude;
            connectorTarget = Vector3.MoveTowards(connectorTarget, wantC, 6f * dt);
            scannerTarget = Vector3.MoveTowards(scannerTarget, wantS, 6f * dt);
            Pose(connector, connectorAngle, connectorTarget);
            Pose(scanner, scannerAngle, scannerTarget);

            bool nowDocked = engage && step >= 1 || engage && step == 0 && run.StepProgress > 0.8f;
            if (nowDocked && !docked && oneShots != null && dockClip != null) oneShots.PlayOneShot(dockClip);
            if (nowDocked && !docked) dockedAt = Time.time;
            docked = nowDocked;

            if (scanBeam != null)
            {
                scanBeam.gameObject.SetActive(engage);
                if (engage)
                {
                    Vector3 from = scanner.head.position;
                    // The beam sweeps the car front to back.
                    Vector3 sweep = player.rotation * Vector3.forward * (Mathf.Sin(Time.time * 2.2f) * 2.2f);
                    Vector3 to = player.position + sweep + Vector3.up * 0.6f;
                    scanBeam.position = from;
                    scanBeam.rotation = Quaternion.LookRotation(to - from) * Quaternion.Euler(90f, 0f, 0f);
                    scanBeam.localScale = new Vector3(1f, (to - from).magnitude, 1f);
                }
            }
            if (servo != null) servo.volume = Mathf.MoveTowards(servo.volume, Mathf.Clamp01(speedC * 0.6f + railMotion * 0.05f) * 0.7f, dt * 3f);
        }

        private Vector3 RailPoint(float angle) =>
            Centre + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * railRadius + Vector3.up * railHeight;

        private Vector3 FoldedTarget(Arm arm, float angle) =>
            RailPoint(angle) + Vector3.up * ((arm.upperLength + arm.foreLength) * 0.9f) - (Quaternion.Euler(0f, angle, 0f) * Vector3.forward) * 0.6f;

        private Vector3 ReadyTarget(Arm arm, float angle) =>
            RailPoint(angle) + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 3f + Vector3.up * 5.5f;

        /// <summary>Places the carriage on the rail and solves the two-bone arm (elbow up) to reach <paramref name="target"/>.</summary>
        private void Pose(Arm arm, float angle, Vector3 target)
        {
            if (arm.carriage == null || arm.shoulder == null || arm.elbow == null) return;
            arm.carriage.position = RailPoint(angle);
            Vector3 flat = target - arm.carriage.position;
            flat.y = 0f;
            Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            arm.carriage.rotation = Quaternion.LookRotation(flat.sqrMagnitude > 0.01f ? flat.normalized : outward);
            Vector3 s = arm.shoulder.position;
            float x = new Vector2(target.x - s.x, target.z - s.z).magnitude;
            float y = target.y - s.y;
            float l1 = arm.upperLength, l2 = arm.foreLength;
            float d = Mathf.Clamp(Mathf.Sqrt(x * x + y * y), Mathf.Abs(l1 - l2) + 0.05f, l1 + l2 - 0.02f);
            float phi = Mathf.Atan2(x, y);
            float alpha = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f));
            float beta = Mathf.Acos(Mathf.Clamp((l1 * l1 + l2 * l2 - d * d) / (2f * l1 * l2), -1f, 1f));
            arm.shoulder.localRotation = Quaternion.Euler((phi - alpha) * Mathf.Rad2Deg, 0f, 0f);
            arm.elbow.localRotation = Quaternion.Euler((Mathf.PI - beta) * Mathf.Rad2Deg, 0f, 0f);
        }

        // ---------------- Data link ----------------

        private void UpdateLink(float progress, bool extracting, bool interference, float dt)
        {
            var player = world != null ? world.PlayerBody : null;
            bool linked = docked && extracting && player != null && connector.head != null;
            if (cable != null)
            {
                cable.enabled = linked;
                if (linked)
                {
                    // Lowers from the arm onto the roof over half a second, with a little sag.
                    Vector3 a = connector.head.position, b = player.position + Vector3.up * 1.2f;
                    float drop = Mathf.Clamp01((Time.time - dockedAt) / 0.5f);
                    b = Vector3.Lerp(a, b, drop);
                    float sag = Mathf.Min(1.2f, (b - a).magnitude * 0.12f);
                    for (int i = 0; i < cablePoints.Length; i++)
                    {
                        float u = i / (cablePoints.Length - 1f);
                        cablePoints[i] = Vector3.Lerp(a, b, u) + Vector3.down * (sag * 4f * u * (1f - u));
                    }
                    cable.positionCount = cablePoints.Length;
                    cable.SetPositions(cablePoints);
                    Color c = interference ? Color.Lerp(new Color(3f, 0.2f, 0.3f), new Color(0.3f, 0.05f, 0.05f), Mathf.Repeat(Time.time * 12f, 1f) < 0.5f ? 0f : 1f)
                                           : Color.Lerp(new Color(0.3f, 1.6f, 2.4f), new Color(2.6f, 1.2f, 3f), progress);
                    cable.startColor = c;
                    cable.endColor = c * 0.7f;
                }
            }

            // Pulses: core → arm → cable → car, faster as the pull deepens; frozen and red under interference.
            BuildPath(linked);
            float rate = linked && !interference ? Mathf.Lerp(2.5f, 16f, progress) : 0f;
            pulseSpawn += rate * dt;
            float total = pathLength.Count > 0 ? pathLength[^1] : 0f;
            float travel = total > 0f ? Mathf.Lerp(12f, 30f, progress) / total : 0f;
            for (int i = 0; i < pulses.Length; i++)
            {
                if (pulses[i] == null) continue;
                if (pulseT[i] < 0f && pulseSpawn >= 1f && linked)
                {
                    pulseSpawn -= 1f;
                    pulseT[i] = 0f;
                }
                if (pulseT[i] < 0f || !linked)
                {
                    pulseT[i] = -1f;
                    if (pulses[i].gameObject.activeSelf) pulses[i].gameObject.SetActive(false);
                    continue;
                }
                if (!interference) pulseT[i] += travel * dt;
                if (pulseT[i] >= 1f) { pulseT[i] = -1f; pulses[i].gameObject.SetActive(false); continue; }
                pulses[i].gameObject.SetActive(true);
                pulses[i].position = Sample(pulseT[i] * total);
                var r = pulseRenderers[i];
                if (r != null)
                {
                    block.Clear();
                    block.SetColor(ColorId, interference ? new Color(3f, 0.3f, 0.3f) : Color.Lerp(new Color(0.6f, 2.4f, 3.4f), new Color(3.4f, 1.6f, 3.6f), progress));
                    r.SetPropertyBlock(block);
                }
            }
            pulseSpawn = Mathf.Min(pulseSpawn, 2f);
        }

        private void BuildPath(bool linked)
        {
            path.Clear();
            pathLength.Clear();
            if (!linked) return;
            path.Add(Centre + Vector3.up * 8f);
            path.Add(connector.shoulder.position);
            path.Add(connector.elbow.position);
            path.Add(connector.head.position);
            for (int i = 1; i < cablePoints.Length; i++) path.Add(cablePoints[i]);
            float acc = 0f;
            pathLength.Add(0f);
            for (int i = 1; i < path.Count; i++)
            {
                acc += (path[i] - path[i - 1]).magnitude;
                pathLength.Add(acc);
            }
        }

        private Vector3 Sample(float distance)
        {
            for (int i = 1; i < path.Count; i++)
                if (distance <= pathLength[i])
                {
                    float seg = pathLength[i] - pathLength[i - 1];
                    return Vector3.Lerp(path[i - 1], path[i], seg > 1e-4f ? (distance - pathLength[i - 1]) / seg : 0f);
                }
            return path.Count > 0 ? path[^1] : Centre;
        }

        // ---------------- Holograms and audio ----------------

        private void UpdateHolograms(InteractionRun run, float progress, bool interference)
        {
            if (hologramRoot != null) hologramRoot.Rotate(0f, (phase == Phase.Unlocked ? 14f : 5f) * Time.deltaTime, 0f, Space.World);
            Color colour; float mode;
            switch (phase)
            {
                case Phase.Alerted: colour = new Color(2.6f, 1.2f, 0.2f); mode = 3f; break;
                case Phase.Unlocked: colour = interference ? new Color(3f, 0.3f, 0.35f) : progress > 0f ? Color.Lerp(new Color(0.5f, 2f, 2.8f), new Color(2.6f, 1.4f, 3f), progress) : new Color(0.4f, 2.4f, 1.2f); mode = progress > 0f ? 1f : 2f; break;
                case Phase.Acquired: colour = new Color(0.5f, 2.8f, 1.2f); mode = 2f; break;
                case Phase.Breached: colour = new Color(3.2f, 0.25f, 0.35f); mode = 3f; break;
                default: colour = new Color(0.2f, 0.8f, 1.1f); mode = 0f; break;
            }
            for (int i = 0; i < holograms.Length; i++)
            {
                if (holograms[i] == null) continue;
                block.Clear();
                block.SetColor(ColorId, colour);
                block.SetFloat(ProgressId, progress);
                block.SetFloat(StepsId, run != null ? run.Steps.Count : 5f);
                block.SetFloat(StepId, phase >= Phase.Acquired ? 99f : run != null ? run.StepIndex : 0f);
                block.SetFloat(ModeId, mode);
                block.SetFloat(GlitchId, glitch);
                block.SetFloat(SeedId, i * 5.3f);
                holograms[i].SetPropertyBlock(block);
            }
        }

        private void UpdateAudio(float progress, bool extracting, bool interference, float dt)
        {
            if (hum != null)
            {
                float target = phase switch { Phase.Dormant => 0f, Phase.Alerted => 0.55f, Phase.Breached => 0.4f, _ => 0.75f };
                hum.volume = Mathf.MoveTowards(hum.volume, target, dt * 0.6f);
                hum.pitch = Mathf.Lerp(0.9f, 1.35f, progress) * (interference ? 0.92f : 1f);
            }
            if (stream != null)
            {
                float target = docked && extracting ? Mathf.Lerp(0.35f, 0.9f, progress) * (interference ? 0.3f : 1f) : 0f;
                stream.volume = Mathf.MoveTowards(stream.volume, target, dt * 1.5f);
                stream.pitch = Mathf.Lerp(0.9f, 1.5f, progress);
            }
        }

#if UNITY_EDITOR
        [Serializable]
        public struct Setup
        {
            public Interactable uplink;
            public Transform[] rings, sleevePanels, scanPlanes, pulses;
            public Renderer[] glow, scanPlaneRenderers, holograms;
            public Light coreLight;
            public Arm connector, scanner;
            public float railRadius, railHeight;
            public LineRenderer cable;
            public Transform scanBeam, hologramRoot;
            public Renderer scanBeamRenderer;
            public Light[] floods, strobes;
            public VehicleChaseCamera chaseCamera;
            public AudioSource hum, servo, stream, oneShots;
            public AudioClip openClip, dockClip, releaseClip;
        }

        public void EditorConfigure(Setup s)
        {
            uplink = s.uplink;
            rings = s.rings ?? Array.Empty<Transform>();
            glowRenderers = s.glow ?? Array.Empty<Renderer>();
            coreLight = s.coreLight;
            sleevePanels = s.sleevePanels ?? Array.Empty<Transform>();
            connector = s.connector;
            scanner = s.scanner;
            railRadius = s.railRadius;
            railHeight = s.railHeight;
            cable = s.cable;
            scanBeam = s.scanBeam;
            scanBeamRenderer = s.scanBeamRenderer;
            scanPlanes = s.scanPlanes ?? Array.Empty<Transform>();
            scanPlaneRenderers = s.scanPlaneRenderers ?? Array.Empty<Renderer>();
            pulses = s.pulses ?? Array.Empty<Transform>();
            hologramRoot = s.hologramRoot;
            holograms = s.holograms ?? Array.Empty<Renderer>();
            floods = s.floods ?? Array.Empty<Light>();
            strobes = s.strobes ?? Array.Empty<Light>();
            chaseCamera = s.chaseCamera;
            hum = s.hum;
            servo = s.servo;
            stream = s.stream;
            oneShots = s.oneShots;
            openClip = s.openClip;
            dockClip = s.dockClip;
            releaseClip = s.releaseClip;
        }

        public void EditorSetChaseCamera(VehicleChaseCamera camera) => chaseCamera = camera;
#endif
    }
}
