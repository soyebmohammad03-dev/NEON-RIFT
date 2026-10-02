# Architecture

## Assemblies

Dependencies point downwards only. Data modules know nothing about flow or UI.

```
NeonRift.Frontend   NeonRift.Gameplay         (scene-level features)
        \              /
         NeonRift.Game                        (composition root, flow, session, config)
        /      |       \
NeonRift.Input |  NeonRift.Missions           (player input bridge / mission data)
               NeonRift.Audio                 (vehicle audio, mixer service; refs Vehicles)
        \      |
       NeonRift.Vehicles     NeonRift.World   (vehicle data + rig / building catalog)
                 \             /
                  NeonRift.Core               (shared primitives: AssetLicense)

NeonRift.Editor           editor tooling (validator, third-party intake, prefab builders, vehicle bench, route builder)
NeonRift.Tests.EditMode   unit tests
```

| Assembly | Owns |
|---|---|
| Core | `AssetLicense` |
| Vehicles | `VehicleDefinition`, `VehicleCatalog`, `VehicleDisplayStats`, `VehicleRig`/`WheelRig`, `DrivingInput`, `IVehicleInputSource`, `IVehicleInputReceiver`; driving model: `VehicleController`, `VehiclePhysicsProfile` (+ settings structs), `VehicleWheel`, `TyreModel`, `Drivetrain`, `Gearbox`, `SteeringSystem`, `VehicleTelemetry`, `DrivingSurface`, `ScriptedDrivingInput` |
| World | `BuildingDefinition`, `BuildingCatalog`, `BuildingTier` (Hero / Midground / Skyline) |
| Missions | `MissionDefinition`, `RunResult`, `MissionOutcome` |
| Input | Generated `NeonRiftControls` (from `Settings/Input/NeonRiftControls.inputactions`), `PlayerDrivingInput` |
| Game | `GameRoot` (also owns `AudioMixerService`, switches Menu/Gameplay snapshots on state change), `GameFlow`/`IGameFlow`, `GameContext` (+ `Audio`), `ISceneEntryPoint`, `RunSession`, `GameConfig` (+ `AudioMixer`), `LoadingOverlay`, `BootstrapLoader` |
| Frontend | `TitleScreen`, `CarSelectScreen`, `VehicleShowroom` |
| Audio | `VehicleAudio`, `EngineSoundModel`, `TyreSoundModel`, `AudioMixerConfig`, `AudioMixerService`, `MixerState`, `AudioChannel`; dev `AudioOutputRecorder`, `AudioSignalAnalysis` (see [Audio.md](Audio.md)) |
| Gameplay | `MissionSceneEntry`, `VehicleSpawnPoint`, `VehicleChaseCamera`, `DrivingRoute`, `RouteAutopilot`; dev tools `VehicleDebugHud`, `VehicleTelemetryLog`, `VehicleAudioValidator` |

## Scene model

- **Bootstrap** (build index 0) is loaded for the whole session. It holds `GameRoot`, the loading overlay and the EventSystem.
- Exactly one **content scene** is loaded additively on top and set active: Frontend, CarSelect, or a mission scene.
- Each content scene has one root component implementing `ISceneEntryPoint`. `GameFlow` calls `Enter(GameContext)` after load and `Exit()` before unload. Scenes never look up services globally.
- Transition: fade in → `Exit` → unload → load additive → set active → unload unused assets → `Enter` → fade out. Requests made during a transition are ignored.
- Playing from a content scene in the editor: `BootstrapLoader` loads Bootstrap additively, and `GameFlow` adopts the open scene. For missions it picks the first catalog vehicle and the mission whose `sceneName` matches.

## Data flow for a run

`CarSelectScreen` → `RunSession.SelectVehicle` / `SelectMission` → `IGameFlow.StartMission()` → mission scene → `MissionSceneEntry.Enter` → `VehicleSpawnPoint.Spawn(selected definition)` (instantiates the gameplay prefab, calls `VehicleController.Configure(definition.PhysicsProfile)` and `VehicleAudio.Configure(definition.AudioProfile)`) → every `IVehicleInputReceiver` on the spawned prefab gets a `PlayerDrivingInput` → `VehicleChaseCamera.SetTarget`. Input action `ResetVehicle` calls `VehicleController.Recover()`.

Results come back through `RunSession.RecordResult(RunResult)`.

## Asset pipeline

`ThirdPartyIntake` (menu: Neon Rift ▸ Assets ▸ Run Third-Party Intake) is the single, re-runnable recipe for supplied models:

- **Vehicles.** A `VehicleModelSetup` asset per car (`Data/Vehicles/ImportSetups`) goes into `VehiclePrefabBuilder`, which produces:
  - a `PF_Vehicle_*` prefab with the source model nested as `Body` (orientation and scale fixed, helper parts hidden);
  - `Wheels/Wheel_XX/Spin` pivots with split meshes (in `Art/Vehicles/<prefab>/`);
  - a `VehicleRig` component;
  - a `VehicleDefinition` added to the `VehicleCatalog`.
- **Buildings.** `BuildingPrefabBuilder` creates `PF_Bld_*` prefabs with:
  - a ground-centre pivot;
  - URP materials and packed textures (in `Art/Buildings/`);
  - an optional box collider and the Environment layer;
  - a `BuildingDefinition` added to the `BuildingCatalog`.
- `GltfMaterialFixer` remaps glTFast clearcoat materials (see D12).

To add a car: add a recipe in `ThirdPartyIntake.VehicleRecipes()` (keywords for wheels, lamps and hidden parts), then run the intake.

## Vehicles

See [VehiclePhysics.md](VehiclePhysics.md) for the model, telemetry, tuning workflow and validation route.

- `VehicleController` (one per car, no other controllers) implements `IVehicleInputReceiver` and reads `DrivingInput` from whatever `IVehicleInputSource` it is given, so player, AI (`RouteAutopilot` is the first) and replay drivers are interchangeable.
- `VehicleDefinition.PhysicsProfile` is the single source of tuning; geometry comes from the prefab's `VehicleRig`. Gameplay prefabs ship with a kinematic `Rigidbody` that becomes dynamic on `Configure`, so a prefab dropped in a scene without a profile stays inert, and Car Select can reuse it as a static model.
- `VehiclePrefabBuilder` adds the Rigidbody, a two-box body collider (`PM_VehicleBody`), the controller and the `Vehicle` layer; re-run with **Neon Rift ▸ Assets ▸ Rebuild Vehicle Prefabs And Catalog**.
- `ExternalStepping` lets tools step a car deterministically (`VehicleTestBench` uses an isolated preview physics scene).
- Physics runs at 100 Hz (`Time.fixedDeltaTime = 0.01`).
- Layers: `Vehicle`(6), `Drivable`(7), `Environment`(8), `Trigger`(9), `Showroom`(10). `Trigger` collides only with `Vehicle`; `Showroom` collides with nothing. Wheel rays ignore `Vehicle`, `Trigger`, `Showroom` and `Ignore Raycast`.
- `Scenes/Dev/TestTrack` provides measured surfaces (1 km straight with 100 m markers, r40 skidpad, 18 m slalom, 10 cm speed bumps, a 15° banked R80 U-turn, a 6° hill and a 12° jump, side and head-on crash walls) and the connected validation route the player spawns on.
