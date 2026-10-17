using System.Linq;
using NeonRift.Campaign.EditorTools;
using NeonRift.Game;
using NeonRift.HarborRain;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NeonRift.Campaign.Tests
{
    /// <summary>The connected campaign's data, save and rules (no Play Mode).</summary>
    public sealed class CampaignTests
    {
        private static CampaignDefinition Def => AssetDatabase.LoadAssetAtPath<CampaignDefinition>(CampaignBuilder.DefinitionPath);
        private static GameConfig Config => AssetDatabase.LoadAssetAtPath<GameConfig>(CampaignBuilder.ConfigPath);
        private static GameConfig StandaloneConfig => AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_NeonRift/Data/Config/GameConfig.asset");

        /// <summary>The two Harbor Rain cars are local-only third-party models (not in the public repository).</summary>
        private static void RequireHarborCars()
        {
            if (Def.Level2Vehicles.Length < 2 || Def.Level2Vehicles.Any(v => v == null))
                Assert.Ignore("Harbor Rain cars not present in this checkout (third-party models kept out of the public repository; see Docs/ThirdParty.md).");
        }

        private static CampaignProgress Memory(out System.Func<string> stored)
        {
            string data = "";
            stored = () => data;
            return new CampaignProgress(() => data, s => data = s);
        }

        // ---------------- Save ----------------

        [Test]
        public void Progress_FreshSave_StartsLocked()
        {
            var p = Memory(out _);
            p.Load();
            Assert.IsFalse(p.NightRunCompleted || p.QualifiedForHarbor || p.PortalCompleted || p.HarborVehiclesUnlocked || p.HarborCompleted);
            Assert.IsNull(p.LoadProblem);
        }

        [Test]
        public void Progress_RoundTrips_AllFlagsAndCars()
        {
            var p = Memory(out var stored);
            p.Load();
            p.RecordNightRun(true, "mercedes_amg_gt3", "A", 101.5f, 1, 3);
            p.MarkPortalCompleted();
            p.UnlockHarborVehicles();
            p.SetLevel2Vehicle("hr_ferrari_f40_lb");
            p.RecordHarbor("S", 400f);
            var again = new CampaignProgress(stored, _ => { });
            again.Load();
            Assert.IsTrue(again.NightRunCompleted && again.QualifiedForHarbor && again.PortalCompleted && again.HarborVehiclesUnlocked && again.HarborCompleted);
            Assert.AreEqual("mercedes_amg_gt3", again.State.level1Vehicle);
            Assert.AreEqual("hr_ferrari_f40_lb", again.State.level2Vehicle);
            Assert.AreEqual("A", again.State.nightRunGrade);
            Assert.AreEqual(1, again.State.harborClears);
        }

        [Test]
        public void Progress_NotQualified_DoesNotUnlockHarbor()
        {
            var p = Memory(out _);
            p.Load();
            p.RecordNightRun(false, "mercedes_sls_gt3", "B", 130f, 3, 3);
            p.UnlockHarborVehicles();
            Assert.IsTrue(p.NightRunCompleted);
            Assert.IsFalse(p.QualifiedForHarbor);
            Assert.IsFalse(p.HarborAvailable);
            Assert.IsFalse(p.HarborVehiclesUnlocked, "the cars unlock only after qualifying");
        }

        [TestCase("{not json")]
        [TestCase("{\"version\":99,\"harborCompleted\":true}")]
        public void Progress_InvalidData_FallsBackToFresh(string bad)
        {
            var p = new CampaignProgress(() => bad, _ => { });
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            p.Load();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.IsNotNull(p.LoadProblem);
            Assert.IsFalse(p.HarborCompleted);
        }

        [Test]
        public void Progress_PartialSave_IsMadeConsistent()
        {
            var p = new CampaignProgress(() => "{\"version\":1,\"harborCompleted\":true}", _ => { });
            p.Load();
            Assert.IsTrue(p.HarborVehiclesUnlocked && p.QualifiedForHarbor && p.NightRunCompleted);
        }

        // ---------------- Cars ----------------

        [Test]
        public void Selections_ShowFiveRealCars()
        {
            var def = Def;
            Assert.IsNotNull(def);
            RequireHarborCars();
            foreach (var cat in new[] { def.Level1Selection, def.Level2Selection })
            {
                Assert.AreEqual(5, cat.Count);
                foreach (var v in cat.Vehicles)
                {
                    Assert.IsNotNull(v);
                    Assert.IsNotNull(v.ShowroomPrefab, v.Id + " showroom prefab");
                    Assert.IsNotNull(v.GameplayPrefab, v.Id + " gameplay prefab");
                    Assert.IsFalse(string.IsNullOrEmpty(v.DisplayName));
                }
            }
            Assert.AreEqual(2, def.Level2Vehicles.Length);
            foreach (var v in StandaloneConfig.VehicleCatalog.Vehicles) Assert.IsTrue(def.Level1Selection.Vehicles.Contains(v));
        }

        [Test]
        public void StandaloneCatalog_KeepsOnlyTheNightRunCars()
        {
            var standalone = StandaloneConfig.VehicleCatalog;
            Assert.AreEqual(3, standalone.Count);
            foreach (var v in Def.Level2Vehicles) Assert.IsFalse(standalone.Vehicles.Contains(v), v.Id + " must not be in the standalone catalog");
            Assert.IsNull(StandaloneConfig.Campaign, "the standalone config has no campaign");
        }

        [Test]
        public void LockRules_Level2CarsOnlyInLevel2AfterQualifying()
        {
            RequireHarborCars();
            var p = Memory(out _);
            p.Load();
            var s = new CampaignSession(Def, p);
            var f40 = Def.Level2Vehicles[0];
            var nightRunCar = StandaloneConfig.VehicleCatalog.Vehicles[0];
            s.Step = CampaignStep.Level1;
            Assert.IsTrue(s.IsLocked(f40, out _));
            Assert.IsFalse(s.IsLocked(nightRunCar, out _));
            s.Step = CampaignStep.Level2Select;
            Assert.IsTrue(s.IsLocked(f40, out _), "locked in Level 2 until qualified");
            p.RecordNightRun(true, nightRunCar.Id, "A", 100f, 1, 3);
            p.UnlockHarborVehicles();
            Assert.IsFalse(s.IsLocked(f40, out _), "unlocked for Level 2");
            s.Step = CampaignStep.Level1;
            Assert.IsTrue(s.IsLocked(f40, out _), "still Level-2-only in the Level 1 selection");
            Assert.AreEqual(Def.Level1, new CampaignSession(Def, p) { Step = CampaignStep.Level1 }.SelectionMission);
            Assert.AreEqual(Def.Level2, new CampaignSession(Def, p) { Step = CampaignStep.Level2Select }.SelectionMission);
        }

        // ---------------- Config, scenes, build ----------------

        [Test]
        public void CampaignConfig_HasBothMissionsAndItsOwnIntro()
        {
            var c = Config;
            Assert.IsNotNull(c.Campaign);
            Assert.IsNotNull(c.FindMissionForScene("NightRun"));
            Assert.AreEqual("harbor_rain", c.FindMissionForScene("HarborRain").Id);
            Assert.AreNotSame(StandaloneConfig.Intro, c.Intro, "the campaign remembers 'intro seen' separately");
            Assert.AreSame(StandaloneConfig.VehicleCatalog, c.VehicleCatalog, "rival crews stay the Night Run cars");
        }

        [Test]
        public void BuildList_KeepsBootstrapFirst_AndCampaignApkStartsWithCampaignBootstrap()
        {
            Assert.AreEqual("Assets/_NeonRift/Scenes/Bootstrap.unity", EditorBuildSettings.scenes[0].path);
            Assert.AreEqual(CampaignBuilder.ScenePath, CampaignAndroidBuild.Scenes[0]);
            foreach (var s in CampaignAndroidBuild.Scenes) Assert.IsTrue(System.IO.File.Exists(s), s);
            Assert.IsFalse(NeonRift.EditorTools.Validation.AndroidBuild.NightRunScenes.Contains(CampaignBuilder.ScenePath));
            Assert.IsFalse(NeonRift.EditorTools.Validation.AndroidBuild.NightRunScenes.Contains(CampaignBuilder.HarborScenePath));
        }

        [Test]
        public void CampaignBootstrap_UsesTheCampaignConfig()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(CampaignBuilder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<GameRoot>(true)).First(r => r != null);
                Assert.AreEqual(Config, new SerializedObject(root).FindProperty("config").objectReferenceValue);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        }

        // ---------------- Subtitles ----------------

        [Test]
        public void EveryCampaignLine_IsVoicedAndSubtitledWithTheSameText()
        {
            var def = Def;
            Assert.AreEqual(CampaignBuilder.Lines.Length, def.Lines.Length);
            foreach (var (id, speaker, text, _) in CampaignBuilder.Lines)
            {
                Assert.IsTrue(def.TryGetLine(id, out var line), id);
                Assert.AreEqual(text, line.text, id + ": subtitle text is the generated script");
                Assert.AreEqual(speaker, line.speaker);
            }
            // The recordings are generated locally (macOS System Voices: personal use only, never published).
            if (def.Lines.All(l => l.clip == null))
                Assert.Ignore("Voice recordings not present in this checkout (generated locally on macOS; see Docs/ThirdParty.md). Subtitle text verified.");
            foreach (var line in def.Lines)
            {
                Assert.IsNotNull(line.clip, line.id + " has audio");
                Assert.Greater(line.clip.length, 1f, line.id);
            }
        }

        [Test]
        public void EveryLineTheCampaignSpeaks_Exists()
        {
            // Ids used by the runtime (teaser, portal, arrival, unlock, trailer shots, briefing, results).
            foreach (var id in new[] { "teaser", "portal", "arrival", "unlock", "trailer_port", "trailer_target", "trailer_crew", "trailer_security",
                                       "trailer_heist", "trailer_alarm", "trailer_escape", "trailer_briefing", "complete" })
                Assert.IsTrue(Def.TryGetLine(id, out _), id);
        }

        [Test]
        public void HarborRainRadio_EveryLineHasTextAndAudio()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(CampaignBuilder.HarborScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var radio = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<HarborRadio>(true)).First(r => r != null);
                var lines = new SerializedObject(radio).FindProperty("lines");
                Assert.Greater(lines.arraySize, 40);
                int voiced = 0;
                for (int i = 0; i < lines.arraySize; i++)
                {
                    var l = lines.GetArrayElementAtIndex(i);
                    string id = l.FindPropertyRelative("id").stringValue;
                    Assert.IsFalse(string.IsNullOrEmpty(l.FindPropertyRelative("text").stringValue), id + " subtitle text");
                    if (l.FindPropertyRelative("clip").objectReferenceValue != null) voiced++;
                }
                // Recordings are local-only (macOS System Voices); when present, every line must have one.
                if (voiced == 0) Assert.Ignore("Radio recordings not present in this checkout (generated locally on macOS). Subtitle text verified for every line.");
                Assert.AreEqual(lines.arraySize, voiced, "every radio line has its recording");
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void Subtitles_SettingGatesTheOverlay_AndClearRemovesTheLine()
        {
            bool was = Subtitles.Enabled;
            int shown = 0;
            void OnShown(string a, string b, float c) => shown++;
            Subtitles.Shown += OnShown;
            try
            {
                Subtitles.Enabled = true;
                Subtitles.Show("CONTROL", "test line", 2f);
                Assert.AreEqual("test line", Subtitles.Current);
                Subtitles.Clear();
                Assert.IsNull(Subtitles.Current);
                Subtitles.Enabled = false;
                Subtitles.Show("CONTROL", "hidden", 2f);
                Assert.AreEqual(1, shown);
                Assert.IsNull(Subtitles.Current);
                Assert.AreEqual("hidden", Subtitles.Log[^1].Text, "still logged");
            }
            finally
            {
                Subtitles.Shown -= OnShown;
                Subtitles.Enabled = was;
            }
        }

        [Test]
        public void TeaserAndPortalAssets_Present()
        {
            var def = Def;
            Assert.GreaterOrEqual(def.TeaserFrames.Length, 5);
            Assert.AreEqual(def.TeaserFrames.Length, def.TeaserCaptions.Length);
            Assert.IsNotNull(def.PortalRing);
            Assert.IsNotNull(def.PortalCore);
            Assert.IsNotNull(def.PortalSparks);
            Assert.AreEqual("NeonRift/Portal", def.PortalCore.shader.name);
            Assert.IsTrue(def.PortalCore.shader.isSupported);
            Assert.IsNotNull(def.PortalOpen);
            Assert.IsNotNull(def.PortalHum);
            Assert.AreNotEqual(Vector3.zero, def.NightRunPortalStage);
        }
    }
}
