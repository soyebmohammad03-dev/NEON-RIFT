# Neon Rift: Night Run

3D cyberpunk racing / heist game. Unity 6 (6000.6.x), URP, new Input System.

## Getting started

1. Install [Git LFS](https://git-lfs.com) **before** committing any binary asset (models, textures, audio), then run `git lfs install` once.
2. Open the project in Unity 6000.6.4f1 or later 6000.6.x.
3. Press Play from any scene. `BootstrapLoader` injects the Bootstrap scene automatically:
   - from `Bootstrap` → Title → Car Select → Night Run
   - from `Scenes/NightRun` → the Night Run mission directly
   - from a mission scene (e.g. `Scenes/Dev/TestTrack`) → that mission starts directly with the first catalog vehicle
4. `Neon Rift ▸ Validate Project` checks config, catalogs and build scenes.
5. Tests: `Window ▸ General ▸ Test Runner ▸ EditMode`, or `Neon Rift ▸ Tests ▸ Run EditMode Tests` (writes `Logs/TestResults-EditMode.txt`).

## Driving

| Action | Keyboard | Gamepad |
|---|---|---|
| Throttle | W / ↑ | Right trigger |
| Brake (hold at a stop to reverse) | S / ↓ | Left trigger |
| Steer | A D / ← → | Left stick |
| Handbrake | Space | A / Cross |
| Interact (hold: hack, extract) · Retry on results | E | X / Square |
| Recover (right the car) | R | Select |
| Back to title | Esc | Start |
| Telemetry overlay (dev builds) | F3 | — |

Vehicle physics, tuning and the test-track validation route: [Docs/VehiclePhysics.md](Docs/VehiclePhysics.md). Vehicle audio and the mixer: [Docs/Audio.md](Docs/Audio.md). The Night Run mission, district and mission systems: [Docs/NightRun.md](Docs/NightRun.md).

## Layout

```
Assets/
  _NeonRift/            all first-party content
    Art/                materials, textures (first-party only)
    Data/               ScriptableObject data: Config, Vehicles, Missions
    Scenes/             Bootstrap, Frontend, CarSelect, NightRun (generated), Dev/TestTrack
    Scripts/<Module>/   one assembly per module (see Docs/Architecture.md)
    Settings/           URP assets, input actions
    UI/                 UXML, USS, panel settings
  <ThirdPartyPack>/     vendor packs stay at their own import root, untouched
Docs/                   architecture, decisions, conventions
```

See [Docs/Architecture.md](Docs/Architecture.md), [Docs/Decisions.md](Docs/Decisions.md) and [Docs/Conventions.md](Docs/Conventions.md).
