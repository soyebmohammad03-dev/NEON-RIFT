namespace NeonRift.Vehicles
{
    /// <summary>
    /// Snapshot of a vehicle's state after its last physics step. Intended for engine/tyre audio, HUD, camera and AI.
    /// Per-wheel detail is on <see cref="VehicleController.Wheels"/>.
    /// </summary>
    public struct VehicleTelemetry
    {
        /// <summary>Speed over ground, m/s.</summary>
        public float Speed;
        public float SpeedKph;
        /// <summary>Speed along the body's forward axis, m/s (negative when reversing).</summary>
        public float ForwardSpeed;
        /// <summary>Speed / estimated top speed, 0..1+.</summary>
        public float NormalizedSpeed;

        /// <summary>Raw driver input.</summary>
        public float ThrottleInput, BrakeInput, SteerInput;
        public bool Handbrake;
        /// <summary>Effective throttle after reverse mapping and assists, 0..1 (what the engine receives).</summary>
        public float Throttle;
        /// <summary>Effective service-brake demand, 0..1.</summary>
        public float Brake;
        /// <summary>Centre-line road-wheel angle, degrees.</summary>
        public float SteerAngle;

        public float EngineRpm;
        /// <summary>(rpm − idle) / (max − idle), 0..1.</summary>
        public float NormalizedEngineRpm;
        /// <summary>Net crank torque, Nm (negative = engine braking).</summary>
        public float EngineTorque;
        /// <summary>Crank torque / peak torque, −1..1. Positive under power, negative on the overrun.</summary>
        public float EngineLoad;
        /// <summary>−1 reverse, 0 neutral, 1..n forward.</summary>
        public int Gear;
        public bool IsShifting;
        public float Clutch;
        /// <summary>Incremented on every gear change; compare with a cached value to detect shifts.</summary>
        public int ShiftCount;

        /// <summary>Mean driven-wheel speed, rpm.</summary>
        public float DrivenWheelRpm;
        /// <summary>Largest normalised combined slip over grounded tyres (1 = at peak grip, above = sliding).</summary>
        public float MaxSlip;
        /// <summary>Largest |slip ratio| over grounded tyres.</summary>
        public float MaxSlipRatio;
        /// <summary>Largest |slip angle| over grounded tyres, degrees.</summary>
        public float MaxSlipAngle;
        /// <summary>Sliding speed summed over grounded tyres, m/s (tyre squeal driver).</summary>
        public float SkidSpeed;

        public int GroundedWheels;
        public bool IsGrounded;
        public bool AllWheelsGrounded;

        /// <summary>Body acceleration in g (smoothed): + = forward / right.</summary>
        public float LongitudinalG, LateralG;
        /// <summary>Yaw rate, deg/s (+ = turning right).</summary>
        public float YawRate;
        /// <summary>Traction-control throttle factor this step (1 = not intervening).</summary>
        public float TractionLimit;
    }
}
