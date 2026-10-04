using System;
using System.Collections.Generic;
using NeonRift.Audio;
using NeonRift.Game;
using NeonRift.Missions;
using NeonRift.Vehicles;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Runs one attempt of a data-driven mission in its scene. Owns the <see cref="MissionProgress"/> state machine and
    /// the <see cref="MissionWorld"/> hub, binds every <see cref="IMissionWorldComponent"/> in the scene, routes zone and
    /// interaction results into the progress, and turns progress changes into HUD, mixer snapshots and audio cues.
    /// Contains no mission-specific rules: objectives, timers, heat, events and messages all come from data.
    /// </summary>
    public sealed class MissionDirector : MonoBehaviour
    {
        [SerializeField] private MissionHud hud;
        [SerializeField] private MissionAudio missionAudio;
        [Tooltip("Lockdown spreads through the district from here (usually the theft location).")]
        [SerializeField] private Transform alertOrigin;
        [Tooltip("How long the operation briefing card stays up at the start, s.")]
        [SerializeField, Min(0f)] private float briefingSeconds = 9f;
        [Tooltip("The briefing leaves early once the player drives faster than this (after 2.5 s), km/h.")]
        [SerializeField, Min(0f)] private float briefingExitKph = 60f;
        [Tooltip("A collision at this relative speed or more counts as a hard contact in the debrief, m/s.")]
        [SerializeField, Min(0f)] private float contactSpeed = 4f;
        [Tooltip("Delay between the mission ending and the results panel, s.")]
        [SerializeField, Min(0f)] private float resultsDelay = 2f;
        [SerializeField, Min(0.1f)] private float mixerFadeSeconds = 1.2f;
        [Tooltip("Road graph for the HUD route and location readout. Optional.")]
        [SerializeField] private CityNavigation navigation;
        [Tooltip("Gameplay camera, for shake and framing on mission beats. Optional.")]
        [SerializeField] private VehicleChaseCamera chaseCamera;
        [Header("Cinematic beat (the theft)")]
        [Tooltip("World event that triggers the beat. Empty disables it.")]
        [SerializeField] private string beatEvent = "core.breached";
        [SerializeField, Min(0.5f)] private float beatSeconds = 2.6f;
        [Tooltip("Time scale at the height of the beat (1 = no slow motion).")]
        [SerializeField, Range(0.2f, 1f)] private float beatTimeScale = 0.45f;
        [Tooltip("Rival crews (standings on the HUD, position in the results). Optional.")]
        [SerializeField] private RivalDirector rivals;
        [Tooltip("How often the HUD route to the objective is re-planned, s.")]
        [SerializeField, Min(0.1f)] private float routeInterval = 0.5f;

        private readonly List<IMissionWorldComponent> components = new();
        private readonly Dictionary<string, IMissionTarget> targets = new();
        private readonly List<Interactable> interactables = new();
        private readonly List<(float time, MissionAnnouncement announcement)> pending = new();
        private readonly ScriptedDrivingInput parkedInput = new();
        private bool parked;

        private GameContext context;
        private Camera viewCamera;
        private IMissionTarget activeTarget;
        private Interactable focused, terminalShown, lastTerminal;
        private float terminalHoldUntil;
        private float resultsAt = -1f;
        private int lastTickSecond = -1;
        private readonly RoadPath route = new();
        private readonly List<(Vector3 position, float heading)> rivalPositions = new();
        private readonly List<(string, bool, bool)> raceRows = new();
        private float nextRoute;
        private int routeVersion = -1;
        private bool routeValid;

        public MissionProgress Progress { get; private set; }
        public MissionWorld World { get; private set; }
        public VehicleController Player { get; private set; }
        public MissionHud Hud => hud;
        public Interactable Focused => focused;
        /// <summary>Replaces the player's Interact button (validation tools).</summary>
        public Func<bool> InteractionInputOverride { get; set; }
        public bool ResultsShown => hud != null && hud.ResultsVisible;
        /// <summary>The pause menu is open: the mission's unscaled-time logic (beat, briefing) holds still.</summary>
        public bool Paused { get; set; }

        public void Begin(GameContext gameContext, MissionDefinition mission, VehicleController player, Camera camera)
        {
            context = gameContext;
            Player = player;
            parked = false;
            resultsAt = -1f;
            viewCamera = camera;
            Progress = new MissionProgress(mission);
            World = new MissionWorld(Progress, player) { AlertOrigin = alertOrigin != null ? alertOrigin.position : transform.position };

            Progress.ObjectiveStarted += OnObjectiveStarted;
            Progress.ObjectiveCompleted += OnObjectiveCompleted;
            Progress.SecurityChanged += OnSecurityChanged;
            Progress.HeatAdded += OnHeatAdded;
            Progress.WorldEvent += World.Raise;
            Progress.PhaseChanged += OnPhaseChanged;
            World.EventRaised += OnWorldEvent;
            World.Announced += OnAnnounced;
            World.ZoneEntered += OnZoneEntered;
            World.InteractionCompleted += OnInteractionCompleted;

            BindScene();
            SetMix(MixerState.Gameplay, 0.2f);
            if (missionAudio != null) missionAudio.Initialize(context?.Config.AudioMixer);
            routeValid = false;
            nextRoute = 0f;
            if (hud != null)
            {
                hud.Clear();
                if (navigation != null) hud.SetRoadNetwork(navigation.Network, navigation.IsBlocked);
                hud.RetryClicked += Retry;
                hud.ContinueClicked += Continue;
                hud.SetVehicleName(context?.Session.SelectedVehicle != null ? context.Session.SelectedVehicle.DisplayName : player != null ? player.name : string.Empty);
            }
            // The briefing is built on the first frame, once the rival crews are on the grid.
            briefingPending = true;
            briefingStarted = -1f;
            splits.Clear();
            contacts = 0;
            if (Player != null) Player.Collided += OnPlayerCollided;
            if (context?.Settings != null)
            {
                context.Settings.Changed -= OnSettingsChanged;
                context.Settings.Changed += OnSettingsChanged;
                musicOn = context.Settings.MusicOn;
                if (hud != null) hud.SetMusicHint(musicOn);
            }
            Debug.Log($"[Mission] '{mission.Id}' started with {components.Count} world components, {targets.Count} targets.");
            Progress.Start();
        }

        public void End()
        {
            if (beatEndsAt >= 0f) EndBeat();
            if (Progress == null) return;
            if (Player != null) Player.Collided -= OnPlayerCollided;
            if (context?.Settings != null) context.Settings.Changed -= OnSettingsChanged;
            briefingPending = false;
            if (hud != null) hud.HideBriefing();
            Progress.ObjectiveStarted -= OnObjectiveStarted;
            Progress.ObjectiveCompleted -= OnObjectiveCompleted;
            Progress.SecurityChanged -= OnSecurityChanged;
            Progress.HeatAdded -= OnHeatAdded;
            Progress.WorldEvent -= World.Raise;
            Progress.PhaseChanged -= OnPhaseChanged;
            World.EventRaised -= OnWorldEvent;
            World.Announced -= OnAnnounced;
            World.ZoneEntered -= OnZoneEntered;
            World.InteractionCompleted -= OnInteractionCompleted;
            // On scene teardown the components may already be destroyed (destruction order is undefined).
            foreach (var c in components)
                if (c is UnityEngine.Object o && o != null) c.Unbind();
            components.Clear();
            targets.Clear();
            foreach (var i in interactables) if (i != null) i.FeedbackRaised -= OnInteractionFeedback;
            interactables.Clear();
            focused = terminalShown = lastTerminal = null;
            pending.Clear();
            if (hud != null)
            {
                hud.RetryClicked -= Retry;
                hud.ContinueClicked -= Continue;
            }
            if (missionAudio != null) missionAudio.StopLoops();
            if (context != null) context.Controls.Driving.Interact.performed -= OnRetryInput;
            Progress = null;
            World = null;
            context = null;
        }

        private void OnDestroy() => End();

        /// <summary>Lets the director use Pause while the results panel is up. Returns true if handled.</summary>
        public bool TryHandlePause()
        {
            if (!ResultsShown) return false;
            Continue();
            return true;
        }

        private void BindScene()
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<IMissionWorldComponent>(true))
                {
                    components.Add(c);
                    if (c is IMissionTarget t && !string.IsNullOrEmpty(t.Id))
                    {
                        if (!targets.TryAdd(t.Id, t)) Debug.LogError($"[Mission] Duplicate target id '{t.Id}'.", (UnityEngine.Object)t);
                    }
                    if (c is Interactable i)
                    {
                        interactables.Add(i);
                        i.FeedbackRaised += OnInteractionFeedback;
                    }
                }
            foreach (var objective in Progress.Definition.Objectives)
                if (objective != null && !targets.ContainsKey(objective.TargetId))
                    Debug.LogError($"[Mission] Objective '{objective.Id}' targets '{objective.TargetId}', which is not in the scene.", this);
            var positions = new List<Vector3?>();
            foreach (var objective in Progress.Definition.Objectives)
                positions.Add(objective != null && targets.TryGetValue(objective.TargetId, out var t) ? t.WaypointPosition : null);
            World.ObjectivePositions = positions;
            foreach (var c in components) c.Bind(World);
        }

        private void Update()
        {
            if (Progress == null || Paused) return;
            float dt = Time.deltaTime;
            UpdateBeat();
            UpdateBriefing();
            Progress.Tick(dt);
            RunAnnouncements();
            if (Progress.Phase == MissionPhase.Running) UpdateInteraction(dt);
            else if (missionAudio != null)
            {
                missionAudio.SetInteract(0f, 0f);
                missionAudio.SetTension(0f, 0f);
            }
            UpdateHud();
            UpdateResults();
            if (parked && Player != null)
            {
                // Brake to a stop, then handbrake only (holding brake at a standstill would select reverse).
                bool moving = Player.Telemetry.ForwardSpeed > 2f;
                parkedInput.Current = new DrivingInput { Brake = moving ? 1f : 0f, Handbrake = !moving };
            }
        }

        // ---------------- Interaction ----------------

        private void UpdateInteraction(float dt)
        {
            // A run under way keeps the focus even if the car drifts out (the run cancels itself after a grace period).
            focused = null;
            terminalShown = null;
            float best = float.MaxValue;
            Vector3 p = Player != null ? Player.transform.position : Vector3.zero;
            foreach (var i in interactables)
            {
                if (i.InUse && i.Current == Interactable.State.Available) { focused = i; break; }
                if (!i.PlayerInside) continue;
                if (i.LockedOut) { terminalShown = i; continue; }
                if (i.Current != Interactable.State.Available) continue;
                float d = (i.transform.position - p).sqrMagnitude;
                if (d < best) { best = d; focused = i; }
            }

            bool held = InteractionInputOverride?.Invoke() ?? (context != null && context.Controls.Driving.Interact.IsPressed());
            float kph = Player != null ? Player.Telemetry.SpeedKph : 0f;
            if (focused != null)
            {
                var current = focused;
                current.Operate(held, kph, dt);
                if (current.Definition != null && current.Definition.IsSequence) terminalShown = current;
                if (missionAudio != null)
                {
                    missionAudio.SetInteract(current.InUse ? 1f : 0f, current.Progress);
                    float tension = current.Definition != null ? current.Definition.Tension : 0f;
                    missionAudio.SetTension(current.InUse ? tension * (0.35f + 0.65f * current.Progress) : 0f, current.Progress);
                }
            }
            else if (missionAudio != null)
            {
                missionAudio.SetInteract(0f, 0f);
                missionAudio.SetTension(0f, 0f);
            }
            // Keep the readout up briefly after completion so the result reads.
            if (terminalShown == null && lastTerminal != null && Time.time < terminalHoldUntil) terminalShown = lastTerminal;
            if (terminalShown != null) lastTerminal = terminalShown;
        }

        private void OnInteractionFeedback(Interactable source, Interactable.Feedback feedback)
        {
            if (hud != null) hud.FlashTerminal(feedback);
            if (missionAudio != null) missionAudio.Play(feedback);
            if (feedback == Interactable.Feedback.Completed)
            {
                lastTerminal = source;
                terminalHoldUntil = Time.time + 1.6f;
            }
            if (feedback == Interactable.Feedback.Interference && chaseCamera != null) chaseCamera.Kick(0.12f);
        }

        private void OnInteractionCompleted(Interactable interactable)
        {
            if (missionAudio != null) missionAudio.Play(MissionAudio.Cue.InteractComplete);
            if (hud != null && interactable.Definition != null && !string.IsNullOrEmpty(interactable.Definition.CompleteMessage))
                hud.Toast(interactable.Definition.CompleteMessage, MessageTone.Success);
            Progress.NotifyInteracted(interactable.Id);
            foreach (var i in interactables) i.Refresh();
        }

        private void OnZoneEntered(MissionZone zone) => Progress.NotifyReached(zone.Id);

        // ---------------- Progress events ----------------

        private void OnObjectiveStarted(ObjectiveDefinition objective)
        {
            if (activeTarget != null) activeTarget.SetObjectiveActive(false);
            activeTarget = null;
            if (objective != null && targets.TryGetValue(objective.TargetId, out var target))
            {
                activeTarget = target;
                target.SetObjectiveActive(true);
            }
            foreach (var i in interactables) i.Refresh();
            bool last = objective != null && Progress.ObjectiveIndex == Progress.Definition.Objectives.Count - 1;
            World.NotifyObjectiveStarted(objective, activeTarget != null ? activeTarget.WaypointPosition : (Vector3?)null, last);
            if (objective == null)
            {
                if (hud != null) hud.HideObjective();
                return;
            }
            if (hud != null)
                hud.SetObjective(Progress.ObjectiveIndex + 1, Progress.Definition.Objectives.Count, objective.Title, objective.Detail,
                                 Progress.Security == SecurityLevel.Lockdown);
            if (missionAudio != null && Progress.ObjectiveIndex > 0) missionAudio.Play(MissionAudio.Cue.Objective);
            Debug.Log($"[Mission] objective {Progress.ObjectiveIndex + 1}: {objective.Id} → {objective.TargetId}" +
                      (Progress.HasTimer ? $" ({Progress.TimeRemaining:0.0}s)" : string.Empty));
        }

        private void OnObjectiveCompleted(ObjectiveDefinition objective)
        {
            Debug.Log($"[Mission] objective '{objective.Id}' complete at {Progress.Elapsed:0.0}s");
            splits.Add(Progress.Elapsed);
            if (hud != null && Progress.ObjectiveIndex < Progress.Definition.Objectives.Count - 1)
                hud.StampObjective($"OBJECTIVE COMPLETE   {MissionRecords.FormatTime(Progress.Elapsed)}", 1.6f);
        }

        private void OnSecurityChanged(SecurityLevel level)
        {
            World.NotifySecurityChanged(level);
            if (missionAudio != null) missionAudio.SetScoreSecurity(level);
            Debug.Log($"[Mission] security → {level} (heat {Progress.Heat:0.00})");
            if (level == SecurityLevel.Lockdown)
            {
                if (chaseCamera != null)
                {
                    chaseCamera.Kick(0.35f);
                    chaseCamera.Focus(World.AlertOrigin + Vector3.up * 20f, 2.2f, 0.25f);
                }
                SetMix(MixerState.Lockdown, mixerFadeSeconds);
                if (missionAudio != null) missionAudio.Play(MissionAudio.Cue.Lockdown);
            }
        }

        private void OnHeatAdded(float amount, string reason)
        {
            if (hud != null) hud.Toast($"{reason}  +{Mathf.RoundToInt(amount * 100f)}% HEAT", MessageTone.Warning);
            if (missionAudio != null) missionAudio.Play(MissionAudio.Cue.Heat);
        }

        /// <summary>The player's finishing position among the rival crews (set when the mission completes).</summary>
        public int FinishPosition { get; private set; } = 1;

        private void OnPhaseChanged(MissionPhase phase)
        {
            if (phase is not (MissionPhase.Completed or MissionPhase.Failed)) return;
            if (rivals != null) FinishPosition = rivals.FinishPosition();
            var result = Progress.ToResult();
            context?.Session.RecordResult(result);
            SetMix(MixerState.Results, mixerFadeSeconds);
            if (missionAudio != null)
            {
                missionAudio.SetInteract(0f, 0f);
                // The score resolves into its outro on the beat; without a score, the plain cue.
                bool success = phase == MissionPhase.Completed;
                if (!missionAudio.EndScore(success)) missionAudio.Play(success ? MissionAudio.Cue.Success : MissionAudio.Cue.Failure);
            }
            if (activeTarget != null) activeTarget.SetObjectiveActive(false);
            activeTarget = null;
            if (hud != null)
            {
                hud.SetPrompt(false, null, 0f, null, false);
                if (phase == MissionPhase.Completed) hud.Banner("EXTRACTED", "THE DATA CORE IS YOURS", MessageTone.Success, resultsDelay + 0.5f);
                else hud.Banner("TRACED", Progress.FailReason, MessageTone.Danger, resultsDelay + 0.5f);
            }
            ParkPlayer();
            resultsAt = Time.time + resultsDelay;
            Debug.Log($"[Mission] {phase} in {Progress.Elapsed:0.0}s, heat {Progress.Heat:0.00}, security {Progress.Security}" +
                      (phase == MissionPhase.Failed ? $" — {Progress.FailReason}" : string.Empty));
        }

        private float beatEndsAt = -1f;
        private bool slowMotion;

        private void OnWorldEvent(string eventId)
        {
            if (!string.IsNullOrEmpty(beatEvent) && eventId == beatEvent) StartBeat();
            if (missionAudio != null) missionAudio.OnWorldEvent(eventId);
            foreach (var a in Progress.Definition.Announcements)
                if (a.eventId == eventId) pending.Add((Time.time + a.delay, a));
        }

        /// <summary>The theft beat: wide camera on car and core, letterbox, brief slow motion (all on unscaled time).</summary>
        private void StartBeat()
        {
            Vector3 subject = alertOrigin != null ? alertOrigin.position : World.AlertOrigin;
            if (chaseCamera != null) chaseCamera.CinematicBeat(subject + Vector3.up * 4f, beatSeconds);
            if (hud != null) hud.SetLetterbox(true);
            beatEndsAt = Time.unscaledTime + beatSeconds;
            slowMotion = beatTimeScale < 0.999f;
            Debug.Log($"[Mission] cinematic beat '{beatEvent}' for {beatSeconds:0.0}s at time scale {beatTimeScale:0.00}");
        }

        private void UpdateBeat()
        {
            if (beatEndsAt < 0f) return;
            float left = beatEndsAt - Time.unscaledTime;
            if (left <= 0f)
            {
                EndBeat();
                return;
            }
            if (slowMotion)
            {
                // Ease into slow motion over 0.3 s and back out over the last 0.6 s.
                float elapsed = beatSeconds - left;
                float w = Mathf.Clamp01(Mathf.Min(elapsed / 0.3f, left / 0.6f));
                Time.timeScale = Mathf.Lerp(1f, beatTimeScale, w);
            }
            if (left < 0.6f && hud != null) hud.SetLetterbox(false);
        }

        private void EndBeat()
        {
            beatEndsAt = -1f;
            if (slowMotion) Time.timeScale = 1f;
            slowMotion = false;
            if (hud != null) hud.SetLetterbox(false);
        }

        private void OnAnnounced(string text, MessageTone tone)
        {
            if (hud != null) hud.Toast(text, tone);
            if (missionAudio != null) missionAudio.Play(MissionAudio.Cue.Message, 0.7f);
        }

        private void RunAnnouncements()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < pending[i].time) continue;
                var a = pending[i].announcement;
                pending.RemoveAt(i);
                if (hud == null) continue;
                if (a.banner)
                {
                    int bar = a.text.IndexOf('|');
                    hud.Banner(bar < 0 ? a.text : a.text.Substring(0, bar), bar < 0 ? null : a.text.Substring(bar + 1), a.tone, 3.5f);
                }
                else hud.Toast(a.text, a.tone);
            }
        }

        // ---------------- HUD ----------------

        private void UpdateHud()
        {
            if (hud == null) return;
            var telemetry = Player != null ? Player.Telemetry : default;
            hud.SetSpeed(telemetry.SpeedKph, telemetry.Gear, telemetry.NormalizedEngineRpm);
            hud.SetSecurity(Progress.Security, Progress.Heat);

            bool running = Progress.Phase == MissionPhase.Running;
            hud.SetTimer(running && Progress.HasTimer, Progress.TimeRemaining);
            if (running && Progress.HasTimer && Progress.TimeRemaining <= 10f)
            {
                int second = Mathf.CeilToInt(Progress.TimeRemaining);
                if (second != lastTickSecond && missionAudio != null) missionAudio.Play(MissionAudio.Cue.CountdownTick, 0.8f);
                lastTickSecond = second;
            }

            if (running && World.TryGetNextCountdown(out var label, out var remaining)) hud.SetCountdown(label, remaining);
            else hud.SetCountdown(null, 0f);

            if (running && activeTarget != null && Player != null && viewCamera != null)
            {
                Vector3 targetPosition = activeTarget.WaypointPosition;
                float distance = Vector3.Distance(Player.transform.position, targetPosition);
                hud.SetObjectiveDistance(distance);
                hud.SetWaypoint(true, viewCamera.WorldToViewportPoint(targetPosition), activeTarget.WaypointLabel, distance,
                                Progress.Security == SecurityLevel.Lockdown);
            }
            else hud.SetWaypoint(false, Vector3.zero, null, 0f, false);

            UpdateNavigator(running);
            UpdateRace(running);

            if (running && focused != null && focused.Definition != null && !(focused.Definition.IsSequence && focused.InUse))
            {
                var d = focused.Definition;
                bool tooFast = Player != null && Player.Telemetry.SpeedKph > d.MaxSpeedKph;
                string hint = tooFast ? "STOP THE CAR TO INTERACT" : focused.InUse ? "KEEP HOLDING" : null;
                string verb = d.IsSequence ? "PRESS" : "HOLD";
                // Rebuilt only when the device or verb changes (no string per frame).
                if (promptFor != focused || promptVerb != verb)
                {
                    promptFor = focused;
                    promptVerb = verb;
                    promptText = $"{verb}  {d.Verb}  ·  {focused.DisplayName}";
                }
                hud.SetPrompt(true, promptText, focused.Progress, hint, tooFast);
            }
            else hud.SetPrompt(false, null, 0f, null, false);
            hud.SetTerminal(running ? terminalShown : null);
            var run = running && focused != null && focused.InUse ? focused.Run : null;
            hud.SetExtractionFrame(run != null && run.Current != null && run.Current.Kind == InteractionStepKind.Sustain ? (run.InterferencePending ? 2 : 1) : 0);
        }

        private void UpdateNavigator(bool running)
        {
            if (navigation == null || Player == null || Player.Body == null) return;
            Vector3 p = Player.Body.position;
            bool hasTarget = running && activeTarget != null;
            Vector3 target = hasTarget ? activeTarget.WaypointPosition : Vector3.zero;
            if (hasTarget && (Time.time >= nextRoute || navigation.Version != routeVersion))
            {
                nextRoute = Time.time + routeInterval;
                routeVersion = navigation.Version;
                routeValid = navigation.Plan(p, target, route);
            }
            if (!hasTarget) routeValid = false;
            string district = navigation.TryGetDistrict(p, out var d) ? d.displayName : "OUTSKIRTS";
            float heading = MinimapProjection.Heading(Player.transform.forward);
            rivalPositions.Clear();
            if (rivals != null)
                for (int i = 0; i < rivals.Rivals.Count; i++)   // index loop: foreach over IReadOnlyList boxes an enumerator
                {
                    var r = rivals.Rivals[i];
                    if (r.Car != null) rivalPositions.Add((r.Car.transform.position, MinimapProjection.Heading(r.Car.transform.forward)));
                }
            hud.SetNavigator(p, heading, district, navigation.StreetAt(p), routeValid ? route.Points : null, routeValid ? route.Length : 0f,
                             hasTarget, target, rivalPositions, Progress.Security == SecurityLevel.Lockdown);
        }

        private Interactable promptFor;
        private string promptVerb, promptText, gapText;
        private string gapOther;
        private int gapMetres = -1;
        private bool gapAhead;

        private void UpdateRace(bool running)
        {
            if (rivals == null || !rivals.HasRivals)
            {
                hud.SetRace(false, 0, 0, null, null);
                return;
            }
            bool visible = running && rivals.RaceActive && rivals.Standings.Count > 0;
            if (!visible) { hud.SetRace(false, 0, 0, null, null); return; }
            raceRows.Clear();
            var standings = rivals.Standings;
            for (int i = 0; i < standings.Count; i++) raceRows.Add((standings[i].Row, standings[i].Finished, standings[i].IsPlayer));
            // The gap line only changes when the rounded distance, the side or the other car changes.
            string gap = null;
            if (rivals.TryGetGap(out var other, out float metres, out bool ahead))
            {
                int m = Mathf.RoundToInt(metres);
                if (m != gapMetres || ahead != gapAhead || !ReferenceEquals(other, gapOther))
                {
                    gapMetres = m;
                    gapAhead = ahead;
                    gapOther = other;
                    int dot = other.IndexOf('·');
                    gapText = $"{m} M {(ahead ? "BEHIND" : "AHEAD OF")} {(dot < 0 ? other : other.Substring(0, dot)).Trim()}";
                }
                gap = gapText;
            }
            hud.SetRace(true, rivals.PlayerPosition, rivals.Count, gap, raceRows);
        }

        // ---------------- Results ----------------

        private void UpdateResults()
        {
            if (resultsAt < 0f || Time.time < resultsAt) return;
            resultsAt = -1f;
            if (hud == null) return;
            hud.ShowResults(BuildDebrief());
            if (context != null) context.Controls.Driving.Interact.performed += OnRetryInput;
        }

        // ---------------- Presentation ----------------

        private bool briefingPending;
        private float briefingStarted = -1f;
        private readonly List<float> splits = new();
        private int contacts;

        /// <summary>Hard contacts with walls, props or cars so far this run.</summary>
        public int Contacts => contacts;

        private bool musicOn = true;

        private void OnSettingsChanged()
        {
            if (hud == null || context?.Settings == null) return;
            bool on = context.Settings.MusicOn;
            hud.SetMusicHint(on);
            if (on != musicOn) hud.Toast(on ? "MUSIC ON" : "MUSIC OFF  ·  PRESS M TO TURN IT BACK ON", MessageTone.Info);
            musicOn = on;
        }

        private void OnPlayerCollided(VehicleCollision c)
        {
            if (Progress != null && Progress.Phase == MissionPhase.Running && c.RelativeSpeed >= contactSpeed) contacts++;
        }

        private void UpdateBriefing()
        {
            if (hud == null) return;
            if (briefingPending)
            {
                briefingPending = false;
                briefingStarted = Time.unscaledTime;
                hud.ShowBriefing(BuildBriefing());
                return;
            }
            if (briefingStarted < 0f) return;
            float shown = Time.unscaledTime - briefingStarted;
            bool driving = Player != null && Player.Telemetry.SpeedKph > briefingExitKph && shown > 2.5f;
            if (shown >= briefingSeconds || driving || Progress.Phase != MissionPhase.Running)
            {
                briefingStarted = -1f;
                hud.HideBriefing();
            }
        }

        private MissionHud.Briefing BuildBriefing()
        {
            var def = Progress.Definition;
            var b = new MissionHud.Briefing
            {
                Kicker = "OPERATION  ·  SECTOR 7",
                Title = def.DisplayName.ToUpperInvariant(),
                Tagline = def.Tagline,
                Text = def.Briefing
            };
            foreach (var o in def.Objectives) b.Objectives.Add(o.Title);
            string car = context?.Session.SelectedVehicle != null ? context.Session.SelectedVehicle.DisplayName.ToUpperInvariant() : null;
            b.Crew.Add(("YOU" + (car != null ? "  ·  " + car : string.Empty), "DRIVER", true));
            if (rivals != null)
                for (int i = 0; i < rivals.Rivals.Count; i++)
                {
                    // Racer names already carry the car ("VEX · SLS AMG GT3").
                    b.Crew.Add((rivals.Rivals[i].Name, def.CrewRoleFor(i) ?? "RIVAL CREW", false));
                }
            float best = MissionRecords.BestTime(def.Id);
            int bestScore = MissionRecords.BestScore(def.Id);
            if (best > 0f) b.Best = $"PERSONAL BEST  {MissionRecords.FormatTime(best)}" + (bestScore >= 0 ? $"  ·  GRADE {MissionGrade.LetterFor(bestScore)}" : string.Empty);
            return b;
        }

        private MissionHud.Debrief BuildDebrief()
        {
            var def = Progress.Definition;
            bool success = Progress.Phase == MissionPhase.Completed;
            float t = Progress.Elapsed;
            bool raced = rivals != null && rivals.HasRivals;
            // Qualifying: the first crews out advance to the next operation; a later finish still completes the job.
            int places = raced ? def.QualifyingPlaces : 0;
            bool qualified = success && (places <= 0 || FinishPosition <= places);
            var d = new MissionHud.Debrief
            {
                Success = success,
                Kicker = "DEBRIEF  ·  " + def.DisplayName.ToUpperInvariant(),
                Title = success ? "MISSION COMPLETE" : "MISSION FAILED",
                Reason = !success ? Progress.FailReason
                    : qualified ? "EXTRACTION CONFIRMED  ·  DATA CORE SECURED" + (places > 0 ? "  ·  QUALIFIED" : string.Empty)
                    : $"DATA CORE SECURED  ·  NOT QUALIFIED  ·  ONLY THE FIRST {places} OUT ADVANCE"
            };
            d.Stats.Add(("TIME", MissionRecords.FormatTime(t)));
            if (success && raced) d.Stats.Add(("POSITION", $"{FinishPosition}/{rivals.Count}"));
            if (success && places > 0) d.Stats.Add(("QUALIFYING", qualified ? $"TOP {places}  ·  QUALIFIED" : "NOT QUALIFIED"));
            d.Stats.Add(("HEAT", $"{Mathf.RoundToInt(Progress.Heat * 100f)}%"));
            d.Stats.Add(("SECURITY", Progress.Security.ToString().ToUpperInvariant()));
            d.Stats.Add(("HARD CONTACTS", contacts.ToString()));
            d.Stats.Add(("OBJECTIVES", $"{(success ? def.Objectives.Count : Progress.ObjectiveIndex)}/{def.Objectives.Count}"));

            for (int i = 0; i < def.Objectives.Count; i++)
            {
                bool done = i < splits.Count;
                d.Splits.Add(($"{i + 1:00}  {def.Objectives[i].Title}", done ? MissionRecords.FormatTime(splits[i]) : "--", !done));
            }

            if (raced)
            {
                // Finishing order: crews that extracted first, then the player (if out), then everyone still running.
                var finished = new List<RivalDirector.Racer>();
                var running = new List<RivalDirector.Racer>();
                foreach (var r in rivals.Rivals) (r.Finished ? finished : running).Add(r);
                finished.Sort((x, y) => x.FinishTime.CompareTo(y.FinishTime));
                running.Sort((x, y) => x.Remaining.CompareTo(y.Remaining));
                int place = 1;
                string Out(int p) => places > 0 && p <= places ? "QUALIFIED" : "EXTRACTED";
                foreach (var r in finished) { d.Crews.Add(($"P{place}  {r.Name}", Out(place), false)); place++; }
                if (success) { d.Crews.Add(($"P{place}  YOU", qualified ? Out(place) : "NOT QUALIFIED", true)); place++; }
                foreach (var r in running) d.Crews.Add(($"P{place++}  {r.Name}", "STILL RUNNING", false));
                if (!success) d.Crews.Add(("--  YOU", "TRACED", true));
            }

            float previous;
            if (success)
            {
                var grade = MissionGrade.Evaluate(t, def.ParTime > 0f ? def.ParTime : t, Progress.Heat, raced ? FinishPosition : 0,
                                                  raced ? rivals.Count : 1, contacts);
                d.Grade = grade.Letter;
                d.Score = grade.Score;
                d.NewBest = MissionRecords.Submit(def.Id, t, grade.Score, out previous);
                if (qualified) MissionRecords.MarkQualified(def.Id);
                d.Best = previous <= 0f ? "FIRST CLEAR  ·  PERSONAL BEST SET"
                    : d.NewBest ? $"NEW PERSONAL BEST  ·  {MissionRecords.FormatTime(previous - t)} FASTER"
                    : $"PERSONAL BEST  {MissionRecords.FormatTime(previous)}  ·  +{MissionRecords.FormatTime(t - previous)}";
                Debug.Log($"[Mission] debrief: P{FinishPosition}{(places > 0 ? (qualified ? " qualified" : " not qualified") : string.Empty)}, grade {grade.Letter} ({grade.Score}: pace {grade.Pace:0}, quiet {grade.Quiet:0}, position {grade.Position:0}, clean {grade.Clean:0}), contacts {contacts}, new best {d.NewBest}");
            }
            else
            {
                previous = MissionRecords.BestTime(def.Id);
                if (previous > 0f) d.Best = $"PERSONAL BEST  {MissionRecords.FormatTime(previous)}";
            }
            return d;
        }

        private void OnRetryInput(UnityEngine.InputSystem.InputAction.CallbackContext _) => Retry();

        /// <summary>Restarts the mission through the normal retry path (also used by validation tools).</summary>
        public void RestartMission() => Retry();

        private void Retry()
        {
            if (context == null || context.Flow.IsTransitioning) return;
            context.Controls.Driving.Interact.performed -= OnRetryInput;
            context.Flow.StartMission();
        }

        private void Continue()
        {
            if (context == null || context.Flow.IsTransitioning) return;
            context.Controls.Driving.Interact.performed -= OnRetryInput;
            context.Flow.GoToCarSelect();
        }

        private void SetMix(MixerState state, float seconds)
        {
            if (context?.Audio == null || !context.Audio.IsAvailable) return;
            context.Audio.TransitionTo(state, seconds);
            Debug.Log($"[Mission] mixer → {context.Audio.State}");
        }

        private void ParkPlayer()
        {
            if (Player == null) return;
            parked = true;
            parkedInput.Current = new DrivingInput { Brake = 1f };
            foreach (var receiver in Player.GetComponentsInChildren<IVehicleInputReceiver>())
                receiver.SetInputSource(parkedInput);
        }
    }
}
