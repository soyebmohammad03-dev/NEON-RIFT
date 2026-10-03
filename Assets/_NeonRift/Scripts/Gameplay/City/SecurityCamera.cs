using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A street security camera. Idle while the grid is calm; once security is raised (Alert or Lockdown) it tracks the
    /// player inside its cone with line of sight, and holding them in view for <see cref="exposureSeconds"/> logs heat
    /// (shortening the trace and checkpoint timers). Cameras make risky routes riskier. Cooldown between detections.
    /// </summary>
    public sealed class SecurityCamera : MonoBehaviour, IMissionWorldComponent
    {
        [SerializeField] private Transform head;
        [SerializeField] private Renderer led;
        [SerializeField, Min(5f)] private float range = 38f;
        [SerializeField, Range(10f, 180f)] private float fieldOfView = 80f;
        [SerializeField, Min(0.1f)] private float exposureSeconds = 1.1f;
        [SerializeField, Range(0f, 1f)] private float heat = 0.08f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 14f;
        [SerializeField] private LayerMask occluders;
        [SerializeField, ColorUsage(false, true)] private Color idleColour = new(0.1f, 0.9f, 0.4f);
        [SerializeField, ColorUsage(false, true)] private Color trackingColour = new(4f, 0.15f, 0.12f);
        [SerializeField, ColorUsage(false, true)] private Color offlineColour = new(0.02f, 0.02f, 0.02f);
        [Header("Events")]
        [Tooltip("Arms the camera even while the grid is calm (a facility's own security).")]
        [SerializeField] private string[] armOn = System.Array.Empty<string>();
        [Tooltip("Takes the camera offline (a hacked security system) until a re-arm event.")]
        [SerializeField] private string[] disarmOn = System.Array.Empty<string>();
        [SerializeField] private string[] rearmOn = System.Array.Empty<string>();
        [Tooltip("While offline the head turns to watch this point instead (e.g. the core), if set.")]
        [SerializeField] private Transform offlineWatch;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MissionWorld world;
        private Material material;
        private Quaternion restRotation;
        private float exposure, cooldownUntil;
        private bool tracking, forcedArm, offline;

        public bool Tracking => tracking;

        private void Awake()
        {
            if (head != null) restRotation = head.localRotation;
            if (led != null)
            {
                material = new Material(led.sharedMaterial) { name = led.sharedMaterial.name + " (runtime)" };
                led.sharedMaterial = material;
            }
            enabled = false;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.EventRaised += OnWorldEvent;
            exposure = 0f;
            cooldownUntil = 0f;
            forcedArm = offline = false;
            tracking = true;
            SetTracking(false);
            enabled = true;
        }

        private void OnWorldEvent(string eventId)
        {
            if (MissionWorld.Matches(armOn, eventId)) forcedArm = true;
            if (MissionWorld.Matches(disarmOn, eventId)) { offline = true; tracking = true; SetTracking(false); }
            if (MissionWorld.Matches(rearmOn, eventId)) { offline = false; forcedArm = true; tracking = true; SetTracking(false); }
        }

        /// <summary>True while the camera is armed (would log heat if it saw the player).</summary>
        public bool Armed => world != null && !offline && (forcedArm || world.Security != SecurityLevel.Calm) && world.Phase == MissionPhase.Running;
        public bool Offline => offline;

        public void Unbind()
        {
            if (world != null) world.EventRaised -= OnWorldEvent;
            world = null;
            enabled = false;
        }

        private void Update()
        {
            if (world == null || world.PlayerBody == null) return;
            bool armed = Armed;
            bool seen = armed && Sees(world.PlayerBody.position + Vector3.up * 0.8f);
            SetTracking(seen);
            if (head != null)
            {
                var rest = offline && offlineWatch != null ? Quaternion.LookRotation(offlineWatch.position - head.position) : head.parent.rotation * restRotation;
                var target = seen ? Quaternion.LookRotation(world.PlayerBody.position - head.position) : rest;
                head.rotation = Quaternion.RotateTowards(head.rotation, target, 120f * Time.deltaTime);
            }
            exposure = seen ? exposure + Time.deltaTime : Mathf.Max(0f, exposure - Time.deltaTime);
            if (exposure >= exposureSeconds && Time.time >= cooldownUntil)
            {
                exposure = 0f;
                cooldownUntil = Time.time + cooldownSeconds;
                world.AddHeat(heat, "CAMERA SPOTTED YOU");
            }
        }

        private bool Sees(Vector3 point)
        {
            Vector3 origin = head != null ? head.position : transform.position;
            Vector3 d = point - origin;
            if (d.sqrMagnitude > range * range) return false;
            Vector3 axis = (head != null && head.parent != null ? head.parent.rotation * restRotation : transform.rotation) * Vector3.forward;
            if (Vector3.Angle(axis, d) > fieldOfView * 0.5f) return false;
            return !Physics.Linecast(origin, point, occluders, QueryTriggerInteraction.Ignore);
        }

        private void SetTracking(bool on)
        {
            if (tracking == on && material != null) return;
            tracking = on;
            if (material != null) material.SetColor(EmissionColor, offline ? offlineColour : on ? trackingColour : idleColour);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform cameraHead, Renderer ledRenderer, LayerMask blocking, float heatPerDetection)
        {
            head = cameraHead;
            led = ledRenderer;
            occluders = blocking;
            heat = heatPerDetection;
        }

        public void EditorConfigureEvents(string[] arm, string[] disarm, string[] rearm, Transform watchWhileOffline)
        {
            armOn = arm ?? System.Array.Empty<string>();
            disarmOn = disarm ?? System.Array.Empty<string>();
            rearmOn = rearm ?? System.Array.Empty<string>();
            offlineWatch = watchWhileOffline;
        }
#endif
    }
}
