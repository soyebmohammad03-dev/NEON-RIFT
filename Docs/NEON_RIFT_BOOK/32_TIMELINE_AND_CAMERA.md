# 32 · Timeline and Camera (cinematics)

> [Index](00_INDEX.md) · Previous: [31 Cinematic Intro](31_CINEMATIC_INTRO.md) · Next: [33 UI and HUD](33_UI_AND_HUD.md)

This chapter covers how **Timeline** and **Cinemachine** are used for cinematic camera work. Gameplay cameras are in [38 Camera System](38_CAMERA_SYSTEM.md).

## Where Timeline is used

Only in the **intro**: one `PlayableDirector` in the NightRun scene (on the `Intro` root object), playing `Assets/_NeonRift/Data/Intro/Intro_NightRun.playable`. It is configured as `playOnAwake = false`, `extrapolationMode = Hold`, `timeUpdateMode = GameTime` (`IntroBuilder.Build`).

## The Timeline is generated from code

`IntroBuilder` keeps a **shot table** (`Shot` struct: name, start, end, ease, FOV, from, to, look-from, look-to) and a **cue table** (`Cue` struct: kind, start, end, text, subtitle). `BuildTimeline` (`Assets/_NeonRift/Scripts/Editor/Intro/IntroBuilder.cs:322`) rebuilds the asset **in place**, so it keeps its GUID:

| Track | Built from | Notes |
|---|---|---|
| One **AnimationTrack per shot camera** | `MoveClip` (436): an `AnimationClip` keying position and a look rotation (quaternion), eased | Each `Shot_*` GameObject has a `CinemachineCamera` and an `Animator` |
| **CinemachineTrack** | One shot per camera, cut or blended | Bound to the main camera's `CinemachineBrain` |
| **IntroCueTrack** | `IntroCueClip` per cue | Bound to `IntroSceneEntry` (`IIntroCueHandler`) |
| Audio tracks | Ambience, Soundtrack, Riser, Hits, Scan | Sources created by `IntroBuilder` and routed at runtime (`IntroSceneEntry.RouteAudio`) |

## The custom cue track (`Assets/_NeonRift/Scripts/Intro/`)

| Type | Role |
|---|---|
| `IntroCueKind` | `FadeIn`, `Dip`, `Card`, `Title`, `Surveillance`, `Headlights`, `Ignition`, `Departure`, `GarageLights`, `DoorOpen`, `Flash`, `SkipHint`, `Letterbox` |
| `IntroCueTrack` | `TrackAsset` of cue clips, bound to the handler |
| `IntroCueClip` | `PlayableAsset` with `kind`, `text`, `subtitle`; `CreatePlayable` |
| `IntroCueBehaviour` | `ProcessFrame` → `IIntroCueHandler.OnCueStart / OnCue(progress) / OnCueEnd`; `OnBehaviourPause` |
| `IIntroCueHandler` | Implemented by `IntroSceneEntry` (`OnCueStart` 374, `OnCue` 401, `OnCueEnd` 437) |

Everything is driven explicitly each frame from the Timeline time, so **scrubbing in the editor and skipping at runtime both land on the right state** (`IntroOverlay` class summary).

## Cinemachine in cinematics

| Camera | Where | Use |
|---|---|---|
| 25 shot cameras (`Shot_*`, priority 0) | `Intro/Shots` | Timeline-animated, Cinemachine-cut |
| `PushCamera` (with `CinemachineHardLookAt`) | `Intro/PushCamera` | Confirm push-in, a 4.6 s Catmull-Rom path through the garage door (`IntroSceneEntry.UpdatePushIn`, `CatmullRom`) |
| Car Select cameras (Hero, Swap, Depart, Opening) | `CarSelect` scene | Switched by priority (`VehicleShowroom.SetCamera`); blends in `Settings/Rendering/Garage_CameraBlends.asset`; the Opening camera is entered with a **Cut** (the brain's default blend is temporarily set to Cut) so the first frame matches the intro's last |
| Chase camera | NightRun mission | `VehicleChaseCamera` writes the pose into a `CinemachineCamera` before the brain updates (Decision D18) |

All shot cameras use the far clip `SkylineBackdrop.RequiredFarClip` (3.5 km), so the skyline rings are visible.

## Scripted camera beats outside Timeline

| Beat | Code |
|---|---|
| Theft beat on `core.breached` (2.6 s swing, letterbox, time scale 0.45) | `MissionDirector.StartBeat` / `UpdateBeat` → `VehicleChaseCamera.CinematicBeat` |
| Data Core extraction framing | `DataCoreChamber.UpdateCamera` → `VehicleChaseCamera.SetFraming` |
| Gate hack framing | `GateLockSystem.UpdateCamera` → `SetFraming` |
| Lockdown focus on the alert origin | `MissionDirector.OnSecurityChanged` → `VehicleChaseCamera.Kick`, `Focus` |
