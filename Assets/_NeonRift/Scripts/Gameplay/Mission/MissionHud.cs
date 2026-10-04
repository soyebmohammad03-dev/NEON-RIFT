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
        private VisualElement shiftLight, letterboxTop, letterboxBottom;
        private const int RpmSegments = 12;
        private Label objectiveStep, objectiveTitle, objectiveDetail, objectiveDistance, waypointLabel, waypointDistance;
        private Label securityState, heatLabel, timer, countdown, promptText, promptHint, speed, gear, vehicleName, bannerTitle, bannerSubtitle, resultsTitle, resultsReason;
        private Button retryButton, continueButton;
        private VisualElement briefing, briefObjectives, briefCrew, resultsGrade, resultsSplits, resultsCrews;
        private Label briefKicker, briefTitle, briefTagline, briefText, briefBest, resultsKicker, resultsGradeLetter, resultsGradeScore, resultsBest;
        private Label resultsSplitsCaption, resultsCrewsCaption;
        private VisualElement race, raceRows, navigator;
        private Label racePosition, raceCount, raceGap, navDistrict, navStreet, navRoute;
        private CityMinimap minimap;
        private TerminalReadout terminal;
        private VisualElement stamp, extractFrame;
        private Label stampText;
        private float stampHideAt;
        private bool objectivePending;
        /// <summary>The navigator's map (dev tooling reads its state).</summary>
        public CityMinimap Minimap => minimap;
        private float nextMinimapRepaint;
        private string lastStreet, lastDistrict, lastRoute, lastGap, lastStreetRaw;
        private int lastRouteKey = int.MinValue, lastHeatPercent = -1;
        private SecurityLevel? lastSecurity;
        private readonly List<int> raceRowKeys = new();
        private int lastRacePosition = -1;
        private readonly List<(Label label, float expires)> activeToasts = new();
        private float bannerHideAt;
        private int lastSpeed = -1, lastDistance = -1, lastWaypointDistance = -1, lastTimerTenths = -1, lastCountdown = -1;
        private string lastGear;

        public event Action RetryClicked;
        public event Action ContinueClicked;
        public bool ResultsVisible => results != null && !results.ClassListContains("nr-hidden");
        /// <summary>The HUD's root element (the pause menu overlays it).</summary>
        public VisualElement Root => root;

        /// <summary>Bottom hint: "M  MUSIC ON / OFF".</summary>
        public void SetMusicHint(bool on)
        {
            var hint = root?.Q<Label>("music-hint");
            if (hint == null) return;
            hint.text = on ? "M  ·  MUSIC ON" : "M  ·  MUSIC OFF";
            hint.EnableInClassList("nr-music-hint--off", !on);
        }

        public bool BriefingVisible => briefing != null && briefing.ClassListContains("nr-brief--in");

        /// <summary>The operation card shown as a mission starts.</summary>
        public sealed class Briefing
        {
            public string Kicker, Title, Tagline, Text, Best;
            public readonly List<string> Objectives = new();
            public readonly List<(string name, string role, bool player)> Crew = new();
        }

        /// <summary>The end-of-run debrief.</summary>
        public sealed class Debrief
        {
            public bool Success, NewBest;
            public string Kicker, Title, Reason, Grade, Best;
            public int Score;
            public readonly List<(string name, string value)> Stats = new();
            public readonly List<(string name, string value, bool dim)> Splits = new();
            public readonly List<(string name, string value, bool player)> Crews = new();
        }

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
            shiftLight = root.Q("shift-light");
            letterboxTop = root.Q("letterbox-top");
            letterboxBottom = root.Q("letterbox-bottom");
            var ticks = root.Q("rpm-ticks");
            if (ticks != null && ticks.childCount == 0)
                for (int i = 0; i < RpmSegments; i++)
                {
                    var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                    tick.AddToClassList("nr-speedo__rpm-tick");
                    if (i >= RpmSegments - 2) tick.AddToClassList("nr-speedo__rpm-tick--red");
                    ticks.Add(tick);
                }
            vehicleName = root.Q<Label>("vehicle-name");
            results = root.Q("results");
            resultsTitle = root.Q<Label>("results-title");
            resultsReason = root.Q<Label>("results-reason");
            resultsStats = root.Q("results-stats");
            briefing = root.Q("briefing");
            briefKicker = root.Q<Label>("brief-kicker");
            briefTitle = root.Q<Label>("brief-title");
            briefTagline = root.Q<Label>("brief-tagline");
            briefText = root.Q<Label>("brief-text");
            briefObjectives = root.Q("brief-objectives");
            briefCrew = root.Q("brief-crew");
            briefBest = root.Q<Label>("brief-best");
            resultsKicker = root.Q<Label>("results-kicker");
            resultsGrade = root.Q("results-grade");
            resultsGradeLetter = root.Q<Label>("results-grade-letter");
            resultsGradeScore = root.Q<Label>("results-grade-score");
            resultsBest = root.Q<Label>("results-best");
            resultsSplits = root.Q("results-splits");
            resultsCrews = root.Q("results-crews");
            resultsSplitsCaption = root.Q<Label>("results-splits-caption");
            resultsCrewsCaption = root.Q<Label>("results-crews-caption");
            retryButton = root.Q<Button>("retry-button");
            continueButton = root.Q<Button>("continue-button");
            race = root.Q("race");
            raceRows = root.Q("race-rows");
            racePosition = root.Q<Label>("race-position");
            raceCount = root.Q<Label>("race-count");
            raceGap = root.Q<Label>("race-gap");
            navigator = root.Q("navigator");
            navDistrict = root.Q<Label>("nav-district");
            navStreet = root.Q<Label>("nav-street");
            navRoute = root.Q<Label>("nav-route");
            terminal = new TerminalReadout(root.Q("terminal"));
            stamp = root.Q("objective-complete");
            stampText = root.Q<Label>("objective-complete-text");
            extractFrame = root.Q("extract-frame");
            var frameElement = root.Q("minimap-frame");
            frameElement.Clear();
            minimap = new CityMinimap();
            minimap.AddToClassList("nr-minimap__canvas");
            minimap.StretchToParentSize();
            frameElement.Add(minimap);
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
            results.RemoveFromClassList("nr-results--in");
            root.Q("hud-root")?.RemoveFromClassList("nr-hud--debrief");
            HideBriefing(true);
            banner.RemoveFromClassList("nr-banner--visible");
            SetLetterbox(false);
            frame.style.opacity = 0f;
            lastSecurity = null;
            lastHeatPercent = -1;
            lastRouteKey = int.MinValue;
            raceRowKeys.Clear();
            toasts.Clear();
            activeToasts.Clear();
            SetSecurity(SecurityLevel.Calm, 0f);
            SetTimer(false, 0f);
            SetCountdown(null, 0f);
            SetRace(false, 0, 0, null, null);
            SetTerminal(null);
            SetExtractionFrame(0);
            stamp?.RemoveFromClassList("nr-stamp--on");
            stampHideAt = 0f;
            objectivePending = false;
        }

        // ---------------- Interactions ----------------

        /// <summary>Multi-stage interaction readout for <paramref name="interactable"/> (null hides it). Call every frame while shown.</summary>
        public void SetTerminal(Interactable interactable)
        {
            terminal?.Show(interactable);
            root?.Q("hud-root")?.EnableInClassList("nr-hud--terminal", interactable != null);
        }

        public void FlashTerminal(Interactable.Feedback feedback) => terminal?.Flash(feedback);

        /// <summary>Screen-edge treatment while data is pulled: 0 off, 1 extracting, 2 warning (interference).</summary>
        public void SetExtractionFrame(int mode)
        {
            if (extractFrame == null) return;
            extractFrame.EnableInClassList("nr-hud-extract--on", mode == 1);
            extractFrame.EnableInClassList("nr-hud-extract--warn", mode == 2);
        }

        /// <summary>"OBJECTIVE COMPLETE"-style stamp over the objective panel for a moment.</summary>
        public void StampObjective(string text, float seconds)
        {
            if (stamp == null) return;
            stampText.text = text;
            stamp.AddToClassList("nr-stamp--on");
            // The finished objective steps aside for the stamp; the next SetObjective brings the panel back.
            Show(objective, false);
            stampHideAt = Time.unscaledTime + seconds;
        }

        // ---------------- Navigator ----------------

        public void SetRoadNetwork(NeonRift.World.RoadNetwork network, Func<int, bool> isClosed)
        {
            if (minimap != null) minimap.SetNetwork(network, isClosed);
            Show(navigator, network != null);
        }

        /// <summary>Location readout and minimap state; the minimap repaints at ~15 Hz.</summary>
        public void SetNavigator(Vector3 position, float heading, string district, string street, IReadOnlyList<Vector3> route, float routeMetres,
                                 bool hasTarget, Vector3 target, IEnumerable<(Vector3 position, float heading)> rivalPositions, bool lockdown)
        {
            if (minimap == null) return;
            if (district != lastDistrict) { lastDistrict = district; navDistrict.text = district ?? string.Empty; }
            // Strings are only built when the underlying value changes (no allocation per frame).
            if (!ReferenceEquals(street, lastStreetRaw) || lastStreet == null)
            {
                lastStreetRaw = street;
                string s = string.IsNullOrEmpty(street) ? "—" : street.ToUpperInvariant();
                if (s != lastStreet) { lastStreet = s; navStreet.text = s; }
            }
            int routeKey = route != null && route.Count > 1 ? Mathf.RoundToInt(routeMetres / 10f) * 10 : hasTarget ? -1 : -2;
            if (routeKey != lastRouteKey)
            {
                lastRouteKey = routeKey;
                lastRoute = routeKey >= 0 ? $"ROUTE  {routeKey} M" : routeKey == -1 ? "NO OPEN ROUTE" : string.Empty;
                navRoute.text = lastRoute;
            }
            navigator.EnableInClassList("nr-nav--lockdown", lockdown);
            if (Time.unscaledTime < nextMinimapRepaint) return;
            nextMinimapRepaint = Time.unscaledTime + 1f / 15f;
            minimap.SetView(position, heading, lockdown);
            minimap.SetRoute(route);
            minimap.SetTarget(hasTarget, target);
            minimap.SetRivals(rivalPositions);
            minimap.MarkDirtyRepaint();
        }

        // ---------------- Race ----------------

        /// <summary>Race standings: position, gap line and one row per racer (name, finished flag, is-player flag).</summary>
        public void SetRace(bool visible, int position, int count, string gap, IReadOnlyList<(string name, bool finished, bool player)> rows)
        {
            if (race == null) return;
            Show(race, visible);
            if (!visible) { lastRacePosition = -1; return; }
            if (position != lastRacePosition)
            {
                lastRacePosition = position;
                racePosition.text = $"P{position}";
                raceCount.text = $"/{count}";
            }
            if (gap != lastGap) { lastGap = gap; raceGap.text = gap ?? string.Empty; }
            if (rows == null) return;
            while (raceRows.childCount < rows.Count)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList("nr-race__row");
                raceRows.Add(l);
            }
            for (int i = 0; i < raceRows.childCount; i++)
            {
                var l = (Label)raceRows[i];
                bool used = i < rows.Count;
                Show(l, used);
                if (!used) continue;
                // Rebuild the row text only when its racer or finished flag changes.
                int key = (rows[i].name?.GetHashCode() ?? 0) * 31 + (rows[i].finished ? 1 : 0);
                if (i >= raceRowKeys.Count) raceRowKeys.Add(int.MinValue);
                if (raceRowKeys[i] != key)
                {
                    raceRowKeys[i] = key;
                    l.text = $"{i + 1}  {rows[i].name}";
                }
                l.EnableInClassList("nr-race__row--player", rows[i].player);
                l.EnableInClassList("nr-race__row--finished", rows[i].finished);
            }
        }

        public void SetVehicleName(string text) => vehicleName.text = text?.ToUpperInvariant() ?? string.Empty;

        public void SetObjective(int step, int total, string title, string detail, bool lockdown)
        {
            // While the "objective complete" stamp is up, the next objective waits underneath it.
            objectivePending = stampHideAt > 0f && !string.IsNullOrEmpty(title);
            Show(objective, !objectivePending && !string.IsNullOrEmpty(title));
            objectiveStep.text = $"OBJECTIVE {step}/{total}";
            objectiveTitle.text = title;
            objectiveDetail.text = detail;
            objective.EnableInClassList("nr-objective--lockdown", lockdown);
            // Slide the panel back in so the change is noticed.
            objective.AddToClassList("nr-objective--refresh");
            objective.schedule.Execute(() => objective.RemoveFromClassList("nr-objective--refresh")).StartingIn(60);
            lastDistance = -1;
        }

        public void HideObjective()
        {
            objectivePending = false;
            Show(objective, false);
        }

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
            int percent = Mathf.RoundToInt(Mathf.Clamp01(heat) * 100f);
            if (level == lastSecurity && percent == lastHeatPercent) return;
            lastSecurity = level;
            lastHeatPercent = percent;
            securityState.text = level switch { SecurityLevel.Lockdown => "LOCKDOWN", SecurityLevel.Alert => "ALERT", _ => "CALM" };
            security.EnableInClassList("nr-security--alert", level == SecurityLevel.Alert);
            security.EnableInClassList("nr-security--lockdown", level == SecurityLevel.Lockdown);
            heatFill.style.width = Length.Percent(percent);
            heatLabel.text = $"HEAT {percent}%";
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
            // Segmented: the fill snaps to whole segments so it reads at a glance instead of shimmering.
            float segments = Mathf.Ceil(Mathf.Clamp01(rpm01) * RpmSegments - 0.15f) / RpmSegments;
            rpmFill.style.width = Length.Percent(Mathf.Clamp01(segments) * 100f);
            rpmFill.EnableInClassList("nr-speedo__rpm-fill--redline", rpm01 > 0.92f);
            if (shiftLight != null) shiftLight.EnableInClassList("nr-speedo__shift--on", gearIndex > 0 && rpm01 > 0.9f);
        }

        /// <summary>Cinematic bars in or out (they animate through USS transitions).</summary>
        public void SetLetterbox(bool on)
        {
            root?.Q("hud-root")?.EnableInClassList("nr-hud--cinematic", on);
            letterboxTop?.EnableInClassList("nr-letterbox--on", on);
            letterboxBottom?.EnableInClassList("nr-letterbox--on", on);
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

        // ---------------- Briefing ----------------

        /// <summary>Slides the operation card in, revealing its rows one by one.</summary>
        public void ShowBriefing(Briefing b)
        {
            if (briefing == null || b == null) return;
            briefKicker.text = b.Kicker ?? string.Empty;
            briefTitle.text = b.Title ?? string.Empty;
            briefTagline.text = b.Tagline ?? string.Empty;
            briefText.text = b.Text ?? string.Empty;
            Show(briefText, !string.IsNullOrEmpty(b.Text));
            briefBest.text = b.Best ?? string.Empty;
            Show(briefBest, !string.IsNullOrEmpty(b.Best));
            briefObjectives.Clear();
            briefCrew.Clear();
            var rows = new List<VisualElement>();
            for (int i = 0; i < b.Objectives.Count; i++) rows.Add(BriefRow(briefObjectives, (i + 1).ToString("00"), b.Objectives[i], null, false));
            foreach (var (name, role, player) in b.Crew) rows.Add(BriefRow(briefCrew, player ? "▶" : "·", name, role, player));
            briefing.RemoveFromClassList("nr-brief--out");
            root.Q("hud-root")?.AddToClassList("nr-hud--briefing");
            briefing.schedule.Execute(() => briefing.AddToClassList("nr-brief--in")).StartingIn(30);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.schedule.Execute(() => row.AddToClassList("nr-brief__row--in")).StartingIn(350 + i * 110);
            }
        }

        public void HideBriefing(bool immediate = false)
        {
            if (briefing == null) return;
            root.Q("hud-root")?.RemoveFromClassList("nr-hud--briefing");
            if (!briefing.ClassListContains("nr-brief--in")) return;
            briefing.RemoveFromClassList("nr-brief--in");
            if (!immediate) briefing.AddToClassList("nr-brief--out");
        }

        private static VisualElement BriefRow(VisualElement list, string index, string name, string role, bool player)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("nr-brief__row");
            if (player) row.AddToClassList("nr-brief__row--player");
            row.Add(Text(index, "nr-brief__index"));
            row.Add(Text(name, "nr-brief__name"));
            if (!string.IsNullOrEmpty(role)) row.Add(Text(role, "nr-brief__role"));
            list.Add(row);
            return row;
        }

        private static Label Text(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            return l;
        }

        // ---------------- Debrief ----------------

        /// <summary>The end-of-run debrief: rows count in, then the grade lands.</summary>
        public void ShowResults(Debrief d)
        {
            SetPrompt(false, null, 0f, null, false);
            Show(waypoint, false);
            HideBriefing(true);
            SetTerminal(null);
            root.Q("hud-root")?.AddToClassList("nr-hud--debrief");
            results.RemoveFromClassList("nr-hidden");
            results.EnableInClassList("nr-results--failed", !d.Success);
            resultsKicker.text = d.Kicker ?? string.Empty;
            resultsTitle.text = d.Title;
            resultsReason.text = d.Reason ?? string.Empty;

            var rows = new List<VisualElement>();
            resultsStats.Clear();
            foreach (var (name, value) in d.Stats) rows.Add(StatRow(resultsStats, name, value, null));
            resultsSplits.Clear();
            foreach (var (name, value, dim) in d.Splits) rows.Add(StatRow(resultsSplits, name, value, dim ? "nr-results__stat--dim" : null));
            Show(resultsSplitsCaption, d.Splits.Count > 0);
            resultsCrews.Clear();
            foreach (var (name, value, player) in d.Crews) rows.Add(StatRow(resultsCrews, name, value, player ? "nr-results__stat--player" : null));
            Show(resultsCrewsCaption, d.Crews.Count > 0);
            resultsBest.text = d.Best ?? string.Empty;
            resultsBest.EnableInClassList("nr-results__best--new", d.NewBest);
            Show(resultsBest, !string.IsNullOrEmpty(d.Best));

            resultsGrade.ClearClassList();
            resultsGrade.AddToClassList("nr-grade");
            bool graded = !string.IsNullOrEmpty(d.Grade);
            if (graded) resultsGrade.AddToClassList("nr-grade--" + d.Grade.ToLowerInvariant());
            else resultsGrade.AddToClassList("nr-grade--none");
            resultsGradeLetter.text = d.Grade ?? string.Empty;
            resultsGradeScore.text = graded ? $"{d.Score} PTS" : string.Empty;

            results.schedule.Execute(() => results.AddToClassList("nr-results--in")).StartingIn(30);
            int delay = 450;
            foreach (var row in rows)
            {
                var r = row;
                r.schedule.Execute(() => r.AddToClassList("nr-results__stat--in")).StartingIn(delay);
                delay += 80;
            }
            if (graded) resultsGrade.schedule.Execute(() => resultsGrade.AddToClassList("nr-grade--in")).StartingIn(delay + 200);
            retryButton.Focus();
        }

        private static VisualElement StatRow(VisualElement list, string name, string value, string modifier)
        {
            var row = new VisualElement();
            row.AddToClassList("nr-results__stat");
            if (modifier != null) row.AddToClassList(modifier);
            row.Add(Text(name, "nr-results__stat-name"));
            row.Add(Text(value, "nr-results__stat-value"));
            list.Add(row);
            return row;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (stampHideAt > 0f && now >= stampHideAt)
            {
                stamp.RemoveFromClassList("nr-stamp--on");
                stampHideAt = 0f;
                if (objectivePending)
                {
                    // Let the stamp fade out before the next objective slides in.
                    objectivePending = false;
                    objective.AddToClassList("nr-objective--refresh");
                    objective.schedule.Execute(() =>
                    {
                        if (stampHideAt > 0f) return;
                        Show(objective, true);
                        objective.schedule.Execute(() => objective.RemoveFromClassList("nr-objective--refresh")).StartingIn(40);
                    }).StartingIn(280);
                }
            }
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
