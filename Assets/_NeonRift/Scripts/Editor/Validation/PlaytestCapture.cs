using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Timed Play Mode screenshots taken from inside the editor loop: optionally taps a key at t = 0, then captures the
    /// Game view (with UI Toolkit) at the given unscaled times. Tool calls stall the main thread, so captures scheduled
    /// from outside land at the wrong moment of a sequence; this runs on the game's own clock.
    /// Writes Logs/Playtest/&lt;prefix&gt;_NN_&lt;t&gt;s.png and Logs/Playtest/&lt;prefix&gt;_done.txt.
    /// </summary>
    public static class PlaytestCapture
    {
        private static readonly List<float> pending = new();
        private static float start;
        private static string name;
        private static int count;

        public static string Schedule(string prefix, Key key, params float[] times)
        {
            if (!Application.isPlaying) return "not in Play Mode";
            name = prefix;
            count = 0;
            pending.Clear();
            pending.AddRange(times);
            pending.Sort();
            System.IO.Directory.CreateDirectory(InputPlaytest.CaptureFolder);
            System.IO.File.Delete($"{InputPlaytest.CaptureFolder}/{prefix}_done.txt");
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if (key != Key.None) InputPlaytest.Tap(key);
            start = Time.unscaledTime;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return $"{times.Length} captures scheduled";
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { Finish(); return; }
            float t = Time.unscaledTime - start;
            while (pending.Count > 0 && t >= pending[0])
            {
                ScreenCapture.CaptureScreenshot($"{InputPlaytest.CaptureFolder}/{name}_{count++:00}_{t:0.0}s.png");
                pending.RemoveAt(0);
            }
            if (pending.Count == 0) Finish();
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            System.IO.File.WriteAllText($"{InputPlaytest.CaptureFolder}/{name}_done.txt", $"{count} frames");
        }
    }
}
