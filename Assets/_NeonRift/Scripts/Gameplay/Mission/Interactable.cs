using System;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A world object the player uses from the car (terminals, uplinks, payloads): stop inside its trigger and work
    /// through its stages with Interact. Its kind comes from an <see cref="InteractionDefinition"/> (one hold, or a
    /// multi-stage sequence run by <see cref="InteractionRun"/>); what it does is data too — stages and completion
    /// raise world events that barriers, lights, machinery and objectives react to. It can be enabled, disabled or
    /// re-armed by world events, so one terminal can open a gate now and again after a lockdown. Too many failed
    /// stages lock it out for a while and log heat. The <see cref="MissionDirector"/> operates the one in focus.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Interactable : MonoBehaviour, IMissionWorldComponent, IMissionTarget
    {
        public enum State { Locked, Available, Completed }

        /// <summary>Moments presentation reacts to (audio cues, screen flashes, HUD).</summary>
        public enum Feedback { Started, StepStarted, StepCompleted, Miss, Interference, Resynced, LinkDropped, Completed, Failed, Cancelled }

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
        // Kept moderate: indicators can be large (the uplink ring), and neon is an accent, not a floodlight.
        [SerializeField, ColorUsage(false, true)] private Color availableColor = new(0.05f, 0.42f, 0.6f);
        [SerializeField, ColorUsage(false, true)] private Color activeColor = new(0.18f, 0.62f, 0.85f);
        [SerializeField, ColorUsage(false, true)] private Color completedColor = new(0.06f, 0.5f, 0.2f);
        [SerializeField, ColorUsage(false, true)] private Color lockedColor = new(0.3f, 0.02f, 0.04f);
        [Tooltip("Shown while this is the current objective.")]
        [SerializeField] private GameObject beacon;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MissionWorld world;
        private Material indicatorMaterial;
        private int overlaps;
        private bool enabledByEvents;
        private float lockoutUntil = -1f;
        private bool wasActive;

        public string Id => id;
        public InteractionDefinition Definition => definition;
        public string DisplayName => displayName;
        public string WaypointLabel => displayName;
        public Vector3 WaypointPosition => transform.position + Vector3.up * waypointHeight;
        public State Current { get; private set; } = State.Locked;
        public bool PlayerInside => overlaps > 0;
        /// <summary>The stage runner (null until bound).</summary>
        public InteractionRun Run { get; private set; }
        /// <summary>Overall progress, 0..1.</summary>
        public float Progress => Current == State.Completed ? 1f : Run != null ? Run.Overall : 0f;
        /// <summary>The interaction is under way (a stage is running).</summary>
        public bool InUse => Run != null && Run.Running;
        public bool LockedOut => lockoutUntil >= 0f;
        public float LockoutRemaining => LockedOut ? Mathf.Max(0f, lockoutUntil - Time.time) : 0f;
        public event Action<Interactable> StateChanged;
        public event Action<Interactable, Feedback> FeedbackRaised;

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
            lockoutUntil = -1f;
            CreateRun();
            Refresh(force: true);
        }

        public void Unbind()
        {
            if (world != null) world.EventRaised -= OnWorldEvent;
            world = null;
            overlaps = 0;
            Run = null;
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
            if (next == State.Locked && Run != null && Run.State != InteractionRun.RunState.Completed) Run.Reset();
            ApplyIndicator();
            StateChanged?.Invoke(this);
        }

        /// <summary>
        /// Operates the interaction for one frame: <paramref name="held"/> is the Interact button. The player counts as
        /// engaged while inside the zone and slower than the definition's limit. Returns true on the frame it completes.
        /// </summary>
        public bool Operate(bool held, float speedKph, float dt)
        {
            if (Current != State.Available || definition == null || Run == null) return false;
            bool engaged = PlayerInside && speedKph <= definition.MaxSpeedKph;
            Run.Tick(dt, held, engaged);
            bool active = Run.Running;
            if (active != wasActive)
            {
                wasActive = active;
                ApplyIndicator();
            }
            return Current == State.Completed;
        }

        private void CreateRun()
        {
            Run = null;
            if (definition == null) return;
            Run = new InteractionRun(definition.Steps, definition.DecayPerSecond);
            Run.Event += e => { if (world != null) world.Raise(Expand(e)); };
            Run.StepStarted += i => { if (i > 0) Notify(Feedback.StepStarted); else Notify(Feedback.Started); };
            Run.StepCompleted += _ => Notify(Feedback.StepCompleted);
            Run.Missed += (step, heat) =>
            {
                if (world != null && heat > 0f) world.AddHeat(heat, "COUNTERMEASURE TRIPPED");
                Notify(Feedback.Miss);
            };
            Run.InterferenceStarted += () => Notify(Feedback.Interference);
            Run.InterferenceResolved += ok => Notify(ok ? Feedback.Resynced : Feedback.LinkDropped);
            Run.Cancelled += reason =>
            {
                if (world != null)
                {
                    world.Announce($"{displayName}: {reason}", MessageTone.Warning);
                    foreach (var e in definition.EventsOnCancel) world.Raise(Expand(e));
                }
                Notify(Feedback.Cancelled);
            };
            Run.Failed += _ => OnFailed();
            Run.Completed += OnCompleted;
        }

        private void OnCompleted()
        {
            Current = State.Completed;
            wasActive = false;
            ApplyIndicator();
            StateChanged?.Invoke(this);
            Notify(Feedback.Completed);
            if (world == null) return;
            if (definition.HeatOnComplete > 0f) world.AddHeat(definition.HeatOnComplete, definition.HeatReason);
            foreach (var e in raiseOnComplete) world.Raise(e);
            world.NotifyInteractionCompleted(this);
        }

        private void OnFailed()
        {
            lockoutUntil = Time.time + definition.LockoutSeconds;
            wasActive = false;
            Notify(Feedback.Failed);
            if (world != null)
            {
                world.Announce($"{displayName}: {definition.FailMessage}", MessageTone.Danger);
                if (definition.FailHeat > 0f) world.AddHeat(definition.FailHeat, "HACK TRACED");
                foreach (var e in definition.EventsOnFail) world.Raise(Expand(e));
            }
            Refresh();
        }

        private void Update()
        {
            if (lockoutUntil < 0f || Time.time < lockoutUntil) return;
            lockoutUntil = -1f;
            Run?.Reset();
            Refresh();
        }

        private void Notify(Feedback feedback) => FeedbackRaised?.Invoke(this, feedback);

        /// <summary>Definition event ids may name the device generically: "{id}.unlock" → "alley_gate_terminal.unlock".</summary>
        private string Expand(string eventId) => eventId != null && eventId.Contains("{id}") ? eventId.Replace("{id}", id) : eventId;

        private bool IsUsable()
        {
            if (world == null || world.Phase != MissionPhase.Running || !enabledByEvents || LockedOut) return false;
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
                Run?.Reset();
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
