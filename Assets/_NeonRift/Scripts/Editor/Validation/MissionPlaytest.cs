using System.Collections.Generic;
using System.Text;
using NeonRift.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Play Mode harness around <see cref="NightRunValidator"/>: starts a scenario on the running mission, captures the
    /// Game view (with the HUD) a moment after chosen world events and interaction stages, and writes the validator's
    /// report to Logs/Playtest/&lt;prefix&gt;_report.txt when the run ends. Runs on the editor loop, so tool calls do
    /// not have to poll during the run.
    /// </summary>
    public static class MissionPlaytest
    {
        private static readonly List<(float time, string label)> pending = new();
        private static readonly Dictionary<string, float> captureEvents = new();
        private static NightRunValidator validator;
        private static string prefix;
        private static int shots;
        private static float startedAt;

        /// <summary>
        /// Starts <paramref name="scenario"/>. <paramref name="captures"/>: "eventId@delay" (world event) or
        /// "stage:LABEL@delay" (an interaction stage starting), plus "t@seconds" for plain timed shots.
        /// </summary>
        public static string Begin(NightRunValidator.Scenario scenario, string capturePrefix, float timeScale, params string[] captures)
        {
            if (!Application.isPlaying) return "not in Play Mode";
            var entry = Object.FindAnyObjectByType<MissionSceneEntry>();
            validator = Object.FindAnyObjectByType<NightRunValidator>();
            if (entry == null || entry.Director == null || entry.Director.Progress == null || validator == null) return "mission not running yet";
            Application.runInBackground = true;
            Time.timeScale = timeScale;
            Time.maximumDeltaTime = 0.1f;
            prefix = capturePrefix;
            shots = 0;
            pending.Clear();
            captureEvents.Clear();
            startedAt = Time.unscaledTime;
            System.IO.Directory.CreateDirectory(InputPlaytest.CaptureFolder);
            System.IO.File.Delete($"{InputPlaytest.CaptureFolder}/{prefix}_report.txt");
            foreach (var c in captures)
            {
                int at = c.LastIndexOf('@');
                string key = at < 0 ? c : c.Substring(0, at);
                float delay = at < 0 ? 0f : float.Parse(c.Substring(at + 1), System.Globalization.CultureInfo.InvariantCulture);
                if (key == "t") pending.Add((Time.unscaledTime + delay, "t" + delay.ToString("0")));
                else captureEvents[key] = delay;
            }
            entry.Director.World.EventRaised += e => Trigger(e);
            foreach (var i in Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None))
                i.FeedbackRaised += (source, f) =>
                {
                    if (f is Interactable.Feedback.Started or Interactable.Feedback.StepStarted && source.Run?.Current != null)
                        Trigger("stage:" + source.Run.Current.Label);
                    if (f == Interactable.Feedback.Interference) Trigger("interference");
                };
            bool ok = validator.Begin(entry.Director, scenario);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return ok ? $"{scenario} started, {captures.Length} capture rules" : validator.Report;
        }

        private static void Trigger(string key)
        {
            if (!captureEvents.TryGetValue(key, out float delay)) return;
            captureEvents.Remove(key);
            pending.Add((Time.unscaledTime + delay, key.Replace(':', '_').Replace(' ', '_').Replace('.', '_')));
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { Finish("left Play Mode"); return; }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < pending[i].time) continue;
                ScreenCapture.CaptureScreenshot($"{InputPlaytest.CaptureFolder}/{prefix}_{shots++:00}_{pending[i].label}.png");
                pending.RemoveAt(i);
            }
            if (validator != null && !validator.Running && pending.Count == 0 && Time.unscaledTime - startedAt > 2f) Finish(null);
        }

        private static void Finish(string reason)
        {
            EditorApplication.update -= Tick;
            Time.timeScale = 1f;
            var sb = new StringBuilder();
            if (reason != null) sb.AppendLine(reason);
            sb.AppendLine(validator != null ? validator.Report : "no validator");
            sb.AppendLine($"captures: {shots}");
            System.IO.File.WriteAllText($"{InputPlaytest.CaptureFolder}/{prefix}_report.txt", sb.ToString());
        }
    }
}
