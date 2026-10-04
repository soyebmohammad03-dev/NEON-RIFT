using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NeonRift.Game;
using NeonRift.Missions;
using Unity.Profiling;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Player-build performance run: launched with <c>-perfroute &lt;output folder&gt;</c> (see
    /// <see cref="PerfRouteArgs"/>), the game boots straight into the mission, drives the full
    /// BoulevardInAlleyOut route with <see cref="NightRunValidator"/>, samples every frame (frame time, CPU main and
    /// render thread, GPU, GC allocation) and writes perfroute_summary.md and perfroute_seconds.csv, then quits.
    /// Nothing happens without the argument.
    /// </summary>
    public sealed class PerfRouteRunner : MonoBehaviour
    {
        private struct Sample
        {
            public float Time, FrameMs, CpuMs, RenderMs, GpuMs, GcKb, Elapsed;
            public int Objective;
            public SecurityLevel Security;
        }

        private readonly List<Sample> samples = new();
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private ProfilerRecorder gc;
        private NightRunValidator validator;
        private MissionDirector director;
        private float startedAt, deadline;
        private bool running, sawRunning;
        private string folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!PerfRouteArgs.TryGet(out string output)) return;
            var go = new GameObject("PerfRouteRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfRouteRunner>().folder = output;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Debug.Log($"[PerfRoute] armed, output {output}");
        }

        private void Update()
        {
            if (!running)
            {
                TryStart();
                return;
            }
            FrameTimingManager.CaptureFrameTimings();
            bool timed = FrameTimingManager.GetLatestTimings(1, timing) > 0;
            var p = director != null ? director.Progress : null;
            samples.Add(new Sample
            {
                Time = Time.realtimeSinceStartup - startedAt,
                FrameMs = Time.unscaledDeltaTime * 1000f,
                CpuMs = timed ? (float)timing[0].cpuMainThreadFrameTime : 0f,
                RenderMs = timed ? (float)timing[0].cpuRenderThreadFrameTime : 0f,
                GpuMs = timed ? (float)timing[0].gpuFrameTime : 0f,
                GcKb = gc.Valid ? gc.LastValue / 1024f : 0f,
                Elapsed = p != null ? p.Elapsed : 0f,
                Objective = p != null ? p.ObjectiveIndex : -1,
                Security = p != null ? p.Security : SecurityLevel.Calm
            });
            if (validator.Running) sawRunning = true;
            if ((sawRunning && !validator.Running) || Time.realtimeSinceStartup > deadline) Finish();
        }

        private void TryStart()
        {
            var entry = FindAnyObjectByType<MissionSceneEntry>();
            if (entry == null || entry.Director == null || entry.Director.Progress == null) return;
            validator = FindAnyObjectByType<NightRunValidator>();
            if (validator == null) { Debug.LogError("[PerfRoute] no validator in the scene"); Application.Quit(2); return; }
            director = entry.Director;
            // Let the city settle (streaming, first shader use) before the clock starts.
            if (startedAt <= 0f) { startedAt = Time.realtimeSinceStartup + 3f; return; }
            if (Time.realtimeSinceStartup < startedAt) return;
            if (!validator.Begin(director, NightRunValidator.Scenario.BoulevardInAlleyOut)) return;
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            startedAt = Time.realtimeSinceStartup;
            deadline = startedAt + 300f;
            running = true;
            Debug.Log($"[PerfRoute] route started at {Screen.width}x{Screen.height}");
        }

        private void Finish()
        {
            running = false;
            gc.Dispose();
            var data = samples.Where(s => s.Time > 0.5f).ToList();
            System.IO.Directory.CreateDirectory(folder);
            var csv = new StringBuilder("second,elapsed,objective,security,frames,frame_avg_ms,frame_max_ms,cpu_main_ms,cpu_render_ms,gpu_ms,gc_kb_per_frame\n");
            foreach (var g in data.GroupBy(s => (int)s.Time))
            {
                var s = g.ToList();
                csv.AppendLine(string.Join(",", g.Key, s[^1].Elapsed.ToString("0.0"), s[^1].Objective, s[^1].Security, s.Count,
                    s.Average(x => x.FrameMs).ToString("0.00"), s.Max(x => x.FrameMs).ToString("0.00"), s.Average(x => x.CpuMs).ToString("0.00"),
                    s.Average(x => x.RenderMs).ToString("0.00"), s.Average(x => x.GpuMs).ToString("0.00"), s.Average(x => x.GcKb).ToString("0.0")));
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "perfroute_seconds.csv"), csv.ToString());

            var sb = new StringBuilder($"[PerfRoute] player build, {Screen.width}x{Screen.height}, vSync off, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}\n");
            sb.AppendLine($"Route {validator.Report.Split('\n').FirstOrDefault()}");
            sb.AppendLine("| Segment | Frames | FPS avg | Frame avg | p95 | p99 | max | CPU main | CPU render | GPU avg | GPU p95 | GC KB/frame |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            void Row(string name, List<Sample> s)
            {
                if (s.Count == 0) return;
                var f = s.Select(x => x.FrameMs).OrderBy(x => x).ToList();
                var g = s.Select(x => x.GpuMs).Where(x => x > 0f).OrderBy(x => x).ToList();
                float P(List<float> l, float q) => l.Count == 0 ? 0f : l[Mathf.Clamp(Mathf.CeilToInt(q * l.Count) - 1, 0, l.Count - 1)];
                sb.AppendLine($"| {name} | {s.Count} | {1000f / f.Average():0} | {f.Average():0.00} ms | {P(f, 0.95f):0.00} | {P(f, 0.99f):0.00} | {f[^1]:0.00} | " +
                              $"{s.Average(x => x.CpuMs):0.00} | {s.Average(x => x.RenderMs):0.00} | {(g.Count > 0 ? g.Average() : 0f):0.00} | {P(g, 0.95f):0.00} | {s.Average(x => x.GcKb):0.00} |");
            }
            Row("Approach", data.Where(s => s.Objective == 0).ToList());
            Row("Heist", data.Where(s => s.Objective is 1 or 2).ToList());
            Row("Escape (lockdown)", data.Where(s => s.Objective >= 3).ToList());
            Row("Whole route", data);
            int hitches = data.Count(s => s.FrameMs > 50f);
            sb.AppendLine($"Hitches over 50 ms: {hitches}. Frames allocating managed memory: {data.Count(s => s.GcKb > 0.01f)} of {data.Count}.");
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "perfroute_summary.md"), sb.ToString());
            Debug.Log(sb.ToString());
            Application.Quit(0);
        }
    }
}
