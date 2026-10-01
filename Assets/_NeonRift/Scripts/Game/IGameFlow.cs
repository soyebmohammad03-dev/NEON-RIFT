using System;

namespace NeonRift.Game
{
    public enum GameState
    {
        None,
        Booting,
        Frontend,
        CarSelect,
        Mission
    }

    /// <summary>High-level navigation between game states. Requests made during a transition are ignored.</summary>
    public interface IGameFlow
    {
        GameState State { get; }
        bool IsTransitioning { get; }
        event Action<GameState> StateChanged;

        void GoToFrontend();
        void GoToCarSelect();
        /// <summary>Loads the session's selected mission with the session's selected vehicle.</summary>
        void StartMission();
        void QuitGame();
    }
}
