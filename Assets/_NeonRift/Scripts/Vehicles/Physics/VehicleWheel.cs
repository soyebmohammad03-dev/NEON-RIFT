using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// One corner: raycast suspension (spring, damper, bump stop), wheel spin and tyre forces. State is public for
    /// telemetry and debugging; only <see cref="VehicleController"/> advances it.
    /// </summary>
    public sealed class VehicleWheel
    {
        /// <summary>Slip denominators never use a speed below this, m/s. Regularises slip at standstill.</summary>
        private const float SlipSpeedFloor = 1.5f;
        /// <summary>Below this spin rate (rad/s) a braked wheel is treated as stopped and may be held by its brake.</summary>
        private const float StoppedSpinRate = 0.05f;
        /// <summary>Below this speed (m/s, both directions) lateral grip acts as static friction.</summary>
        private const float StaticLateralSpeed = 0.5f;
        /// <summary>Step used to measure the tyre's longitudinal stiffness, in slip-ratio units.</summary>
        private const float SlopeProbe = 1e-3f;
        /// <summary>Share of bump travel at the top of the stroke where the bump stop works.</summary>
        private const float BumpStopZone = 0.25f;
        /// <summary>Half-angle of the tyre arc sampled for ground contact, degrees.</summary>
        private const float ProfileHalfAngle = 40f;
        /// <summary>How fast an airborne wheel drops to full droop, m/s (visual and state).</summary>
        private const float DroopSpeed = 3f;
        /// <summary>Spin inertia = this × wheel mass × radius² (tyre mass sits near the rim).</summary>
        private const float WheelInertiaFactor = 0.7f;

        private Vector3 localTop;
        private float travel;
        private float bumpTravel;
        private float springRate;
        private float springFreeCompression;
        private float bumpDamping;
        private float reboundDamping;
        private float damperKnee;
        private float highSpeedDamping;
        private float bumpStopRate;
        private float baseInertia;
        private Collider lastCollider;
        private DrivingSurface lastSurface;
        private Rigidbody groundBody;

        public WheelPosition Position { get; }
        public bool IsFront { get; }
        public bool IsLeft { get; }
        public float Radius { get; }
        public Transform Pivot { get; }
        public Transform Spin { get; }
        /// <summary>Wheel centre at ride height, in vehicle space.</summary>
        public Vector3 LocalRideCentre { get; }

        public float StaticLoad { get; private set; }
        /// <summary>Sprung mass carried by this corner at rest, kg.</summary>
        public float CornerMass { get; private set; }
        public float SpringRate => springRate;
        public float Travel => travel;

        public bool IsGrounded { get; private set; }
        /// <summary>Compression from full droop, m (0 = hanging, <see cref="Travel"/> = full bump).</summary>
        public float Compression { get; private set; }
        public float CompressionRatio => travel > 0f ? Mathf.Clamp01(Compression / travel) : 0f;
        public float CompressionVelocity { get; private set; }
        /// <summary>Distance from the top of the stroke down to the wheel centre, m (drives the visual).</summary>
        public float CentreOffset { get; private set; }
        public bool OnBumpStop { get; private set; }
        /// <summary>Normal load on the tyre, N.</summary>
        public float Load { get; private set; }
        public Vector3 ContactPoint { get; private set; }
        public Vector3 ContactNormal { get; private set; }
        public Collider GroundCollider => IsGrounded ? lastCollider : null;
        public float SurfaceGrip { get; private set; } = 1f;
        public float SurfaceRollingScale { get; private set; } = 1f;

        /// <summary>Road-wheel steer angle, degrees.</summary>
        public float SteerAngle { get; internal set; }
        /// <summary>Spin rate, rad/s (+ = rolling forward).</summary>
        public float AngularVelocity { get; private set; }
        public float Rpm => AngularVelocity * VehicleUnits.RadPerSecToRpm;
        public float SlipRatio { get; private set; }
        /// <summary>Tyre surface speed minus ground speed along the wheel, m/s (+ = wheelspin, − = locking).</summary>
        public float SlipSpeed { get; private set; }
        /// <summary>Slip angle, degrees (+ = sliding right).</summary>
        public float SlipAngle { get; private set; }
        /// <summary>Combined slip normalised to the tyre's peak (1 = at peak grip, above = sliding).</summary>
        public float CombinedSlip { get; private set; }
        /// <summary>Tyre force in the contact frame: x = longitudinal, y = lateral (+ right), N.</summary>
        public Vector2 TyreForce { get; private set; }
        /// <summary>Drive torque applied this step, Nm.</summary>
        public float DriveTorque { get; private set; }
        /// <summary>Brake torque applied this step after anti-lock, Nm.</summary>
        public float BrakeTorque { get; private set; }
        /// <summary>True when the wheel is stopped and held by its brake (static friction) this step.</summary>
        public bool IsHeld { get; private set; }
        /// <summary>Total spin inertia this step including reflected engine inertia, kg·m².</summary>
        public float EffectiveInertia { get; private set; }

        public VehicleWheel(in WheelRig rig, Vector3 localRideCentre)
        {
            Position = rig.Position;
            IsFront = rig.IsFront;
            IsLeft = rig.IsLeft;
            Radius = rig.Radius;
            Pivot = rig.Pivot;
            Spin = rig.Spin;
            LocalRideCentre = localRideCentre;
        }

        /// <param name="staticLoad">Normal load at rest, N.</param>
        public void Configure(in AxleSettings axle, float staticLoad)
        {
            StaticLoad = staticLoad;
            CornerMass = staticLoad / VehicleUnits.Gravity;
            bumpTravel = axle.bumpTravel;
            travel = axle.bumpTravel + axle.droopTravel;
            localTop = LocalRideCentre + Vector3.up * axle.bumpTravel;

            float omega = 2f * Mathf.PI * axle.springFrequency;
            springRate = CornerMass * omega * omega;
            float critical = 2f * Mathf.Sqrt(springRate * CornerMass);
            bumpDamping = axle.bumpDamping * critical;
            reboundDamping = axle.reboundDamping * critical;
            damperKnee = axle.damperKneeSpeed > 0f ? axle.damperKneeSpeed : float.MaxValue;
            highSpeedDamping = axle.highSpeedDamping > 0f ? axle.highSpeedDamping : 1f;
            // When the suspension bottoms, the stop and the tyre carry the load in series.
            float stop = springRate * axle.bumpStopScale;
            float tyreRate = axle.tyre.verticalStiffness;
            bumpStopRate = tyreRate > 0f ? stop * tyreRate / (stop + tyreRate) : stop;
            // Preloaded so the static load holds the wheel exactly at the model's ride height.
            springFreeCompression = axle.droopTravel - staticLoad / springRate;
            baseInertia = WheelInertiaFactor * axle.wheelMass * Radius * Radius;
            Reset();
        }

        public void Reset()
        {
            IsGrounded = false;
            Compression = travel - bumpTravel;
            CentreOffset = bumpTravel;
            CompressionVelocity = Load = 0f;
            AngularVelocity = SlipRatio = SlipAngle = CombinedSlip = 0f;
            TyreForce = Vector2.zero;
            OnBumpStop = false;
            groundBody = null;
        }

        /// <summary>Teleport support: keep spin consistent with a new ground speed.</summary>
        public void SetAngularVelocity(float value) => AngularVelocity = value;

        /// <summary>
        /// Finds the ground under the tyre. Rays are cast along the suspension axis at points spread over the tyre's
        /// rolling profile (±<see cref="ProfileHalfAngle"/>); the wheel centre sits where the tyre circle first touches
        /// any of them, so a sharp edge is rolled onto instead of stepping the wheel up instantly.
        /// </summary>
        internal void UpdateContact(in PhysicsScene scene, in Pose pose, Vector3 rollingDirection, Rigidbody rb, int groundMask, int samples, float dt)
        {
            Vector3 up = pose.up;
            Vector3 origin = pose.position + pose.rotation * localTop;
            Vector3 along = Vector3.ProjectOnPlane(rollingDirection, up).normalized;
            bool wasGrounded = IsGrounded;

            bool found = false;
            RaycastHit hit = default;
            float centreDistance = float.MaxValue;
            samples = Mathf.Max(1, samples | 1);
            for (int i = 0; i < samples; i++)
            {
                float angle = samples == 1 ? 0f : Mathf.Lerp(-ProfileHalfAngle, ProfileHalfAngle, i / (samples - 1f)) * Mathf.Deg2Rad;
                float offset = Radius * Mathf.Sin(angle);
                float drop = Radius * Mathf.Cos(angle);
                if (!scene.Raycast(origin + along * offset, -up, out var h, travel + drop, groundMask, QueryTriggerInteraction.Ignore)) continue;
                float d = h.distance - drop;
                if (d < centreDistance) { centreDistance = d; hit = h; found = true; }
            }

            if (found)
            {
                float compression = travel - centreDistance;
                CompressionVelocity = wasGrounded
                    ? (compression - Compression) / dt
                    : -Vector3.Dot(rb.GetPointVelocity(origin), up);
                Compression = compression;
                CentreOffset = Mathf.Clamp(travel - compression, 0f, travel);
                ContactPoint = hit.point;
                ContactNormal = hit.normal;
                groundBody = hit.rigidbody;
                if (hit.collider != lastCollider)
                {
                    lastCollider = hit.collider;
                    lastSurface = hit.collider.GetComponentInParent<DrivingSurface>();
                }
                SurfaceGrip = lastSurface != null ? lastSurface.Grip : 1f;
                SurfaceRollingScale = lastSurface != null ? lastSurface.RollingResistanceScale : 1f;
                IsGrounded = true;
            }
            else
            {
                IsGrounded = false;
                Compression = 0f;
                CompressionVelocity = 0f;
                CentreOffset = Mathf.MoveTowards(CentreOffset, travel, DroopSpeed * dt);
                groundBody = null;
                OnBumpStop = false;
            }
        }

        /// <summary>Spring + damper + bump-stop force along the suspension, N (before anti-roll). 0 when airborne.</summary>
        internal float SuspensionForce()
        {
            if (!IsGrounded) return 0f;
            float spring = springRate * (Mathf.Min(Compression, travel) - springFreeCompression);
            float damper = DamperForce(CompressionVelocity, CompressionVelocity > 0f ? bumpDamping : reboundDamping);
            float stopStart = travel - bumpTravel * BumpStopZone;
            float intoStop = Compression - stopStart;
            OnBumpStop = intoStop > 0f;
            float stop = OnBumpStop ? bumpStopRate * intoStop : 0f;
            return spring + damper + stop;
        }

        /// <summary>Digressive damper: linear up to the knee speed, then the high-speed share of that rate.</summary>
        private float DamperForce(float speed, float rate)
        {
            float magnitude = Mathf.Abs(speed);
            if (magnitude <= damperKnee) return rate * speed;
            return Mathf.Sign(speed) * rate * (damperKnee + highSpeedDamping * (magnitude - damperKnee));
        }

        internal void ApplyLoad(Rigidbody rb, float load)
        {
            Load = IsGrounded ? Mathf.Max(0f, load) : 0f;
            if (Load <= 0f) return;
            Vector3 force = ContactNormal * Load;
            rb.AddForceAtPosition(force, ContactPoint);
            if (groundBody != null && !groundBody.isKinematic) groundBody.AddForceAtPosition(-force, ContactPoint);
        }

        /// <summary>
        /// Integrates wheel spin and applies tyre forces. Spin is solved implicitly against the tyre's local stiffness so
        /// it stays stable at any speed; a stopped wheel held by its brake uses static friction instead of slip.
        /// </summary>
        /// <param name="holdMass">Mass this wheel must stop if it is held by its brake (vehicle mass shared among held wheels), kg.</param>
        internal void UpdateTyre(Rigidbody rb, Vector3 wheelForward, in TyreSettings tyre, float driveTorque,
                                 float serviceBrakeTorque, float handbrakeTorque, float extraInertia,
                                 bool antiLock, float antiLockTarget, float holdMass, float dt)
        {
            IsHeld = false;
            float inertia = baseInertia + extraInertia;
            EffectiveInertia = inertia;
            DriveTorque = driveTorque;

            if (!IsGrounded)
            {
                BrakeTorque = serviceBrakeTorque + handbrakeTorque;
                AngularVelocity += driveTorque / inertia * dt;
                AngularVelocity = Mathf.MoveTowards(AngularVelocity, 0f, BrakeTorque / inertia * dt);
                SlipRatio = SlipSpeed = SlipAngle = CombinedSlip = 0f;
                TyreForce = Vector2.zero;
                return;
            }

            Vector3 normal = ContactNormal;
            Vector3 forward = Vector3.ProjectOnPlane(wheelForward, normal).normalized;
            Vector3 side = Vector3.Cross(normal, forward);
            Vector3 velocity = rb.GetPointVelocity(ContactPoint);
            if (groundBody != null) velocity -= groundBody.GetPointVelocity(ContactPoint);
            float vLong = Vector3.Dot(velocity, forward);
            float vLat = Vector3.Dot(velocity, side);
            float reference = Mathf.Max(Mathf.Abs(vLong), SlipSpeedFloor);
            float slipTan = vLat / reference;
            float grip = SurfaceGrip;

            if (antiLock && serviceBrakeTorque > 0f)
            {
                float available = TyreModel.AvailableLongitudinal(tyre, TyreForce.y, Load, StaticLoad, grip);
                // Engine braking / regen (negative drive torque) uses up part of the tyre's braking capacity.
                serviceBrakeTorque = Mathf.Clamp(available * antiLockTarget * Radius + driveTorque, 0f, serviceBrakeTorque);
            }
            float rollingTorque = tyre.rollingResistance * SurfaceRollingScale * Load * Radius;
            float holdTorque = serviceBrakeTorque + handbrakeTorque + rollingTorque;
            BrakeTorque = serviceBrakeTorque + handbrakeTorque;

            float slip = (AngularVelocity * Radius - vLong) / reference;
            Vector2 f0 = TyreModel.Force(tyre, slip, slipTan, Load, StaticLoad, grip);
            float fx;

            float netTorque = driveTorque - f0.x * Radius;
            if (Mathf.Abs(AngularVelocity) < StoppedSpinRate && Mathf.Abs(netTorque) <= holdTorque)
            {
                // Stopped and held by the brakes: static friction. Cancel the sliding speed this step, limited by what
                // the brakes can hold and what the tyre can grip.
                AngularVelocity = 0f;
                IsHeld = true;
                // Stop the sliding and balance gravity along the tyre within this step (no creep on slopes).
                float stopForce = -(vLong / dt + Vector3.Dot(Physics.gravity, forward)) * Mathf.Max(CornerMass, holdMass);
                float peak = TyreModel.AvailableLongitudinal(tyre, 0f, Load, StaticLoad, grip);
                float lo = Mathf.Max(-peak, (driveTorque - holdTorque) / Radius);
                float hi = Mathf.Min(peak, (driveTorque + holdTorque) / Radius);
                fx = Mathf.Clamp(stopForce, Mathf.Min(lo, hi), Mathf.Max(lo, hi));
            }
            else
            {
                float direction = Mathf.Abs(AngularVelocity) >= StoppedSpinRate ? Mathf.Sign(AngularVelocity) : Mathf.Sign(netTorque);
                // Stiffness for the implicit solve: the larger of the local slope and the secant through zero slip.
                // Past the peak the local slope is ~0; the secant keeps one step from overshooting through zero slip.
                float fProbe = TyreModel.Force(tyre, slip + SlopeProbe, slipTan, Load, StaticLoad, grip).x;
                float local = (fProbe - f0.x) / SlopeProbe;
                float secant = Mathf.Abs(slip) > SlopeProbe ? f0.x / slip : local;
                float stiffness = Mathf.Max(0f, Mathf.Max(local, secant)) / reference;          // N per m/s of slip speed
                float compliance = Radius * Radius / inertia + 1f / CornerMass;                 // slip speed per N·s
                float torque = driveTorque - holdTorque * direction;
                fx = (f0.x + stiffness * dt * Radius * torque / inertia) / (1f + stiffness * dt * compliance);
            }

            // Lateral: at walking pace the tyre does not slide sideways (static friction, balancing gravity on a
            // camber); otherwise never apply more than cancels the sliding speed this step.
            float fy = f0.y;
            if (Mathf.Abs(vLong) < StaticLateralSpeed && Mathf.Abs(vLat) < StaticLateralSpeed)
            {
                float peakLateral = tyre.lateralGrip * TyreModel.LoadFactor(tyre, Load, StaticLoad) * grip * Load;
                fy = Mathf.Clamp(-(vLat / dt + Vector3.Dot(Physics.gravity, side)) * CornerMass, -peakLateral, peakLateral);
            }
            else
            {
                float lateralStop = Mathf.Abs(vLat) * CornerMass / dt;
                if (Mathf.Abs(fy) > lateralStop) fy = Mathf.Sign(fy) * lateralStop;
            }

            var force = TyreModel.ClampToEllipse(tyre, new Vector2(fx, fy), Load, StaticLoad, grip);
            if (AngularVelocity != 0f || Mathf.Abs(netTorque) > holdTorque)
            {
                float before = AngularVelocity;
                float direction = Mathf.Abs(before) >= StoppedSpinRate ? Mathf.Sign(before) : Mathf.Sign(driveTorque - force.x * Radius);
                AngularVelocity = before + (driveTorque - force.x * Radius - holdTorque * direction) / inertia * dt;
                if (direction != 0f && Mathf.Sign(AngularVelocity) != direction) AngularVelocity = 0f;
            }

            TyreForce = force;
            SlipRatio = slip;
            SlipSpeed = AngularVelocity * Radius - vLong;
            SlipAngle = Mathf.Atan(slipTan) * Mathf.Rad2Deg;
            CombinedSlip = TyreModel.CombinedSlip(tyre, slip, slipTan);

            Vector3 world = forward * force.x + side * force.y;
            rb.AddForceAtPosition(world, ContactPoint);
            if (groundBody != null && !groundBody.isKinematic) groundBody.AddForceAtPosition(-world, ContactPoint);
        }

        /// <summary>Differential coupling: moves spin toward <paramref name="target"/> by <paramref name="fraction"/>.</summary>
        internal void BlendAngularVelocity(float target, float fraction)
        {
            AngularVelocity = Mathf.Lerp(AngularVelocity, target, fraction);
        }
    }
}
