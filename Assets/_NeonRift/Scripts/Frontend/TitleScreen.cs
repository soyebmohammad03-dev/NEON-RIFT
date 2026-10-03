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
            startButton.Focus();
        }

        public void Exit()
        {
            if (startButton != null) startButton.clicked -= OnStart;
            if (quitButton != null) quitButton.clicked -= OnQuit;
            if (introButton != null) introButton.clicked -= OnIntro;
            context = null;
        }

        private void OnStart() => context?.Flow.GoToCarSelect();

        private void OnQuit() => context?.Flow.QuitGame();

        private void OnIntro() => context?.Flow.PlayIntro();
    }
}
