# 28 · Particles and VFX

> [Index](00_INDEX.md) · Previous: [27 Water and Effects](27_WATER_AND_EFFECTS.md) · Next: [29 Audio System](29_AUDIO_SYSTEM.md)

## Particle systems

**There are no Unity `ParticleSystem` components in the completed project.** This was checked by counting component types in every scene file (`Bootstrap`, `Frontend`, `CarSelect`, `NightRun`, `Dev/TestTrack`) and every prefab under `Assets/_NeonRift/Prefabs/`, and by searching all scripts for `ParticleSystem`. The VFX Graph package is not installed.

> An older note in `Docs/NightRun.md` ("4 small particle systems") describes the vertical-slice build. The current generated scene contains none.

## How the visual effects are made instead

All effects are **geometry + custom shaders + script-driven material properties**, which keeps them deterministic and cheap.

| Category | Technique | Code / asset |
|---|---|---|
| Glows, light shafts, beams, haze cones, steam | Additive unlit quads and cones with `NeonRift/AdditiveGlow` (texture alpha shapes the glow; fog fades it out) | `Assets/_NeonRift/Art/Shaders/NeonRift_AdditiveGlow.shader`; materials from `DistrictKit.Glow` (`Scripts/Editor/District/DistrictKit.cs:204`) |
| Screens, holograms | `NeonRift/TerminalScreen` driven per renderer with `MaterialPropertyBlock` | `NeonRift_TerminalScreen.shader`, `TerminalDisplay.cs` |
| Strobes, LEDs, beacons, signals | Emission colour set through property blocks or shared runtime materials | `SecurityDrones`, `SecurityCamera`, `SecurityAlarm`, `TrafficSignalNetwork` |
| Lockdown spread | Per-group delayed colour fades (140 m/s wave) | `SecurityLightGroup`, `LockdownScreens` |
| Data Core extraction | Pulses moved along a cable path, scan rings, colour ramps, flashes on interference | `DataCoreChamber.UpdateLink`, `UpdateCore`, `UpdateHolograms` |
| Atmosphere | Full-screen Render Graph pass | `GroundHazeFeature` + `NeonRift_GroundHaze.shader` |
| Post FX | Bloom, ACES, split toning, vignette, grain, chromatic aberration (lockdown) | URP Volumes |
| Screen-space UI effects | Flashes, fades, letterbox, banners, toasts | `IntroOverlay`, `MissionHud` (USS transitions) |

## Not implemented

Tyre smoke, sparks on impact, exhaust smoke, debris, rain and splashes.
