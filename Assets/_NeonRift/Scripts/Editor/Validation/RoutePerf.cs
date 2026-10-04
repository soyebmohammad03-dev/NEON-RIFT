using System.Collections.Generic;
using System.Linq;
using System.Text;
using NeonRift.Gameplay;
using NeonRift.Missions;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Full-route performance profile: rides along with a running <see cref="NightRunValidator"/> scenario (or any
    /// Play Mode session) and samples every frame with <see cref="ProfilerRecorder"/>s — PlayerLoop and main-thread
    /// time, GC allocation, batches, SetPass calls, draw calls, triangles, shadow casters — plus enabled real-time
    /// lights, mission phase and position. Writes one CSV row per second and a summary per route segment (approach,
    /// heist, escape) with p95/p99 frame times to Logs/Perf/&lt;prefix&gt;_*.
    /// <para>PlayerLoop excludes the editor's own loop, so it is the closest editor-side figure to a player build;
    /// main-thread time includes editor overhead.</para>
    /// </summary>
    public static class RoutePerf
    {
        public const string Folder = "Logs/Perf";

        private sealed class Frame
        {
            public float Time, Elapsed, PlayerLoopMs, MainMs, GpuMs, GcKb;
            public long Batches, SetPass, DrawCalls, Triangles, ShadowCasters;
            public int Lights, Objective;
            public SecurityLevel Security;
            public Vector3 Position;
        }

        private static readonly List<Frame> frames = new();
        private static ProfilerRecorder playerLoop, mainThread, gcAlloc, batches, setPass, drawCalls, triangles, shadowCasters;
        private static string prefix;
        private static bool running, sawValidator;
        private static float startedAt;
        private static Light[] lights = new Light[0];

        public static bool Running => running;

        /// <summary>Starts sampling; stops by itself when the validator finishes (or after <paramref name="maxSeconds"/>).</summary>
        public static string Begin(string capturePrefix, float maxSeconds = 300f)
        {
            if (!Application.isPlaying) return "not in Play Mode";
            // GPU frame times need frame timing stats; measure at a fixed 1080p, not whatever size the Game view has.
            PlayerSettings.enableFrameTimingStats = true;
            PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "RoutePerf 1080p");
            prefix = capturePrefix;
            frames.Clear();
            Dispose();
            playerLoop = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop", 1);
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count", 1);
            lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            startedAt = Time.unscaledTime;
            limit = maxSeconds;
            running = true;
            sawValidator = false;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return $"profiling ({lights.Length} lights in scene; recorders valid: loop {playerLoop.Valid}, main {mainThread.Valid}, gc {gcAlloc.Valid}, batches {batches.Valid})";
        }

        private static float limit;
        private static readonly FrameTiming[] timing = new FrameTiming[1];
        private static int lastFrame = -1;

        private static void Tick()
        {
            if (!running) return;
            if (!Application.isPlaying) { Finish("left Play Mode"); return; }
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            var entry = Object.FindAnyObjectByType<MissionSceneEntry>();
            var validator = Object.FindAnyObjectByType<NightRunValidator>();
            var progress = entry != null && entry.Director != null ? entry.Director.Progress : null;
            FrameTimingManager.CaptureFrameTimings();
            float gpu = FrameTimingManager.GetLatestTimings(1, timing) > 0 ? (float)timing[0].gpuFrameTime : 0f;
            int lit = 0;
            foreach (var l in lights) if (l != null && l.isActiveAndEnabled && l.intensity > 0f) lit++;
            frames.Add(new Frame
            {
                Time = Time.unscaledTime - startedAt,
                Elapsed = progress != null ? progress.Elapsed : 0f,
                PlayerLoopMs = playerLoop.LastValue * 1e-6f,
                MainMs = mainThread.LastValue * 1e-6f,
                GpuMs = gpu,
                GcKb = gcAlloc.LastValue / 1024f,
                Batches = batches.LastValue,
                SetPass = setPass.LastValue,
                DrawCalls = drawCalls.LastValue,
                Triangles = triangles.LastValue,
                ShadowCasters = shadowCasters.LastValue,
                Lights = lit,
                Objective = progress != null ? progress.ObjectiveIndex : -1,
                Security = progress != null ? progress.Security : SecurityLevel.Calm,
                Position = entry != null && entry.Director != null && entry.Director.Player != null ? entry.Director.Player.transform.position : Vector3.zero
            });
            if (validator != null && validator.Running) sawValidator = true;
            bool done = sawValidator && validator != null && !validator.Running;
            if (done || Time.unscaledTime - startedAt > limit) Finish(done ? null : "time limit");
        }

        private static void Finish(string reason)
        {
            running = false;
            EditorApplication.update -= Tick;
            Dispose();
            System.IO.Directory.CreateDirectory(Folder);
            // The first frames after Begin include scene warm-up; skip half a second.
            var data = frames.Where(f => f.Time > 0.5f).ToList();
            var csv = new StringBuilder("second,elapsed,objective,security,x,z,frames,playerloop_avg_ms,playerloop_max_ms,main_avg_ms,gpu_avg_ms,gc_kb_per_frame,batches,setpass,drawcalls,triangles_k,shadow_casters,lights\n");
            foreach (var g in data.GroupBy(f => Mathf.FloorToInt(f.Time)))
            {
                var s = g.ToList();
                var last = s[s.Count - 1];
                csv.AppendLine(string.Join(",", g.Key, last.Elapsed.ToString("0.0"), last.Objective, last.Security, last.Position.x.ToString("0"), last.Position.z.ToString("0"),
                    s.Count, s.Average(f => f.PlayerLoopMs).ToString("0.00"), s.Max(f => f.PlayerLoopMs).ToString("0.00"), s.Average(f => f.MainMs).ToString("0.00"),
                    s.Average(f => f.GpuMs).ToString("0.00"), s.Average(f => f.GcKb).ToString("0.0"), (long)s.Average(f => f.Batches), (long)s.Average(f => f.SetPass), (long)s.Average(f => f.DrawCalls),
                    ((long)s.Average(f => f.Triangles) / 1000), (long)s.Average(f => f.ShadowCasters), (int)s.Average(f => f.Lights)));
            }
            System.IO.File.WriteAllText($"{Folder}/{prefix}_seconds.csv", csv.ToString());

            var sb = new StringBuilder($"[RoutePerf] {prefix}: {data.Count} frames over {(data.Count > 0 ? data[data.Count - 1].Time : 0f):0.0} s at {Screen.width}x{Screen.height}{(reason != null ? $" ({reason})" : string.Empty)}\n");
            sb.AppendLine("PlayerLoop = game work per frame (excludes the editor's loop). Main = whole main thread incl. editor overhead.");
            sb.AppendLine("| Segment | Frames | PlayerLoop avg | p95 | p99 | max | Main avg | GPU avg | GPU p95 | GC KB/frame | Batches | SetPass | Draw calls | Tris (k) | Shadow casters | Lights |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            void Row(string name, List<Frame> s)
            {
                if (s.Count == 0) return;
                var loop = s.Select(f => f.PlayerLoopMs).OrderBy(v => v).ToList();
                float P(float q) => loop[Mathf.Clamp(Mathf.CeilToInt(q * loop.Count) - 1, 0, loop.Count - 1)];
                var gpu = s.Select(f => f.GpuMs).Where(v => v > 0f).OrderBy(v => v).ToList();
                string gpuAvg = gpu.Count > 0 ? $"{gpu.Average():0.00} ms" : "n/a", gpuP95 = gpu.Count > 0 ? $"{gpu[Mathf.Clamp(Mathf.CeilToInt(0.95f * gpu.Count) - 1, 0, gpu.Count - 1)]:0.00}" : "n/a";
                sb.AppendLine($"| {name} | {s.Count} | {loop.Average():0.00} ms | {P(0.95f):0.00} | {P(0.99f):0.00} | {loop[loop.Count - 1]:0.00} | {s.Average(f => f.MainMs):0.00} ms | {gpuAvg} | {gpuP95} | " +
                              $"{s.Average(f => f.GcKb):0.0} | {s.Average(f => f.Batches):0} | {s.Average(f => f.SetPass):0} | {s.Average(f => f.DrawCalls):0} | " +
                              $"{s.Average(f => f.Triangles) / 1000f:0} | {s.Average(f => f.ShadowCasters):0} | {s.Average(f => f.Lights):0} |");
            }
            Row("Approach (objective 1)", data.Where(f => f.Objective == 0).ToList());
            Row("Heist (objectives 2–3)", data.Where(f => f.Objective is 1 or 2).ToList());
            Row("Escape (lockdown)", data.Where(f => f.Objective >= 3).ToList());
            Row("Whole route", data);
            sb.AppendLine();
            sb.AppendLine("Slowest seconds (PlayerLoop average):");
            foreach (var g in data.GroupBy(f => Mathf.FloorToInt(f.Time)).OrderByDescending(g => g.Average(f => f.PlayerLoopMs)).Take(6))
            {
                var last = g.Last();
                sb.AppendLine($"  t={g.Key,4}s mission {last.Elapsed,5:0.0}s at ({last.Position.x:0},{last.Position.z:0}) {last.Security}: {g.Average(f => f.PlayerLoopMs):0.00} ms avg, GPU {g.Average(f => f.GpuMs):0.00} ms, " +
                              $"{g.Max(f => f.PlayerLoopMs):0.00} max, {g.Average(f => f.Batches):0} batches, {g.Average(f => f.Triangles) / 1000f:0}k tris, {g.Average(f => f.Lights):0} lights");
            }
            int gcFrames = data.Count(f => f.GcKb > 0.01f);
            sb.AppendLine($"Frames allocating managed memory: {gcFrames} of {data.Count} ({(data.Count > 0 ? gcFrames * 100f / data.Count : 0f):0.0}%), total {data.Sum(f => f.GcKb) / 1024f:0.00} MB");
            System.IO.File.WriteAllText($"{Folder}/{prefix}_summary.md", sb.ToString());
            Debug.Log(sb.ToString());
        }

        private static void Dispose()
        {
            playerLoop.Dispose();
            mainThread.Dispose();
            gcAlloc.Dispose();
            batches.Dispose();
            setPass.Dispose();
            drawCalls.Dispose();
            triangles.Dispose();
            shadowCasters.Dispose();
        }
    }
}
