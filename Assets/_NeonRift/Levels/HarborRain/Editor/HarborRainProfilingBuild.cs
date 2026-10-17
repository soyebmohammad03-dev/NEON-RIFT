using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// A development / profiler-connected Android build of Harbor Rain for on-device measurement. The scene list is passed
    /// to the build directly (EditorBuildSettings is not modified), the output is a NEW file next to the existing APKs
    /// (which are never overwritten), and the active build target / player settings this changes are reported so they can
    /// be put back. Run it only with a device attached; it is not run automatically.
    /// </summary>
    public static class HarborRainProfilingBuild
    {
        public const string Output = "Builds/Android/HarborRain_Profiling_DEV.apk";

        [MenuItem("Neon Rift/Harbor Rain/Build Android Profiling APK (does not touch build settings)")]
        public static string Build()
        {
            if (File.Exists("Builds/Android/NEON_RIFT_NIGHT_RUN.apk") && Path.GetFullPath(Output) == Path.GetFullPath("Builds/Android/NEON_RIFT_NIGHT_RUN.apk")) return "refused: would overwrite an existing APK";
            var before = EditorUserBuildSettings.activeBuildTarget;
            var options = new BuildPlayerOptions
            {
                // Harbor Rain first (the standalone entry adopts it once BootstrapLoader has brought up Bootstrap).
                scenes = new[] { "Assets/_NeonRift/Levels/HarborRain/Scenes/HarborRain.unity", "Assets/_NeonRift/Scenes/Bootstrap.unity" },
                locationPathName = Output,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.ConnectWithProfiler | BuildOptions.AllowDebugging
            };
            var report = BuildPipeline.BuildPlayer(options);
            string text = $"[HarborRain] profiling build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalMinutes:0.0} min → {Output}\n" +
                          $"  active build target before: {before}, after: {EditorUserBuildSettings.activeBuildTarget} (switch back if they differ)\n" +
                          "  device tests: adb install -r, launch, Profiler → Android player; measure the coast road (wave set), the restricted bay (crane lift), the escape (3 police units): avg FPS, frame-time spikes, memory, thermal state.";
            File.WriteAllText("Logs/HarborRainProfilingBuild.txt", text);
            Debug.Log(text);
            return text;
        }
    }
}
