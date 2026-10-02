namespace NeonRift.Missions
{
    /// <summary>How aware the city's security grid is of the player. Only ever escalates during a mission.</summary>
    public enum SecurityLevel
    {
        /// <summary>Nothing logged. Normal city.</summary>
        Calm,
        /// <summary>Intrusions logged (heat above zero); the grid is watching.</summary>
        Alert,
        /// <summary>Theft detected: the district is being sealed.</summary>
        Lockdown
    }
}
