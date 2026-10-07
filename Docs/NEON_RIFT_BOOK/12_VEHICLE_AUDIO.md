# 12 · Vehicle Audio

> [Index](00_INDEX.md) · Previous: [11 Vehicle Physics](11_VEHICLE_PHYSICS.md) · Next: [13 Vehicle Visuals](13_VEHICLE_VISUALS.md)

## Idea

A car's sound is **data** (`VehicleAudioProfile`), and **one component plays every car** (`VehicleAudio`), driven entirely by the controller's telemetry and events (Decision D19). Every loop is started **once**. Afterwards only volume and pitch change. All engine and chassis sounds are **synthesised in-house**, with no samples and no licences.

## Pieces

| Piece | File | Role |
|---|---|---|
| `VehicleAudioProfile` (+ `EngineAudioSettings`, `ChassisAudioSettings`, `EngineSoundLayer`) | `Assets/_NeonRift/Scripts/Vehicles/Audio/` | Per-car data: engine loops at 5 rpm points × on/off load, levels, smoothing, pitch limits, shift and pop clips, tyre/road/wind beds, thumps, impacts |
| `EngineSoundModel` | `Scripts/Audio/EngineSoundModel.cs` | Pure blend maths: equal-power crossfade between neighbouring rpm layers in **log-rpm** space, equal-power on/off-load crossfade, pitch = rpm / recorded rpm; layers fade out before pitch limits instead of over-stretching (`Layer`, `Evaluate`, `Blend`) |
| `TyreSoundModel` | `Scripts/Audio/TyreSoundModel.cs` | Slip → squeal / scrub intensity (load- and speed-aware): `SlipIntensity`, `SquealDirectionWeight`, `Smooth` |
| `VehicleAudio` | `Scripts/Audio/VehicleAudio.cs` | On the prefab. `Configure` (87) creates sources and calls `Play()` once per loop; `Tick` → `UpdateEngine` (189), `UpdateTyres` (225), `UpdateThumps` (264); `OnGearChanged` (280) and `OnCollided` (288) play one-shots; `SetPlayerView` (149): the player's car is mostly 2D with no Doppler |
| `ShowroomEngine` | `Scripts/Frontend/ShowroomEngine.cs` | Same loops for a parked car in Car Select: idle plus `Blip` |

## What drives each sound

| Sound | Driver |
|---|---|
| Engine layers | Audio rpm = telemetry rpm smoothed 30–50 ms; load = effective throttle (0 on the overrun), cut while shifting |
| Overrun pops | Random one-shots after lifting off above `popMinRpm` (combustion cars only) |
| Tyre squeal | Per-wheel combined slip over `skidSlipRange`, scaled by load and speed |
| Tyre scrub | Combined slip just below or at the peak (`scrubSlipRange`) |
| Loose surface | Wheels on surfaces with grip below `looseSurfaceGrip` (grass, gravel) |
| Road roar, wind | Speed (wind ∝ speed²) |
| Gear shift | `Gearbox.GearChanged` |
| Suspension thump | Compression speed over `thumpSpeed`, harder on the bump stop |
| Impacts | `VehicleController.Collided` impulse: light / medium / heavy, log-scale level |

## Clips (generated)

`VehicleAudioGenerator` (`Scripts/Editor/Audio/VehicleAudioGenerator.cs`, menu **Neon Rift ▸ Audio ▸ Generate Vehicle Audio**) uses `EngineSynth` (`Editor/Audio/EngineSynth.cs`) and `Dsp` (`Editor/Audio/Dsp.cs`):

| Voice | Car | Model |
|---|---|---|
| **V8Road** | SLS AMG | Exhaust pulses at real firing angles (cross-plane V8 order 1-5-4-8-6-3-7-2) split across two banks, pipe resonances, comb pipe, muffler low-pass, saturation |
| **V8Race** | SLS AMG GT3 | Open race exhaust, brighter, more overrun pops |
| **EV** | Terzo Millennio | Motor and gear whine orders, inverter noise, low rumble |

Output folders: `Assets/_NeonRift/Audio/Generated/Engine_V8Road/`, `Engine_V8Race/`, `Engine_EV/` (10 loops each), `Engine_Pops/`, `Tyres/`, `Road/`, `Mechanical/`, `Impacts/`. Every loop holds a whole number of engine cycles (seamless). Engine loops are PCM on desktop for clean pitch-shifting; everything else is ADPCM, Decompress-On-Load, mono. `VehicleAudioPrefabs` (menu **Neon Rift ▸ Audio ▸ Add Vehicle Audio To Prefabs**) adds and routes the component.

## Mixing and voices

- Routing: engine → **Engine** group, tyres → **Tires**, impacts → **SFX** (see [29 Audio](29_AUDIO_SYSTEM.md)).
- A car the camera is not following gets +140 priority on every source (`VehicleAudio.OtherCarPriorityOffset`), so the player's car always keeps its voices.
- On an audio device change, `VehicleAudio.OnAudioConfigurationChanged` restarts the loops.

## Validation

- `VehicleAudioTests` (`Tests/EditMode/VehicleAudioTests.cs`): `EngineModel_IsEqualPower_AndCrossfadesLoad`, `EngineModel_IsContinuous_AndPitchTracksRpm`, `TyreModel_SquealsOnlyWhenSlidingAndMoving`, `CatalogVehicles_HaveValidAudioProfiles`, `LoopingClips_AreSeamless_AndOneShotsStartAndEndSilent`, `VehicleAudio_FollowsTelemetry_WithoutRestartingLoops`, `VehicleAudio_SkidFollowsTyreSlip`.
- `VehicleAudioValidator` (`Scripts/Gameplay/VehicleAudioValidator.cs`): scripted idle → launch → lift-off → brake → idle → handbrake slide → wall impact → recover, recording the listener with `AudioOutputRecorder` and measuring with `AudioSignalAnalysis` (`ClickScore`, `Pitch`, `Brightness`). Results table: `Docs/Audio.md`.
- Invariant: `VehicleAudio.PlayCalls == LoopCount` for the life of the car (no restarts).
