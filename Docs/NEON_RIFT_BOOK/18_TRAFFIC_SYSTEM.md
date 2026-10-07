# 18 · Traffic System

> [Index](00_INDEX.md) · Previous: [17 Rival AI](17_RIVAL_AI.md) · Next: [19 Mission System](19_MISSION_SYSTEM.md)

## Overview

City traffic uses the **generic passenger car pack** (10 models) as real physical cars: a Rigidbody with four raycast springs, lateral tyre grip and a drive/brake force. They are deliberately simpler than the player's `VehicleController`: traffic cars do not use `IVehicleInputSource`. There are two pools: **roaming** (14) and **parked** (26).

| Piece | File |
|---|---|
| Pool manager (roaming + parked) | `Assets/_NeonRift/Scripts/Gameplay/City/CityTraffic.cs` (`IMissionWorldComponent`) |
| One physical car | `Scripts/Gameplay/City/TrafficCar.cs` (`enum Mode { Parked, Driving, Knocked, Stopped }`) |
| Prefab builder | `Scripts/Editor/District/TrafficCarBuilder.cs` → `Assets/_NeonRift/Prefabs/Traffic/PackCar_*.prefab` (10) |
| Scene wiring | `NightRunBuilder.BuildTraffic` (`Scripts/Editor/District/NightRunBuilder.CityLife.cs`) |
| Source model | `Assets/ThirdParty/Sketchfab/Vehicles/generic-passenger-car-pack/source/fab.fbx` + `textures/` |

## Prefabs (`TrafficCarBuilder.Build`)

The pack is one FBX with ten bodies in a ring, each with four loose wheels. The builder re-frames each body and its four nearest wheels so the car sits on the ground at the origin facing +Z. It then adds URP materials from the pack's textures (`Assets/_NeonRift/Art/Traffic/Traffic_Pack*`), a **box collider sized from the body's vertices** (read with `Mesh.AcquireReadOnlyMeshData`, because the FBX is not Read/Write enabled), a Rigidbody (1350–1900 kg) and a `TrafficCar`. Wheel transforms are named `Wheel`.

## Physics (`TrafficCar.FixedUpdate`, line 120)

- Four springs at the wheel positions (ride height, pitch, roll).
- Tyres kill sideways slip up to the grip limit (about 0.95 g), so a hard hit still slides the car.
- Steering is a yaw rate toward the aim point, limited by speed (no turning on the spot).
- Drive/brake force toward the target speed; parked-and-hit, knocked or stopped cars brake to a halt.
- Tint per car: `SetTint` writes `_BaseColor` through a `MaterialPropertyBlock` on the paint renderers.

## Roaming behaviour (`CityTraffic.Update`, `Steer`)

| Rule | Implementation |
|---|---|
| Lanes | Kerb-side lane of the road graph (`LaneFor`); no alleys, service roads, tunnels or the Data Core compound (`InCompound`, exclusion rect (100, 55, 120 × 155)) |
| Dead ends pruned | Nodes with a single drivable edge are removed repeatedly (`Start`, lines 102–108), so cars never strand at the compound spur |
| Route | At each node pick the next edge (`NextEdge`, `PeekNext`) |
| Speed | Cruise 8–13 m/s (`cruiseRange`), slower for corners |
| Distance keeping | Forward sphere cast for anything ahead (other traffic, the player, the rivals) and a braking distance |
| **Give way at junctions** | `JunctionBusy`: halts short of the line when the player or a rival (`Drivers`) will reach the junction within ~3.5 s |
| **Lockdown** | `OnSecurity(Lockdown)`: pull over and stop |
| Traffic signals | **Not obeyed.** `TrafficSignalNetwork` is visual only (see below) |

## Parked pool

2,073 parking bays are recorded from the lots and garage decks (`spots`). The nearest 26 to the player are filled (`FillSpots`, within `parkedRadius` 170 m). Parked cars are **kinematic until hit**.

## Spawning and recycling (no popping)

`Spawn` / `Acquire`: cars are placed **out of the camera's view** (`InView`) between `spawnMin` 70 m and `spawnMax` 260 m. They are recycled when beyond `recycleBeyond` 380 m, overturned, stuck, or disturbed and left behind. Placement uses `Ground` raycasts.

## Collisions

- The **Traffic** layer (13) ignores itself: traffic never collides with traffic (`Physics.IgnoreLayerCollision` in `CityTraffic.Start`). It collides with the player, rivals and the world.
- `TrafficCar.OnCollisionEnter` (182): the car turns dynamic, is shunted and spun by the momentum share of the hitting car, plays an impact sound by strength and stops driving for 2.5–7 s (`Knocked`), then rejoins the nearest road (`Resume`).
- Rival obstacle probes include the Traffic layer, and the scripted validation driver brakes gently for traffic on its line (`NightRunValidator.FollowTraffic`).

## Audio

A 3D engine loop pitched by speed (low voice priority) and impact one-shots on dedicated sources (`TrafficCar` fields `engine`, `sfx`, `engineLoop`).

## Traffic signals (`TrafficSignalNetwork`)

`Scripts/Gameplay/City/TrafficSignalNetwork.cs`: every north–south and east–west lens shares **one runtime material per colour**, so the whole grid cycles (green → amber → all-red, north–south vs east–west) with six material updates. On `grid.anomaly` the grid **stutters amber**, and in a lockdown it **flashes red**. It works without a mission (free roam) and binds to one for the lockdown look. Signal geometry: `CityProps.BuildIntersections`.

## Intro traffic (separate)

The intro does **not** use `CityTraffic`. It uses visual-only copies of the pack cars driven on Timeline-synced lanes by `IntroCityLife` ([31 Cinematic Intro](31_CINEMATIC_INTRO.md)).

## Validation

- `CityTests.TrafficCars_ArePhysicalAndIgnoreEachOther`, `NightRun_CityLifeIsSetUp`.
- Play Mode (`Docs/City.md`): 14 roaming + 26 parked live during every heist scenario, 29 cars within 150 m; a scripted crash woke four parked cars, which were shoved and came to rest upright. Final series: Boulevard runs completed with 0 player collisions and 0 rival contacts.
