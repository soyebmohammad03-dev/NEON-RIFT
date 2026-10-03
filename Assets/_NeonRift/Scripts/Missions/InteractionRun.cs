using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Missions
{
    /// <summary>
    /// Runtime of one multi-stage interaction (hack a terminal, extract a core …): walks an ordered list of
    /// <see cref="InteractionStep"/>s, each with its own input rule, and reports what happens through events.
    /// Plain C# driven by <see cref="Tick"/> with the Interact button state and whether the player is engaged
    /// (stopped inside the zone), so every rule is unit-testable and presentation stays in the scene.
    /// </summary>
    public sealed class InteractionRun
    {
        public enum RunState { Idle, Running, Completed, Failed }

        /// <summary>Seconds the player may be disengaged (moving, outside the zone) before the run is cancelled.</summary>
        public const float CancelGrace = 0.6f;

        private readonly IReadOnlyList<InteractionStep> steps;
        private readonly float holdDecay;
        private readonly float totalDuration;
        private bool wasHeld;
        private float disengaged, cursorTime;
        private int nextInterference, nextCue, checkpoint;

        public RunState State { get; private set; } = RunState.Idle;
        public int StepIndex { get; private set; }
        /// <summary>Progress through the current step, 0..1.</summary>
        public float StepProgress { get; private set; }
        public int Misses { get; private set; }
        /// <summary>Timing steps: cursor position on the track, 0..1.</summary>
        public float Cursor { get; private set; }
        /// <summary>Timing steps: centre of the target window, 0..1.</summary>
        public float WindowCentre { get; private set; }
        public bool InterferencePending { get; private set; }
        public float InterferenceTimeLeft { get; private set; }
        /// <summary>The Interact button is held this frame (presentation: "in use").</summary>
        public bool Held { get; private set; }
        public bool Disengaged => disengaged > 0f;

        public IReadOnlyList<InteractionStep> Steps => steps;
        public InteractionStep Current => StepIndex >= 0 && StepIndex < steps.Count ? steps[StepIndex] : null;
        public bool Running => State == RunState.Running;

        /// <summary>Progress through the whole interaction, 0..1 (steps weighted by duration).</summary>
        public float Overall
        {
            get
            {
                if (State == RunState.Completed) return 1f;
                if (totalDuration <= 0f) return 0f;
                float done = 0f;
                for (int i = 0; i < StepIndex && i < steps.Count; i++) done += steps[i].Duration;
                if (Current != null) done += Current.Duration * StepProgress;
                return Mathf.Clamp01(done / totalDuration);
            }
        }

        /// <summary>World event ids raised by steps and cues, in order.</summary>
        public event Action<string> Event;
        public event Action<int> StepStarted;
        public event Action<int> StepCompleted;
        /// <summary>A Timing press outside the window: (step, heat to add).</summary>
        public event Action<InteractionStep, float> Missed;
        public event Action InterferenceStarted;
        /// <summary>Interference ended: true if the player re-synced in time.</summary>
        public event Action<bool> InterferenceResolved;
        public event Action Completed;
        public event Action<string> Failed;
        public event Action<string> Cancelled;

        public InteractionRun(IReadOnlyList<InteractionStep> steps, float holdDecayPerSecond = 0.5f)
        {
            if (steps == null || steps.Count == 0) throw new ArgumentException("An interaction needs at least one step.", nameof(steps));
            this.steps = steps;
            holdDecay = holdDecayPerSecond;
            foreach (var s in steps) totalDuration += s.Duration;
        }

        /// <summary>Back to the very start (new attempt, re-armed terminal, after a lockout).</summary>
        public void Reset()
        {
            State = RunState.Idle;
            checkpoint = 0;
            StepIndex = 0;
            ClearStep();
            Misses = 0;
            wasHeld = false;
            disengaged = 0f;
            Held = false;
        }

        /// <summary>Advances the run. <paramref name="held"/>: Interact button; <paramref name="engaged"/>: stopped inside the zone.</summary>
        public void Tick(float dt, bool held, bool engaged)
        {
            bool pressed = held && !wasHeld;
            wasHeld = held;
            Held = held && State == RunState.Running;
            if (State is RunState.Completed or RunState.Failed) return;

            if (State == RunState.Idle)
            {
                if (!engaged || !held) return;
                State = RunState.Running;
                StepIndex = checkpoint;
                StartStep();
                Held = true;
                pressed = false;   // the press that started the run is not a Timing answer
            }

            if (!engaged)
            {
                disengaged += dt;
                if (disengaged > CancelGrace) Cancel("CONNECTION LOST");
                return;
            }
            disengaged = 0f;

            var step = Current;
            switch (step.Kind)
            {
                case InteractionStepKind.Auto:
                    StepProgress += dt / step.Duration;
                    break;
                case InteractionStepKind.Hold:
                    StepProgress = held ? StepProgress + dt / step.Duration : Mathf.Max(0f, StepProgress - holdDecay * dt);
                    break;
                case InteractionStepKind.Timing:
                    cursorTime += dt;
                    Cursor = Mathf.PingPong(cursorTime / step.Duration, 1f);
                    if (pressed)
                    {
                        if (InWindow(step, 0.5f)) StepProgress = 1f;
                        else if (Miss(step)) return;
                    }
                    break;
                case InteractionStepKind.Sustain:
                    if (InterferencePending)
                    {
                        InterferenceTimeLeft -= dt;
                        if (pressed)
                        {
                            InterferencePending = false;
                            InterferenceResolved?.Invoke(true);
                        }
                        else if (InterferenceTimeLeft <= 0f)
                        {
                            InterferencePending = false;
                            StepProgress = Mathf.Max(0f, StepProgress - step.Rollback);
                            InterferenceResolved?.Invoke(false);
                        }
                        break;
                    }
                    StepProgress += dt / step.Duration;
                    var at = step.Interference;
                    if (nextInterference < at.Length && StepProgress >= at[nextInterference] && StepProgress < 1f)
                    {
                        StepProgress = at[nextInterference];
                        nextInterference++;
                        InterferencePending = true;
                        InterferenceTimeLeft = step.ResponseSeconds;
                        InterferenceStarted?.Invoke();
                    }
                    break;
            }

            var cues = step.Cues;
            while (nextCue < cues.Length && StepProgress >= cues[nextCue].at)
                Raise(cues[nextCue++].eventId);

            if (StepProgress >= 1f) CompleteStep();
        }

        /// <summary>True if the Timing cursor is inside the window (<paramref name="fraction"/> 0.5 = the full window).</summary>
        public bool InWindow(InteractionStep step, float fraction) =>
            step != null && Mathf.Abs(Cursor - WindowCentre) <= step.Window * fraction;

        /// <summary>
        /// The input a perfect player would give this frame (validation drivers and tests): start the run, hold on
        /// Hold steps, press inside the Timing window, answer interference.
        /// </summary>
        public bool SuggestedInput()
        {
            if (State == RunState.Idle) return true;
            if (State != RunState.Running || Current == null) return false;
            return Current.Kind switch
            {
                InteractionStepKind.Hold => true,
                InteractionStepKind.Timing => InWindow(Current, 0.3f),
                InteractionStepKind.Sustain => InterferencePending,
                _ => false
            };
        }

        private bool Miss(InteractionStep step)
        {
            Misses++;
            // Move the window so a mashed button does not land twice in the same place.
            WindowCentre = 0.2f + Mathf.Repeat(WindowCentre - 0.2f + 0.37f, 0.6f);
            Missed?.Invoke(step, step.MissHeat);
            if (step.MaxMisses > 0 && Misses >= step.MaxMisses)
            {
                State = RunState.Failed;
                Held = false;
                Failed?.Invoke("COUNTERMEASURE LOCKOUT");
                return true;
            }
            return false;
        }

        private void Cancel(string reason)
        {
            State = RunState.Idle;
            StepIndex = checkpoint;
            ClearStep();
            Held = false;
            Cancelled?.Invoke(reason);
        }

        private void StartStep()
        {
            ClearStep();
            var step = Current;
            if (step.Checkpoint) checkpoint = StepIndex;
            if (step.Kind == InteractionStepKind.Timing)
            {
                WindowCentre = step.WindowCentre;
                Misses = 0;
            }
            StepStarted?.Invoke(StepIndex);
            Raise(step.EventOnStart);
        }

        private void CompleteStep()
        {
            var step = Current;
            StepProgress = 1f;
            InterferencePending = false;
            StepCompleted?.Invoke(StepIndex);
            Raise(step.EventOnComplete);
            if (StepIndex + 1 >= steps.Count)
            {
                State = RunState.Completed;
                Held = false;
                Completed?.Invoke();
                return;
            }
            StepIndex++;
            StartStep();
        }

        private void ClearStep()
        {
            StepProgress = 0f;
            cursorTime = 0f;
            Cursor = 0f;
            nextInterference = 0;
            nextCue = 0;
            InterferencePending = false;
            InterferenceTimeLeft = 0f;
            disengaged = 0f;
        }

        private void Raise(string eventId)
        {
            if (!string.IsNullOrWhiteSpace(eventId)) Event?.Invoke(eventId);
        }
    }
}
