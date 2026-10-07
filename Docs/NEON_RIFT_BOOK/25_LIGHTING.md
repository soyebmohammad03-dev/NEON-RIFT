# 25 · Lighting

> [Index](00_INDEX.md) · Previous: [24 Environmental Interactions](24_ENVIRONMENTAL_INTERACTIONS.md) · Next: [26 Weather and Atmosphere](26_WEATHER_AND_ATMOSPHERE.md)

## How the night-city look is produced

The look is **emissive-first**. Windows, neon, signs, lamp heads and ground pools are emissive materials that bloom, and **real-time lights are budgeted** to the ones near the camera. There is **no global illumination**: `NightRun_Lighting.lighting` has baked GI and realtime GI both off (`NightRunBuilder.LightingSettingsAsset`: "Real-time lights + baked reflection probes for now; baked GI comes with the mobile lighting pass"). Wet-looking roads come from **baked box-projected reflection probes**.

All of it is set up by `NightRunBuilder.BuildLighting` (`Assets/_NeonRift/Scripts/Editor/District/NightRunBuilder.cs:668`) and `ConfigurePipeline` (1143).

## Components of the look

| Element | Implementation | Where |
|---|---|---|
| **Pipeline** | URP Forward+, HDR, 4× MSAA (+ SMAA High on the camera), SSAO renderer feature, shadow distance 90 m, **2 cascades**, soft shadows quality **Low** (from the GPU bench, −37 % GPU), SRP Batcher, GPU Resident Drawer | `Assets/_NeonRift/Settings/Rendering/PC_RPAsset.asset`, `PC_Renderer.asset`; `NightRunBuilder.ConfigurePipeline` |
| **Sky** | `NeonRift/NightSky` shader: navy zenith, low sodium-brown light-pollution band, two-scale overcast lit from below, moon direction | `Art/Shaders/NeonRift_NightSky.shader`, `RenderSettings.skybox` |
| **Moon** | One directional light, intensity 0.16, soft shadows, rotation (34°, −35°) | `BuildLighting` |
| **Ambient** | Trilight: sky (0.04, 0.044, 0.06), equator (0.05, 0.042, 0.038), ground (0.02, 0.016, 0.012): cool sky, warm street bounce | `BuildLighting` |
| **Street lights** | 787 street and flood lamps (600–680 cd, 32 m range), each with a **disabled** real-time spot; **`LightBudget`** keeps the **56 nearest within 150 m** on (front-weighted, `behindPenalty` 2.2, re-evaluated every 0.2 s, 0.6 s fades) and gives the **2 nearest in front** soft shadows (`maxShadowed`, with hysteresis) | `Scripts/Gameplay/City/LightBudget.cs`; prefabs `PF_Prop_StreetLight_{Led,Sodium,Warm}` |
| **Lamp kinds** | LED (~4000 K neutral), Sodium, Warm (`DistrictKit.LampKind`, `LampColours`) | `Scripts/Editor/District/DistrictKit.cs` |
| **Distant lamps** | Emissive heads, ground pools and haze cones (`CityProps.LampHaze`), so streets read without real lights | `CityProps.cs` |
| **Neon** | Emissive materials up to about 2.8 HDR in cyan, magenta, warm white, amber, red (violet and lime removed) | `DistrictKit.CreateMaterials`; textures `DistrictTextures` |
| **Towers** | `NeonRift/TowerFacade`: procedural lit office windows by suite, lobby band, uplight; crown strips and washes; aviation lights | `Art/Shaders/NeonRift_TowerFacade.shader`, `TowerDressing.cs` |
| **Shopfronts** | Interiors lit like shops (warm white, fluorescent, halogen); colour stays in the sign bands | `DistrictTextures.Shopfronts` |
| **Reflection probes** | 63 baked, box-projected (128 px in Sector 7 and Spire, 64 px elsewhere) along arterials and Sector 7 streets; the crew garage has its own | `NightRunDistrict.BuildReflectionProbes`, `NightRunBuilder.BakeProbes`; EXRs in `Assets/_NeonRift/Scenes/NightRun/` |
| **Vehicle lights** | Headlights (the player's left one shadowed), tail spot (brake/reverse) | `Scripts/Vehicles/VehicleLights.cs` |
| **Mission lights** | Core floods (`Flood_NW`, `Flood_SE`), tunnel, gate and beacon lights: mostly off until needed | `NightRunBuilder.BuildCompound`, `Light()` helper |
| **Emergency lighting** | Security strips turning red with the lockdown wave; rotating beacons and sirens; drone red/blue strobes and searchlights | `SecurityLightGroup`, `SecurityAlarm`, `SecurityDrones` |
| **Garage** | 19 hall lights (intro only) struck in banks by `CrewGarage`; the Car Select hall has its own key light and rims | `NightRunBuilder.Garage.cs`, `GarageBuilder.cs` |

## Post-processing (URP Volumes)

| Volume | File | Contents (from `BuildLighting` and `Docs/Environment.md`) |
|---|---|---|
| `NightRun_Base` | `Settings/Rendering/NightRun_Base.asset` | Bloom threshold 1.05 / intensity 0.34 / high-quality filtering; ACES tonemapping; **fixed post-exposure** (URP has no eye adaptation); contrast 14, saturation −8, white balance −4; split toning (neutral-cool shadows, warm highlights); lift/gamma/gain; vignette 0.22; film grain 0.08; `GroundHaze` |
| `NightRun_Alert` | `NightRun_Alert.asset` | Alert tint, blended at weight 0.25 |
| `NightRun_Lockdown` | `NightRun_Lockdown.asset` | Colour filter (1, 0.93, 0.92), contrast 22, dark red vignette, bloom 0.55, chromatic aberration 0.07, darker haze. "The red comes from the security lights themselves, not the grade" |
| `Garage_Volume` | `Garage_Volume.asset` | Car Select look |
| Intro surveillance | `Data/Intro/Intro_Surveillance.asset` | Green CCTV grade for the intro's surveillance shot |

Blending: `SecurityPostEffects` (`Scripts/Gameplay/Mission/SecurityPostEffects.cs`). **Stop NaN** is enabled on scene cameras as a safeguard (Decision D13).

## Fog and haze

Exponential-squared distance fog (density 0.0011, in the colour of the horizon glow), plus the custom **ground haze**. See [26](26_WEATHER_AND_ATMOSPHERE.md).

## Performance trade-offs (measured; `Docs/Environment.md`, `Docs/Performance.md`)

- Each shadowed lamp re-renders the combined block meshes inside its cone: 2 shadowed lamps add about 0.45 M triangles and 200 shadow casters over 0. `maxShadowed` is the dial (desktop 2, mobile 0).
- Moon shadow cascades 4 → 2 cut 4.53 M → 3.13 M triangles at the spawn view.
- Soft-shadow quality High → Low: −37 % GPU in the lockdown escape, with no visible difference (mean pixel difference 0.48/255).
- MSAA 4× and SSAO were kept for the look (2× saved 7–12 %, SSAO off saved 11 %).
- Real-time lights peak at about 70 in the lockdown (beacons, strobes, drone searchlights) on top of the lamp budget.

## Tools

`LookDevShots` (**Neon Rift ▸ Night Run ▸ Capture Look-Dev Shots (Play Mode)**) and `ViewCapture` (`Scripts/Editor/District/ViewCapture.cs`) for before/after captures; `GpuBench` for A/B GPU measurements ([39](39_PERFORMANCE.md)).
