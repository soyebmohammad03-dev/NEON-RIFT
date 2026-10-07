# 27 · Water and Effects

> [Index](00_INDEX.md) · Previous: [26 Weather and Atmosphere](26_WEATHER_AND_ATMOSPHERE.md) · Next: [28 Particles and VFX](28_PARTICLES_AND_VFX.md)

## Water

| Feature | Status |
|---|---|
| Water bodies (harbour water, rivers, water surfaces, water shader) | **Not currently implemented in the completed version.** No water surface or water shader exists in `Assets/_NeonRift/` (the Unity built-in "Water" layer is unused) |
| Splashes, wet tyre spray, rain ripples | **Not implemented** |
| Puddles on the road | **Implemented as texture and material detail only** |
| Fountain basin in courtyards | Static lit geometry (`CityInfill.Courtyard`), no simulated water |

### Wet asphalt and puddles: how it is done

`DistrictTextures.Asphalt` (`Assets/_NeonRift/Scripts/Editor/District/DistrictTextures.cs:128`) bakes the road texture procedurally. Damp patches come from low-frequency noise, and **"after rain" puddles** are low-frequency blobs over a threshold with soft edges (comment at line 141: *"After rain: puddles (low-frequency blobs over a threshold, soft edges) inside damp patches"*). The puddle and damp areas get **higher smoothness** (about 0.6, more glassy in puddles), so the **baked box-projected reflection probes** and real-time lamps streak lamp and sign highlights across the road. That is what reads as "wet neon streets" in the screenshots. It is a static material look; nothing dries, ripples or splashes.

## Other effects

| Effect | Implementation | File |
|---|---|---|
| Neon glow and bloom | Emissive materials + URP Bloom (threshold 1.05) | `DistrictKit`, `NightRun_Base.asset` |
| Light shafts, beacon columns, lamp haze cones | `NeonRift/AdditiveGlow`: additive, unlit, fog-aware glow shaped by a texture's alpha | `Assets/_NeonRift/Art/Shaders/NeonRift_AdditiveGlow.shader` |
| Objective beacons (cyan), rival work beacons (magenta) | 420 m-tall `Beacon` columns (`NightRunBuilder.Beacon`), shown only while relevant | `District_BeaconCyan`, `District_BeaconMagenta` |
| Terminal screens and holograms | `NeonRift/TerminalScreen`: stage strip, scrolling rows, progress bar, scan line, tearing on misses and interference, via `MaterialPropertyBlock` | `Art/Shaders/NeonRift_TerminalScreen.shader`, `Scripts/Gameplay/Mission/TerminalDisplay.cs` |
| Data Core pulses, scan rings, holograms, strobes | Script-animated meshes and emission | `Scripts/Gameplay/Mission/DataCoreChamber.cs` |
| Lockdown wave | Emission colour change by distance from the theft | `SecurityLightGroup`, `LockdownScreens` |
| Steam vents | Crossed soft plume quads (`District_Steam`) | `CityProps` / `DistrictKit` |
| Drone searchlight beams | Cone mesh `Drone_Beam` scaled to the target each frame | `SecurityDrones`, `IntroCityLife` |
| Mech Drone thruster flames | Additive glow material `Drone_Mech_Flame` (the model's Fire_Alpha texture) | `Scripts/Editor/World/DroneModels.cs` |
| Camera shake | Impact and speed shake | `VehicleChaseCamera` |
| Intro flashes, fades, letterbox, CCTV overlay | UI Toolkit overlay | `Scripts/Intro/IntroOverlay.cs` |

See also [28 Particles and VFX](28_PARTICLES_AND_VFX.md).
