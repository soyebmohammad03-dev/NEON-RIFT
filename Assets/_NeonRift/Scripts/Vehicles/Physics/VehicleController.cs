using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Rigidbody vehicle with raycast suspension, a combined-slip tyre model, engine/gearbox drivetrain, anti-roll
    /// bars and aerodynamics. All tuning comes from a <see cref="VehiclePhysicsProfile"/>; all geometry from the
    /// prefab's <see cref="VehicleRig"/>. Drivers (player, AI, replay) plug in through <see cref="IVehicleInputSource"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(VehicleRig))]
    public sealed class VehicleController : MonoBehaviour, IVehicleInputReceiver
    {
        /// <summary>Spin can not exceed this (rad/s); keeps crashes from producing absurd tumbling.</summary>
        private const float MaxBodyAngularSpeed = 25f;
        /// <summary>Speed-limiter fade band below the limit, m/s.</summary>
        private const float LimiterBand = 1.5f;
        /// <summary>How fast traction control gives throttle back, per second.</summary>
        private const float TractionRecoveryRate = 3f;
        /// <summary>Throttle cut per unit of driven-wheel slip beyond the tyre's peak slip ratio.</summary>
        private const float TractionFeedbackGain = 2f;
        /// <summary>Traction control judges slip against at least this ground speed, m/s, so a launch is not choked.</summary>
        private const float TractionReferenceSpeed = 4f;
        /// <summary>Telemetry acceleration smoothing time constant, s.</summary>
        private const float AccelerationSmoothing = 0.1f;
        /// <summary>Differential and lock fractions are defined per 0.01 s step.</summary>
        private const float ReferenceStep = 0.01f;

        [Tooltip("Layers the wheels can stand on. Exclude the Vehicle layer so wheels never hit their own body.")]
        [SerializeField] private LayerMask groundLayers = ~0;
        [Tooltip("Rays per wheel sampling the tyre's profile along its rolling direction. 1 = single centre ray (cheapest, harsh over sharp bumps); 5 = smooth roll-over. Lower for distant AI cars or low-end mobile.")]
        [SerializeField, Range(1, 9)] private int contactSamples = 5;
        [Tooltip("When on, FixedUpdate does nothing and the owner calls Step (tests, offline tuning, replays).")]
        [SerializeField] private bool externalStepping;

        private readonly Drivetrain drivetrain = new Drivetrain();
        private readonly SteeringSystem steering = new SteeringSystem();
        private VehicleWheel[] wheels = Array.Empty<VehicleWheel>();
        private float[] spinAngles = Array.Empty<float>();
        private Vector3[] rigCentres;
        private Rigidbody body;
        private VehicleRig rig;
        private VehiclePhysicsProfile profile;
        private IVehicleInputSource inputSource;
        private PhysicsScene physicsScene;
        private VehicleTelemetry telemetry;
        private DrivingInput lastInput;
        private Vector3 frontAxleLocal, rearAxleLocal;
        private Vector3 lastVelocity;
        private Vector2 smoothedAcceleration;
        private float tractionLimit = 1f;
        private int shiftCount;
        private int drivenCount;
        private int heldWheels;

        public VehiclePhysicsProfile Profile => profile;
        public bool IsConfigured => profile != null;
        public Rigidbody Body => body;
        public VehicleRig Rig => rig;
        public IReadOnlyList<VehicleWheel> Wheels => wheels;
        public Drivetrain Drivetrain => drivetrain;
        public SteeringSystem Steering => steering;
        public VehicleTelemetry Telemetry => telemetry;
        public DrivingInput LastInput => lastInput;
        public IVehicleInputSource InputSource => inputSource;
        /// <summary>Steady-state top speed predicted from the profile, m/s.</summary>
        public float EstimatedTopSpeed { get; private set; }
        public float Wheelbase { get; private set; }
        public Vector3 LocalCentreOfMass => body != null ? body.centerOfMass : Vector3.zero;

        public int ContactSamples
        {
            get => contactSamples;
            set => contactSamples = Mathf.Clamp(value, 1, 9);
        }

        public bool ExternalStepping
        {
            get => externalStepping;
            set => externalStepping = value;
        }

        /// <summary>Raised from OnCollisionEnter for body impacts (damage, audio, camera shake).</summary>
        public event Action<VehicleCollision> Collided;

        public void SetInputSource(IVehicleInputSource source) => inputSource = source;

        /// <summary>Applies a physics profile. Call once after instantiating; may be called again to retune live.</summary>
        public void Configure(VehiclePhysicsProfile physicsProfile)
        {
            if (physicsProfile == null) throw new ArgumentNullException(nameof(physicsProfile));
            body = GetComponent<Rigidbody>();
            rig = GetComponent<VehicleRig>();
            physicsScene = gameObject.scene.GetPhysicsScene();
            profile = physicsProfile;

            BuildWheels();
            ConfigureBody();

            float drivenRadius = 0f;
            foreach (var w in wheels) if (profile.IsDriven(w.IsFront)) drivenRadius = Mathf.Max(drivenRadius, w.Radius);
            EstimatedTopSpeed = profile.EstimateTopSpeed(drivenRadius);

            drivetrain.Configure(profile.Engine, profile.Transmission);
            drivetrain.Gearbox.GearChanged -= OnGearChanged;
            drivetrain.Gearbox.GearChanged += OnGearChanged;

            float gripAcceleration = Mathf.Min(profile.FrontAxle.tyre.lateralGrip, profile.RearAxle.tyre.lateralGrip) * VehicleUnits.Gravity;
            steering.Configure(profile.Steering, Wheelbase, Track(true), profile.FrontAxle.tyre.peakSlipAngle, gripAcceleration);

            tractionLimit = 1f;
            lastVelocity = body.linearVelocity;
            telemetry = default;
        }

        /// <summary>Places the car upright at a pose with zero velocity and a reset drivetrain.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            foreach (var w in wheels) w.Reset();
            drivetrain.Reset();
            steering.Reset();
            tractionLimit = 1f;
            lastVelocity = Vector3.zero;
            smoothedAcceleration = Vector2.zero;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Recovery after a crash or roll-over: rights the car on the ground beneath it, keeping its heading.
        /// </summary>
        public void Recover(float dropHeight = 0.4f)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Vector3 position = transform.position;
            if (physicsScene.Raycast(position + Vector3.up * 5f, Vector3.down, out var hit, 50f, groundLayers, QueryTriggerInteraction.Ignore))
                position = hit.point;
            Teleport(position + Vector3.up * dropHeight, rotation);
        }

        private void FixedUpdate()
        {
            if (profile != null && !externalStepping) Step(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            if (profile != null) UpdateVisuals(Time.deltaTime);
        }

        /// <summary>Advances the vehicle by one physics step. Call before the physics scene simulates.</summary>
        public void Step(float dt)
        {
            if (profile == null || body.isKinematic || dt <= 0f) return;

            lastInput = Sanitise(inputSource?.ReadInput() ?? DrivingInput.None);
            var pose = new Pose(body.position, body.rotation);
            Vector3 up = pose.up;
            Vector3 velocity = body.linearVelocity;
            Vector3 localVelocity = Quaternion.Inverse(pose.rotation) * velocity;
            float forwardSpeed = localVelocity.z;

            // Suspension.
            foreach (var w in wheels)
                w.UpdateContact(physicsScene, pose, Quaternion.AngleAxis(w.SteerAngle, up) * pose.forward, body, groundLayers, contactSamples, dt);
            ApplySuspension();

            // Steering.
            steering.Update(lastInput.Steer, forwardSpeed, dt);
            foreach (var w in wheels)
                if (w.IsFront) w.SteerAngle = w.IsLeft ? steering.LeftAngle : steering.RightAngle;

            // Drivetrain.
            float drivenSpeed = 0f;
            foreach (var w in wheels) if (profile.IsDriven(w.IsFront)) drivenSpeed += w.AngularVelocity;
            drivenSpeed /= Mathf.Max(1, drivenCount);
            var assists = profile.Assists;
            float limiter = assists.speedLimiterKph > 0f
                ? Mathf.Clamp01((assists.speedLimiterKph * VehicleUnits.KphToMs - forwardSpeed) / LimiterBand)
                : 1f;
            drivetrain.Update(lastInput, forwardSpeed, drivenSpeed, tractionLimit * limiter, lastInput.Handbrake, dt);

            // Tyres.
            var brakes = profile.Brakes;
            var transmission = profile.Transmission;
            float serviceBrake = drivetrain.Brake * brakes.maxBrakeTorque;
            // Wheels held by their brakes share the job of holding the whole car (static friction).
            float holdMass = body.mass / Mathf.Max(1, heldWheels);
            heldWheels = 0;
            foreach (var w in wheels)
            {
                var axle = profile.Axle(w.IsFront);
                float share = DriveShare(w.IsFront);
                float brake = serviceBrake * 0.5f * (w.IsFront ? brakes.frontBias : 1f - brakes.frontBias);
                float handbrake = !w.IsFront && lastInput.Handbrake ? brakes.handbrakeTorque : 0f;
                Vector3 wheelForward = Quaternion.AngleAxis(w.SteerAngle, up) * pose.forward;
                w.UpdateTyre(body, wheelForward, axle.tyre, drivetrain.DriveTorque * share, brake, handbrake,
                             drivetrain.ReflectedInertia * share, brakes.antiLock, brakes.antiLockTarget, holdMass, dt);
                if (w.IsHeld) heldWheels++;
            }
            ApplyDifferentials(transmission.differentialLock, dt);

            ApplyAerodynamics(pose, velocity, forwardSpeed);
            UpdateTractionControl(dt);
            UpdateTelemetry(pose, localVelocity, dt);
        }

        private void ApplySuspension()
        {
            Span<float> forces = stackalloc float[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) forces[i] = wheels[i].SuspensionForce();

            // Anti-roll bars couple the two wheels of each axle.
            for (int i = 0; i < wheels.Length; i++)
            {
                var a = wheels[i];
                if (!a.IsLeft) continue;
                for (int j = 0; j < wheels.Length; j++)
                {
                    var b = wheels[j];
                    if (b.IsLeft || b.IsFront != a.IsFront) continue;
                    float bar = profile.Axle(a.IsFront).antiRollStiffness * (a.Compression - b.Compression);
                    if (a.IsGrounded) forces[i] += bar;
                    if (b.IsGrounded) forces[j] -= bar;
                }
            }
            for (int i = 0; i < wheels.Length; i++) wheels[i].ApplyLoad(body, forces[i]);
        }

        private float DriveShare(bool front)
        {
            var t = profile.Transmission;
            switch (t.driveType)
            {
                case DriveType.FrontWheelDrive: return front ? 0.5f : 0f;
                case DriveType.RearWheelDrive: return front ? 0f : 0.5f;
                default: return 0.5f * (front ? t.frontTorqueShare : 1f - t.frontTorqueShare);
            }
        }

        private void ApplyDifferentials(float lockAmount, float dt)
        {
            if (lockAmount <= 0f) return;
            float fraction = 1f - Mathf.Pow(1f - Mathf.Clamp01(lockAmount), dt / ReferenceStep);
            for (int i = 0; i < wheels.Length; i++)
            {
                var a = wheels[i];
                if (!a.IsLeft || !profile.IsDriven(a.IsFront)) continue;
                for (int j = 0; j < wheels.Length; j++)
                {
                    var b = wheels[j];
                    if (b.IsLeft || b.IsFront != a.IsFront) continue;
                    float ia = a.EffectiveInertia, ib = b.EffectiveInertia;
                    float mean = (a.AngularVelocity * ia + b.AngularVelocity * ib) / (ia + ib);
                    a.BlendAngularVelocity(mean, fraction);
                    b.BlendAngularVelocity(mean, fraction);
                }
            }
        }

        private void ApplyAerodynamics(in Pose pose, Vector3 velocity, float forwardSpeed)
        {
            var aero = profile.Aero;
            float speed = velocity.magnitude;
            if (speed < 0.1f) return;
            body.AddForce(-velocity / speed * aero.DragForce(speed));
            float q = 0.5f * aero.airDensity * forwardSpeed * forwardSpeed;
            Vector3 down = -pose.up;
            if (aero.frontDownforceArea > 0f) body.AddForceAtPosition(down * (q * aero.frontDownforceArea), pose.position + pose.rotation * frontAxleLocal);
            if (aero.rearDownforceArea > 0f) body.AddForceAtPosition(down * (q * aero.rearDownforceArea), pose.position + pose.rotation * rearAxleLocal);
        }

        /// <summary>
        /// Traction control. Feed-forward: limits throttle so the requested drive torque does not exceed what the
        /// driven tyres can transmit (plus what is needed to accelerate the wheels with the car). Feedback: cuts
        /// further when a driven wheel is already past its peak slip, so a spin is caught rather than sustained.
        /// </summary>
        private void UpdateTractionControl(float dt)
        {
            var assists = profile.Assists;
            float target = 1f;
            float requested = drivetrain.UnlimitedDriveTorque();
            if (assists.tractionControl && requested > 0f)
            {
                float min = float.MaxValue, sum = 0f, excessSlip = 0f;
                float acceleration = Mathf.Max(0f, smoothedAcceleration.y);
                foreach (var w in wheels)
                {
                    if (!profile.IsDriven(w.IsFront)) continue;
                    if (w.IsGrounded)
                    {
                        float allowed = profile.Axle(w.IsFront).tyre.peakSlipRatio * Mathf.Max(Mathf.Abs(telemetry.ForwardSpeed), TractionReferenceSpeed);
                        excessSlip = Mathf.Max(excessSlip, w.SlipSpeed / allowed - 1f);
                    }
                    float tyre = w.IsGrounded
                        ? TyreModel.AvailableLongitudinal(profile.Axle(w.IsFront).tyre, w.TyreForce.y, w.Load, w.StaticLoad, w.SurfaceGrip) * assists.tractionControlTarget * w.Radius
                        : 0f;
                    float capacity = (tyre + w.EffectiveInertia * acceleration / w.Radius) / DriveShareOf(w);
                    min = Mathf.Min(min, capacity);
                    sum += capacity * DriveShareOf(w);
                }
                float lockAmount = profile.Transmission.differentialLock;
                float capacityTotal = Mathf.Lerp(min, sum, lockAmount);
                target = Mathf.Clamp01(capacityTotal / requested) * Mathf.Clamp01(1f - excessSlip * TractionFeedbackGain);
            }
            tractionLimit = target < tractionLimit ? target : Mathf.MoveTowards(tractionLimit, target, TractionRecoveryRate * dt);
        }

        private float DriveShareOf(VehicleWheel w) => Mathf.Max(1e-3f, DriveShare(w.IsFront));

        private void UpdateTelemetry(in Pose pose, Vector3 localVelocity, float dt)
        {
            // Differentiate in world space, then express in the body frame (includes the centripetal term).
            Vector3 velocity = body.linearVelocity;
            Vector3 accel = Quaternion.Inverse(pose.rotation) * ((velocity - lastVelocity) / dt);
            lastVelocity = velocity;
            float k = 1f - Mathf.Exp(-dt / AccelerationSmoothing);
            smoothedAcceleration = Vector2.Lerp(smoothedAcceleration, new Vector2(accel.x, accel.z), k);

            var e = profile.Engine;
            ref var t = ref telemetry;
            float speed = body.linearVelocity.magnitude;
            t.Speed = speed;
            t.SpeedKph = speed * VehicleUnits.MsToKph;
            t.ForwardSpeed = localVelocity.z;
            t.NormalizedSpeed = EstimatedTopSpeed > 0f ? speed / EstimatedTopSpeed : 0f;
            t.ThrottleInput = lastInput.Throttle;
            t.BrakeInput = lastInput.Brake;
            t.SteerInput = lastInput.Steer;
            t.Handbrake = lastInput.Handbrake;
            t.Throttle = drivetrain.Throttle;
            t.Brake = drivetrain.Brake;
            t.SteerAngle = steering.Angle;
            t.EngineRpm = drivetrain.EngineRpm;
            t.NormalizedEngineRpm = e.maxRpm > e.idleRpm ? Mathf.Clamp01((drivetrain.EngineRpm - e.idleRpm) / (e.maxRpm - e.idleRpm)) : 0f;
            t.EngineTorque = drivetrain.EngineTorque;
            t.EngineLoad = e.peakTorque > 0f ? Mathf.Clamp(drivetrain.EngineTorque / e.peakTorque, -1f, 1f) : 0f;
            t.Gear = drivetrain.Gearbox.Gear;
            t.IsShifting = drivetrain.Gearbox.IsShifting;
            t.Clutch = drivetrain.Clutch;
            t.ShiftCount = shiftCount;
            t.TractionLimit = tractionLimit;
            t.LongitudinalG = smoothedAcceleration.y / VehicleUnits.Gravity;
            t.LateralG = smoothedAcceleration.x / VehicleUnits.Gravity;
            t.YawRate = Vector3.Dot(body.angularVelocity, pose.up) * Mathf.Rad2Deg;

            float drivenRpm = 0f, maxSlip = 0f, maxRatio = 0f, maxAngle = 0f, skid = 0f;
            int grounded = 0;
            foreach (var w in wheels)
            {
                if (profile.IsDriven(w.IsFront)) drivenRpm += w.Rpm;
                if (!w.IsGrounded) continue;
                grounded++;
                maxSlip = Mathf.Max(maxSlip, w.CombinedSlip);
                maxRatio = Mathf.Max(maxRatio, Mathf.Abs(w.SlipRatio));
                maxAngle = Mathf.Max(maxAngle, Mathf.Abs(w.SlipAngle));
                if (w.CombinedSlip > 1f)
                {
                    float v = Mathf.Max(Mathf.Abs(t.ForwardSpeed), 1.5f);
                    skid += new Vector2(w.SlipRatio * v, Mathf.Tan(w.SlipAngle * Mathf.Deg2Rad) * v).magnitude;
                }
            }
            t.DrivenWheelRpm = drivenRpm / Mathf.Max(1, drivenCount);
            t.MaxSlip = maxSlip;
            t.MaxSlipRatio = maxRatio;
            t.MaxSlipAngle = maxAngle;
            t.SkidSpeed = skid;
            t.GroundedWheels = grounded;
            t.IsGrounded = grounded > 0;
            t.AllWheelsGrounded = grounded == wheels.Length;
        }

        /// <summary>Moves wheel visuals to the suspension state and spins them. Called from LateUpdate.</summary>
        public void UpdateVisuals(float dt)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w.Pivot == null) continue;
                Vector3 centre = w.LocalRideCentre + Vector3.up * (profile.Axle(w.IsFront).bumpTravel - w.CentreOffset);
                w.Pivot.SetPositionAndRotation(transform.TransformPoint(centre), transform.rotation * Quaternion.Euler(0f, w.SteerAngle, 0f));
                if (w.Spin == null) continue;
                spinAngles[i] = Mathf.Repeat(spinAngles[i] + w.AngularVelocity * Mathf.Rad2Deg * dt, 360f);
                w.Spin.localRotation = Quaternion.Euler(spinAngles[i], 0f, 0f);
            }
        }

        private void OnGearChanged(int from, int to) => shiftCount++;

        private void OnCollisionEnter(Collision collision)
        {
            if (Collided == null || collision.contactCount == 0) return;
            var contact = collision.GetContact(0);
            Collided.Invoke(new VehicleCollision(collision.impulse.magnitude, collision.relativeVelocity.magnitude,
                                                 contact.point, contact.normal, collision.collider));
        }

        private void BuildWheels()
        {
            var rigWheels = rig.Wheels;
            if (rigCentres == null || rigCentres.Length != rigWheels.Count)
            {
                // Ride-height wheel centres are read from the untouched rig once; visuals move afterwards.
                rigCentres = new Vector3[rigWheels.Count];
                for (int i = 0; i < rigWheels.Count; i++)
                    rigCentres[i] = rigWheels[i].Pivot != null ? transform.InverseTransformPoint(rigWheels[i].Pivot.position) : Vector3.zero;
            }
            wheels = new VehicleWheel[rigWheels.Count];
            spinAngles = new float[rigWheels.Count];
            for (int i = 0; i < rigWheels.Count; i++) wheels[i] = new VehicleWheel(rigWheels[i], rigCentres[i]);

            float frontZ = 0f, rearZ = 0f;
            int nf = 0, nr = 0;
            float frontY = 0f, rearY = 0f;
            foreach (var w in wheels)
            {
                if (w.IsFront) { frontZ += w.LocalRideCentre.z; frontY += w.LocalRideCentre.y; nf++; }
                else { rearZ += w.LocalRideCentre.z; rearY += w.LocalRideCentre.y; nr++; }
            }
            if (nf == 0 || nr == 0) throw new InvalidOperationException($"{name}: the VehicleRig needs front and rear wheels.");
            frontAxleLocal = new Vector3(0f, frontY / nf, frontZ / nf);
            rearAxleLocal = new Vector3(0f, rearY / nr, rearZ / nr);
            Wheelbase = frontAxleLocal.z - rearAxleLocal.z;

            float frontShare = profile.Chassis.frontWeightDistribution;
            float weight = profile.Chassis.mass * VehicleUnits.Gravity;
            drivenCount = 0;
            foreach (var w in wheels)
            {
                float axleLoad = weight * (w.IsFront ? frontShare : 1f - frontShare);
                w.Configure(profile.Axle(w.IsFront), axleLoad / (w.IsFront ? nf : nr));
                if (profile.IsDriven(w.IsFront)) drivenCount++;
            }
        }

        private float Track(bool front)
        {
            float left = 0f, right = 0f;
            foreach (var w in wheels)
            {
                if (w.IsFront != front) continue;
                if (w.IsLeft) left = w.LocalRideCentre.x; else right = w.LocalRideCentre.x;
            }
            return Mathf.Abs(right - left);
        }

        private void ConfigureBody()
        {
            var c = profile.Chassis;
            body.isKinematic = false;
            body.useGravity = true;
            body.mass = c.mass;
            body.linearDamping = 0f;
            body.angularDamping = c.angularDamping;
            body.maxAngularVelocity = MaxBodyAngularSpeed;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.centerOfMass = new Vector3(0f, c.centreOfMassHeight,
                rearAxleLocal.z + c.frontWeightDistribution * (frontAxleLocal.z - rearAxleLocal.z));

            // Solid box of the car's outer dimensions, scaled per axis by the profile.
            Vector3 size = rig.Dimensions;
            float m = c.mass / 12f;
            body.inertiaTensor = Vector3.Scale(new Vector3(
                m * (size.y * size.y + size.z * size.z),
                m * (size.x * size.x + size.z * size.z),
                m * (size.x * size.x + size.y * size.y)), c.inertiaScale);
            body.inertiaTensorRotation = Quaternion.identity;
        }

        private static DrivingInput Sanitise(DrivingInput input)
        {
            static float Clean(float v, float min) => float.IsNaN(v) ? 0f : Mathf.Clamp(v, min, 1f);
            input.Throttle = Clean(input.Throttle, 0f);
            input.Brake = Clean(input.Brake, 0f);
            input.Steer = Clean(input.Steer, -1f);
            return input;
        }
    }
}
