# 48 · Glossary

> [Index](00_INDEX.md) · Previous: [47 Teacher Q&A](47_TEACHER_QA_GUIDE.md)

| Term | Meaning | See |
|---|---|---|
| **Ackermann** | Steering geometry where the inner front wheel turns more than the outer | [11](11_VEHICLE_PHYSICS.md) |
| **Alert** | Middle security level, reached on the first heat | [23](23_HEAT_AND_LOCKDOWN.md) |
| **Alert origin** | The theft position (Data Core) the lockdown wave spreads from | `MissionWorld.AlertOrigin` |
| **Announcement** | A data-driven HUD message or banner triggered by a world event | `MissionAnnouncement` |
| **Block** | Merged grid cells between road lines; the unit of street walls, meshes and infill | [07](07_CITY_AND_WORLD_GENERATION.md) |
| **Bootstrap** | Always-loaded scene with the composition root | [06](06_BOOTSTRAP_AND_SCENES.md) |
| **Checkpoint** (gate) | Expressway or harbor barrier that seals in a lockdown | `SecurityBarrier` |
| **Checkpoint** (interaction) | A stage a cancelled run returns to | `InteractionStep.checkpoint` |
| **Clutter / Detail layers** | Small props culled beyond 90 m / 180 m | `CameraCullDistances` |
| **Combined slip** | Normalised slip ratio and slip angle combined into one value for the tyre force | `TyreModel` |
| **Composition root** | The one place long-lived services are created (`GameRoot`) | [02](02_ARCHITECTURE.md) |
| **Core trunk tap** | A rival crew's second task, hacking the core's data line from the kerb | [17](17_RIVAL_AI.md) |
| **Crew** | The player's team; also each rival team (VEX, KADE) | — |
| **Crew garage / Unit 7** | The hidden garage on W Avenue: the Car Select hall placed in the city | `CrewGarage` |
| **Data Core** | The heist target in the Sector 7 compound | [22](22_DATA_CORE.md) |
| **Debrief** | The end-of-run results screen with a grade | [36](36_RESULTS_AND_DEBRIEF.md) |
| **Engaged** | Inside an interactable's trigger and below its speed limit | [20](20_HEIST_SYSTEM.md) |
| **Event id** | String such as `core.breached` that scene objects react to | [19](19_MISSION_SYSTEM.md) |
| **Extraction** | Final zone, the Rift Gate tunnel; also the uplink stage that pulls the data | — |
| **Friction ellipse** | Limit on the combined longitudinal and lateral tyre force | `TyreModel.ClampToEllipse` |
| **Heat** | 0–1 risk value that raises security and shortens timers | [23](23_HEAT_AND_LOCKDOWN.md) |
| **Hero building** | A hand-placed landmark model from the catalog | `CityLayout.Heroes` |
| **Infill** | Procedural content behind the street walls | `CityInfill` |
| **Interactable** | A "stop and work the device" trigger with stages | `Interactable` |
| **Interference** | A Sustain-stage disruption the player must answer with E | `InteractionStep.WithInterference` |
| **Kerb lane** | The outer lane next to the pavement; where rivals stop to work | [17](17_RIVAL_AI.md) |
| **Light budget** | Keeping only the nearest 56 lamps lit as real lights | `LightBudget` |
| **Lockdown** | Highest security level after the theft | [23](23_HEAT_AND_LOCKDOWN.md) |
| **Lockout** | Temporary block after too many timing misses | `InteractionDefinition` |
| **Match cut** | The intro's push-in ending exactly on Car Select's first frame | [31](31_CINEMATIC_INTRO.md) |
| **Mission world** | The per-attempt hub objects bind to | `MissionWorld` |
| **Par** | Target time (115 s) for full pace marks | `MissionDefinition.parTime` |
| **Podium / street wall** | Generated low-rise frontage along every block side | [08](08_BUILDINGS_AND_ENVIRONMENT.md) |
| **Pure pursuit** | Steering toward a look-ahead point on a path | `RacerDriver`, `RouteAutopilot` |
| **Qualifying** | Only the first two crews out advance | [17](17_RIVAL_AI.md) |
| **Race marker** | A named place rivals drive to (child slots per rival) | `RaceMarker` |
| **Racing line** | A rounded, resampled path with lateral range and advisory speed | `RacingLine` |
| **Relay hack** | A rival crew's first task | [17](17_RIVAL_AI.md) |
| **Rift Gate** | The south-east extraction tunnel | — |
| **Rival task** | One stage of a rival crew's own heist | `RivalTask` |
| **Road graph / network** | Nodes and edges describing where cars can drive | [09](09_ROADS_AND_ROUTE_NETWORK.md) |
| **Rubber band** | Small bounded pace adjustment for rivals by gap to the player | `RacerProfile.RubberBand` |
| **Sector 7 (S7)** | The hero district with the Data Core | — |
| **Sentry** | Hovering Buster Drone over the compound | `HoverSentry` |
| **Skyway** | Elevated harbor road opened during the escape | [23](23_HEAT_AND_LOCKDOWN.md) |
| **Snapshot** | AudioMixer state (Gameplay, Menu, Results, Lockdown, Ducked) | [29](29_AUDIO_SYSTEM.md) |
| **Split** | Time at which each objective was completed | `MissionHud.StampObjective` |
| **Stage** | One step of a multi-stage interaction (Auto, Hold, Timing, Sustain) | `InteractionStep` |
| **Telemetry** | Per-step snapshot of a car's state | `VehicleTelemetry` |
| **Theft beat** | 2.6 s slow-motion camera swing on `core.breached` | [38](38_CAMERA_SYSTEM.md) |
| **Trace** | The 80 s escape timer before you are found | — |
| **Uplink** | The ring around the core where the data is extracted | [22](22_DATA_CORE.md) |
| **VEX / KADE** | The two rival crews and their driver profiles | [17](17_RIVAL_AI.md) |
| **Work FX** | Magenta beacon and light over a rival car while it hacks | `RivalDirector.Racer.WorkFx` |
