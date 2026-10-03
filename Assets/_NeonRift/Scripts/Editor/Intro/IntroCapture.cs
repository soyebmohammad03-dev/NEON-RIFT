using System.Collections.Generic;
using System.IO;
using NeonRift.Intro;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Intro
{
    /// <summary>
    /// Review tool: while the intro plays in Play Mode, captures the Game view (with overlay) when the Timeline passes
    /// each requested time. Writes <c>_done.txt</c> when finished or when the intro reaches its title hold.
    /// </summary>
    public static class IntroCapture
    {
        private static readonly Queue<float> Pending = new();
        private static string folder;
        private static int count;

        public static string Begin(string outputFolder, params float[] times)
        {
            folder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(folder);
            File.Delete(Path.Combine(folder, "_done.txt"));
            Pending.Clear();
            foreach (float t in times) Pending.Enqueue(t);
            count = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return $"capturing {times.Length} frames into {folder}";
        }

        private static void Tick()
        {
            var entry = Object.FindAnyObjectByType<IntroSceneEntry>();
            if (!EditorApplication.isPlaying || Pending.Count == 0)
            {
                Finish();
                return;
            }
            if (entry == null || !entry.IsPlaying) return;
            if (entry.TimelineTime < Pending.Peek()) return;
            float t = Pending.Dequeue();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"intro_{count++:00}_{t:00.0}s.png"));
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            if (folder != null) File.WriteAllText(Path.Combine(folder, "_done.txt"), count.ToString());
        }
    }
}
