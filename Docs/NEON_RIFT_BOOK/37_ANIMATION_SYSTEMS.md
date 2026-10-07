# 37 · Animation Systems

> [Index](00_INDEX.md) · Previous: [36 Results and Debrief](36_RESULTS_AND_DEBRIEF.md) · Next: [38 Camera System](38_CAMERA_SYSTEM.md)

## Overview

Almost every moving thing in Neon Rift is **procedural animation in C#**: transforms or material properties set each frame from game state (time, progress, events). Unity animation assets are used in only three places:

1. **Timeline AnimationTracks** on the intro's shot cameras.
2. A **legacy `Animation`** loop on the Mech Drone model.
3. A **sampled pose** of the Buster Drone (baked once, then static).

There are no Animator controllers, no character animation and no hand-keyed object animation.

## Catalogue

| What moves | How it moves | System responsible | Code | Asset / prefab |
|---|---|---|---|---|
| **Car body** | Rigidbody physics | `VehicleController` | `Scripts/Vehicles/Physics/VehicleController.cs` | `Prefabs/Vehicles/PF_Vehicle_*` |
| **Wheels** (steer, suspension travel, spin) | `LateUpdate` sets each pivot's position and steer rotation and each Spin transform's roll angle from angular velocity | `VehicleController.UpdateVisuals` (385) | same | `Wheels/Wheel_XX/Spin` pivots (`VehiclePrefabBuilder`) |
| **Vehicle lights** (brake brightening, reverse white, headlight levels) | Intensity and colour from input each frame | `VehicleLights.Update` | `Scripts/Vehicles/VehicleLights.cs` | — |
| **Traffic cars** (body via springs, wheels) | Physics + wheel roll; intro copies: positions and wheel rotations along lanes from Timeline time | `TrafficCar`, `IntroCityLife.Apply` | `Scripts/Gameplay/City/TrafficCar.cs`, `Scripts/Intro/IntroCityLife.cs` | `Prefabs/Traffic/PackCar_*` |
| **Turntable** (idle spin, drag momentum, whip on car switch, swing to the door) | Rotation integrated from angular velocity; eased coroutines | `VehicleShowroom.Update`, `Swap`, `Depart` | `Scripts/Frontend/VehicleShowroom.cs` | CarSelect scene |
| **Car Select departure** (car pulls out of the garage) | Eased acceleration along the exit heading, wheels spun by distance, step capped at 0.05 s | `VehicleShowroom.Depart` | same | — |
| **Car Select garage door** | Eased rise (`SmoothStep`) | `VehicleShowroom.Depart` | same | — |
| **Crew garage in the city** (light banks striking with flicker, roller door) | Bank intensities over time; smoothstep door travel 5.4 m over 3 s | `CrewGarage.Update`, `PlaceDoor` | `Scripts/Gameplay/CrewGarage.cs` | NightRun `CrewGarage` |
| **Security gates** (sliding panels, warning strobes) | Kinematic panels moved in `FixedUpdate` (they push cars), eased | `SecurityBarrier.MovePanels`, `SetWarning`, `Ease` | `Scripts/Gameplay/Mission/SecurityBarrier.cs` | 5 barriers |
| **Gate bolts, lamps, hologram** | Bolts slide into / out of the post (0.5 s); lamp pulse, flash, colour | `GateLockSystem.Update`, `PlaceBolts` | `Scripts/Gameplay/Mission/GateLockSystem.cs` | AlleyGate |
| **Data Core armoured sleeve** | Six panels unlock in sequence with an outward kick and sink into the pedestal over 3.2 s | `DataCoreChamber.UpdateSleeve` | `Scripts/Gameplay/Mission/DataCoreChamber.cs` | built by `NightRunBuilder.BuildChamber` |
| **Data Core arms** (hydraulic connector and scanner) | Carriages ride a circular rail (`RailPoint`); the connector arm is solved with **two-bone IK, elbow up** (`Pose`) toward the car roof; the scanner sweeps a beam | `DataCoreChamber.UpdateArms` | same | `BuildArm` |
| **Data cable and pulses** | Curved path sampled each frame (`BuildPath`, `Sample`); pulses travel along it at 2.5–16 per second | `DataCoreChamber.UpdateLink` | same | — |
| **Core column, scan rings, holograms, strobes** | Colour ramps, ring spin, scan-plane sweeps, hologram tearing | `UpdateCore`, `UpdateFacility`, `UpdateHolograms` | same | — |
| **Terminal screens** | Shader parameters (stage strip, scroll, progress, scan line, tear) | `TerminalDisplay.Update` | `Scripts/Gameplay/Mission/TerminalDisplay.cs` + `NeonRift_TerminalScreen.shader` | kiosks |
| **Security cameras** | Head yaw/pitch tracking the player in its cone; LED colour | `SecurityCamera.Update`, `SetTracking` | `Scripts/Gameplay/City/SecurityCamera.cs` | 12 poles |
| **Security drones** (lockdown) | Steering toward a chase slot, bank and pitch from velocity, searchlight aim, strobes | `SecurityDrones.Update` | `Scripts/Gameplay/City/SecurityDrones.cs` | `PF_Drone_Mech` |
| **Mech Drone model** (hover/limb motion) | **Legacy `Animation`** playing the FBX's "Take 001" clip in a loop, culled by renderers | set up by `DroneModels.Mech` | `Scripts/Editor/World/DroneModels.cs` | `Prefabs/Drones/PF_Drone_Mech.prefab` |
| **Buster Drone sentries** | Pose **sampled once** at 14 s of its lift-off clip and baked to static meshes; at runtime only a bob and a scanning yaw | `DroneModels.Buster` + `HoverSentry.Update` | `Scripts/Editor/World/DroneModels.cs`, `Scripts/Gameplay/City/HoverSentry.cs` | `PF_Drone_Buster.prefab` |
| **Intro drones and traffic** | Flights (lerp + bob), orbits around the core, lanes, all from Timeline time | `IntroCityLife.Apply` | `Scripts/Intro/IntroCityLife.cs` | `Intro/CityLife` |
| **Intro shot cameras** | **Timeline AnimationTracks** (eased position + look quaternion) cut by a Cinemachine track | `IntroBuilder.MoveClip`, `BuildTimeline` | `Scripts/Editor/Intro/IntroBuilder.cs` | `Data/Intro/Intro_NightRun.playable` |
| **Intro push-in** | Catmull-Rom camera path | `IntroSceneEntry.UpdatePushIn` | `Scripts/Intro/IntroSceneEntry.cs` | — |
| **Chase camera** | See [38](38_CAMERA_SYSTEM.md) | `VehicleChaseCamera` | `Scripts/Gameplay/VehicleChaseCamera.cs` | — |
| **Lockdown wave** | Per-group delayed emission and light colour fades, pulsing | `SecurityLightGroup.Update` | `Scripts/Gameplay/Mission/SecurityLightGroup.cs` | 35 groups |
| **Screens / billboards** | Material swap with the wave; glitch flicker | `LockdownScreens.Update` | `Scripts/Gameplay/City/LockdownScreens.cs` | — |
| **Traffic signals** | Shared-material cycle; amber stutter; red flashing | `TrafficSignalNetwork.Update` | `Scripts/Gameplay/City/TrafficSignalNetwork.cs` | — |
| **Sirens and beacons** | Beacon rotation; siren fade-in after the wave | `SecurityAlarm.Update` | `Scripts/Gameplay/Mission/SecurityAlarm.cs` | — |
| **Crowds** | Slow sway; groups step out of sight within 16 m | `CrowdGroups.Update` | `Scripts/Gameplay/City/CrowdGroups.cs` | — |
| **Aviation lights** | Blink in the `NeonRift/SkylineBackdrop` shader's aviation mode | shader | `Art/Shaders/NeonRift_SkylineBackdrop.shader` | — |
| **UI** | USS transitions and class toggles; counting numbers; easing bars; Intro overlay fades and flashes | `MissionHud`, `CarSelectScreen.StatRow.Tick`, `IntroOverlay` | `Scripts/Gameplay/Mission/MissionHud.cs`, `Scripts/Frontend/CarSelectScreen.cs`, `Scripts/Intro/IntroOverlay.cs` | `UI/*.uss` |

## Not animated

Gantry cranes, the tower crane, the Spire monument and the skyway are static geometry. Pedestrians have no walk cycles.
