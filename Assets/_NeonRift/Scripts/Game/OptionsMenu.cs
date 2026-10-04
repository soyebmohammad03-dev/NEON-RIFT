using System;
using System.Collections.Generic;
using NeonRift.Audio;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NeonRift.Game
{
    /// <summary>
    /// The game's menu overlay (UI Toolkit, built in code): a stack of pages of rows. Used as the pause menu in a
    /// mission and as the options menu on the title screen. Rows are buttons, sliders (volumes) or choices
    /// (on/off, quality). Keyboard / gamepad: Up/Down (W/S, arrows, d-pad, stick) move, Left/Right (A/D, Q/E,
    /// shoulders) change a value, Enter / A activates, Esc / B goes back. The mouse works on every row.
    /// Includes the shared Settings (audio, graphics) and Controls pages.
    /// </summary>
    public sealed class OptionsMenu
    {
        public sealed class Row
        {
            public string Label;
            public Action Activate;
            /// <summary>-1 / +1 from Left/Right; null for plain buttons.</summary>
            public Action<int> Adjust;
            public Func<string> Value;
            public Func<float> Fill;
            internal VisualElement Element;
            internal Label ValueLabel;
            internal VisualElement FillBar;
        }

        private sealed class Page
        {
            public string Title, Footer;
            public List<Row> Rows;
            public VisualElement Body;
            public int Selected;
        }

        private readonly GameContext context;
        private readonly VisualElement overlay, panel;
        private readonly Label title, footer;
        private readonly Stack<Page> pages = new();
        private readonly InputAction up, down, left, right, confirm, back;
        private bool armed;

        public bool IsOpen { get; private set; }
        /// <summary>Back pressed on the first page (the owner closes the menu or ignores it).</summary>
        public event Action BackFromRoot;

        public OptionsMenu(VisualElement root, GameContext gameContext)
        {
            context = gameContext;
            overlay = new VisualElement { name = "options-overlay", pickingMode = PickingMode.Position };
            overlay.AddToClassList("nr-menu");
            overlay.AddToClassList("nr-hidden");
            panel = new VisualElement();
            panel.AddToClassList("nr-menu__panel");
            title = new Label();
            title.AddToClassList("nr-menu__title");
            footer = new Label();
            footer.AddToClassList("nr-menu__footer");
            panel.Add(title);
            overlay.Add(panel);
            overlay.Add(footer);
            root.Add(overlay);
            var menu = context.Controls.Menu;
            up = menu.Up;
            down = menu.Down;
            left = menu.Previous;
            right = menu.Next;
            confirm = menu.Confirm;
            back = menu.Back;
        }

        // ---------------- Open / close ----------------

        public void Open(string pageTitle, List<Row> rows, string pageFooter = null)
        {
            ClearBodies();
            pages.Clear();
            IsOpen = true;
            overlay.RemoveFromClassList("nr-hidden");
            Push(pageTitle, rows, pageFooter);
            up.performed += OnUp;
            down.performed += OnDown;
            left.performed += OnLeft;
            right.performed += OnRight;
            confirm.performed += OnConfirm;
            back.performed += OnBack;
            if (context.Settings != null) context.Settings.Changed += RefreshPage;
            context.Controls.Menu.Enable();
            // The key that opened the menu (Esc / Start) must not also count as Back in the same frame.
            armed = false;
            overlay.schedule.Execute(() => armed = true).StartingIn(120);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            overlay.AddToClassList("nr-hidden");
            up.performed -= OnUp;
            down.performed -= OnDown;
            left.performed -= OnLeft;
            right.performed -= OnRight;
            confirm.performed -= OnConfirm;
            back.performed -= OnBack;
            if (context.Settings != null) context.Settings.Changed -= RefreshPage;
            ClearBodies();
            pages.Clear();
        }

        private void ClearBodies()
        {
            foreach (var p in pages) p.Body?.RemoveFromHierarchy();
        }

        public void Push(string pageTitle, List<Row> rows, string pageFooter = null)
        {
            if (pages.Count > 0) pages.Peek().Body.AddToClassList("nr-hidden");
            var page = new Page { Title = pageTitle, Rows = rows, Footer = pageFooter, Body = new VisualElement() };
            page.Body.AddToClassList("nr-menu__body");
            for (int i = 0; i < rows.Count; i++) page.Body.Add(Build(rows[i], page, i));
            panel.Add(page.Body);
            pages.Push(page);
            Show(page);
        }

        private void Pop()
        {
            if (pages.Count <= 1) { BackFromRoot?.Invoke(); return; }
            pages.Pop().Body.RemoveFromHierarchy();
            var page = pages.Peek();
            page.Body.RemoveFromClassList("nr-hidden");
            Show(page);
        }

        private void Show(Page page)
        {
            title.text = page.Title;
            footer.text = page.Footer ?? "↑↓ SELECT   ·   ←→ CHANGE   ·   ENTER / A  CONFIRM   ·   ESC / B  BACK";
            Select(page, page.Selected);
        }

        private VisualElement Build(Row row, Page page, int index)
        {
            var e = new VisualElement();
            e.AddToClassList("nr-menu__row");
            var label = new Label(row.Label);
            label.AddToClassList("nr-menu__label");
            e.Add(label);
            if (row.Value != null || row.Fill != null)
            {
                var value = new VisualElement();
                value.AddToClassList("nr-menu__value");
                if (row.Fill != null)
                {
                    var track = new VisualElement();
                    track.AddToClassList("nr-menu__track");
                    row.FillBar = new VisualElement();
                    row.FillBar.AddToClassList("nr-menu__fill");
                    track.Add(row.FillBar);
                    value.Add(track);
                }
                row.ValueLabel = new Label();
                row.ValueLabel.AddToClassList("nr-menu__value-text");
                value.Add(row.ValueLabel);
                e.Add(value);
            }
            row.Element = e;
            Refresh(row);
            e.RegisterCallback<PointerEnterEvent>(_ => Select(page, index));
            e.RegisterCallback<PointerDownEvent>(evt =>
            {
                Select(page, index);
                if (row.Adjust != null)
                {
                    // Click on the right half increases, left half decreases.
                    var local = e.WorldToLocal(evt.position);
                    row.Adjust(local.x > e.layout.width * 0.5f ? 1 : -1);
                    Refresh(row);
                }
                else row.Activate?.Invoke();
            });
            return e;
        }

        /// <summary>Settings can change from outside the menu (M toggles the music): redraw the visible values.</summary>
        private void RefreshPage()
        {
            if (Current == null) return;
            foreach (var row in Current.Rows) Refresh(row);
        }

        private static void Refresh(Row row)
        {
            if (row.ValueLabel != null && row.Value != null) row.ValueLabel.text = row.Value();
            if (row.FillBar != null && row.Fill != null) row.FillBar.style.width = Length.Percent(Mathf.Clamp01(row.Fill()) * 100f);
        }

        private void Select(Page page, int index)
        {
            if (page.Rows.Count == 0) return;
            page.Selected = (index % page.Rows.Count + page.Rows.Count) % page.Rows.Count;
            for (int i = 0; i < page.Rows.Count; i++) page.Rows[i].Element.EnableInClassList("nr-menu__row--selected", i == page.Selected);
        }

        // ---------------- Input ----------------

        private Page Current => pages.Count > 0 ? pages.Peek() : null;
        private void OnUp(InputAction.CallbackContext _) { if (Current != null) Select(Current, Current.Selected - 1); }
        private void OnDown(InputAction.CallbackContext _) { if (Current != null) Select(Current, Current.Selected + 1); }
        private void OnLeft(InputAction.CallbackContext _) => Adjust(-1);
        private void OnRight(InputAction.CallbackContext _) => Adjust(1);

        private void Adjust(int step)
        {
            var row = Current != null && Current.Rows.Count > 0 ? Current.Rows[Current.Selected] : null;
            if (row?.Adjust == null) return;
            row.Adjust(step);
            Refresh(row);
        }

        private void OnConfirm(InputAction.CallbackContext _)
        {
            if (!armed || Current == null || Current.Rows.Count == 0) return;
            var row = Current.Rows[Current.Selected];
            if (row.Activate != null) row.Activate();
            else if (row.Adjust != null) { row.Adjust(1); Refresh(row); }
        }

        private void OnBack(InputAction.CallbackContext _)
        {
            if (!armed) return;
            Pop();
        }

        // ---------------- Shared pages ----------------

        public Row Button(string label, Action activate) => new() { Label = label, Activate = activate };

        /// <summary>Audio and graphics settings (persisted through <see cref="GameSettings"/>).</summary>
        public void PushSettings()
        {
            var s = context.Settings;
            Row Volume(string label, AudioChannel channel) => new()
            {
                Label = label,
                Adjust = d => s.SetVolume(channel, Mathf.Round((s.GetVolume(channel) + d * 0.1f) * 10f) / 10f),
                Value = () => $"{Mathf.RoundToInt(s.GetVolume(channel) * 100f)}%",
                Fill = () => s.GetVolume(channel)
            };
            Row Toggle(string label, Func<bool> get, Action<bool> set) => new()
            {
                Label = label,
                Adjust = _ => set(!get()),
                Value = () => get() ? "ON" : "OFF"
            };
            var names = QualitySettings.names;
            var rows = new List<Row>
            {
                Volume("MASTER VOLUME", AudioChannel.Master),
                Volume("MUSIC VOLUME", AudioChannel.Music),
                Volume("EFFECTS VOLUME", AudioChannel.Effects),
                Volume("INTERFACE VOLUME", AudioChannel.UI),
                Toggle("MUSIC  (M)", () => s.MusicOn, s.SetMusicOn),
                new()
                {
                    Label = "GRAPHICS QUALITY",
                    Adjust = d => s.SetQuality((s.Quality + d + names.Length) % names.Length),
                    Value = () => names.Length > 0 ? names[s.Quality].ToUpperInvariant() : "-"
                },
                Toggle("FULLSCREEN", () => s.Fullscreen, s.SetFullscreen),
                Toggle("V-SYNC", () => s.VSync, s.SetVSync),
                Button("BACK", Pop)
            };
            Push("SETTINGS", rows);
        }

        /// <summary>Keyboard and gamepad bindings.</summary>
        public void PushControls()
        {
            (string action, string keys, string pad)[] table =
            {
                ("ACCELERATE", "W  /  ↑", "RIGHT TRIGGER"),
                ("BRAKE / REVERSE", "S  /  ↓", "LEFT TRIGGER"),
                ("STEER", "A D  /  ← →", "LEFT STICK"),
                ("HANDBRAKE", "SPACE", "B"),
                ("INTERACT / HACK", "E", "A"),
                ("RECOVER CAR", "R", "SELECT"),
                ("PAUSE", "ESC", "START"),
                ("MUSIC ON / OFF", "M", "Y"),
            };
            var rows = new List<Row>();
            foreach (var (action, keys, pad) in table)
                rows.Add(new Row { Label = action, Value = () => $"{keys}      ·      {pad}" });
            rows.Add(Button("BACK", Pop));
            Push("CONTROLS", rows, "KEYBOARD  ·  GAMEPAD        ESC / B  BACK");
        }
    }
}
