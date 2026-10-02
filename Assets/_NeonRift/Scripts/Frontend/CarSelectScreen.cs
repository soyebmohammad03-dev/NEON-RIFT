using NeonRift.Game;
using NeonRift.Input;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NeonRift.Frontend
{
    /// <summary>Browse the vehicle catalog, preview in 3D, confirm into the selected mission.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CarSelectScreen : MonoBehaviour, ISceneEntryPoint
    {
        [SerializeField] private VehicleShowroom showroom;

        [Header("Stat bar ranges (bar is full at Max)")]
        [SerializeField] private Vector2 topSpeedRangeKph = new(150f, 420f);
        [Tooltip("Lower is better: bar is full at X (fastest), empty at Y.")]
        [SerializeField] private Vector2 zeroToHundredRangeSeconds = new(2f, 8f);
        [SerializeField] private Vector2 powerRangeHp = new(100f, 1200f);

        private GameContext context;
        private VehicleCatalog catalog;
        private int index;

        private Label nameLabel, makerLabel, descLabel, counterLabel, emptyLabel;
        private Button prevButton, nextButton, confirmButton, backButton;
        private VisualElement dragArea;
        private StatRow topSpeed, acceleration, power, handling;
        private bool dragging;
        private NeonRiftControls.MenuActions menu;

        public void Enter(GameContext gameContext)
        {
            context = gameContext;
            catalog = context.Config.VehicleCatalog;
            BindUi();

            menu = context.Controls.Menu;
            menu.Previous.performed += OnPrevious;
            menu.Next.performed += OnNext;
            menu.Confirm.performed += OnConfirmAction;
            menu.Back.performed += OnBackAction;
            menu.Enable();

            var selected = context.Session.SelectedVehicle;
            index = catalog != null && selected != null ? Mathf.Max(0, catalog.IndexOf(selected)) : 0;
            Refresh();
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

        private void Update()
        {
            if (context == null || dragging) return;
            showroom.Spin(menu.Rotate.ReadValue<float>());
        }

        private void BindUi()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            nameLabel = root.Q<Label>("vehicle-name");
            makerLabel = root.Q<Label>("vehicle-maker");
            descLabel = root.Q<Label>("vehicle-desc");
            counterLabel = root.Q<Label>("counter-label");
            emptyLabel = root.Q<Label>("empty-label");
            prevButton = root.Q<Button>("prev-button");
            nextButton = root.Q<Button>("next-button");
            confirmButton = root.Q<Button>("confirm-button");
            backButton = root.Q<Button>("back-button");
            dragArea = root.Q("drag-area");

            var stats = root.Q("stats");
            stats.Clear();
            topSpeed = new StatRow(stats, "TOP SPEED");
            acceleration = new StatRow(stats, "0–100 KM/H");
            power = new StatRow(stats, "POWER");
            handling = new StatRow(stats, "HANDLING");

            prevButton.clicked += Previous;
            nextButton.clicked += Next;
            confirmButton.clicked += Confirm;
            backButton.clicked += Back;

            dragArea.RegisterCallback<PointerDownEvent>(e => { dragging = true; dragArea.CapturePointer(e.pointerId); });
            dragArea.RegisterCallback<PointerMoveEvent>(e => { if (dragging) showroom.Drag(e.deltaPosition.x); });
            dragArea.RegisterCallback<PointerUpEvent>(e => { dragging = false; dragArea.ReleasePointer(e.pointerId); });
            dragArea.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
        }

        private void Refresh()
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
                makerLabel.text = descLabel.text = counterLabel.text = string.Empty;
                SetStats(null);
                showroom.Show(null);
                backButton.Focus();
                return;
            }

            index = (index % count + count) % count;
            var vehicle = catalog.Vehicles[index];
            nameLabel.text = vehicle.DisplayName.ToUpperInvariant();
            makerLabel.text = vehicle.Manufacturer?.ToUpperInvariant() ?? string.Empty;
            descLabel.text = vehicle.Description;
            counterLabel.text = $"{index + 1} / {count}";
            SetStats(vehicle);
            showroom.Show(vehicle);
            confirmButton.Focus();
        }

        private void SetStats(VehicleDefinition vehicle)
        {
            if (vehicle == null)
            {
                topSpeed.Set(0f, "—");
                acceleration.Set(0f, "—");
                power.Set(0f, "—");
                handling.Set(0f, "—");
                return;
            }
            var s = vehicle.DisplayStats;
            topSpeed.Set(Mathf.InverseLerp(topSpeedRangeKph.x, topSpeedRangeKph.y, s.topSpeedKph), $"{s.topSpeedKph:0} KM/H");
            acceleration.Set(Mathf.InverseLerp(zeroToHundredRangeSeconds.y, zeroToHundredRangeSeconds.x, s.zeroToHundredSeconds), $"{s.zeroToHundredSeconds:0.0} S");
            power.Set(Mathf.InverseLerp(powerRangeHp.x, powerRangeHp.y, s.powerHp), $"{s.powerHp:0} HP");
            handling.Set(s.handlingRating / 10f, $"{s.handlingRating:0.0}");
        }

        private void Previous() { index--; Refresh(); }
        private void Next() { index++; Refresh(); }

        private void Confirm()
        {
            if (catalog == null || catalog.Count == 0 || context.Flow.IsTransitioning) return;
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
            context.Flow.StartMission();
        }

        private void Back()
        {
            if (!context.Flow.IsTransitioning) context.Flow.GoToFrontend();
        }

        private void OnPrevious(InputAction.CallbackContext _) => Previous();
        private void OnNext(InputAction.CallbackContext _) => Next();
        private void OnConfirmAction(InputAction.CallbackContext _) => Confirm();
        private void OnBackAction(InputAction.CallbackContext _) => Back();

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
                row.Add(header);
                row.Add(track);
                parent.Add(row);
            }

            public void Set(float normalized, string text)
            {
                fill.style.width = Length.Percent(Mathf.Clamp01(normalized) * 100f);
                value.text = text;
            }
        }
    }
}
