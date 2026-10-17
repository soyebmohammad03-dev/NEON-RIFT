using System;
using NeonRift.Audio;
using NeonRift.Input;
using UnityEngine.InputSystem;
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
        private GameSettings settings;
        private UnityEngine.InputSystem.InputAction musicToggle;

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
            // Snapshot fades must run while the game is paused (time scale 0).
            if (config.AudioMixer != null && config.AudioMixer.Mixer != null) config.AudioMixer.Mixer.updateMode = UnityEngine.Audio.AudioMixerUpdateMode.UnscaledTime;
            flow = new GameFlow(config, session, loadingOverlay);
            settings = new GameSettings(audio);
            settings.Load();
            var context = new GameContext(config, session, flow, controls, audio, settings);
            if (config.Campaign != null) context.Campaign = CreateCampaign(context);
            flow.Initialize(context);
            // M (or gamepad Y) toggles the music anywhere in the game.
            musicToggle = new UnityEngine.InputSystem.InputAction("ToggleMusic", UnityEngine.InputSystem.InputActionType.Button);
            musicToggle.AddBinding("<Keyboard>/m");
            musicToggle.AddBinding("<Gamepad>/buttonNorth");
            musicToggle.performed += _ => settings.ToggleMusic();
            musicToggle.Enable();
            flow.StateChanged += OnStateChanged;
            // Phones / tablets: on-screen controls that feed a virtual gamepad (keyboard and gamepad keep working).
            if (Application.isMobilePlatform)
                gameObject.AddComponent<TouchControls>().Initialize(controls, flow, settings);
        }

        /// <summary>Campaign bootstrap only: the saved progress and the presentation overlay (above the HUDs, below the loading fade).</summary>
        private CampaignSession CreateCampaign(GameContext context)
        {
            var progress = new CampaignProgress();
            progress.Load();
            var campaign = new CampaignSession(config.Campaign, progress);
            var loadingDoc = loadingOverlay.GetComponent<UnityEngine.UIElements.UIDocument>();
            var go = new GameObject("CampaignOverlay");
            go.transform.SetParent(transform, false);
            var doc = go.AddComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = loadingDoc.panelSettings;
            doc.sortingOrder = loadingDoc.sortingOrder - 1f;
            go.AddComponent<CampaignOverlay>().Initialize(context, campaign);
            Debug.Log($"[Campaign] started: night run {(progress.NightRunCompleted ? "done" : "open")}, harbor {(progress.HarborAvailable ? "unlocked" : "locked")}, cars {(progress.HarborVehiclesUnlocked ? "unlocked" : "locked")}, harbor {(progress.HarborCompleted ? "done" : "open")}");
            return campaign;
        }

        // Menus get the menu mix; missions the gameplay mix. Results/lockdown/ducking are driven by mission code.
        private void OnStateChanged(GameState state) =>
            audio.TransitionTo(state is GameState.Mission or GameState.Intro ? MixerState.Gameplay : MixerState.Menu, config.FadeSeconds);

        private async void Start()
        {
            if (flow == null) return;
            // Mixer parameters set in Awake are ignored; apply the saved settings here.
            settings.ApplyAudio();
            settings.ApplyGraphics();
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
            musicToggle?.Dispose();
        }
    }
}
