using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Engine, clutch and gearbox. Turns pedal demand into drive torque at the driven wheels and keeps a physical
    /// engine speed: locked to the wheels when the clutch is engaged, free-revving (limited by engine inertia) when it
    /// slips or is open. Reverse is selected by holding the brake at standstill.
    /// </summary>
    public sealed class Drivetrain
    {
        /// <summary>Below this forward speed (m/s) the brake pedal can select reverse and the throttle can select first.</summary>
        private const float DirectionChangeSpeed = 0.6f;
        /// <summary>How long the pedal must be held at standstill before the direction changes, s.</summary>
        private const float DirectionChangeDelay = 0.2f;
        /// <summary>Clutch opening time at the start of a shift or when the handbrake is pulled, s.</summary>
        private const float ClutchReleaseTime = 0.03f;

        private EngineSettings engine;
        private TransmissionSettings transmission;
        private float directionTimer;

        public Gearbox Gearbox { get; } = new Gearbox();
        public float EngineRpm { get; private set; }
        /// <summary>0 = open, 1 = fully engaged.</summary>
        public float Clutch { get; private set; } = 1f;
        /// <summary>Net crank torque, Nm (negative = engine braking).</summary>
        public float EngineTorque { get; private set; }
        /// <summary>Torque delivered to all driven wheels together, Nm (signed in the wheels' rolling direction).</summary>
        public float DriveTorque { get; private set; }
        /// <summary>Engine inertia reflected through the gearing onto all driven wheels together, kg·m².</summary>
        public float ReflectedInertia { get; private set; }
        /// <summary>Throttle after reverse mapping and assists, 0..1.</summary>
        public float Throttle { get; private set; }
        /// <summary>Throttle the driver asked for after reverse mapping (before assists), 0..1.</summary>
        public float RequestedThrottle { get; private set; }
        /// <summary>Service brake demand after reverse mapping, 0..1.</summary>
        public float Brake { get; private set; }
        /// <summary>Gearbox × final drive for the current gear (signed).</summary>
        public float TotalRatio => Gearbox.Ratio * transmission.finalDrive;
        public bool IsClutchLocked { get; private set; }
        public float IdleRpm => engine.idleRpm;
        public float MaxRpm => engine.maxRpm;

        public void Configure(in EngineSettings engineSettings, in TransmissionSettings transmissionSettings)
        {
            engine = engineSettings;
            transmission = transmissionSettings;
            Gearbox.Configure(transmissionSettings);
            Reset();
        }

        public void Reset()
        {
            Gearbox.Reset();
            EngineRpm = engine.idleRpm;
            Clutch = 1f;
            EngineTorque = DriveTorque = ReflectedInertia = 0f;
            Throttle = RequestedThrottle = Brake = 0f;
            directionTimer = 0f;
        }

        /// <summary>Engine-braking (or regen) torque at an engine speed, Nm, positive.</summary>
        public float EngineBrakeAt(float rpm)
        {
            float span = engine.maxRpm - engine.idleRpm;
            return span > 0f ? engine.engineBrakeTorque * Mathf.Clamp01((rpm - engine.idleRpm) / span) : 0f;
        }

        /// <summary>Wheel torque the current gear would deliver at full requested throttle with no assist limit, Nm.</summary>
        public float UnlimitedDriveTorque()
        {
            float limiter = Mathf.Clamp01((engine.maxRpm - EngineRpm) / (engine.maxRpm * 0.015f));
            return RequestedThrottle * engine.TorqueAt(Mathf.Max(EngineRpm, engine.idleRpm)) * limiter
                   * Mathf.Abs(TotalRatio) * transmission.efficiency;
        }

        /// <param name="input">Raw driver input.</param>
        /// <param name="forwardSpeed">Body speed along its forward axis, m/s.</param>
        /// <param name="drivenWheelSpeed">Mean angular velocity of the driven wheels, rad/s.</param>
        /// <param name="throttleLimit">Assist limit (traction control, speed limiter), 0..1.</param>
        /// <param name="clutchOpen">Force the clutch open (handbrake turn).</param>
        public void Update(in DrivingInput input, float forwardSpeed, float drivenWheelSpeed, float throttleLimit, bool clutchOpen, float dt)
        {
            UpdateDirection(input, forwardSpeed, dt);
            bool reverse = Gearbox.Gear < 0;
            RequestedThrottle = reverse ? input.Brake : input.Throttle;
            Brake = reverse ? input.Throttle : input.Brake;
            Throttle = RequestedThrottle * Mathf.Clamp01(throttleLimit);

            // Shift decisions use the speed the engine would turn at with the clutch locked, so a slipping launch
            // clutch does not trigger shifts.
            Gearbox.Tick(dt);
            Gearbox.UpdateAutomatic(Mathf.Max(0f, drivenWheelSpeed * TotalRatio * VehicleUnits.RadPerSecToRpm), Throttle);

            float ratio = TotalRatio;
            float lockedRpm = Mathf.Max(0f, drivenWheelSpeed * ratio * VehicleUnits.RadPerSecToRpm);
            bool engage = !Gearbox.IsShifting && !clutchOpen && Gearbox.Gear != 0;
            float clutchTime = engage ? transmission.clutchEngageTime : ClutchReleaseTime;
            Clutch = Mathf.MoveTowards(Clutch, engage ? 1f : 0f, dt / Mathf.Max(clutchTime, 1e-3f));

            // A slipping clutch holds the engine near the launch speed until the wheels catch up.
            float slipFloor = engine.launchRpm > 0f ? engine.idleRpm + Throttle * (engine.launchRpm - engine.idleRpm) : 0f;
            IsClutchLocked = Clutch >= 1f && lockedRpm >= slipFloor;

            if (IsClutchLocked)
            {
                EngineRpm = lockedRpm;
            }
            else if (Gearbox.IsShifting)
            {
                // Rev-match toward the new gear so engagement is seamless.
                float t = Mathf.Clamp01(dt / Mathf.Max(Gearbox.ShiftTimeRemaining, dt));
                EngineRpm = Mathf.Lerp(EngineRpm, Mathf.Max(lockedRpm, engine.idleRpm), t);
            }
            else
            {
                float freeRpm = engine.idleRpm + Throttle * (engine.maxRpm - engine.idleRpm);
                float target = Mathf.Lerp(freeRpm, Mathf.Max(lockedRpm, slipFloor), Clutch);
                float torque = target > EngineRpm
                    ? Mathf.Max(Throttle, 0.1f) * engine.TorqueAt(Mathf.Max(EngineRpm, engine.idleRpm))
                    : EngineBrakeAt(EngineRpm) + 0.1f * engine.peakTorque;
                float rpmRate = torque / engine.inertia * VehicleUnits.RadPerSecToRpm;
                EngineRpm = Mathf.MoveTowards(EngineRpm, target, rpmRate * dt);
            }
            EngineRpm = Mathf.Clamp(EngineRpm, engine.idleRpm, engine.maxRpm * 1.02f);

            float limiter = Mathf.Clamp01((engine.maxRpm - EngineRpm) / (engine.maxRpm * 0.015f));
            float combustion = Throttle * engine.TorqueAt(Mathf.Max(EngineRpm, engine.idleRpm)) * limiter;
            EngineTorque = combustion - (1f - Throttle) * EngineBrakeAt(EngineRpm);

            // A slipping clutch passes drive torque but cannot transmit engine braking below the engine's speed.
            float transmitted = IsClutchLocked ? EngineTorque * Clutch : Mathf.Max(0f, EngineTorque) * Clutch;
            DriveTorque = transmitted * ratio * transmission.efficiency;
            ReflectedInertia = IsClutchLocked ? engine.inertia * ratio * ratio : 0f;
        }

        private void UpdateDirection(in DrivingInput input, float forwardSpeed, float dt)
        {
            bool reverse = Gearbox.Gear < 0;
            // The handbrake parks the car: holding it never turns the brake pedal into reverse.
            bool wantsReverse = !reverse && !input.Handbrake && forwardSpeed < DirectionChangeSpeed && input.Brake > 0.1f && input.Throttle < 0.05f;
            bool wantsForward = reverse && forwardSpeed > -DirectionChangeSpeed && input.Throttle > 0.1f && input.Brake < 0.05f;
            if (!wantsReverse && !wantsForward)
            {
                directionTimer = 0f;
                return;
            }
            directionTimer += dt;
            if (directionTimer < DirectionChangeDelay) return;
            directionTimer = 0f;
            Gearbox.SelectDirection(wantsReverse);
        }
    }
}
