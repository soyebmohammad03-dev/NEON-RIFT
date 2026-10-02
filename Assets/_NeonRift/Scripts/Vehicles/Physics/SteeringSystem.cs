using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Rate-limited steering with a speed-dependent lock and Ackermann geometry. Angles are road-wheel angles in
    /// degrees, positive = right.
    /// </summary>
    public sealed class SteeringSystem
    {
        private SteeringSettings settings;
        private float wheelbase;
        private float track;
        private float frontPeakSlipAngle;
        private float gripAcceleration;

        /// <summary>Smoothed steering input, −1..1.</summary>
        public float Input { get; private set; }
        /// <summary>Current lock at full input, degrees.</summary>
        public float Limit { get; private set; }
        /// <summary>Centre-line steer angle, degrees.</summary>
        public float Angle { get; private set; }
        public float LeftAngle { get; private set; }
        public float RightAngle { get; private set; }

        /// <param name="gripAcceleration">Lateral acceleration the tyres can sustain, m/s².</param>
        public void Configure(in SteeringSettings steering, float wheelbaseMetres, float trackMetres, float frontPeakSlipAngleDeg, float gripAcceleration)
        {
            settings = steering;
            wheelbase = Mathf.Max(0.5f, wheelbaseMetres);
            track = Mathf.Max(0.5f, trackMetres);
            frontPeakSlipAngle = frontPeakSlipAngleDeg;
            this.gripAcceleration = Mathf.Max(1f, gripAcceleration);
            Reset();
        }

        public void Reset()
        {
            Input = Angle = LeftAngle = RightAngle = 0f;
            Limit = settings.maxSteerAngle;
        }

        /// <summary>Road-wheel lock available at a speed, degrees.</summary>
        public float MaxAngleAt(float speed)
        {
            float lockAngle = settings.maxSteerAngle;
            float v2 = speed * speed;
            if (v2 < 0.01f) return lockAngle;
            float kinematic = Mathf.Atan(wheelbase * gripAcceleration / v2) * Mathf.Rad2Deg;
            float gripLimited = Mathf.Min(lockAngle, kinematic + frontPeakSlipAngle * settings.slipAngleAllowance);
            return Mathf.Lerp(lockAngle, gripLimited, settings.speedSensitivity);
        }

        public void Update(float rawInput, float speed, float dt)
        {
            rawInput = Mathf.Clamp(rawInput, -1f, 1f);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(speed) / (settings.highSpeedReferenceKph * VehicleUnits.KphToMs));
            float rateScale = Mathf.Lerp(1f, settings.highSpeedRateScale, speedFactor);
            bool returning = Mathf.Abs(rawInput) < Mathf.Abs(Input) || rawInput * Input < 0f;
            float rate = (returning ? settings.returnRate : settings.steerRate) * rateScale;
            Input = Mathf.MoveTowards(Input, rawInput, rate * dt);

            Limit = MaxAngleAt(Mathf.Abs(speed));
            Angle = Input * Limit;

            float a = Mathf.Abs(Angle);
            if (a < 0.01f)
            {
                LeftAngle = RightAngle = Angle;
                return;
            }
            float radius = wheelbase / Mathf.Tan(a * Mathf.Deg2Rad);
            float inner = Mathf.Lerp(a, Mathf.Atan(wheelbase / Mathf.Max(0.1f, radius - track * 0.5f)) * Mathf.Rad2Deg, settings.ackermann);
            float outer = Mathf.Lerp(a, Mathf.Atan(wheelbase / (radius + track * 0.5f)) * Mathf.Rad2Deg, settings.ackermann);
            if (Angle > 0f) { RightAngle = inner; LeftAngle = outer; }
            else { LeftAngle = -inner; RightAngle = -outer; }
        }
    }
}
