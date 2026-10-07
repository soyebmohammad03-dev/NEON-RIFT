# 26 · Weather and Atmosphere

> [Index](00_INDEX.md) · Previous: [25 Lighting](25_LIGHTING.md) · Next: [27 Water and Effects](27_WATER_AND_EFFECTS.md)

## Summary

| Effect | Status |
|---|---|
| **Rain (falling)** | **Not currently implemented in the completed version.** There is no rain system, particle rain or rain audio |
| Weather states, wind, lightning, snow | **Not implemented** |
| Distance fog | Implemented (URP / built-in `RenderSettings` fog) |
| Ground-hugging height haze | Implemented (custom Volume override + Render Graph pass) |
| "After the rain" wet look | Implemented as **materials and textures only** (damp asphalt patches, glassy puddles, reflections). No wetness simulation |
| Steam from manholes | Implemented as static soft plume meshes |
| Night sky with clouds and moon | Implemented as a sky shader |

## Distance fog

`NightRunBuilder.BuildLighting` (`Assets/_NeonRift/Scripts/Editor/District/NightRunBuilder.cs:668`): `RenderSettings.fog = true`, mode **ExponentialSquared**, density **0.0011**, coloured from the horizon light-pollution glow so distance dissolves into the sky (the older purple fog made distant towers black cut-outs).

## Ground haze (`NeonRift.Rendering`)

| | |
|---|---|
| **WHAT** | Exponential **height fog** layered on top of distance fog |
| **CODE** | `Assets/_NeonRift/Scripts/Rendering/GroundHaze.cs` (a `VolumeComponent` override, so profiles can blend it), `Scripts/Rendering/GroundHazeFeature.cs` (`ScriptableRendererFeature` with a Render Graph pass `HazePass`, `RecordRenderGraph`) |
| **SHADER** | `Assets/_NeonRift/Art/Shaders/NeonRift_GroundHaze.shader` |
| **HOW** | After opaques and the sky, before transparents: one full-screen triangle reads the camera depth texture and integrates exponential height fog **analytically** along each view ray, then alpha-blends over the colour target (no extra colour copy) |
| **SETTINGS** | Clear for the first 40 m; density 0.006/m at street level, falling by e every 22 m of height; light-pollution colour (fog × 1.35), capped at 78 % opacity so silhouettes never vanish. The lockdown profile tints it red and darker |
| **REGISTERED** | On both `PC_Renderer.asset` and `Mobile_Renderer.asset` (`NightRunBuilder.EnsureHazeFeature`) |

## Sky and clouds

`NeonRift/NightSky` (`Art/Shaders/NeonRift_NightSky.shader`): deep navy zenith, a low sodium-brown light-pollution band, a two-scale overcast lit from below, and moonlit thin cloud edges. Material `c.Kit.NightSky`, assigned as `RenderSettings.skybox`; ground colour and moon direction set by `BuildLighting`.

## Skyline haze

`SkylineBackdrop` rings (1.7, 2.1, 2.55 km) are each hazier than the one in front (`_HazeColor` = fog colour), drawn unfogged by `NeonRift/SkylineBackdrop` so the far city keeps its silhouettes.

## Steam

Steam vents: 30 % of Kowloon and Lowtown manholes (25 total) get two crossed soft plume quads with the additive `District_Steam` material (`Docs/Environment.md`, city detail pass). Static geometry, not particles.

## Light shafts and beams

The drone searchlight beams and street-lamp haze cones are additive meshes (`NeonRift/AdditiveGlow`), not volumetric lighting. See [28](28_PARTICLES_AND_VFX.md).

## Related

[25 Lighting](25_LIGHTING.md) · [27 Water and Effects](27_WATER_AND_EFFECTS.md)
