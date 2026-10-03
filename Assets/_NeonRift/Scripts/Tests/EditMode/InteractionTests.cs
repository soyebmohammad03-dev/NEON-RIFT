using System.Collections.Generic;
using NeonRift.Missions;
using NUnit.Framework;
using UnityEngine;

namespace NeonRift.Tests
{
    /// <summary>The multi-stage interaction runner (pure logic) and objective start delays.</summary>
    public class InteractionTests
    {
        private const float Dt = 0.02f;

        private static InteractionRun Run(params InteractionStep[] steps) => new(new List<InteractionStep>(steps));

        private static void Tick(InteractionRun run, float seconds, bool held, bool engaged = true)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Dt) run.Tick(Dt, held, engaged);
        }

        [Test]
        public void StagesRunInOrderAndRaiseTheirEvents()
        {
            var run = Run(new InteractionStep("CONNECTING", InteractionStepKind.Auto, 1f, eventOnStart: "a.start", eventOnComplete: "a.done"),
                          new InteractionStep("AUTH", InteractionStepKind.Hold, 1f, eventOnComplete: "b.done"));
            var events = new List<string>();
            bool completed = false;
            run.Event += events.Add;
            run.Completed += () => completed = true;

            Tick(run, 0.5f, held: false);
            Assert.AreEqual(InteractionRun.RunState.Idle, run.State, "Nothing starts until the player presses Interact.");
            run.Tick(Dt, true, true);
            Assert.AreEqual(InteractionRun.RunState.Running, run.State);
            Tick(run, 1.1f, held: false);
            Assert.AreEqual(1, run.StepIndex, "Auto stages run without the button.");
            Tick(run, 0.5f, held: false);
            Assert.AreEqual(0f, run.StepProgress, 1e-4f, "Hold stages need the button.");
            Tick(run, 1.1f, held: true);
            Assert.IsTrue(completed);
            Assert.AreEqual(1f, run.Overall, 1e-4f);
            CollectionAssert.AreEqual(new[] { "a.start", "a.done", "b.done" }, events);
        }

        [Test]
        public void TimingPressInsideTheWindowPassesAndMissesCostHeatThenLockOut()
        {
            var step = new InteractionStep("BYPASS", InteractionStepKind.Timing, 1f).WithTiming(0.2f, 0.5f, 2, 0.05f);
            var run = Run(step);
            float heat = 0f;
            string failure = null;
            run.Missed += (_, h) => heat += h;
            run.Failed += r => failure = r;
            run.Tick(Dt, true, true);          // start (this press is not an answer)
            run.Tick(Dt, false, true);
            // Press immediately: the cursor is near 0, far from the window at 0.5.
            run.Tick(Dt, true, true);
            Assert.AreEqual(1, run.Misses);
            Assert.AreEqual(0.05f, heat, 1e-5f);
            Assert.AreNotEqual(0.5f, run.WindowCentre, "A miss moves the window.");
            run.Tick(Dt, false, true);
            run.Tick(Dt, true, true);
            Assert.AreEqual(InteractionRun.RunState.Failed, run.State);
            Assert.IsNotNull(failure);

            // A well-timed press succeeds.
            run = Run(new InteractionStep("BYPASS", InteractionStepKind.Timing, 1f).WithTiming(0.2f, 0.5f, 2, 0.05f));
            bool done = false;
            run.Completed += () => done = true;
            for (int i = 0; i < 400 && !done; i++) run.Tick(Dt, run.SuggestedInput(), true);
            Assert.IsTrue(done);
            Assert.AreEqual(0, run.Misses);
        }

        [Test]
        public void InterferenceStallsUntilAnsweredAndRollsBackWhenIgnored()
        {
            var run = Run(new InteractionStep("EXTRACT", InteractionStepKind.Sustain, 4f).WithInterference(new[] { 0.5f }, 1f, 0.2f));
            int started = 0;
            var resolved = new List<bool>();
            run.InterferenceStarted += () => started++;
            run.InterferenceResolved += resolved.Add;
            run.Tick(Dt, true, true);
            Tick(run, 2.2f, held: false);
            Assert.AreEqual(1, started);
            Assert.IsTrue(run.InterferencePending);
            float stalled = run.StepProgress;
            Tick(run, 0.5f, held: false);
            Assert.AreEqual(stalled, run.StepProgress, 1e-4f, "Interference stalls the extraction.");
            Tick(run, 0.6f, held: false);       // let it time out
            CollectionAssert.AreEqual(new[] { false }, resolved);
            Assert.Less(run.StepProgress, stalled - 0.1f, "Ignored interference rolls progress back.");

            // Answered in time: no rollback.
            run = Run(new InteractionStep("EXTRACT", InteractionStepKind.Sustain, 4f).WithInterference(new[] { 0.5f }, 1f, 0.2f));
            bool done = false;
            run.Completed += () => done = true;
            for (int i = 0; i < 1000 && !done; i++) run.Tick(Dt, run.SuggestedInput(), true);
            Assert.IsTrue(done);
        }

        [Test]
        public void LeavingCancelsBackToTheLastCheckpoint()
        {
            var run = Run(new InteractionStep("A", InteractionStepKind.Auto, 0.5f),
                          new InteractionStep("B", InteractionStepKind.Auto, 0.5f, checkpoint: true),
                          new InteractionStep("C", InteractionStepKind.Auto, 2f));
            string cancelled = null;
            run.Cancelled += r => cancelled = r;
            run.Tick(Dt, true, true);
            Tick(run, 1.2f, held: false);
            Assert.AreEqual(2, run.StepIndex);
            Tick(run, InteractionRun.CancelGrace * 0.5f, held: false, engaged: false);
            Assert.AreEqual(InteractionRun.RunState.Running, run.State, "A brief wobble is forgiven.");
            Tick(run, InteractionRun.CancelGrace + 0.1f, held: false, engaged: false);
            Assert.AreEqual(InteractionRun.RunState.Idle, run.State);
            Assert.IsNotNull(cancelled);
            run.Tick(Dt, true, true);
            Assert.AreEqual(1, run.StepIndex, "Restarts from the checkpoint stage, not the beginning.");
        }

        [Test]
        public void CuesFireOnceAtTheirProgress()
        {
            var run = Run(new InteractionStep("EXTRACT", InteractionStepKind.Auto, 2f).WithCues(new InteractionCue(0.25f, "quarter"), new InteractionCue(0.75f, "late")));
            var events = new List<string>();
            run.Event += events.Add;
            run.Tick(Dt, true, true);
            Tick(run, 1f, held: false);
            CollectionAssert.AreEqual(new[] { "quarter" }, events);
            Tick(run, 1.1f, held: false);
            CollectionAssert.AreEqual(new[] { "quarter", "late" }, events);
        }

        [Test]
        public void ObjectiveStartDelayHoldsTheNextObjective()
        {
            var m = ScriptableObject.CreateInstance<MissionDefinition>();
            try
            {
                m.EditorConfigure("t", "T", "", "", "S", new List<ObjectiveDefinition>
                    {
                        new("take", ObjectiveKind.Interact, "core", "TAKE", "", eventsOnComplete: new[] { "acquired" }),
                        new("run", ObjectiveKind.Reach, "exit", "RUN", "", SecurityLevel.Lockdown, 60f, "X", new[] { "lockdown" }, startDelay: 2f),
                    }, new string[0], new string[0], new string[0], new List<MissionAnnouncement>(), 25f, 30f);
                var p = new MissionProgress(m);
                var events = new List<string>();
                p.WorldEvent += events.Add;
                p.Start();
                Assert.IsTrue(p.NotifyInteracted("core"));
                Assert.IsTrue(p.WaitingForObjective);
                Assert.AreEqual(SecurityLevel.Calm, p.Security, "Security waits for the delayed objective.");
                Assert.IsFalse(p.NotifyReached("exit"), "Nothing is current during the beat.");
                CollectionAssert.AreEqual(new[] { "acquired" }, events);
                for (int i = 0; i < 90; i++) p.Tick(0.02f);
                Assert.IsTrue(p.WaitingForObjective);
                for (int i = 0; i < 20; i++) p.Tick(0.02f);
                Assert.IsFalse(p.WaitingForObjective);
                Assert.AreEqual(SecurityLevel.Lockdown, p.Security);
                Assert.IsTrue(p.HasTimer);
                CollectionAssert.AreEqual(new[] { "acquired", "lockdown" }, events);
            }
            finally { Object.DestroyImmediate(m); }
        }
    }
}
