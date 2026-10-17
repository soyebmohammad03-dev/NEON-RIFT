using System.Collections;
using System.Collections.Generic;
using System.Text;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Development aid for visual review (Harbor Rain 2.0): drives the running level through one section and saves Play
    /// Mode screenshots to <c>Logs/HarborReview/</c> — the Game view (chase camera, HUD) plus a few fixed side views from a
    /// copy of the main camera. Sections: "heist" (positioning → connection → locks → lift → theft → back to driving, then
    /// an automated escape with the police behind, logged to police_log.txt), "coast" (the extended sea road, bends,
    /// headland spray, gallery, conveyor, works, pier), "ships", "port". Not part of the mission.
    /// </summary>
    public sealed class HarborReviewShots : MonoBehaviour
    {
        private static bool restoreBackground;
        private static string section = "heist";
        private Camera shotCam;
        private HarborRainMission m;
        private VehicleController car;
        private readonly StringBuilder log = new();
        private const string Dir = "Logs/HarborReview";

        public static void Run(string which)
        {
            section = which;
            restoreBackground = Application.runInBackground;
            Application.runInBackground = true;
            System.IO.Directory.CreateDirectory(Dir);
            if (System.IO.File.Exists($"{Dir}/done.txt")) System.IO.File.Delete($"{Dir}/done.txt");
            new GameObject("HarborReviewShots").AddComponent<HarborReviewShots>();
        }

        private IEnumerator Capture(string name, Vector3? from = null, Vector3? at = null, float fov = 55f)
        {
            if (from.HasValue)
            {
                shotCam.transform.SetPositionAndRotation(from.Value, Quaternion.LookRotation(at.Value - from.Value));
                shotCam.fieldOfView = fov;
                shotCam.enabled = true;
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot($"{Dir}/{section}_{name}.png");
            yield return null;
            shotCam.enabled = false;
            yield return null;
            log.AppendLine($"{Time.time:0.0}  shot {name}");
        }

        private IEnumerator Place(Vector3 p, float yaw, float settle = 1.5f)
        {
            car.Teleport(p + Vector3.up * 0.3f, Quaternion.Euler(0f, yaw, 0f));
            car.Body.linearVelocity = Vector3.zero;
            var cam = HarborRainEntry.Active.Driving.ChaseCamera;
            if (cam != null) cam.Snap();
            yield return new WaitForSeconds(settle);
        }

        private IEnumerator Start()
        {
            while (HarborRainEntry.Active == null || HarborRainEntry.Active.Mission == null) yield return null;
            m = HarborRainEntry.Active.Mission;
            var main = Camera.main;
            var copy = Instantiate(main.gameObject);
            copy.name = "ReviewCamera";
            copy.tag = "Untagged";
            // Scripts first (some require the audio listener), then the remaining behaviours.
            foreach (var b in copy.GetComponents<MonoBehaviour>())
                if (b.GetType().Name != "UniversalAdditionalCameraData") DestroyImmediate(b);
            foreach (var b in copy.GetComponents<Behaviour>())
                if (b is not Camera && b.GetType().Name != "UniversalAdditionalCameraData") Destroy(b);
            for (int i = copy.transform.childCount - 1; i >= 0; i--) Destroy(copy.transform.GetChild(i).gameObject);
            shotCam = copy.GetComponent<Camera>();
            shotCam.depth = main.depth + 10;
            shotCam.enabled = false;
            yield return new WaitForSeconds(1f);
            if (m.Current == HarborRainMission.Phase.Garage) m.ChooseCar(0);
            yield return new WaitForSeconds(2f);
            if (m.Cinematics.Playing) m.Cinematics.Skip();
            yield return new WaitForSeconds(1f);
            car = HarborRainEntry.Active.Driving.PlayerVehicle;
            switch (section)
            {
                case "heist": yield return Heist(); yield return Escape(); break;
                case "coast": yield return Coast(); break;
                case "ships": yield return Ships(); break;
                case "port": yield return Port(); break;
                case "gate": yield return Gate(); break;
                case "vehicles": yield return Vehicles(); break;
                case "waveprobe": yield return WaveProbe(); break;
                case "audio": yield return AudioCheck(); break;
                case "perf": yield return Perf(); break;
                case "truck": yield return Truck(); break;
                case "forklift": yield return Forklift(); break;
                case "glitter": yield return Glitter(); break;
                case "closing": yield return Perf(); yield return Forklift(); yield return Truck(); break;
                case "teaser": yield return Teaser(); break;
                // Campaign playtest: the operation is already running with the campaign's car (no garage, no reset).
                case "campaign": yield return Heist(); yield return Escape(); yield return Extract(); break;
                case "final": yield return PortWide(); yield return Forklift(); yield return Heist(); yield return Escape(); yield return Extract(); break;
            }
            System.IO.File.WriteAllText($"{Dir}/{section}_log.txt", log.ToString());
            System.IO.File.WriteAllText($"{Dir}/done.txt", section);
            Application.runInBackground = restoreBackground;
            m.Garage.ResetProgress();
            Destroy(copy);
            Destroy(gameObject);
        }

        // ---------------- Heist ----------------

        private IEnumerator Heist()
        {
            yield return Place(m.DebugPoint("manifestPoint").position, 90f);
            m.OpenPuzzle("Manifest");
            var mp = (ManifestPuzzle)m.ActivePuzzle;
            mp.Choose(mp.Answer);
            yield return new WaitForSeconds(1.2f);
            yield return Place(m.DebugPoint("securityGatePoint").position, 0f);
            m.OpenPuzzle("Breach");
            var reels = (ReelPuzzle)m.ActivePuzzle;
            while (reels.Status == HarborPuzzle.State.Running) { if (reels.Showing == reels.Target[reels.Active]) reels.Lock(); yield return null; }
            yield return new WaitForSeconds(1.2f);
            var crane = m.Crane;
            var bay = m.DebugPoint("shipmentPoint").position;
            var box = crane.TargetBox;
            log.AppendLine($"target bay {m.TargetBay} at {bay}, box {box.position}");

            // Stage A: the car comes up the lane; the gantry moves over KX-7.
            yield return Place(new Vector3(225f, 0f, 104f), 0f, 1.2f);
            yield return Capture("a1_positioning_chase");
            yield return Capture("a2_gantry_travel", new Vector3(140f, 34f, 70f), new Vector3(225f, 12f, 160f), 60f);
            float t0 = Time.time;
            while (crane.Current == HarborBayCrane.Step.Positioning && Time.time - t0 < 30f) yield return null;
            log.AppendLine($"positioning took {Time.time - t0:0.0} s → {crane.Current}");
            yield return Capture("a3_over_target", box.position + new Vector3(30f, 16f, -34f), box.position + Vector3.up * 8f, 50f);

            // Stage B: park beside it, open the lock puzzle; the frame squares up and comes down.
            yield return Place(bay, 0f, 1f);
            m.OpenPuzzle("Acquire");
            var lp = (LockPuzzle)m.ActivePuzzle;
            m.Hud.ShowPuzzle(null);
            yield return Capture("b1_puzzle_framing");
            t0 = Time.time;
            while (crane.Current == HarborBayCrane.Step.Aligning && Time.time - t0 < 15f) yield return null;
            yield return Capture("b2_descending", box.position + new Vector3(16f, 7f, -20f), box.position + Vector3.up * 4f, 45f);
            while (!crane.Seated && Time.time - t0 < 30f) yield return null;
            log.AppendLine($"aligned + descended + seated in {Time.time - t0:0.0} s; sway {crane.SwayDegrees:0.00}°");
            yield return Capture("b3_seated_close", box.position + new Vector3(5f, 4.5f, -9f), box.position + new Vector3(0f, 2.6f, -5.5f), 45f);
            m.Hud.ShowPuzzle(lp);
            while (lp.Stage == 0) { if (lp.RingInWindow) lp.LockRing(); yield return null; }
            m.Hud.ShowPuzzle(null);
            float lockT = Time.time;
            while (crane.LocksEngaged < 2 && Time.time - lockT < 5f) yield return null;
            yield return Capture("b4_locks_turning", box.position + new Vector3(4f, 4f, -9f), box.position + new Vector3(0f, 2.6f, -5.8f), 40f);
            while (crane.LocksEngaged < 4 && Time.time - lockT < 5f) yield return null;
            log.AppendLine($"4 locks in {Time.time - lockT:0.00} s");
            yield return Capture("b5_locked_chase");
            m.Hud.ShowPuzzle(lp);
            while (lp.Stage == 1) { lp.TouchSteer = Mathf.Clamp(-lp.Needle * 4f, -1f, 1f); yield return null; }
            // Stage C: the lift.
            m.ForceInteract = true;
            bool shotTension = false;
            while (lp.Status == HarborPuzzle.State.Running)
            {
                if (!shotTension && crane.BoxLift > 0.4f) { shotTension = true; m.Hud.ShowPuzzle(null); yield return Capture("c1_taking_weight", box.position + new Vector3(18f, 4f, -16f), box.position + Vector3.up * 3f, 45f); m.Hud.ShowPuzzle(lp); }
                yield return null;
            }
            m.ForceInteract = false;
            yield return new WaitForSeconds(0.6f);
            yield return Capture("c2_beat_chase");
            yield return new WaitForSeconds(0.8f);
            yield return Capture("c3_beat_wide", new Vector3(150f, 30f, 62f), box.position + Vector3.up * 5f, 55f);
            while (m.Current != HarborRainMission.Phase.Escape) yield return null;
            log.AppendLine($"theft at box lift {crane.BoxLift:0.0} m; car held {m.CarHeld}");
            // Stage D: the alarm, the searchlights.
            yield return new WaitForSeconds(0.5f);
            yield return Capture("d1_theft_chase");
            yield return new WaitForSeconds(1.2f);
            yield return Capture("d2_alarm_searchlights", new Vector3(80f, 50f, 20f), new Vector3(225f, 4f, 160f), 60f);
            yield return new WaitForSeconds(1.2f);
            yield return Capture("e1_back_to_driving");
            log.AppendLine($"after theft: car held {m.CarHeld}, police {m.Police?.Current}, alarm {m.HeistAlarm?.Active}, heist stage {m.HeistStage}");
        }

        // ---------------- Escape with the police ----------------

        private sealed class Autopilot : IVehicleInputSource
        {
            public RacerDriver Driver;
            public DrivingInput ReadInput() => Driver != null ? Driver.ReadInput() : DrivingInput.None;
        }

        private IEnumerator Escape()
        {
            var police = m.Police;
            var profile = ScriptableObject.CreateInstance<RacerProfile>();
            var pilot = new Autopilot();
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(pilot);
            float start = Time.time, nextPlan = 0f, nextShot = Time.time + 6f;
            int shots = 0;
            var lastPos = new List<Vector3>();
            foreach (var u in police.Units) lastPos.Add(u.Car.Body.position);
            bool overridden = false;
            while (Time.time - start < 200f && m.Current == HarborRainMission.Phase.Escape)
            {
                if (Time.time >= nextPlan && m.RoutePath.Count > 1)
                {
                    // The stock AI drives the player's car to CONTROL's target (a test stand-in for a driver), a bit slower than flat out.
                    nextPlan = Time.time + 4f;
                    if (pilot.Driver == null) pilot.Driver = new RacerDriver(car, profile, police.Navigation) { PaceScale = 0.8f };
                    pilot.Driver.SetGoal(m.RoutePath[^1], true);
                    if (!m.CarHeld) foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(pilot);
                }
                if (m.Stage == HarborRainMission.EscapeStage.Override && !overridden && m.ActivePuzzle == null && car.Body.linearVelocity.magnitude < 2f)
                {
                    // At the east gate's junction box: run the power override (debug-solved), then drive on to the extraction.
                    if (m.OpenPuzzle("Override")) { ((PowerPuzzle)m.ActivePuzzle).DebugSolve(); overridden = true; log.AppendLine($"{Time.time - start:0}s override solved; police nearest {police.ClosestDistance:0}"); nextPlan = Time.time + 1.5f; }
                    foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(pilot);
                }
                if (Time.time >= nextShot && shots < 14)
                {
                    nextShot = Time.time + 8f;
                    shots++;
                    yield return Capture($"f{shots}_pursuit_chase");
                    var back = -car.transform.forward;
                    yield return Capture($"f{shots}b_pursuit_behind", car.transform.position + back * 26f + Vector3.up * 9f, car.transform.position + Vector3.up * 1f, 60f);
                }
                var sb = new StringBuilder($"{Time.time - start:00.0}s state {police.Current} sees {police.AnySees} lost {police.SinceSeen:0.0}  car {car.Body.position:F0} {car.Body.linearVelocity.magnitude:0} m/s |");
                for (int i = 0; i < police.Units.Count; i++)
                {
                    var u = police.Units[i];
                    sb.Append($" {u.Callsign} d={u.Distance:0} v={u.Car.Body.linearVelocity.magnitude:0} {(u.Intercepting ? "INTERCEPT" : "")}{(u.Sees ? " sees" : "")} drv={u.Driver.State} jump={u.MaxJump}cm |");
                }
                if (Mathf.FloorToInt(Time.time * 2f) != Mathf.FloorToInt((Time.time - Time.deltaTime) * 2f)) log.AppendLine(sb.ToString());
                yield return null;
            }
            log.AppendLine($"escape drive ended after {Time.time - start:0} s: stage {m.Stage}, police contacts {m.PoliceContacts}, closest {police.ClosestDistance:0} m, interceptions {police.Interceptions}, losses {police.Losses}, reacquired {police.Reacquired}");
            for (int i = 0; i < police.Units.Count; i++)
                log.AppendLine($"  {police.Units[i].Callsign}: travelled {police.Units[i].TravelledInPursuit:0} m in pursuit, largest frame step {police.Units[i].MaxJump} cm, recoveries {police.Units[i].Driver.Recoveries}, reversals {police.Units[i].Driver.Reversals}");
            yield return Capture("g1_end_chase");
        }

        // ---------------- Port overview and extraction (2.2) ----------------

        private IEnumerator PortWide()
        {
            yield return Place(new Vector3(-4f, 0f, -470f), 0f, 2f);
            yield return Capture("w1_port_overview", new Vector3(-150f, 70f, -420f), new Vector3(120f, 0f, -60f), 60f);
            yield return Capture("w2_gate_and_terminal", new Vector3(60f, 32f, -560f), new Vector3(-30f, 2f, -400f), 58f);
            yield return Place(new Vector3(-250f, 0f, 230f), 270f, 2f);
            yield return Capture("w3_berth_ship_cranes", new Vector3(-180f, 26f, 205f), new Vector3(-380f, 10f, 300f), 60f);
        }

        private IEnumerator Extract()
        {
            log.AppendLine($"extract: mission phase {m.Current}, stage {m.Stage}");
            if (m.Current != HarborRainMission.Phase.Escape) yield break;
            var ex = m.DebugPoint("extractionPoint").position;
            yield return Place(ex + new Vector3(-70f, 0f, 0f), 90f, 1.5f);
            yield return Capture("h1_extraction_approach");
            var pilot = new Autopilot();
            pilot.Driver = new RacerDriver(car, ScriptableObject.CreateInstance<RacerProfile>(), m.Police.Navigation) { PaceScale = 0.55f };
            pilot.Driver.SetGoal(ex, true);
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(pilot);
            float t0 = Time.time;
            bool mid = false;
            while (Time.time - t0 < 40f && m.Current == HarborRainMission.Phase.Escape)
            {
                if (!mid && Vector3.Distance(car.Body.position, ex) < 35f) { mid = true; yield return Capture("h2_extraction_arriving"); }
                yield return null;
            }
            log.AppendLine($"extract: reached {m.Current} after {Time.time - t0:0} s, grade {m.LastGrade}");
            yield return Capture("h3_extracted_chase");
            yield return Capture("h4_extraction_wide", ex + new Vector3(-40f, 18f, -30f), ex + new Vector3(10f, 1f, 5f), 60f);
            yield return new WaitForSeconds(3f);
            yield return Capture("h5_results");
        }

        // ---------------- Coast ----------------

        /// <summary>
        /// Campaign (Night Run opening): frames for the intercepted-transmission teaser — atmosphere only (cranes, stacks,
        /// a truck in the yard lights, a security camera, a searchlight, the surge on the seawall), never the heist.
        /// The player's car is parked near each subject (the light budget follows it) but out of frame.
        /// </summary>
        private IEnumerator Teaser()
        {
            var storm = FindAnyObjectByType<StormController>();
            if (storm != null) storm.Escalate(0.8f, 1f);
            // Clean frames: no HUD.
            var hudDoc = m.Hud != null ? m.Hud.GetComponent<UnityEngine.UIElements.UIDocument>() : null;
            if (hudDoc != null) hudDoc.rootVisualElement.style.display = UnityEngine.UIElements.DisplayStyle.None;
            yield return Place(new Vector3(-150f, 0f, 150f), 0f, 1.6f);
            yield return Capture("t1_cranes", new Vector3(-180f, 24f, 190f), new Vector3(-380f, 14f, 300f), 52f);
            var depot = GameObject.Find("Forklift_Depot");
            if (depot != null)
            {
                var t = depot.transform;
                Vector3 side = Vector3.Cross(Vector3.up, t.forward);
                yield return Place(t.position + side * 14f - t.forward * 4f, t.eulerAngles.y, 1.6f);
                yield return Capture("t2_stacks", t.position - side * 15f - t.forward * 12f + Vector3.up * 4.5f, t.position + t.forward * 6f + Vector3.up * 2.5f, 50f);
            }
            yield return Place(new Vector3(140f, 0f, -240f), 90f, 1f);
            var freight = GameObject.Find("TerminalTractor_Freight");
            if (freight != null)
            {
                var t = freight.transform;
                float t0 = Time.time;
                while (Time.time - t0 < 30f && (t.GetComponent<HarborVehicleDressing>()?.Speed ?? 0f) < 6f) yield return null;
                yield return new WaitForSeconds(2.5f);
                yield return Capture("t3_truck", t.TransformPoint(new Vector3(-7.5f, 2.0f, -15f)), t.TransformPoint(new Vector3(0f, 2.2f, 2f)), 50f);
            }
            yield return Place(new Vector3(-30f, 0f, -440f), 0f, 1.6f);
            yield return Capture("t4_gate", new Vector3(60f, 32f, -560f), new Vector3(-30f, 2f, -400f), 54f);
            if (m.Searchlights.Length > 0 && m.Searchlights[0] != null)
            {
                var sl = m.Searchlights[0].transform;
                Vector3 back = Vector3.ProjectOnPlane(-sl.forward, Vector3.up).normalized;
                yield return Place(sl.position + back * 20f - Vector3.up * sl.position.y, 0f, 1.6f);
                yield return Capture("t5_searchlight", sl.position + back * 14f + Vector3.up * 3f, sl.position - back * 30f - Vector3.up * 6f, 58f);
            }
            var spray = FindAnyObjectByType<HarborSprayBursts>();
            yield return Place(new Vector3(1908f, 0f, 300f), 0f, 0.5f);
            float s0 = Time.time;
            int before = spray != null ? spray.Bursts : 0;
            while (spray != null && spray.Bursts == before && Time.time - s0 < 30f) yield return null;
            yield return new WaitForSeconds(0.6f);
            yield return Capture("t6_surge", new Vector3(1925f, 6f, 300f), new Vector3(1895f, 2f, 350f), 56f);
            if (hudDoc != null) hudDoc.rootVisualElement.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
        }

        private IEnumerator Coast()
        {
            var storm = FindAnyObjectByType<StormController>();
            if (storm != null) storm.Escalate(1f, 1f);
            foreach (var (name, p, yaw) in new[]
            {
                ("01_sea_road_east", new Vector3(1700f, 0f, 276f), 90f), ("02_bend_a_approach", new Vector3(1850f, 0f, 276f), 90f),
                ("03_bend_a_turning", new Vector3(1900f, 0f, 282f), 50f), ("04_headland_spray_zone", new Vector3(1908f, 0f, 318f), 0f),
                ("05_storm_gallery", new Vector3(1908f, 0f, 392f), 0f), ("06_inside_gallery", new Vector3(1908f, 0f, 420f), 0f),
                ("07_bend_b_approach", new Vector3(1908f, 0f, 505f), 0f), ("08_pier_conveyor", new Vector3(1980f, 0f, 549f), 90f),
                ("09_pier_works", new Vector3(2080f, 0f, 549f), 90f), ("10_pier_extraction_lighthouse", new Vector3(2170f, 0f, 549f), 90f)
            })
            {
                yield return Place(p, yaw, 1.6f);
                yield return Capture(name);
            }
            var spray = FindAnyObjectByType<HarborSprayBursts>();
            yield return Place(new Vector3(1908f, 0f, 318f), 0f, 0.5f);
            float t0 = Time.time;
            int before = spray != null ? spray.Bursts : 0;
            while (spray != null && !spray.Warning && Time.time - t0 < 25f) yield return null;
            yield return Capture("11_spray_warning");
            while (spray != null && spray.Bursts == before && Time.time - t0 < 30f) yield return null;
            yield return new WaitForSeconds(0.6f);
            yield return Capture("12_spray_burst");
            yield return Capture("13_spray_side", new Vector3(1925f, 6f, 300f), new Vector3(1895f, 2f, 350f), 60f);
            yield return Capture("14_headland_aerial", new Vector3(1760f, 70f, 380f), new Vector3(1980f, 0f, 470f), 60f);
            yield return Capture("15_pier_aerial", new Vector3(2000f, 45f, 470f), new Vector3(2230f, 0f, 560f), 60f);
        }

        // ---------------- Ships ----------------

        private IEnumerator Ships()
        {
            yield return Place(new Vector3(-250f, 0f, 230f), 270f, 2f);
            yield return Capture("01_quay_road_ship");
            yield return Capture("02_ship_from_apron", new Vector3(-180f, 6f, 285f), new Vector3(-320f, 12f, 330f), 60f);
            yield return Capture("03_ship_wide", new Vector3(-60f, 20f, 380f), new Vector3(-320f, 10f, 330f), 55f);
            yield return Place(new Vector3(1200f, 0f, 276f), 270f, 2f);
            yield return Capture("04_coast_road_west_view");
            yield return Capture("05_harbour_basin", new Vector3(900f, 12f, 300f), new Vector3(400f, 0f, 700f), 60f);
            yield return Place(new Vector3(2200f, 0f, 552f), 270f, 2f);
            yield return Capture("06_pier_looking_back");
            yield return Capture("07_extraction_boat", new Vector3(2230f, 5f, 590f), new Vector3(2262f, 1f, 579f), 55f);
            yield return Capture("08_horizon_from_pier", new Vector3(2260f, 4f, 566f), new Vector3(2400f, 2f, 1500f), 60f);
        }

        // ---------------- Gate approach and vehicles ----------------

        private IEnumerator Gate()
        {
            foreach (var (name, z) in new[] { ("01_spawn_520", -520f), ("02_approach_470", -470f), ("03_gantry_440", -440f), ("04_canopy_400", -405f), ("05_under_canopy", -376f) })
            {
                yield return Place(new Vector3(-4f, 0f, z), 0f, 1.8f);
                yield return Capture(name);
            }
            yield return Capture("06_gate_wide", new Vector3(-60f, 25f, -520f), new Vector3(0f, 2f, -380f), 55f);
        }

        private IEnumerator Vehicles()
        {
            yield return Place(new Vector3(560f, 0f, -64f), 180f, 2f);
            foreach (var name in new[] { "Forklift_Depot", "Forklift_Logistics", "TerminalTractor_Freight", "TerminalTractor_Industrial" })
            {
                var go = GameObject.Find(name);
                if (go == null) { log.AppendLine($"missing {name}"); continue; }
                var t = go.transform;
                bool fork = name.StartsWith("Forklift");
                yield return Place(t.position + t.right * (fork ? 9f : 16f) + t.forward * (fork ? 6f : 10f), Quaternion.LookRotation(-t.right).eulerAngles.y, 0.5f);
                for (int k = 0; k < 2; k++)
                {
                    var at = t.position + Vector3.up * (fork ? 1.2f : 2f);
                    var from = at + t.right * (fork ? 5.5f : 11f) + t.forward * (fork ? 3.5f : 7f) + Vector3.up * (fork ? 1.2f : 2.5f);
                    yield return Capture($"{name}_{k}", from, at, 50f);
                    var f = go.GetComponent<HarborForklift>();
                    log.AppendLine($"{name}: pos {t.position:F1} {(f != null ? $"forks {f.Lift:0.00} m, trips {f.Trips}" : $"speed {go.GetComponent<HarborVehicleDressing>()?.Speed:0.0} m/s")}");
                    yield return new WaitForSeconds(3f);
                }
            }
            yield return Place(new Vector3(-50f, 0f, -480f), 0f, 2f);
            yield return Capture("05_held_trucks_close", new Vector3(-50f, 2f, -470f), new Vector3(-60f, 1.5f, -452f), 55f);
        }

        private IEnumerator WaveProbe()
        {
            var w = m.Waves;
            var sec = w.Sections[1];
            yield return Place(new Vector3(sec.road.xMin - 12f, 0f, 276f), 90f, 1f);
            w.Trigger(1);
            float t0 = Time.time;
            while (Time.time - t0 < 30f)
            {
                if (sec.plume != null && sec.plume.particleCount > 0)
                {
                    var ps = new ParticleSystem.Particle[sec.plume.particleCount];
                    int n = sec.plume.GetParticles(ps);
                    var bb = new Bounds(ps[0].position, Vector3.zero);
                    float alpha = 0f, size = 0f;
                    for (int i = 0; i < n; i++) { bb.Encapsulate(ps[i].position); alpha += ps[i].GetCurrentColor(sec.plume).a / 255f; size += ps[i].GetCurrentSize(sec.plume); }
                    var main = sec.plume.main;
                    log.AppendLine($"     plume space {main.simulationSpace} at {sec.plume.transform.position:F0}: particles span {bb.min:F0}..{bb.max:F0}, mean alpha {alpha / n:0.00}, mean size {size / n:0.0}, renderer {sec.plume.GetComponent<ParticleSystemRenderer>().renderMode} mat {sec.plume.GetComponent<ParticleSystemRenderer>().sharedMaterial?.name}");
                }
                log.AppendLine($"{Time.time - t0:00.0} {sec.state,-9} plume {sec.plume?.particleCount,4} (on {sec.plume?.gameObject.activeInHierarchy}) mist {sec.mist?.particleCount,4} drops {sec.droplets?.particleCount,4} spouts {sec.spouts?.particleCount,4} | overtop {(sec.overtop != null ? sec.overtop.enabled + "/" + sec.overtop.gameObject.activeInHierarchy : "-")} front {sec.overFront:0.00} thick {sec.overThick:0.00} | flood {(sec.flood != null ? sec.flood.enabled + "" : "-")} front {sec.floodFront:0.00} depth {sec.floodDepth:0.00} foam {sec.floodFoam:0.00}");
                yield return new WaitForSeconds(0.5f);
            }
        }

        // ---------------- Audio (levels and WAVs; not a listening test) ----------------

        private IEnumerator Record(NeonRift.Audio.AudioOutputRecorder rec, string name, float seconds)
        {
            rec.Begin(seconds);
            yield return new WaitForSecondsRealtime(seconds + 0.1f);
            rec.End();
            var x = rec.Samples();
            double sum = 0; float peak = 0f;
            foreach (var v in x) { sum += v * v; peak = Mathf.Max(peak, Mathf.Abs(v)); }
            float rms = x.Length > 0 ? Mathf.Sqrt((float)(sum / x.Length)) : 0f;
            log.AppendLine($"AUDIO {name}: {x.Length} samples, RMS {20f * Mathf.Log10(Mathf.Max(rms, 1e-6f)):0.0} dBFS, peak {peak:0.000}; music vol {m.Music?.Volume:0.00}, radio duck {HarborRadio.Duck:0.00}, radio speaking {m.Radio?.Current}");
            WriteWav($"{Dir}/audio_{name}.wav", x, rec.SampleRate);
        }

        private static void WriteWav(string path, float[] x, int rate)
        {
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
            int bytes = x.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (var v in x) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
        }

        private IEnumerator AudioCheck()
        {
            var listener = FindAnyObjectByType<AudioListener>();
            var rec = listener.gameObject.AddComponent<NeonRift.Audio.AudioOutputRecorder>();
#if UNITY_EDITOR
            log.AppendLine($"editor audio muted: {UnityEditor.EditorUtility.audioMasterMute}");
#endif
            yield return Place(new Vector3(0f, 0f, -470f), 0f, 3f);
            yield return Record(rec, "01_spawn_music_storm_engine", 5f);
            log.AppendLine($"radio before: speaking {m.Radio.Current}, phase {m.Current}");
            m.Radio.Say("police_close");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Record(rec, "02_radio_line_music_ducked", 3f);
            yield return new WaitForSecondsRealtime(3f);
            yield return Record(rec, "03_after_radio_music_back", 3f);
            // A police unit: siren on, the car at three distances.
            var u = m.Police.Units[0];
            u.Kit.Set(HarborPoliceCar.Lights.Respond, true, false);
            foreach (var d in new[] { 15f, 60f, 150f })
            {
                yield return Place(u.Car.Body.position + u.Car.transform.right * d, 0f, 1f);
                AudioSource sir = null;
                foreach (var a in u.Car.GetComponentsInChildren<AudioSource>(true)) if (a.name == "Siren") sir = a;
                log.AppendLine($"siren: {(sir == null ? "none" : $"playing {sir.isPlaying}, clip {sir.clip?.name}, vol {sir.volume:0.00}, group {sir.outputAudioMixerGroup?.name}, enabled {sir.enabled}/{sir.gameObject.activeInHierarchy}, dist to listener {Vector3.Distance(sir.transform.position, listener.transform.position):0} m, min/max {sir.minDistance}/{sir.maxDistance}")}");
                yield return Record(rec, $"04_siren_{d:0}m", 3f);
            }
            u.Kit.Set(HarborPoliceCar.Lights.Off, false, false);
            Destroy(rec);
        }

        // ---------------- Performance (editor Play Mode; not a device) ----------------

        private IEnumerator Sample(string where)
        {
            yield return new WaitForSeconds(1.5f);
            int frames = 0; float t = 0f, worst = 0f; long tris = 0, calls = 0, setpass = 0;
            double cpuMain = 0, cpuRender = 0, gpu = 0; int timed = 0;
            var timings = new FrameTiming[1];
            long mono0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            int gc0 = System.GC.CollectionCount(0);
            while (t < 4f)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime; t += dt; worst = Mathf.Max(worst, dt); frames++;
                // CPU / GPU work per frame (independent of the unfocused editor's frame throttling).
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].cpuMainThreadFrameTime > 0)
                { cpuMain += timings[0].cpuMainThreadFrameTime; cpuRender += timings[0].cpuRenderThreadFrameTime; gpu += timings[0].gpuFrameTime; timed++; }
#if UNITY_EDITOR
                tris += UnityEditor.UnityStats.triangles; calls += UnityEditor.UnityStats.drawCalls; setpass += UnityEditor.UnityStats.setPassCalls;
#endif
            }
            long mono1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var budget = FindFirstObjectByType<NeonRift.Gameplay.LightBudget>();
            int lights = 0; foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.isActiveAndEnabled) lights++;
            log.AppendLine($"PERF {where}: {t / frames * 1000f:0.0} ms avg ({frames / t:0} fps), worst {worst * 1000f:0} ms, draw calls {calls / frames}, set-pass {setpass / frames}, triangles {tris / frames:N0}, " +
                           $"active lights {lights} (budget {budget?.ActiveCount}), managed heap +{(mono1 - mono0) / 1024f:0} KB over {t:0} s, gen-0 GCs {System.GC.CollectionCount(0) - gc0}" +
                           (timed > 0 ? $" | frame timing: CPU main {cpuMain / timed:0.0} ms, render thread {cpuRender / timed:0.0} ms, GPU {gpu / timed:0.0} ms ({timed} frames)" : " | frame timing unavailable"));
        }

        private IEnumerator Perf()
        {
            yield return Place(new Vector3(-4f, 0f, -470f), 0f, 1f); yield return Sample("gate approach");
            {
                var cost = new Dictionary<string, (long tris, int n, bool shadow)>();
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.isVisible || r is not MeshRenderer mr || !mr.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                    long tri = 0; for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tri += mf.sharedMesh.GetIndexCount(i) / 3;
                    var root = r.transform; while (root.parent != null && root.parent.parent != null) root = root.parent;
                    string key = root.name + "/" + mf.sharedMesh.name;
                    cost.TryGetValue(key, out var c);
                    cost[key] = (c.tris + tri, c.n + 1, c.shadow || r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
                }
                var list = new List<KeyValuePair<string, (long tris, int n, bool shadow)>>(cost);
                list.Sort((x, y) => y.Value.tris.CompareTo(x.Value.tris));
                for (int i = 0; i < Mathf.Min(18, list.Count); i++) log.AppendLine($"  visible {list[i].Value.tris,9:N0} tris ×{list[i].Value.n,3} {(list[i].Value.shadow ? "shadow" : "      ")} {list[i].Key}");
            }
            yield return Place(m.DebugPoint("shipmentPoint").position, 0f, 1f); yield return Sample("restricted bay");
            yield return Place(new Vector3(1180f, 0f, 276f), 90f, 1f); m.Waves.Trigger(1);
            float w0 = Time.time; while (m.Waves.Sections[1].state != HarborWaves.State.Impact && Time.time - w0 < 30f) yield return null;
            yield return Sample("coast road, wave impact");
            yield return Place(new Vector3(2000f, 0f, 549f), 90f, 1f); yield return Sample("pier");
            yield return Place(new Vector3(-250f, 0f, 230f), 270f, 1f); yield return Sample("quay road by the ship");
        }

        // ---------------- Port truck (2.2) ----------------

        private void TruckState(string tag, Transform t)
        {
            var d = t.GetComponent<HarborVehicleDressing>();
            float minWheel = 99f, maxWheel = -99f;
            foreach (Transform c in t)
                if (c.name.EndsWith("Wheel"))
                {
                    float bottom = c.position.y - 0.535f;
                    float ground = Physics.Raycast(c.position + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : float.NaN;
                    float gap = bottom - (float.IsNaN(ground) ? 0f : ground);
                    minWheel = Mathf.Min(minWheel, gap); maxWheel = Mathf.Max(maxWheel, gap);
                }
            var spin = t.Find("FrontWheel/Spin");
            var steer = t.Find("FrontWheel");
            var tail = t.Find("Body/TailLamps")?.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            string brake = "-";
            if (tail != null) { tail.GetPropertyBlock(block); brake = block.GetColor("_EmissionColor").r > 2f ? "ON" : "off"; }
            var box = t.Find("Body/Cargo/Container");
            string cargo = box != null ? $"container at deck {box.position.y - t.position.y:0.000} m, offset {Vector3.Distance(Vector3.ProjectOnPlane(box.position - t.position, Vector3.up), Vector3.zero):0.000} m" : "no container";
            log.AppendLine($"{tag}: {t.name} pos {t.position:F1} yaw {t.eulerAngles.y:0} speed {(d != null ? d.Speed : 0f):0.0} m/s | wheel-ground gap {minWheel:+0.000;-0.000}..{maxWheel:+0.000;-0.000} m | front spin {(spin != null ? spin.localEulerAngles.x : 0f):0} deg, steer {(steer != null ? Mathf.DeltaAngle(0f, steer.localEulerAngles.y) : 0f):0.0} deg | brake lamps {brake} | {cargo}");
        }

        private IEnumerator Truck()
        {
            // Park the player's car behind the freight truck's start (it only yields to a car ahead of it); the light budget
            // follows the car, so the yard lights around the truck are live for the shots.
            yield return Place(new Vector3(140f, 0f, -240f), 90f, 1f);
            var freight = GameObject.Find("TerminalTractor_Freight");
            if (freight == null) { log.AppendLine("missing TerminalTractor_Freight"); yield break; }
            var t = freight.transform;
            // Let it pull away from the start and get up to speed.
            float t0 = Time.time;
            while (Time.time - t0 < 30f && (t.GetComponent<HarborVehicleDressing>()?.Speed ?? 0f) < 6f) yield return null;
            TruckState("moving", t);
            Vector3 F(Vector3 local) => t.TransformPoint(local);
            yield return Capture("01_front34_moving", F(new Vector3(7.5f, 2.3f, 22f)), F(new Vector3(0f, 1.8f, 6f)), 50f);
            TruckState("moving", t);
            yield return Capture("02_side_moving", F(new Vector3(17f, 2.0f, 2f)), F(new Vector3(0f, 1.7f, 2f)), 58f);
            yield return Capture("03_rear34_moving", F(new Vector3(-6.5f, 2.4f, -17f)), F(new Vector3(0f, 1.6f, -3f)), 52f);
            yield return Capture("04_wheels_close", F(new Vector3(5.2f, 1.0f, 10.5f)), F(new Vector3(1.0f, 0.7f, 6.5f)), 55f);
            for (int k = 0; k < 3; k++)
            {
                yield return Capture($"05_tracking_{k}", F(new Vector3(9f, 3.2f, 14f)), F(new Vector3(0f, 1.5f, 3f)), 55f);
                TruckState($"tracking {k}", t);
                yield return new WaitForSeconds(1.2f);
            }
            // Arrival: braking into the unload stop (brake lamps), then the chase camera's view of the stopped truck.
            while (Time.time - t0 < 120f && (t.GetComponent<HarborVehicleDressing>()?.Speed ?? 0f) > 0.2f) yield return null;
            TruckState("stopped", t);
            yield return Capture("06_stopped_rear", F(new Vector3(-3.5f, 1.6f, -14f)), F(new Vector3(0f, 1.2f, -5f)), 55f);
            // One-way runs: with the camera far away the truck re-enters at the start of its road, facing along it.
            yield return Place(new Vector3(-4f, 0f, -560f), 0f, 14f);
            TruckState("re-entered (expect x≈170..230, yaw 90)", t);

            var mule = GameObject.Find("CrewTransferTruck");
            if (mule != null)
            {
                yield return Place(mule.transform.position + new Vector3(-16f, 0f, -14f), 0f, 1.5f);
                var mt = mule.transform;
                Vector3 M(Vector3 l) => mt.TransformPoint(l);
                TruckState("crew truck", mt);
                yield return Capture("07_crew_front34", M(new Vector3(-8f, 2.2f, 21f)), M(new Vector3(0f, 1.8f, 6f)), 50f);
                yield return Capture("08_crew_side", M(new Vector3(-17f, 1.9f, 2f)), M(new Vector3(0f, 1.6f, 2f)), 58f);
                yield return Capture("09_crew_rear", M(new Vector3(4f, 1.8f, -16f)), M(new Vector3(0f, 1.3f, -4f)), 52f);
            }
            yield return Capture("10_held_queue", new Vector3(-30f, 3.2f, -598f), new Vector3(-60f, 2f, -540f), 55f);
            yield return Capture("11_held_cabs", new Vector3(-50f, 2.2f, -548f), new Vector3(-60f, 2f, -534f), 55f);
            yield return Place(new Vector3(-50f, 0f, -585f), 0f, 1.5f);
            yield return Capture("12_chase_by_held_trucks");
        }

        // ---------------- Ground sparkle diagnosis (2.2) ----------------

        private IEnumerator Glitter()
        {
            foreach (var (p, yaw) in new[] { (new Vector3(300f, 0f, -240f), 90f), (new Vector3(0f, 0f, -150f), 0f) })
            {
                yield return Place(p, yaw, 2f);
                string tag = $"{p.x:0}";
                yield return Capture($"{tag}_a_normal");
                var parts = FindObjectsByType<ParticleSystemRenderer>();
                foreach (var r in parts) r.enabled = false;
                yield return Capture($"{tag}_b_no_particles");
                foreach (var r in parts) r.enabled = true;
                var wet = new List<Renderer>();
                foreach (var r in FindObjectsByType<MeshRenderer>())
                    if (r.sharedMaterial != null && (r.sharedMaterial.name.Contains("Puddle") || r.sharedMaterial.name.Contains("Flood") || r.sharedMaterial.name.Contains("LightPool"))) { wet.Add(r); r.enabled = false; }
                yield return Capture($"{tag}_c_no_puddles_pools");
                foreach (var r in wet) r.enabled = true;
                var names = new HashSet<string>();
                foreach (var r in parts) if (r.isVisible) names.Add(r.name);
                log.AppendLine($"{tag}: visible particle systems: {string.Join(", ", names)}");
            }
        }

        // ---------------- Forklift (2.2) ----------------

        private IEnumerator Forklift()
        {
            var go = GameObject.Find("Forklift_Depot");
            if (go == null) { log.AppendLine("missing Forklift_Depot"); yield break; }
            var f = go.GetComponent<HarborForklift>();
            // The car waits off to one side (inside the light budget's reach, out of the forklift's way).
            var t = go.transform;
            yield return Place(t.position + Vector3.Cross(Vector3.up, t.forward) * 14f - t.forward * 4f, t.eulerAngles.y, 1f);
            string last = "";
            int shot = 0;
            float t0 = Time.time;
            while (Time.time - t0 < 240f && shot < 14)
            {
                if (f.State != last)
                {
                    last = f.State;
                    // A fixed side of the working line (the camera doesn't swing round with the forklift), a little ahead of it.
                    var lineSide = Vector3.Cross(Vector3.up, (f.PointB - f.PointA).normalized);
                    var at = t.position + t.forward * 1.3f + Vector3.up * 1.1f;
                    var from = at + lineSide * 6.5f + Vector3.up * 1.3f - (f.PointB - f.PointA).normalized * 1.5f;
                    yield return Capture($"{shot:00}_{last.ToLower()}", from, at, 52f);
                    log.AppendLine($"  {Time.time - t0:0.0}s {last}: lift {f.Lift:0.00} m, carrying {f.Carrying}, steer {f.Steer:0} deg, trips {f.Trips}, pos {t.position:F1}");
                    shot++;
                }
                yield return null;
            }
            log.AppendLine($"forklift: {f.Trips} trips in {Time.time - t0:0} s");
        }

        // ---------------- Port ----------------

        private IEnumerator Port()
        {
            foreach (var (name, p, yaw) in new[]
            {
                ("01_spawn", new Vector3(0f, 0f, -470f), 0f), ("02_south_perimeter", new Vector3(-200f, 0f, -305f), 270f), ("03_spine", new Vector3(0f, 0f, -150f), 0f),
                ("04_cross_road", new Vector3(200f, 0f, -180f), 90f), ("05_freight_service", new Vector3(300f, 0f, -240f), 90f), ("06_industrial_service", new Vector3(-400f, 0f, -120f), 270f),
                ("07_terminal_road", new Vector3(-300f, 0f, -50f), 90f), ("08_lane80", new Vector3(300f, 0f, 80f), 90f), ("09_quay_road_east", new Vector3(500f, 0f, 230f), 90f),
                ("10_restricted_lane", new Vector3(225f, 0f, 110f), 0f), ("11_east_road", new Vector3(641f, 0f, -100f), 0f), ("12_service_road", new Vector3(1100f, 0f, 156f), 90f)
            })
            {
                yield return Place(p, yaw, 1.6f);
                yield return Capture(name);
            }
        }
    }
}
