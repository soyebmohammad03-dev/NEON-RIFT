namespace NeonRift.Audio
{
    /// <summary>Mixer snapshots the game can be in. Each maps to a snapshot in the mixer asset.</summary>
    public enum MixerState
    {
        Gameplay,
        Menu,
        Results,
        Lockdown,
        Ducked
    }

    /// <summary>User-facing volume channels (exposed mixer parameters).</summary>
    public enum AudioChannel
    {
        Master,
        Effects,
        Music,
        UI
    }
}
