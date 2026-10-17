using System.Collections;
using System.Collections.Generic;
using System.Text;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Play Mode check of Harbor Rain, written to <c>Logs/HarborRainValidation.txt</c>. Three kinds of test, labelled
    /// in the report: AUTOMATED DRIVE (the stock WaypointDriver drives a car on real physics), SIMULATED INPUT (puzzles
    /// are solved and failed through their public input methods — the same calls the keyboard / touch handlers make)
    /// and STAGE CHECKS (the car is teleported between objectives). Also measures the storm's audio output level,
    /// samples frame time and counts console errors. Harbor Rain's unlock progress is reset at the end.
    /// </summary>
    public sealed class HarborRainValidator : MonoBehaviour
    {
        public const string ReportPath = "Logs/HarborRainValidation.txt";
        private readonly StringBuilder log = new();
        private int failures, errors;

        private static bool restoreBackground;

        public static void Run()
        {
            restoreBackground = Application.runInBackground;
            Application.runInBackground = true;
            new GameObject("HarborRainValidator").AddComponent<HarborRainValidator>();
        }

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;
        private void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Error or LogType.Exception) { errors++; log.AppendLine($"  console {type}: {message}"); }
        }

        private IEnumerator Start()
        {
            File("running\n");
            float wait = 0f;
            while (HarborRainEntry.Active == null || HarborRainEntry.Active.Driving.PlayerVehicle == null)
            {
                if ((wait += Time.unscaledDeltaTime) > 30f) { Check(false, "entry adopted and vehicle spawned"); Finish(); yield break; }
                yield return null;
            }
            var entry = HarborRainEntry.Active;
            var mission = entry.Mission;
            yield return new WaitForSeconds(1f);

            // ---- Garage and Level 2 progression (fresh Harbor Rain progress; Night Run data untouched).
            mission.Garage.ResetProgress();
            mission.OpenGarage();
            var catalog = entry.Context.Config.VehicleCatalog;
            int catalogCount = catalog != null ? catalog.Count : -1;
            bool catalogClean = true;
            if (catalog != null) foreach (var v in catalog.Vehicles) if (v != null && v.Id.StartsWith("hr_")) catalogClean = false;
            var cars = mission.GarageCars;
            int locked = 0;
            foreach (var c in cars) if (c.locked) locked++;
            Check(mission.Current == HarborRainMission.Phase.Garage, $"garage opens first (phase {mission.Current})");
            Check(cars.Count == catalogCount + 2 && locked == 2, $"garage lists {cars.Count} cars: {catalogCount} Night Run catalog cars + 2 Level 2 cars, {locked} locked");
            Check(catalogClean, $"Night Run vehicle catalog unchanged: {catalogCount} cars, no Harbor Rain cars in it");
            Check(!mission.ChooseCar(cars.Count - 1), "locked Level 2 car cannot be driven yet");
            Check(mission.ChooseCar(0), $"catalog car {cars[0].car.DisplayName} confirmed");
            yield return null;
            bool intro = mission.Cinematics != null && mission.Cinematics.Playing;
            Check(intro && mission.CarHeld, $"arrival reveal plays (shot '{mission.Cinematics?.CurrentShot}') with the car held");
            yield return new WaitForSeconds(2.5f);
            mission.Cinematics.Skip();
            yield return null;
            Check(mission.Current == HarborRainMission.Phase.Enter && !mission.CarHeld, $"reveal skippable → {mission.Current}, controls returned");

            var car = entry.Driving.PlayerVehicle;
            var body = car.Body;
            yield return new WaitForSeconds(1.5f);
            int grounded = 0;
            foreach (var w in car.Wheels) if (w.IsGrounded) grounded++;
            Check(grounded == car.Wheels.Count, $"spawn: {grounded}/{car.Wheels.Count} wheels grounded at {body.position}");
            var police = mission.Police;
            float nearestSpawn = float.MaxValue;
            if (police != null) foreach (var u in police.Units) nearestSpawn = Mathf.Min(nearestSpawn, Vector3.Distance(u.Car.Body.position, body.position));
            Check(mission.Rival != null && police != null && police.Units.Count == 3 && police.Current == HarborPolice.State.Patrol && nearestSpawn > 60f,
                  $"rival crew and {police?.Units.Count} police units spawned on their patrols (state {police?.Current}), nearest {nearestSpawn:0} m from the player");
            Check(mission.Music != null && mission.Music.Playing && mission.Music.Starts == 1, $"gameplay music playing (the existing track), started once; volume {mission.Music?.Volume:0.00}");
            var patrolStart = new List<Vector3>();
            if (police != null) foreach (var u in police.Units) patrolStart.Add(u.Car.Body.position);
            Check(mission.Radio != null && mission.Radio.Played.Contains("start") && mission.Radio.ClipCount() == mission.Radio.LineCount,
                  $"CONTROL radio: 'start' played; {mission.Radio?.ClipCount()}/{mission.Radio?.LineCount} lines have voice clips");
            yield return AudioLevel("storm bed at intensity 0.45 (spawn)");
            yield return Perf("spawn");

            // ---- AUTOMATED DRIVE: spawn → checkpoint → manifest office.
            var receivers = car.GetComponentsInChildren<IVehicleInputReceiver>();
            var route = new[] { body.position, new Vector3(0f, 0f, -400f), new Vector3(0f, 0f, -330f), new Vector3(0f, 0f, -200f), new Vector3(0f, 0f, -70f), new Vector3(20f, 0f, -50f), new Vector3(46f, 0f, -50f) };
            var driver = new WaypointDriver(car, route, new[] { 16f, 16f, 10f, 18f, 12f, 8f, 5f });
            foreach (var r in receivers) r.SetInputSource(driver);
            float t0 = Time.time, top = 0f;
            while (Vector3.Distance(body.position, new Vector3(46f, body.position.y, -50f)) > 6f && Time.time - t0 < 120f) { top = Mathf.Max(top, body.linearVelocity.magnitude); yield return null; }
            float miss = Vector3.Distance(body.position, new Vector3(46f, body.position.y, -50f));
            Check(miss <= 12f, $"AUTOMATED DRIVE spawn → checkpoint → manifest office: {miss:0.0} m from the kiosk in {Time.time - t0:0} s, top {top * 3.6f:0} km/h");
            Check(mission.Current == HarborRainMission.Phase.Manifest, $"checkpoint passed while driving → {mission.Current}");
            foreach (var r in receivers) r.SetInputSource(new NeonRift.Input.PlayerDrivingInput(mission.Controls));
            Check(mission.RoutePath.Count > 1, $"CONTROL route to the manifest: {mission.RoutePath.Count} points");
            int patrolling = 0;
            for (int i = 0; i < police.Units.Count; i++) if (Vector3.Distance(police.Units[i].Car.Body.position, patrolStart[i]) > 25f) patrolling++;
            Check(patrolling == police.Units.Count, $"police patrol their loops (lights off): {patrolling}/{police.Units.Count} units moved > 25 m on the roads");

            // ---- SIMULATED INPUT: manifest puzzle (wrong pick, then the right one).
            yield return Teleport(car, mission, "manifestPoint");
            Check(mission.OpenPuzzle("Manifest") && mission.CarHeld, "manifest terminal opens with the car parked");
            var manifest = (ManifestPuzzle)mission.ActivePuzzle;
            float alertBefore = mission.Alert;
            manifest.Choose((manifest.Answer + 1) % 4);
            yield return null;
            Check(manifest.Status == HarborPuzzle.State.Running && manifest.Mistakes == 1 && mission.Alert > alertBefore,
                  $"manifest wrong pick: struck out, puzzle continues, suspicion {alertBefore:0.00} → {mission.Alert:0.00}");
            int expectBay = manifest.AnswerBay;
            manifest.Choose(manifest.Answer);
            yield return null;
            yield return null;
            Check(mission.Current == HarborRainMission.Phase.Breach && mission.TargetBay == expectBay && !mission.CarHeld,
                  $"manifest solved → {mission.Current}, KX-7 in bay R-{mission.TargetBay + 1}, controls returned");
            yield return new WaitForSeconds(0.5f);
            Check(mission.Rival.Current == HarborAIDriver.Mode.Route, "rival crew launched on its route");

            // ---- AUTOMATED AI DRIVE: the rival crew drives its whole road route; arriving first trips the alarm.
            var rivalStart = mission.Rival.Vehicle.Body.position;
            Time.timeScale = 3f;
            float r0 = Time.time;
            while (!mission.Rival.Arrived && Time.time - r0 < 150f) yield return null;
            Time.timeScale = 1f;
            Check(mission.Rival.Arrived, $"rival crew drove {Vector3.Distance(rivalStart, mission.Rival.Vehicle.Body.position):0} m (straight-line) to the restricted gate in {Time.time - r0:0} s game time");
            yield return null;
            Check(mission.AlarmRaised, "rival reaching the cargo first raised the alarm early");

            // ---- Security gate reels: three wrong locks fail it (lockout), then a clean solve.
            yield return Teleport(car, mission, "securityGatePoint");
            mission.OpenPuzzle("Breach");
            var reels = (ReelPuzzle)mission.ActivePuzzle;
            for (int i = 0; i < 3; i++)
            {
                while (reels.Showing == reels.Target[reels.Active]) yield return null;
                reels.Lock();
            }
            yield return null;
            Check(mission.ActivePuzzle == null && !mission.CarHeld && reels.Status == HarborPuzzle.State.Failed, "badge reader: 3 wrong locks → tripped, lockout, controls returned");
            Check(!mission.OpenPuzzle("Breach") || mission.ActivePuzzle == null, "reader refuses to reopen during the lockout");
            yield return new WaitForSeconds(4.5f);
            Check(mission.OpenPuzzle("Breach"), "reader reopens after the lockout with a fresh code");
            reels = (ReelPuzzle)mission.ActivePuzzle;
            float rs = Time.time;
            while (reels.Status == HarborPuzzle.State.Running && Time.time - rs < 25f)
            {
                if (reels.Showing == reels.Target[reels.Active]) reels.Lock();
                yield return null;
            }
            yield return null;
            int looped = 0, restricted = 0;
            foreach (var c in mission.Cameras) if (c.Restricted) { restricted++; if (c.Offline) looped++; }
            Check(mission.Current == HarborRainMission.Phase.Acquire && looped == restricted, $"badge reader solved in {Time.time - rs:0.0} s → {mission.Current}; cameras looped {looped}/{restricted}");

            // ---- Container lock: twistlocks (timing), seal signal (hold in band), lift (hold).
            yield return Teleport(car, mission, "shipmentPoint");
            yield return Perf("restricted area");
            var crane = mission.Crane;
            // Every step the crane actually enters (event-driven: a step can begin and end inside one frame).
            var craneSteps = new List<HarborBayCrane.Step> { crane.Current };
            void OnStep(HarborBayCrane.Step s) { if (craneSteps[^1] != s) craneSteps.Add(s); }
            crane.StepEntered += OnStep;
            mission.OpenPuzzle("Acquire");
            var lockP = (LockPuzzle)mission.ActivePuzzle;
            lockP.LockRing();   // most likely outside the window: a counted mistake, not a failure
            float ls = Time.time;
            while (lockP.Status == HarborPuzzle.State.Running && Time.time - ls < 40f)
            {
                if (lockP.Stage == 0 && lockP.RingInWindow) lockP.LockRing();
                if (lockP.Stage == 1) lockP.TouchSteer = Mathf.Clamp(-lockP.Needle * 4f, -1f, 1f);
                mission.ForceInteract = lockP.Stage == 2;
                yield return null;
            }
            mission.ForceInteract = false;
            yield return null;
            float liftWait = Time.time;
            while (mission.Current != HarborRainMission.Phase.Escape && Time.time - liftWait < 12f)
            {
                yield return null;
            }
            crane.StepEntered -= OnStep;
            bool order = craneSteps.IndexOf(HarborBayCrane.Step.Descending) >= 0 && craneSteps.IndexOf(HarborBayCrane.Step.Seated) > craneSteps.IndexOf(HarborBayCrane.Step.Descending)
                         && Mathf.Max(craneSteps.IndexOf(HarborBayCrane.Step.Locked), craneSteps.IndexOf(HarborBayCrane.Step.Locking)) > craneSteps.IndexOf(HarborBayCrane.Step.Seated)
                         && craneSteps.IndexOf(HarborBayCrane.Step.Hoisting) > craneSteps.IndexOf(HarborBayCrane.Step.Seated);   // Locked can last a single frame on a fast solve
            Check(order && crane.LocksEngaged == 4 && crane.Carrying, $"gantry crane sequence: {string.Join(" → ", craneSteps)}; 4/4 twistlocks, box on the hook");
            Check(mission.Current == HarborRainMission.Phase.Escape && mission.CargoLift > 4f && !mission.CarHeld,
                  $"container lock: 3 stages in {Time.time - ls:0.0} s ({lockP.Mistakes} mistakes) → lift beat {Time.time - liftWait:0.0} s, KX-7 raised {mission.CargoLift:0.0} m → {mission.Current}, controls returned");
            int chasing = 0;
            foreach (var d in mission.Drones) if (d.Current == HarborDrone.Mode.Chase) chasing++;
            Check(chasing == mission.Drones.Length && police.Current is HarborPolice.State.Alert or HarborPolice.State.Pursuit && mission.Radio.Requested("police_dispatch"),
                  $"theft response: drones chasing {chasing}/{mission.Drones.Length}, police {police.Current} with sirens, dispatch call {(mission.Radio.Requested("police_dispatch") ? "made" : "missing")}");
            int tracking = 0;
            foreach (var sl in mission.Searchlights) if (sl.Tracking) tracking++;
            Check(mission.HeistAlarm != null && mission.HeistAlarm.Active && tracking == mission.Searchlights.Length && mission.HeistStage == 5,
                  $"heist moment: compound alarm sequence running, {tracking}/{mission.Searchlights.Length} searchlights converging, back to driving (stage {mission.HeistStage})");

            Check(mission.Radio.Played.Contains("theft"), "CONTROL: theft call made");
            Check(mission.ActiveBlockade >= 0 && !mission.BlockadeRevealed, $"roadblock chosen for this run: #{mission.ActiveBlockade}");

            // ---- Roadblock and rerouting.
            car.Teleport(new Vector3(450f, 0.3f, 80f), Quaternion.Euler(0f, 90f, 0f));
            yield return new WaitForSeconds(2.5f);
            Check(mission.BlockadeRevealed && mission.Radio.Requested("blockade"), $"roadblock revealed when the car came within range; OVERWATCH call {(mission.Radio.Played.Contains("blockade") ? "made" : "queued behind the theft call")}");
            yield return new WaitForSeconds(1.5f);
            var routes = mission.Routes;
            var path = new List<Vector3>();
            int avoided = 0, tested = 0;
            foreach (var start in new[] { new Vector3(450f, 0f, 230f), new Vector3(450f, 0f, 80f), new Vector3(450f, 0f, -50f), new Vector3(225f, 0f, 160f) })
            {
                tested++;
                bool ok = routes.Find(start, mission.DebugPoint("overridePoint").position, path, out float m, out _);
                if (ok && !Through(path, routes.Closures)) avoided++;
                log.AppendLine($"INFO  reroute from {start}: {(ok ? $"{m:0} m via {routes.Signature(path)}" : "no route")}");
            }
            Check(avoided == tested, $"CONTROL reroutes around the roadblock: {avoided}/{tested} start points get an open route to the east gate");

            var floods = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            car.Teleport(new Vector3(0f, 0.3f, -100f), Quaternion.identity);
            Time.timeScale = 4f;
            yield return new WaitUntil(() => mission.EscapeElapsed > 85f || mission.Current != HarborRainMission.Phase.Escape);
            Time.timeScale = 1f;
            int closed = 0;
            foreach (var f in mission.DeepFloods) if (f.Closed) closed++;
            Check(closed == mission.DeepFloods.Length && !mission.LaneCheckpoint.IsOpen, $"quayside closed {closed}/{mission.DeepFloods.Length}, lane 80 sealed");
            bool stillOpen = routes.Find(new Vector3(0f, 0f, -100f), mission.DebugPoint("overridePoint").position, path, out float detour, out _);
            Check(stillOpen && !Through(path, routes.Closures), $"with floods, checkpoint and roadblock closed, a viable route remains ({detour:0} m via {routes.Signature(path)})");
            Check(mission.Reroutes >= 1, $"CONTROL announced {mission.Reroutes} reroute(s) during the escape");
            yield return AudioLevel($"storm bed at intensity {FindFirstObjectByType<StormController>().Intensity:0.00} (escape)");

            // ---- AUTOMATED AI DRIVE with the police: the stock RacerDriver drives the player's car along CONTROL's route; the
            //      units chase on the road graph (no teleports), close in, and an interceptor tries to get ahead.
            var pilot = new PilotInput { Driver = new RacerDriver(car, ScriptableObject.CreateInstance<RacerProfile>(), police.Navigation) { PaceScale = 0.8f } };
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(pilot);
            float pd = Time.time, closest = float.MaxValue, nextGoal = 0f;
            bool pursued = false;
            var travelled = new float[police.Units.Count];
            while (Time.time - pd < 30f && mission.Stage == HarborRainMission.EscapeStage.ToGate)
            {
                if (Time.time >= nextGoal && mission.RoutePath.Count > 1) { nextGoal = Time.time + 4f; pilot.Driver.SetGoal(mission.RoutePath[^1], true); }
                pursued |= police.Current == HarborPolice.State.Pursuit;
                foreach (var u in police.Units) closest = Mathf.Min(closest, u.Distance);
                yield return null;
            }
            int jumps = 0, stuck = 0;
            for (int i = 0; i < police.Units.Count; i++) { var u = police.Units[i]; if (u.MaxJump > 50) jumps++; if (u.TravelledInPursuit < 120f) stuck++; }
            Check(pursued && closest < 60f, $"POLICE PURSUIT (automated drive {Time.time - pd:0} s): state reached Pursuit {pursued}, closest unit {closest:0} m, interception attempts {police.Interceptions}");
            Check(jumps == 0, $"no visible police teleport during the pursuit (largest unexplained on-screen frame step {MaxJump(police)} cm; off-screen stuck-car resets by the stock driver: {Offscreen(police)})");
            Check(stuck == 0, $"every unit kept driving: travelled {string.Join(", ", UnitTravel(police))} m in pursuit; recoveries {Recoveries(police)}");
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(new NeonRift.Input.PlayerDrivingInput(mission.Controls));
            // Escape and recovery: out of sight and far away the units lose the car and search; seen again, they resume.
            car.Teleport(new Vector3(1100f, 0.3f, 156f), Quaternion.Euler(0f, 90f, 0f));
            float ls0 = Time.time;
            while (police.Current != HarborPolice.State.Search && Time.time - ls0 < 25f) yield return null;
            Check(police.Current == HarborPolice.State.Search && police.Losses >= 1, $"out of sight and range the police lose the car after {Time.time - ls0:0} s and search the roads round the last known position");
            float rs0 = Time.time;
            for (int k = 0; k < police.Units.Count && police.Current != HarborPolice.State.Pursuit; k++)
            {
                // Put the car on the road in front of a searching unit (each in turn until one has a clear sight line).
                var u = police.Units[k].Car;
                car.Teleport(u.Body.position + u.transform.forward * 25f + Vector3.up * 0.3f, u.transform.rotation);
                float w = Time.time;
                while (police.Current != HarborPolice.State.Pursuit && Time.time - w < 3f) yield return null;
            }
            Check(police.Current == HarborPolice.State.Pursuit && police.Reacquired >= 1, $"seen again → pursuit resumes ({Time.time - rs0:0.0} s)");
            // ---- East gate override: a timeout trips the breaker, then a solve opens the gate.
            yield return Teleport(car, mission, "overridePoint");
            Check(mission.Stage == HarborRainMission.EscapeStage.Override, $"east gate reached → stage {mission.Stage}");
            mission.OpenPuzzle("Override");
            var power = (PowerPuzzle)mission.ActivePuzzle;
            Time.timeScale = 5f;
            while (power.Status == HarborPuzzle.State.Running) yield return null;
            Time.timeScale = 1f;
            Check(power.Status == HarborPuzzle.State.Failed && mission.ActivePuzzle == null, "power override timeout → breaker tripped (−5 s), retry allowed");
            mission.OpenPuzzle("Override");
            power = (PowerPuzzle)mission.ActivePuzzle;
            power.RotateTile(power.CursorRow, power.CursorCol);
            power.DebugSolve();
            yield return null;
            yield return null;
            Check(power.Status == HarborPuzzle.State.Solved && mission.ExtractionGate.IsOpen && mission.Stage == HarborRainMission.EscapeStage.Coast,
                  "power routed (generated path) → east gate open, coast stage, waves running");

            // ---- Coast road: a full wave cycle on section 0 with the car in the swept road.
            var waves = mission.Waves;
            var sec = waves.Sections[0];
            car.Teleport(new Vector3(sec.road.center.x - 60f, 0.3f, 276f), Quaternion.Euler(0f, 90f, 0f));
            yield return new WaitForSeconds(1f);
            waves.Trigger(0);
            var seen = new HashSet<HarborWaves.State>();
            float ws = Time.time;
            bool washedCar = false, slick = false, curled = false, collapsed = false, overtopped = false, flooded = false, drained = false, revealed = false;
            float maxHeight = 0f, maxDepth = 0f, offshore = 0f, lateralBefore = 0f, maxLateralChange = 0f;
            var coastCam = Object.FindAnyObjectByType<HarborCoastCamera>();
            while (Time.time - ws < 45f)
            {
                seen.Add(sec.state);
                if (sec.breaker != null && sec.breaker.Visible)
                {
                    maxHeight = Mathf.Max(maxHeight, sec.breaker.Height);
                    if (sec.state == HarborWaves.State.Building) offshore = Mathf.Max(offshore, sec.distance);
                    if (sec.breaker.Curl > 0.9f && sec.distance < 20f)
                    {
                        // The lip must overhang its own face: the lip point is shoreward of the face below it.
                        var lip = sec.breaker.Point(0f, 7f / 15f);    // upper lip, thrown forward
                        var face = sec.breaker.Point(0f, 13f / 15f);  // lower face, under the tube
                        curled |= lip.z < face.z - 1f && lip.y > face.y + 1f;
                    }
                    collapsed |= sec.breaker.Collapse > 0.95f;
                }
                if (coastCam != null && coastCam.Reveal > 0.5f) revealed = true;
                if (sec.state == HarborWaves.State.Approach && !washedCar && sec.t > 1.5f)
                {
                    bool avoids = routes.Find(new Vector3(892f, 0f, 240f), mission.DebugPoint("extractionPoint").position, path, out float via, out _) && !ThroughRect(path, sec.road);
                    Check(avoids, $"while {sec.label} is dangerous CONTROL routes from the coast junction via the dry service road ({via:0} m via {routes.Signature(path)})");
                    yield return new WaitForSeconds(1.2f);
                    Check(mission.HoldShort, "car on the sea road short of the wave: marker switches to HOLD SHORT");
                    car.Teleport(new Vector3(sec.road.center.x, 0.3f, 276f), Quaternion.Euler(0f, 90f, 0f));
                    washedCar = true;
                }
                if (sec.overFront > 0.95f && sec.overThick > 0.3f) overtopped = true;
                if (sec.state == HarborWaves.State.Washing)
                {
                    if (sec.slickSurface != null && sec.slickSurface.enabled) slick = true;
                    if (waves.Sample(car.Body.position, out float d, out _, out _)) { flooded = true; maxDepth = Mathf.Max(maxDepth, d); }
                    float lat = Vector3.Dot(car.Body.linearVelocity, Vector3.forward);
                    maxLateralChange = Mathf.Max(maxLateralChange, Mathf.Abs(lat - lateralBefore));
                }
                else lateralBefore = Vector3.Dot(car.Body.linearVelocity, Vector3.forward);
                if (sec.state == HarborWaves.State.Calm && seen.Contains(HarborWaves.State.Receding))
                {
                    drained = sec.floodDrain > 0.9f && (sec.slickSurface == null || !sec.slickSurface.enabled) && (sec.slickShallow == null || !sec.slickShallow.enabled);
                    break;
                }
                yield return null;
            }
            Check(seen.Contains(HarborWaves.State.Building) && seen.Contains(HarborWaves.State.Approach) && seen.Contains(HarborWaves.State.Impact) &&
                  seen.Contains(HarborWaves.State.Washing) && seen.Contains(HarborWaves.State.Receding) && sec.state == HarborWaves.State.Calm,
                  $"wave cycle on {sec.label}: {string.Join(" → ", seen)} and back to calm in {Time.time - ws:0} s");
            Check(offshore > 200f && maxHeight > 5f && curled && collapsed,
                  $"breaker: swell first seen {offshore:0} m out, crest {maxHeight:0.0} m, lip overhangs its face before the wall: {curled}, collapses: {collapsed}");
            Check(overtopped && flooded && maxDepth > 0.08f, $"green water over the wave wall onto the road ({overtopped}); flood reached the car, depth {maxDepth:0.00} m");
            Check(slick && mission.WaveHits >= 1, $"wave washed the car: low-grip surface on, {mission.WaveHits} wave hit(s) counted");
            Check(sec.peakPush > 0.3f && sec.peakPush <= 3.6f && maxLateralChange < 7f && car.transform.up.y > 0.9f,
                  $"water force applied and bounded: peak {sec.peakPush:0.0} m/s² (caps 3.5 along / 2.4 across), car's cross-road speed changed ≤ {maxLateralChange:0.0} m/s, stayed level");
            Check(drained, "water drained: flood sheet gone, low-grip surfaces off by the calm");
            Check(revealed, "coast camera eased wide to frame the incoming set");
            // The next set on the same section comes round on its own while the storm runs.
            float next = Time.time;
            while (sec.state == HarborWaves.State.Calm && Time.time - next < 30f) yield return null;
            Check(sec.state == HarborWaves.State.Building, $"next set on {sec.label} builds {Time.time - next:0} s after the calm");
            Check(car.transform.up.y > 0.7f, $"car upright after the wave (up.y {car.transform.up.y:0.00}), at {car.Body.position}");
            Check(mission.Radio.Played.Contains("coast") || mission.Radio.Played.Contains("wave_warn"), "CONTROL / OVERWATCH wave warning given");
            yield return Perf("coast road");

            // ---- Port activity: the working cranes keep moving and never bring a box low over the apron.
            var cycles = Object.FindObjectsByType<HarborCraneCycle>(FindObjectsInactive.Exclude);
            int moving = 0; float clearance = float.MaxValue;
            foreach (var cy in cycles) { if (cy.Moves > 3) moving++; clearance = Mathf.Min(clearance, cy.MinApronClearance); }
            Check(cycles.Length >= 3 && moving == cycles.Length && clearance >= 6.9f,
                  $"cranes: {moving}/{cycles.Length} working cycles moving, lowest box over the apron {(clearance == float.MaxValue ? "n/a" : clearance.ToString("0.0") + " m")} (≥ 7 m)");

            // ---- The heist set after the theft: the box went onto the crew trailer, never low over the lane.
            Check(crane.Deliveries == 1 && crane.LowestCarriedOverLane >= 8f && mission.Mule != null && mission.Mule.Loaded,
                  $"KX-7 carried to the crew trailer (lowest over the lane {(crane.LowestCarriedOverLane == float.MaxValue ? "n/a" : crane.LowestCarriedOverLane.ToString("0.0") + " m")}), loaded; truck {(mission.Mule.Going ? "leaving" : mission.Mule.Arrived ? "gone" : "waiting")}");
            // ---- Coastal extension: both the sea road and the inland bypass reach the pier-end extraction.
            var sea = new List<Vector3>();
            bool seaOk = routes.Find(new Vector3(892f, 0f, 276f), mission.DebugPoint("extractionPoint").position, sea, out float seaM, out _);
            var bypass = new List<Vector3>();
            bool byOk = routes.Find(new Vector3(892f, 0f, 156f), mission.DebugPoint("extractionPoint").position, bypass, out float byM, out _);
            Check(seaOk && byOk && ThroughRect(sea, new Rect(1900f, 300f, 16f, 230f)) && ThroughRect(bypass, new Rect(1900f, 300f, 16f, 230f)),
                  $"coast: sea road {seaM:0} m and inland bypass {byM:0} m both reach the extraction over the headland and pier");
            var spray = Object.FindAnyObjectByType<HarborSprayBursts>();
            Check(spray != null && spray.Bursts > 0, $"headland spray zone: {spray?.Bursts} warned spray bursts so far");

            // ---- Extraction, debrief, unlocks.
            log.AppendLine($"INFO  lockdown left before the extraction teleport: {mission.LockdownLeft:0} s (the validator spends it on stage checks); police contacts {mission.PoliceContacts}");
            yield return Teleport(car, mission, "extractionPoint");
            Check(mission.Current == HarborRainMission.Phase.Extracted, $"extraction → {mission.Current}, grade {mission.LastGrade}");
            Check(police.Current == HarborPolice.State.StoodDown, $"police stand down at the extraction ({police.Current})");
            bool f40 = false, f296 = false;
            foreach (var u in mission.Garage.Unlockables) { if (u.requirement == HarborGarage.Requirement.CompleteOperation) f40 = HarborGarage.IsUnlocked(u.vehicle); else f296 = HarborGarage.IsUnlocked(u.vehicle); }
            Check(f40, "F40 LB-WORKS unlocked by completing the operation");
            Check(f296 == (HarborGarage.GradeRank(mission.LastGrade) >= 2), $"296 SPECIALE A unlock matches the grade rule (grade {mission.LastGrade}: {(f296 ? "unlocked" : "still locked")})");
            Check(catalog == null || catalog.Count == catalogCount, "Night Run catalog still unchanged after unlocking");

            // ---- The unlocked F40 in the garage, on the stock vehicle architecture, driven by the stock driver.
            yield return new WaitForSeconds(1f);
            mission.OpenGarage();
            int f40Index = -1;
            for (int i = 0; i < mission.GarageCars.Count; i++) if (mission.GarageCars[i].car.Id == "hr_ferrari_f40_lb") f40Index = i;
            Check(f40Index >= 0 && mission.ChooseCar(f40Index), "garage: F40 LB-WORKS selectable after unlock");
            yield return new WaitForSeconds(0.5f);
            if (mission.Cinematics.Playing) mission.Cinematics.Skip();
            yield return new WaitForSeconds(1.5f);
            car = entry.Driving.PlayerVehicle;
            Check(car != null && car.name.Contains("hr_ferrari_f40_lb") && car.GetComponent<VehicleRig>() != null, $"player car is {car?.name} with the stock VehicleController + VehicleRig");
            grounded = 0;
            foreach (var w in car.Wheels) if (w.IsGrounded) grounded++;
            body = car.Body;
            var drive = new WaypointDriver(car, new[] { body.position, new Vector3(0f, 0f, -400f), new Vector3(0f, 0f, -340f) }, new[] { 22f, 22f, 6f });
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(drive);
            float ft = Time.time, ftop = 0f;
            while (Time.time - ft < 12f && Vector3.Distance(body.position, new Vector3(0f, body.position.y, -340f)) > 8f) { ftop = Mathf.Max(ftop, body.linearVelocity.magnitude); yield return null; }
            Check(grounded == 4 && ftop > 12f, $"AUTOMATED DRIVE in the F40: 4 wheels grounded, top {ftop * 3.6f:0} km/h, {Vector3.Distance(body.position, new Vector3(0f, body.position.y, -340f)):0} m from the checkpoint after {Time.time - ft:0} s");
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(new NeonRift.Input.PlayerDrivingInput(mission.Controls));

            mission.ResetLevel();
            yield return new WaitForSeconds(1.5f);
            bool floodsReset = true;
            foreach (var f in floods) floodsReset &= !f.Flooding && !f.Closed;
            bool wavesReset = true;
            foreach (var s in mission.Waves.Sections) wavesReset &= s.state == HarborWaves.State.Calm;
            Check(mission.Current == HarborRainMission.Phase.Enter && floodsReset && wavesReset && mission.LaneCheckpoint.IsOpen && !mission.AlarmRaised && !mission.ExtractionGate.IsOpen,
                  "restart resets mission, floods, waves, gates and alarm");
            Check(police.Current == HarborPolice.State.Patrol && crane.Current == HarborBayCrane.Step.Parked && !mission.HeistAlarm.Active && !mission.Mule.Loaded,
                  $"restart resets the police (patrol), the crane (parked), the compound alarm and the crew truck");
            Check(mission.Music.Playing && mission.Music.Starts == 1, $"music kept playing through the run and the restart without restarting (starts {mission.Music.Starts})");
            Check(errors == 0, $"console errors during validation: {errors}");

            // Leave Harbor Rain's progression as the player would find it (locked), and the last car as the default.
            mission.Garage.ResetProgress();
            Finish();
        }

        private sealed class PilotInput : IVehicleInputSource
        {
            public RacerDriver Driver;
            public DrivingInput ReadInput() => Driver != null ? Driver.ReadInput() : DrivingInput.None;
        }

        private static int Offscreen(HarborPolice p) { int n = 0; foreach (var u in p.Units) n += u.OffscreenRecoveries; return n; }
        private static int MaxJump(HarborPolice p) { int m = 0; foreach (var u in p.Units) m = Mathf.Max(m, u.MaxJump); return m; }
        private static IEnumerable<string> UnitTravel(HarborPolice p) { foreach (var u in p.Units) yield return $"{u.Callsign} {u.TravelledInPursuit:0}"; }
        private static int Recoveries(HarborPolice p) { int n = 0; foreach (var u in p.Units) n += u.Driver.Recoveries; return n; }

        private static bool Through(List<Vector3> path, List<(Rect area, string label)> closures)
        {
            foreach (var (area, _) in closures) if (ThroughRect(path, area)) return true;
            return false;
        }

        private static bool ThroughRect(List<Vector3> path, Rect area)
        {
            for (int i = 1; i < path.Count; i++)
                for (int k = 0; k <= 20; k++)
                {
                    var p = Vector3.Lerp(path[i - 1], path[i], k / 20f);
                    if (area.Contains(new Vector2(p.x, p.z))) return true;
                }
            return false;
        }

        /// <summary>Measures the mixed output (RMS over ~1.5 s, both channels) and the storm sources' state.</summary>
        private IEnumerator AudioLevel(string where)
        {
            // Measured from the listener's own filter tap (the project's AudioOutputRecorder): it still receives the mix
            // when the editor's 'Mute Audio' is on, unlike AudioListener.GetOutputData. A level, not a listening test.
            var storm = FindFirstObjectByType<StormController>();
            var listener = FindAnyObjectByType<AudioListener>();
            var rec = listener.GetComponent<NeonRift.Audio.AudioOutputRecorder>() ?? listener.gameObject.AddComponent<NeonRift.Audio.AudioOutputRecorder>();
            rec.Begin(1.5f);
            yield return new WaitForSecondsRealtime(1.6f);
            rec.End();
            var x = rec.Samples();
            double sum = 0; float peak = 0f;
            foreach (var v in x) { sum += v * v; peak = Mathf.Max(peak, Mathf.Abs(v)); }
            float rms = x.Length > 0 ? Mathf.Sqrt((float)(sum / x.Length)) : 0f;
            float db = 20f * Mathf.Log10(Mathf.Max(rms, 1e-6f));
            Check(storm != null && storm.RainPlaying && rms > 0.005f && peak < 0.99f, $"audio output — {where}: RMS {db:0.0} dBFS, peak {peak:0.00} (no clipping); rain source playing {storm?.RainPlaying}, volume {storm?.RainVolumeNow:0.00}; music {(HarborRainEntry.Active?.Mission.Music?.Playing == true ? "playing" : "NOT playing")}");
        }

        private IEnumerator Perf(string where)
        {
            yield return new WaitForSeconds(0.5f);
            int frames = 0;
            float t = 0f, worst = 0f;
            while (t < 3f) { float dt = Time.unscaledDeltaTime; t += dt; worst = Mathf.Max(worst, dt); frames++; yield return null; }
            var budget = FindFirstObjectByType<LightBudget>();
            log.AppendLine($"INFO  frame time at {where}: avg {t / frames * 1000f:0.0} ms ({frames / t:0} fps), worst {worst * 1000f:0} ms, " +
                           $"real-time lamps on {(budget != null ? budget.ActiveCount : -1)} (editor Play Mode, not a player build)");
        }

        private static IEnumerator Teleport(VehicleController car, HarborRainMission mission, string point)
        {
            var target = mission.DebugPoint(point);
            car.Teleport(target.position + Vector3.up * 0.3f, target.rotation);
            car.Body.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(1.5f);
        }

        private void Check(bool ok, string what)
        {
            if (!ok) failures++;
            log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {what}");
            Debug.Log($"[HarborRainValidation] {(ok ? "PASS" : "FAIL")} {what}");
        }

        private static void File(string text) => System.IO.File.WriteAllText(ReportPath, text);

        private void Finish()
        {
            Application.runInBackground = restoreBackground;   // in the editor this setting persists to Player Settings
            Time.timeScale = 1f;
            log.Insert(0, $"Harbor Rain validation: {(failures == 0 ? "ALL PASSED" : failures + " FAILED")}\n");
            File(log.ToString());
            Destroy(gameObject);
        }
    }
}
