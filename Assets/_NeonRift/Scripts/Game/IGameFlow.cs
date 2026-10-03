using System;

namespace NeonRift.Game
{
    public enum GameState
    {
        None,
        Booting,
        Intro,
        Frontend,
        CarSelect,
        Mission
    }

    /// <summary>Marks the scene entry point that runs the opening cinematic (it may share a scene with a mission).</summary>
    public interface IIntroEntryPoint : ISceneEntryPoint
    {
    }

    /// <summary>High-level navigation between game states. Requests made during a transition are ignored.</summary>
    public interface IGameFlow
    {
        GameState State { get; }
        bool IsTransitioning { get; }
        event Action<GameState> StateChanged;

        void GoToFrontend();
        /// <summary>Plays the opening cinematic (it ends on the title, then Car Select).</summary>
        void PlayIntro();
        void GoToCarSelect();
        /// <summary>Loads the session's selected mission with the session's selected vehicle.</summary>
        void StartMission();
        void QuitGame();
    }
}
