using System;
using NeonRift.Audio;
using NeonRift.Input;
using UnityEngine;

namespace NeonRift.Game
{
    /// <summary>
    /// Composition root. Lives in the Bootstrap scene for the whole session and builds the
    /// objects every scene shares. This is the only place long-lived services are created.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameRoot : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private LoadingOverlay loadingOverlay;

        private NeonRiftControls controls;
        private GameFlow flow;
        private AudioMixerService audio;

        private void Awake()
        {
            if (config == null || loadingOverlay == null)
            {
                Debug.LogError("[GameRoot] GameConfig and LoadingOverlay must be assigned.", this);
                enabled = false;
                return;
            }

            controls = new NeonRiftControls();
            var session = new RunSession();
            audio = new AudioMixerService(config.AudioMixer);
            flow = new GameFlow(config, session, loadingOverlay);
            flow.Initialize(new GameContext(config, session, flow, controls, audio));
            flow.StateChanged += OnStateChanged;
        }

        // Menus get the menu mix; missions the gameplay mix. Results/lockdown/ducking are driven by mission code.
        private void OnStateChanged(GameState state) =>
            audio.TransitionTo(state is GameState.Mission or GameState.Intro ? MixerState.Gameplay : MixerState.Menu, config.FadeSeconds);

        private async void Start()
        {
            if (flow == null) return;
            try
            {
                await flow.BootAsync(gameObject.scene);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        private void OnDestroy()
        {
            if (flow != null) flow.StateChanged -= OnStateChanged;
            controls?.Dispose();
        }
    }
}
