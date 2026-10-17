using System;
using System.IO;
using System.Linq;
using System.Text;
using NeonRift.EditorTools.Validation;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace NeonRift.Campaign.EditorTools
{
    /// <summary>
    /// Builds the connected campaign's APK: the campaign bootstrap first, then the shared front end, Car Select, Night
    /// Run and Harbor Rain. It uses the project's existing Android configuration (<see cref="AndroidBuild.Configure"/>,
    /// the existing signing setup — no custom keystore is configured, so the standard debug signing applies) and only
    /// changes the application id for the duration of the build, so the campaign installs beside the standalone Night
    /// Run APK; the id is restored afterwards. Existing APKs are never touched. Writes the build log and a manifest.
    /// </summary>
    public static class CampaignAndroidBuild
    {
        public const string Output = "Builds/Android/NEON_RIFT_COMPLETE_CAMPAIGN.apk";
        public const string Manifest = "Builds/Android/NEON_RIFT_COMPLETE_CAMPAIGN_manifest.txt";
        public const string Log = "Logs/Campaign/AndroidBuild_Campaign.txt";
        public const string ApplicationId = "com.soyebmohammad.neonrift.campaign";

        public static readonly string[] Scenes =
        {
            CampaignBuilder.ScenePath,
            "Assets/_NeonRift/Scenes/Frontend.unity",
            "Assets/_NeonRift/Scenes/CarSelect.unity",
            "Assets/_NeonRift/Scenes/NightRun.unity",
            CampaignBuilder.HarborScenePath
        };

        [MenuItem("Neon Rift/Campaign/Build Campaign APK (Release)")]
        public static string BuildRelease() => Build(false);

        [MenuItem("Neon Rift/Campaign/Build Campaign APK (Development)")]
        public static string BuildDevelopment() => Build(true);

        /// <summary>Files Unity rewrites as a side effect of an Android build (shader prefiltering, analytics flag);
        /// they are put back byte for byte afterwards so the project is left exactly as it was.</summary>
        private static readonly string[] Restore =
        {
            "Assets/_NeonRift/Settings/Rendering/Mobile_RPAsset.asset",
            "Assets/_NeonRift/Settings/Rendering/UniversalRenderPipelineGlobalSettings.asset",
            "ProjectSettings/UnityConnectSettings.asset"
        };

        /// <summary>The mobile pipeline's default volume profile (the Android look depends on it).</summary>
        private static string MobileVolumeProfile()
        {
            var rp = AssetDatabase.LoadMainAssetAtPath(Restore[0]);
            if (rp == null) return "missing RP asset";
            var p = new SerializedObject(rp).FindProperty("m_VolumeProfile");
            return p != null && p.objectReferenceValue != null ? AssetDatabase.GetAssetPath(p.objectReferenceValue) : "NONE";
        }

        public static string Build(bool development)
        {
            var snapshot = Restore.ToDictionary(f => f, f => File.Exists(f) ? File.ReadAllBytes(f) : null);
            string profileBefore = MobileVolumeProfile();
            foreach (var scene in Scenes) if (!File.Exists(scene)) throw new FileNotFoundException("campaign scene missing", scene);
            if (File.Exists(Output)) File.Move(Output, Output.Replace(".apk", $"_previous_{DateTime.Now:yyyyMMdd_HHmmss}.apk"));
            string previousId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            string previousName = PlayerSettings.productName;
            AndroidBuild.Configure();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            UnityEditor.Build.Reporting.BuildReport report;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = Output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = development ? BuildOptions.Development : BuildOptions.None
                });
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousId);
                PlayerSettings.productName = previousName;
                AssetDatabase.SaveAssets();
                foreach (var kv in snapshot)
                    if (kv.Value != null && (!File.Exists(kv.Key) || !File.ReadAllBytes(kv.Key).SequenceEqual(kv.Value)))
                    {
                        File.WriteAllBytes(kv.Key, kv.Value);
                        if (kv.Key.StartsWith("Assets/")) AssetDatabase.ImportAsset(kv.Key, ImportAssetOptions.ForceUpdate);
                    }
            }
            var s = report.summary;
            var messages = report.steps.SelectMany(step => step.messages)
                .Where(m => m.type is LogType.Error or LogType.Exception or LogType.Warning)
                .Select(m => $"{m.type}: {m.content}").ToList();
            bool ok = s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded && File.Exists(Output);
            string summary = $"[CampaignAndroidBuild] {(development ? "development" : "release")} {s.result} in {s.totalTime.TotalMinutes:0.0} min, " +
                             $"{(File.Exists(Output) ? new FileInfo(Output).Length / (1024f * 1024f) : 0f):0} MB, {s.totalErrors} errors, {s.totalWarnings} warnings → {Output}";
            Directory.CreateDirectory(Path.GetDirectoryName(Log));
            File.WriteAllText(Log, summary + "\nScenes:\n" + string.Join("\n", Scenes) + "\n\n" + string.Join("\n", messages));
            var manifest = new StringBuilder();
            manifest.AppendLine("NEON RIFT — COMPLETE CAMPAIGN (Night Run → portal → Harbor Rain)");
            manifest.AppendLine($"Built: {DateTime.Now:yyyy-MM-dd HH:mm}");
            manifest.AppendLine($"Result: {s.result}{(ok ? "" : " (NO APK PRODUCED)")}");
            manifest.AppendLine($"Unity: {Application.unityVersion}");
            manifest.AppendLine($"Target: Android ARM64, IL2CPP, APK, {(development ? "development" : "release")}");
            manifest.AppendLine($"Application id: {ApplicationId} (installs beside the standalone Night Run APK, com.soyebmohammad.neonrift)");
            manifest.AppendLine($"Version: {PlayerSettings.bundleVersion}");
            manifest.AppendLine("Signing: project setting (no custom keystore configured → Unity debug keystore), unchanged");
            manifest.AppendLine($"Mobile URP default volume profile at build start: {profileBefore}");
            manifest.AppendLine($"Size: {(File.Exists(Output) ? new FileInfo(Output).Length / (1024f * 1024f) : 0f):0} MB");
            manifest.AppendLine("Scenes (index 0 = entry point):");
            for (int i = 0; i < Scenes.Length; i++) manifest.AppendLine($"  {i}  {Scenes[i]}");
            manifest.AppendLine($"Errors {s.totalErrors}, warnings {s.totalWarnings}; full log: {Log}");
            File.WriteAllText(Manifest, manifest.ToString());
            Debug.Log(summary);
            return summary;
        }
    }
}
