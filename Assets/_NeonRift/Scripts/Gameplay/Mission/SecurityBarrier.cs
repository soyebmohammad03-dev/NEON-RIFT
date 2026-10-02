using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A physical barrier the city controls: sliding gates, checkpoint shutters, blast walls. World events open or
    /// close it. Closing is staged so the player can read it coming: an optional countdown (shortened by heat) shown
    /// on the HUD, a warning phase with strobing lights and a klaxon, then the panels slide in on a kinematic body
    /// (they shove a car aside rather than passing through it) and lock with a slam.
    /// Updates only while something is happening.
    /// </summary>
    public sealed class SecurityBarrier : MonoBehaviour, IMissionWorldComponent
    {
        private enum Stage { Open, Pending, Warning, Closing, Closed, Opening }

        [Serializable]
        public struct Panel
        {
            [Tooltip("Moving part. Needs a kinematic Rigidbody so it pushes cars instead of tunnelling.")]
            public Rigidbody body;
            [Tooltip("Local offset from the closed pose to the open pose, m.")]
            public Vector3 openOffset;
        }

        [SerializeField] private string label = "SECURITY GATE";
        [SerializeField] private bool startsClosed;
        [SerializeField] private string[] closeOn = Array.Empty<string>();
        [SerializeField] private string[] openOn = Array.Empty<string>();

        [Header("Timing")]
        [Tooltip("Delay between the close event and the warning phase, s. Shown on the HUD as a countdown.")]
        [SerializeField, Min(0f)] private float closeDelay;
        [Tooltip("Seconds taken off the delay per 1.0 heat.")]
        [SerializeField, Min(0f)] private float heatDelayPenalty;
        [SerializeField, Min(0f)] private float minimumDelay;
        [Tooltip("Warning lights and klaxon before the panels move, s.")]
        [SerializeField, Min(0f)] private float warningSeconds = 1.5f;
        [Tooltip("Time for the panels to travel, s.")]
        [SerializeField, Min(0.1f)] private float travelSeconds = 2.2f;

        [Header("Parts")]
        [SerializeField] private Panel[] panels = Array.Empty<Panel>();
        [Tooltip("Emissive strips that strobe in the warning phase and stay lit when closed.")]
        [SerializeField] private Renderer[] warningRenderers = Array.Empty<Renderer>();
        [SerializeField] private Light[] warningLights = Array.Empty<Light>();
        [SerializeField, ColorUsage(false, true)] private Color idleColor = new(0.2f, 1.6f, 2.2f);
        [SerializeField, ColorUsage(false, true)] private Color warningColor = new(4f, 0.25f, 0.35f);
        [SerializeField, Min(0f)] private float warningLightIntensity = 30f;
        [SerializeField, Min(0.5f)] private float strobeHz = 3f;

        [Header("Audio")]
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip warningClip;
        [SerializeField] private AudioClip motorClip;
        [SerializeField] private AudioClip slamClip;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MissionWorld world;
        private Material warningMaterial;
        private Vector3[] closedPositions;
        private Stage stage;
        private float stageTime, pendingSeconds;
        private float travel; // 0 = open, 1 = closed

        public string Label => label;
        public bool IsClosed => stage == Stage.Closed;
        public bool IsOpen => stage == Stage.Open;
        /// <summary>Seconds until the panels start moving, while a close is pending (warning included).</summary>
        public float SecondsUntilClosing => stage == Stage.Pending ? pendingSeconds - stageTime + warningSeconds
                                          : stage == Stage.Warning ? warningSeconds - stageTime : 0f;

        private void Awake()
        {
            closedPositions = new Vector3[panels.Length];
            for (int i = 0; i < panels.Length; i++)
                if (panels[i].body != null)
                {
                    panels[i].body.isKinematic = true;
                    panels[i].body.interpolation = RigidbodyInterpolation.Interpolate;
                    closedPositions[i] = panels[i].body.transform.localPosition;
                }
            if (warningRenderers.Length > 0 && warningRenderers[0] != null)
            {
                warningMaterial = new Material(warningRenderers[0].sharedMaterial) { name = warningRenderers[0].sharedMaterial.name + " (runtime)" };
                foreach (var r in warningRenderers) if (r != null) r.sharedMaterial = warningMaterial;
            }
            SetImmediate(startsClosed);
        }

        private void OnDestroy()
        {
            if (warningMaterial != null) Destroy(warningMaterial);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.EventRaised += OnWorldEvent;
            SetImmediate(startsClosed);
        }

        public void Unbind()
        {
            if (world != null)
            {
                world.EventRaised -= OnWorldEvent;
                world.ClearCountdown(this);
            }
            world = null;
        }

        private void OnWorldEvent(string eventId)
        {
            if (MissionWorld.Matches(closeOn, eventId)) Close();
            else if (MissionWorld.Matches(openOn, eventId)) Open();
        }

        public void Close()
        {
            if (stage is Stage.Closed or Stage.Closing or Stage.Warning or Stage.Pending) return;
            float heat = world != null ? world.Heat : 0f;
            pendingSeconds = closeDelay > 0f ? Mathf.Max(minimumDelay, closeDelay - heat * heatDelayPenalty) : 0f;
            Enter(pendingSeconds > 0f ? Stage.Pending : Stage.Warning);
            if (pendingSeconds > 0f && world != null)
            {
                world.SetCountdown(this, label + " SEALING", Time.time + pendingSeconds + warningSeconds);
                world.Announce($"{label} SEALING IN {Mathf.CeilToInt(pendingSeconds + warningSeconds)}s", MessageTone.Warning);
            }
        }

        public void Open()
        {
            if (stage is Stage.Open or Stage.Opening) return;
            world?.ClearCountdown(this);
            Enter(Stage.Opening);
        }

        private void Enter(Stage next)
        {
            stage = next;
            stageTime = 0f;
            enabled = next is Stage.Pending or Stage.Warning or Stage.Closing or Stage.Opening;
            if (source == null) return;
            switch (next)
            {
                case Stage.Warning when warningClip != null:
                    source.Stop();
                    source.clip = warningClip;
                    source.loop = true;
                    source.Play();
                    break;
                case Stage.Closing or Stage.Opening when motorClip != null:
                    source.Stop();
                    source.clip = motorClip;
                    source.loop = true;
                    source.Play();
                    break;
            }
        }

        private void Update()
        {
            stageTime += Time.deltaTime;
            switch (stage)
            {
                case Stage.Pending:
                    if (stageTime >= pendingSeconds) Enter(Stage.Warning);
                    break;
                case Stage.Warning:
                    SetWarning(Mathf.Repeat(stageTime * strobeHz, 1f) < 0.5f ? 1f : 0.15f);
                    if (stageTime >= warningSeconds) Enter(Stage.Closing);
                    break;
                case Stage.Closing:
                    SetWarning(Mathf.Repeat(stageTime * strobeHz * 2f, 1f) < 0.5f ? 1f : 0.3f);
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (stage is not (Stage.Closing or Stage.Opening)) return;
            float step = Time.fixedDeltaTime / travelSeconds;
            travel = Mathf.Clamp01(travel + (stage == Stage.Closing ? step : -step));
            MovePanels(Ease(travel), physics: true);
            if (stage == Stage.Closing && travel >= 1f) Finish(closed: true);
            else if (stage == Stage.Opening && travel <= 0f) Finish(closed: false);
        }

        private void Finish(bool closed)
        {
            Enter(closed ? Stage.Closed : Stage.Open);
            SetWarning(closed ? 1f : 0f);
            if (source != null)
            {
                source.Stop();
                if (closed && slamClip != null) source.PlayOneShot(slamClip);
            }
            if (world == null) return;
            world.ClearCountdown(this);
            if (closed) world.Announce($"{label} SEALED", MessageTone.Danger);
        }

        private void SetImmediate(bool closed)
        {
            travel = closed ? 1f : 0f;
            MovePanels(travel, physics: false);
            stage = closed ? Stage.Closed : Stage.Open;
            enabled = false;
            SetWarning(closed ? 1f : 0f);
            if (source != null) source.Stop();
        }

        private void MovePanels(float closedAmount, bool physics)
        {
            for (int i = 0; i < panels.Length; i++)
            {
                var body = panels[i].body;
                if (body == null) continue;
                var parent = body.transform.parent;
                Vector3 local = closedPositions[i] + panels[i].openOffset * (1f - closedAmount);
                Vector3 world = parent != null ? parent.TransformPoint(local) : local;
                if (physics) body.MovePosition(world);
                else
                {
                    // Teleport: an interpolated body would overwrite a transform-only change with its old pose.
                    body.position = world;
                    body.transform.localPosition = local;
                }
            }
        }

        /// <summary>0 = idle colour, 1 = full warning colour.</summary>
        private void SetWarning(float amount)
        {
            if (warningMaterial != null) warningMaterial.SetColor(EmissionColor, Color.Lerp(idleColor, warningColor, amount));
            foreach (var l in warningLights)
                if (l != null)
                {
                    l.enabled = amount > 0.01f;
                    l.intensity = amount * warningLightIntensity;
                }
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

#if UNITY_EDITOR
        public void EditorConfigure(string barrierLabel, bool closedAtStart, string[] onClose, string[] onOpen,
                                    float delay, float heatPenalty, float minDelay, float warning, float travelTime,
                                    Panel[] movingPanels, Renderer[] strips, Light[] lights,
                                    AudioSource audioSource, AudioClip warn, AudioClip motor, AudioClip slam)
        {
            label = barrierLabel;
            startsClosed = closedAtStart;
            closeOn = onClose ?? Array.Empty<string>();
            openOn = onOpen ?? Array.Empty<string>();
            closeDelay = delay;
            heatDelayPenalty = heatPenalty;
            minimumDelay = minDelay;
            warningSeconds = warning;
            travelSeconds = travelTime;
            panels = movingPanels;
            warningRenderers = strips ?? Array.Empty<Renderer>();
            warningLights = lights ?? Array.Empty<Light>();
            source = audioSource;
            warningClip = warn;
            motorClip = motor;
            slamClip = slam;
        }
#endif
    }
}
