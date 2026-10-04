using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Builds the macOS player used for the performance run (release, not development: the numbers should be a
    /// player's). Run it with <c>-perfroute &lt;folder&gt;</c> (see <c>PerfRouteRunner</c>).
    /// </summary>
    public static class PerfBuild
    {
        public const string Output = "Builds/PerfMac/NeonRift.app";

        [MenuItem("Neon Rift/Validation/Build Perf Player (macOS)")]
        public static string Build()
        {
            PlayerSettings.enableFrameTimingStats = true;
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Output,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            });
            var s = report.summary;
            string text = $"[PerfBuild] {s.result} in {s.totalTime.TotalMinutes:0.0} min, {s.totalSize / (1024f * 1024f):0} MB, {s.totalErrors} errors, {s.totalWarnings} warnings → {Output}";
            System.IO.Directory.CreateDirectory(RoutePerf.Folder);
            System.IO.File.WriteAllText($"{RoutePerf.Folder}/perfbuild.txt", text + "\n" + string.Join("\n", scenes));
            Debug.Log(text);
            return text;
        }
    }
}
