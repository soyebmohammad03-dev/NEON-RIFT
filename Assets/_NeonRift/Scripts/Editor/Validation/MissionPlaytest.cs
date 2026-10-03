using System.Collections.Generic;
using System.Text;
using NeonRift.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonRift.EditorTools.Validation
{
    /// <summary>
    /// Play Mode harness around <see cref="NightRunValidator"/>: starts a scenario on the running mission, captures the
    /// Game view (with the HUD) a moment after chosen world events and interaction stages, and writes the validator's
    /// report to Logs/Playtest/&lt;prefix&gt;_report.txt when the run ends. Runs on the editor loop, so tool calls do
    /// not have to poll during the run.
    /// </summary>
    public static class MissionPlaytest
    {
        private static readonly List<(float time, string label)> pending = new();
        private static readonly Dictionary<string, float> captureEvents = new();
        private static NightRunValidator validator;
        private static string prefix;
        private static int shots;
        private static float startedAt;

        /// <summary>
        /// Starts <paramref name="scenario"/>. <paramref name="captures"/>: "eventId@delay" (world event) or
        /// "stage:LABEL@delay" (an interaction stage starting), plus "t@seconds" for plain timed shots. Prefix any rule
        /// with "aerial:" for a high three-quarter view framing the player and every rival instead of the game view.
        /// </summary>
        public static string Begin(NightRunValidator.Scenario scenario, string capturePrefix, float timeScale, params string[] captures)
        {
            if (!Application.isPlaying) return "not in Play Mode";
            var entry = Object.FindAnyObjectByType<MissionSceneEntry>();
            validator = Object.FindAnyObjectByType<NightRunValidator>();
            if (entry == null || entry.Director == null || entry.Director.Progress == null || validator == null) return "mission not running yet";
            Application.runInBackground = true;
            Time.timeScale = timeScale;
            Time.maximumDeltaTime = 0.1f;
            prefix = capturePrefix;
            shots = 0;
            pending.Clear();
            captureEvents.Clear();
            startedAt = Time.unscaledTime;
            System.IO.Directory.CreateDirectory(InputPlaytest.CaptureFolder);
            System.IO.File.Delete($"{InputPlaytest.CaptureFolder}/{prefix}_report.txt");
            foreach (var c in captures)
            {
                int at = c.LastIndexOf('@');
                string key = at < 0 ? c : c.Substring(0, at);
                float delay = at < 0 ? 0f : float.Parse(c.Substring(at + 1), System.Globalization.CultureInfo.InvariantCulture);
                string view = key.StartsWith("aerial:") ? "aerial" : key.StartsWith("rival0:") ? "rival0" : key.StartsWith("rival1:") ? "rival1" : null;
                if (view != null) key = key.Substring(view.Length + 1);
                if (key == "t") pending.Add((Time.unscaledTime + delay, (view != null ? view + "_" : string.Empty) + "t" + delay.ToString("0")));
                else captureEvents[(view != null ? view + ":" : string.Empty) + key] = delay;
            }
            entry.Director.World.EventRaised += e => Trigger(e);
            foreach (var i in Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None))
                i.FeedbackRaised += (source, f) =>
                {
                    if (f is Interactable.Feedback.Started or Interactable.Feedback.StepStarted && source.Run?.Current != null)
                        Trigger("stage:" + source.Run.Current.Label);
                    if (f == Interactable.Feedback.Interference) Trigger("interference");
                };
            bool ok = validator.Begin(entry.Director, scenario);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return ok ? $"{scenario} started, {captures.Length} capture rules" : validator.Report;
        }

        private static void Trigger(string key)
        {
            foreach (var k in new[] { key, "aerial:" + key, "rival0:" + key, "rival1:" + key })
            {
                if (!captureEvents.TryGetValue(k, out float delay)) continue;
                captureEvents.Remove(k);
                pending.Add((Time.unscaledTime + delay, k.Replace(':', '_').Replace(' ', '_').Replace('.', '_')));
            }
        }

        /// <summary>A chase view behind rival <paramref name="index"/> (shows the crews racing each other and the player).</summary>
        public static void CaptureChase(string file, int index)
        {
            var rivals = Object.FindAnyObjectByType<RivalDirector>();
            if (rivals == null || index >= rivals.Rivals.Count || rivals.Rivals[index].Car == null) return;
            var car = rivals.Rivals[index].Car.transform;
            Vector3 forward = car.forward;
            forward.y = 0f;
            forward.Normalize();
            Render(file, car.position - forward * 9.5f + Vector3.up * 3.4f, car.position + forward * 8f + Vector3.up * 0.8f, 58f);
        }

        private static void Render(string file, Vector3 position, Vector3 lookAt, float fov)
        {
            var go = new GameObject("PlaytestCaptureCamera");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 3000f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            go.transform.position = position;
            go.transform.LookAt(lookAt);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }

        /// <summary>Renders a high three-quarter view framing the player and the rivals to a PNG (a temporary URP camera).</summary>
        public static void CaptureAerial(string file)
        {
            var points = new List<Vector3>();
            var entry = Object.FindAnyObjectByType<MissionSceneEntry>();
            if (entry != null && entry.Director != null && entry.Director.Player != null) points.Add(entry.Director.Player.transform.position);
            var rivals = Object.FindAnyObjectByType<RivalDirector>();
            // Frame the cars near the player (a distant rival would shrink everything to dots).
            if (rivals != null)
                foreach (var r in rivals.Rivals)
                    if (r.Car != null && (points.Count == 0 || Vector3.Distance(r.Car.transform.position, points[0]) < 150f)) points.Add(r.Car.transform.position);
            if (points.Count == 0) return;
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            float span = Mathf.Clamp(bounds.size.magnitude, 12f, 300f);
            var go = new GameObject("AerialCaptureCamera");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 3000f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Vector3 centre = bounds.center;
            // Low three-quarter view from behind the player's heading, close enough to read the cars.
            Vector3 back = entry != null && entry.Director != null && entry.Director.Player != null ? -entry.Director.Player.transform.forward : Vector3.back;
            back.y = 0f;
            Vector3 dir = (back.normalized * 0.8f + Vector3.Cross(Vector3.up, back.normalized) * 0.35f + Vector3.up * 0.42f).normalized;
            go.transform.position = centre + dir * (span * 0.75f + 16f);
            go.transform.LookAt(centre + Vector3.up * 1f);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { Finish("left Play Mode"); return; }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < pending[i].time) continue;
                string file = $"{InputPlaytest.CaptureFolder}/{prefix}_{shots++:00}_{pending[i].label}.png";
                if (pending[i].label.StartsWith("aerial")) CaptureAerial(file);
                else if (pending[i].label.StartsWith("rival")) CaptureChase(file, pending[i].label[5] - '0');
                else ScreenCapture.CaptureScreenshot(file);
                pending.RemoveAt(i);
            }
            if (validator != null && !validator.Running && pending.Count == 0 && Time.unscaledTime - startedAt > 2f) Finish(null);
        }

        private static void Finish(string reason)
        {
            EditorApplication.update -= Tick;
            Time.timeScale = 1f;
            var sb = new StringBuilder();
            if (reason != null) sb.AppendLine(reason);
            sb.AppendLine(validator != null ? validator.Report : "no validator");
            sb.AppendLine($"captures: {shots}");
            System.IO.File.WriteAllText($"{InputPlaytest.CaptureFolder}/{prefix}_report.txt", sb.ToString());
        }
    }
}
