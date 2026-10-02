using System.Linq;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace NeonRift.Tests
{
    /// <summary>Pure-model tests: tyre, steering, gearbox, drivetrain and profile data. No physics scene.</summary>
    public class VehicleModelTests
    {
        private static readonly TyreSettings Tyre = TyreSettings.Default;
        private const float Load = 4000f;

        private static float LateralTan(float degrees) => Mathf.Tan(degrees * Mathf.Deg2Rad);

        [Test]
        public void Tyre_NoSlip_NoForce()
        {
            Assert.That(TyreModel.Force(Tyre, 0f, 0f, Load, Load, 1f).magnitude, Is.LessThan(1e-3f));
        }

        [Test]
        public void Tyre_ForceOpposesSlip()
        {
            Assert.That(TyreModel.Force(Tyre, 0.05f, 0f, Load, Load, 1f).x, Is.GreaterThan(0f), "wheelspin pushes forward");
            Assert.That(TyreModel.Force(Tyre, -0.05f, 0f, Load, Load, 1f).x, Is.LessThan(0f), "locking pulls back");
            Assert.That(TyreModel.Force(Tyre, 0f, LateralTan(3f), Load, Load, 1f).y, Is.LessThan(0f), "sliding right pushes left");
        }

        [Test]
        public void Tyre_PeaksAtPeakSlip_ThenFallsTowardSliding()
        {
            float half = TyreModel.Force(Tyre, Tyre.peakSlipRatio * 0.5f, 0f, Load, Load, 1f).x;
            float peak = TyreModel.Force(Tyre, Tyre.peakSlipRatio, 0f, Load, Load, 1f).x;
            float slide = TyreModel.Force(Tyre, 1f, 0f, Load, Load, 1f).x;
            Assert.That(peak, Is.GreaterThan(half));
            Assert.That(peak, Is.GreaterThan(slide));
            Assert.That(peak, Is.EqualTo(Tyre.longitudinalGrip * Load).Within(1f));
            Assert.That(slide / peak, Is.EqualTo(Tyre.slidingGripRatio).Within(0.05f));
        }

        [Test]
        public void Tyre_CombinedSlip_TradesLateralForLongitudinal()
        {
            float pure = Mathf.Abs(TyreModel.Force(Tyre, 0f, LateralTan(Tyre.peakSlipAngle), Load, Load, 1f).y);
            float combined = Mathf.Abs(TyreModel.Force(Tyre, Tyre.peakSlipRatio, LateralTan(Tyre.peakSlipAngle), Load, Load, 1f).y);
            Assert.That(combined, Is.LessThan(pure * 0.8f));
        }

        [Test]
        public void Tyre_NeverExceedsFrictionEllipse()
        {
            for (float sr = -1f; sr <= 1f; sr += 0.05f)
            for (float deg = -60f; deg <= 60f; deg += 3f)
            {
                var f = TyreModel.Force(Tyre, sr, LateralTan(deg), Load, Load, 1f);
                float n = Mathf.Pow(f.x / (Tyre.longitudinalGrip * Load), 2f) + Mathf.Pow(f.y / (Tyre.lateralGrip * Load), 2f);
                Assert.That(n, Is.LessThanOrEqualTo(1.0001f), $"slip {sr:0.00}, {deg}°");
            }
        }

        [Test]
        public void Tyre_LoadSensitivity_CostsGripUnderExtraLoad()
        {
            float nominal = TyreModel.Force(Tyre, Tyre.peakSlipRatio, 0f, Load, Load, 1f).x / Load;
            float heavy = TyreModel.Force(Tyre, Tyre.peakSlipRatio, 0f, Load * 1.5f, Load, 1f).x / (Load * 1.5f);
            Assert.That(heavy, Is.LessThan(nominal));
        }

        [Test]
        public void Tyre_SurfaceGripScalesForce()
        {
            float asphalt = TyreModel.Force(Tyre, 0f, LateralTan(Tyre.peakSlipAngle), Load, Load, 1f).y;
            float gravel = TyreModel.Force(Tyre, 0f, LateralTan(Tyre.peakSlipAngle), Load, Load, 0.45f).y;
            Assert.That(gravel / asphalt, Is.EqualTo(0.45f).Within(1e-3f));
        }

        [Test]
        public void Steering_LockFallsWithSpeed_AndIsFullAtParkingSpeed()
        {
            var s = new SteeringSystem();
            var settings = SteeringSettings.Default;
            s.Configure(settings, 2.7f, 1.6f, 6f, 10f);
            Assert.That(s.MaxAngleAt(0f), Is.EqualTo(settings.maxSteerAngle).Within(1e-3f));
            float slow = s.MaxAngleAt(10f), fast = s.MaxAngleAt(60f);
            Assert.That(fast, Is.LessThan(slow));
            Assert.That(fast, Is.LessThan(10f));
        }

        [Test]
        public void Steering_IsRateLimited_AndAckermannTurnsInnerWheelMore()
        {
            var s = new SteeringSystem();
            var settings = SteeringSettings.Default;
            s.Configure(settings, 2.7f, 1.6f, 6f, 10f);
            s.Update(1f, 0f, 0.01f);
            Assert.That(s.Input, Is.EqualTo(settings.steerRate * 0.01f).Within(1e-4f), "one step of a digital key press");
            for (int i = 0; i < 100; i++) s.Update(1f, 0f, 0.01f);
            Assert.That(s.Input, Is.EqualTo(1f));
            Assert.That(s.RightAngle, Is.GreaterThan(s.LeftAngle), "right turn: right wheel is inside");
        }

        private static TransmissionSettings SixSpeed()
        {
            var t = TransmissionSettings.Default;
            t.shiftTime = 0.1f;
            return t;
        }

        [Test]
        public void Gearbox_ShiftsUpAboveShiftRpm_WithHysteresis()
        {
            var g = new Gearbox();
            var t = SixSpeed();
            g.Configure(t);
            g.Tick(1f);
            g.UpdateAutomatic(t.shiftUpRpm + 100f, 1f);
            Assert.That(g.Gear, Is.EqualTo(2));
            Assert.That(g.IsShifting, Is.True);
            g.Tick(0.05f);
            g.UpdateAutomatic(t.shiftUpRpm + 100f, 1f);
            Assert.That(g.Gear, Is.EqualTo(2), "no second shift while the first is running");
        }

        [Test]
        public void Gearbox_ShiftsDownWhenLugging_AndReverseRatioIsNegative()
        {
            var g = new Gearbox();
            var t = SixSpeed();
            g.Configure(t);
            g.Shift(4);
            g.Tick(1f);
            g.UpdateAutomatic(t.shiftDownRpm - 200f, 0f);
            Assert.That(g.Gear, Is.EqualTo(3));
            Assert.That(g.RatioFor(-1), Is.LessThan(0f));
            Assert.That(g.RatioFor(0), Is.EqualTo(0f));
        }

        [Test]
        public void Drivetrain_HoldingBrakeAtStandstill_SelectsReverse_ThrottleSelectsForward()
        {
            var d = new Drivetrain();
            d.Configure(EngineSettings.Default, TransmissionSettings.Default);
            var brake = new DrivingInput { Brake = 1f };
            for (int i = 0; i < 40; i++) d.Update(brake, 0f, 0f, 1f, false, 0.01f);
            Assert.That(d.Gearbox.Gear, Is.EqualTo(-1));
            Assert.That(d.Throttle, Is.EqualTo(1f), "in reverse the brake pedal drives");
            var go = new DrivingInput { Throttle = 1f };
            for (int i = 0; i < 40; i++) d.Update(go, 0f, 0f, 1f, false, 0.01f);
            Assert.That(d.Gearbox.Gear, Is.EqualTo(1));
        }

        [Test]
        public void Drivetrain_LaunchClutchSlips_ThenLocksToWheels()
        {
            var d = new Drivetrain();
            var e = EngineSettings.Default;
            d.Configure(e, TransmissionSettings.Default);
            var go = new DrivingInput { Throttle = 1f };
            for (int i = 0; i < 50; i++) d.Update(go, 0f, 0f, 1f, false, 0.01f);
            Assert.That(d.IsClutchLocked, Is.False);
            Assert.That(d.EngineRpm, Is.EqualTo(e.launchRpm).Within(50f), "slipping clutch holds launch rpm");
            Assert.That(d.DriveTorque, Is.GreaterThan(0f));
            float wheel = 3500f / (d.TotalRatio * VehicleUnits.RadPerSecToRpm);
            d.Update(go, 10f, wheel, 1f, false, 0.01f);
            Assert.That(d.IsClutchLocked, Is.True);
            Assert.That(d.EngineRpm, Is.EqualTo(3500f).Within(1f));
        }

        [Test]
        public void Drivetrain_HandbrakeOpensClutch()
        {
            var d = new Drivetrain();
            d.Configure(EngineSettings.Default, TransmissionSettings.Default);
            var input = new DrivingInput { Throttle = 1f, Handbrake = true };
            for (int i = 0; i < 20; i++) d.Update(input, 10f, 30f, 1f, true, 0.01f);
            Assert.That(d.Clutch, Is.EqualTo(0f));
            Assert.That(d.DriveTorque, Is.EqualTo(0f));
        }

        [Test]
        public void CatalogProfiles_AreValid_AndPredictPlausibleTopSpeeds()
        {
            var vehicles = VehiclePerformanceProbe.CatalogVehicles().ToList();
            Assert.That(vehicles, Is.Not.Empty);
            foreach (var v in vehicles)
            {
                Assert.That(v.PhysicsProfile, Is.Not.Null, v.Id);
                Assert.That(v.PhysicsProfile.Validate(), Is.Empty, v.Id);
                float top = v.PhysicsProfile.EstimateTopSpeed(0.35f) * VehicleUnits.MsToKph;
                Assert.That(top, Is.InRange(200f, 400f), v.Id);
                float kw = v.PhysicsProfile.Engine.PeakPowerKw(out _);
                Assert.That(kw, Is.InRange(250f, 800f), v.Id);
            }
        }

        [Test]
        public void Catalog_ExcludesAmgGt3_AndFlagsNonCommercialLicences()
        {
            var vehicles = VehiclePerformanceProbe.CatalogVehicles().ToList();
            Assert.That(vehicles.Select(v => v.Id), Has.No.Member("mercedes_amg_gt3"));
            var terzo = vehicles.FirstOrDefault(v => v.Id == "lamborghini_terzo_millennio");
            Assert.That(terzo, Is.Not.Null);
            Assert.That(terzo.License.CommercialUseAllowed, Is.False);
            Assert.That(NeonRift.EditorTools.ProjectValidator.Warnings(), Has.Some.Contains("lamborghini_terzo_millennio"));
        }
    }
}
