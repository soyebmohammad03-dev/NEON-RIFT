using System.Collections.Generic;
using System.Linq;
using System.Text;
using NeonRift.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Runs several <see cref="NightRunValidator"/> scenarios back to back in one Play Mode session (restarting the
    /// mission through its normal retry path between runs) and writes a summary table plus every full report to
    /// Logs/Playtest/&lt;prefix&gt;_series.txt. Counts player collisions, rival car contacts (and where they happened),
    /// reversals, resets and finishing order. Used for the rival-collision regression runs.
    /// </summary>
    public static class MissionSeries
    {
        private static readonly Queue<NightRunValidator.Scenario> queue = new();
        private static readonly StringBuilder table = new();
        private static readonly StringBuilder reports = new();
        private static readonly List<string> contacts = new();
        private static string prefix;
        private static NightRunValidator validator;
        private static MissionDirector director;
        private static NightRunValidator.Scenario current;
        private static float waitUntil;
        private static int run;
        private static bool running;

        public static string Begin(string seriesPrefix, float timeScale, params NightRunValidator.Scenario[] scenarios)
        {
            if (!Application.isPlaying) return "not in Play Mode";
            prefix = seriesPrefix;
            queue.Clear();
            foreach (var s in scenarios) queue.Enqueue(s);
            table.Clear();
            reports.Clear();
            contacts.Clear();
            run = 0;
            running = false;
            Application.runInBackground = true;
            Time.timeScale = timeScale;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            table.AppendLine("| # | Scenario | Result | Time | Player collisions | Rival contacts | Reversals | Resets | Rivals finished |");
            table.AppendLine("|---|---|---|---|---|---|---|---|---|");
            System.IO.Directory.CreateDirectory(InputPlaytest.CaptureFolder);
            System.IO.File.Delete($"{InputPlaytest.CaptureFolder}/{prefix}_series.txt");
            waitUntil = Time.unscaledTime + 1f;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return $"{scenarios.Length} runs queued";
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (message.StartsWith("[Rivals] contact")) contacts.Add($"run {run}: {message}");
            if (type is LogType.Error or LogType.Exception) contacts.Add($"run {run} ERROR: {message.Split('\n')[0]}");
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { Finish("left Play Mode"); return; }
            if (Time.unscaledTime < waitUntil) return;
            if (!running)
            {
                if (queue.Count == 0) { Finish(null); return; }
                var entry = Object.FindAnyObjectByType<MissionSceneEntry>();
                director = entry != null ? entry.Director : null;
                validator = Object.FindAnyObjectByType<NightRunValidator>();
                if (director == null || director.Progress == null || director.Player == null || validator == null) { waitUntil = Time.unscaledTime + 0.5f; return; }
                current = queue.Dequeue();
                run++;
                if (!validator.Begin(director, current)) { table.AppendLine($"| {run} | {current} | could not start | | | | | | |"); return; }
                running = true;
                return;
            }
            if (validator != null && validator.Running) return;
            // Run over: record it, then restart the mission for the next one.
            var rivals = Object.FindAnyObjectByType<RivalDirector>();
            var progress = director != null ? director.Progress : null;
            string report = validator != null ? validator.Report : string.Empty;
            int playerCollisions = 0;
            var m = System.Text.RegularExpressions.Regex.Match(report, @"collisions (\d+)");
            if (m.Success) playerCollisions = int.Parse(m.Groups[1].Value);
            int carContacts = 0, reversals = 0, resets = 0, finished = 0;
            if (rivals != null)
                foreach (var r in rivals.Rivals)
                {
                    carContacts += r.VehicleContacts;
                    reversals += r.Driver.Reversals;
                    resets += r.Driver.Recoveries;
                    if (r.Finished) finished++;
                }
            table.AppendLine($"| {run} | {current} | {(progress != null ? progress.Phase.ToString() : "?")} | {(progress != null ? progress.Elapsed.ToString("0.0") : "?")} s | " +
                             $"{playerCollisions} | {carContacts} | {reversals} | {resets} | {finished} |");
            reports.AppendLine($"===== run {run}: {current}");
            reports.AppendLine(report);
            running = false;
            if (queue.Count == 0) { Finish(null); return; }
            director.RestartMission();
            waitUntil = Time.unscaledTime + 6f;
        }

        private static void Finish(string reason)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            var sb = new StringBuilder();
            if (reason != null) sb.AppendLine(reason);
            sb.Append(table);
            sb.AppendLine();
            sb.AppendLine("Contacts and errors:");
            if (contacts.Count == 0) sb.AppendLine("  none");
            foreach (var c in contacts.Take(80)) sb.AppendLine("  " + c);
            sb.AppendLine();
            sb.Append(reports);
            System.IO.File.WriteAllText($"{InputPlaytest.CaptureFolder}/{prefix}_series.txt", sb.ToString());
            contacts.Clear();
        }
    }
}
