using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>How an AI driver drives: commitment in corners and braking, lane craft, obstacle handling, recovery and
    /// (optional) rubber-banding. Same physics as the player; only the decisions come from here.</summary>
    [CreateAssetMenu(menuName = "Neon Rift/Racing/Racer Profile", fileName = "RacerProfile")]
    public sealed class RacerProfile : ScriptableObject
    {
        [SerializeField] private string displayName = "RIVAL";

        [Header("Pace")]
        [Tooltip("Lateral acceleration the driver commits to in corners, in g.")]
        [SerializeField, Range(0.4f, 1.4f)] private float cornerGrip = 0.95f;
        [Tooltip("Deceleration the braking planner assumes, m/s².")]
        [SerializeField, Range(3f, 14f)] private float braking = 8f;
        [Tooltip("Multiplies each street's advisory speed limit.")]
        [SerializeField, Range(0.5f, 2f)] private float streetSpeedScale = 1.25f;
        [SerializeField, Range(10f, 100f)] private float maxSpeed = 72f;

        [Header("Steering")]
        [SerializeField, Range(2f, 12f)] private float lookAheadMin = 5f;
        [SerializeField, Range(10f, 40f)] private float lookAheadMax = 28f;
        [Tooltip("Look-ahead per m/s of speed, s.")]
        [SerializeField, Range(0.1f, 0.8f)] private float lookAheadTime = 0.32f;
        [Tooltip("How fast the driver moves across the road when changing line, m/s.")]
        [SerializeField, Range(0.5f, 8f)] private float laneChangeRate = 3.5f;

        [Header("Traffic and obstacles")]
        [Tooltip("Probe length = this + speed × probeTime, m.")]
        [SerializeField, Range(5f, 40f)] private float probeBase = 14f;
        [SerializeField, Range(0.5f, 3f)] private float probeTime = 1.5f;
        [Tooltip("0 = waits behind slower cars, 1 = always looks for a gap.")]
        [SerializeField, Range(0f, 1f)] private float aggression = 0.7f;

        [Header("Recovery")]
        [SerializeField, Range(0.5f, 6f)] private float stuckSeconds = 2f;
        [SerializeField, Range(0.5f, 4f)] private float reverseSeconds = 1.5f;
        [Tooltip("After this many failed reversals the car is righted where it stands.")]
        [SerializeField, Range(1, 6)] private int reversesBeforeReset = 3;

        [Header("Rubber band (off = 0)")]
        [Tooltip("Pace bonus when far behind the player (0.06 = +6 %).")]
        [SerializeField, Range(0f, 0.3f)] private float catchUp = 0.05f;
        [Tooltip("Pace reduction when far ahead of the player.")]
        [SerializeField, Range(0f, 0.3f)] private float easeOff = 0.06f;
        [Tooltip("Gap at which the band is fully applied, m.")]
        [SerializeField, Min(10f)] private float bandDistance = 220f;

        public string DisplayName => displayName;
        public float CornerAcceleration => cornerGrip * 9.81f;
        public float Braking => braking;
        public float StreetSpeedScale => streetSpeedScale;
        public float MaxSpeed => maxSpeed;
        public float LookAheadMin => lookAheadMin;
        public float LookAheadMax => lookAheadMax;
        public float LookAheadTime => lookAheadTime;
        public float LaneChangeRate => laneChangeRate;
        public float ProbeBase => probeBase;
        public float ProbeTime => probeTime;
        public float Aggression => aggression;
        public float StuckSeconds => stuckSeconds;
        public float ReverseSeconds => reverseSeconds;
        public int ReversesBeforeReset => reversesBeforeReset;

        /// <summary>Pace multiplier for a gap to the player (positive = this driver is behind), m.</summary>
        public float RubberBand(float gapBehindPlayer)
        {
            float t = Mathf.Clamp(gapBehindPlayer / bandDistance, -1f, 1f);
            return t >= 0f ? 1f + catchUp * t : 1f + easeOff * t;
        }

#if UNITY_EDITOR
        public void EditorConfigure(string name, float grip, float brakingDecel, float speedScale, float aggressionLevel, float catchUpBonus, float easeOffPenalty)
        {
            displayName = name;
            cornerGrip = grip;
            braking = brakingDecel;
            streetSpeedScale = speedScale;
            aggression = aggressionLevel;
            catchUp = catchUpBonus;
            easeOff = easeOffPenalty;
        }
#endif
    }
}
