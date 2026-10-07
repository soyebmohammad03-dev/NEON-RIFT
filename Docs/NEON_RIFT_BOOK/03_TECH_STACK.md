# 03 · Tech Stack

> [Index](00_INDEX.md) · Previous: [02 Architecture](02_ARCHITECTURE.md) · Next: [04 Project Structure](04_PROJECT_STRUCTURE.md)

Every entry below was checked against `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, the `.asmdef` references and `using` statements in `Assets/_NeonRift/Scripts/`.

## Engine

| | |
|---|---|
| **WHAT** | Unity **6000.6.4f1** (revision 12bfff696524) |
| **USED FOR** | Everything |
| **IMPORTANT SETTINGS** | Fixed timestep **0.01 s (100 Hz)** (`ProjectSettings/TimeManager.asset`, stored as rational 1411200/141120000); max allowed timestep 0.1 s; Enter Play Mode options: **no domain reload, no scene reload** (`EditorSettings.asset`, options 3); active input handler = Input System only (`ProjectSettings.asset`, `activeInputHandler: 1`); real voices **64**, virtual 512 (`AudioManager.asset`); gravity −9.81 |

## Language

**C#** in 13 assemblies (see [02 Architecture](02_ARCHITECTURE.md)). Async flow uses Unity's `Awaitable` (`GameFlow.TransitionAsync`).

## Packages actually installed (`Packages/manifest.json`)

| Package | Version | Used by the code? | What it is used for / where |
|---|---|---|---|
| `com.unity.render-pipelines.universal` (URP) | 17.6.0 | **Yes** | Rendering. Assets: `Assets/_NeonRift/Settings/Rendering/PC_RPAsset.asset` (HDR, 4× MSAA, shadow distance 90 m, 2 cascades, soft-shadow quality Low, SRP Batcher, GPU Resident Drawer) + `PC_Renderer.asset` (rendering mode Forward+, SSAO and `GroundHazeFeature` renderer features); `Mobile_RPAsset.asset` / `Mobile_Renderer.asset`. Volumes: `NightRun_Base.asset`, `NightRun_Alert.asset`, `NightRun_Lockdown.asset`, `Garage_Volume.asset`. Custom passes: `Scripts/Rendering/GroundHazeFeature.cs` (Render Graph) |
| `com.unity.inputsystem` | 1.19.0 | **Yes** | `Settings/Input/NeonRiftControls.inputactions` → generated `Scripts/Input/NeonRiftControls.cs`; `PlayerDrivingInput`, menus, `InputPlaytest` |
| `com.unity.cinemachine` | 6.6.0 | **Yes** | `CinemachineCamera` + `CinemachineBrain`: chase camera (`VehicleChaseCamera`), Car Select cameras (`VehicleShowroom`), the intro's shot cameras and push-in (`IntroSceneEntry`, `IntroBuilder`) |
| `com.unity.timeline` | 6.6.0 | **Yes** | The intro Timeline `Data/Intro/Intro_NightRun.playable`; custom track `IntroCueTrack`/`IntroCueClip`/`IntroCueBehaviour` (`Scripts/Intro/`) |
| `com.unity.cloud.gltfast` | 6.20.0 | **Yes** (import) | Imports the Sketchfab `.glb` cars, buildings and the Buster Drone. `Editor/GltfMaterialFixer.cs` remaps glTFast clearcoat materials (Decision D12) |
| `com.unity.modules.video` | 1.0.0 | **Yes** | `VideoPlayer` path for a pre-rendered intro (`IntroSettings.Mode.Video`, `IntroSceneEntry.PlayVideo`). Implemented but no clip is supplied |
| `com.unity.ugui` | 2.6.0 | Indirectly | The Bootstrap scene's `EventSystem` with `InputSystemUIInputModule`. No uGUI screens: all UI is UI Toolkit |
| `com.unity.test-framework` | 1.8.0 | **Yes** | EditMode tests (`Scripts/Tests/EditMode/`), `Editor/TestRunReporter.cs` |
| `com.unity.splines` | 2.9.1 | **No** | Installed, but no script references `UnityEngine.Splines`. Routes use the project's own `DrivingRoute`, `RacingLine` and `RoadPath` |
| `com.unity.probuilder` | 6.1.2 | **No** | Installed (listed in older docs as "blockout"), but no code or scene uses it. The city geometry comes from the project's own `MeshBuilder` |
| `com.unity.ai.navigation` | 2.0.12 | **No** | Installed, but no NavMesh is used. AI plans on the project's road graph (`RoadGraph`, `CityNavigation`) |
| `com.unity.collab-proxy` | 2.12.4 | No | Unity Version Control client; Git is the source of truth (Decision D10) |
| `com.unity.ai.assistant`, `com.unity.ai.inference`, `com.unity.pipeline` | — | No (tooling) | Unity AI and editor tooling used during development (MCP bridge); not used by game code |
| `com.unity.ide.rider`, `com.unity.ide.visualstudio` | — | No | IDE integration |

Unity built-in features used directly: **UI Toolkit** (`UnityEngine.UIElements`; `Painter2D` in `CityMinimap`), **AudioMixer**, **PlayerPrefs**, **legacy `Animation`** (Mech Drone hover loop, set up in `Editor/World/DroneModels.cs`), **Animator** (on the intro's shot cameras, driven by Timeline animation tracks), **ReflectionProbe** (baked, box-projected), **LODGroup**, **Unity.Collections** (`Mesh.AcquireReadOnlyMeshData` in `TrafficCarBuilder`).

Not used: `WheelCollider` (deliberately; Decision D15), `ParticleSystem` (no particle systems exist in any scene or script; see [28](28_PARTICLES_AND_VFX.md)), NavMesh, uGUI screens.

## Source control

| Tool | Use | Files |
|---|---|---|
| **Git** | Repository `https://github.com/soyebmohammad03-dev/NEON-RIFT` (`main`) | `.gitignore` |
| **Git LFS** | Models, textures, audio, EXR probes, screenshots, generated district meshes | `.gitattributes` |
| **UnityYAMLMerge** | Merge driver for scenes, prefabs and assets (`merge=unityyamlmerge`) | `.gitattributes`, `Docs/SourceControl.md` |

## Internal / custom frameworks

| Framework | What | Where |
|---|---|---|
| City generator | Data-driven layout → blocks → meshes, props, lights, probes, road graph | `Scripts/Editor/District/` |
| Vehicle physics | Raycast suspension, combined-slip tyres, drivetrain, aero | `Scripts/Vehicles/Physics/` |
| Mission framework | Data + pure state machine + event-id world | `Scripts/Missions/`, `Scripts/Gameplay/Mission/` |
| Multi-stage interactions | `InteractionStep` / `InteractionRun` / `Interactable` | `Scripts/Missions/`, `Scripts/Gameplay/Mission/` |
| Racing AI | Road-graph planner + racing line + traffic sweep | `Scripts/Gameplay/Racing/` |
| Procedural audio | Offline synthesis (engines, heist, mission, intro, score) | `Scripts/Editor/Audio/` |
| Procedural textures and fonts | Facades, asphalt, signage, 5×7 pixel font | `Editor/District/DistrictTextures.cs`, `PixelFont.cs` |
| Custom shaders | `NeonRift/AdditiveGlow`, `GroundHaze`, `NightSky`, `SkylineBackdrop`, `TerminalScreen`, `TowerFacade` | `Assets/_NeonRift/Art/Shaders/` |
| Validation harnesses | Vehicle bench, mission validator and series, route profiler, GPU bench, input playtest | `Scripts/Editor/Vehicles/`, `Scripts/Editor/Validation/`, `Scripts/Gameplay/Mission/NightRunValidator.cs` |
