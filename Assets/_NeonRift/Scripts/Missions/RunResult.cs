namespace NeonRift.Missions
{
    public enum MissionOutcome
    {
        None,
        Completed,
        Failed,
        Abandoned
    }

    /// <summary>Outcome of one mission attempt, handed from the mission scene to the results screen.</summary>
    public readonly struct RunResult
    {
        public readonly MissionOutcome Outcome;
        public readonly float ElapsedSeconds;
        public readonly int SecurityLevelReached;

        public RunResult(MissionOutcome outcome, float elapsedSeconds, int securityLevelReached)
        {
            Outcome = outcome;
            ElapsedSeconds = elapsedSeconds;
            SecurityLevelReached = securityLevelReached;
        }
    }
}
