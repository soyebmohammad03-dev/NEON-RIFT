# Neon Rift: Night Run

A cyberpunk racing / heist game in Unity 6 (6000.6.x, URP, new Input System). You don't just race around a track: you infiltrate, manipulate and escape a city that reacts to you.

Pick a car and drive into a 1.1 × 1.4 km night city of five districts. Reach Sector 7's Data Core by the long clean boulevard or the short alley, whose gate hack logs heat. Breach the core while two rival crews wait for you, then escape through a city in lockdown: gates seal, cameras arm, routes change, the crew opens the skyway. Beat the trace and the rivals to the Rift Gate.

| | |
|---|---|
| ![Car select](Docs/Screenshots/01_car_select.jpg) | ![Kowloon, Lantern Arcade](Docs/Screenshots/03_kowloon_lantern_arcade.jpg) |
| ![Lockdown at the compound](Docs/Screenshots/08_lockdown_compound.jpg) | ![Escape through the alley](Docs/Screenshots/09_lockdown_alley_escape.jpg) |

More in [Docs/Screenshots](Docs/Screenshots).

## Getting started

1. Install [Git LFS](https://git-lfs.com) **before** cloning or committing binary assets (models, textures, audio, generated meshes), then run `git lfs install` once.
2. Open the project in Unity 6000.6.4f1 or a later 6000.6.x.
3. Press Play from any scene. `BootstrapLoader` injects the Bootstrap scene automatically:
   - from `Bootstrap`: Title → Car Select → Night Run
   - from `Scenes/NightRun`: the Night Run mission directly (first catalog car, the others as rivals)
   - from a mission scene (e.g. `Scenes/Dev/TestTrack`): that mission starts directly with the first catalog vehicle
4. `Neon Rift ▸ Validate Project` checks config, catalogs and build scenes.
5. Tests: `Window ▸ General ▸ Test Runner ▸ EditMode`, or `Neon Rift ▸ Tests ▸ Run EditMode Tests` (writes `Logs/TestResults-EditMode.txt`).
6. The city is generated: `Neon Rift ▸ Night Run ▸ Build Night Run (district + mission)` (about 40 s, including the reflection-probe bake).

## Driving

| Action | Keyboard | Gamepad |
|---|---|---|
| Throttle | W / ↑ | Right trigger |
| Brake (hold at a stop to reverse) | S / ↓ | Left trigger |
| Steer | A D / ← → | Left stick |
| Handbrake | Space | B / Circle |
| Interact (hold: hack, extract) · Retry on results | E | A / Cross |
| Recover (right the car) | R | Select |
| Back to title | Esc | Start |
| Telemetry overlay (dev builds) | F3 | — |

In the editor, keyboard input only reaches the game while the Game view has focus (click it once). When a
mission starts, the console logs `[Input] Driving controls enabled. Devices: ...`. If it warns that no keyboard
or gamepad is connected, the editor's Input System backend has lost its devices: restart the editor. That was
the cause of the "WASD does nothing" report in October 2026. `Logs/InputPlaytest.txt` is written by
`InputPlaytest.RunStandardScript` (Editor/Validation), which drives the real Keyboard device through a timed
script in Play Mode.

## What's in it

- **Car Select → mission.** The chosen car's model, physics profile, audio profile and stats carry into the mission. The other catalog cars become the rival crews.
- **Vehicle physics.** A raycast suspension, combined-slip tyre, drivetrain and aero model with physical units, verified on a deterministic bench. See [Docs/VehiclePhysics.md](Docs/VehiclePhysics.md).
- **Engine audio.** Layered, telemetry-driven and synthesised in-house. See [Docs/Audio.md](Docs/Audio.md).
- **The city.** Five districts (Sector 7, Spire Heights, Kowloon Market, Harbor Yards, Lowtown) with a road hierarchy, the elevated Harbor Skyway, the Lantern Arcade, Spire Plaza, a container yard, a construction site, traffic signals, street furniture, landmarks and a night lighting grade. Everything is generated from data. See [Docs/City.md](Docs/City.md).
- **Rival AI.** Drivers that use the player's own input interface. They plan over the road graph (re-planning when gates close), follow a racing line with a braking planner, sweep ahead for cars and obstacles to overtake, back out when stuck, and apply a bounded, data-driven rubber band.
- **The heist.** Data-driven objectives, heat, an Alert/Lockdown state machine, gates and checkpoints, cameras, sirens, a lockdown wave across lights and screens, and traffic signals flashing red. See [Docs/NightRun.md](Docs/NightRun.md).
- **HUD.** Objective, distance, heat and security, trace timer, seal countdowns, interaction prompt, race standings, street and district readout, and a heading-up minimap with the live GPS route (closed gates shown red).

## Layout

```
Assets/
  _NeonRift/            all first-party content
    Art/                materials, textures, generated meshes (first-party only)
    Data/               ScriptableObject data: Config, Vehicles, Missions, World (road network), Racing (AI profiles)
    Scenes/             Bootstrap, Frontend, CarSelect, NightRun (generated), Dev/TestTrack
    Scripts/<Module>/   one assembly per module (see Docs/Architecture.md)
    Settings/           URP assets, input actions
    UI/                 UXML, USS, panel settings
  ThirdParty/           vendor models at their own import root, untouched, each with ATTRIBUTION.txt
Docs/                   architecture, decisions, conventions, city, mission, physics, audio
```

Night look and before/after shots: [Docs/Environment.md](Docs/Environment.md). See [Docs/Architecture.md](Docs/Architecture.md), [Docs/Decisions.md](Docs/Decisions.md) and [Docs/Conventions.md](Docs/Conventions.md).

## Credits and licences

See [LICENSE.md](LICENSE.md) for ownership: first-party content is all rights reserved; third-party models keep their own licences.

Code, generated art (district textures, signage font, meshes) and all audio are first-party.

Third-party 3D models (Sketchfab), used under their Creative Commons licences. Full details are in [Docs/ThirdParty.md](Docs/ThirdParty.md) and each folder's `ATTRIBUTION.txt`:

| Model | Author | Licence |
|---|---|---|
| 2010 Mercedes SLS AMG | Dave Love SketchFab | CC BY 4.0 |
| Mercedes SLS GT3 | Dave Love SketchFab | CC BY 4.0 |
| ( FREE ) Lamborghini Terzo Millennio | SDC PERFORMANCE | CC BY-NC 4.0 (non-commercial) |
| Mercedes-AMG GT3 Red Bull Racing (not in the catalog) | VTX | CC BY-NC-SA 4.0 (non-commercial) |
| [FREE] London Skyscraper, Singapore Office Skyscraper [FREE], Asian Themed Low Poly Night City Buildings, Low Poly Night City Building Skyline | 99.Miles | CC BY 4.0 |

Vehicle brands and liveries are trademarks of their owners. This is a non-commercial project; a commercial release would need de-branded models and fictional names.
