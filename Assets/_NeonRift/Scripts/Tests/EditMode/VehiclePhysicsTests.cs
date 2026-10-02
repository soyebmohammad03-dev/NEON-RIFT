using System.Collections.Generic;
using System.Linq;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace NeonRift.Tests
{
    /// <summary>
    /// Every catalog vehicle driven on the deterministic bench (isolated physics scene, 100 Hz). Bands are wide enough
    /// for tuning but catch broken physics: instability, jitter, NaNs, spins from keyboard input, unrealistic figures.
    /// </summary>
    public class VehiclePhysicsTests
    {
        private static IEnumerable<TestCaseData> Vehicles() =>
            VehiclePerformanceProbe.CatalogVehicles().Select(v => new TestCaseData(v).SetName($"{{m}}({v.Id})"));

        [TestCaseSource(nameof(Vehicles))]
        public void SpawnsGroundedAtRideHeight_WithoutJitter(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            var r = VehiclePerformanceProbe.Settle(b);
            Assert.That(r.GroundedWheels, Is.EqualTo(4));
            Assert.That(r.ResidualSpeed, Is.LessThan(0.005f), "body still moving at rest (m/s)");
            Assert.That(r.MaxWheelSpin, Is.LessThan(0.05f), "wheels creeping at rest (rad/s)");
            Assert.That(r.MaxRideHeightError, Is.LessThan(0.005f), "static ride height differs from the model (m)");
        }

        [TestCaseSource(nameof(Vehicles))]
        public void Accelerates_WithinRealisticBand(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            var r = VehiclePerformanceProbe.Launch(b);
            Assert.That(r.ZeroTo100, Is.InRange(2.5f, 6f), "0-100 km/h (s)");
            Assert.That(r.ZeroTo200, Is.InRange(6f, 16f), "0-200 km/h (s)");
        }

        [TestCaseSource(nameof(Vehicles))]
        public void ReachesPredictedTopSpeed(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            float top = VehiclePerformanceProbe.TopSpeed(b);
            float predicted = b.Vehicle.EstimatedTopSpeed * VehicleUnits.MsToKph;
            Assert.That(top, Is.EqualTo(predicted).Within(predicted * 0.06f));
        }

        [TestCaseSource(nameof(Vehicles))]
        public void BrakesStraight_FromHighSpeed(VehicleDefinition v)
        {
            using (var b = VehicleTestBench.Create(v))
            {
                var r = VehiclePerformanceProbe.Brake(b, 100f);
                Assert.That(r.Distance, Is.InRange(26f, 45f), "100-0 km/h (m)");
                Assert.That(r.HeadingChange, Is.LessThan(2f), "pulled off line (deg)");
            }
            using (var b = VehicleTestBench.Create(v))
            {
                var r = VehiclePerformanceProbe.Brake(b, 200f);
                Assert.That(r.HeadingChange, Is.LessThan(3f), "unstable braking from 200 km/h (deg)");
            }
        }

        [TestCaseSource(nameof(Vehicles))]
        public void CorneringGrip_MatchesTyres(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            var r = VehiclePerformanceProbe.RampSteer(b, 60f);
            Assert.That(r.Spun, Is.False);
            Assert.That(r.MaxLateralG, Is.InRange(0.8f, 1.6f));
        }

        [TestCaseSource(nameof(Vehicles))]
        public void FullKeyboardSteerAtSpeed_SlidesButDoesNotSpin(VehicleDefinition v)
        {
            foreach (float kph in new[] { 80f, 160f, 220f })
            {
                using var b = VehicleTestBench.Create(v);
                var r = VehiclePerformanceProbe.StepSteer(b, kph);
                Assert.That(r.Spun, Is.False, $"{kph} km/h");
                Assert.That(r.PeakSideslip, Is.LessThan(10f), $"{kph} km/h body slip (deg)");
                Assert.That(r.ResidualYawRate, Is.LessThan(2f), $"{kph} km/h still rotating after release (deg/s)");
            }
        }

        [TestCaseSource(nameof(Vehicles))]
        public void SpeedBumps_DoNotLaunchTheBody(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            var r = VehiclePerformanceProbe.Bumps(b, 40f);
            Assert.That(r.BodyContact, Is.False, "body hit the bump");
            Assert.That(r.MaxPitch, Is.LessThan(6f), "pitch (deg)");
        }

        [TestCaseSource(nameof(Vehicles))]
        public void WallImpact_StaysStable_AndCanRecover(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            // Collision events (OnCollisionEnter) only fire in Play Mode; they are checked on the test track run.
            b.AddBox("Wall", new Vector3(0f, 1f, 80f), new Vector3(30f, 2f, 1f), Quaternion.identity, "Environment");
            VehiclePerformanceProbe.Settle(b, 1.5f);
            b.AccelerateTo(60f);
            b.Run(10f, () => b.Body.position.z > 78f);
            b.SetInput();
            b.Run(3f);
            Assert.That(b.Body.position.z, Is.LessThan(80f), "passed through the wall");
            Assert.That(Vector3.Dot(b.Body.transform.up, Vector3.up), Is.GreaterThan(0.7f), "not upright after impact");
            Assert.That(b.Body.linearVelocity.magnitude, Is.LessThan(3f));

            // Reverse away from the wall using the brake pedal (reverse selection at standstill).
            float z = b.Body.position.z;
            b.SetInput(brake: 1f);
            b.Run(4f);
            Assert.That(b.Body.position.z, Is.LessThan(z - 2f), "could not reverse away");

            // Recovery puts it back on its wheels.
            b.Vehicle.Recover();
            b.SetInput();
            b.Run(2f);
            Assert.That(b.Vehicle.Telemetry.AllWheelsGrounded, Is.True);
        }

        [TestCaseSource(nameof(Vehicles))]
        public void Slope_HeldByHandbrake_RollsWithout(VehicleDefinition v)
        {
            using var b = VehicleTestBench.Create(v);
            var slope = Quaternion.Euler(-12f, 0f, 0f);
            var box = b.AddBox("Slope", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 200f), slope, "Drivable");
            b.Vehicle.Teleport(box.transform.position + slope * new Vector3(0f, 0.5f + 0.3f, 30f), slope);
            b.SetInput(handbrake: true);
            b.Run(2f);
            Vector3 held = b.Body.position;
            b.Run(2f);
            Assert.That(Vector3.Distance(held, b.Body.position), Is.LessThan(0.02f), "creeps while parked on a 12° slope (m)");
            b.SetInput();
            b.Run(2f);
            Assert.That(Vector3.Distance(held, b.Body.position), Is.GreaterThan(0.5f), "should roll without brakes");
        }

        [TestCaseSource(nameof(Vehicles))]
        public void CompletesValidationRouteLap_OnLine_AndUpright(VehicleDefinition v)
        {
            var r = VehiclePerformanceProbe.RouteLap(v, 6f);
            Assert.That(r.Completed, Is.True, $"left the route by {r.MaxOffset:0.0} m in {r.WorstSection}");
            Assert.That(r.MaxOffset, Is.LessThan(4f), $"worst in {r.WorstSection} (m)");
            Assert.That(r.MinUp, Is.GreaterThan(0.9f), "body tipped");
            Assert.That(r.LapTime, Is.InRange(50f, 90f), "lap time (s)");
        }

        [TestCaseSource(nameof(Vehicles))]
        public void SpawnPoint_AppliesTheDefinitionsProfile(VehicleDefinition v)
        {
            var go = new GameObject("SpawnPoint");
            var point = go.AddComponent<VehicleSpawnPoint>();
            VehicleController spawned = null;
            try
            {
                spawned = point.Spawn(v);
                Assert.That(spawned, Is.Not.Null);
                Assert.That(spawned.Profile, Is.SameAs(v.PhysicsProfile));
                Assert.That(spawned.Body.mass, Is.EqualTo(v.PhysicsProfile.Chassis.mass));
                Assert.That(spawned.Body.isKinematic, Is.False);
                Assert.That(spawned.Wheels.Count, Is.EqualTo(4));
                Assert.That(spawned.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Vehicle")));
            }
            finally
            {
                if (spawned != null) Object.DestroyImmediate(spawned.gameObject);
                Object.DestroyImmediate(go);
            }
        }
    }
}
