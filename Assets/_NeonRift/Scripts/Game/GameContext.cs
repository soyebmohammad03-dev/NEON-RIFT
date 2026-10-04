using NeonRift.Audio;
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
        /// <summary>Mixer snapshots and user volumes.</summary>
        public AudioMixerService Audio { get; }
        /// <summary>Volumes, music on/off and graphics options (persisted).</summary>
        public GameSettings Settings { get; }

        public GameContext(GameConfig config, RunSession session, IGameFlow flow, NeonRiftControls controls, AudioMixerService audio,
                           GameSettings settings = null)
        {
            Settings = settings ?? new GameSettings(audio);
            Config = config;
            Session = session;
            Flow = flow;
            Controls = controls;
            Audio = audio;
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
