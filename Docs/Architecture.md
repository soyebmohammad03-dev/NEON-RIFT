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
       NeonRift.Vehicles                      (vehicle data, DrivingInput contract)

NeonRift.Editor           editor tooling (validator)
NeonRift.Tests.EditMode   unit tests
```

| Assembly | Owns |
|---|---|
| Vehicles | `VehicleDefinition`, `VehicleCatalog`, `VehicleDisplayStats`, `DrivingInput`, `IVehicleInputSource`, `IVehicleInputReceiver` |
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

## Ready for the vehicle phase

- The vehicle controller lives in `NeonRift.Vehicles`. It implements `IVehicleInputReceiver` and reads `DrivingInput` from whatever `IVehicleInputSource` it is given, so player, AI and replay drivers are interchangeable.
- The physics profile and audio profile become additional ScriptableObjects referenced from `VehicleDefinition`.
- Physics runs at 100 Hz (`Time.fixedDeltaTime = 0.01`).
- Layers: `Vehicle`(6), `Drivable`(7), `Environment`(8), `Trigger`(9), `Showroom`(10). `Trigger` collides only with `Vehicle`; `Showroom` collides with nothing.
- `Scenes/Dev/TestTrack` provides measured surfaces: 1 km straight with 100 m markers, r40 skidpad, 18 m slalom, 10 cm speed bumps, a 15° banked R80 U-turn, a 6° hill and a 12° jump, and side and head-on crash walls.
