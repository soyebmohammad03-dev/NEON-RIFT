# 46 · Developer Quick Reference

> [Index](00_INDEX.md) · Previous: [45 Known Limitations](45_KNOWN_LIMITATIONS.md) · Next: [47 Teacher Q&A](47_TEACHER_QA_GUIDE.md)

Paths: `S/` = `Assets/_NeonRift/Scripts/`, `D/` = `Assets/_NeonRift/Data/`, `P/` = `Assets/_NeonRift/Prefabs/`.

## System → scripts → scene → prefab → data

| System | Primary script | Secondary scripts | Scene | Prefab | Data |
|---|---|---|---|---|---|
| Boot / flow | `S/Game/GameRoot.cs` | `GameFlow`, `BootstrapLoader`, `GameContext`, `RunSession`, `LoadingOverlay` | Bootstrap | — | `D/Config/GameConfig.asset` |
| Settings / options | `S/Game/GameSettings.cs` | `OptionsMenu` | all | — | PlayerPrefs `NeonRift.Settings.*` |
| Title | `S/Frontend/TitleScreen.cs` | `OptionsMenu` | Frontend | — | `UI/TitleScreen.uxml` |
| Car Select | `S/Frontend/CarSelectScreen.cs` | `VehicleShowroom`, `ShowroomEngine`, `S/Editor/Frontend/GarageBuilder.cs`, `GaragePlanningWall` | CarSelect | `P/Vehicles/PF_Vehicle_*` | `D/Vehicles/VehicleCatalog.asset` |
| Vehicle physics | `S/Vehicles/Physics/VehicleController.cs` | `VehicleWheel`, `TyreModel`, `Drivetrain`, `Gearbox`, `SteeringSystem` | all driving scenes | `P/Vehicles/PF_Vehicle_*` | `D/Vehicles/Physics/VehiclePhysics_*.asset`, `D/Physics/PM_VehicleBody.asset` |
| Vehicle audio | `S/Audio/VehicleAudio.cs` | `EngineSoundModel`, `TyreSoundModel`, `S/Editor/Audio/VehicleAudioGenerator.cs`, `EngineSynth` | — | — | `D/Vehicles/Audio/VehicleAudio_*.asset` |
| Vehicle visuals | `S/Vehicles/VehicleRig.cs` | `VehicleLights`, `S/Editor/Vehicles/VehiclePrefabBuilder.cs`, `VehicleLodSetup`, `GltfMaterialFixer` | — | `P/Vehicles/` | `D/Vehicles/ImportSetups/` |
| Spawn | `S/Gameplay/VehicleSpawnPoint.cs` | `MissionSceneEntry` | NightRun, TestTrack | — | `D/Vehicles/Vehicle_*.asset` |
| Input | `S/Input/PlayerDrivingInput.cs` | `NeonRiftControls` (generated) | all | — | `Settings/Input/NeonRiftControls.inputactions` |
| Mission | `S/Gameplay/Mission/MissionDirector.cs` | `MissionProgress`, `MissionWorld`, `MissionZone` | NightRun | — | `D/Missions/Mission_NightRun.asset` |
| Heist interactions | `S/Gameplay/Mission/Interactable.cs` | `InteractionRun`, `InteractionStep`, `InteractionDefinition`, `TerminalDisplay`, `TerminalReadout` | NightRun | — | `D/Missions/Interaction_*.asset` |
| Data Core | `S/Gameplay/Mission/DataCoreChamber.cs` | `S/Editor/District/NightRunBuilder.Heist.cs` | NightRun | — | `Interaction_DataCoreUplink`, `Interaction_CoreSecurity` |
| Gates | `S/Gameplay/Mission/SecurityBarrier.cs` | `GateLockSystem` | NightRun | — | `Interaction_HackGate` |
| Lockdown reactions | `S/Gameplay/Mission/SecurityLightGroup.cs` | `SecurityAlarm`, `SecurityPostEffects`, `S/Gameplay/City/LockdownScreens.cs`, `TrafficSignalNetwork`, `SecurityCamera`, `SecurityDrones` | NightRun | `P/Drones/PF_Drone_Mech` | `Settings/Rendering/NightRun_Alert.asset`, `NightRun_Lockdown.asset` |
| Navigation | `S/Gameplay/Navigation/CityNavigation.cs` | `S/World/RoadGraph.cs`, `RoadNetwork` | NightRun | — | `D/World/RoadNetwork_NightRun.asset` |
| Rival AI | `S/Gameplay/Racing/RivalDirector.cs` | `RacerDriver`, `RacingLine`, `RaceMarker`, `RacerProfile`, `RivalTask` | NightRun | — | `D/Racing/Racer_*.asset`, `Mission_NightRun.rivalTasks` |
| Traffic | `S/Gameplay/City/CityTraffic.cs` | `TrafficCar`, `S/Editor/District/TrafficCarBuilder.cs` | NightRun | `P/Traffic/PackCar_*` | — |
| Crowds, sentries | `S/Gameplay/City/CrowdGroups.cs` | `HoverSentry` | NightRun | `P/Drones/PF_Drone_Buster` | — |
| Lights budget | `S/Gameplay/City/LightBudget.cs` | `CameraCullDistances` | NightRun | `P/District/PF_Prop_StreetLight_*` | — |
| City generation | `S/Editor/District/NightRunBuilder.cs` | `NightRunDistrict`, `CityLayout`, `CityInfill`, `CityProps`, `CityLandmarks`, `TowerDressing`, `SkylineBackdrop`, `DistrictKit`, `DistrictTextures`, `MeshBuilder`, `BlockMeshes` | NightRun (generated) | `P/Buildings`, `P/District` | `D/World/Building*.asset` |
| Atmosphere | `S/Rendering/GroundHazeFeature.cs` | `GroundHaze` | — | — | `Settings/Rendering/PC_Renderer.asset` |
| HUD | `S/Gameplay/Mission/MissionHud.cs` | `CityMinimap`, `MinimapProjection`, `TerminalReadout` | NightRun | — | `UI/MissionHud.uxml`, `MissionHud.uss` |
| Camera | `S/Gameplay/VehicleChaseCamera.cs` | `CameraCullDistances` | NightRun, TestTrack | — | — |
| Mission audio / music | `S/Gameplay/Mission/MissionAudio.cs` | `MissionAudioSet`, `ScoreMix`, `S/Editor/Audio/ScoreComposer.cs`, `MissionAudioGenerator`, `HeistAudioGenerator` | NightRun | — | `D/Missions/MissionAudio_NightRun.asset` |
| Mixer | `S/Audio/AudioMixerService.cs` | `AudioMixerConfig`, `MasterLimiter`, `S/Editor/Audio/AudioMixerBuilder.cs` | — | — | `D/Audio/AudioMixerConfig.asset`, `Audio/Mixer/NeonRiftMixer.mixer` |
| Intro | `S/Intro/IntroSceneEntry.cs` | `IntroOverlay`, `IntroCue*`, `IntroCityLife`, `CrewGarage`, `S/Editor/Intro/IntroBuilder.cs` | NightRun | — | `D/Intro/Intro_NightRun.playable`, `D/Config/IntroSettings.asset` |
| Grade / records | `S/Missions/MissionGrade.cs` | `MissionRecords` | — | — | PlayerPrefs `NeonRift.Best.*` |

## Interfaces

| Interface | File | Implemented by |
|---|---|---|
| `ISceneEntryPoint` | `S/Game/GameContext.cs` | `TitleScreen`, `CarSelectScreen`, `MissionSceneEntry`, `IntroSceneEntry` |
| `IIntroEntryPoint` | `S/Game/IGameFlow.cs` | `IntroSceneEntry` |
| `IGameFlow` | `S/Game/IGameFlow.cs` | `GameFlow` |
| `IVehicleInputSource` | `S/Vehicles/DrivingInput.cs` | `PlayerDrivingInput`, `RacerDriver`, `WaypointDriver`, `RouteAutopilot`, `ScriptedDrivingInput` |
| `IVehicleInputReceiver` | `S/Vehicles/DrivingInput.cs` | `VehicleController` |
| `IMissionWorldComponent` | `S/Gameplay/Mission/IMissionWorldComponent.cs` | 15 classes (see [02](02_ARCHITECTURE.md)) |
| `IMissionTarget` | `S/Gameplay/Mission/IMissionTarget.cs` | `MissionZone`, `Interactable` |
| `IIntroCueHandler` | `S/Intro/IntroCue.cs` | `IntroSceneEntry` |

## ScriptableObjects

`GameConfig`, `IntroSettings`, `VehicleCatalog`, `VehicleDefinition`, `VehiclePhysicsProfile`, `VehicleAudioProfile`, `MissionDefinition`, `InteractionDefinition`, `MissionAudioSet`, `RacerProfile`, `RoadNetwork`, `BuildingCatalog`, `BuildingDefinition`, `AudioMixerConfig`, `VehicleModelSetup` (editor).

## Managers, controllers, services, builders, state machines

| Role | Classes |
|---|---|
| Composition root / services | `GameRoot`; `AudioMixerService`, `GameSettings`, `GameFlow` |
| Managers / directors | `MissionDirector`, `RivalDirector`, `CityTraffic`, `LightBudget`, `TrafficSignalNetwork`, `SecurityDrones`, `CrowdGroups` |
| Controllers | `VehicleController`, `VehicleChaseCamera`, `CarSelectScreen`, `VehicleShowroom`, `IntroSceneEntry` |
| Pure state machines / logic | `MissionProgress`, `InteractionRun`, `Gearbox`, `Drivetrain`, `MissionGrade`, `ScoreMix`, `EngineSoundModel`, `TyreModel`, `MinimapProjection`, `RoadGraph` |
| Factories / spawners | `VehicleSpawnPoint.Spawn`, `RivalDirector.Spawn`, `CityTraffic.Create/Spawn` |
| Editor builders (generators) | `NightRunBuilder`, `NightRunDistrict`, `GarageBuilder`, `IntroBuilder`, `ValidationRouteBuilder`, `VehiclePrefabBuilder`, `BuildingPrefabBuilder`, `TrafficCarBuilder`, `DroneModels`, `ThirdPartyIntake`, `AudioMixerBuilder`, all `*AudioGenerator`s, `ScoreComposer` |

## Menu cheat-sheet (all under **Neon Rift**)

Validate Project · Night Run ▸ Build Night Run (district + mission) · Night Run ▸ Capture Look-Dev Shots (Play Mode) · Car Select ▸ Build Garage · Intro ▸ Build Intro / Generate Intro Audio / Reset 'Intro Seen' · Assets ▸ Run Third-Party Intake / Rebuild Vehicle Prefabs And Catalog · Vehicles ▸ Build All Vehicle Prefabs / Apply Vehicle LODs To Catalog Prefabs / Apply Vehicle Copy From Recipes / Measure Vehicle Performance / Update Car Select Stats From Physics · Audio ▸ Generate Vehicle Audio / Generate Mission Audio / Build Audio Mixer / Add Vehicle Audio To Prefabs · Test Track ▸ Build Validation Route · Tests ▸ Run EditMode Tests (write report) · Validation ▸ Build Perf Player (macOS).

## World event ids (Night Run)

`mission.start`, `mission.complete`, `mission.failed`, `core.arrive`, `core.security.off`, `core.extract.connect`, `core.extract.init`, `core.extract.stabilize`, `core.extract.begin`, `grid.anomaly`, `core.extract.trace`, `core.acquired`, `core.breached`, `lockdown`, `escape.start`, `alley.gate.open`, `alley_gate_terminal.hack|abort|lockout|unlock` (constants in `S/Editor/District/NightRunBuilder.cs` and `NightRunBuilder.Heist.cs`).

## Target and marker ids

Zones `core_compound`, `extraction`; interactables `core_security_terminal`, `core_uplink`, `alley_gate_terminal`; gates `alley_gate`, `compound_gate`, `expressway_checkpoint`, `harbor_checkpoint`, `skyway_gate`; race markers `spire_relay`, `kowloon_relay`, `trunk_tap`, `extraction`, `core_staging`.
