# 40 · Testing and Validation

> [Index](00_INDEX.md) · Previous: [39 Performance](39_PERFORMANCE.md) · Next: [41 Input Playtesting](41_INPUT_PLAYTESTING.md)

## Automated tests (EditMode)

- **Location:** `Assets/_NeonRift/Scripts/Tests/EditMode/` (assembly `NeonRift.Tests.EditMode`).
- **Count:** 98 test methods in 11 files. Physics tests run once per catalog car (`[TestCaseSource]`), giving **125 test cases**. The last recorded run (`Logs/TestResults-EditMode.txt`, local, not committed) shows `passed=125 failed=0 skipped=0`.
- **Run them:** **Window ▸ General ▸ Test Runner ▸ EditMode**, or **Neon Rift ▸ Tests ▸ Run EditMode Tests (write report)**. The second option uses `Scripts/Editor/TestRunReporter.cs` and writes a plain-text report.
- **Note:** after the tests the editor is left on an untitled scene, so reopen `NightRun.unity` before pressing Play.

| Test file | Methods | What it covers |
|---|---|---|
| `FoundationTests.cs` | 5 | Catalog duplicate ids and missing prefabs, lookup and default, session readiness, result record/clear, project wiring (`ProjectValidator`) |
| `InputTests.cs` | 8 (+ fixture setup) | Keyboard throttle, brake, steer, combined; handbrake and interact; gamepad driving and buttons; Menu map disable leaves Driving on (an `InputTestFixture`) |
| `VehicleModelTests.cs` | 16 | Tyre curve, slip direction, peak and sliding, combined slip, friction ellipse, load sensitivity, surface grip; steering lock and Ackermann; gearbox shifts and hysteresis; reverse selection; launch clutch; handbrake clutch; profile validity; AMG GT3 excluded and NC licences flagged |
| `VehiclePhysicsTests.cs` | 11 (× cars) | Spawn at ride height without jitter, acceleration band, top speed, straight braking, cornering grip, full-lock step steer (slides, no spin), speed bumps, wall impact and recovery, slope hold, validation-route lap, spawn applies the profile (on the deterministic `VehicleTestBench`) |
| `VehicleAudioTests.cs` | 12 | Engine model equal-power and continuity; tyre squeal; profiles valid; loops seamless, one-shots clean; mixer groups, snapshots and volumes; `VehicleAudio` follows telemetry without restarting loops; skid follows slip; score stems sample-locked; `ScoreMix` rules and grid timing; `MasterLimiter` |
| `CityTests.cs` | 13 | Network valid and connected; city size; every block has a zone and pavement; heroes avoid roads; spawn → core path; closed gates re-route; remembered blocked edges; skyway; racing line slows for corners; rubber band bounded; rivals are the cars not picked; city life set up; traffic cars physical and ignoring each other |
| `MissionTests.cs` | 11 | Objectives advance and fire events; heat → Alert and shorter timer; heat clamp and minimum timer; timer failure; untimed objectives never fail; Night Run data valid; **scene provides every target and reaction** (including rival tasks and markers); grade; records |
| `InteractionTests.cs` | 6 | Stages in order with events; timing hits and misses, heat, lockout; interference stall and rollback; leaving cancels to the checkpoint; cues fire once; objective start delay |
| `IntroTests.cs` | 5 | First-launch policy; Always/Never; Video without a clip falls back; config points at the intro; NightRun scene has a wired, inert intro |
| `MinimapTests.cs` | 5 | North-up / east-right; projection independent of heading; round trip; compass headings; player arrow direction |
| `CarSelectTests.cs` | 2 | Catalog cars have Car Select data; CarSelect scene is a wired garage |

## Play Mode validation tools (editor)

| Tool | File | Purpose |
|---|---|---|
| `NightRunValidator` | `Scripts/Gameplay/Mission/NightRunValidator.cs` | Plays the real mission through the player's interfaces: the autopilot drives a `DrivingRoute`, stops at the interactable in focus, holds Interact until it completes. Scenarios: `BoulevardInAlleyOut`, `AlleyInExpresswayOut`, `TraceTimeout`, `AlleyInFailRetry` (`GateTrick` misses on purpose). Reports a timeline of objectives, security, heat, events, collisions and `RivalDirector.DescribeAi()` |
| `MissionPlaytest` | `Scripts/Editor/Validation/MissionPlaytest.cs` | Starts a scenario, captures the Game view after chosen world events and stages (`"event@delay"`, `"stage:LABEL@delay"`, `rival0:` / `aerial:` prefixes), writes `Logs/Playtest/<prefix>_report.txt` |
| `MissionSeries` | `Scripts/Editor/Validation/MissionSeries.cs` | Several scenarios back to back via the normal retry path → `Logs/Playtest/<prefix>_series.txt` (player collisions, rival contacts with positions, reversals, resets, rivals finished) |
| `PlaytestCapture` | `Scripts/Editor/Validation/PlaytestCapture.cs` | Timed captures from the editor loop (used for Car Select) |
| `IntroCapture` | `Scripts/Editor/Intro/IntroCapture.cs` | Captures the intro at given Timeline times; writes `_done.txt` |
| `LookDevShots` | `Scripts/Editor/District/LookDevShots.cs` | Fixed viewpoints for look comparisons |
| `InputPlaytest` | `Scripts/Editor/Validation/InputPlaytest.cs` | Real keyboard events in Play Mode ([41](41_INPUT_PLAYTESTING.md)) |
| `VehicleAudioValidator` | `Scripts/Gameplay/VehicleAudioValidator.cs` | Scripted audio run with listener recording and signal analysis |
| `VehiclePerformanceProbe`, `VehicleTestBench` | `Scripts/Editor/Vehicles/` | Deterministic physics measurements |
| `RouteAutopilot`, `VehicleTelemetryLog`, `VehicleDebugHud` | `Scripts/Gameplay/` | Validation laps, per-section telemetry, F3 overlay |
| `RoutePerf`, `GpuBench`, `PerfBuild` | `Scripts/Editor/Validation/` | Performance ([39](39_PERFORMANCE.md)) |
| `ProjectValidator` | `Scripts/Editor/ProjectValidator.cs` | **Neon Rift ▸ Validate Project**: config, catalogs, build scenes; warnings for non-commercial assets |

## Test → purpose → location (selected highlights)

| Test | Purpose | Location |
|---|---|---|
| `MissionTests.NightRunSceneProvidesEveryTargetAndReaction` | The generated scene contains every target, reaction, gate, rival marker and work FX the data names | `Tests/EditMode/MissionTests.cs:157` |
| `VehiclePhysicsTests.CompletesValidationRouteLap_OnLine_AndUpright` | Every catalog car laps the 1.55 km route on the autopilot | `Tests/EditMode/VehiclePhysicsTests.cs:142` |
| `CityTests.ClosedGates_ChangeTheRoute` | Navigation respects gates | `Tests/EditMode/CityTests.cs:86` |
| `IntroTests.NightRunScene_HasAWiredIntro` | Intro wiring is complete and inert in missions | `Tests/EditMode/IntroTests.cs:75` |
| `MinimapTests.Projection_DoesNotDependOnPlayerHeading` | North-up guarantee | `Tests/EditMode/MinimapTests.cs:29` |
| `VehicleAudioTests.VehicleAudio_FollowsTelemetry_WithoutRestartingLoops` | Loops are never restarted | `Tests/EditMode/VehicleAudioTests.cs:142` |

## Recorded validation runs

Recorded in `Docs/NightRun.md`, `Docs/City.md`, `Docs/Intro.md` and `Docs/Audio.md`: e.g. rival regression series with 0 contacts after the intent rules; final rival-heist runs (VEX 124.9 s vs the player at about 125 s; the player won at 127.8 s); `TraceTimeout` failing as designed at 71.3 s; the intro played to the title and Skip and Confirm verified; the departure heading held at 180°.

## Unity console validation

Systems log with prefixes (`[GameFlow]`, `[Mission]`, `[Rivals]`, `[Input]`, `[Intro]`, `[CarSelect]`, `[Spawn]`, `[NightRun]`). `[Rivals] contact …` lines record the geometry of every rival contact for diagnosis.
