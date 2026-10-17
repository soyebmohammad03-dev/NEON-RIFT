using System;
using System.Collections;
using System.Collections.Generic;
using NeonRift.Game;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Harbor Rain inside the connected campaign (campaign bootstrap only; the standalone level never creates this).
    /// <list type="bullet">
    /// <item>ARRIVAL: the Night Run car bursts out of the rift at the south perimeter, the camera rises over the storm-lit
    /// port, and the unlock card confirms the real Night Run result and the new operation; Confirm opens the five-car
    /// selection.</item>
    /// <item>TRAILER (after the selection): eight shots of the real level — port, target, the chosen car, security, the
    /// crane, the lockdown, the coast road — narrated and subtitled, ending on the operation's actual objectives and
    /// controls. Skipping lands in exactly the same state as watching: the level reset and the operation started with
    /// the selected car.</item>
    /// <item>RESULTS: after extraction, Confirm shows the campaign results (both operations) and the next choices.</item>
    /// </list>
    /// </summary>
    public sealed class HarborCampaign : MonoBehaviour
    {
        public enum Mode { Arrival, Trailer }

        private GameContext context;
        private CampaignSession campaign;
        private HarborRainEntry entry;
        private HarborRainMission mission;
        private CinematicCamera cam;
        private PortalRift portal;
        private bool skip, confirm;
        private Coroutine running;

        public Mode Kind { get; private set; }
        /// <summary>Current shot / stage (validation and review).</summary>
        public string Shot { get; private set; } = "";
        public bool WaitingForConfirm { get; private set; }
        public bool Finished { get; private set; }
        public bool ResultsShown { get; private set; }

        public static HarborCampaign Create(HarborRainEntry harborEntry, GameContext gameContext, Mode mode)
        {
            var go = new GameObject("HarborCampaign");
            go.transform.SetParent(harborEntry.transform, false);
            var c = go.AddComponent<HarborCampaign>();
            c.context = gameContext;
            c.campaign = gameContext.Campaign;
            c.entry = harborEntry;
            c.mission = harborEntry.Mission;
            c.Kind = mode;
            gameContext.Controls.Menu.Skip.performed += c.OnSkip;
            gameContext.Controls.Menu.Confirm.performed += c.OnConfirm;
            gameContext.Controls.Driving.Interact.performed += c.OnConfirm;
            gameContext.Controls.Menu.Enable();
            c.running = c.StartCoroutine(mode == Mode.Arrival ? c.Arrival() : c.Trailer());
            if (mode == Mode.Trailer)
            {
                c.mission.Extracted += c.OnExtracted;
                c.mission.CampaignContinue = c.ShowResults;
                c.mission.CampaignPause = c.OnPause;
            }
            return c;
        }

        private void OnDestroy()
        {
            if (context != null)
            {
                context.Controls.Menu.Skip.performed -= OnSkip;
                context.Controls.Menu.Confirm.performed -= OnConfirm;
                context.Controls.Driving.Interact.performed -= OnConfirm;
            }
            if (mission != null)
            {
                mission.Extracted -= OnExtracted;
                mission.CampaignContinue = null;
                mission.CampaignPause = null;
            }
            if (cam != null) Destroy(cam.gameObject);
            if (portal != null) Destroy(portal.gameObject);
        }

        private void OnSkip(InputAction.CallbackContext _) => skip = true;
        private void OnConfirm(InputAction.CallbackContext _) => confirm = true;

        /// <summary>Validation: the same as pressing Skip.</summary>
        public void Skip() => skip = true;
        /// <summary>Validation: the same as pressing Confirm.</summary>
        public void Confirm() => confirm = true;

        private bool Tapped() => Pointer.current != null && Pointer.current.press.wasPressedThisFrame;

        private VehicleController Car => entry.Driving.PlayerVehicle;
        private CampaignOverlay Overlay => campaign.Overlay;

        // ---------------- Arrival ----------------

        private IEnumerator Arrival()
        {
            var def = campaign.Definition;
            var car = Car;
            var spawn = entry.Driving.SpawnPoint;
            Vector3 fwd = spawn != null ? spawn.Rotation * Vector3.forward : car.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 ground = spawn != null ? spawn.Position : car.transform.position;
            // The white surge from the city carries over the cut and clears on the port.
            Overlay.SetFlash(1f);
            mission.SetHudVisible(false);
            Overlay.SetLetterbox(true);
            campaign.Progress.MarkPortalCompleted();

            var hold = new ScriptedDrivingInput { Current = new DrivingInput { Brake = 1f, Handbrake = true } };
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(hold);
            car.gameObject.SetActive(false);
            Vector3 portalPos = ground + fwd * 4f;
            portal = PortalRift.Create(def, portalPos, Quaternion.LookRotation(-fwd, Vector3.up),
                                       new AudioMixerConfigRef(context.Config.AudioMixer != null ? context.Config.AudioMixer.Sfx : null));
            portal.SetOpen(1f);
            cam = CinematicCamera.Create("ArrivalCamera", 48f);
            Vector3 shotA = portalPos + fwd * 17f + right * 5.5f + Vector3.up * 1.4f;
            cam.Pose(shotA, portal.Centre - Vector3.up * 1.6f);
            var mood = FindAnyObjectByType<HarborMusic>();
            if (mood != null) { mood.Level = 0.55f; mood.Begin(); }

            Shot = "surge";
            // Let the loading fade clear, then the white.
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { Overlay.SetFlash(1f - t / 0.6f); yield return null; }
            Overlay.SetFlash(0f);
            Overlay.SetHint("SKIP  ·  SPACE / START");

            // The car comes through: nose at the plane, moving, so it emerges out of the opening.
            Shot = "emerge";
            car.Teleport(portalPos - fwd * 2.3f + Vector3.up * 0.1f, Quaternion.LookRotation(fwd, Vector3.up));
            car.gameObject.SetActive(true);
            if (car.Body != null) car.Body.linearVelocity = fwd * 15f;
            var roll = new ScriptedDrivingInput { Current = new DrivingInput { Throttle = 0.25f } };
            foreach (var r in car.GetComponentsInChildren<IVehicleInputReceiver>()) r.SetInputSource(roll);
            portal.Surge();
            portal.PlayArrive();
            cam.Shake(0.9f);
            for (float t = 0f; t < 2.4f && !skip; t += Time.unscaledDeltaTime)
            {
                if (t > 1.1f) roll.Current = new DrivingInput { Brake = 0.7f };
                cam.Pose(shotA, Vector3.Lerp(portal.Centre - Vector3.up * 1.6f, car.transform.position + Vector3.up * 0.8f, Mathf.SmoothStep(0f, 1f, t / 1.4f)));
                yield return null;
            }
            roll.Current = new DrivingInput { Brake = 1f, Handbrake = true };

            // Rise over the port while the rift closes behind the car.
            Shot = "reveal";
            float line = campaign.Speak("arrival");
            Vector3 port = PortCentre();
            Vector3 riseFrom = car.transform.position - fwd * 6f + right * 2.5f + Vector3.up * 2.2f;
            Vector3 riseTo = car.transform.position - fwd * 34f - right * 10f + Vector3.up * 46f;
            float rise = Mathf.Max(6.5f, line + 1f);
            for (float t = 0f; t < rise && !skip; t += Time.unscaledDeltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / rise);
                portal.SetOpen(1f - Mathf.InverseLerp(0.5f, 3f, t));
                cam.Pose(Vector3.Lerp(riseFrom, riseTo, u), Vector3.Lerp(car.transform.position + Vector3.up, port, Mathf.SmoothStep(0f, 1f, u * 1.4f)), Mathf.Lerp(50f, 42f, u));
                Overlay.SetCaption("KOWLOON BAY CONTAINER PORT", t > 1.5f ? "HARBOR RAIN" : null, Mathf.InverseLerp(1.5f, 2.3f, t));
                yield return null;
            }
            if (skip) cam.Pose(riseTo, port, 42f);
            skip = false;
            portal.SetOpen(0f);
            Overlay.SetCaption(null, null);
            Overlay.SetHint(null);

            // The new operation: the real Night Run result, the unlock, the brief.
            Shot = "unlock";
            campaign.Progress.UnlockHarborVehicles();
            var p = campaign.Progress.State;
            var cars = new List<string>();
            foreach (var v in def.Level2Vehicles) if (v != null) cars.Add(v.DisplayName.ToUpperInvariant());
            string result = p.nightRunGrade == "DEV" ? "DEVELOPMENT ENTRY (NIGHT RUN NOT PLAYED)" :
                $"QUALIFIED  ·  P{p.nightRunPosition} OF {p.nightRunCrews}  ·  GRADE {p.nightRunGrade}  ·  {FormatTime(p.nightRunTime)}";
            var lines = new List<string>
            {
                result,
                "Your performance has attracted the attention of a new contact.",
                "You have been selected for another operation.",
                "NEW OPERATION UNLOCKED: HARBOR RAIN",
                "The target is moving through a secured industrial port. Intercept the cargo, evade port security, and reach extraction."
            };
            if (cars.Count > 0) lines.Add($"▸ HARBOR RAIN CARS UNLOCKED: {string.Join("  ·  ", cars)}");
            Overlay.ShowCard("OPERATION NIGHT RUN: COMPLETE", "A NEW OPERATION", lines, "PREPARE YOUR CREW   ·   ENTER / A / TAP", 0.9f);
            for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime) yield return null;
            campaign.Speak("unlock");
            confirm = false;
            WaitingForConfirm = true;
            Debug.Log($"[Campaign] arrival: unlock card ({result}); waiting for Confirm");
            float idle = 0f;
            while (true)
            {
                idle += Time.unscaledDeltaTime;
                cam.Pose(riseTo + right * Mathf.Sin(idle * 0.05f) * 6f, port, 42f);
                if (confirm || skip || Tapped())
                {
                    if (Overlay.CardRevealed) break;
                    Overlay.RevealCardNow();
                    confirm = skip = false;
                }
                yield return null;
            }
            WaitingForConfirm = false;
            Finished = true;
            Overlay.StopVoice();
            campaign.Step = CampaignStep.Level2Select;
            Debug.Log("[Campaign] arrival done → Harbor Rain car selection");
            context.Flow.GoToCarSelect();
        }

        // ---------------- Trailer ----------------

        private struct TrailerShot
        {
            public string Id, Kicker, Title, Line;
            public Func<float, (Vector3 pos, Vector3 look, float fov)> Pose;
            public Action<float> Tick;
        }

        private IEnumerator Trailer()
        {
            var car = Car;
            campaign.Progress.SetLevel2Vehicle(context.Session.SelectedVehicle != null ? context.Session.SelectedVehicle.Id : "");
            cam = CinematicCamera.Create("TrailerCamera", 45f);
            mission.SetHudVisible(false);
            Overlay.SetLetterbox(true);
            Overlay.SetHint("SKIP  ·  SPACE / START");
            var shots = BuildShots(car);
            Debug.Log($"[Campaign] Harbor Rain trailer: {shots.Count} shots with {(context.Session.SelectedVehicle != null ? context.Session.SelectedVehicle.Id : "?")}");
            foreach (var s in shots)
            {
                if (skip) break;
                Shot = s.Id;
                float len = campaign.Speak(s.Line);
                float dur = Mathf.Max(4.4f, len + 0.7f);
                for (float t = 0f; t < dur && !skip; t += Time.unscaledDeltaTime)
                {
                    float u = t / dur;
                    var (pos, look, fov) = s.Pose(u);
                    cam.Pose(pos, look, fov);
                    s.Tick?.Invoke(t);
                    Overlay.SetCaption(s.Kicker, s.Title, Mathf.Min(Mathf.InverseLerp(0.3f, 0.9f, t), Mathf.InverseLerp(dur, dur - 0.5f, t)));
                    // A quick dip between shots.
                    Overlay.SetFade(Mathf.Max(Mathf.InverseLerp(0.25f, 0f, t), Mathf.InverseLerp(dur - 0.2f, dur, t)) * 0.9f);
                    yield return null;
                }
            }
            Overlay.SetFade(0f);
            Overlay.SetCaption(null, null);

            if (!skip)
            {
                // Briefing: the operation's real objectives and the controls for this device.
                Shot = "briefing";
                var lines = new List<string>();
                var steps = HarborRainMission.Objectives;
                for (int i = 0; i < steps.Count; i++) lines.Add($"{i + 1}.  {steps[i]}");
                lines.Add(Controls());
                Overlay.ShowCard("OPERATION KX-7  ·  HARBOR RAIN  ·  " + (context.Session.SelectedVehicle != null ? context.Session.SelectedVehicle.DisplayName.ToUpperInvariant() : ""),
                                 "MISSION BRIEFING", lines, "START OPERATION   ·   ENTER / A / TAP", 0.22f, new Color(1f, 0.69f, 0.25f));
                float len = campaign.Speak("trailer_briefing");
                confirm = false;
                float end = Time.unscaledTime + Mathf.Max(len + 3.5f, 9f);
                var (pos, look, fov) = shots[^1].Pose(1f);
                while (!skip && Time.unscaledTime < end)
                {
                    cam.Pose(pos, look, fov);
                    if (confirm || Tapped()) break;
                    yield return null;
                }
            }
            EndTrailer();
        }

        private void EndTrailer()
        {
            Shot = "done";
            Overlay.ClearAll();
            if (cam != null) { Destroy(cam.gameObject); cam = null; }
            foreach (var u in mission.Police != null ? mission.Police.Units : new List<HarborPolice.Unit>())
                if (u.Kit != null) u.Kit.Set(HarborPoliceCar.Lights.Off, false, false);
            mission.TrailerLockdown(false, 0f);
            mission.TrailerHighlightBay(false);
            mission.TrailerAlarm(false);
            mission.SetHudVisible(true);
            campaign.Step = CampaignStep.Level2;
            Finished = true;
            // Watched or skipped, the operation starts from the same reset state with the selected car.
            mission.StartCampaignOperation();
            Debug.Log($"[Campaign] trailer {(skip ? "skipped" : "finished")} → operation started ({mission.Current}) with {(Car != null ? Car.name : "?")}");
            skip = confirm = false;
        }

        private List<TrailerShot> BuildShots(VehicleController car)
        {
            Vector3 carPos = car != null ? car.transform.position : Vector3.zero;
            Vector3 cf = car != null ? Flat(car.transform.forward) : Vector3.forward, cr = Vector3.Cross(Vector3.up, cf);
            int bay = Mathf.Clamp(mission.TargetBay, 0, Mathf.Max(0, mission.BayContainers.Length - 1));
            Vector3 box = mission.BayContainers.Length > 0 && mission.BayContainers[bay] != null ? mission.BayContainers[bay].position : PortCentre();
            Vector3 gate = mission.SecurityGatePoint != null ? mission.SecurityGatePoint.position : box;
            Vector3 port = PortCentre();
            Vector3 toPort = Flat(port - carPos), side = Vector3.Cross(Vector3.up, toPort);
            Vector3 coast = mission.CoastPoint != null ? mission.CoastPoint.position : port;
            Vector3 wave = coast;
            if (mission.Waves != null && mission.Waves.Sections.Length > 0 && mission.Waves.Sections[0].breaker != null) wave = mission.Waves.Sections[0].breaker.transform.position;
            Vector3 along = Flat(wave - coast).sqrMagnitude > 1f ? Flat(wave - coast) : side;
            Vector3 police = mission.Police != null && mission.Police.Units.Count > 0 && mission.Police.Units[0].Car != null ? mission.Police.Units[0].Car.transform.position : gate;
            Vector3 boxAway = Flat(box - gate).sqrMagnitude > 1f ? Flat(box - gate) : toPort;
            string carName = context.Session.SelectedVehicle != null ? context.Session.SelectedVehicle.DisplayName.ToUpperInvariant() : "YOUR CAR";
            bool alarmStarted = false, waveStarted = false;

            return new List<TrailerShot>
            {
                new()
                {
                    Id = "port", Kicker = "KOWLOON BAY  ·  02:47  ·  STORM WARNING", Title = "THE PORT", Line = "trailer_port",
                    Pose = u => (carPos - toPort * 40f + side * 35f + Vector3.up * (62f - 8f * u) + toPort * 40f * u, port, 40f)
                },
                new()
                {
                    Id = "target", Kicker = "CONTAINER KX-7  ·  FALSE MANIFEST", Title = "THE TARGET", Line = "trailer_target",
                    // High over the neighbouring stacks (the review camera's proven view of the bay), easing in.
                    Pose = u => (box + Vector3.Lerp(new Vector3(30f, 16f, -34f), new Vector3(22f, 12f, -26f), u), box + Vector3.up * 3f, 48f),
                    Tick = t => { if (t < 0.1f) mission.TrailerHighlightBay(true); }
                },
                new()
                {
                    Id = "crew", Kicker = carName + "  ·  CONTROL  ·  OVERWATCH", Title = "THE CREW", Line = "trailer_crew",
                    Pose = u => (carPos + cf * Mathf.Lerp(7f, 4.6f, u) + cr * Mathf.Lerp(3.6f, -1.8f, u) + Vector3.up * Mathf.Lerp(0.8f, 1.2f, u), carPos + Vector3.up * 0.7f, 38f),
                    Tick = t => { if (t < 0.1f && car != null && car.TryGetComponent(out VehicleLights l)) l.SetHeadlights(true); }
                },
                new()
                {
                    Id = "security", Kicker = "CAMERAS  ·  BADGE GATES  ·  PATROLS", Title = "SECURITY", Line = "trailer_security",
                    Pose = u => (gate - boxAway * 16f + Vector3.Cross(Vector3.up, boxAway) * Mathf.Lerp(8f, 3f, u) + Vector3.up * Mathf.Lerp(3f, 5f, u), gate + Vector3.up * 2.5f, 44f)
                },
                new()
                {
                    Id = "heist", Kicker = "GANTRY CRANE  ·  TWISTLOCKS  ·  SEAL SIGNAL", Title = "THE HEIST", Line = "trailer_heist",
                    // The gantry over the restricted bays (the review camera's proven framings of the crane).
                    Pose = u => (Vector3.Lerp(new Vector3(140f, 34f, 70f), new Vector3(150f, 30f, 62f), u), Vector3.Lerp(new Vector3(225f, 12f, 160f), box + Vector3.up * 6f, u), 56f)
                },
                new()
                {
                    Id = "alarm", Kicker = "ALARM  ·  LOCKDOWN  ·  PORT POLICE", Title = "THE CONSEQUENCES", Line = "trailer_alarm",
                    Pose = u => u < 0.55f
                        ? (Vector3.Lerp(new Vector3(80f, 50f, 20f), new Vector3(95f, 44f, 34f), u / 0.55f), new Vector3(225f, 4f, 160f), 58f)
                        : (police + new Vector3(4.5f, 1.4f, 5.5f), police + Vector3.up * 0.8f, 40f),
                    Tick = t =>
                    {
                        mission.TrailerLockdown(true, t);
                        if (!alarmStarted)
                        {
                            alarmStarted = true;
                            mission.TrailerAlarm(true);
                            if (mission.Police != null)
                                foreach (var unit in mission.Police.Units) if (unit.Kit != null) unit.Kit.Set(HarborPoliceCar.Lights.Respond, false, true);
                        }
                    }
                },
                new()
                {
                    Id = "escape", Kicker = "STORM SURGE  ·  COAST ROAD  ·  EXTRACTION", Title = "THE ESCAPE", Line = "trailer_escape",
                    Pose = u => (coast - along * Mathf.Lerp(26f, 14f, u) + Vector3.up * Mathf.Lerp(4f, 6f, u) + Vector3.Cross(Vector3.up, along) * 6f, coast + along * 30f, 50f),
                    Tick = t =>
                    {
                        if (!waveStarted && mission.Waves != null) { waveStarted = true; mission.Waves.Begin(); mission.Waves.Trigger(0); mission.TrailerAlarm(false); }
                        mission.TrailerLockdown(false, 0f);
                    }
                },
            };
        }

        private Vector3 PortCentre()
        {
            int bay = Mathf.Clamp(mission.TargetBay, 0, Mathf.Max(0, mission.ShipmentBays.Length - 1));
            if (mission.ShipmentBays.Length > 0 && mission.ShipmentBays[bay] != null) return mission.ShipmentBays[bay].position + Vector3.up * 8f;
            return entry.transform.position;
        }

        private static string Controls() => TouchControls.Active
            ? "CONTROLS   ◀ ▶ STEER   ·   GAS / BRAKE   ·   ACTION BUTTON  INTERACT   ·   ❚❚  PAUSE"
            : "CONTROLS   W S / A D DRIVE   ·   E INTERACT   ·   TAB DEVICE   ·   R RECOVER   ·   ESC PAUSE   (GAMEPAD  A  INTERACT  ·  START  PAUSE)";

        // ---------------- Results and pause ----------------

        private void OnExtracted(string grade, float seconds)
        {
            campaign.Progress.RecordHarbor(grade, seconds);
            Debug.Log($"[Campaign] Harbor Rain complete: grade {grade}, {FormatTime(seconds)}");
        }

        /// <summary>After the Harbor Rain debrief: both operations, then where to go next.</summary>
        public void ShowResults()
        {
            if (ResultsShown) return;
            ResultsShown = true;
            campaign.Step = CampaignStep.Complete;
            if (mission.Hud != null) mission.Hud.ShowResults(false, null, null);
            mission.SetHudVisible(false);
            var p = campaign.Progress.State;
            var lines = new List<string>
            {
                "▸ OPERATION 1  ·  NIGHT RUN",
                p.nightRunGrade == "DEV" ? "Not played in this save (development entry)." :
                    $"Qualified  ·  P{p.nightRunPosition} of {p.nightRunCrews}  ·  grade {p.nightRunGrade}  ·  {FormatTime(p.nightRunTime)}  ·  {p.level1Vehicle}",
                "▸ OPERATION 2  ·  HARBOR RAIN",
                $"KX-7 extracted  ·  grade {p.harborGrade}  ·  {FormatTime(p.harborTime)}  ·  {p.level2Vehicle}",
                "Both jobs are done. The city and the harbour."
            };
            Overlay.ShowCard("NEON RIFT  ·  CAMPAIGN RESULTS", "CAMPAIGN COMPLETE", lines, "CONTINUE   ·   ENTER / A / TAP", 0.6f, new Color(0.93f, 0.25f, 0.82f));
            campaign.Speak("complete");
            StartCoroutine(ResultChoices());
        }

        public bool ResultsWaiting { get; private set; }

        private IEnumerator ResultChoices()
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime) yield return null;
            confirm = skip = false;
            ResultsWaiting = true;
            while (true)
            {
                if (confirm || skip || Tapped())
                {
                    if (Overlay.CardRevealed) break;
                    Overlay.RevealCardNow();
                    confirm = skip = false;
                }
                yield return null;
            }
            ResultsWaiting = false;
            Overlay.HideCard();
            Overlay.ShowChoices("WHAT NEXT?", new List<(string, Action)>
            {
                ("REPLAY HARBOR RAIN", () => { Overlay.HideCard(); campaign.Step = CampaignStep.Level2; context.Flow.StartMission(); }),
                ("CHANGE CAR FOR HARBOR RAIN", () => { Overlay.HideCard(); campaign.Step = CampaignStep.Level2Select; context.Flow.GoToCarSelect(); }),
                ("PLAY NIGHT RUN", () => { Overlay.HideCard(); campaign.Step = CampaignStep.Level1; context.Flow.GoToCarSelect(); }),
                ("TITLE SCREEN", () => { Overlay.HideCard(); context.Flow.GoToFrontend(); })
            });
        }

        private bool pauseHooked;

        private void OnPause(bool on)
        {
            var menu = Overlay.Menu;
            if (!pauseHooked) { pauseHooked = true; menu.BackFromRoot += () => mission.ResumeFromPause(); }
            if (!on) { menu.Close(); return; }
            menu.Open("PAUSED", new List<OptionsMenu.Row>
            {
                menu.Button("RESUME", () => mission.ResumeFromPause()),
                menu.Button("RESTART HARBOR RAIN", () => { mission.ResumeFromPause(); context.Flow.StartMission(); }),
                menu.Button("CONTROLS", menu.PushControls),
                menu.Button("SETTINGS", menu.PushSettings),
                menu.Button("CHANGE CAR", () => { mission.ResumeFromPause(); campaign.Step = CampaignStep.Level2Select; context.Flow.GoToCarSelect(); }),
                menu.Button("QUIT TO TITLE", () => { mission.ResumeFromPause(); context.Flow.GoToFrontend(); })
            });
        }

        // ---------------- Helpers ----------------

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static string FormatTime(float s)
        {
            int cs = Mathf.RoundToInt(Mathf.Max(0f, s) * 100f);
            return $"{cs / 6000}:{cs % 6000 / 100:00}.{cs % 100:00}";
        }
    }
}
