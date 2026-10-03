using System.Collections.Generic;
using System.Text;
using NeonRift.Missions;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// HUD view of a multi-stage interaction: device header, the stage list with status, the active stage with its own
    /// widget (hold bar, timing track with target window and cursor, interference alarm), a scrolling data stream and
    /// overall progress. Reads the <see cref="Interactable"/> it is shown; rebuilds rows only when the stage changes.
    /// </summary>
    public sealed class TerminalReadout
    {
        private readonly VisualElement root, rows, timing, timingWindow, timingCursor, stepFill, overallFill, alarm;
        private readonly Label device, state, title, stageLabel, stageDetail, instruction, stream, overall, alarmText;
        private readonly List<(VisualElement row, Label status)> rowViews = new();
        private readonly StringBuilder streamText = new();
        private readonly System.Random rng = new(7);
        private Interactable shown;
        private int lastStep = -1;
        private InteractionRun.RunState lastState;
        private float nextStream, flashUntil;
        private string flashClass;

        public bool Visible => shown != null;

        public TerminalReadout(VisualElement container)
        {
            root = container;
            root.Clear();
            root.AddToClassList("nr-term");
            root.AddToClassList("nr-hidden");
            root.pickingMode = PickingMode.Ignore;

            var header = Add(root, "nr-term__header");
            header.Add(device = Text("nr-term__device"));
            header.Add(state = Text("nr-term__state"));
            root.Add(title = Text("nr-term__title"));
            rows = Add(root, "nr-term__rows");

            var stage = Add(root, "nr-term__stage");
            stage.Add(stageLabel = Text("nr-term__stage-label"));
            stage.Add(stageDetail = Text("nr-term__stage-detail"));
            var stepTrack = Add(stage, "nr-term__bar");
            stepFill = Add(stepTrack, "nr-term__bar-fill");
            timing = Add(stage, "nr-term__timing");
            timingWindow = Add(timing, "nr-term__timing-window");
            timingCursor = Add(timing, "nr-term__timing-cursor");
            alarm = Add(stage, "nr-term__alarm");
            alarm.Add(alarmText = Text("nr-term__alarm-text"));
            stage.Add(instruction = Text("nr-term__instruction"));

            root.Add(stream = Text("nr-term__stream"));
            var foot = Add(root, "nr-term__foot");
            var overallTrack = Add(foot, "nr-term__overall");
            overallFill = Add(overallTrack, "nr-term__overall-fill");
            foot.Add(overall = Text("nr-term__percent"));
        }

        /// <summary>Shows <paramref name="interactable"/>'s readout, or hides the panel when null.</summary>
        public void Show(Interactable interactable)
        {
            if (interactable != shown)
            {
                shown = interactable;
                lastStep = -1;
                root.EnableInClassList("nr-hidden", shown == null);
                if (shown != null)
                {
                    root.RemoveFromClassList("nr-term--enter");
                    root.AddToClassList("nr-term--enter");
                    root.schedule.Execute(() => root.RemoveFromClassList("nr-term--enter")).StartingIn(30);
                }
            }
            if (shown == null || shown.Run == null || shown.Definition == null) return;
            Refresh(shown.Run);
        }

        /// <summary>A brief colour flash of the whole panel (miss, interference, completion).</summary>
        public void Flash(Interactable.Feedback feedback)
        {
            string cls = feedback switch
            {
                Interactable.Feedback.Miss or Interactable.Feedback.Failed or Interactable.Feedback.LinkDropped => "nr-term--flash-bad",
                Interactable.Feedback.StepCompleted or Interactable.Feedback.Resynced or Interactable.Feedback.Completed => "nr-term--flash-good",
                Interactable.Feedback.Interference => "nr-term--flash-warn",
                _ => null
            };
            if (cls == null) return;
            if (flashClass != null) root.RemoveFromClassList(flashClass);
            flashClass = cls;
            root.AddToClassList(cls);
            flashUntil = Time.unscaledTime + 0.25f;
        }

        private void Refresh(InteractionRun run)
        {
            if (flashClass != null && Time.unscaledTime >= flashUntil)
            {
                root.RemoveFromClassList(flashClass);
                flashClass = null;
            }
            var def = shown.Definition;
            if (lastStep < 0)
            {
                device.text = def.DeviceName.ToUpperInvariant();
                title.text = shown.DisplayName;
                BuildRows(run);
            }
            int step = run.StepIndex;
            bool done = shown.Current == Interactable.State.Completed;
            if (step != lastStep || run.State != lastState)
            {
                lastStep = step;
                lastState = run.State;
                for (int i = 0; i < rowViews.Count; i++)
                {
                    var (row, status) = rowViews[i];
                    bool complete = done || i < step;
                    bool active = !done && i == step && run.State == InteractionRun.RunState.Running;
                    row.EnableInClassList("nr-term__row--done", complete);
                    row.EnableInClassList("nr-term__row--active", active);
                    status.text = complete ? "OK" : active ? "··" : "--";
                }
            }

            string stateText;
            if (shown.LockedOut) stateText = $"LOCKOUT {shown.LockoutRemaining:0.0}s";
            else if (done) stateText = "COMPLETE";
            else if (run.State == InteractionRun.RunState.Running) stateText = run.Disengaged ? "SIGNAL WEAK" : "LINK ACTIVE";
            else stateText = "STANDBY";
            state.text = stateText;
            root.EnableInClassList("nr-term--lockout", shown.LockedOut);
            root.EnableInClassList("nr-term--done", done);

            var current = run.Current;
            bool running = run.State == InteractionRun.RunState.Running && current != null;
            int dots = 1 + (int)(Time.unscaledTime * 3f) % 3;
            stageLabel.text = done ? def.CompleteMessage : running ? current.Label + new string('.', dots) : shown.LockedOut ? def.FailMessage : $"PRESS E · {def.Verb}";
            stageDetail.text = running ? current.Detail ?? string.Empty : string.Empty;
            stepFill.style.width = Length.Percent((running ? run.StepProgress : done ? 1f : 0f) * 100f);

            bool isTiming = running && current.Kind == InteractionStepKind.Timing;
            timing.EnableInClassList("nr-hidden", !isTiming);
            if (isTiming)
            {
                timingWindow.style.left = Length.Percent((run.WindowCentre - current.Window * 0.5f) * 100f);
                timingWindow.style.width = Length.Percent(current.Window * 100f);
                timingCursor.style.left = Length.Percent(run.Cursor * 100f);
                timing.EnableInClassList("nr-term__timing--hot", run.InWindow(current, 0.5f));
            }

            bool interference = running && run.InterferencePending;
            alarm.EnableInClassList("nr-hidden", !interference);
            if (interference) alarmText.text = $"INTERFERENCE  ·  PRESS E TO RE-SYNC  {run.InterferenceTimeLeft:0.0}s";

            instruction.text = Instruction(run, current, running, done);
            overallFill.style.width = Length.Percent(shown.Progress * 100f);
            overall.text = $"{Mathf.FloorToInt(shown.Progress * 100f):00}%";

            if (Time.unscaledTime >= nextStream)
            {
                nextStream = Time.unscaledTime + (running ? 0.07f : 0.3f);
                UpdateStream(running, interference);
            }
        }

        private string Instruction(InteractionRun run, InteractionStep current, bool running, bool done)
        {
            if (done) return string.Empty;
            if (shown.LockedOut) return "DEVICE LOCKED · WAIT";
            if (!running) return "STOP INSIDE THE ZONE AND PRESS E";
            if (run.Disengaged) return "STAY STOPPED IN THE ZONE";
            return current.Kind switch
            {
                InteractionStepKind.Hold => run.Held ? "KEEP HOLDING E" : "HOLD E",
                InteractionStepKind.Timing => $"PRESS E WHEN THE CURSOR IS IN THE WINDOW   ·   MISSES {run.Misses}/{(current.MaxMisses > 0 ? current.MaxMisses : 9)}",
                InteractionStepKind.Sustain => run.InterferencePending ? "RE-SYNC NOW" : "HOLD POSITION · LINK RUNNING",
                _ => "STAND BY · AUTOMATIC"
            };
        }

        private void BuildRows(InteractionRun run)
        {
            rows.Clear();
            rowViews.Clear();
            for (int i = 0; i < run.Steps.Count; i++)
            {
                var row = Add(rows, "nr-term__row");
                var index = Text("nr-term__row-index");
                index.text = $"{i + 1:00}";
                var label = Text("nr-term__row-label");
                label.text = run.Steps[i].Label;
                var status = Text("nr-term__row-status");
                row.Add(index);
                row.Add(label);
                row.Add(status);
                rowViews.Add((row, status));
            }
        }

        private void UpdateStream(bool running, bool interference)
        {
            const string hex = "0123456789ABCDEF";
            streamText.Clear();
            for (int line = 0; line < 2; line++)
            {
                for (int g = 0; g < 6; g++)
                {
                    for (int k = 0; k < 4; k++)
                        streamText.Append(interference && rng.NextDouble() < 0.3 ? '#' : running ? hex[rng.Next(16)] : '0');
                    streamText.Append(' ');
                }
                if (line == 0) streamText.Append('\n');
            }
            stream.text = streamText.ToString();
        }

        private static VisualElement Add(VisualElement parent, string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        private static Label Text(string cls)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            return l;
        }
    }
}
