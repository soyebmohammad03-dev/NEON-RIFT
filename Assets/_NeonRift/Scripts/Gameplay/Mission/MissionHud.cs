using System;
using System.Collections.Generic;
using NeonRift.Missions;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// The Night Run gameplay HUD (UI Toolkit). A passive view: the <see cref="MissionDirector"/> pushes state in and
    /// receives the two results-screen choices back. Text is only rewritten when the value changes.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MissionHud : MonoBehaviour
    {
        [Tooltip("Seconds a toast stays on screen.")]
        [SerializeField, Min(0.5f)] private float toastSeconds = 3.2f;
        [SerializeField, Range(1, 6)] private int maxToasts = 4;
        [Tooltip("Keeps the waypoint marker this far inside the screen edge, px (panel space).")]
        [SerializeField] private float edgeMargin = 70f;

        private VisualElement root, frame, objective, waypoint, waypointArrow, security, heatFill, prompt, promptFill, rpmFill, toasts, banner, results, resultsStats;
        private Label objectiveStep, objectiveTitle, objectiveDetail, objectiveDistance, waypointLabel, waypointDistance;
        private Label securityState, heatLabel, timer, countdown, promptText, promptHint, speed, gear, vehicleName, bannerTitle, bannerSubtitle, resultsTitle, resultsReason;
        private Button retryButton, continueButton;
        private readonly List<(Label label, float expires)> activeToasts = new();
        private float bannerHideAt;
        private int lastSpeed = -1, lastDistance = -1, lastWaypointDistance = -1, lastTimerTenths = -1, lastCountdown = -1;
        private string lastGear;

        public event Action RetryClicked;
        public event Action ContinueClicked;
        public bool ResultsVisible => results != null && !results.ClassListContains("nr-hidden");

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            frame = root.Q("lockdown-frame");
            objective = root.Q("objective");
            objectiveStep = root.Q<Label>("objective-step");
            objectiveTitle = root.Q<Label>("objective-title");
            objectiveDetail = root.Q<Label>("objective-detail");
            objectiveDistance = root.Q<Label>("objective-distance");
            waypoint = root.Q("waypoint");
            waypointArrow = root.Q("waypoint-arrow");
            waypointLabel = root.Q<Label>("waypoint-label");
            waypointDistance = root.Q<Label>("waypoint-distance");
            security = root.Q("security");
            securityState = root.Q<Label>("security-state");
            heatFill = root.Q("heat-fill");
            heatLabel = root.Q<Label>("heat-label");
            timer = root.Q<Label>("timer");
            countdown = root.Q<Label>("countdown");
            toasts = root.Q("toasts");
            banner = root.Q("banner");
            bannerTitle = root.Q<Label>("banner-title");
            bannerSubtitle = root.Q<Label>("banner-subtitle");
            prompt = root.Q("prompt");
            promptText = root.Q<Label>("prompt-text");
            promptHint = root.Q<Label>("prompt-hint");
            promptFill = root.Q("prompt-fill");
            speed = root.Q<Label>("speed");
            gear = root.Q<Label>("gear");
            rpmFill = root.Q("rpm-fill");
            vehicleName = root.Q<Label>("vehicle-name");
            results = root.Q("results");
            resultsTitle = root.Q<Label>("results-title");
            resultsReason = root.Q<Label>("results-reason");
            resultsStats = root.Q("results-stats");
            retryButton = root.Q<Button>("retry-button");
            continueButton = root.Q<Button>("continue-button");
            retryButton.clicked += OnRetry;
            continueButton.clicked += OnContinue;
            Clear();
        }

        private void OnDisable()
        {
            if (retryButton != null) retryButton.clicked -= OnRetry;
            if (continueButton != null) continueButton.clicked -= OnContinue;
        }

        private void OnRetry() => RetryClicked?.Invoke();
        private void OnContinue() => ContinueClicked?.Invoke();

        /// <summary>Resets every element to its idle state.</summary>
        public void Clear()
        {
            if (root == null) return;
            root.style.display = DisplayStyle.Flex;
            Show(objective, false);
            Show(waypoint, false);
            prompt.RemoveFromClassList("nr-prompt--visible");
            results.AddToClassList("nr-hidden");
            banner.RemoveFromClassList("nr-banner--visible");
            frame.style.opacity = 0f;
            toasts.Clear();
            activeToasts.Clear();
            SetSecurity(SecurityLevel.Calm, 0f);
            SetTimer(false, 0f);
            SetCountdown(null, 0f);
        }

        public void SetVehicleName(string text) => vehicleName.text = text?.ToUpperInvariant() ?? string.Empty;

        public void SetObjective(int step, int total, string title, string detail, bool lockdown)
        {
            Show(objective, !string.IsNullOrEmpty(title));
            objectiveStep.text = $"OBJECTIVE {step}/{total}";
            objectiveTitle.text = title;
            objectiveDetail.text = detail;
            objective.EnableInClassList("nr-objective--lockdown", lockdown);
            // Slide the panel back in so the change is noticed.
            objective.AddToClassList("nr-objective--refresh");
            objective.schedule.Execute(() => objective.RemoveFromClassList("nr-objective--refresh")).StartingIn(60);
            lastDistance = -1;
        }

        public void HideObjective() => Show(objective, false);

        public void SetObjectiveDistance(float metres)
        {
            int rounded = Mathf.RoundToInt(metres / 5f) * 5;
            if (rounded == lastDistance) return;
            lastDistance = rounded;
            objectiveDistance.text = metres < 0f ? string.Empty : $"{rounded} M";
        }

        /// <summary>Places the waypoint marker. <paramref name="viewport"/> is the target in viewport space (z = depth).</summary>
        public void SetWaypoint(bool visible, Vector3 viewport, string label, float metres, bool extraction)
        {
            Show(waypoint, visible);
            if (!visible) return;
            var size = root.layout.size;
            if (size.x <= 0f || size.y <= 0f) return;

            bool behind = viewport.z < 0f;
            Vector2 p = new(viewport.x, 1f - viewport.y);
            if (behind) p = Vector2.one - p;
            Vector2 centre = new(0.5f, 0.5f);
            Vector2 px = Vector2.Scale(p, size);
            Vector2 min = new(edgeMargin, edgeMargin), max = size - min;
            bool offscreen = behind || px.x < min.x || px.x > max.x || px.y < min.y || px.y > max.y;
            if (offscreen)
            {
                // Push the marker to the screen edge along the direction from the centre. A target behind the camera
                // goes to the left or right edge (never the bottom, where it would sit on the speedometer).
                Vector2 dir = p - centre;
                if (behind) dir = new Vector2(Mathf.Abs(dir.x) < 1e-3f ? 1f : Mathf.Sign(dir.x), Mathf.Min(dir.y, 0f) * 0.5f);
                dir = Vector2.Scale(dir, size);
                Vector2 half = size * 0.5f - min;
                float scale = Mathf.Min(half.x / Mathf.Max(1e-3f, Mathf.Abs(dir.x)), half.y / Mathf.Max(1e-3f, Mathf.Abs(dir.y)));
                px = size * 0.5f + dir * scale;
                waypointArrow.style.rotate = new Rotate(Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg);
            }
            waypoint.EnableInClassList("nr-waypoint--offscreen", offscreen);
            waypoint.EnableInClassList("nr-waypoint--extraction", extraction);
            waypoint.style.left = px.x;
            waypoint.style.top = px.y;
            waypointLabel.text = label;
            int rounded = Mathf.RoundToInt(metres);
            if (rounded != lastWaypointDistance)
            {
                lastWaypointDistance = rounded;
                waypointDistance.text = $"{rounded} M";
            }
        }

        public void SetSecurity(SecurityLevel level, float heat)
        {
            securityState.text = level switch { SecurityLevel.Lockdown => "LOCKDOWN", SecurityLevel.Alert => "ALERT", _ => "CALM" };
            security.EnableInClassList("nr-security--alert", level == SecurityLevel.Alert);
            security.EnableInClassList("nr-security--lockdown", level == SecurityLevel.Lockdown);
            heatFill.style.width = Length.Percent(Mathf.Clamp01(heat) * 100f);
            heatLabel.text = $"HEAT {Mathf.RoundToInt(heat * 100f)}%";
            frame.style.opacity = level == SecurityLevel.Lockdown ? 1f : 0f;
        }

        public void SetTimer(bool visible, float seconds)
        {
            Show(timer, visible);
            if (!visible) { lastTimerTenths = -1; return; }
            int tenths = Mathf.CeilToInt(seconds * 10f);
            if (tenths == lastTimerTenths) return;
            lastTimerTenths = tenths;
            int whole = tenths / 10;
            timer.text = seconds < 10f ? $"TRACE  {whole:00}.{tenths % 10}" : $"TRACE  {whole / 60}:{whole % 60:00}";
            timer.EnableInClassList("nr-timer--critical", seconds < 15f);
        }

        public void SetCountdown(string label, float seconds)
        {
            bool visible = !string.IsNullOrEmpty(label);
            Show(countdown, visible);
            if (!visible) { lastCountdown = -1; return; }
            int s = Mathf.CeilToInt(seconds);
            if (s == lastCountdown) return;
            lastCountdown = s;
            countdown.text = $"{label}  {s}s";
        }

        public void SetPrompt(bool visible, string text, float progress, string hint, bool blocked)
        {
            prompt.EnableInClassList("nr-prompt--visible", visible);
            if (!visible) return;
            promptText.text = text;
            promptFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
            promptHint.text = hint ?? string.Empty;
            prompt.EnableInClassList("nr-prompt--blocked", blocked);
        }

        public void SetSpeed(float kph, int gearIndex, float rpm01)
        {
            int s = Mathf.RoundToInt(Mathf.Abs(kph));
            if (s != lastSpeed)
            {
                lastSpeed = s;
                speed.text = s.ToString();
            }
            string g = gearIndex < 0 ? "R" : gearIndex == 0 ? "N" : gearIndex.ToString();
            if (g != lastGear)
            {
                lastGear = g;
                gear.text = g;
            }
            rpmFill.style.width = Length.Percent(Mathf.Clamp01(rpm01) * 100f);
            rpmFill.EnableInClassList("nr-speedo__rpm-fill--redline", rpm01 > 0.92f);
        }

        public void Toast(string text, MessageTone tone)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("nr-toast");
            label.AddToClassList("nr-toast--enter");
            if (tone != MessageTone.Info) label.AddToClassList("nr-toast--" + tone.ToString().ToLowerInvariant());
            toasts.Insert(0, label);
            label.schedule.Execute(() => label.RemoveFromClassList("nr-toast--enter")).StartingIn(30);
            activeToasts.Add((label, Time.unscaledTime + toastSeconds));
            while (activeToasts.Count > maxToasts)
            {
                activeToasts[0].label.RemoveFromHierarchy();
                activeToasts.RemoveAt(0);
            }
        }

        public void Banner(string title, string subtitle, MessageTone tone, float seconds)
        {
            bannerTitle.text = title;
            bannerSubtitle.text = subtitle ?? string.Empty;
            banner.EnableInClassList("nr-banner--danger", tone == MessageTone.Danger);
            banner.EnableInClassList("nr-banner--success", tone == MessageTone.Success);
            banner.AddToClassList("nr-banner--visible");
            bannerHideAt = Time.unscaledTime + seconds;
        }

        public void ShowResults(bool success, string reason, IEnumerable<(string name, string value)> stats)
        {
            SetPrompt(false, null, 0f, null, false);
            Show(waypoint, false);
            results.RemoveFromClassList("nr-hidden");
            results.EnableInClassList("nr-results--failed", !success);
            resultsTitle.text = success ? "MISSION COMPLETE" : "MISSION FAILED";
            resultsReason.text = reason;
            resultsStats.Clear();
            foreach (var (name, value) in stats)
            {
                var row = new VisualElement();
                row.AddToClassList("nr-results__stat");
                var n = new Label(name);
                n.AddToClassList("nr-results__stat-name");
                var v = new Label(value);
                v.AddToClassList("nr-results__stat-value");
                row.Add(n);
                row.Add(v);
                resultsStats.Add(row);
            }
            retryButton.Focus();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (bannerHideAt > 0f && now >= bannerHideAt)
            {
                banner.RemoveFromClassList("nr-banner--visible");
                bannerHideAt = 0f;
            }
            for (int i = activeToasts.Count - 1; i >= 0; i--)
            {
                var (label, expires) = activeToasts[i];
                if (now < expires) continue;
                if (!label.ClassListContains("nr-toast--leave"))
                {
                    label.AddToClassList("nr-toast--leave");
                    activeToasts[i] = (label, now + 0.35f);
                }
                else
                {
                    label.RemoveFromHierarchy();
                    activeToasts.RemoveAt(i);
                }
            }
        }

        private static void Show(VisualElement e, bool visible) => e.EnableInClassList("nr-hidden", !visible);
    }
}
