using System.Collections.Generic;
using System.Linq;
using NeonRift.EditorTools.District;
using NeonRift.Game;
using NeonRift.Gameplay;
using NeonRift.Missions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonRift.Tests
{
    /// <summary>Mission state machine (pure logic) and the Night Run data/scene wiring.</summary>
    public class MissionTests
    {
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        private MissionDefinition Mission(float escapeTime = 80f, float penalty = 25f, float minimum = 30f)
        {
            var m = ScriptableObject.CreateInstance<MissionDefinition>();
            created.Add(m);
            m.EditorConfigure("test", "Test", "", "", "Test", new List<ObjectiveDefinition>
                {
                    new("reach", ObjectiveKind.Reach, "zone", "REACH", ""),
                    new("hack", ObjectiveKind.Interact, "core", "HACK", "", SecurityLevel.Calm, 0f, null, null, new[] { "breached", "lockdown" }),
                    new("escape", ObjectiveKind.Reach, "exit", "ESCAPE", "", SecurityLevel.Lockdown, escapeTime, "TRACED", new[] { "escape.start" }),
                },
                new[] { "start" }, new[] { "complete" }, new[] { "failed" }, new List<MissionAnnouncement>(), penalty, minimum);
            return m;
        }

        [Test]
        public void ObjectivesAdvanceInOrderAndFireWorldEvents()
        {
            var progress = new MissionProgress(Mission());
            var events = new List<string>();
            progress.WorldEvent += events.Add;
            progress.Start();
            Assert.AreEqual("reach", progress.Current.Id);
            Assert.AreEqual(new[] { "start" }, events.ToArray());

            Assert.IsFalse(progress.NotifyInteracted("core"), "Interacting with a later target must not skip ahead.");
            Assert.IsFalse(progress.NotifyReached("exit"));
            Assert.IsTrue(progress.NotifyReached("zone"));
            Assert.AreEqual("hack", progress.Current.Id);
            Assert.IsTrue(progress.IsCurrentTarget("core"));

            Assert.IsTrue(progress.NotifyInteracted("core"));
            Assert.AreEqual("escape", progress.Current.Id);
            Assert.AreEqual(SecurityLevel.Lockdown, progress.Security);
            CollectionAssert.AreEqual(new[] { "start", "breached", "lockdown", "escape.start" }, events);
            Assert.IsTrue(progress.HasTimer);
            Assert.AreEqual(80f, progress.TimeRemaining, 1e-4f);

            Assert.IsTrue(progress.NotifyReached("exit"));
            Assert.AreEqual(MissionPhase.Completed, progress.Phase);
            Assert.IsNull(progress.Current);
            Assert.AreEqual("complete", events.Last());
            Assert.AreEqual(MissionOutcome.Completed, progress.ToResult().Outcome);
            Assert.AreEqual((int)SecurityLevel.Lockdown, progress.ToResult().SecurityLevelReached);
        }

        [Test]
        public void HeatRaisesAlertAndShortensTheEscapeTimer()
        {
            var progress = new MissionProgress(Mission());
            var levels = new List<SecurityLevel>();
            progress.SecurityChanged += levels.Add;
            progress.Start();
            progress.AddHeat(0.4f, "gate");
            Assert.AreEqual(SecurityLevel.Alert, progress.Security);
            Assert.AreEqual(0.4f, progress.Heat, 1e-5f);

            progress.NotifyReached("zone");
            progress.NotifyInteracted("core");
            Assert.AreEqual(80f - 0.4f * 25f, progress.TimeRemaining, 1e-4f, "Heat carried into the escape costs time up front.");

            float before = progress.TimeRemaining;
            progress.AddHeat(0.2f, "gate again");
            Assert.AreEqual(before - 0.2f * 25f, progress.TimeRemaining, 1e-4f, "Heat gained during the escape costs time immediately.");
            CollectionAssert.AreEqual(new[] { SecurityLevel.Alert, SecurityLevel.Lockdown }, levels);
            Assert.AreEqual(SecurityLevel.Lockdown, progress.Security, "Security never de-escalates.");
        }

        [Test]
        public void HeatIsClampedAndTimerRespectsMinimum()
        {
            var progress = new MissionProgress(Mission(escapeTime: 40f, penalty: 30f, minimum: 25f));
            progress.Start();
            progress.AddHeat(0.8f, "a");
            progress.AddHeat(0.8f, "b");
            Assert.AreEqual(1f, progress.Heat, 1e-6f);
            progress.NotifyReached("zone");
            progress.NotifyInteracted("core");
            Assert.AreEqual(25f, progress.TimeRemaining, 1e-4f);
        }

        [Test]
        public void TimerExpiryFailsTheMission()
        {
            var progress = new MissionProgress(Mission(escapeTime: 50f));
            var events = new List<string>();
            progress.WorldEvent += events.Add;
            progress.Start();
            progress.NotifyReached("zone");
            progress.NotifyInteracted("core");
            for (int i = 0; i < 49; i++) progress.Tick(1f);
            Assert.AreEqual(MissionPhase.Running, progress.Phase);
            progress.Tick(1.5f);
            Assert.AreEqual(MissionPhase.Failed, progress.Phase);
            Assert.AreEqual("TRACED", progress.FailReason);
            Assert.AreEqual("failed", events.Last());
            Assert.IsFalse(progress.NotifyReached("exit"), "A failed mission cannot be completed afterwards.");
            Assert.AreEqual(MissionOutcome.Failed, progress.ToResult().Outcome);
        }

        [Test]
        public void UntimedObjectivesNeverFail()
        {
            var progress = new MissionProgress(Mission());
            progress.Start();
            for (int i = 0; i < 1000; i++) progress.Tick(1f);
            Assert.AreEqual(MissionPhase.Running, progress.Phase);
            Assert.AreEqual(1000f, progress.Elapsed, 1e-3f);
        }

        [Test]
        public void NightRunMissionDataIsValid()
        {
            var mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(NightRunBuilder.MissionPath);
            Assert.IsNotNull(mission, "Mission_NightRun is missing; run Neon Rift ▸ Night Run ▸ Build.");
            CollectionAssert.IsEmpty(mission.Validate());
            Assert.AreEqual("NightRun", mission.SceneName);
            Assert.IsFalse(mission.DevelopmentOnly);
            // Infiltrate → disable facility security → extract the core → (beat) → escape.
            CollectionAssert.AreEqual(new[] { ObjectiveKind.Reach, ObjectiveKind.Interact, ObjectiveKind.Interact, ObjectiveKind.Reach },
                                      mission.Objectives.Select(o => o.Kind).ToArray());
            var escape = mission.Objectives[3];
            Assert.AreEqual(SecurityLevel.Lockdown, escape.SecurityLevel);
            Assert.Greater(escape.TimeLimit, 0f);
            Assert.Greater(escape.StartDelay, 1f, "DATA ACQUIRED needs a beat before SECURITY BREACH DETECTED.");
            CollectionAssert.Contains(escape.EventsOnStart, NightRunBuilder.EventLockdown);
            CollectionAssert.Contains(mission.Objectives[2].EventsOnComplete, NightRunBuilder.EventCoreAcquired);
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:GameConfig")[0]));
            Assert.AreEqual(mission, config.DefaultMission, "Car Select should launch Night Run.");
        }

        [Test]
        public void NightRunSceneProvidesEveryTargetAndReaction()
        {
            var mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(NightRunBuilder.MissionPath);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenScene(NightRunBuilder.ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var targets = roots.SelectMany(r => r.GetComponentsInChildren<IMissionTarget>(true)).ToList();
                foreach (var o in mission.Objectives)
                    Assert.AreEqual(1, targets.Count(t => t.Id == o.TargetId), $"Objective '{o.Id}' needs exactly one target '{o.TargetId}'.");

                var entry = roots.SelectMany(r => r.GetComponents<MissionSceneEntry>()).Single();
                Assert.IsNotNull(entry.Director, "MissionSceneEntry must reference the MissionDirector.");
                var so = new SerializedObject(entry);
                foreach (var field in new[] { "spawnPoint", "chaseCamera", "director", "viewCamera" })
                    Assert.IsNotNull(so.FindProperty(field).objectReferenceValue, $"MissionSceneEntry.{field} is not set.");
                var dso = new SerializedObject(entry.Director);
                foreach (var field in new[] { "hud", "missionAudio", "alertOrigin" })
                    Assert.IsNotNull(dso.FindProperty(field).objectReferenceValue, $"MissionDirector.{field} is not set.");

                // Lockdown must change routes: gates that close on it, the alley gate that can be reopened, and the
                // skyway gate the crew opens when the escape starts (an alternate route).
                var barriers = roots.SelectMany(r => r.GetComponentsInChildren<SecurityBarrier>(true)).ToList();
                Assert.GreaterOrEqual(barriers.Count, 5);
                bool Listens(SerializedObject o, string list, string id) =>
                    Enumerable.Range(0, o.FindProperty(list).arraySize).Any(i => o.FindProperty(list).GetArrayElementAtIndex(i).stringValue == id);
                int opening = 0;
                foreach (var b in barriers)
                {
                    var bso = new SerializedObject(b);
                    bool closes = Listens(bso, "closeOn", NightRunBuilder.EventLockdown);
                    bool opens = Listens(bso, "openOn", NightRunBuilder.EventEscape);
                    if (opens) opening++;
                    Assert.IsTrue(closes || opens, $"{b.name} should react to the theft (close on lockdown or open on escape).");
                    var panels = bso.FindProperty("panels");
                    for (int i = 0; i < panels.arraySize; i++)
                    {
                        var body = panels.GetArrayElementAtIndex(i).FindPropertyRelative("body").objectReferenceValue as Rigidbody;
                        Assert.IsNotNull(body, $"{b.name} panel {i} has no body.");
                        Assert.IsTrue(body.isKinematic);
                    }
                }
                Assert.GreaterOrEqual(opening, 1, "The lockdown should open at least one alternate route.");

                // City and rivals are wired: navigation knows every gate, rivals have slots and markers, lamps are budgeted.
                var nav = roots.SelectMany(r => r.GetComponentsInChildren<CityNavigation>(true)).Single();
                Assert.IsNotNull(nav.Network);
                Assert.That(nav.Network.Validate(), Is.Empty);
                Assert.AreEqual(barriers.Count, new SerializedObject(nav).FindProperty("blockers").arraySize, "Every gate must be known to navigation.");
                var rivals = roots.SelectMany(r => r.GetComponentsInChildren<RivalDirector>(true)).Single();
                var rso = new SerializedObject(rivals);
                Assert.GreaterOrEqual(rso.FindProperty("slots").arraySize, mission.MaxRivals);
                var markerIds = roots.SelectMany(r => r.GetComponentsInChildren<RaceMarker>(true)).Select(m => m.Id).ToList();
                foreach (var o in mission.Objectives)
                    if (!string.IsNullOrEmpty(o.RivalGoalId)) Assert.Contains(o.RivalGoalId, markerIds);
                Assert.IsNotNull(new SerializedObject(entry).FindProperty("rivals").objectReferenceValue);
                var budget = roots.SelectMany(r => r.GetComponentsInChildren<LightBudget>(true)).Single();
                Assert.Greater(budget.Count, 300);

                var terminal = targets.OfType<Interactable>().FirstOrDefault(i => i.Id == NightRunBuilder.GateTerminalId)
                               ?? roots.SelectMany(r => r.GetComponentsInChildren<Interactable>(true)).Single(i => i.Id == NightRunBuilder.GateTerminalId);
                Assert.IsNotNull(terminal.Definition);
                Assert.Greater(terminal.Definition.HeatOnComplete, 0f, "The shortcut gate must have a consequence (heat).");

                // Triggers are on the Trigger layer so only vehicles set them off.
                int trigger = LayerMask.NameToLayer("Trigger");
                foreach (var t in targets.OfType<Component>())
                    Assert.AreEqual(trigger, t.gameObject.layer, $"{t.name} must be on the Trigger layer.");
                Assert.Greater(roots.SelectMany(r => r.GetComponentsInChildren<SecurityLightGroup>(true)).Count(), 5);
                Assert.AreEqual(1, roots.SelectMany(r => r.GetComponentsInChildren<SecurityPostEffects>(true)).Count());

                // The heist: the terminal and the uplink are multi-stage, and the chamber and terminal screens perform them.
                var all = roots.SelectMany(r => r.GetComponentsInChildren<Interactable>(true)).ToList();
                var coreTerminal = all.Single(i => i.Id == NightRunBuilder.CoreTerminalId);
                var uplink = all.Single(i => i.Id == NightRunBuilder.CoreUplinkId);
                Assert.IsTrue(coreTerminal.Definition.IsSequence && uplink.Definition.IsSequence);
                Assert.IsTrue(uplink.Definition.Steps.Any(s => s.Kind == InteractionStepKind.Sustain && s.Interference.Length > 0), "Extraction needs interference.");
                Assert.IsTrue(coreTerminal.Definition.Steps.Any(s => s.Kind == InteractionStepKind.Timing), "The bypass needs a skill stage.");
                var chamber = roots.SelectMany(r => r.GetComponentsInChildren<DataCoreChamber>(true)).Single();
                var cso = new SerializedObject(chamber);
                Assert.AreEqual(uplink, cso.FindProperty("uplink").objectReferenceValue);
                foreach (var field in new[] { "cable", "chaseCamera", "hum", "stream", "openClip", "hologramRoot" })
                    Assert.IsNotNull(cso.FindProperty(field).objectReferenceValue, $"DataCoreChamber.{field} is not set.");
                Assert.AreEqual(6, cso.FindProperty("sleevePanels").arraySize);
                Assert.Greater(cso.FindProperty("pulses").arraySize, 10);
                var displays = roots.SelectMany(r => r.GetComponentsInChildren<TerminalDisplay>(true)).ToList();
                Assert.IsTrue(displays.Any(d => new SerializedObject(d).FindProperty("source").objectReferenceValue == coreTerminal));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
    }
}
