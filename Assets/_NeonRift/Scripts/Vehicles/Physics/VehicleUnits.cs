namespace NeonRift.Vehicles
{
    public static class VehicleUnits
    {
        public const float Gravity = 9.81f;
        public const float MsToKph = 3.6f;
        public const float KphToMs = 1f / 3.6f;
        public const float RpmToRadPerSec = 2f * UnityEngine.Mathf.PI / 60f;
        public const float RadPerSecToRpm = 60f / (2f * UnityEngine.Mathf.PI);
    }
}
