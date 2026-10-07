# 24 · Environmental Interactions

> [Index](00_INDEX.md) · Previous: [23 Heat and Lockdown](23_HEAT_AND_LOCKDOWN.md) · Next: [25 Lighting](25_LIGHTING.md)

This chapter lists every way the **world itself** responds to the player, with the mechanism behind each. Everything goes through the **event-id model**: an `IMissionWorldComponent` binds to `MissionWorld` and reacts to `EventRaised` / `SecurityChanged` ([19 Mission System](19_MISSION_SYSTEM.md)).

| Interaction | Player action | World response | Component | File |
|---|---|---|---|---|
| Mission zones | Drive into a trigger | Objective completes, beacon hides | `MissionZone` (Trigger layer, collides with vehicles only) | `Scripts/Gameplay/Mission/MissionZone.cs` |
| Terminals and uplink | Stop in the zone, work the stages with E | Screens, holograms, machinery, events | `Interactable`, `TerminalDisplay`, `DataCoreChamber` | `Scripts/Gameplay/Mission/` |
| Hackable gate | Hack the alley controller | Bolts withdraw, panel slides open; re-hack in a lockdown | `Interactable` + `GateLockSystem` + `SecurityBarrier` | [21](21_SECURITY_GATE.md) |
| Sealing gates | Steal the core | Countdown, strobes and klaxon, panels slide (they **push** cars) | `SecurityBarrier` | `Scripts/Gameplay/Mission/SecurityBarrier.cs` |
| Security cameras | Stay in a camera's cone with line of sight | Head tracks the car, LED red, heat after 1.1 s | `SecurityCamera` | `Scripts/Gameplay/City/SecurityCamera.cs` |
| Traffic | Drive near or into cars | Cars give way at junctions; hits shunt, spin and stop them | `CityTraffic`, `TrafficCar` | `Scripts/Gameplay/City/` |
| Parked cars | Hit them | They wake (kinematic → dynamic) and are shoved | `TrafficCar` | same |
| Pedestrian groups | Drive within 16 m | The group steps out of sight (never stands in a car's way) | `CrowdGroups` | `Scripts/Gameplay/City/CrowdGroups.cs` |
| Street light budget | Move through the city | The 56 nearest lamps (front-weighted) fade on, 2 with shadows | `LightBudget` | `Scripts/Gameplay/City/LightBudget.cs` |
| Signals and screens | Extraction progress, lockdown | Amber stutter, red flashing, warning wave | `TrafficSignalNetwork`, `LockdownScreens` | `Scripts/Gameplay/City/` |
| Drones | Counter-intrusion or lockdown | Pursuit with searchlights | `SecurityDrones` | `Scripts/Gameplay/City/SecurityDrones.cs` |
| Crew garage | (intro only) | Light banks strike, roller door lifts | `CrewGarage` | `Scripts/Gameplay/CrewGarage.cs` |
| Out of bounds | Fall below −30 m | Car returns to the spawn point | `MissionSceneEntry.FixedUpdate` | `Scripts/Gameplay/MissionSceneEntry.cs` |
| Recover | Press R | Car is righted on the ground beneath it | `VehicleController.Recover` | `Scripts/Vehicles/Physics/VehicleController.cs:141` |

## Not implemented

- **Destructible environment:** props and buildings are static colliders; nothing breaks.
- **Vehicle damage:** none (see [13](13_VEHICLE_VISUALS.md)).
- **Pedestrian collision or animation:** crowd figures are static low-poly silhouettes with a slow sway, and they have no colliders.
