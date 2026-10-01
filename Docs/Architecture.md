# Architecture

## Assemblies

Dependencies point downwards only. Data modules know nothing about flow or UI.

```
NeonRift.Frontend   NeonRift.Gameplay         (scene-level features)
        \              /
         NeonRift.Game                        (composition root, flow, session, config)
        /      |       \
NeonRift.Input |  NeonRift.Missions           (player input bridge / mission data)
        \      |
       NeonRift.Vehicles     NeonRift.World   (vehicle data + rig / building catalog)
                 \             /
                  NeonRift.Core               (shared primitives: AssetLicense)

NeonRift.Editor           editor tooling (validator, third-party intake, prefab builders)
NeonRift.Tests.EditMode   unit tests
```

| Assembly | Owns |
|---|---|
| Core | `AssetLicense` |
| Vehicles | `VehicleDefinition`, `VehicleCatalog`, `VehicleDisplayStats`, `VehicleRig`/`WheelRig`, `DrivingInput`, `IVehicleInputSource`, `IVehicleInputReceiver` |
| World | `BuildingDefinition`, `BuildingCatalog`, `BuildingTier` (Hero / Midground / Skyline) |
| Missions | `MissionDefinition`, `RunResult`, `MissionOutcome` |
| Input | Generated `NeonRiftControls` (from `Settings/Input/NeonRiftControls.inputactions`), `PlayerDrivingInput` |
| Game | `GameRoot`, `GameFlow`/`IGameFlow`, `GameContext`, `ISceneEntryPoint`, `RunSession`, `GameConfig`, `LoadingOverlay`, `BootstrapLoader` |
| Frontend | `TitleScreen`, `CarSelectScreen`, `VehicleShowroom` |
| Gameplay | `MissionSceneEntry`, `VehicleSpawnPoint` |

## Scene model

- **Bootstrap** (build index 0) is loaded for the whole session. It holds `GameRoot`, the loading overlay and the EventSystem.
- Exactly one **content scene** is loaded additively on top and set active: Frontend, CarSelect, or a mission scene.
- Each content scene has one root component implementing `ISceneEntryPoint`. `GameFlow` calls `Enter(GameContext)` after load and `Exit()` before unload. Scenes never look up services globally.
- Transition: fade in → `Exit` → unload → load additive → set active → unload unused assets → `Enter` → fade out. Requests made during a transition are ignored.
- Playing from a content scene in the editor: `BootstrapLoader` loads Bootstrap additively, and `GameFlow` adopts the open scene. For missions it picks the first catalog vehicle and the mission whose `sceneName` matches.

## Data flow for a run

`CarSelectScreen` → `RunSession.SelectVehicle` / `SelectMission` → `IGameFlow.StartMission()` → mission scene → `MissionSceneEntry.Enter` → `VehicleSpawnPoint.Spawn(selected definition)` → every `IVehicleInputReceiver` on the spawned prefab gets a `PlayerDrivingInput`.

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

## Ready for the vehicle phase

- The vehicle controller lives in `NeonRift.Vehicles`. It implements `IVehicleInputReceiver` and reads `DrivingInput` from whatever `IVehicleInputSource` it is given, so player, AI and replay drivers are interchangeable.
- The physics profile and audio profile become additional ScriptableObjects referenced from `VehicleDefinition`.
- Physics runs at 100 Hz (`Time.fixedDeltaTime = 0.01`).
- Layers: `Vehicle`(6), `Drivable`(7), `Environment`(8), `Trigger`(9), `Showroom`(10). `Trigger` collides only with `Vehicle`; `Showroom` collides with nothing.
- `Scenes/Dev/TestTrack` provides measured surfaces: 1 km straight with 100 m markers, r40 skidpad, 18 m slalom, 10 cm speed bumps, a 15° banked R80 U-turn, a 6° hill and a 12° jump, and side and head-on crash walls.
