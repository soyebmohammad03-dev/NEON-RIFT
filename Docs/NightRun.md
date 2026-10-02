# Night Run

The first mission. Infiltrate Sector 7, steal the Data Core, survive the lockdown and escape through the Rift Gate, racing two rival crews. Sector 7 now sits at the centre of a five-district city: see [City.md](City.md).

Rebuild everything (scene, textures, meshes, mission data, audio, probes): **Neon Rift ▸ Night Run ▸ Build Night Run (district + mission)**. The scene is generated; change the builder, not the scene.

## Mission flow

| # | Objective (data) | Target | Security | What happens |
|---|---|---|---|---|
| 1 | `reach_core` — REACH THE DATA CORE | zone `core_compound` | Calm (Alert if heat) | Cyan beacon on the core; choose boulevard or alley |
| 2 | `hack_core` — BREACH THE DATA CORE | interactable `core_uplink` | — | Stop on the uplink ring, hold **E** for 4 s. Rings spin up. Fires `core.breached`, `lockdown` |
| 3 | `escape` — ESCAPE TO THE RIFT GATE | zone `extraction` | Lockdown, 80 s trace (−25 s per 1.0 heat) | Gates seal, lights turn red from the core outward, sirens, lockdown grade |

Timeout fails the mission ("TRACE COMPLETE"). Results panel: **E** retry, **Esc** continue (car select).

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
- **Short risky route in** (Market Street → Service Alley), ≈ 520 m, a 10 m-wide alley with a jersey-barrier chicane and a locked gate: hacking it (2.5 s) adds **35 % heat**.
- **Heat** raises security to Alert, shortens the escape trace (25 s per 1.0 heat, applied at the start and immediately for heat gained during the escape) and shortens checkpoint countdowns.
- **Lockdown** (data, `closeOn: lockdown`):
  - Alley gate slams shut (1 s warning). The terminal re-arms: hack again for +35 % heat.
  - Compound north gate seals in 12 s (−4 s per heat).
  - Expressway checkpoint seals in 30 s (−12 s per heat).
- **Escape options**: race north/east before the compound gate and checkpoint seal (fast, ≈ 1 km), or re-hack the alley (short, ≈ 700 m, costs heat). If both seal, the long west loop via W Avenue and Market Street is still open but tight on time.

## Rival crews

The two catalog cars the player did not pick spawn behind the player on W Avenue (`RivalDirector`, driver profiles `Data/Racing/Racer_Vex` and `Racer_Kade`).

| Objective | Rivals (`rivalGoalId`, delay) |
|---|---|
| `reach_core` | Race to the S7 car park off the Access Road (`core_staging`, 0.6 s) and wait there |
| `hack_core` | Hold |
| `escape` | Race to the Rift Gate tunnel (`extraction`, 1.2 s) |

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
| `Interactable` + `InteractionDefinition` | Gameplay | Reusable "stop and hold Interact" mechanic: verb, hold time, max speed, heat, message; raises/enables/disables/re-arms on world events |
| `SecurityBarrier` | Gameplay | Event-driven gates: countdown (heat-scaled) → strobe + klaxon → kinematic panels slide (push cars, never teleport) → slam |
| `SecurityLightGroup` | Gameplay | Kerb/crown neon per block; lockdown wave spreads from the theft at 140 m/s |
| `SecurityAlarm`, `SecurityPostEffects`, `DataCoreVisual` | Gameplay | Sirens + rotating beacons, lockdown colour grade, core presentation |
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


### City build validation (five-district city, rivals on)

| Check | Result |
|---|---|
| `BoulevardInAlleyOut` (SLS AMG) | **Completed** in 102.5 s. Alley re-hack +35 % heat; two camera detections in the alley (+8 % each, heat 0.51); compound gate, harbor checkpoint and expressway checkpoint sealed on schedule; finished **P1/3** |
| Rivals in that run | Raced W Avenue → North Boulevard → Access Road at up to 173 km/h, parked in the S7 car park, left 1.2 s after the theft, avoided the closing expressway checkpoint and re-routed via W Avenue and Market Street; no recoveries |
| `AlleyInExpresswayOut` | Lost the race at the expressway checkpoint, as before. With camera heat (0.59) it seals sooner (~23 s) |
| Title → Car Select → Night Run | Car 2/3 (SLS GT3) became the player with its own physics profile; SLS AMG and Terzo Millennio spawned as rivals with their own profiles and engine audio |
| EditMode | 83/83 (adds `CityTests`: network validity, connectivity, gate re-routing, skyway, racing line, rubber band, rival field; scene wiring for navigation, rivals, light budget) |
