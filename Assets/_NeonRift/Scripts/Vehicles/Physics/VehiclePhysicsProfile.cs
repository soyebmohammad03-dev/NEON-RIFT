using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Everything that gives a car its driving personality. Referenced by <see cref="VehicleDefinition"/> and applied to
    /// the spawned prefab by <see cref="VehicleController.Configure"/>. Geometry (wheel positions, radii, ride height)
    /// comes from the prefab's <see cref="VehicleRig"/>, so one profile could drive any body.
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Rift/Vehicles/Vehicle Physics Profile", fileName = "VehiclePhysics")]
    public sealed class VehiclePhysicsProfile : ScriptableObject
    {
        [SerializeField] private ChassisSettings chassis = ChassisSettings.Default;
        [SerializeField] private AxleSettings frontAxle = AxleSettings.Default;
        [SerializeField] private AxleSettings rearAxle = AxleSettings.Default;
        [SerializeField] private EngineSettings engine = EngineSettings.Default;
        [SerializeField] private TransmissionSettings transmission = TransmissionSettings.Default;
        [SerializeField] private BrakeSettings brakes = BrakeSettings.Default;
        [SerializeField] private SteeringSettings steering = SteeringSettings.Default;
        [SerializeField] private AeroSettings aero = AeroSettings.Default;
        [SerializeField] private AssistSettings assists = AssistSettings.Default;

        [Tooltip("Where the figures came from (published data, estimate, measured). Shown to designers only.")]
        [SerializeField, TextArea(2, 6)] private string tuningNotes;

        public ChassisSettings Chassis => chassis;
        public AxleSettings FrontAxle => frontAxle;
        public AxleSettings RearAxle => rearAxle;
        public EngineSettings Engine => engine;
        public TransmissionSettings Transmission => transmission;
        public BrakeSettings Brakes => brakes;
        public SteeringSettings Steering => steering;
        public AeroSettings Aero => aero;
        public AssistSettings Assists => assists;
        public string TuningNotes => tuningNotes;

        public AxleSettings Axle(bool front) => front ? frontAxle : rearAxle;

#if UNITY_EDITOR
        public void EditorConfigure(ChassisSettings chassisSettings, AxleSettings front, AxleSettings rear, EngineSettings engineSettings,
                                    TransmissionSettings transmissionSettings, BrakeSettings brakeSettings, SteeringSettings steeringSettings,
                                    AeroSettings aeroSettings, AssistSettings assistSettings, string notes)
        {
            chassis = chassisSettings;
            frontAxle = front;
            rearAxle = rear;
            engine = engineSettings;
            transmission = transmissionSettings;
            brakes = brakeSettings;
            steering = steeringSettings;
            aero = aeroSettings;
            assists = assistSettings;
            tuningNotes = notes;
        }
#endif

        public bool IsDriven(bool front) => transmission.driveType switch
        {
            DriveType.FrontWheelDrive => front,
            DriveType.RearWheelDrive => !front,
            _ => true
        };

        /// <summary>
        /// Steady-state top speed on level ground (m/s): the highest speed where the best gear still out-pulls drag
        /// and rolling resistance, capped by the rev limit in top gear and the speed limiter.
        /// </summary>
        public float EstimateTopSpeed(float drivenWheelRadius)
        {
            if (drivenWheelRadius <= 0f || transmission.ForwardGearCount == 0) return 0f;
            float rolling = 0.5f * (frontAxle.tyre.rollingResistance + rearAxle.tyre.rollingResistance) * chassis.mass * VehicleUnits.Gravity;
            float top = 0f;
            for (float v = 1f; v < 150f; v += 0.1f)
            {
                float best = 0f;
                foreach (float ratio in transmission.gearRatios)
                {
                    float total = ratio * transmission.finalDrive;
                    float rpm = v / drivenWheelRadius * total * VehicleUnits.RadPerSecToRpm;
                    if (rpm > engine.maxRpm) continue;
                    float force = engine.TorqueAt(Mathf.Max(rpm, engine.idleRpm)) * total * transmission.efficiency / drivenWheelRadius;
                    best = Mathf.Max(best, force);
                }
                if (best < aero.DragForce(v) + rolling) break;
                top = v;
            }
            if (assists.speedLimiterKph > 0f) top = Mathf.Min(top, assists.speedLimiterKph * VehicleUnits.KphToMs);
            return top;
        }

        /// <summary>Human-readable data problems; empty when the profile is usable.</summary>
        public List<string> Validate()
        {
            var p = new List<string>();
            var t = transmission;
            if (t.gearRatios == null || t.gearRatios.Length == 0) p.Add("No forward gears.");
            else
            {
                for (int i = 0; i < t.gearRatios.Length; i++)
                {
                    if (t.gearRatios[i] <= 0f) p.Add($"Gear {i + 1} ratio must be positive.");
                    if (i > 0 && t.gearRatios[i] >= t.gearRatios[i - 1]) p.Add($"Gear {i + 1} ratio must be lower than gear {i}.");
                }
            }
            if (engine.torqueCurve == null || engine.torqueCurve.length < 2) p.Add("Engine torque curve needs at least two keys.");
            if (engine.idleRpm >= engine.maxRpm) p.Add("Idle rpm must be below max rpm.");
            if (t.ForwardGearCount > 1)
            {
                if (t.shiftUpRpm >= engine.maxRpm) p.Add("Shift-up rpm must be below max rpm.");
                if (t.shiftDownRpm >= t.shiftUpRpm * 0.8f) p.Add("Shift-down rpm must leave a gap below shift-up rpm.");
                if (t.shiftDownRpm <= engine.idleRpm) p.Add("Shift-down rpm must be above idle.");
            }
            if (engine.launchRpm > 0f && engine.launchRpm <= engine.idleRpm) p.Add("Launch rpm must be above idle (or 0 for no clutch).");
            foreach (var (name, axle) in new[] { ("Front", frontAxle), ("Rear", rearAxle) })
            {
                if (axle.reboundDamping <= 0f || axle.bumpDamping <= 0f) p.Add($"{name} axle needs damping.");
                if (axle.tyre.peakSlipRatio <= 0f || axle.tyre.peakSlipAngle <= 0f) p.Add($"{name} tyre peak slip must be positive.");
            }
            if (brakes.maxBrakeTorque <= 0f) p.Add("No brake torque.");
            return p;
        }
    }
}
