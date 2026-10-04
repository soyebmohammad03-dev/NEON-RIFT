using System.Collections;
using System.Collections.Generic;
using NeonRift.Game;
using NeonRift.Input;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NeonRift.Frontend
{
    /// <summary>
    /// Car Select in the crew garage: browse the vehicle catalog (cards, performance from the physics bench, copy),
    /// preview the car on the <see cref="VehicleShowroom"/> turntable, and confirm into the selected mission. Confirm
    /// plays the garage departure (door, rev, pull-out) before the flow loads the mission, so the screen hands over to
    /// the Night Run instead of cutting to it.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CarSelectScreen : MonoBehaviour, ISceneEntryPoint
    {
        [SerializeField] private VehicleShowroom showroom;

        [Header("Stat bar ranges (bar is full at the better end)")]
        [SerializeField] private Vector2 topSpeedRangeKph = new(200f, 360f);
        [Tooltip("0–100 km/h: bar full at X (quickest), empty at Y.")]
        [SerializeField] private Vector2 zeroToHundredRangeSeconds = new(2.2f, 6f);
        [Tooltip("100–0 km/h braking distance: bar full at X (shortest), empty at Y.")]
        [SerializeField] private Vector2 brakingRangeMetres = new(28f, 44f);

        private GameContext context;
        private VehicleCatalog catalog;
        private int index;
        private bool starting;

        private VisualElement root, info, specs, cards, fade;
        private Label nameLabel, makerLabel, categoryLabel, indexLabel, descLabel, emptyLabel, driveChip, powerChip, weightChip;
        private Button prevButton, nextButton, confirmButton, backButton;
        private VisualElement dragArea;
        private StatRow acceleration, topSpeed, handling, braking;
        private readonly List<VisualElement> cardElements = new();
        private bool dragging;
        private NeonRiftControls.MenuActions menu;
        private Coroutine textSwap;

        public void Enter(GameContext gameContext)
        {
            context = gameContext;
            catalog = context.Config.VehicleCatalog;
            starting = false;
            BindUi();

            menu = context.Controls.Menu;
            menu.Previous.performed += OnPrevious;
            menu.Next.performed += OnNext;
            menu.Confirm.performed += OnConfirmAction;
            menu.Back.performed += OnBackAction;
            menu.Enable();

            var selected = context.Session.SelectedVehicle;
            index = catalog != null && selected != null ? Mathf.Max(0, catalog.IndexOf(selected)) : 0;
            BuildCards();
            Refresh(animate: false);
            StartCoroutine(Opening());
        }

        public void Exit()
        {
            if (context == null) return;
            menu.Previous.performed -= OnPrevious;
            menu.Next.performed -= OnNext;
            menu.Confirm.performed -= OnConfirmAction;
            menu.Back.performed -= OnBackAction;
            menu.Disable();

            prevButton.clicked -= Previous;
            nextButton.clicked -= Next;
            confirmButton.clicked -= Confirm;
            backButton.clicked -= Back;
            context = null;
        }

        /// <summary>Fade up from black on the garage; after the intro, open on the headlight and pull back.</summary>
        private IEnumerator Opening()
        {
            root.AddToClassList("nr-cs--intro");
            yield return null;
            root.RemoveFromClassList("nr-cs--intro");
            if (context != null && context.Session.ArrivedFromIntro) yield return showroom.OpeningPullBack(context.Config.FadeSeconds + 0.6f);
            context?.Session.ClearIntroArrival();
        }

        private void Update()
        {
            foreach (var row in new[] { acceleration, topSpeed, handling, braking }) row?.Tick();
            if (context == null || dragging || starting) return;
            showroom.Spin(menu.Rotate.ReadValue<float>());
        }

        private void BindUi()
        {
            root = GetComponent<UIDocument>().rootVisualElement.Q("cs-root");
            info = root.Q("info");
            specs = root.Q("specs");
            cards = root.Q("cards");
            fade = root.Q("cs-fade");
            nameLabel = root.Q<Label>("vehicle-name");
            makerLabel = root.Q<Label>("vehicle-maker");
            categoryLabel = root.Q<Label>("vehicle-category");
            indexLabel = root.Q<Label>("vehicle-index");
            descLabel = root.Q<Label>("vehicle-desc");
            emptyLabel = root.Q<Label>("empty-label");
            driveChip = root.Q<Label>("chip-drive");
            powerChip = root.Q<Label>("chip-power");
            weightChip = root.Q<Label>("chip-weight");
            prevButton = root.Q<Button>("prev-button");
            nextButton = root.Q<Button>("next-button");
            confirmButton = root.Q<Button>("confirm-button");
            backButton = root.Q<Button>("back-button");
            dragArea = root.Q("drag-area");
            root.RemoveFromClassList("nr-cs--departing");
            // Soft film-style shades top and bottom (USS has no gradients): a vertical alpha ramp texture.
            foreach (var (selector, fromTop) in new[] { (".nr-cs__shade--top", true), (".nr-cs__shade--bottom", false) })
            {
                var shade = root.Q(className: selector.Substring(1));
                if (shade == null) continue;
                shade.style.backgroundColor = new StyleColor(Color.clear);
                shade.style.backgroundImage = new StyleBackground(Ramp(fromTop));
            }
            fade.RemoveFromClassList("nr-cs__fade--in");

            var stats = root.Q("stats");
            stats.Clear();
            acceleration = new StatRow(stats, "ACCELERATION  0–100");
            topSpeed = new StatRow(stats, "TOP SPEED");
            handling = new StatRow(stats, "HANDLING");
            braking = new StatRow(stats, "BRAKING  100–0");

            prevButton.clicked += Previous;
            nextButton.clicked += Next;
            confirmButton.clicked += Confirm;
            backButton.clicked += Back;

            dragArea.RegisterCallback<PointerDownEvent>(e => { dragging = true; dragArea.CapturePointer(e.pointerId); });
            dragArea.RegisterCallback<PointerMoveEvent>(e => { if (dragging) showroom.Drag(e.deltaPosition.x); });
            dragArea.RegisterCallback<PointerUpEvent>(e => { dragging = false; dragArea.ReleasePointer(e.pointerId); });
            dragArea.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
        }

        private static readonly Dictionary<bool, Texture2D> ramps = new();

        private static Texture2D Ramp(bool darkAtTop)
        {
            if (ramps.TryGetValue(darkAtTop, out var t) && t != null) return t;
            t = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 128; y++)
            {
                float v = y / 127f;                        // 0 at the bottom of the texture
                float a = darkAtTop ? v : 1f - v;          // dark edge of the screen
                t.SetPixel(0, y, new Color(0.01f, 0.012f, 0.03f, Mathf.Pow(a, 1.8f) * 0.88f));
            }
            t.Apply();
            ramps[darkAtTop] = t;
            return t;
        }

        private void BuildCards()
        {
            cards.Clear();
            cardElements.Clear();
            if (catalog == null) return;
            for (int i = 0; i < catalog.Count; i++)
            {
                var v = catalog.Vehicles[i];
                var card = new VisualElement();
                card.AddToClassList("nr-cs__card");
                var idx = new Label($"{i + 1:00}");
                idx.AddToClassList("nr-cs__card-index");
                var n = new Label(v.DisplayName.ToUpperInvariant());
                n.AddToClassList("nr-cs__card-name");
                var m = new Label((v.Manufacturer ?? string.Empty).ToUpperInvariant());
                m.AddToClassList("nr-cs__card-maker");
                card.Add(idx);
                card.Add(n);
                card.Add(m);
                int captured = i;
                card.RegisterCallback<ClickEvent>(_ => Select(captured));
                cards.Add(card);
                cardElements.Add(card);
            }
        }

        private void Select(int i)
        {
            if (starting || i == index) return;
            index = i;
            Refresh(animate: true);
        }

        private void Refresh(bool animate)
        {
            int count = catalog != null ? catalog.Count : 0;
            bool hasVehicles = count > 0;
            emptyLabel.EnableInClassList("nr-hidden", hasVehicles);
            bool hasMission = context.Session.SelectedMission != null || context.Config.DefaultMission != null;
            confirmButton.SetEnabled(hasVehicles && hasMission);
            prevButton.SetEnabled(count > 1);
            nextButton.SetEnabled(count > 1);

            if (!hasVehicles)
            {
                nameLabel.text = "—";
                makerLabel.text = descLabel.text = categoryLabel.text = indexLabel.text = string.Empty;
                SetStats(null);
                showroom.Show(null, false);
                backButton.Focus();
                return;
            }

            index = (index % count + count) % count;
            var vehicle = catalog.Vehicles[index];
            for (int i = 0; i < cardElements.Count; i++) cardElements[i].EnableInClassList("nr-cs__card--selected", i == index);
            var others = new List<VehicleDefinition>();
            for (int i = 1; i < count; i++) others.Add(catalog.Vehicles[(index + i) % count]);
            showroom.ShowParked(others);
            showroom.Show(vehicle, animate);
            if (textSwap != null) StopCoroutine(textSwap);
            textSwap = StartCoroutine(SwapText(vehicle, count, animate));
            confirmButton.Focus();
        }

        /// <summary>Text slides out, the content changes while it is hidden, then it slides back in with the stat bars.</summary>
        private IEnumerator SwapText(VehicleDefinition vehicle, int count, bool animate)
        {
            if (animate)
            {
                info.AddToClassList("nr-cs__info--out");
                specs.AddToClassList("nr-cs__specs--out");
                for (float t = 0f; t < 0.17f; t += Time.unscaledDeltaTime) yield return null;
            }
            nameLabel.text = vehicle.DisplayName.ToUpperInvariant();
            makerLabel.text = (vehicle.Manufacturer ?? string.Empty).ToUpperInvariant();
            categoryLabel.text = string.IsNullOrEmpty(vehicle.Category) ? string.Empty : vehicle.Category.ToUpperInvariant();
            indexLabel.text = $"{index + 1:00} / {count:00}";
            descLabel.text = vehicle.Description;
            SetStats(vehicle);
            info.RemoveFromClassList("nr-cs__info--out");
            specs.RemoveFromClassList("nr-cs__specs--out");
            textSwap = null;
        }

        private void SetStats(VehicleDefinition vehicle)
        {
            if (vehicle == null)
            {
                foreach (var row in new[] { acceleration, topSpeed, handling, braking }) row.Set(0f, "—");
                driveChip.text = powerChip.text = weightChip.text = "—";
                return;
            }
            var s = vehicle.DisplayStats;
            acceleration.Set(Mathf.InverseLerp(zeroToHundredRangeSeconds.y, zeroToHundredRangeSeconds.x, s.zeroToHundredSeconds), s.zeroToHundredSeconds, "{0:0.0} S");
            topSpeed.Set(Mathf.InverseLerp(topSpeedRangeKph.x, topSpeedRangeKph.y, s.topSpeedKph), s.topSpeedKph, "{0:0} KM/H");
            handling.Set(s.handlingRating / 10f, s.handlingRating, "{0:0.0} / 10");
            if (s.brakingDistanceMetres > 0f) braking.Set(Mathf.InverseLerp(brakingRangeMetres.y, brakingRangeMetres.x, s.brakingDistanceMetres), s.brakingDistanceMetres, "{0:0.0} M");
            else braking.Set(0f, "—");
            driveChip.text = vehicle.DrivetrainLabel;
            powerChip.text = $"{s.powerHp:0} HP";
            weightChip.text = $"{s.massKg:0} KG";
        }

        private void Previous() { if (!starting) { index--; Refresh(animate: true); } }
        private void Next() { if (!starting) { index++; Refresh(animate: true); } }

        private void Confirm()
        {
            if (starting || catalog == null || catalog.Count == 0 || context.Flow.IsTransitioning) return;
            var session = context.Session;
            session.SelectVehicle(catalog.Vehicles[index], catalog);
            if (session.SelectedMission == null)
            {
                var mission = context.Config.DefaultMission;
                if (mission == null)
                {
                    Debug.LogError("[CarSelect] No playable default mission configured in GameConfig.");
                    return;
                }
                session.SelectMission(mission);
            }
            StartCoroutine(Departure());
        }

        /// <summary>UI steps back, the car leaves the garage, fade, then the mission loads.</summary>
        private IEnumerator Departure()
        {
            starting = true;
            confirmButton.AddToClassList("nr-cs__start--pressed");
            for (float t = 0f; t < 0.12f; t += Time.unscaledDeltaTime) yield return null;
            root.AddToClassList("nr-cs--departing");
            var depart = StartCoroutine(showroom.Depart());
            for (float t = 0f; t < 3.6f; t += Time.unscaledDeltaTime) yield return null;
            fade.AddToClassList("nr-cs__fade--in");
            yield return depart;
            for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime) yield return null;
            Debug.Log($"[CarSelect] start: {catalog.Vehicles[index].Id}");
            context?.Flow.StartMission();
        }

        private void Back()
        {
            if (!starting && !context.Flow.IsTransitioning) context.Flow.GoToFrontend();
        }

        private void OnPrevious(InputAction.CallbackContext _) => Previous();
        private void OnNext(InputAction.CallbackContext _) => Next();
        private void OnConfirmAction(InputAction.CallbackContext _) => Confirm();
        private void OnBackAction(InputAction.CallbackContext _) => Back();

        /// <summary>Dev / validation: the vehicle currently shown.</summary>
        public VehicleDefinition Current => catalog != null && catalog.Count > 0 ? catalog.Vehicles[(index % catalog.Count + catalog.Count) % catalog.Count] : null;
        public bool IsStarting => starting;

        private sealed class StatRow
        {
            private readonly VisualElement fill;
            private readonly Label value;

            public StatRow(VisualElement parent, string title)
            {
                var row = new VisualElement();
                row.AddToClassList("nr-stat");
                var header = new VisualElement();
                header.AddToClassList("nr-stat__header");
                header.Add(new Label(title));
                value = new Label();
                value.AddToClassList("nr-stat__value");
                header.Add(value);
                var track = new VisualElement();
                track.AddToClassList("nr-stat__track");
                fill = new VisualElement();
                fill.AddToClassList("nr-stat__fill");
                track.Add(fill);
                // Ten segments, like a gauge.
                var ticks = new VisualElement { pickingMode = PickingMode.Ignore };
                ticks.AddToClassList("nr-stat__ticks");
                for (int i = 0; i < 11; i++)
                {
                    var tick = new VisualElement();
                    tick.AddToClassList("nr-stat__tick");
                    ticks.Add(tick);
                }
                track.Add(ticks);
                row.Add(header);
                row.Add(track);
                parent.Add(row);
            }

            private float shown, target;
            private string format;
            private float animStart = -1f;

            public void Set(float normalized, string text)
            {
                fill.style.width = Length.Percent(Mathf.Lerp(4f, 100f, Mathf.Clamp01(normalized)));
                value.text = text;
                format = null;
                animStart = -1f;
            }

            /// <summary>Bar eases to <paramref name="normalized"/> (USS transition); the number counts to <paramref name="number"/>.</summary>
            public void Set(float normalized, float number, string numberFormat)
            {
                fill.style.width = Length.Percent(Mathf.Lerp(4f, 100f, Mathf.Clamp01(normalized)));
                if (format == null) shown = number;
                format = numberFormat;
                target = number;
                animStart = Time.unscaledTime;
                from = shown;
                Tick();
            }

            private float from;

            /// <summary>Advances the count-up (call every frame).</summary>
            public void Tick()
            {
                if (format == null || animStart < 0f) return;
                float k = Mathf.Clamp01((Time.unscaledTime - animStart) / 0.5f);
                k = 1f - (1f - k) * (1f - k) * (1f - k);
                shown = Mathf.Lerp(from, target, k);
                value.text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, shown);
                if (k >= 1f) animStart = -1f;
            }
        }
    }
}
