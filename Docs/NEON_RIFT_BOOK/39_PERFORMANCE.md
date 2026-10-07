# 39 · Performance

> [Index](00_INDEX.md) · Previous: [38 Camera System](38_CAMERA_SYSTEM.md) · Next: [40 Testing and Validation](40_TESTING_AND_VALIDATION.md)

Every number in this chapter comes from `Docs/Performance.md`, `Docs/Environment.md` or `Docs/City.md`, where the measurements were recorded. Editor numbers include editor overhead and drift between runs; decisions were made on interleaved A/B measurements, and absolute numbers come from the player build.

## Optimisations that exist

| Area | Technique | Where |
|---|---|---|
| **Geometry batching** | One combined mesh **per material per block** (`BlockMeshes`), static batching flags, SRP Batcher, GPU Resident Drawer | `Scripts/Editor/District/BlockMeshes.cs`, `DistrictKit.Renderer`, `Settings/Rendering/PC_RPAsset.asset` |
| **Cell splitting for culling** | Intersection furniture and tower dressing in 400 m cells; infill clutter in 64 m cells | `CityProps.BuildIntersections`, `TowerDressing.Cell`, `CityInfill.ClutterAt` |
| **Layer cull distances** | Detail layer beyond 180 m, Clutter layer beyond 90 m (spherical, so turning doesn't pop) | `Scripts/Gameplay/CameraCullDistances.cs` |
| **LODs** | Vehicle LOD0/1/2 + cull (`VehicleLodSetup`); hero buildings drop interiors below 15 % (`HeroLod`); prop LODs (`DistrictKit.AddLod`) | `Scripts/Editor/Vehicles/VehicleLodSetup.cs`, `NightRunDistrict.HeroLod` |
| **Light budget** | 787 lamps with disabled spots; the 56 nearest within 150 m on, 2 shadowed | `Scripts/Gameplay/City/LightBudget.cs` |
| **Emissive-only interiors and far lamps** | Infill lighting and distant streets use emission and ground pools, not lights | `CityInfill.YardLamp`, `CityProps.LampHaze` |
| **Shadows** | Moon cascades 4 → 2, shadow distance 90 m; **soft-shadow quality Low** (−37 % GPU, no visible change); cars and vans cast no shadows in infill; drones and sentries cast none | `NightRunBuilder.ConfigurePipeline`, `DroneModels` |
| **Skyline** | About 3 k-triangle backdrop rings in one draw; Sketchfab skyline clusters removed (< 0.2 % pixel change) | `SkylineBackdrop.cs`, `CityLayout.Skyline` (empty) |
| **Physics** | 100 Hz; Traffic ignores Traffic; parked cars kinematic until hit; one simple box collider per mass; no mesh colliders on the city | `TimeManager.asset`, `CityTraffic.Start`, `TrafficCar` |
| **Allocation-free frame** | HUD strings rebuilt only on change; index loops instead of `foreach` over `IReadOnlyList` (no boxed enumerators); race rows in 10 % steps | `MissionDirector`, `MissionHud`, `VehicleAudio`, `RacerDriver`, `RivalDirector` |
| **Audio voices** | 64 real voices, explicit priorities, other cars +140 | `ProjectSettings/AudioManager.asset`, `VehicleAudio` |
| **Intro rig** | Inert during missions; intro city life only active in its shot windows | `IntroSceneEntry`, `IntroCityLife` |
| **Sentry drone** | Posed skinned mesh baked to a static mesh (no per-frame skinning) | `DroneModels.Buster` |

Kept for the look: MSAA 4× (2× saved 7–12 % inconsistently and softened neon edges) and SSAO (off saved 11 %).

## Measurement tools

| Tool | File | What it does |
|---|---|---|
| `RoutePerf` | `Scripts/Editor/Validation/RoutePerf.cs` | Rides along a Play Mode scenario; per frame: PlayerLoop and main thread, GPU (`FrameTimingManager`), GC, SetPass, triangles, shadow casters, lights, phase, position → `Logs/Perf/<prefix>_seconds.csv` and `_summary.md` per segment (approach, heist, escape) |
| `GpuBench` | `Scripts/Editor/Validation/GpuBench.cs` | Freezes the scene and A/B tests URP variants (soft shadows, MSAA, SSAO …), 120 settle + 300 measured frames, three interleaved rounds |
| `PerfBuild` + `PerfRouteRunner` + `PerfRouteArgs` | `Scripts/Editor/Validation/PerfBuild.cs`, `Scripts/Gameplay/PerfRouteRunner.cs`, `Scripts/Game/PerfRouteArgs.cs` | **Neon Rift ▸ Validation ▸ Build Perf Player (macOS)** builds a release player; `-perfroute <folder>` boots into the mission, drives the BoulevardInAlleyOut route and writes `perfroute_summary.md` / `perfroute_seconds.csv` |
| Unity Profiler | — | Hotspot attribution (GC per sample, time per script) |

## Recorded results

**GPU bench** (lockdown escape, frozen, 1080p): baseline 21.06 ms → soft shadows Low **13.32 ms (−37 %)**; soft shadows off −31 %; MSAA 2× −7 %.

**Player build** (release macOS, Apple M3, 1920×1080 windowed, V-sync off, full route; measured **before** the pack-car traffic replaced the earlier block traffic):

| Segment | FPS avg | Frame avg | p95 | CPU main | GPU avg | GC/frame |
|---|---|---|---|---|---|---|
| Approach | 35 | 28.9 ms | 47.1 | 26.2 ms | 23.5 ms | 0 |
| Heist | 38 | 26.0 ms | 38.4 | 24.2 ms | 17.5 ms | 0 |
| Escape | 39 | 25.4 ms | 39.1 | 23.7 ms | 22.1 ms | 0 |
| **Whole route** | **38** | 26.5 ms | 41.4 | 24.5 ms | 21.3 ms | **0** |

**Scene stats at the spawn view** (editor, `UnityStats`): 3.82 M triangles (phase 1) → 2.94 M after vehicle LODs and 2 cascades → 4.06 M after the block infill (mostly shadow and depth passes over the new masses).

No newer player-build measurement is recorded after the pack traffic, the rival heists, the drones or the intro city life were added.

## Remaining hotspots (recorded)

Mission start hitches (shader first use; a warm-up collection would help), tower triangles (towers have no LODs: 2.6–4.2 M triangles in view), and about 70 real-time lights in the lockdown (beacons, strobes, drone searchlights).
