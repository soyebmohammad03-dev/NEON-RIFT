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

`GameRoot` switches Menu ↔ Gameplay on game-state changes. The mission director switches to Lockdown when the district locks down and to Results at the end.

## Music (supplied tracks)

- **Gameplay:** `machine-speed-sport-aggressive-electronic.mp3` (111 s, streamed Vorbis) loops on the Score group at a low 0.30 (`MissionAudioSet.musicTrack`). It fades in at the start and out at the end, when the outro plays. It ignores the listener pause, so it keeps playing (ducked by the Ducked snapshot) under the pause menu. When a track is assigned it replaces the procedural stems below, which remain as the fallback.
- **Opening cinematic:** `danger-chaos-cinematic-trailer-hybrid.mp3` (102 s) is the Timeline's "Soundtrack" track from 0 to 84 s, with a 0.4 s fade-in and a 2.5 s fade-out into Car Select.
- **Music on/off:** **M** (gamepad **Y**) anywhere in the game. `GameSettings` sets the Music bus to −80 dB or back to the music volume, and saves the choice. The HUD shows `M · MUSIC ON/OFF` at the bottom, and the title shows the hint. The pause menu's Settings page has the same switch plus master / music / effects / interface volumes.
- The mixer runs on unscaled time, so the pause ducking fades while the game is frozen.

## Adaptive score

The game's music is original and fully procedural (`ScoreComposer`, editor only; no samples, no licensed material). It is one 8-bar theme in A minor at ≈96 bpm: Am(add9) – Fmaj7 – Dm9 – Esus4 → E, two bars each. The theme is rendered as four stems of exactly the same length (882,048 samples, a whole number of 128-sample ADPCM blocks, so the loop never gains a gap). The tempo is derived from that length (95.995 bpm), so the runtime beat grid matches the audio.

| Stem | Content | Calm | Heist work (× tension) | Alert | Lockdown |
|---|---|---|---|---|---|
| Bed | detuned-saw pad + sub, breath | 0.80 | 0.80 | 0.55 | 0.20 |
| Pulse | soft kick, sidechained eighth-note bass, hats | 0.45 | → 0.22 | 0.85 | 0.55 |
| Arp | sixteenth plucks, dotted-eighth echo | 0 | → 0.80 | 0.35 → 0.85 | 0.25 × tension |
| Drive | four-on-the-floor, claps, rolling 16th bass, A–Bb alarm stab, tom fills | 0 | 0 | 0 | 1.00 |

- **Runtime** (`MissionAudio`, pure mixing rules in `ScoreMix`): all stems start on one scheduled DSP tick (`PlayScheduled`), so they stay sample-locked. Layers rise at 0.8/s and fall at 0.4/s. The drive stem slams in at 4/s. Security changes land on the next half bar, so the lockdown hits on the grid together with the lockdown stinger.
- **End:** the stems fade over 1.2 s. On the next beat the outro plays: *success* resolves the theme to A major with a bell arpeggio, *failure* is a low A/Bb/E cluster sinking a semitone.
- **Intro:** the cinematic uses the same theme (`Intro_Bed`, `Intro_Groove`; this replaces the old placeholder drone and pulse). The groove joins at the bed's position in the loop, so both stay on the same bar and chord.
- **Device changes** (headphones plugged in, output switched): Unity resets audio and stops every source. `MissionAudio` and `VehicleAudio` listen to `AudioSettings.OnAudioConfigurationChanged`, restart their loops and re-sync the score stems.

### Voices, priorities and headroom

Three cars carry about 22 looping sources each, so a mission plays about 52 sources. The old 32-voice limit virtualised the music and city ambience, which ran at the default priority 128 while engine layers ran at 0. Measured: the score was about 35 dB below its level, effectively silent. Fixes:

- Real voices 32 → 64 (`ProjectSettings/AudioManager`).
- Priorities: score and outro 0; mission cues and stingers 4; ambience 8; tension 16; interaction loop 24; gate klaxon/motor 96; sirens 112. A car the camera is not following has +140 on every source (`VehicleAudio.OtherCarPriorityOffset`).
- Gain staging: 10 district sirens at 0.35 (was 0.8), gate klaxon source 0.6, score 0.7.
- `MasterLimiter` on the listener: −1 dBFS ceiling, instant attack, 150 ms release. It is a safety net, not the mix.

**Measured** (Play Mode, BoulevardInAlleyOut, the full 150 s mix recorded at the listener with `AudioOutputRecorder`):

| | Before | After |
|---|---|---|
| Score alone, calm | 0.0013 RMS (virtualised) | 0.054 RMS (predicted 0.055) |
| Lockdown escape | 0.25–0.29 RMS, full-scale clipping from 76 s to 108 s | 0.14–0.18 RMS, peak 0.89 (the ceiling) |
| Limiter activity | — | idle except −2.2 dB (heist cue), −4.8 dB (breach + lockdown stingers), −2.9 dB (escape) |

Review excerpts of the actual game mix (engine, ambience, cues, score): `Docs/Audio/mission_heist_calm_to_extraction.wav`, `mission_lockdown_escape.wav`, `mission_extraction_outro.wav`.

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
- No occlusion/reverb zones, no Doppler on the player car (by design). The Ducked snapshot is unused so far.
- The score's balance has been measured (levels, peaks, seams, limiter activity) but not judged by ear by a person.
- Validation must run with the editor focused (an unfocused editor ticks too slowly for real-time audio checks).
