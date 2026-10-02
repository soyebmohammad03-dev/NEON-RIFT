using System;
using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>
    /// Runtime state of one mission attempt: current objective, security level, heat and the objective timer.
    /// Plain C#, driven by the scene through <see cref="Tick"/>, <see cref="NotifyReached"/>,
    /// <see cref="NotifyInteracted"/> and <see cref="AddHeat"/>; it reports back through events.
    /// Knows nothing about scenes, so the whole mission flow is unit-testable.
    /// </summary>
    public sealed class MissionProgress
    {
        private readonly MissionDefinition definition;
        private float timerStart;

        public MissionPhase Phase { get; private set; } = MissionPhase.NotStarted;
        public int ObjectiveIndex { get; private set; } = -1;
        public ObjectiveDefinition Current =>
            ObjectiveIndex >= 0 && ObjectiveIndex < definition.Objectives.Count ? definition.Objectives[ObjectiveIndex] : null;
        public SecurityLevel Security { get; private set; } = SecurityLevel.Calm;
        /// <summary>Accumulated intrusion heat, 0..1.</summary>
        public float Heat { get; private set; }
        public float Elapsed { get; private set; }
        public bool HasTimer { get; private set; }
        public float TimeRemaining { get; private set; }
        /// <summary>The time the running timer started with (after the heat penalty), s.</summary>
        public float TimerDuration => timerStart;
        public string FailReason { get; private set; } = string.Empty;
        public MissionDefinition Definition => definition;

        /// <summary>Objective that just started (null when the mission ends).</summary>
        public event Action<ObjectiveDefinition> ObjectiveStarted;
        public event Action<ObjectiveDefinition> ObjectiveCompleted;
        public event Action<SecurityLevel> SecurityChanged;
        /// <summary>Heat gained: (amount, reason).</summary>
        public event Action<float, string> HeatAdded;
        /// <summary>A world event id from the mission data, for the scene to distribute.</summary>
        public event Action<string> WorldEvent;
        public event Action<MissionPhase> PhaseChanged;

        public MissionProgress(MissionDefinition definition)
        {
            this.definition = definition ? definition : throw new ArgumentNullException(nameof(definition));
        }

        public void Start()
        {
            if (Phase != MissionPhase.NotStarted) return;
            SetPhase(MissionPhase.Running);
            Fire(definition.EventsOnStart);
            Advance();
        }

        public void Tick(float dt)
        {
            if (Phase != MissionPhase.Running || dt <= 0f) return;
            Elapsed += dt;
            if (!HasTimer) return;
            TimeRemaining = Mathf.Max(0f, TimeRemaining - dt);
            if (TimeRemaining <= 0f) Fail(Current?.TimeoutReason ?? "TIME EXPIRED");
        }

        /// <summary>The player entered the zone <paramref name="zoneId"/>. Returns true if it completed the objective.</summary>
        public bool NotifyReached(string zoneId) => TryComplete(ObjectiveKind.Reach, zoneId);

        /// <summary>The player completed interactable <paramref name="interactableId"/>. Returns true if it completed the objective.</summary>
        public bool NotifyInteracted(string interactableId) => TryComplete(ObjectiveKind.Interact, interactableId);

        /// <summary>True if <paramref name="targetId"/> is what the current objective is waiting for.</summary>
        public bool IsCurrentTarget(string targetId) =>
            Phase == MissionPhase.Running && Current != null && Current.TargetId == targetId;

        /// <summary>Adds heat (clamped to 1). A running timer loses the matching time straight away.</summary>
        public void AddHeat(float amount, string reason)
        {
            if (Phase != MissionPhase.Running || amount <= 0f) return;
            float before = Heat;
            Heat = Mathf.Clamp01(Heat + amount);
            float gained = Heat - before;
            if (gained <= 0f) return;
            if (HasTimer) TimeRemaining = Mathf.Max(0f, TimeRemaining - gained * definition.HeatTimePenalty);
            HeatAdded?.Invoke(gained, reason);
            if (Security == SecurityLevel.Calm) Raise(SecurityLevel.Alert);
        }

        public void Fail(string reason)
        {
            if (Phase != MissionPhase.Running) return;
            FailReason = reason;
            HasTimer = false;
            SetPhase(MissionPhase.Failed);
            Fire(definition.EventsOnFail);
        }

        /// <summary>Snapshot for the results screen.</summary>
        public RunResult ToResult() => new(
            Phase == MissionPhase.Completed ? MissionOutcome.Completed : Phase == MissionPhase.Failed ? MissionOutcome.Failed : MissionOutcome.Abandoned,
            Elapsed, (int)Security);

        private bool TryComplete(ObjectiveKind kind, string targetId)
        {
            var objective = Current;
            if (Phase != MissionPhase.Running || objective == null || objective.Kind != kind || objective.TargetId != targetId) return false;
            HasTimer = false;
            ObjectiveCompleted?.Invoke(objective);
            Fire(objective.EventsOnComplete);
            Advance();
            return true;
        }

        private void Advance()
        {
            ObjectiveIndex++;
            var next = Current;
            if (next == null)
            {
                SetPhase(MissionPhase.Completed);
                Fire(definition.EventsOnComplete);
                ObjectiveStarted?.Invoke(null);
                return;
            }
            if (next.SecurityLevel > Security) Raise(next.SecurityLevel);
            HasTimer = next.TimeLimit > 0f;
            if (HasTimer)
            {
                timerStart = Mathf.Max(Mathf.Min(definition.MinimumTimeLimit, next.TimeLimit), next.TimeLimit - Heat * definition.HeatTimePenalty);
                TimeRemaining = timerStart;
            }
            ObjectiveStarted?.Invoke(next);
            Fire(next.EventsOnStart);
        }

        private void Raise(SecurityLevel level)
        {
            Security = level;
            SecurityChanged?.Invoke(level);
        }

        private void SetPhase(MissionPhase phase)
        {
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        private void Fire(string[] events)
        {
            foreach (var e in events)
                if (!string.IsNullOrWhiteSpace(e)) WorldEvent?.Invoke(e);
        }
    }
}
