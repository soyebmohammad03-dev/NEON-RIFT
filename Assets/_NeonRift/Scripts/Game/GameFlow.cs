using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonRift.Game
{
    /// <summary>
    /// Owns the single loaded content scene. The Bootstrap scene stays loaded underneath;
    /// content scenes are loaded additively, activated, then handed the <see cref="GameContext"/>.
    /// </summary>
    internal sealed class GameFlow : IGameFlow
    {
        private readonly GameConfig config;
        private readonly RunSession session;
        private readonly LoadingOverlay overlay;

        private GameContext context;
        private Scene contentScene;
        private ISceneEntryPoint contentEntry;

        public GameState State { get; private set; } = GameState.None;
        public bool IsTransitioning { get; private set; }
        public event Action<GameState> StateChanged;

        public GameFlow(GameConfig config, RunSession session, LoadingOverlay overlay)
        {
            this.config = config;
            this.session = session;
            this.overlay = overlay;
        }

        public void Initialize(GameContext gameContext) => context = gameContext;

        public async Awaitable BootAsync(Scene bootstrapScene)
        {
            SetState(GameState.Booting);

            // Entering Play Mode from a content scene in the editor: adopt it instead of loading the front end.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != bootstrapScene && scene.isLoaded)
                {
                    AdoptScene(scene);
                    await overlay.HideAsync(config.FadeSeconds);
                    return;
                }
            }

            await TransitionAsync(GameState.Frontend, config.FrontendScene);
        }

        public void GoToFrontend() => Request(GameState.Frontend, config.FrontendScene);

        public void GoToCarSelect() => Request(GameState.CarSelect, config.CarSelectScene);

        public void StartMission()
        {
            if (!session.IsReadyToLaunch)
            {
                Debug.LogError("[GameFlow] Cannot start mission: a vehicle and a mission must be selected first.");
                return;
            }
            if (!GameConfig.IsPlayable(session.SelectedMission))
            {
                Debug.LogError($"[GameFlow] Mission '{session.SelectedMission.Id}' is not playable in this build.");
                return;
            }
            session.ClearResult();
            Request(GameState.Mission, session.SelectedMission.SceneName);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private async void Request(GameState target, string sceneName)
        {
            if (IsTransitioning)
            {
                Debug.LogWarning($"[GameFlow] Ignored request for {target}: a transition is already running.");
                return;
            }
            try
            {
                await TransitionAsync(target, sceneName);
            }
            catch (OperationCanceledException)
            {
                // Play Mode exited mid-transition.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async Awaitable TransitionAsync(GameState target, string sceneName)
        {
            IsTransitioning = true;
            try
            {
                await overlay.ShowAsync(config.FadeSeconds);

                contentEntry?.Exit();
                contentEntry = null;

                if (contentScene.IsValid() && contentScene.isLoaded)
                    await WaitFor(SceneManager.UnloadSceneAsync(contentScene));

                var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (load == null)
                    throw new InvalidOperationException($"Scene '{sceneName}' could not be loaded. Is it in the Build Profile scene list?");
                await WaitFor(load);

                contentScene = SceneManager.GetSceneByName(sceneName);
                SceneManager.SetActiveScene(contentScene);
                await WaitFor(Resources.UnloadUnusedAssets());

                contentEntry = FindEntryPoint(contentScene);
                SetState(target);
                if (contentEntry != null) contentEntry.Enter(context);
                else Debug.LogError($"[GameFlow] Scene '{sceneName}' has no root component implementing ISceneEntryPoint.");
            }
            finally
            {
                await overlay.HideAsync(config.FadeSeconds);
                IsTransitioning = false;
            }
        }

        private void AdoptScene(Scene scene)
        {
            var state = StateForScene(scene.name);
            if (state == GameState.Mission) PrepareDevelopmentSession(scene.name);

            contentScene = scene;
            SceneManager.SetActiveScene(scene);
            contentEntry = FindEntryPoint(scene);
            if (contentEntry == null)
            {
                Debug.LogWarning($"[GameFlow] '{scene.name}' has no ISceneEntryPoint; running it without game flow.");
                SetState(GameState.None);
                return;
            }
            SetState(state);
            contentEntry.Enter(context);
        }

        private void PrepareDevelopmentSession(string sceneName)
        {
            if (session.SelectedVehicle == null && config.VehicleCatalog != null && config.VehicleCatalog.Default != null)
                session.SelectVehicle(config.VehicleCatalog.Default, config.VehicleCatalog);

            var mission = config.FindMissionForScene(sceneName);
            if (mission != null) session.SelectMission(mission);
            else Debug.LogWarning($"[GameFlow] No MissionDefinition references scene '{sceneName}'.");
        }

        private GameState StateForScene(string sceneName)
        {
            if (sceneName == config.FrontendScene) return GameState.Frontend;
            if (sceneName == config.CarSelectScene) return GameState.CarSelect;
            return config.FindMissionForScene(sceneName) != null ? GameState.Mission : GameState.None;
        }

        private void SetState(GameState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        private static ISceneEntryPoint FindEntryPoint(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out ISceneEntryPoint entry))
                    return entry;
            return null;
        }

        private static async Awaitable WaitFor(AsyncOperation operation)
        {
            while (!operation.isDone)
                await Awaitable.NextFrameAsync();
        }
    }
}
