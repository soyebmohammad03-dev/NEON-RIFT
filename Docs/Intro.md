# Opening cinematic

Game launch → **intro** (real-time, in the city) → **NEON RIFT / NIGHT RUN** title → Enter / A → the camera pushes into the hero car → **Car Select** → Night Run. Original work throughout: no footage, music, dialogue or characters from other games.

## How it fits the game flow

- `GameState.Intro` and `IGameFlow.PlayIntro()`. On boot, `GameFlow` plays the intro when `IntroSettings.ShouldPlayOnBoot()` says so; otherwise it goes to the normal Title screen, which now has a **WATCH INTRO** button.
- The intro runs **in the Night Run scene** (`GameConfig.introScene`), so it shows the real city: lamps, signals, haze and towers. The 18 MB scene is not duplicated. `GameFlow` enters scenes through the root component that matches the state: `IntroSceneEntry` (an `IIntroEntryPoint`) for the intro, `MissionSceneEntry` for missions. The intro rig is inert during missions: shots, overlay and stage lights are off, and the director doesn't play on awake. This is covered by a test, and spawn-view stats are unchanged.
- The cars in the lineup are the catalog's gameplay prefabs, spawned through the normal `VehicleSpawnPoint` path: same models, physics, lights and engine audio. The hero car (middle) is the session's selected car, or the catalog default. The two rivals drive away with the normal `RacerDriver` AI.
- Nothing persists except the "seen" flag. `Exit()` restores the time scale, removes the lineup and disables the rig. Verified: intro → Car Select → Night Run gives a normal mission (HUD visible, time scale 1, the selected car drives, the others are rivals).

## Settings (`Data/Config/IntroSettings.asset`)

| Field | Default | Meaning |
|---|---|---|
| Policy | FirstLaunchOnly | `Always` (good while authoring), `FirstLaunchOnly` (stored in PlayerPrefs `NeonRift.IntroSeen`), `Never` |
| Skippable | on | Space / Start skips to the title, even on the first launch |
| Mode | Realtime | `Video` + a `VideoClip` plays a pre-rendered intro instead of shots 1–9, then the real-time title takes over. **This path is implemented but untested: there is no rendered clip yet** |

Menu: **Neon Rift ▸ Intro ▸ Reset 'Intro Seen'** shows it again on the next launch.

## Building it

`IntroBuilder` generates the rig and the Timeline from a shot table in code (`Neon Rift ▸ Intro ▸ Build Intro`). It also runs as part of every city rebuild.

- **Timeline** (`Data/Intro/Intro_NightRun.playable`, 62 s):
  - one animation track per shot camera (eased position plus a look direction keyed as a quaternion);
  - a Cinemachine track that cuts and blends between the 18 shot cameras;
  - an `IntroCueTrack` for story beats;
  - six audio tracks.
- **Cues** (`IntroCueKind`): `FadeIn`, `Dip` (soft cut), `Card` (text card), `Title`, `Surveillance` (CCTV overlay and grade), `Headlights`, `Ignition`, `Departure`, `Flash`, `SkipHint`, `Letterbox`. One-shot cues are also applied by Skip, so skipping lands in a consistent world state.
- **World anchors are read from the built city:**
  - the Data Core from the mission director's alert origin;
  - the compound gate by name;
  - the signal shot from the actual signal lens geometry on W Avenue;
  - the rivals' departure goal from the `core_staging` race marker.

| # | Time (s) | Shot |
|---|---|---|
| 1 | 0–7.5 | Out of black: the city from far away (city ambience, drone) |
| 2 | 7.5–14.5 | High over Sector 7: scale, skyline, roads, lights (2.5 s blend) |
| 3 | 14.5–21 | Down into the streets towards the industrial and commercial districts |
| 4 | 21–30.5 | Lineup: headlight close-up (the lights switch on), slide along the hero car, the three cars from the front (impact on the dip) |
| 5 | 30.5–37 | The Data Core compound. Card: *IN NEON RIFT, INFORMATION IS POWER.* / *WHOEVER RUNS THE GRID RUNS THE CITY.* |
| 7 | 37–42 | The Data Core. Card: *TONIGHT, A CREW GOES FOR THE DATA CORE.* / *ONE RUN. NO SECOND CHANCE.* |
| 8 | 42–46.5 | Security camera on the lineup: CCTV overlay, green grade, scanner beeps, *UNUSUAL ACTIVITY · W AVENUE* |
| 9 | 46.5–52.5 | Eight 0.75 s cuts with flash and whoosh: ignition (engines start), headlights, wheels (the rivals pull away), street, security gate, traffic signal, Data Core, the avenue. Riser and pulse build |
| 10 | 52.5–62 | Impact. NEON RIFT, then NIGHT RUN, over W Avenue behind the hero car. From 56 s: hold and **PRESS ENTER · A** |
| 11 | on Confirm | 2.4 s push-in to the hero car's front three-quarter, fade, then Car Select |

## Audio

Original and procedural (`IntroAudioGenerator`, `Audio/Generated/Intro`):

- **Sound effects:** riser, impact, whoosh, scanner beeps, ignition.
- **Placeholder music:** a 16 s drone loop and an 8 s 120 bpm pulse loop. Their file names say *Placeholder*; replace them with a composed score.
- **Ambience:** the existing city ambience.
- **Engines:** the cars' own engine audio starts at the ignition cue.

Routed through the mixer: ambience → Ambience, drone/pulse/riser/hits/scan → Music, ignition → Sfx.

## Validation (Play Mode, October 2026)

| Check | Result |
|---|---|
| First launch from Bootstrap | Intro played automatically and held on the title at t = 57.5 s |
| Enter at the title | `[Intro] confirm: pushing in to the hero car` → Car Select; "seen" flag set |
| Car Select → Night Run after the intro | Right Arrow → SLS GT3, Enter → Night Run. The GT3 is the player, the SLS AMG and Terzo are rivals, HUD visible, time scale 1, keyboard driving script passes (W, D, Space, arrows) |
| Second launch | Booted to the normal Title (intro already seen); WATCH INTRO replayed it to the title |
| Skip | Space pressed at t = 14.4 s → jumped to the title shot, rivals gone, time scale 1, title reached |
| Tests | `IntroTests`: policy logic, video fallback, config wiring, scene/Timeline wiring and inertness |

Frames from a real playback:

| | |
|---|---|
| ![](Screenshots/Intro/intro_02_10.0s.jpg) | ![](Screenshots/Intro/intro_03_16.0s.jpg) |
| ![](Screenshots/Intro/intro_04_22.5s.jpg) | ![](Screenshots/Intro/intro_06_29.0s.jpg) |
| ![](Screenshots/Intro/intro_07_33.0s.jpg) | ![](Screenshots/Intro/intro_09_44.0s.jpg) |
| ![](Screenshots/Intro/intro_10_46.9s.jpg) | ![](Screenshots/Intro/intro_01_50.6s.jpg) |
| ![](Screenshots/Intro/intro_02_51.4s.jpg) | ![](Screenshots/Intro/title_57.5s.jpg) |

Not yet verified by ear: the mix balance of the intro audio. The clips are generated and bound, and the playback ran, but levels have not been listened to on speakers.
