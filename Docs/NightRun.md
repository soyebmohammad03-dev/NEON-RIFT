# Night Run

The first mission. Infiltrate Sector 7, steal the Data Core, survive the lockdown and escape through the Rift Gate, racing two rival crews. Sector 7 now sits at the centre of a five-district city: see [City.md](City.md).

Rebuild everything (scene, textures, meshes, mission data, audio, probes): **Neon Rift ▸ Night Run ▸ Build Night Run (district + mission)**. The scene is generated; change the builder, not the scene.

## Mission flow

| # | Objective (data) | Target | Security | What happens |
|---|---|---|---|---|
| 1 | `reach_core`: INFILTRATE THE DATA CORE FACILITY | zone `core_compound` | Calm (Alert if heat) | Boulevard or alley. Entering fires `core.arrive`: floods sweep, compound strips go amber, the compound cameras go live, a red scan ring hunts up the column, holograms turn amber, the facility hum starts |
| 2 | `disable_security`: DISABLE FACILITY SECURITY | interactable `core_security_terminal` (kiosk west of the core) | — | CONNECTING (auto) → AUTHENTICATING (hold E) → BYPASSING (timing, 3 misses = lockout + heat) → SECURITY OVERRIDE. Fires `core.security.off`: cameras offline (they turn to watch the core), floods stand down, the chamber opens |
| 3 | `hack_core`: EXTRACT THE DATA CORE | interactable `core_uplink` (ring round the core) | — | CONNECT EXTRACTION DEVICE → INITIALIZE → STABILIZE DATA LINK (timing) → DATA EXTRACTION (14 s, interference at 38 % and 74 %) → EXTRACTION COMPLETE. Fires `core.acquired`: **DATA ACQUIRED** banner and stinger |
| 4 | `escape`: ESCAPE TO THE RIFT GATE | zone `extraction` | Lockdown, 80 s trace (−25 s per 1.0 heat) | Starts 2.4 s after the data is acquired (`startDelay`) with `core.breached`, `lockdown`, `escape.start`: **SECURITY BREACH DETECTED** banner and breach stinger, the theft camera beat, gates seal, lights turn red from the core outward, sirens, lockdown grade |

Timeout fails the mission ("TRACE COMPLETE"). Results panel: **E** retry, **Esc** continue (car select).

## The Data Core heist

The multi-stage interactions are described in [Interactions.md](Interactions.md). The Data Core's presentation is `DataCoreChamber` (it replaced `DataCoreVisual`). It reads world events and the uplink's `InteractionRun`, and owns no rules.

- **Armoured sleeve:** six curved panels around the column with cyan seams. On `core.security.off` they unlock one after another (a small outward kick) and sink into the pedestal over 3.2 s. A hydraulic one-shot plays, the column's light floods out, and the rings spin up.
- **Machinery:** two arms on carriages that ride a rail round the pedestal to the car's side.
  - The connector arm (two-bone IK, elbow up) reaches for the roof during CONNECT, docks with a clunk, and lowers a glowing data cable onto the car.
  - The scanner arm holds a beam that sweeps the car front to back.
- **Extraction:**
  - Pulses travel core → arm → cable → car, from 2.5 to 16 per second as progress climbs.
  - The core shifts from cyan through hot white-blue to magenta.
  - Amber strobes start at 30 % and speed up.
  - Scan rings sweep faster, and the holograms show the stage strip and progress.
  - The 3D data-stream loop and the 2D tension bed build.
  - Interference: pulses freeze, and the link, cable, pulses and core flash red while the holograms tear.
  - The camera holds a slow, wide framing of the car and the core (`VehicleChaseCamera.SetFraming`) while the chamber opens and while data is pulled. The player keeps control of the car.
- **City reacting** (stage cues):
  - At 50 %, `grid.anomaly`: every traffic signal stutters amber and street screens within 420 m glitch to the warning.
  - At 80 %, `core.extract.trace`: the compound cameras reboot (they log heat again) and the compound beacons start spinning.
- **Acquired / breached:** the cable releases and the core collapses to an ember. 2.4 s later the breach turns it to the alarm colour, and the floods turn red.

| Facility alerted | Bypass (timing) | Chamber open, arm connecting |
|---|---|---|
| ![](Screenshots/Heist/core_arrive.jpg) | ![](Screenshots/Heist/stage_BYPASSING.jpg) | ![](Screenshots/Heist/stage_CONNECT_EXTRACTION_DEVICE.jpg) |

| Extraction | Interference | Counter-intrusion (80 %) |
|---|---|---|
| ![](Screenshots/Heist/core_extract_begin.jpg) | ![](Screenshots/Heist/interference.jpg) | ![](Screenshots/Heist/core_extract_trace.jpg) |

| Data acquired | Breach detected (theft beat) |
|---|---|
| ![](Screenshots/Heist/core_acquired.jpg) | ![](Screenshots/Heist/core_breached.jpg) |

**Validation (Play Mode, `MissionPlaytest` + `NightRunValidator`, BoulevardInAlleyOut, SLS AMG):** completed in 125.5 s with 0 collisions.
- Terminal: 4.8 s.
- Uplink: 20.9 s, with both interference events answered after a 0.8 s reaction delay.
- `grid.anomaly` and `core.extract.trace` fired on cue.
- 2.4 s beat, then lockdown, alley re-hack, Rift Gate.

The same run without the reaction delay (instant answers) completed too: three runs in all.

## Routes and consequences

```
            North Boulevard ───────────────┐
 W Avenue      │  Access Rd                 │ East Expressway
   (long,      │  [COMPOUND GATE]           │  [CHECKPOINT z 170]
    clean)     │   DATA CORE                │
               │   Service Alley            │
               │   [ALLEY GATE + terminal]  │
           Market Street ──────────────────┤
 spawn ▲                                    ▼ Rift Gate tunnel (extraction)
```

- **Long route in** (W Avenue → North Boulevard → Access Road), ≈ 910 m, wide and fast, no heat.
- **Short risky route in** (Market Street → Service Alley), ≈ 520 m, a 10 m-wide alley with a jersey-barrier chicane and a locked gate. Hacking it (five stages, about 5 s plus the bypass timing; see below) adds **35 % heat**.
- **Heat** raises security to Alert, shortens the escape trace (25 s per 1.0 heat, applied at the start and immediately for heat gained during the escape) and shortens checkpoint countdowns.
- **Lockdown** (data, `closeOn: lockdown`):
  - Alley gate slams shut (1 s warning). The terminal re-arms: hack again for +35 % heat.
  - Compound north gate seals in 12 s (−4 s per heat).
  - Expressway checkpoint seals in 30 s (−12 s per heat).
- **Escape options**: race north/east before the compound gate and checkpoint seal (fast, ≈ 1 km), or re-hack the alley (short, ≈ 700 m, costs heat). If both seal, the long west loop via W Avenue and Market Street is still open but tight on time.

## The alley gate hack

The gate controller is the same framework as the Data Core terminal ([Interactions.md](Interactions.md)). The definition is generic: its stage events use `{id}`, so any gate terminal can reuse it.

| Stage | Kind | What the world does |
|---|---|---|
| CONNECTING | Auto 0.9 s | `alley_gate_terminal.hack`: lock lamps and the lamp bar go amber and pulse, the bolts chatter in their seats, the gate status hologram works |
| AUTHENTICATING | Hold E 1.3 s | Kiosk screens scroll |
| BYPASSING SECURITY | Timing, 3 misses allowed | Each miss: +5 % heat (raises Alert), the screens tear, a denied buzz. Three misses: `alley_gate_terminal.lockout`, lamps flash red, 5 s lockout, +12 % heat |
| OVERRIDE | Auto 0.8 s | "Override accepted" |
| GATE RELEASE | Auto 1.1 s | `alley_gate_terminal.unlock`: four bolts withdraw into the housing with a ratchet and a pneumatic sigh, lamps turn green. On completion `alley.gate.open`, then the panel slides |

- **Cancelling:** driving off (over 10 km/h or out of the zone for 0.6 s) drops the link ("CONNECTION LOST"). `alley_gate_terminal.abort` returns the locks to sealed red, and the next attempt starts from CONNECTING.
- **Lockdown:** the gate slams. The lock system re-seats the bolts 2.6 s later with a latch clunk, and the terminal re-arms for another hack (+35 % heat).
- **Camera:** while the gate is hacked or releasing and the car is within 40 m, the camera rises and frames the gate. It stays near-straight behind the car so it does not swing into the alley walls.

| Bypass (timing) | Lockout after three misses | Drive-off abort |
|---|---|---|
| ![](Screenshots/Gate/bypass.jpg) | ![](Screenshots/Gate/lockout.jpg) | ![](Screenshots/Gate/abort.jpg) |

| Locks releasing | Re-hack in the lockdown |
|---|---|
| ![](Screenshots/Gate/release.jpg) | ![](Screenshots/Gate/lockdown_rehack.jpg) |

**Validation:** `NightRunValidator` scenario `AlleyInFailRetry` (Play Mode, real mission):
1. Three deliberate misses → lockout (+5 % ×3 and +12 % heat, security Alert).
2. 5 s wait → restart → drove off during BYPASSING → CONNECTION LOST and abort event.
3. Stopped → full hack → bolts released → gate open (+35 % heat).
4. Then the heist.

`BoulevardInAlleyOut` re-hacked the slammed gate during the escape with the new stages and completed (133.7 s, 0 collisions).

The `AlleyInFailRetry` run stalled later, on its expressway escape, after contacts with the rivals leaving the S7 car park. That is the rival issue handled in the three-car heist phase.

## Rival crews

The two catalog cars the player did not pick spawn in a column in the clear lane to the player's left on W Avenue (`RivalDirector`, driver profiles `Data/Racing/Racer_Vex` and `Racer_Kade`). They start about 3 s into the mission, usually while the player is still on the grid, so they must never be boxed in behind the player.

| Objective | Rivals (`rivalGoalId`, delay) |
|---|---|
| `reach_core` | Race to the S7 car park off the Access Road (`core_staging`, 0.6 s) and wait there |
| `hack_core` | Hold |
| `escape` | Race to the Rift Gate tunnel (`extraction`, 1.2 s) |

### Rival driving (`RacerDriver`)

Rivals drive through the same `IVehicleInputSource` → `VehicleController` path as the player. Nothing teleports them while they drive. Every 0.1 s each driver:

- sweeps five lateral lines ahead with sphere casts (walls, props, gates);
- **path occupancy:** walks its own upcoming path (the racing line at each candidate offset, so it is exact through corners) and finds the first point where another car's body (a 3.8 m segment) comes within 2.2 m. That distance becomes the line's clearance, and the line the car is actually on also caps its speed (braking-distance fail-safe);
- **side risk:** cars alongside (−6..+5 m along the route) rule out lines that would side-swipe them;
- **yield:** a car predicted to pass within 2.8 m in the next 1.6 s (closest approach) and ahead of us makes this driver match its speed for 0.5 s.

**Stuck:** when there is no progress while it is trying (or it waits behind something stationary for more than 3 s), the driver backs out with opposite lock, but never into a car behind it. Each reversal makes that road edge cost +250 m in the driver's own plans for 25 s, and from the second reversal it re-plans around the edge. After three failed reversals it resets: out of the player's view, onto its line a few metres back; in view, it is only righted in place. `RivalDirector.DescribeAi()` reports per-rival car contacts, reversals, resets and yields. `NightRunValidator` appends it to its report, and `[Rivals]` log lines name where and why a rival reversed.

Standings come from route distance to the current objective over the road graph. The HUD shows position, the gap to the next car and the field. Results show the finishing position (rivals already extracted are ahead). Rivals re-plan when gates change, so a sealed checkpoint sends them another way.

## New in the city

- **Harbor checkpoint** on Market Street east of the expressway seals 22 s after the theft (−8 s per heat).
- **Skyway gate**: closed at the start; the crew opens it when the escape starts (`escape.start`), giving an elevated route over the harbor that only exists during a lockdown.
- **Security cameras** (12, at the alley, compound, checkpoints, tunnel and skyway) arm once security is raised. Staying in view for 1.1 s logs +8 % heat.
- **Traffic signals** across the city flash red in a lockdown. Billboards and shelter screens switch to warnings with the lockdown wave.

## Architecture

| Piece | Where | Role |
|---|---|---|
| `MissionDefinition` + `ObjectiveDefinition` + `MissionAnnouncement` | Missions | Ordered objectives, targets, security level, time limits, world events, HUD messages. Pure data |
| `MissionProgress` | Missions | Pure C# state machine (objectives, heat, security, timer, fail/complete). Unit-tested |
| `MissionDirector` | Gameplay | Runs one attempt: binds scene components, routes zones/interactions into progress, drives HUD, mixer snapshots (Gameplay → Lockdown → Results), audio cues, results/retry |
| `MissionWorld` | Gameplay | Per-attempt hub handed to scene components: player, security, heat, world events (string ids), countdowns, announcements. No singletons |
| `IMissionWorldComponent` | Gameplay | Anything in the scene that takes part; found and bound by the director |
| `MissionZone` | Gameplay | Reach targets (trigger layer), beacon while current |
| `Interactable` + `InteractionDefinition` | Gameplay | Reusable "stop and work the device" mechanic: stages (auto, hold, timing, sustain), max speed, heat, lockout; raises/enables/disables/re-arms on world events |
| `SecurityBarrier` | Gameplay | Event-driven gates: countdown (heat-scaled) → strobe + klaxon → kinematic panels slide (push cars, never teleport) → slam |
| `SecurityLightGroup` | Gameplay | Kerb/crown neon per block; lockdown wave spreads from the theft at 140 m/s |
| `SecurityAlarm`, `SecurityPostEffects`, `DataCoreChamber` | Gameplay | Sirens + rotating beacons, lockdown colour grade, the Data Core heist presentation |
| `InteractionRun` + `TerminalDisplay` + `TerminalReadout` | Missions / Gameplay | Multi-stage interactions, device screens, HUD readout ([Interactions.md](Interactions.md)) |
| `MissionHud` + `MissionHud.uxml/.uss` | Gameplay / UI | Objective + distance, waypoint (edge-clamped), security state + heat, trace timer, seal countdowns, prompt + progress, toasts, banners, speed/gear/rpm, results |
| `MissionAudio` + `MissionAudioSet` | Gameplay | Ambience bed, hack loop, cues; routed into the existing mixer groups |
| `NightRunValidator` | Gameplay (dev) | Plays the real mission end to end through the input interfaces and reports a timeline |

Adding a mechanic: create an `InteractionDefinition` (or a new `IMissionWorldComponent`), give scene objects event ids, and reference those ids from mission data. No director changes.

## District (Sector 7)

- One asphalt ground plane; roads are the gaps between raised pavement blocks (kerb 15 cm), so intersections need no special geometry.
- Blocks are lined with procedural podium street walls (shopfront ground floor, lit upper floors, neon awning, crown strip, facade and blade signs, rooftop billboards and plant), combined into one mesh per material per block, box colliders per podium.
- Audited catalog buildings: Singapore Office ×3 and London Skyscraper ×1 as heroes on the pavement line (podiums shrink around them so the street wall stays closed), Asian Night City towers inside blocks, skyline towers and the skyline cluster beyond reach.
- Textures (wet asphalt, pavement, 3 facade variants, shopfronts, signage atlas with an in-house 5×7 pixel font, billboards, glow shapes) are generated deterministically by `DistrictTextures`.
- Props are prefabs with LOD culling: `PF_Prop_StreetLight`, `PF_Prop_StreetLightLit`, `PF_Prop_JerseyBarrier`, `PF_Prop_SecurityBeacon`.

## Lighting and performance budget

- Forward+ (PC renderer). Moonlight (one shadowed directional) + 63 unshadowed street spot lights (every other lamp) + ≈ 23 mission lights (core, floods, tunnel, gate/beacon lights mostly off until lockdown).
- Emissive windows, neon and signs carry most of the night look with bloom; 23 baked box-projected reflection probes give the wet roads their neon reflections.
- Static batching flags on all district geometry; one mesh per material per block; props cull via LOD groups; 4 small particle systems.
- Mobile path (later): bake GI/lightmaps for street lights and drop real-time lamps to a handful near the player; the data and scene structure don't change.

## Camera

`VehicleChaseCamera` (unchanged architecture) is tuned for the city in the scene: 6.2 m back, 2.05 m high, 60° base FOV +12° at top speed, 0.26 s look-ahead (max 8 m), sphere-cast collision against `Drivable` and `Environment`. Look-ahead now leads forward travel only (fades in over 0–3 m/s): with raw velocity, reversing pulled the aim point towards the camera and pitched it onto the roof.

## Camera and HUD (phase 7)

- **Cornering:** the chase camera banks into corners by 1.6° per g of lateral acceleration (max 3°, eased over 0.3 s, faded out below 8 m/s). It reads as weight, not tilt. Existing behaviour is unchanged: look-ahead, speed FOV, acceleration pull-back, collision, impact and speed shake.
- **Theft beat:** on `core.breached` (`MissionDirector` fields: event, 2.6 s, time scale 0.45), the camera swings 55° round and 3.4 m up on the far side of the car from the Data Core and aims between them; letterbox bars slide in; time eases to 0.45 and back. Everything runs on unscaled time and is restored on mission end. The driving HUD (objective, race board, speedo, minimap, waypoint) steps back during the beat; banner, toasts and the trace timer stay. Verified in Play Mode: time scale 0.45 during the beat, 1.00 afterwards.
- **Camera clipping:** food-cart and night-market canopies now have colliders (well above car height) so the camera no longer passes through them.
- **HUD readability:** soft dark backing behind the objective, race board and speedometer; a segmented 12-step RPM bar with a two-segment redline zone and a shift light.

| Driving HUD | Theft beat (blending in) |
|---|---|
| ![](Screenshots/CameraHud/hud_lowtown.jpg) | ![](Screenshots/CameraHud/theft_beat.jpg) |

**Rival fix during this phase:** in fast-timing runs the Terzo turned into the S7 car park across the scripted player's path, and once reversed out into it (2 of 5 runs had contacts up to 18.9 m/s). Rivals now give way to the player on any crossing course within their stopping horizon, and never reverse while a car is, or within a second will be, in a 6 × 10 m box behind them. The next four runs had 0 player collisions and 0 reversals (one rival-to-rival touch). None of them hit the fast ~102 s timing, so that exact case is improved but not re-verified.

**Still open:** the fast-timing (~102 s) boulevard run produced one more player/Terzo contact (18.1 m/s) after the crossing fix. It did not reproduce in four later runs, including one with the editor kept in the foreground. `RivalDirector` now logs every rival contact (`[Rivals] contact …`: position, closing speed, both speeds, angle, driver state) so the next occurrence can be diagnosed.

## Minimap (north-up)

The minimap used to be heading-up: `CityMinimap.ToMap` rotated the whole world by the car's heading, so the roads spun whenever the player turned. It is now **north-up**:

- `MinimapProjection` (pure math, unit-tested) maps world XZ to panel pixels. +Z (north) is up and +X (east) is right, centred on the player, and it has no heading input at all.
- The **player arrow** turns with the car's compass heading (`MinimapProjection.Heading`, 0 = north, clockwise). Rival markers are small orange arrows with their own headings. A white tick on the rim marks north.
- Roads, closed gates (red), the GPS route and the objective marker all use the same projection, so they never rotate. Off-map objectives stay clamped to the rim in their true direction.

Tests: `MinimapTests` (north-up/east-right, projection independent of heading at 0/−90/90/180/270, round trip, compass headings, arrow direction matches the world direction).

Play Mode (`InputPlaytest.RunMinimapScript`, real keyboard events): straight, left, right, then a sustained left turn. The headings logged were 0° → 250° → 267° → 128°. W Avenue stayed vertical on the map in every capture, and the objective's map pixel moved only with the car's position.

| Heading 250° | Heading 128° |
|---|---|
| ![](Screenshots/CityInfill/minimap_heading250.jpg) | ![](Screenshots/CityInfill/minimap_heading128.jpg) |

## Validation

- EditMode: `MissionTests` (state machine, heat/timer rules, failure, data validity, scene wiring).
- Play Mode: `NightRunValidator` scenarios — `AlleyInExpresswayOut`, `BoulevardInAlleyOut`, `TraceTimeout`. Start one from a tool with the scene playing:
  `FindAnyObjectByType<NightRunValidator>().Begin(FindAnyObjectByType<MissionSceneEntry>().Director, Scenario.AlleyInExpresswayOut)`; the report is logged as `[NightRunValidator]`.
  With an unfocused editor, enable `unity command set_autotick --enable true` (or focus Unity) or Play Mode barely ticks.

Results at the vertical-slice commit (SLS AMG, validation driver at 0.72 g corners); see the latest table below for the city build:

| Scenario | Result |
|---|---|
| `BoulevardInAlleyOut` | **Completed** in 103 s, 0 collisions. Core breach → lockdown → alley gate slammed → re-hacked (+35 % heat during the escape) → Rift Gate. Mixer Gameplay → Lockdown → Results; results panel; Retry through the UI reloads the mission |
| `TraceTimeout` | **Failed** as designed: trace 71.3 s (80 − 0.35 × 25), all three gates sealed on schedule, "TRACE COMPLETE — YOU WERE FOUND" |
| `AlleyInExpresswayOut` | Gate hack, Alert, breach, lockdown, compound gate escaped before sealing; the validation driver reached the expressway checkpoint ~3 s after it sealed and hit the closed barrier (the "lost the race" branch). A faster player makes it; the boulevard escape is the safe option |

Audio during lockdown: 6/6 sirens playing, ambience on the Ambience group, vehicle audio continuous. EditMode: 73/73.


### Rival AI validation (October 2026)

| Check | Result |
|---|---|
| `BoulevardInAlleyOut` ×2 (final code) | **Completed** both times (118 s), 0 player collisions. Rivals: **0 car contacts, 0 reversals, 0 resets**. Before the grid fix, three earlier runs completed too, but the Terzo reversed once at the start every time, boxed in behind the player |
| `AlleyInExpresswayOut` ×1 (final code; ×2 on earlier builds) | Rivals 0 contacts, 0 reversals. The validation driver still loses the expressway checkpoint race, as designed |
| Player parked in the rivals' lane on W Avenue (2.9, −180) | Both rivals passed at 109–170 km/h: 0 contacts, 0 reversals, 3 yields |
| Player parked across the apex of the W Ave → N Boulevard right turn (6, 300) | Before path occupancy: the GT3 touched it (1 contact). After: 0 contacts. The GT3 stopped 0.6 m short, reversed once and drove round; the Terzo went round without stopping |

After the city detail rebuild: `BoulevardInAlleyOut` ×3 all **Completed**. Two were clean. One faster run (103 s instead of 118 s) had the scripted player and the Terzo meet at the S7 car-park entrance (148, 256) while the Terzo was pulling in to park: 3 contacts (max 15.2 m/s), 1 Terzo reversal. This is **intermittent and timing-dependent**, and still open.

### City build validation (five-district city, rivals on)

| Check | Result |
|---|---|
| `BoulevardInAlleyOut` (SLS AMG) | **Completed** in 102.5 s. Alley re-hack +35 % heat; two camera detections in the alley (+8 % each, heat 0.51); compound gate, harbor checkpoint and expressway checkpoint sealed on schedule; finished **P1/3** |
| Rivals in that run | Raced W Avenue → North Boulevard → Access Road at up to 173 km/h, parked in the S7 car park, left 1.2 s after the theft, avoided the closing expressway checkpoint and re-routed via W Avenue and Market Street; no recoveries |
| `AlleyInExpresswayOut` | Lost the race at the expressway checkpoint, as before. With camera heat (0.59) it seals sooner (~23 s) |
| Title → Car Select → Night Run | Car 2/3 (SLS GT3) became the player with its own physics profile; SLS AMG and Terzo Millennio spawned as rivals with their own profiles and engine audio |
| EditMode | 83/83 (adds `CityTests`: network validity, connectivity, gate re-routing, skyway, racing line, rubber band, rival field; scene wiring for navigation, rivals, light budget) |
