# Audio

Vehicle sound is driven entirely by `VehicleController` telemetry and events. Each car's character is data (`VehicleAudioProfile`); one component (`VehicleAudio`) plays every car.

## Architecture

| Piece | Assembly | Role |
|---|---|---|
| `VehicleAudioProfile` (+ `EngineAudioSettings`, `ChassisAudioSettings`, `EngineSoundLayer`) | Vehicles | Per-car data: engine loops at several rpm points × on/off load, levels, smoothing, pitch limits, shift/pop clips, tyre/road/wind beds, thumps, impacts. Referenced from `VehicleDefinition.AudioProfile`. |
| `EngineSoundModel` | Audio | Pure blend math: equal-power crossfade between neighbouring rpm layers (log-rpm), equal-power on/off-load crossfade, pitch = rpm / recorded rpm, layers fade out before pitch limits instead of over-stretching. |
| `TyreSoundModel` | Audio | Slip → squeal/scrub intensity (load- and speed-aware). |
| `VehicleAudio` | Audio | On the vehicle prefab. `Configure` creates the sources and calls `Play()` once per loop; afterwards only volume and pitch change. Gear changes (`Gearbox.GearChanged`) and impacts (`VehicleController.Collided`) play one-shots. |
| `AudioMixerConfig` + `AudioMixerService` | Audio | Mixer routing groups, state snapshots, user volume channels. The service is created by `GameRoot` and exposed as `GameContext.Audio`. |
| `AudioOutputRecorder`, `AudioSignalAnalysis` | Audio | Dev tools: record the listener mix; measure clicks, loop seams, pitch, brightness; write WAV. |

Spawn path: `VehicleSpawnPoint.Spawn` configures physics, then `VehicleAudio.Configure(definition.AudioProfile, controller)`. `MissionSceneEntry` marks the player's car (`SetPlayerView`: mostly 2D, no Doppler, so the chase camera does not swing the mix).

### Engine
- Loops recorded at five rpm points, each with an on-load and an overrun version (10 per engine).
- Audio rpm = telemetry rpm with a short smoothing (30–50 ms): shifts drop quickly but never step.
- Load = effective throttle (0 when the engine is braking), cut to `shiftLoad` while shifting, with separate attack/release times.
- Level rises with normalised rpm (`idleVolume` → 1) and with load (`offLoadVolume` → 1).
- Overrun pops: random one-shots shortly after lifting off above `popMinRpm` (combustion cars only).

### Driving sounds
| Sound | Driver |
|---|---|
| Tyre squeal | Per-wheel combined slip over `skidSlipRange`, scaled by load and speed; pitch rises with slip |
| Tyre scrub (heavy braking, understeer) | Combined slip just below/at the peak (`scrubSlipRange`) |
| Loose surface | Wheels on surfaces with grip below `looseSurfaceGrip` (grass, gravel) |
| Road roar / wind | Speed (wind ∝ speed²) |
| Gear shift | `Gearbox.GearChanged` |
| Suspension thump | Wheel compression speed over `thumpSpeed`, harder on the bump stop |
| Impacts | `VehicleController.Collided` impulse: light / medium / heavy, level on a log scale |

### Mixer (`Audio/Mixer/NeonRiftMixer.mixer`, built by **Neon Rift ▸ Audio ▸ Build Audio Mixer**)

```
Master            [MasterVolume]
├─ Gameplay       [EffectsVolume]
│  ├─ Engine
│  ├─ Tires
│  ├─ SFX
│  └─ Ambience
├─ Music          [MusicVolume]
│  └─ Score
└─ UI             [UIVolume]
```

User volumes are exposed parameters on the parent groups (`AudioMixerService.SetVolume`). Snapshots act on the leaf groups, so user settings and state ducking never fight:

| Snapshot | Use | Offsets (dB) |
|---|---|---|
| Gameplay | driving | Tires −3, Ambience −4 |
| Menu | title, car select, pause | Engine −12, Tires −20, SFX −6, Ambience −15 |
| Results | results screen | Engine −18, Tires −25, SFX −8, Ambience −12 |
| Lockdown | city lockdown / siren | Engine −2, Ambience −4 |
| Ducked | mission alerts, dialogue | Engine −8, Tires −8, Ambience −8, Score −10 |

`GameRoot` switches Menu ↔ Gameplay on game-state changes; mission code will drive Results / Lockdown / Ducked.

## Assets and licences

All audio is **synthesised in-house** by `VehicleAudioGenerator` (**Neon Rift ▸ Audio ▸ Generate Vehicle Audio**). It is deterministic and re-runnable, and the output is project-owned, with no third-party samples and no licence obligations.

- **Combustion engines:** exhaust pulses at real firing angles. The cross-plane V8 firing order 1-5-4-8-6-3-7-2 is split across two exhaust banks, which produces the burble. Each bank has pipe resonances, a comb-filter pipe, a muffler low-pass and saturation. There are two voices:
  - **V8Road** (SLS AMG): muffled, valved road exhaust.
  - **V8Race** (SLS GT3): open race exhaust, brighter and rawer, with more overrun pops.
- **EV** (Terzo Millennio): motor and gear whine orders, inverter noise and low rumble.
- **Loops:** every loop holds a whole number of engine cycles and is filtered to steady state, so it loops with no seam.
- **Beds and one-shots:** filtered-noise beds and modal one-shots for shifts, pops, thumps and impacts. All one-shots fade in and out.
- **Import:** engine loops are PCM on desktop for the cleanest pitch-shifting. Everything else, and every clip on mobile, is ADPCM. All clips are Decompress-On-Load mono.

If the project later wants recorded engines, free sources with clear licences include CC0 recordings on Freesound.org (filter by licence) and Sonniss GDC bundles (royalty-free). Drop them into a new `VehicleAudioProfile`; no code changes are needed.

## Validation results (engine-audio phase)

Play Mode on `TestTrack`, scripted by `VehicleAudioValidator` (idle → launch → lift-off → brake to stop → idle → handbrake slide → wall impact → recover), recording the real listener mix at 48 kHz:

| | SLS AMG (road V8) | SLS GT3 (race V8) | Terzo (EV) |
|---|---|---|---|
| Idle | 800 rpm, −35 dBFS | 1100 rpm, −35 dBFS | silent (EV) |
| Launch: audio rpm vs engine rpm | 5032 vs 5054 avg | 5340 vs 5367 | 4618 vs 4688 |
| Load → level / brightness (idle → launch) | −35 → −21 dB, 0.022 → 0.098 | −35 → −19.5 dB, 0.054 → 0.182 | → −24.6 dB, 0.154 |
| Output pitch follows rpm | yes (per-bank rhythm, drops at each upshift) | yes | yes, order 6 (7194 rpm → 706 Hz) |
| Lift-off load blend | 0.05 | 0.05 (+ overrun crackle) | 0.10 (regen) |
| Squeal: handbrake slide / straight braking | 1.00 / low | 1.00 / 0.30 | 1.00 / 0.06 |
| Impact sound on wall/post hit | yes | yes | yes |
| `Play()` calls during run | 15 for 15 loops | 15 for 15 | 15 for 15 |
| Click score, driving phases (steady engine ≈ 4–6) | loops only: ≤ 5.6 through 8 shifts | 5.3–9.0 | 4.4–6.1 |

Higher click scores appear only at deliberately percussive one-shots (shift clunks, impacts, race-exhaust pops); a loops-only run of the SLS proved the loop path click-free (6.4 over the whole run).

## Validation tools
- `VehicleAudio.PlayCalls` must equal `LoopCount` for the life of the car (no restarts); `ShiftSounds`, `ImpactSounds`, `SkidLevel`, `AudioRpm`, `LoadBlend` expose state.
- `AudioOutputRecorder` on the listener + `AudioSignalAnalysis.ClickScore / Pitch / Brightness` measure the real mix in Play Mode.
- `VehicleAudioValidator` (Gameplay, dev): `Begin(vehicle, wavPath)` runs the scripted sequence and logs a per-phase report.

## Known limitations
- Procedural engines are physically structured but not recordings; they are convincing as an engine but not a specific real car. Swap in recorded loops via a new profile when available.
- Pitch-shifting covers ±40–60 % between rpm points; formant shift is mild but present between layers.
- The EV is silent at standstill (no AVAS pedestrian sound yet).
- Braking squeal is weighted toward sideways sliding because the physics' ABS hands over to locked wheels below ~20 km/h.
- No occlusion/reverb zones, no Doppler on the player car (by design), no music yet; mission code must drive Results/Lockdown/Ducked snapshots.
- Validation must run with the editor focused (an unfocused editor ticks too slowly for real-time audio checks).
