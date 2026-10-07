# 38 · Camera System

> [Index](00_INDEX.md) · Previous: [37 Animation Systems](37_ANIMATION_SYSTEMS.md) · Next: [39 Performance](39_PERFORMANCE.md)

## Gameplay chase camera: `VehicleChaseCamera`

`Assets/_NeonRift/Scripts/Gameplay/VehicleChaseCamera.cs`, `[DefaultExecutionOrder(-50)]`, on a `CinemachineCamera`. It **computes the pose itself** and writes it into the Cinemachine camera before the `CinemachineBrain` updates; Cinemachine remains the blending layer (Decision D18). It follows the body rigidly in position and **never damps roll or pitch**, so you see what the chassis is doing.

| Behaviour | Field (code default) | Night Run value (set by `NightRunBuilder.BuildMissionRig`, lines 931–941) |
|---|---|---|
| Distance / height behind the car | `distance` 5.6 m, `height` 1.75 m | **6.2 m / 2.05 m** |
| Speed pull-back / height change | `speedDistance` 1.2, `speedHeight` −0.25 | — |
| Heading swing toward the direction of travel when sliding | `travelHeadingWeight` 0.55 above `travelHeadingMinSpeed` 4 m/s, `yawSmoothTime` 0.22 s | — |
| **Look-ahead** (leads forward travel only, fading in over 0–3 m/s) | `lookAheadTime` 0.18 s, `maxLookAhead` 6 m | **0.26 s / 8 m** |
| Speed FOV | `baseFieldOfView` 58°, `speedFieldOfView` +9° | **60° + 12°** |
| Acceleration lag | `accelerationLag` 0.7, smooth 0.25 s | — |
| Collision | Sphere cast (`collisionRadius` 0.25, `minDistance` 1.6) against `obstacleLayers` = Drivable (7) + Environment (8) | — |
| **Cornering bank** | `rollPerG` 1.6° per g of lateral acceleration, `maxRoll` 3°, eased 0.3 s, faded out below 8 m/s | — |
| Framing (heist and gate) | `frameSwing` 38°, `frameRise` 3.6 m, `framePullBack` 5.5 m, `frameBlend` 1.4 s | — |
| **Theft beat** | `beatSwing` 55°, `beatRise` 3.4 m, `beatPullBack` 5 m, `beatBlend` 0.5 s | — |
| Shake | `impactShake` 0.05, `speedShake` 0.012, `shakeDecay` 3.5, `maxShake` 0.45 | — |

Public API: `SetTarget`, `Snap` (after a teleport or recovery), `Kick` (impact shake), `Focus(point, seconds, weight)`, `CinematicBeat`, `SetFraming(target, weight)`; `OnCollided` reacts to car impacts. **Perspective** projection only; the camera far clip is extended for the skyline (`SkylineBackdrop.RequiredFarClip` = 3.5 km), and `CameraCullDistances` sets per-layer culling (Detail 180 m, Clutter 90 m).

## Special framings during the mission

| Moment | Caller | Effect |
|---|---|---|
| Data Core chamber opening and extraction | `DataCoreChamber.UpdateCamera` | Slow, wide framing of the car and the core; the player keeps control |
| Gate hack / release within 40 m | `GateLockSystem.UpdateCamera` | Camera rises and frames the gate, near-straight behind the car |
| Interference | `MissionDirector.OnInteractionFeedback` | `Kick(0.12)` |
| Lockdown | `MissionDirector.OnSecurityChanged` | `Kick(0.35)`, `Focus` on the alert origin + 20 m |
| Theft beat (`core.breached`) | `MissionDirector.StartBeat` | Swing to the far side of the car from the core, letterbox, time scale 0.45 for 2.6 s (unscaled timing, restored at the end) |
| Recover / out of bounds | `MissionSceneEntry` | `Snap()` |

## Car Select cameras

`VehicleShowroom` switches Cinemachine cameras by priority (`SetCamera`): **Hero** (27° lens, slow dolly drift `heroDrift` ±9° over 26 s), **Swap** (low side shot during a switch), **Depart** (follow camera for the drive-out), **Opening** (headlight close-up that matches the intro's final frame; entered with a Cut blend). Blends: `Assets/_NeonRift/Settings/Rendering/Garage_CameraBlends.asset`. Cameras are created by `GarageBuilder` ([15](15_CAR_SELECTION.md)).

## Intro cameras

25 Timeline-animated shot cameras cut by a Cinemachine track, plus the push-in camera ([31](31_CINEMATIC_INTRO.md), [32](32_TIMELINE_AND_CAMERA.md)).

## Test track

`Scenes/Dev/TestTrack.unity` uses the same `VehicleChaseCamera`, wired by `ValidationRouteBuilder.ConfigureScene`.
