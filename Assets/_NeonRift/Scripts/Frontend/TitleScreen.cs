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
            startButton.Focus();
        }

        public void Exit()
        {
            if (startButton != null) startButton.clicked -= OnStart;
            if (quitButton != null) quitButton.clicked -= OnQuit;
            if (introButton != null) introButton.clicked -= OnIntro;
            if (optionsButton != null) optionsButton.clicked -= OnOptions;
            options?.Close();
            context = null;
        }

        private void OnStart() => context?.Flow.GoToCarSelect();

        private void OnQuit() => context?.Flow.QuitGame();

        private void OnIntro() => context?.Flow.PlayIntro();

        /// <summary>Settings and controls, shared with the pause menu.</summary>
        private void OnOptions()
        {
            if (context == null || options.IsOpen) return;
            options.Open("OPTIONS", new System.Collections.Generic.List<OptionsMenu.Row>
            {
                options.Button("SETTINGS", options.PushSettings),
                options.Button("CONTROLS", options.PushControls),
                options.Button("BACK", () => { options.Close(); startButton.Focus(); })
            });
        }
    }
}
