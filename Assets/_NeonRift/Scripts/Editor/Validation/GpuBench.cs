using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Controlled GPU A/B bench (Play Mode): with the scene held still, applies each URP variant in turn, lets it
    /// settle, then averages <see cref="FrameTimingManager"/> GPU time over a fixed number of frames. Every variant is
    /// measured in the same session, view and resolution, so the differences are attributable (absolute editor numbers
    /// are not a player build's). The pipeline asset is restored afterwards. Writes Logs/Perf/&lt;prefix&gt;_gpu.md.
    /// </summary>
    public static class GpuBench
    {
        private sealed class Variant
        {
            public string Name;
            public Action<UniversalRenderPipelineAsset, UniversalRendererData> Apply;
            public readonly List<float> Gpu = new(), Cpu = new();
        }

        private static readonly List<Variant> variants = new();
        private static int index, frame;
        private static string prefix;
        private static int settle = 30, sample = 180, rounds = 2, round;
        private static (int msaa, bool soft, int addRes, float scale, bool ssao, int mainRes) original;
        private static readonly FrameTiming[] timing = new FrameTiming[1];

        /// <param name="only">Comma-separated name fragments: bench just those variants (plus the baseline).</param>
        public static string Begin(string capturePrefix, int samplesPerVariant = 180, int settleFrames = 30, int roundCount = 2, string only = null)
        {
            if (!Application.isPlaying) return "not in Play Mode";
            var asset = UniversalRenderPipeline.asset;
            if (asset == null) return "no URP asset";
            var data = asset.rendererDataList[0] as UniversalRendererData;
            PlayerSettings.enableFrameTimingStats = true;
            // Variants switch shader keywords: compile them synchronously so a placeholder shader never gets measured.
            asyncShaders = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            prefix = capturePrefix;
            originalSoftQuality = null;
            sample = samplesPerVariant;
            settle = settleFrames;
            rounds = roundCount;
            var ssao = data != null ? data.rendererFeatures.FirstOrDefault(f => f != null && f.name.Contains("Ambient")) : null;
            original = (asset.msaaSampleCount, asset.supportsSoftShadows, asset.additionalLightsShadowmapResolution, asset.renderScale,
                        ssao != null && ssao.isActive, asset.mainLightShadowmapResolution);
            variants.Clear();
            void Add(string name, Action<UniversalRenderPipelineAsset, UniversalRendererData> apply) => variants.Add(new Variant { Name = name, Apply = apply });
            void Restore(UniversalRenderPipelineAsset a, UniversalRendererData d)
            {
                a.msaaSampleCount = original.msaa;
                SetSoftShadows(a, original.soft);
                SetSoftQuality(a, null);
                a.additionalLightsShadowmapResolution = original.addRes;
                a.renderScale = original.scale;
                a.mainLightShadowmapResolution = original.mainRes;
                if (ssao != null) ssao.SetActive(original.ssao);
            }
            Add("baseline (current settings)", Restore);
            Add("MSAA 2x", (a, d) => { Restore(a, d); a.msaaSampleCount = 2; });
            Add("MSAA off", (a, d) => { Restore(a, d); a.msaaSampleCount = 1; });
            Add("SSAO off", (a, d) => { Restore(a, d); if (ssao != null) ssao.SetActive(false); });
            Add("soft shadows off", (a, d) => { Restore(a, d); SetSoftShadows(a, false); });
            Add("soft shadow quality Medium", (a, d) => { Restore(a, d); SetSoftQuality(a, "Medium"); });
            Add("soft shadow quality Low", (a, d) => { Restore(a, d); SetSoftQuality(a, "Low"); });
            Add("additional-light shadow atlas 1024", (a, d) => { Restore(a, d); a.additionalLightsShadowmapResolution = 1024; });
            Add("main shadow map 1024", (a, d) => { Restore(a, d); a.mainLightShadowmapResolution = 1024; });
            Add("render scale 0.85", (a, d) => { Restore(a, d); a.renderScale = 0.85f; });
            if (!string.IsNullOrEmpty(only))
            {
                var keep = only.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
                variants.RemoveAll(v => v != variants[0] && !keep.Any(k => v.Name.Contains(k)));
            }
            index = 0;
            frame = 0;
            round = 0;
            variants[0].Apply(asset, data);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return $"benching {variants.Count} variants × {rounds} rounds at {Screen.width}x{Screen.height}";
        }

        private static int lastFrame = -1;
        private static bool asyncShaders = true;

        private static string originalSoftQuality;

        /// <summary>Soft-shadow filter quality on the asset (Low / Medium / High); null restores the original.</summary>
        private static void SetSoftQuality(UniversalRenderPipelineAsset asset, string quality)
        {
            var so = new SerializedObject(asset);
            var p = so.FindProperty("m_SoftShadowQuality");
            if (p == null) return;
            originalSoftQuality ??= p.enumNames[p.enumValueIndex];
            int i = System.Array.IndexOf(p.enumNames, quality ?? originalSoftQuality);
            if (i < 0 || i == p.enumValueIndex) return;
            p.enumValueIndex = i;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The asset's soft-shadow switch has no public setter.</summary>
        private static void SetSoftShadows(UniversalRenderPipelineAsset asset, bool on)
        {
            var so = new SerializedObject(asset);
            var p = so.FindProperty("m_SoftShadowsSupported");
            if (p == null || p.boolValue == on) return;
            p.boolValue = on;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { Finish("left Play Mode"); return; }
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            FrameTimingManager.CaptureFrameTimings();
            frame++;
            if (frame > settle && FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0)
            {
                variants[index].Gpu.Add((float)timing[0].gpuFrameTime);
                variants[index].Cpu.Add((float)timing[0].cpuMainThreadFrameTime);
            }
            if (frame < settle + sample) return;
            frame = 0;
            index++;
            if (index >= variants.Count)
            {
                index = 0;
                // Rounds interleave the variants, so slow drift (thermals, the editor) spreads over all of them.
                if (++round >= rounds) { Finish(null); return; }
            }
            var asset = UniversalRenderPipeline.asset;
            variants[index].Apply(asset, asset.rendererDataList[0] as UniversalRendererData);
        }

        private static void Finish(string reason)
        {
            EditorApplication.update -= Tick;
            EditorSettings.asyncShaderCompilation = asyncShaders;
            var asset = UniversalRenderPipeline.asset;
            if (asset != null && variants.Count > 0) variants[0].Apply(asset, asset.rendererDataList[0] as UniversalRendererData);
            var sb = new StringBuilder($"[GpuBench] {prefix} at {Screen.width}x{Screen.height}{(reason != null ? $" ({reason})" : string.Empty)}\n");
            sb.AppendLine("| Variant | GPU avg (ms) | GPU median | vs baseline | CPU main avg |");
            sb.AppendLine("|---|---|---|---|---|");
            float baseline = variants.Count > 0 && variants[0].Gpu.Count > 0 ? variants[0].Gpu.Average() : 0f;
            foreach (var v in variants)
            {
                if (v.Gpu.Count == 0) { sb.AppendLine($"| {v.Name} | n/a | | | |"); continue; }
                var sorted = v.Gpu.OrderBy(x => x).ToList();
                float avg = v.Gpu.Average();
                sb.AppendLine($"| {v.Name} | {avg:0.00} | {sorted[sorted.Count / 2]:0.00} | {(baseline > 0 ? (avg - baseline) / baseline * 100f : 0f):+0;-0}% | {v.Cpu.Average():0.00} |");
            }
            System.IO.Directory.CreateDirectory(RoutePerf.Folder);
            System.IO.File.WriteAllText($"{RoutePerf.Folder}/{prefix}_gpu.md", sb.ToString());
            Debug.Log(sb.ToString());
        }
    }
}
