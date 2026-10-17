<div align="center">

# NEON RIFT

**A cyberpunk driving-and-heist campaign built in Unity 6.**
Two connected operations: steal a data core from a city in lockdown, then follow a rift to a storm-lashed container port and lift a container from under the cranes.

[![Unity 6000.6.4f1](https://img.shields.io/badge/Unity-6000.6.4f1-black?logo=unity)](Docs/INSTALLATION.md)
[![URP 17.6](https://img.shields.io/badge/render%20pipeline-URP%2017.6-0f6fde)](Docs/Architecture.md)
[![C#](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)](Assets/_NeonRift/Scripts)
[![Platforms](https://img.shields.io/badge/platforms-macOS%20editor%20%7C%20Android%20(ARM64)-3ddc84)](Docs/BUILDING.md)
[![Licence](https://img.shields.io/badge/licence-all%20rights%20reserved%20%2B%20third--party-lightgrey)](LICENSE.md)
[![Repository checks](https://github.com/soyebmohammad03-dev/NEON-RIFT/actions/workflows/repository-checks.yml/badge.svg)](https://github.com/soyebmohammad03-dev/NEON-RIFT/actions/workflows/repository-checks.yml)
[![Last commit](https://img.shields.io/github/last-commit/soyebmohammad03-dev/NEON-RIFT)](https://github.com/soyebmohammad03-dev/NEON-RIFT/commits/main)

![Neon Rift: the Harbor Rain teaser, the rift opening in the city, the car arriving at Kowloon Bay, the crane twistlocks, the gate override and the storm coast road](Docs/Screenshots/Campaign/00_banner.jpg)

</div>

You don't just race around a track. You choose a car, drive into a city that reacts to you, break into a guarded
facility, and get out while gates seal, cameras arm, drones search and rival crews race you to the exit. Do it well
enough and a rift opens on the road out of town, leading to the second operation: a container heist in a flooded
port at night, under storm, cranes and police.

| | Operation | What happens |
|---|---|---|
| **1** | **NIGHT RUN** | A 1.1 × 1.4 km night city of five districts. Reach Sector 7's Data Core (long clean boulevard or short alley with a gate hack that logs heat), breach it in multi-stage interactions while two rival crews run their own heists, then escape a city-wide lockdown to the Rift Gate. Finish in the **top two** crews to qualify. |
| **2** | **HARBOR RAIN** | Kowloon Bay container port in a storm. Crack the cargo manifest, bypass the security gate, have the gantry crane lift container KX-7 onto your crew's truck, then outrun the port police to the east gate, override its power and cross the storm-surge coast road to extraction. |

## Gallery

All images are real Game-view captures from Play Mode in the Unity editor (most of them taken by the project's
automated playtest and review tools, which drive the game through its normal input paths). None are mock-ups.

| Campaign opening | Level 1: qualify | Through the rift |
|---|---|---|
| ![Intercepted Harbor Rain transmission in the Night Run opening, with its subtitle](Docs/Screenshots/Campaign/01_harbor_rain_teaser.jpg) | ![Night Run debrief: mission complete, P1, top 2 qualified](Docs/Screenshots/Campaign/03_night_run_qualified.jpg) | ![The rift opening across the road out of the city](Docs/Screenshots/Campaign/04_portal_opens.jpg) |
| **Night Run: the city** | **Arrival in Harbor Rain** | **New operation unlocked** |
| ![Kowloon Market, Lantern Arcade](Docs/Screenshots/03_kowloon_lantern_arcade.jpg) | ![The car bursting out of the rift at the port perimeter](Docs/Screenshots/Campaign/06_harbor_arrival.jpg) | ![Unlock card with the real Night Run result](Docs/Screenshots/Campaign/07_new_operation_unlocked.jpg) |
| **Five-car selection** | **Mission briefing** | **Gantry crane heist** |
| ![Level 2 car selection: five cars, all available](Docs/Screenshots/Campaign/08_level2_car_select.jpg) | ![Harbor Rain briefing: the nine real objectives and the controls](Docs/Screenshots/Campaign/09_mission_briefing.jpg) | ![Twistlocks turning on container KX-7](Docs/Screenshots/Campaign/10_crane_twistlocks.jpg) |
| **Lockdown** | **Police pursuit** | **Storm coast road** |
| ![Alarm and searchlights over the restricted area](Docs/Screenshots/Campaign/11_alarm_lockdown.jpg) | ![Gate override with a port police unit closing in](Docs/Screenshots/Campaign/12_police_pursuit.jpg) | ![Wave spray over the seawall on the coast road](Docs/Screenshots/Campaign/13_storm_coast_road.jpg) |

More: [Night Run screenshots](Docs/Screenshots) · [campaign screenshots](Docs/Screenshots/Campaign) ·
[night look before/after](Docs/Environment.md).

## Features

**Campaign**
- **Two-level campaign** with its own bootstrap and a saved progression (Night Run qualification, portal, unlocks,
  Harbor Rain completion, the car used in each level). Progress survives restarts; the title offers Harbor Rain
  directly once it is unlocked. See [Docs/CAMPAIGN.md](Docs/CAMPAIGN.md).
- **The original standalone Night Run is untouched**: its own bootstrap, title, three-car selection and mission run
  exactly as before.
- **Harbor Rain teaser** cut into the Night Run opening: an intercepted transmission from the port, separately
  skippable.
- **Portal transition**: after a qualified Night Run, a rift opens on the road out of the city and your car drives
  into it under its own physics; it bursts out of a rift at the port.
- **Unlock card** that quotes your real Night Run result (position, grade and time).
- **Five-car selection for Level 2.** The two Harbor Rain cars are locked in the Level 1 selection and become
  available for Harbor Rain once you qualify ([see the note on those two models](#licensing-and-third-party-assets)).
- **Harbor Rain trailer**: narrated shots of the real level, ending on the mission's actual objectives and controls;
  skipping it gives exactly the same start.
- **Campaign results** and replay choices (replay Harbor Rain, change car, play Night Run, title).

**Driving and heists**
- Raycast suspension, combined-slip tyres, drivetrain and aero in physical units, verified on a deterministic bench
  ([Docs/VehiclePhysics.md](Docs/VehiclePhysics.md)); telemetry-driven synthesised engine audio
  ([Docs/Audio.md](Docs/Audio.md)).
- Night Run: data-driven objectives, heat, Alert → Lockdown, gate and controller hacks, a Data Core breach and
  extraction, cameras, drones, sirens, a lockdown wave across lights and screens
  ([Docs/NightRun.md](Docs/NightRun.md)).
- Rival crews that use the player's own input interface, plan over the road graph, follow a racing line and run
  their own heists; the first two crews out qualify.
- Harbor Rain: a manifest clue puzzle, a badge-reader reel bypass, a gantry crane that squares up, seats, locks and
  lifts the container onto a truck, working forklifts and terminal tractors, security cameras and searchlights,
  drones, a port police pursuit with roadblocks and re-routing, floods, a storm-surge coast road and an optional
  drone-relay objective.
- Graded debriefs, split times and personal bests; pause menu with settings and controls.

**Presentation**
- Spoken radio and narration with **subtitles for every spoken line** (setting: on by default). See
  [the voice note](#licensing-and-third-party-assets).
- Real-time opening cinematic, Car Select in a generated crew garage, minimap, night lighting and rain.
- Keyboard, gamepad and touch controls (touch and tilt steering on Android) — [Docs/CONTROLS.md](Docs/CONTROLS.md).

## Getting started

**Requirements:** Unity **6000.6.4f1** (or a later 6000.6.x) with the Android module if you want to build the APK,
[Git LFS](https://git-lfs.com), and plenty of free disk (the checkout with LFS files is about 1 GB; Unity's import cache for this
project grows to roughly 30 GB). Packages are pulled
from the Unity registry by `Packages/manifest.json` (URP 17.6, Input System 1.19, Cinemachine, Timeline, glTFast,
Splines, ProBuilder, AI Navigation, Test Framework).

```bash
git lfs install
git clone https://github.com/soyebmohammad03-dev/NEON-RIFT.git
```

Then open the folder in Unity Hub and press Play in one of:

| To play | Open this scene and press Play |
|---|---|
| **The connected campaign** (Night Run → Harbor Rain) | `Assets/_NeonRift/Levels/Campaign/Scenes/CampaignBootstrap.unity` |
| **The original standalone Night Run** | `Assets/_NeonRift/Scenes/Bootstrap.unity` (or any Night Run scene) |
| Harbor Rain on its own (development) | `Assets/_NeonRift/Levels/HarborRain/Scenes/HarborRain.unity` |

Full setup, Android install and troubleshooting: [Docs/INSTALLATION.md](Docs/INSTALLATION.md).

### Controls (keyboard / gamepad)

| Action | Keyboard | Gamepad |
|---|---|---|
| Throttle / brake (hold brake to reverse) | W / S or ↑ / ↓ | Right / left trigger |
| Steer | A / D or ← / → | Left stick |
| Handbrake | Space | B |
| Interact (hack, extract, puzzles) | E | A |
| Recover the car | R | Select |
| Pause | Esc | Start |
| Skip a cinematic | Space | Start |
| Harbor Rain device (message log) | Tab | Y |

All controls, touch and tilt steering: [Docs/CONTROLS.md](Docs/CONTROLS.md).

## Android build

The campaign APK is built by `Neon Rift ▸ Campaign ▸ Build Campaign APK (Release)` — ARM64, IL2CPP, application id
`com.soyebmohammad.neonrift.campaign`, so it installs beside the standalone Night Run build. See
[Docs/BUILDING.md](Docs/BUILDING.md).

**No APK is published.** The locally built campaign APK (about 526 MB) contains the two Harbor Rain cars and
macOS-generated voice recordings, which cannot be redistributed publicly (below), and it has **not** been tested on a
physical Android device. See [Docs/BUILDING.md § Distribution](Docs/BUILDING.md#distribution).

## Testing

| Check | How | Last result (Play Mode / editor, macOS) |
|---|---|---|
| EditMode tests (142, incl. 17 campaign tests) | `Neon Rift ▸ Tests ▸ Run EditMode Tests (write report)` | 142 passed, 0 failed |
| Full campaign playtest (opening → Night Run → portal → Harbor Rain → results) | `Neon Rift ▸ Campaign ▸ Playtest: Full Campaign` | 39/39 checks, 0 console errors |
| Original Night Run bootstrap | `… ▸ Playtest: Original Night Run Bootstrap` | 6/6 |
| Trailer skip + subtitles · saved progress · without the Harbor Rain cars | the other `… ▸ Playtest:` items | 14/14 · 4/4 · 4/4 |
| Harbor Rain validator | `Neon Rift ▸ Harbor Rain ▸ Validate (Play Mode)` | all passed, 3 consecutive runs (one at 8 fps) |
| Repository checks (links, images, file sizes, licensing exclusions) | GitHub Actions on every push | see badge |

These are automated editor runs, not human play sessions or device tests; Night Run and Harbor Rain were completed
by the project's autopilots. Details and reproduction steps: [Docs/TESTING.md](Docs/TESTING.md).

## Documentation

| | |
|---|---|
| [Docs/CAMPAIGN.md](Docs/CAMPAIGN.md) | The two-level campaign, unlocks, saves, subtitles |
| [Docs/INSTALLATION.md](Docs/INSTALLATION.md) | Setup, opening the project, installing the APK |
| [Docs/CONTROLS.md](Docs/CONTROLS.md) | Keyboard, gamepad, touch and tilt |
| [Docs/BUILDING.md](Docs/BUILDING.md) | Rebuilding generated content and both APKs |
| [Docs/TESTING.md](Docs/TESTING.md) | Test suites, validators, playtests and results |
| [Docs/Architecture.md](Docs/Architecture.md) | Assemblies, scenes, flow, campaign layer |
| [Docs/ThirdParty.md](Docs/ThirdParty.md) | Every third-party asset, its licence and status |
| [Docs/NEON_RIFT_BOOK](Docs/NEON_RIFT_BOOK/00_INDEX.md) | The full technical book (Night Run systems in depth) |
| [CHANGELOG.md](CHANGELOG.md) · [CONTRIBUTING.md](CONTRIBUTING.md) · [SECURITY.md](SECURITY.md) | |

## Project layout

```
Assets/
  _NeonRift/
    Scripts/<Module>/      first-party code, one assembly per module (Game, Gameplay, Frontend, Intro, Vehicles, ...)
    Scenes/                Bootstrap (standalone entry), Frontend, CarSelect, NightRun, Dev/TestTrack
    Levels/Campaign/       campaign bootstrap, campaign data, voice/portal assets, build + playtest tools, tests
    Levels/HarborRain/     Level 2: scene, runtime scripts, editor builders, generated content, imported assets
    Data/ Art/ UI/ Settings/
  ThirdParty/              vendor models and music at their own import root, each with ATTRIBUTION.txt
Docs/                      documentation and screenshots
```

## Licensing and third-party assets

- **First-party content** (code, scenes, generated meshes, textures and audio, shaders, UI) is © 2026 Soyeb
  Mohammad, **all rights reserved**. The repository is public to view; no open-source licence is granted. See
  [LICENSE.md](LICENSE.md).
- **Third-party models** keep their own licences (CC BY 4.0, CC BY-NC 4.0, CC BY-NC-SA 4.0, CC0). Two vehicle models
  are non-commercial, so **the project must not be used commercially**. Brand names and liveries are trademarks of
  their owners.
- **Not in this repository:** the two Harbor Rain cars (a Ferrari F40 Liberty Walk and a Ferrari 296 Speciale A)
  and one work-boat model. Their redistribution rights are unverified or contradicted by their own source pages.
  The project runs without them: the selection shows the cars that are present.
- **Voice recordings are not in this repository either.** The spoken lines were generated with macOS System Voices,
  whose licence permits personal, non-commercial use only and forbids publishing them. A public checkout shows the
  same lines as subtitles; on a Mac, the build tools regenerate the audio locally.
- Some owner-supplied items (the passenger-car pack used for traffic and two music tracks) still have no recorded
  source or licence; see [Docs/ThirdParty.md](Docs/ThirdParty.md).

## Known limitations

- The Android build has **not been run on a physical device**: performance, touch layout and memory on phones are
  unverified. The Harbor Rain level is heavy (several million triangles in some views).
- Wide night views of the port remain dark, since only the nearest lights are real-time.
- The generated speech and sound mix have not been reviewed by ear.
- Night Run has no spoken dialogue (text messages only), and no crew cars accompany yours through the portal.
- Unity tests run in the editor, not in CI (no Unity licence is configured for GitHub Actions).
