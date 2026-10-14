using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Builds the Android APK (ARM64, IL2CPP) from the normal scene list, so the Bootstrap flow is the same as on PC.
    /// Only Android / mobile player settings are touched: application id, landscape-only orientation, APK (not AAB).
    /// The result is written to <c>Logs/AndroidBuild.txt</c>.
    /// </summary>
    public static class AndroidBuild
    {
        public const string Output = "Builds/Android/NEON_RIFT_NIGHT_RUN.apk";
        public const string ReleaseOutput = "Builds/Android/NEON_RIFT_NIGHT_RUN_release.apk";

        [MenuItem("Neon Rift/Build/Android APK (Development)")]
        public static string BuildDevelopment() => Build(true);

        [MenuItem("Neon Rift/Build/Android APK (Release)")]
        public static string BuildRelease() => Build(false);

        public static void Configure()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.soyebmohammad.neonrift");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Orientation only applies to mobile players: landscape both ways, never portrait.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            EditorUserBuildSettings.buildAppBundle = false;
        }

        public static string Build(bool development)
        {
            Configure();
            string output = development ? Output : ReleaseOutput;
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None
            });
            var s = report.summary;
            var messages = report.steps.SelectMany(step => step.messages)
                .Where(m => m.type is LogType.Error or LogType.Exception or LogType.Warning)
                .Select(m => $"{m.type}: {m.content}");
            string text = $"[AndroidBuild] {(development ? "development" : "release")} {s.result} in {s.totalTime.TotalMinutes:0.0} min, " +
                          $"{s.totalSize / (1024f * 1024f):0} MB, {s.totalErrors} errors, {s.totalWarnings} warnings → {output}";
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/AndroidBuild.txt", text + "\nScenes:\n" + string.Join("\n", scenes) + "\n\n" + string.Join("\n", messages));
            Debug.Log(text);
            return text;
        }
    }
}
