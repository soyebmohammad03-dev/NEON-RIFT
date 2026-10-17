using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Harbor Rain's HUD, built in code on the project's panel settings and theme: objective (top left), escape timer
    /// (top centre), warnings (right), banner, hold-to-bypass bar, a world waypoint and the results / pause cards.
    /// Same palette as the Night Run HUD (cyan accents, dark translucent panels, amber warnings, red lockdown).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HarborRainHud : MonoBehaviour
    {
        private static readonly Color Cyan = new(0.33f, 0.91f, 1f);
        private static readonly Color Amber = new(1f, 0.7f, 0.2f);
        private static readonly Color Red = new(1f, 0.28f, 0.25f);
        private static readonly Color Panel = new(0.016f, 0.027f, 0.047f, 0.62f);
        private static readonly Color Text = new(0.9f, 0.96f, 1f);

        private VisualElement root, marker, holdFill, results, pause, warnings, holdBox, alertFill, stepsBox;
        private Label alertLabel, status;
        private readonly List<Label> stepLabels = new();
        public HarborMinimap Minimap { get; private set; }
        private Label step, title, detail, distance, timer, banner, holdLabel, markerLabel, resultsTitle, resultsBody;
        private float bannerUntil;
        private readonly List<(Label label, float until)> toasts = new();

        // Command centre: radio subtitle, device (phone-style) messages, puzzle console, garage, lens water, letterbox.
        private VisualElement radioBox, device, deviceHistory, lens, letterTop, letterBottom, garage, optionalBox;
        private Label radioSpeaker, radioText, deviceSender, deviceText, deviceHint, garageTitle, garageBody, skipLabel, optional;
        private float radioUntil, deviceUntil, deviceFlicker;
        private readonly List<(string sender, string text, bool critical)> history = new();
        public HarborPuzzleView PuzzleView { get; private set; }
        public bool DeviceExpanded { get; private set; }
        public IReadOnlyList<(string sender, string text, bool critical)> History => history;
        public int MessageCount => history.Count;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.right = root.style.top = root.style.bottom = 0;

            var objective = Box(root, 56, 48, null, null, 520);
            step = Add(objective, 13, Cyan, 3);
            title = Add(objective, 30, Text, 1, true);
            detail = Add(objective, 16, new Color(0.75f, 0.85f, 0.92f), 0);
            distance = Add(objective, 15, Cyan, 2);

            // Cargo / security progress: the operation's steps under the objective.
            stepsBox = new VisualElement { pickingMode = PickingMode.Ignore };
            stepsBox.style.marginTop = 10;
            objective.Add(stepsBox);

            // Security alert meter and storm / flood status, top right.
            var security = Box(root, null, 40, 56, null, 360);
            security.style.borderLeftColor = Amber;
            alertLabel = Add(security, 14, Text, 3, true);
            var alertTrack = new VisualElement();
            alertTrack.style.height = 8; alertTrack.style.marginTop = 6;
            alertTrack.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            alertFill = new VisualElement();
            alertFill.style.height = Length.Percent(100);
            alertTrack.Add(alertFill);
            security.Add(alertTrack);
            status = Add(security, 13, new Color(0.7f, 0.82f, 0.9f), 2);
            status.style.marginTop = 8;

            Minimap = new HarborMinimap();
            Minimap.style.position = Position.Absolute;
            Minimap.style.left = 40;
            Minimap.style.bottom = 40;
            root.Add(Minimap);

            timer = new Label();
            Style(timer, 34, Red, 3, true);
            timer.style.position = Position.Absolute;
            timer.style.top = 40;
            timer.style.left = 0; timer.style.right = 0;
            timer.style.unityTextAlign = TextAnchor.UpperCenter;
            root.Add(timer);

            warnings = new VisualElement { pickingMode = PickingMode.Ignore };
            warnings.style.position = Position.Absolute;
            warnings.style.right = 56; warnings.style.top = 330;
            warnings.style.width = 460;
            warnings.style.alignItems = Align.FlexEnd;
            root.Add(warnings);

            banner = new Label();
            Style(banner, 46, Text, 8, true);
            banner.style.position = Position.Absolute;
            banner.style.top = Length.Percent(28);
            banner.style.left = 0; banner.style.right = 0;
            banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(banner);

            holdBox = Box(root, null, null, null, 210, 520);
            holdBox.style.left = Length.Percent(50);
            holdBox.style.translate = new Translate(Length.Percent(-50), 0);
            holdLabel = Add(holdBox, 16, Text, 3, true);
            holdLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            var track = new VisualElement();
            track.style.height = 10; track.style.marginTop = 10;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            holdFill = new VisualElement();
            holdFill.style.height = Length.Percent(100);
            holdFill.style.backgroundColor = Cyan;
            track.Add(holdFill);
            holdBox.Add(track);

            marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            var diamond = new VisualElement();
            diamond.style.width = 18; diamond.style.height = 18;
            diamond.style.left = -9; diamond.style.top = -9;
            diamond.style.rotate = new Rotate(new Angle(45f));
            diamond.style.borderLeftWidth = diamond.style.borderRightWidth = diamond.style.borderTopWidth = diamond.style.borderBottomWidth = 3;
            diamond.style.borderLeftColor = diamond.style.borderRightColor = diamond.style.borderTopColor = diamond.style.borderBottomColor = Cyan;
            diamond.style.position = Position.Absolute;
            marker.Add(diamond);
            markerLabel = new Label();
            Style(markerLabel, 14, Cyan, 2, true);
            markerLabel.style.position = Position.Absolute;
            markerLabel.style.top = 16; markerLabel.style.left = -150; markerLabel.style.width = 300;
            markerLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            marker.Add(markerLabel);
            root.Add(marker);

            results = Card(out resultsTitle, out resultsBody);
            pause = Card(out var pauseTitle, out var pauseBody);
            pauseTitle.text = "PAUSED";
            pauseBody.text = "ESC  RESUME\nE  RESTART HARBOR RAIN\nG  GARAGE (CHANGE CAR)\nR  RECOVER VEHICLE";

            BuildCommand();

            ShowHold(false, 0f, null);
            ShowResults(false, null, null);
            ShowPause(false);
            SetTimer(null, false);
            banner.text = "";
        }

        // ---------------- API ----------------

        public void SetObjective(string stepText, string titleText, string detailText)
        {
            step.text = stepText;
            title.text = titleText;
            detail.text = detailText;
        }

        public void SetSteps(string[] steps, int current)
        {
            while (stepLabels.Count < steps.Length)
            {
                var l = Add(stepsBox, 13, Text, 1);
                stepLabels.Add(l);
            }
            for (int i = 0; i < stepLabels.Count; i++)
            {
                var l = stepLabels[i];
                // 2.2: a window of the previous, current and next step keeps the panel compact while driving
                // (the step counter above already shows the operation's progress).
                bool shown = i < steps.Length && i >= current - 1 && i <= current + 1;
                l.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown) continue;
                bool done = i < current, now = i == current;
                l.text = (done ? "■  " : now ? "▶  " : "□  ") + steps[i];
                l.style.color = done ? new Color(0.45f, 0.75f, 0.6f) : now ? Cyan : new Color(0.55f, 0.6f, 0.66f);
            }
        }

        public void SetAlert(float level, string label, bool alarm)
        {
            alertLabel.text = label;
            alertFill.style.width = Length.Percent(Mathf.Clamp01(level) * 100f);
            alertFill.style.backgroundColor = alarm ? Red : Color.Lerp(Cyan, Amber, level);
        }

        /// <summary>Storm / flood / police status lines; long closure lists are capped so the panel never covers the road.</summary>
        public void SetStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) { status.text = ""; return; }
            var lines = text.Split('\n');
            const int max = 5;
            status.text = lines.Length <= max ? text : string.Join("\n", lines, 0, max) + $"\n+{lines.Length - max} MORE";
        }

        public void SetDistance(float metres) => distance.text = metres >= 0f ? $"{metres:0} M" : "";

        public void SetTimer(string text, bool urgent)
        {
            timer.text = text ?? "";
            timer.style.color = urgent ? Red : Amber;
        }

        public void Banner(string text, Color? colour = null, float seconds = 2.6f)
        {
            banner.text = text;
            banner.style.color = colour ?? Text;
            bannerUntil = Time.unscaledTime + seconds;
        }

        public void Warning(string text, bool severe, float seconds = 7f)
        {
            var label = new Label(text);
            Style(label, 16, severe ? Red : Amber, 2, true);
            label.style.backgroundColor = Panel;
            label.style.paddingLeft = label.style.paddingRight = 14;
            label.style.paddingTop = label.style.paddingBottom = 8;
            label.style.marginBottom = 8;
            label.style.borderLeftWidth = 4;
            label.style.borderLeftColor = severe ? Red : Amber;
            warnings.Add(label);
            toasts.Add((label, Time.unscaledTime + seconds));
            // Never more than three stacked warnings: the oldest gives way.
            while (toasts.Count > 3) { toasts[0].label.RemoveFromHierarchy(); toasts.RemoveAt(0); }
        }

        public void ShowHold(bool visible, float progress, string label)
        {
            holdBox.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            holdLabel.text = label;
            holdFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
        }

        public void SetMarker(Camera cam, Vector3? world, string label)
        {
            if (cam == null || world == null) { marker.style.display = DisplayStyle.None; return; }
            var vp = cam.WorldToViewportPoint(world.Value + Vector3.up * 3f);
            if (vp.z < 0f) { vp.x = 1f - vp.x; vp.y = 0.82f; }
            vp.x = Mathf.Clamp(vp.x, 0.04f, 0.96f);
            vp.y = Mathf.Clamp(vp.y, 0.3f, 0.86f);
            // Keep clear of the objective panel (top left) and the security panel (top right).
            if (vp.y > 0.55f) vp.x = Mathf.Clamp(vp.x, 0.3f, 0.7f);
            var size = root.layout.size;
            if (float.IsNaN(size.x) || size.x <= 0f) return;
            marker.style.display = DisplayStyle.Flex;
            marker.style.left = vp.x * size.x;
            marker.style.top = (1f - vp.y) * size.y;
            markerLabel.text = label;
        }

        public void ShowResults(bool visible, string titleText, string body)
        {
            results.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            resultsTitle.text = titleText;
            resultsBody.text = body;
        }

        public void ShowPause(bool visible) => pause.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void ClearWarnings()
        {
            foreach (var t in toasts) t.label.RemoveFromHierarchy();
            toasts.Clear();
        }

        private void Update()
        {
            TickCommand();
            if (banner.text.Length > 0 && Time.unscaledTime > bannerUntil) banner.text = "";
            for (int i = toasts.Count - 1; i >= 0; i--)
                if (Time.unscaledTime > toasts[i].until) { toasts[i].label.RemoveFromHierarchy(); toasts.RemoveAt(i); }
        }

        // ---------------- Command centre ----------------

        private void BuildCommand()
        {
            lens = new VisualElement { pickingMode = PickingMode.Ignore };
            lens.style.position = Position.Absolute;
            lens.style.left = lens.style.right = lens.style.top = lens.style.bottom = 0;
            lens.style.backgroundColor = new Color(0.55f, 0.65f, 0.75f, 0f);
            lens.style.backgroundImage = new StyleBackground(Droplets());
            lens.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, 0f);
            root.Insert(0, lens);

            letterTop = Bar(true); letterBottom = Bar(false);

            optionalBox = Box(root, 56, null, null, 360, 420);
            optionalBox.style.borderLeftColor = new Color(0.7f, 0.6f, 1f);
            optional = Add(optionalBox, 13, new Color(0.8f, 0.78f, 1f), 1);

            radioBox = Box(root, null, null, null, 120, 760);
            radioBox.style.left = Length.Percent(50);
            radioBox.style.translate = new Translate(Length.Percent(-50), 0);
            radioBox.style.borderLeftColor = Amber;
            radioSpeaker = Add(radioBox, 13, Amber, 4, true);
            radioText = Add(radioBox, 19, Text, 0.5f);

            device = Box(root, null, null, 56, 40, 380);
            device.style.borderLeftColor = new Color(0.7f, 0.6f, 1f);
            var head = Add(device, 11, new Color(0.7f, 0.6f, 1f), 4, true);
            head.text = "◈  RIFTLINK  ·  ENCRYPTED";
            deviceSender = Add(device, 13, Cyan, 2, true);
            deviceSender.style.marginTop = 6;
            deviceText = Add(device, 15, Text, 0);
            deviceHistory = new VisualElement { pickingMode = PickingMode.Ignore };
            deviceHistory.style.marginTop = 8;
            device.Add(deviceHistory);
            deviceHint = Add(device, 11, new Color(0.55f, 0.6f, 0.68f), 1);
            deviceHint.style.marginTop = 6;
            deviceHint.text = "TAB  MESSAGE LOG (parked)";

            PuzzleView = new HarborPuzzleView();
            root.Add(PuzzleView);

            garage = Card(out garageTitle, out garageBody);
            garage.pickingMode = PickingMode.Ignore;

            skipLabel = new Label("E  SKIP");
            Style(skipLabel, 14, new Color(0.8f, 0.85f, 0.9f), 3, true);
            skipLabel.style.position = Position.Absolute;
            skipLabel.style.right = 56; skipLabel.style.bottom = 70;
            root.Add(skipLabel);

            ShowRadio(null, null, 0f);
            device.style.display = DisplayStyle.None;
            garage.style.display = DisplayStyle.None;
            ShowLetterbox(false);
            SetOptional(null);
        }

        private VisualElement Bar(bool top)
        {
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Absolute;
            bar.style.left = bar.style.right = 0;
            if (top) bar.style.top = 0; else bar.style.bottom = 0;
            bar.style.height = Length.Percent(10);
            bar.style.backgroundColor = Color.black;
            root.Add(bar);
            return bar;
        }

        public void ShowRadio(string speaker, string text, float seconds, bool critical = false)
        {
            bool on = !string.IsNullOrEmpty(text);
            radioBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            radioSpeaker.text = $"◉  {speaker}";
            radioSpeaker.style.color = critical ? Red : Amber;
            radioBox.style.borderLeftColor = critical ? Red : Amber;
            radioText.text = text;
            radioUntil = Time.unscaledTime + seconds;
        }

        /// <summary>A device message (also the radio log). Compact while driving; TAB expands the log when parked.</summary>
        public void AddMessage(string sender, string text, bool critical)
        {
            history.Add((sender, text, critical));
            if (history.Count > 12) history.RemoveAt(0);
            // The line is already on screen as the spoken radio subtitle: log it, but don't show it twice.
            bool spoken = radioBox.style.display == DisplayStyle.Flex && radioText.text == text;
            if (spoken && !DeviceExpanded) { RefreshHistory(); return; }
            device.style.display = DisplayStyle.Flex;
            deviceSender.text = $"{sender}{(critical ? "  ·  PRIORITY" : "")}";
            deviceSender.style.color = critical ? Red : Cyan;
            deviceText.text = text.Length > 120 && !DeviceExpanded ? text.Substring(0, 117) + "…" : text;
            deviceUntil = Time.unscaledTime + (critical ? 9f : 6f);
            deviceFlicker = 0.4f;
            RefreshHistory();
        }

        /// <summary>A code or instruction card pushed to the device (no radio voice).</summary>
        public void DeviceCard(string sender, string text, bool critical = false) => AddMessage(sender, text, critical);

        public void SetDeviceExpanded(bool on)
        {
            DeviceExpanded = on;
            device.style.width = on ? 560 : 380;
            if (on) { device.style.display = DisplayStyle.Flex; deviceUntil = float.MaxValue; }
            else deviceUntil = Time.unscaledTime + 2f;
            RefreshHistory();
        }

        private void RefreshHistory()
        {
            deviceHistory.Clear();
            if (!DeviceExpanded) return;
            for (int i = history.Count - 2, n = 0; i >= 0 && n < 6; i--, n++)
            {
                var (sender, text, critical) = history[i];
                var l = Add(deviceHistory, 12, critical ? new Color(1f, 0.55f, 0.5f) : new Color(0.7f, 0.8f, 0.88f), 0);
                l.text = $"{sender}:  {text}";
                l.style.marginTop = 4;
            }
        }

        /// <summary>Signal interference on the device during heavy storm (a brief flicker).</summary>
        public void Interference(float amount) => deviceFlicker = Mathf.Max(deviceFlicker, amount);

        public void ShowPuzzle(HarborPuzzle puzzle) => PuzzleView.Show(puzzle);

        public void ShowGarage(bool visible, string titleText = null, string body = null)
        {
            garage.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            garageTitle.text = titleText;
            garageBody.text = body;
        }

        public void SetLens(float water)
        {
            float w = Mathf.Clamp01(water);
            lens.style.backgroundColor = new Color(0.55f, 0.65f, 0.75f, w * 0.06f);   // droplets carry the effect; only a faint film
            lens.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, Mathf.Clamp01(w * 1.1f) * 0.75f);
        }

        /// <summary>Water on the lens: soft droplets with bright rims and a few run-down streaks (generated once).</summary>
        private static Texture2D Droplets()
        {
            const int w = 1024, h = 576;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var rng = new System.Random(4);
            for (int d = 0; d < 220; d++)
            {
                int cx = rng.Next(w), cy = rng.Next(h), r = 1 + rng.Next(rng.NextDouble() < 0.12 ? 6 : 3);
                bool streak = rng.NextDouble() < 0.1;
                int len = streak ? 20 + rng.Next(60) : 0;
                for (int y = -r - len; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    int X = cx + x, Y = cy + y;
                    if (X < 0 || Y < 0 || X >= w || Y >= h) continue;
                    float dist = y < -r ? Mathf.Abs(x) / (r * 0.45f) : Mathf.Sqrt(x * x + y * y) / r;
                    if (dist > 1f) continue;
                    float rim = Mathf.Clamp01((dist - 0.6f) / 0.4f); rim = rim * rim * (3f - 2f * rim);   // brighter toward the droplet edge
                    byte a = (byte)Mathf.Clamp((0.08f + rim * 0.32f) * (y < -r ? 0.4f : 1f) * 255f, 0, 255);
                    var c = px[Y * w + X];
                    if (a > c.a) px[Y * w + X] = new Color32(205, 220, 235, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        public void ShowLetterbox(bool on, bool skippable = false)
        {
            letterTop.style.display = letterBottom.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            skipLabel.style.display = on && skippable ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetOptional(string text)
        {
            optionalBox.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            optional.text = text ?? "";
        }

        private void TickCommand()
        {
            if (radioBox.style.display == DisplayStyle.Flex && Time.unscaledTime > radioUntil) ShowRadio(null, null, 0f);
            if (device.style.display == DisplayStyle.Flex && !DeviceExpanded && Time.unscaledTime > deviceUntil) device.style.display = DisplayStyle.None;
            deviceFlicker = Mathf.Max(0f, deviceFlicker - Time.unscaledDeltaTime);
            device.style.opacity = deviceFlicker > 0f && Random.value < 0.5f ? 0.45f : 1f;
            PuzzleView.Refresh(Time.unscaledDeltaTime);
        }

        // ---------------- Building blocks ----------------

        private VisualElement Box(VisualElement parent, float? left, float? top, float? right, float? bottom, float width)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.position = Position.Absolute;
            if (left.HasValue) box.style.left = left.Value;
            if (top.HasValue) box.style.top = top.Value;
            if (right.HasValue) box.style.right = right.Value;
            if (bottom.HasValue) box.style.bottom = bottom.Value;
            box.style.width = width;
            box.style.backgroundColor = Panel;
            box.style.paddingLeft = box.style.paddingRight = 18;
            box.style.paddingTop = box.style.paddingBottom = 14;
            box.style.borderLeftWidth = 3;
            box.style.borderLeftColor = Cyan;
            parent.Add(box);
            return box;
        }

        private VisualElement Card(out Label heading, out Label body)
        {
            var shade = new VisualElement { pickingMode = PickingMode.Ignore };
            shade.style.position = Position.Absolute;
            shade.style.left = shade.style.right = shade.style.top = shade.style.bottom = 0;
            shade.style.backgroundColor = new Color(0f, 0.01f, 0.02f, 0.6f);
            shade.style.alignItems = Align.Center;
            shade.style.justifyContent = Justify.Center;
            var card = new VisualElement();
            card.style.backgroundColor = Panel;
            card.style.paddingLeft = card.style.paddingRight = 48;
            card.style.paddingTop = card.style.paddingBottom = 36;
            card.style.borderTopWidth = 3;
            card.style.borderTopColor = Cyan;
            card.style.minWidth = 640;
            heading = Add(card, 44, Text, 8, true);
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            body = Add(card, 18, new Color(0.78f, 0.88f, 0.95f), 2);
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            body.style.marginTop = 18;
            shade.Add(card);
            root.Add(shade);
            return shade;
        }

        private static Label Add(VisualElement parent, int size, Color colour, float spacing, bool bold = false)
        {
            var l = new Label();
            Style(l, size, colour, spacing, bold);
            parent.Add(l);
            return l;
        }

        private static void Style(Label l, int size, Color colour, float spacing, bool bold)
        {
            l.pickingMode = PickingMode.Ignore;
            l.style.fontSize = size;
            l.style.color = colour;
            l.style.letterSpacing = spacing;
            l.style.whiteSpace = WhiteSpace.Normal;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
        }
    }
}
