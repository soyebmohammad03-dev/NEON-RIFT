using System.Collections.Generic;
using System.Linq;
using System.Text;
using NeonRift.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Vehicles
{
    /// <summary>
    /// Standard manoeuvres run on <see cref="VehicleTestBench"/>: settle, launch, top speed, braking, ramp steer,
    /// step steer and bumps. Results feed the automated tests and the "Measure Vehicle Performance" report.
    /// </summary>
    public static class VehiclePerformanceProbe
    {
        public struct SettleResult
        {
            public float ResidualSpeed, MaxRideHeightError, MaxWheelSpin;
            public int GroundedWheels;
        }

        public struct LaunchResult
        {
            public float ZeroTo100, ZeroTo200, QuarterMileTime, QuarterMileKph, MaxSlip;
            public int FinalGear;
        }

        public struct BrakeResult
        {
            public float Distance, MeanDecelerationG, MaxSlip, MaxYawRate, HeadingChange;
        }

        public struct CorneringResult
        {
            public float MaxLateralG, SpeedAtMax, MaxSideslip;
            public bool Spun;
        }

        public struct StepSteerResult
        {
            public float PeakYawRate, PeakSideslip, PeakLateralG, ResidualYawRate, HeadingChange;
            public bool Spun;
        }

        public struct BumpResult
        {
            public float MaxCompression, MaxPitch, SettleTime;
            public int BumpStopSteps, AirborneWheelSteps;
            public bool BodyContact;
        }

        public static SettleResult Settle(VehicleTestBench bench, float seconds = 3f)
        {
            bench.SetInput();
            bench.Run(seconds - 1f);
            float residual = 0f, spin = 0f;
            bench.Run(1f, null, () =>
            {
                residual = Mathf.Max(residual, bench.Body.linearVelocity.magnitude);
                foreach (var w in bench.Vehicle.Wheels) spin = Mathf.Max(spin, Mathf.Abs(w.AngularVelocity));
            });
            var r = new SettleResult { ResidualSpeed = residual, MaxWheelSpin = spin };
            foreach (var w in bench.Vehicle.Wheels)
            {
                if (w.IsGrounded) r.GroundedWheels++;
                float droop = w.Travel - bench.Vehicle.Profile.Axle(w.IsFront).bumpTravel;
                r.MaxRideHeightError = Mathf.Max(r.MaxRideHeightError, Mathf.Abs(w.Compression - droop));
            }
            return r;
        }

        public static LaunchResult Launch(VehicleTestBench bench)
        {
            Settle(bench, 1.5f);
            var r = new LaunchResult { ZeroTo100 = float.NaN, ZeroTo200 = float.NaN, QuarterMileTime = float.NaN };
            float start = bench.Time;
            Vector3 origin = bench.Body.position;
            bench.SetInput(throttle: 1f);
            bench.Run(40f, () => !float.IsNaN(r.QuarterMileTime) && bench.SpeedKph >= 200f, () =>
            {
                float t = bench.Time - start;
                if (float.IsNaN(r.ZeroTo100) && bench.SpeedKph >= 100f) r.ZeroTo100 = t;
                if (float.IsNaN(r.ZeroTo200) && bench.SpeedKph >= 200f) r.ZeroTo200 = t;
                if (float.IsNaN(r.QuarterMileTime) && Vector3.Distance(origin, bench.Body.position) >= 402.3f)
                {
                    r.QuarterMileTime = t;
                    r.QuarterMileKph = bench.SpeedKph;
                }
                r.MaxSlip = Mathf.Max(r.MaxSlip, bench.Vehicle.Telemetry.MaxSlip);
            });
            r.FinalGear = bench.Vehicle.Telemetry.Gear;
            return r;
        }

        /// <summary>Full throttle until acceleration dies away. Returns top speed in km/h.</summary>
        public static float TopSpeed(VehicleTestBench bench, float maxSeconds = 150f)
        {
            bench.SetInput(throttle: 1f);
            float last = 0f, best = 0f;
            for (float t = 0f; t < maxSeconds; t += 5f)
            {
                bench.Recentre();
                bench.Run(5f);
                best = Mathf.Max(best, bench.SpeedKph);
                if (bench.SpeedKph - last < 0.5f && t > 20f) break;
                last = bench.SpeedKph;
            }
            return best;
        }

        public static BrakeResult Brake(VehicleTestBench bench, float fromKph = 100f)
        {
            Settle(bench, 1.5f);
            bench.AccelerateTo(fromKph + 3f);
            bench.SetInput();
            bench.Run(5f, () => bench.SpeedKph <= fromKph);
            var r = new BrakeResult();
            Vector3 start = bench.Body.position;
            float startYaw = bench.Body.rotation.eulerAngles.y;
            float t0 = bench.Time;
            bench.SetInput(brake: 1f);
            bench.Run(10f, () => bench.ForwardSpeed <= 0.05f, () =>
            {
                r.MaxSlip = Mathf.Max(r.MaxSlip, bench.Vehicle.Telemetry.MaxSlipRatio);
                r.MaxYawRate = Mathf.Max(r.MaxYawRate, Mathf.Abs(bench.Vehicle.Telemetry.YawRate));
            });
            r.Distance = Vector3.Distance(start, bench.Body.position);
            float v = fromKph * VehicleUnits.KphToMs;
            r.MeanDecelerationG = v * v / (2f * r.Distance) / VehicleUnits.Gravity;
            r.HeadingChange = Mathf.Abs(Mathf.DeltaAngle(startYaw, bench.Body.rotation.eulerAngles.y));
            bench.SetInput();
            return r;
        }

        /// <summary>Holds a speed while steering input rises slowly to full lock; reports the lateral-g plateau.</summary>
        public static CorneringResult RampSteer(VehicleTestBench bench, float kph, float rampSeconds = 8f, float maxInput = 1f)
        {
            Settle(bench, 1.5f);
            bench.AccelerateTo(kph);
            var r = new CorneringResult();
            float t0 = bench.Time;
            float target = kph * VehicleUnits.KphToMs;
            bench.Run(rampSeconds, null, () =>
            {
                float s = Mathf.Clamp01((bench.Time - t0) / rampSeconds) * maxInput;
                var input = bench.Input.Current;
                input.Steer = s;
                bench.Input.Current = input;
                bench.HoldSpeed(target);
                float lat = Mathf.Abs(bench.Vehicle.Telemetry.LateralG);
                if (lat > r.MaxLateralG) { r.MaxLateralG = lat; r.SpeedAtMax = bench.SpeedKph; }
                r.MaxSideslip = Mathf.Max(r.MaxSideslip, Mathf.Abs(bench.Sideslip));
                if (Mathf.Abs(bench.Sideslip) > 25f) r.Spun = true;
            });
            bench.SetInput();
            return r;
        }

        /// <summary>Digital (keyboard-style) full steer for <paramref name="holdSeconds"/> at speed, then release.</summary>
        public static StepSteerResult StepSteer(VehicleTestBench bench, float kph, float holdSeconds = 1f, float input = 1f)
        {
            Settle(bench, 1.5f);
            bench.AccelerateTo(kph);
            var r = new StepSteerResult();
            float startYaw = bench.Body.rotation.eulerAngles.y;
            float target = kph * VehicleUnits.KphToMs;
            bench.SetInput(steer: input);
            bench.Run(holdSeconds, null, () =>
            {
                bench.HoldSpeed(target);
                Sample(bench, ref r);
            });
            var released = bench.Input.Current;
            released.Steer = 0f;
            bench.Input.Current = released;
            bench.Run(3f, null, () =>
            {
                bench.HoldSpeed(target);
                Sample(bench, ref r);
            });
            r.ResidualYawRate = Mathf.Abs(bench.Vehicle.Telemetry.YawRate);
            r.HeadingChange = Mathf.Abs(Mathf.DeltaAngle(startYaw, bench.Body.rotation.eulerAngles.y));
            bench.SetInput();
            return r;
        }

        private static void Sample(VehicleTestBench bench, ref StepSteerResult r)
        {
            var t = bench.Vehicle.Telemetry;
            r.PeakYawRate = Mathf.Max(r.PeakYawRate, Mathf.Abs(t.YawRate));
            r.PeakLateralG = Mathf.Max(r.PeakLateralG, Mathf.Abs(t.LateralG));
            r.PeakSideslip = Mathf.Max(r.PeakSideslip, Mathf.Abs(bench.Sideslip));
            if (Mathf.Abs(bench.Sideslip) > 25f) r.Spun = true;
        }

        /// <summary>Drives over speed bumps at a constant speed.</summary>
        public static BumpResult Bumps(VehicleTestBench bench, float kph, float bumpHeight = 0.1f, int count = 3, float spacing = 22f)
        {
            for (int i = 0; i < count; i++) bench.AddBump(60f + i * spacing, bumpHeight, 0.3f);
            Settle(bench, 1.5f);
            var r = new BumpResult();
            float target = kph * VehicleUnits.KphToMs;
            bool contact = false;
            void OnHit(VehicleCollision c) => contact = true;
            bench.Vehicle.Collided += OnHit;
            float lastDisturbed = bench.Time;
            float endZ = 60f + (count - 1) * spacing;
            bench.SetInput();
            bench.Run(30f, () => bench.Body.position.z > endZ + 60f, () =>
            {
                bench.HoldSpeed(target);
                float pitch = Mathf.Abs(Mathf.DeltaAngle(0f, bench.Body.rotation.eulerAngles.x));
                r.MaxPitch = Mathf.Max(r.MaxPitch, pitch);
                bool disturbed = pitch > 0.6f;
                foreach (var w in bench.Vehicle.Wheels)
                {
                    r.MaxCompression = Mathf.Max(r.MaxCompression, w.CompressionRatio);
                    if (w.OnBumpStop) r.BumpStopSteps++;
                    if (!w.IsGrounded) { r.AirborneWheelSteps++; disturbed = true; }
                }
                if (disturbed) lastDisturbed = bench.Time;
            });
            bench.Vehicle.Collided -= OnHit;
            r.BodyContact = contact;
            float lastBumpTime = endZ / Mathf.Max(1f, target);
            r.SettleTime = Mathf.Max(0f, lastDisturbed - lastBumpTime - 1.5f);
            return r;
        }

        /// <summary>One telemetry row: time, inputs, body motion and per-axle tyre state.</summary>
        public static string TraceRow(VehicleTestBench b)
        {
            var t = b.Vehicle.Telemetry;
            var w = b.Vehicle.Wheels;
            return $"t={b.Time:0.00} v={b.SpeedKph:0} g{t.Gear} rpm={t.EngineRpm:0} thr={t.Throttle:0.00} brk={t.Brake:0.00} in={b.Vehicle.Steering.Input:0.00} " +
                   $"ang={t.SteerAngle:0.0} yaw={t.YawRate:0.0} beta={b.Sideslip:0.0} lat={t.LateralG:0.00} lon={t.LongitudinalG:0.00} " +
                   $"aF={w[0].SlipAngle:0.0}/{w[1].SlipAngle:0.0} aR={w[2].SlipAngle:0.0}/{w[3].SlipAngle:0.0} " +
                   $"sF={w[0].CombinedSlip:0.00}/{w[1].CombinedSlip:0.00} sR={w[2].CombinedSlip:0.00}/{w[3].CombinedSlip:0.00} " +
                   $"Fz={w[0].Load:0}/{w[1].Load:0}/{w[2].Load:0}/{w[3].Load:0}";
        }

        /// <summary>Step steer with a telemetry row every <paramref name="interval"/> seconds.</summary>
        public static string TraceStepSteer(VehicleDefinition def, float kph, float holdSeconds = 1f, float input = 1f, float interval = 0.1f, float totalSeconds = 3.5f)
        {
            var sb = new StringBuilder($"[Trace] step steer {def.Id} {kph:0} km/h input {input:0.00} for {holdSeconds:0.0} s\n");
            using var b = VehicleTestBench.Create(def);
            Settle(b, 1.5f);
            b.AccelerateTo(kph);
            float target = kph * VehicleUnits.KphToMs;
            float start = b.Time;
            b.SetInput(steer: input);
            while (b.Time - start < totalSeconds)
            {
                if (b.Time - start >= holdSeconds)
                {
                    var i = b.Input.Current;
                    i.Steer = 0f;
                    b.Input.Current = i;
                }
                b.Run(interval, null, () => b.HoldSpeed(target));
                sb.AppendLine(TraceRow(b));
            }
            return sb.ToString();
        }

        /// <summary>Straight-line full braking from <paramref name="kph"/> with a telemetry row every <paramref name="interval"/> seconds.</summary>
        public static string TraceBrake(VehicleDefinition def, float kph, float interval = 0.1f)
        {
            var sb = new StringBuilder($"[Trace] brake {def.Id} from {kph:0} km/h\n");
            using var b = VehicleTestBench.Create(def);
            Settle(b, 1.5f);
            b.AccelerateTo(kph);
            b.SetInput(brake: 1f);
            for (int i = 0; i < 200 && b.ForwardSpeed > 0.05f; i++)
            {
                b.Run(interval);
                var w = b.Vehicle.Wheels;
                sb.AppendLine(TraceRow(b) + $" sr={w[0].SlipRatio:0.00}/{w[1].SlipRatio:0.00}/{w[2].SlipRatio:0.00}/{w[3].SlipRatio:0.00} Tb={w[0].BrakeTorque:0}/{w[2].BrakeTorque:0}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes measured top speed, 0-100 time, peak power and mass into each catalog definition's Car Select stats
        /// (handling rating stays authored) and clears the provisional flag.
        /// </summary>
        [MenuItem("Neon Rift/Vehicles/Update Car Select Stats From Physics")]
        public static void UpdateDisplayStatsFromMenu() => Debug.Log(UpdateDisplayStats());

        public static string UpdateDisplayStats()
        {
            var sb = new StringBuilder("[VehicleProbe] Car Select stats from physics\n");
            foreach (var def in CatalogVehicles())
            {
                float zeroTo100, top;
                using (var b = VehicleTestBench.Create(def)) zeroTo100 = Launch(b).ZeroTo100;
                using (var b = VehicleTestBench.Create(def)) top = TopSpeed(b);
                float hp = def.PhysicsProfile.Engine.PeakPowerKw(out _) * 1.341f;
                var so = new SerializedObject(def);
                var stats = so.FindProperty("displayStats");
                stats.FindPropertyRelative("topSpeedKph").floatValue = Mathf.Round(top);
                stats.FindPropertyRelative("zeroToHundredSeconds").floatValue = Mathf.Round(zeroTo100 * 10f) / 10f;
                stats.FindPropertyRelative("powerHp").floatValue = Mathf.Round(hp / 5f) * 5f;
                stats.FindPropertyRelative("massKg").floatValue = def.PhysicsProfile.Chassis.mass;
                so.FindProperty("displayStatsProvisional").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                sb.AppendLine($"  {def.Id}: {top:0} km/h, 0-100 {zeroTo100:0.0} s, {hp:0} hp, {def.PhysicsProfile.Chassis.mass:0} kg");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        public struct RouteLapResult
        {
            public bool Completed;
            public float LapTime, MaxOffset, MinUp, MaxSpeedKph, MaxLateralG;
            public string WorstSection;
        }

        public const string TestTrackScene = "Assets/_NeonRift/Scenes/Dev/TestTrack.unity";

        /// <summary>
        /// One lap of the test track's validation route driven by <see cref="NeonRift.Gameplay.RouteAutopilot"/> on the
        /// bench. Fails the lap if the car strays more than <paramref name="maxOffset"/> from the driving line.
        /// </summary>
        public static RouteLapResult RouteLap(VehicleDefinition def, float maxOffset = 10f, float timeout = 150f)
        {
            var r = new RouteLapResult { MinUp = 1f };
            using var b = VehicleTestBench.Create(def);
            var routeObject = b.AddFromScene(TestTrackScene, "ValidationRoute");
            if (routeObject == null) { r.WorstSection = "route missing"; return r; }
            var route = routeObject.GetComponent<NeonRift.Gameplay.DrivingRoute>();
            Vector3 start = route.PointAt(0), next = route.PointAt(5);
            b.Vehicle.Teleport(start + Vector3.up * 0.05f + (next - start).normalized * 2f, Quaternion.LookRotation(next - start, Vector3.up));
            Settle(b, 1f);
            var pilot = new NeonRift.Gameplay.RouteAutopilot(route, b.Vehicle);
            b.Vehicle.SetInputSource(pilot);
            float t0 = b.Time;
            bool leftStart = false;
            int index = 0;
            b.Run(timeout, () =>
            {
                index = route.FindClosest(b.Body.position, index);
                float d = route.DistanceAt(index);
                if (d > route.Length * 0.5f) leftStart = true;
                if (leftStart && d < route.Length * 0.05f) { r.Completed = true; return true; }
                float offset = Vector3.Distance(b.Body.position, route.PointAt(index));
                if (offset > r.MaxOffset) { r.MaxOffset = offset; r.WorstSection = route.SectionAt(d); }
                r.MinUp = Mathf.Min(r.MinUp, Vector3.Dot(b.Body.rotation * Vector3.up, Vector3.up));
                r.MaxSpeedKph = Mathf.Max(r.MaxSpeedKph, b.SpeedKph);
                r.MaxLateralG = Mathf.Max(r.MaxLateralG, Mathf.Abs(b.Vehicle.Telemetry.LateralG));
                return r.MaxOffset > maxOffset;
            });
            r.LapTime = b.Time - t0;
            if (r.MaxOffset > maxOffset) r.Completed = false;
            b.Vehicle.SetInputSource(b.Input);
            return r;
        }

        [MenuItem("Neon Rift/Vehicles/Measure Vehicle Performance")]
        public static void MeasureCatalogFromMenu() => Debug.Log(MeasureCatalog());

        public static string MeasureCatalog()
        {
            var sb = new StringBuilder("[VehicleProbe] Bench results (flat asphalt, 100 Hz)\n");
            foreach (var def in CatalogVehicles())
                sb.AppendLine(Measure(def));
            return sb.ToString();
        }

        public static IEnumerable<VehicleDefinition> CatalogVehicles()
        {
            var guid = AssetDatabase.FindAssets("t:VehicleCatalog").FirstOrDefault();
            var catalog = guid != null ? AssetDatabase.LoadAssetAtPath<VehicleCatalog>(AssetDatabase.GUIDToAssetPath(guid)) : null;
            return catalog != null ? catalog.Vehicles.Where(v => v != null) : Enumerable.Empty<VehicleDefinition>();
        }

        public static string Measure(VehicleDefinition def)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== {def.DisplayName} ({def.Id}) profile {def.PhysicsProfile.name}");
            using (var b = VehicleTestBench.Create(def))
            {
                var s = Settle(b);
                sb.AppendLine($"  settle: residual {s.ResidualSpeed * 1000f:0.0} mm/s, ride-height error {s.MaxRideHeightError * 1000f:0.0} mm, wheel spin {s.MaxWheelSpin:0.000} rad/s, grounded {s.GroundedWheels}");
                var l = Launch(b);
                sb.AppendLine($"  launch: 0-100 {l.ZeroTo100:0.00} s, 0-200 {l.ZeroTo200:0.00} s, 1/4 mile {l.QuarterMileTime:0.00} s @ {l.QuarterMileKph:0} km/h, max slip {l.MaxSlip:0.00}");
                sb.AppendLine($"  top speed: {TopSpeed(b):0} km/h (profile estimate {b.Vehicle.EstimatedTopSpeed * VehicleUnits.MsToKph:0}) gear {b.Vehicle.Telemetry.Gear} @ {b.Vehicle.Telemetry.EngineRpm:0} rpm");
            }
            using (var b = VehicleTestBench.Create(def))
            {
                var r = Brake(b);
                sb.AppendLine($"  brake 100-0: {r.Distance:0.0} m, mean {r.MeanDecelerationG:0.00} g, max slip ratio {r.MaxSlip:0.00}, heading change {r.HeadingChange:0.0}°");
            }
            using (var b = VehicleTestBench.Create(def))
            {
                var r = Brake(b, 200f);
                sb.AppendLine($"  brake 200-0: {r.Distance:0.0} m, mean {r.MeanDecelerationG:0.00} g, heading change {r.HeadingChange:0.0}°");
            }
            foreach (float kph in new[] { 60f, 120f })
            {
                using var b = VehicleTestBench.Create(def);
                var r = RampSteer(b, kph);
                sb.AppendLine($"  ramp steer {kph:0} km/h: max lat {r.MaxLateralG:0.00} g @ {r.SpeedAtMax:0} km/h, max sideslip {r.MaxSideslip:0.0}°, spun {r.Spun}");
            }
            foreach (float kph in new[] { 80f, 160f, 220f })
            {
                using var b = VehicleTestBench.Create(def);
                var r = StepSteer(b, kph);
                sb.AppendLine($"  step steer {kph:0} km/h (full, 1 s): yaw {r.PeakYawRate:0}°/s, lat {r.PeakLateralG:0.00} g, sideslip {r.PeakSideslip:0.0}°, residual yaw {r.ResidualYawRate:0.0}°/s, spun {r.Spun}");
            }
            {
                var lap = RouteLap(def);
                sb.AppendLine($"  validation route lap: completed {lap.Completed}, {lap.LapTime:0.0} s, max {lap.MaxSpeedKph:0} km/h, max lat {lap.MaxLateralG:0.00} g, max offset {lap.MaxOffset:0.0} m ({lap.WorstSection}), min up {lap.MinUp:0.00}");
            }
            foreach (float kph in new[] { 30f, 60f })
            {
                using var b = VehicleTestBench.Create(def);
                var r = Bumps(b, kph);
                sb.AppendLine($"  bumps {kph:0} km/h: max comp {r.MaxCompression:0.00}, pitch {r.MaxPitch:0.0}°, bump-stop steps {r.BumpStopSteps}, airborne wheel steps {r.AirborneWheelSteps}, settle {r.SettleTime:0.00} s, body contact {r.BodyContact}");
            }
            return sb.ToString();
        }
    }
}
