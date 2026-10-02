namespace NeonRift.Gameplay
{
    /// <summary>
    /// Implemented by scene objects that take part in a mission (zones, interactables, barriers, lights, alarms).
    /// The <see cref="MissionDirector"/> finds them in its scene and binds them to the running <see cref="MissionWorld"/>;
    /// they never look the mission up globally.
    /// </summary>
    public interface IMissionWorldComponent
    {
        void Bind(MissionWorld world);
        void Unbind();
    }
}
