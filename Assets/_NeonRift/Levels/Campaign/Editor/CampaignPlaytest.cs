using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NeonRift.Frontend;
using NeonRift.Game;
using NeonRift.Gameplay;
using NeonRift.HarborRain;
using NeonRift.Intro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Campaign.EditorTools
{
    /// <summary>
    /// Automated campaign playtests in Play Mode (development tool; nothing here ships). Each scenario enters Play Mode
    /// from a bootstrap scene, drives the real flow through the same entry points a player uses (skip, confirm, car
    /// selection, the Night Run validator's autopilot, Harbor Rain's review autopilot), records what happened and saves
    /// Game-view screenshots to <c>Logs/Campaign/shots</c>; the report goes to <c>Logs/Campaign/playtest_&lt;scenario&gt;.txt</c>.
    /// Scenarios: "full" (fresh save: opening + teaser → Level 1 select → Night Run → portal → arrival → Level 2 select →
    /// trailer → Harbor Rain → campaign results), "skip" (dev entry → select → skip the trailer → state check),
    /// "standalone" (the original Bootstrap: no campaign, no teaser, three cars), "relaunch" (saved progress after a restart).
    /// </summary>
    [InitializeOnLoad]
    public static class CampaignPlaytest
    {
        private const string Key = "NeonRift.Campaign.Playtest";
        public const string Dir = "Logs/Campaign";
        public const string ShotDir = Dir + "/shots";

        static CampaignPlaytest() => EditorApplication.update += Poll;

        [MenuItem("Neon Rift/Campaign/Playtest: Full Campaign (fresh save)")] private static void MenuFull() => Debug.Log(Start("full"));
        [MenuItem("Neon Rift/Campaign/Playtest: Skip Trailer + Subtitles")] private static void MenuSkip() => Debug.Log(Start("skip", false));
        [MenuItem("Neon Rift/Campaign/Playtest: Original Night Run Bootstrap")] private static void MenuStandalone() => Debug.Log(Start("standalone"));
        [MenuItem("Neon Rift/Campaign/Playtest: Saved Progress After Restart")] private static void MenuRelaunch() => Debug.Log(Start("relaunch", false));
        [MenuItem("Neon Rift/Campaign/Playtest: Without Harbor Rain Cars")] private static void MenuMissing() => Debug.Log(Start("missingcars", false));

        public static string Start(string scenario, bool freshSave = true)
        {
            if (EditorApplication.isPlaying) return "already playing";
            Directory.CreateDirectory(ShotDir);
            if (File.Exists($"{Dir}/playtest_{scenario}.txt")) File.Delete($"{Dir}/playtest_{scenario}.txt");
            if (File.Exists($"{Dir}/done_{scenario}.txt")) File.Delete($"{Dir}/done_{scenario}.txt");
            if (freshSave && scenario is "full")
            {
                PlayerPrefs.DeleteKey(CampaignProgress.Key);
                PlayerPrefs.DeleteKey("NeonRift.Campaign.IntroSeen");
            }
            if (scenario == "standalone") PlayerPrefs.DeleteKey("NeonRift.IntroSeen");
            PlayerPrefs.Save();
            string scene = scenario == "standalone" ? "Assets/_NeonRift/Scenes/Bootstrap.unity" : CampaignBuilder.ScenePath;
            EditorSceneManager.OpenScene(scene);
            SessionState.SetString(Key, scenario);
            EditorApplication.EnterPlaymode();
            return $"entering play for {scenario} ({scene})";
        }

        private static void Poll()
        {
            string scenario = SessionState.GetString(Key, "");
            if (scenario.Length == 0 || !EditorApplication.isPlaying) return;
            SessionState.EraseString(Key);
            Application.runInBackground = true;
            var go = new GameObject("CampaignPlaytest");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Begin(scenario);
        }

        /// <summary>The scenario driver (lives across scene loads).</summary>
        public sealed class Runner : MonoBehaviour
        {
            private string scenario;
            private readonly StringBuilder log = new();
            private int pass, fail;
            private float t0;

            public void Begin(string which)
            {
                scenario = which;
                t0 = Time.realtimeSinceStartup;
                Application.logMessageReceived += OnLog;
                StartCoroutine(Run());
            }

            private void OnDestroy() => Application.logMessageReceived -= OnLog;

            private readonly List<string> errors = new();

            private void OnLog(string message, string stack, LogType type)
            {
                if (type is LogType.Error or LogType.Exception && errors.Count < 40) errors.Add(message.Split('\n')[0]);
                if (message.StartsWith("[Campaign]") || message.StartsWith("[Intro]") || message.StartsWith("[CarSelect]") || message.StartsWith("[HarborRain] campaign"))
                    Note("log: " + message.Split('\n')[0]);
            }

            private void Note(string text) => log.AppendLine($"{Time.realtimeSinceStartup - t0,7:0.0}s  {text}");

            private void Check(bool ok, string what)
            {
                if (ok) pass++; else fail++;
                Note($"{(ok ? "PASS" : "FAIL")}  {what}");
            }

            private IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot($"{ShotDir}/{scenario}_{name}.png");
                Note($"shot {name}");
                yield return null;
            }

            private IEnumerator Until(Func<bool> condition, float timeout, string what)
            {
                // Scene loads in the editor (first load after a recompile, shader variants) can take a minute or more.
                timeout = Mathf.Max(timeout, 150f);
                float start = Time.realtimeSinceStartup;
                while (!condition() && Time.realtimeSinceStartup - start < timeout) yield return null;
                bool ok = condition();
                if (!ok) Check(false, $"timed out waiting for {what} ({timeout:0}s)");
            }

            private static IEnumerator Wait(float s)
            {
                float start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < s) yield return null;
            }

            private static GameRoot Root => FindAnyObjectByType<GameRoot>();

            private static GameContext Context
            {
                get
                {
                    var root = Root;
                    if (root == null) return null;
                    var flow = typeof(GameRoot).GetField("flow", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(root);
                    return flow?.GetType().GetField("context", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(flow) as GameContext;
                }
            }

            private IEnumerator Run()
            {
                Note($"scenario {scenario}");
                yield return Until(() => Context != null, 20f, "game context");
                switch (scenario)
                {
                    case "full": yield return Full(); break;
                    case "skip": yield return SkipTrailer(); break;
                    case "standalone": yield return Standalone(); break;
                    case "relaunch": yield return Relaunch(); break;
                    case "missingcars": yield return MissingCars(); break;
                }
                Note($"console errors: {errors.Count}");
                foreach (var e in errors) Note("  error: " + e);
                string text = $"CAMPAIGN PLAYTEST {scenario}: {pass} passed, {fail} failed, {errors.Count} console errors\n\n" + log;
                File.WriteAllText($"{Dir}/playtest_{scenario}.txt", text);
                File.WriteAllText($"{Dir}/done_{scenario}.txt", $"{pass} {fail}");
                Debug.Log($"[CampaignPlaytest] {scenario}: {pass} passed, {fail} failed");
                Destroy(gameObject);
            }

            // ---------------- Scenarios ----------------

            private IEnumerator Opening(bool expectTeaser)
            {
                var ctx = Context;
                IntroSceneEntry intro = null;
                yield return Until(() => (intro = FindAnyObjectByType<IntroSceneEntry>()) != null && intro.IsPlaying, 180f, "the opening");
                if (intro == null) yield break;
                Check(true, "opening plays on a fresh save");
                yield return Wait(5f);
                yield return Shot("a1_opening");
                Note("skip pressed (as Space / Start)");
                intro.SkipToTitle();
                yield return Wait(0.3f);
                if (expectTeaser)
                {
                    Check(intro.TeaserRunning, "skip lands on the Harbor Rain teaser (campaign)");
                    yield return Wait(1.6f);
                    yield return Shot("a2_teaser_cranes");
                    yield return Wait(2.2f);
                    Check(Subtitles.Current != null && Subtitles.Current.Contains("Kowloon Bay"), $"teaser line subtitled ('{Subtitles.Current}')");
                    yield return Shot("a3_teaser_subtitle");
                    yield return Wait(2.6f);
                    yield return Shot("a4_teaser_late");
                    yield return Until(() => !intro.TeaserRunning, 20f, "teaser end");
                    Check(intro.TeaserPlayed, "teaser played once");
                    Check(ctx.Campaign.Overlay != null && !ctx.Campaign.Overlay.TeaserVisible && !ctx.Campaign.Overlay.SubtitleVisible, "teaser layers and subtitle cleared at the title");
                }
                else Check(!intro.TeaserRunning && !intro.TeaserPlayed, "no teaser in the standalone opening");
                yield return Until(() => intro.PhaseName == "Title", 20f, "the title");
                yield return Wait(1.5f);
                yield return Shot("a5_title");
                intro.Confirm();
                yield return Until(() => FindAnyObjectByType<CarSelectScreen>() != null && !ctx.Flow.IsTransitioning, 120f, "Car Select");
                yield return Wait(3.5f);
            }

            private IEnumerator Full()
            {
                var ctx = Context;
                Check(ctx.Campaign != null, "campaign bootstrap creates the campaign");
                Check(!ctx.Campaign.Progress.NightRunCompleted && !ctx.Campaign.Progress.HarborVehiclesUnlocked, "fresh save: nothing unlocked");
                yield return Opening(expectTeaser: true);

                // Level 1 selection: five cars, the two Harbor Rain cars locked.
                var cs = FindAnyObjectByType<CarSelectScreen>();
                Check(cs != null && cs.Count == 5, $"Level 1 selection shows five cars ({cs?.Count})");
                if (cs == null) yield break;
                yield return Shot("b1_level1_select");
                cs.ShowIndex(3);
                yield return Wait(1.2f);
                Check(cs.CurrentLocked, $"{cs.Current?.Id} locked in Level 1");
                yield return Shot("b2_level1_locked_car");
                cs.ShowIndex(4);
                yield return Wait(0.8f);
                Check(cs.CurrentLocked, $"{cs.Current?.Id} locked in Level 1");
                cs.ConfirmCurrent();
                yield return Wait(0.5f);
                Check(!cs.IsStarting, "a locked car cannot be started");
                // The SLS AMG on the boulevard route: the validator's most reliable full run (needs a top-two finish to qualify).
                cs.ShowIndex(0);
                yield return Wait(1.2f);
                string l1 = cs.Current.Id;
                cs.ConfirmCurrent();
                yield return Until(() => FindAnyObjectByType<MissionSceneEntry>()?.Director?.Progress != null && !ctx.Flow.IsTransitioning, 60f, "Night Run");

                // Night Run, driven by the project's validator autopilot through the real objectives.
                var entry = FindAnyObjectByType<MissionSceneEntry>();
                var director = entry.Director;
                Check(ctx.Session.SelectedVehicle != null && ctx.Session.SelectedVehicle.Id == l1, $"Night Run uses the selected car ({l1})");
                Check(director.Progress.Definition == ctx.Campaign.Definition.Level1, "Level 1 is Night Run");
                yield return Wait(2f);
                yield return Shot("c1_nightrun_start");
                bool qualified = false;
                for (int attempt = 1; attempt <= 3 && !qualified; attempt++)
                {
                    yield return Until(() => !ctx.Flow.IsTransitioning && FindAnyObjectByType<MissionSceneEntry>()?.Director?.Player != null &&
                                             FindAnyObjectByType<MissionSceneEntry>().Director.Progress != null, 60f, "mission ready");
                    entry = FindAnyObjectByType<MissionSceneEntry>();
                    director = entry.Director;
                    var validator = FindAnyObjectByType<NightRunValidator>();
                    yield return Wait(1f);
                    Check(validator != null && validator.Begin(director, NightRunValidator.Scenario.BoulevardInAlleyOut), $"autopilot run {attempt} started");
                    yield return Until(() => director.ResultsShown, 420f, "Night Run results");
                    qualified = director.LastQualified && director.CampaignPortalReady;
                    Note($"run {attempt}: phase {director.Progress.Phase}, P{director.FinishPosition}, qualified {director.LastQualified}, grade {director.LastGrade}, portal ready {director.CampaignPortalReady}");
                    yield return Wait(1.5f);
                    yield return Shot($"c2_results_run{attempt}");
                    if (!qualified)
                    {
                        Check(!director.CampaignPortalReady, "no portal without a qualified success");
                        if (attempt < 3) { director.RestartMission(); yield return Wait(8f); }
                    }
                }
                Check(qualified, "Night Run completed and qualified");
                if (!qualified) yield break;
                Check(ctx.Campaign.Progress.NightRunCompleted && ctx.Campaign.Progress.QualifiedForHarbor, "save: Night Run completed + qualified");
                Check(!ctx.Campaign.Progress.HarborVehiclesUnlocked, "Harbor Rain cars not yet unlocked before the portal");

                // Continue → portal.
                typeof(MissionDirector).GetMethod("Continue", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(director, null);
                yield return Until(() => NightRunPortalSequence.Active != null, 5f, "portal sequence");
                var portal = NightRunPortalSequence.Active;
                string lastShot = "";
                while (NightRunPortalSequence.Active != null)
                {
                    if (portal.Shot != lastShot)
                    {
                        lastShot = portal.Shot;
                        Note($"portal shot {lastShot}");
                        if (lastShot is "card" or "ignite" or "approach" or "enter") { yield return Wait(lastShot == "card" ? 1.6f : lastShot == "ignite" ? 2.2f : 0.6f); yield return Shot($"d_portal_{lastShot}"); }
                    }
                    yield return null;
                    if (lastShot == "leaving") break;
                }
                Check(portal != null && portal.CarCrossed, "the car physically drove through the portal plane (no timeout, no skip)");

                // Arrival.
                HarborCampaign hc = null;
                yield return Until(() => (hc = HarborRainEntry.Active != null ? HarborRainEntry.Active.Campaign : null) != null, 90f, "Harbor Rain arrival");
                if (hc == null) yield break;
                Check(hc.Kind == HarborCampaign.Mode.Arrival && ctx.Campaign.Step == CampaignStep.Arrival, "arrival mode in Harbor Rain");
                Check(ctx.Campaign.Progress.PortalCompleted, "save: portal completed");
                string last = "";
                while (!hc.WaitingForConfirm)
                {
                    if (hc.Shot != last)
                    {
                        last = hc.Shot;
                        if (last == "emerge") { yield return Wait(0.5f); yield return Shot("e1_arrival_emerge"); }
                        if (last == "reveal") { yield return Wait(4f); yield return Shot("e2_arrival_reveal"); }
                    }
                    yield return null;
                }
                yield return Wait(7f);
                yield return Shot("e3_unlock_card");
                Check(ctx.Campaign.Progress.HarborVehiclesUnlocked, "save: Harbor Rain cars unlocked at the unlock card");
                Check(ctx.Campaign.Overlay.CardTitle == "A NEW OPERATION", "unlock card shown");
                hc.Confirm();
                yield return Until(() => FindAnyObjectByType<CarSelectScreen>() != null && !ctx.Flow.IsTransitioning, 60f, "Level 2 Car Select");
                yield return Wait(3.5f);

                // Level 2 selection: five cars, all available; step through each.
                cs = FindAnyObjectByType<CarSelectScreen>();
                Check(ctx.Campaign.Step == CampaignStep.Level2Select && cs.Count == 5, "Level 2 selection shows five cars");
                for (int i = 0; i < cs.Count; i++)
                {
                    cs.ShowIndex(i);
                    yield return Wait(1.1f);
                    Check(!cs.CurrentLocked, $"{cs.Current.Id} available in Level 2");
                    yield return Shot($"f{i + 1}_level2_{cs.Current.Id}");
                }
                cs.ShowIndex(3);
                yield return Wait(1f);
                string pick = cs.Current.Id;
                cs.ConfirmCurrent();

                // Trailer, watched to the end.
                yield return Until(() => HarborRainEntry.Active != null && HarborRainEntry.Active.Campaign != null && HarborRainEntry.Active.Campaign.Kind == HarborCampaign.Mode.Trailer, 90f, "Harbor Rain trailer");
                hc = HarborRainEntry.Active.Campaign;
                var mission = HarborRainEntry.Active.Mission;
                Check(HarborRainEntry.Active.Driving.PlayerVehicle != null && ctx.Session.SelectedVehicle.Id == pick, $"Harbor Rain spawns the selected car ({pick}: {HarborRainEntry.Active.Driving.PlayerVehicle?.name})");
                last = "";
                int subtitled = 0, shots = 0;
                while (!hc.Finished)
                {
                    if (hc.Shot != last)
                    {
                        last = hc.Shot;
                        shots++;
                        yield return Wait(last == "briefing" ? 3.5f : 2.2f);
                        if (Subtitles.Current != null) subtitled++;
                        Note($"trailer shot {last}: subtitle '{Subtitles.Current}'");
                        yield return Shot($"g_trailer_{last}");
                    }
                    yield return null;
                }
                Check(shots >= 8, $"trailer played {shots} shots including the briefing");
                Check(subtitled >= 7, $"narration subtitled in {subtitled} shots");
                yield return Wait(1.5f);
                Check(mission.Current == HarborRainMission.Phase.Enter && !mission.CarHeld, $"operation started after the trailer ({mission.Current})");
                Check(ctx.Campaign.Progress.State.level2Vehicle == pick, "save: Level 2 car recorded");
                yield return Shot("h1_mission_start");

                // Harbor Rain end to end: Harbor Rain's own review autopilot (heist → alarm → pursuit → extraction).
                HarborReviewShots.Run("campaign");
                yield return Until(() => mission.Current is HarborRainMission.Phase.Extracted or HarborRainMission.Phase.Failed, 900f, "Harbor Rain extraction");
                Note($"Harbor Rain ended: {mission.Current}, grade {mission.LastGrade}");
                Check(mission.Current == HarborRainMission.Phase.Extracted, "Harbor Rain completed (extracted)");
                yield return Wait(3f);
                yield return Shot("h2_harbor_results");
                Check(ctx.Campaign.Progress.HarborCompleted, "save: Harbor Rain completed");
                mission.CampaignContinue?.Invoke();
                yield return Wait(4.5f);
                yield return Shot("i1_campaign_results");
                Check(ctx.Campaign.Overlay.CardTitle == "CAMPAIGN COMPLETE" && ctx.Campaign.Step == CampaignStep.Complete, "campaign results shown");
                HarborRainEntry.Active.Campaign.Confirm();
                yield return Wait(1.5f);
                Check(ctx.Campaign.Overlay.MenuOpen && !ctx.Campaign.Overlay.CardVisible, "Confirm: the results card gives way to the next-step choices");
                yield return Shot("i2_campaign_choices");

                // Persistence: a fresh read of the save.
                var reread = new CampaignProgress();
                reread.Load();
                Check(reread.NightRunCompleted && reread.QualifiedForHarbor && reread.PortalCompleted && reread.HarborVehiclesUnlocked && reread.HarborCompleted &&
                      reread.State.level1Vehicle == l1 && reread.State.level2Vehicle == pick, "save reloads with every flag and both cars");
                ctx.Campaign.Overlay.HideChoices();
                ctx.Campaign.Overlay.HideCard();
                ctx.Flow.GoToFrontend();
                yield return Until(() => FindAnyObjectByType<TitleScreen>() != null && !ctx.Flow.IsTransitioning, 30f, "title");
                yield return Wait(1.5f);
                Check(Subtitles.Current == null && !ctx.Campaign.Overlay.SubtitleVisible && !ctx.Campaign.Overlay.CardVisible, "no stale subtitles or cards on the title");
                yield return Shot("j1_title_after_campaign");
            }

            private IEnumerator SkipTrailer()
            {
                var ctx = Context;
                yield return Until(() => (FindAnyObjectByType<TitleScreen>() != null || FindAnyObjectByType<IntroSceneEntry>()?.IsPlaying == true) && !ctx.Flow.IsTransitioning, 40f, "title or opening");
                var intro = FindAnyObjectByType<IntroSceneEntry>();
                if (intro != null && intro.IsPlaying)
                {
                    intro.SkipToTitle();
                    yield return Wait(0.5f);
                    intro.SkipToTitle();
                    var onSkip = typeof(IntroSceneEntry).GetMethod("EndTeaser", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (intro.TeaserRunning) onSkip.Invoke(intro, null);
                    Check(!intro.TeaserRunning && Subtitles.Current == null, "skipping the teaser clears its subtitle");
                    yield return Until(() => intro.PhaseName == "Title", 20f, "title");
                    ctx.Flow.GoToFrontend();
                }
                yield return Until(() => FindAnyObjectByType<TitleScreen>() != null && !ctx.Flow.IsTransitioning, 30f, "title");
                yield return Wait(1f);
                FindAnyObjectByType<TitleScreen>().DevEnterHarbor(arrival: false);
                yield return Until(() => FindAnyObjectByType<CarSelectScreen>() != null && !ctx.Flow.IsTransitioning, 30f, "Level 2 Car Select (dev entry)");
                yield return Wait(3.5f);
                var cs = FindAnyObjectByType<CarSelectScreen>();
                Check(ctx.Campaign.Step == CampaignStep.Level2Select && cs.Count == 5, "dev entry opens the Level 2 selection");
                cs.ShowIndex(4);
                yield return Wait(1f);
                string pick = cs.Current.Id;
                Check(!cs.CurrentLocked, $"{pick} available after the dev unlock");
                cs.ConfirmCurrent();
                yield return Until(() => HarborRainEntry.Active?.Campaign != null && HarborRainEntry.Active.Campaign.Kind == HarborCampaign.Mode.Trailer, 90f, "trailer");
                var hc = HarborRainEntry.Active.Campaign;
                var mission = HarborRainEntry.Active.Mission;
                yield return Wait(4f);
                Check(Subtitles.Current != null, $"trailer narration on screen before the skip ('{Subtitles.Current}')");
                yield return Shot("k1_trailer_before_skip");
                hc.Skip();
                yield return Wait(1.5f);
                Check(hc.Finished && mission.Current == HarborRainMission.Phase.Enter && !mission.CarHeld, $"skip starts the operation ({mission.Current})");
                Check(Subtitles.Current == null && !ctx.Campaign.Overlay.SubtitleVisible && !ctx.Campaign.Overlay.CardVisible, "skip clears narration subtitles and cards");
                Check(ctx.Session.SelectedVehicle.Id == pick && HarborRainEntry.Active.Driving.PlayerVehicle != null, $"skip keeps the selected car ({pick})");
                Check(ctx.Campaign.Step == CampaignStep.Level2, "campaign step Level 2 after the skip");
                yield return Wait(3f);
                Check(HarborRainEntry.Active.Mission.Hud != null, "Harbor Rain HUD present");
                yield return Shot("k2_after_skip_gameplay");
                // Restart from the campaign pause menu path: reloads Harbor Rain with the same car and the trailer again.
                var radioLog = Subtitles.Log.Count;
                ctx.Flow.StartMission();
                yield return Until(() => HarborRainEntry.Active?.Campaign != null && HarborRainEntry.Active.Campaign.Kind == HarborCampaign.Mode.Trailer && !ctx.Flow.IsTransitioning, 90f, "restart");
                Check(ctx.Session.SelectedVehicle.Id == pick, "restart keeps the car");
                HarborRainEntry.Active.Campaign.Skip();
                yield return Wait(2f);
                Check(HarborRainEntry.Active.Mission.Current == HarborRainMission.Phase.Enter, "restarted operation running");
                Check(Subtitles.Log.Count > radioLog, "radio lines reported to the subtitle log");
                // Subtitle setting off: the radio card and overlay stay empty.
                ctx.Settings.SetSubtitles(false);
                Check(!Subtitles.Enabled && Subtitles.Current == null, "subtitles can be switched off");
                ctx.Settings.SetSubtitles(true);
                Check(Subtitles.Enabled, "subtitles back on (default ON)");
                ctx.Flow.GoToFrontend();
                yield return Until(() => FindAnyObjectByType<TitleScreen>() != null && !ctx.Flow.IsTransitioning, 30f, "title");
                Check(Subtitles.Current == null, "no stale subtitle after leaving Harbor Rain");
            }

            private IEnumerator Standalone()
            {
                var ctx = Context;
                Check(ctx.Campaign == null, "original Bootstrap: no campaign");
                yield return Opening(expectTeaser: false);
                var cs = FindAnyObjectByType<CarSelectScreen>();
                Check(cs != null && cs.Count == 3, $"original Car Select shows the three Night Run cars ({cs?.Count})");
                yield return Shot("s1_standalone_select");
                cs.ConfirmCurrent();
                yield return Until(() => FindAnyObjectByType<MissionSceneEntry>()?.Director?.Progress != null && !ctx.Flow.IsTransitioning, 60f, "Night Run");
                var director = FindAnyObjectByType<MissionSceneEntry>().Director;
                Check(director.Progress.Definition.Id == "night_run" || director.Progress.Definition.SceneName == "NightRun", $"Night Run starts ({director.Progress.Definition.Id})");
                Check(!director.CampaignPortalReady, "standalone Night Run never offers the portal");
                yield return Wait(3f);
                yield return Shot("s2_standalone_nightrun");
            }

            /// <summary>
            /// A public checkout: the two Harbor Rain cars are local-only third-party models, so their catalog slots resolve
            /// to nothing. Simulated in memory (cloned definition and catalogs with those slots empty; no asset is changed).
            /// </summary>
            private IEnumerator MissingCars()
            {
                var ctx = Context;
                yield return Until(() => (FindAnyObjectByType<TitleScreen>() != null || FindAnyObjectByType<IntroSceneEntry>()?.IsPlaying == true) && !ctx.Flow.IsTransitioning, 180f, "title or opening");
                var old = ctx.Campaign;
                var def = Instantiate(old.Definition);
                var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                NeonRift.Vehicles.VehicleCatalog Strip(NeonRift.Vehicles.VehicleCatalog c)
                {
                    var copy = Instantiate(c);
                    var list = (List<NeonRift.Vehicles.VehicleDefinition>)typeof(NeonRift.Vehicles.VehicleCatalog).GetField("vehicles", bf).GetValue(copy);
                    for (int i = 0; i < list.Count; i++) if (old.Definition.IsLevel2Vehicle(list[i])) list[i] = null;
                    return copy;
                }
                typeof(CampaignDefinition).GetField("level1Selection", bf).SetValue(def, Strip(old.Definition.Level1Selection));
                typeof(CampaignDefinition).GetField("level2Selection", bf).SetValue(def, Strip(old.Definition.Level2Selection));
                typeof(CampaignDefinition).GetField("level2Vehicles", bf).SetValue(def, new NeonRift.Vehicles.VehicleDefinition[] { null, null });
                var session = new CampaignSession(def, old.Progress);
                typeof(CampaignSession).GetProperty("Overlay").SetValue(session, old.Overlay);
                ctx.Campaign = session;
                Note("campaign swapped for one whose Harbor Rain car slots are empty (public checkout)");
                var intro = FindAnyObjectByType<IntroSceneEntry>();
                if (intro != null && intro.IsPlaying) { ctx.Flow.GoToFrontend(); yield return Wait(1f); }
                yield return Until(() => FindAnyObjectByType<TitleScreen>() != null && !ctx.Flow.IsTransitioning, 60f, "title");
                yield return Wait(1f);
                FindAnyObjectByType<TitleScreen>().DevEnterHarbor(arrival: true);
                yield return Until(() => HarborRainEntry.Active?.Campaign != null && HarborRainEntry.Active.Campaign.WaitingForConfirm, 150f, "arrival unlock card");
                Check(ctx.Campaign.Overlay.CardTitle == "A NEW OPERATION", "unlock card shown without Harbor Rain cars");
                yield return Wait(2f);
                yield return Shot("m1_unlock_without_cars");
                // The first press finishes revealing the card; the next one continues (as for a player).
                for (int k = 0; k < 6 && HarborRainEntry.Active != null && HarborRainEntry.Active.Campaign != null && !HarborRainEntry.Active.Campaign.Finished; k++)
                {
                    HarborRainEntry.Active.Campaign.Confirm();
                    yield return Wait(1f);
                }
                yield return Until(() => FindAnyObjectByType<CarSelectScreen>() != null && !ctx.Flow.IsTransitioning, 60f, "Level 2 select");
                if (FindAnyObjectByType<CarSelectScreen>() == null) yield break;
                yield return Wait(3f);
                var cs = FindAnyObjectByType<CarSelectScreen>();
                Check(cs.Count == 3, $"Level 2 selection lists the three present cars ({cs.Count})");
                cs.ShowIndex(1);
                yield return Wait(1f);
                Check(!cs.CurrentLocked && cs.Current != null, $"present car selectable ({cs.Current?.Id})");
                yield return Shot("m2_level2_three_cars");
                string pick = cs.Current.Id;
                cs.ConfirmCurrent();
                yield return Until(() => HarborRainEntry.Active?.Campaign != null && HarborRainEntry.Active.Campaign.Kind == HarborCampaign.Mode.Trailer, 90f, "trailer");
                HarborRainEntry.Active.Campaign.Skip();
                yield return Wait(2f);
                Check(HarborRainEntry.Active.Mission.Current == HarborRainMission.Phase.Enter && ctx.Session.SelectedVehicle.Id == pick, "Harbor Rain starts with the chosen car");
                ctx.Campaign = old;
                ctx.Flow.GoToFrontend();
                yield return Until(() => FindAnyObjectByType<TitleScreen>() != null && !ctx.Flow.IsTransitioning, 30f, "title");
            }

            private IEnumerator Relaunch()
            {
                var ctx = Context;
                var p = ctx.Campaign.Progress;
                Note($"save after relaunch: NR {p.NightRunCompleted} qualified {p.QualifiedForHarbor} portal {p.PortalCompleted} cars {p.HarborVehiclesUnlocked} harbor {p.HarborCompleted} L1 {p.State.level1Vehicle} L2 {p.State.level2Vehicle}");
                Check(p.HarborAvailable, "progress survives an application restart");
                yield return Until(() => FindAnyObjectByType<TitleScreen>() != null && !ctx.Flow.IsTransitioning, 40f, "title (opening already seen)");
                yield return Wait(1.5f);
                var title = FindAnyObjectByType<TitleScreen>();
                var harbor = title.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement.Q<UnityEngine.UIElements.Button>("harbor-button");
                Check(harbor != null, $"title offers Harbor Rain directly ('{harbor?.text}')");
                yield return Shot("r1_title_relaunch");
                // Level 1 selection still keeps the Harbor Rain cars locked.
                ctx.Campaign.Step = CampaignStep.Level1;
                ctx.Flow.GoToCarSelect();
                yield return Until(() => FindAnyObjectByType<CarSelectScreen>() != null && !ctx.Flow.IsTransitioning, 30f, "Level 1 select");
                yield return Wait(3f);
                var cs = FindAnyObjectByType<CarSelectScreen>();
                cs.ShowIndex(3);
                yield return Wait(1f);
                Check(cs.CurrentLocked, "Harbor Rain cars stay Level-2-only in the Level 1 selection");
                yield return Shot("r2_level1_after_unlock");
                ctx.Flow.GoToFrontend();
                yield return Until(() => FindAnyObjectByType<TitleScreen>() != null && !ctx.Flow.IsTransitioning, 30f, "title");
                yield return Wait(1f);
                harbor = FindAnyObjectByType<TitleScreen>().GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement.Q<UnityEngine.UIElements.Button>("harbor-button");
                using (var click = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { click.target = harbor; harbor.SendEvent(click); }
                yield return Until(() => FindAnyObjectByType<CarSelectScreen>() != null && !ctx.Flow.IsTransitioning, 30f, "Level 2 select from the title");
                yield return Wait(3f);
                cs = FindAnyObjectByType<CarSelectScreen>();
                cs.ShowIndex(3);
                yield return Wait(1f);
                Check(ctx.Campaign.Step == CampaignStep.Level2Select && !cs.CurrentLocked, "title → Harbor Rain: Level 2 selection with the cars unlocked");
                yield return Shot("r3_level2_from_title");
            }
        }
    }
}
