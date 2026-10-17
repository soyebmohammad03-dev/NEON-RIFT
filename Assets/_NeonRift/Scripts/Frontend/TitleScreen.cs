using NeonRift.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonRift.Frontend
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class TitleScreen : MonoBehaviour, ISceneEntryPoint
    {
        private GameContext context;
        private Button startButton;
        private Button quitButton;
        private Button introButton;
        private Button optionsButton;
        private OptionsMenu options;

        public void Enter(GameContext gameContext)
        {
            context = gameContext;
            var root = GetComponent<UIDocument>().rootVisualElement;

            startButton = root.Q<Button>("start-button");
            quitButton = root.Q<Button>("quit-button");
            introButton = root.Q<Button>("intro-button");
            root.Q<Label>("version-label").text = $"v{Application.version}";

            startButton.clicked += OnStart;
            quitButton.clicked += OnQuit;
            if (introButton != null) introButton.clicked += OnIntro;
            optionsButton = root.Q<Button>("options-button");
            if (optionsButton != null) optionsButton.clicked += OnOptions;
            options = new OptionsMenu(root, context);
            options.BackFromRoot += () => { options.Close(); startButton.Focus(); };
            if (context.Campaign != null) SetUpCampaign(root);
            startButton.Focus();
        }

        // ---------------- Campaign (campaign bootstrap only) ----------------

        private Button harborButton;
        private UnityEngine.InputSystem.InputAction devHarbor, devArrival;

        /// <summary>
        /// START runs the campaign from Night Run; once Night Run has been qualified, HARBOR RAIN goes straight to the
        /// Level 2 car selection (no need to replay Level 1). The status line shows the saved progress.
        /// </summary>
        private void SetUpCampaign(VisualElement root)
        {
            var campaign = context.Campaign;
            var p = campaign.Progress;
            var sub = root.Q<Label>(className: "nr-subtitle");
            if (sub != null) sub.text = "NIGHT RUN  ·  HARBOR RAIN";
            startButton.text = p.NightRunCompleted ? "PLAY NIGHT RUN" : "START CAMPAIGN";
            if (p.HarborAvailable)
            {
                harborButton = new Button(OnHarbor) { name = "harbor-button", text = p.HarborCompleted ? "PLAY HARBOR RAIN" : "CONTINUE  ·  HARBOR RAIN" };
                harborButton.AddToClassList("nr-button");
                harborButton.AddToClassList("nr-button--primary");
                startButton.parent.Insert(startButton.parent.IndexOf(startButton) + 1, harborButton);
            }
            var status = new Label(
                $"CAMPAIGN   ·   NIGHT RUN {(p.QualifiedForHarbor ? "QUALIFIED" : p.NightRunCompleted ? "CLEARED · NOT QUALIFIED" : "—")}" +
                $"   ·   HARBOR RAIN {(p.HarborCompleted ? "COMPLETE" : p.HarborAvailable ? "UNLOCKED" : "LOCKED")}") { name = "campaign-status" };
            status.AddToClassList("nr-version");
            // Bottom-left (the version and music hints own the bottom-right corner).
            status.style.position = Position.Absolute;
            status.style.left = 48;
            status.style.right = StyleKeyword.Auto;
            status.style.bottom = 30;
            root.Q(className: "nr-title-screen")?.Add(status);
            // Development builds and the editor only: jump into Harbor Rain without replaying Night Run.
            if (Debug.isDebugBuild)
            {
                devHarbor = new UnityEngine.InputSystem.InputAction("DevHarborSelect", binding: "<Keyboard>/f9");
                devHarbor.performed += _ => DevEnterHarbor(arrival: false);
                devHarbor.Enable();
                devArrival = new UnityEngine.InputSystem.InputAction("DevHarborArrival", binding: "<Keyboard>/f8");
                devArrival.performed += _ => DevEnterHarbor(arrival: true);
                devArrival.Enable();
            }
        }

        private void OnHarbor()
        {
            if (context?.Campaign == null || context.Flow.IsTransitioning) return;
            context.Campaign.Step = CampaignStep.Level2Select;
            context.Flow.GoToCarSelect();
        }

        /// <summary>Development shortcut (F9: Level 2 car selection, F8: the portal arrival). Never in release builds.</summary>
        public void DevEnterHarbor(bool arrival)
        {
            if (context?.Campaign == null || !Debug.isDebugBuild || context.Flow.IsTransitioning) return;
            var campaign = context.Campaign;
            campaign.Progress.DevUnlockHarbor();
            Debug.Log($"[Campaign] DEV: entering Harbor Rain ({(arrival ? "arrival" : "car selection")})");
            if (!arrival) { OnHarbor(); return; }
            var catalog = context.Config.VehicleCatalog;
            if (context.Session.SelectedVehicle == null && catalog != null && catalog.Default != null) context.Session.SelectVehicle(catalog.Default, catalog);
            campaign.Step = CampaignStep.Arrival;
            context.Session.SelectMission(campaign.Definition.Level2);
            context.Flow.StartMission();
        }

        public void Exit()
        {
            if (startButton != null) startButton.clicked -= OnStart;
            if (quitButton != null) quitButton.clicked -= OnQuit;
            if (introButton != null) introButton.clicked -= OnIntro;
            if (optionsButton != null) optionsButton.clicked -= OnOptions;
            options?.Close();
            devHarbor?.Dispose();
            devArrival?.Dispose();
            devHarbor = devArrival = null;
            context = null;
        }

        private void OnStart()
        {
            if (context?.Campaign != null) context.Campaign.Step = CampaignStep.Level1;
            context?.Flow.GoToCarSelect();
        }

        private void OnQuit() => context?.Flow.QuitGame();

        private void OnIntro() => context?.Flow.PlayIntro();

        /// <summary>Settings and controls, shared with the pause menu.</summary>
        private void OnOptions()
        {
            if (context == null || options.IsOpen) return;
            var rows = new System.Collections.Generic.List<OptionsMenu.Row>
            {
                options.Button("SETTINGS", options.PushSettings),
                options.Button("CONTROLS", options.PushControls),
                options.Button("BACK", () => { options.Close(); startButton.Focus(); })
            };
            if (context.Campaign != null)
                rows.Insert(2, options.Button("RESET CAMPAIGN PROGRESS", () =>
                    options.Push("RESET CAMPAIGN?", new System.Collections.Generic.List<OptionsMenu.Row>
                    {
                        options.Button("NO, KEEP MY PROGRESS", options.Pop),
                        options.Button("YES, START OVER", () =>
                        {
                            context.Campaign.Progress.Reset();
                            options.Close();
                            context.Flow.GoToFrontend();
                        })
                    }, "Clears Night Run qualification, the Harbor Rain unlock and the Harbor Rain cars.")));
            options.Open("OPTIONS", rows);
        }
    }
}
