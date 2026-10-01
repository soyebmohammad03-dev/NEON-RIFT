using NeonRift.Input;

namespace NeonRift.Game
{
    /// <summary>
    /// Everything a scene is allowed to know about the running game. Handed to each scene's
    /// <see cref="ISceneEntryPoint"/> by the flow — scenes never look it up globally.
    /// </summary>
    public sealed class GameContext
    {
        public GameConfig Config { get; }
        public RunSession Session { get; }
        public IGameFlow Flow { get; }
        public NeonRiftControls Controls { get; }

        public GameContext(GameConfig config, RunSession session, IGameFlow flow, NeonRiftControls controls)
        {
            Config = config;
            Session = session;
            Flow = flow;
            Controls = controls;
        }
    }

    /// <summary>
    /// Implemented by exactly one root-level component in each content scene.
    /// Enter runs after the scene's Awake/OnEnable and before its first Start.
    /// </summary>
    public interface ISceneEntryPoint
    {
        void Enter(GameContext context);
        void Exit();
    }
}
