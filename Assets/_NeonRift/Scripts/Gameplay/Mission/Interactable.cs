using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A world object the player uses from the car (terminals, uplinks, payloads): stop inside its trigger and
    /// hold Interact. Its kind comes from an <see cref="InteractionDefinition"/>; what it does is data too —
    /// on completion it raises world events that barriers, lights and objectives react to. It can be enabled,
    /// disabled or re-armed by world events, so one terminal can open a gate now and again after a lockdown.
    /// The hold itself is run by the <see cref="MissionDirector"/> for the one interactable in focus.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Interactable : MonoBehaviour, IMissionWorldComponent, IMissionTarget
    {
        public enum State { Locked, Available, Completed }

        [SerializeField] private string id;
        [SerializeField] private InteractionDefinition definition;
        [Tooltip("Object name on the prompt, e.g. SECURITY GATE.")]
        [SerializeField] private string displayName;
        [SerializeField] private float waypointHeight = 3f;
        [SerializeField] private bool startsEnabled = true;

        [Header("World events")]
        [Tooltip("Raised when the interaction completes.")]
        [SerializeField] private string[] raiseOnComplete = Array.Empty<string>();
        [SerializeField] private string[] enableOn = Array.Empty<string>();
        [SerializeField] private string[] disableOn = Array.Empty<string>();
        [Tooltip("Makes a completed interactable usable again (e.g. the gate terminal after a lockdown closes the gate).")]
        [SerializeField] private string[] rearmOn = Array.Empty<string>();

        [Header("Presentation")]
        [Tooltip("Emissive parts that show the state (locked / available / in use / done).")]
        [SerializeField] private Renderer[] indicators = Array.Empty<Renderer>();
        [SerializeField, ColorUsage(false, true)] private Color availableColor = new(0.15f, 1.2f, 1.6f);
        [SerializeField, ColorUsage(false, true)] private Color activeColor = new(3f, 3f, 3.4f);
        [SerializeField, ColorUsage(false, true)] private Color completedColor = new(0.3f, 2.8f, 0.9f);
        [SerializeField, ColorUsage(false, true)] private Color lockedColor = new(2.6f, 0.15f, 0.25f);
        [Tooltip("Shown while this is the current objective.")]
        [SerializeField] private GameObject beacon;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MissionWorld world;
        private Material indicatorMaterial;
        private int overlaps;
        private bool enabledByEvents;

        public string Id => id;
        public InteractionDefinition Definition => definition;
        public string DisplayName => displayName;
        public string WaypointLabel => displayName;
        public Vector3 WaypointPosition => transform.position + Vector3.up * waypointHeight;
        public State Current { get; private set; } = State.Locked;
        public bool PlayerInside => overlaps > 0;
        /// <summary>Hold progress, 0..1.</summary>
        public float Progress { get; private set; }
        public bool InUse { get; private set; }
        public event Action<Interactable> StateChanged;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (beacon != null) beacon.SetActive(false);
            if (indicators.Length > 0 && indicators[0] != null)
            {
                // One runtime copy shared by all indicator parts keeps them batched and leaves the asset untouched.
                indicatorMaterial = new Material(indicators[0].sharedMaterial) { name = indicators[0].sharedMaterial.name + " (runtime)" };
                foreach (var r in indicators) if (r != null) r.sharedMaterial = indicatorMaterial;
            }
        }

        private void OnDestroy()
        {
            if (indicatorMaterial != null) Destroy(indicatorMaterial);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.EventRaised += OnWorldEvent;
            enabledByEvents = startsEnabled;
            Progress = 0f;
            Refresh(force: true);
        }

        public void Unbind()
        {
            if (world != null) world.EventRaised -= OnWorldEvent;
            world = null;
            overlaps = 0;
            InUse = false;
        }

        public void SetObjectiveActive(bool active)
        {
            if (beacon != null) beacon.SetActive(active);
            Refresh();
        }

        /// <summary>Called by the director when the objective set changes; re-evaluates availability.</summary>
        public void Refresh(bool force = false)
        {
            var next = Current == State.Completed ? State.Completed : IsUsable() ? State.Available : State.Locked;
            if (next == Current && !force) return;
            Current = next;
            if (next != State.Available) { Progress = 0f; InUse = false; }
            ApplyIndicator();
            StateChanged?.Invoke(this);
        }

        /// <summary>Advances the hold by <paramref name="dt"/>. Returns true on the frame it completes.</summary>
        public bool Hold(bool held, float speedKph, float dt)
        {
            if (Current != State.Available || definition == null) return false;
            bool canHold = held && speedKph <= definition.MaxSpeedKph;
            bool wasInUse = InUse;
            InUse = canHold;
            if (canHold) Progress += definition.HoldSeconds > 0f ? dt / definition.HoldSeconds : 1f;
            else Progress = Mathf.Max(0f, Progress - definition.DecayPerSecond * dt);
            if (InUse != wasInUse) ApplyIndicator();
            if (Progress < 1f) return false;

            Progress = 1f;
            InUse = false;
            Current = State.Completed;
            ApplyIndicator();
            StateChanged?.Invoke(this);
            if (world != null)
            {
                if (definition.HeatOnComplete > 0f) world.AddHeat(definition.HeatOnComplete, definition.HeatReason);
                foreach (var e in raiseOnComplete) world.Raise(e);
                world.NotifyInteractionCompleted(this);
            }
            return true;
        }

        private bool IsUsable()
        {
            if (world == null || world.Phase != MissionPhase.Running || !enabledByEvents) return false;
            // Interactables that an objective points at only work while that objective is current.
            return !world.IsMissionTarget(id) || world.IsObjectiveTarget(id);
        }

        private void OnWorldEvent(string eventId)
        {
            if (MissionWorld.Matches(enableOn, eventId)) enabledByEvents = true;
            if (MissionWorld.Matches(disableOn, eventId)) enabledByEvents = false;
            if (MissionWorld.Matches(rearmOn, eventId) && Current == State.Completed)
            {
                Current = State.Locked;
                Progress = 0f;
            }
            Refresh();
        }

        private void ApplyIndicator()
        {
            if (indicatorMaterial == null) return;
            var colour = Current switch
            {
                State.Completed => completedColor,
                State.Available => InUse ? activeColor : availableColor,
                _ => lockedColor
            };
            indicatorMaterial.SetColor(EmissionColor, colour);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (world != null && world.IsPlayer(other)) overlaps++;
        }

        private void OnTriggerExit(Collider other)
        {
            if (world != null && world.IsPlayer(other)) overlaps = Mathf.Max(0, overlaps - 1);
        }

#if UNITY_EDITOR
        public void EditorConfigure(string interactableId, InteractionDefinition interaction, string objectName, float height, bool enabledAtStart,
                                    string[] onComplete, string[] onEnable, string[] onDisable, string[] onRearm,
                                    Renderer[] indicatorRenderers, GameObject beaconObject)
        {
            id = interactableId;
            definition = interaction;
            displayName = objectName;
            waypointHeight = height;
            startsEnabled = enabledAtStart;
            raiseOnComplete = onComplete ?? Array.Empty<string>();
            enableOn = onEnable ?? Array.Empty<string>();
            disableOn = onDisable ?? Array.Empty<string>();
            rearmOn = onRearm ?? Array.Empty<string>();
            indicators = indicatorRenderers ?? Array.Empty<Renderer>();
            beacon = beaconObject;
        }
#endif
    }
}
