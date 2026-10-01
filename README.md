# Neon Rift: Night Run

3D cyberpunk racing / heist game. Unity 6 (6000.6.x), URP, new Input System.

## Getting started

1. Install [Git LFS](https://git-lfs.com) **before** committing any binary asset (models, textures, audio), then run `git lfs install` once.
2. Open the project in Unity 6000.6.4f1 or later 6000.6.x.
3. Press Play from any scene. `BootstrapLoader` injects the Bootstrap scene automatically:
   - from `Bootstrap` → Title → Car Select → mission
   - from a mission scene (e.g. `Scenes/Dev/TestTrack`) → that mission starts directly with the first catalog vehicle
4. `Neon Rift ▸ Validate Project` checks config, catalogs and build scenes.
5. Tests: `Window ▸ General ▸ Test Runner ▸ EditMode`.

## Layout

```
Assets/
  _NeonRift/            all first-party content
    Art/                materials, textures (first-party only)
    Data/               ScriptableObject data: Config, Vehicles, Missions
    Scenes/             Bootstrap, Frontend, CarSelect, Dev/TestTrack
    Scripts/<Module>/   one assembly per module (see Docs/Architecture.md)
    Settings/           URP assets, input actions
    UI/                 UXML, USS, panel settings
  <ThirdPartyPack>/     vendor packs stay at their own import root, untouched
Docs/                   architecture, decisions, conventions
```

See [Docs/Architecture.md](Docs/Architecture.md), [Docs/Decisions.md](Docs/Decisions.md) and [Docs/Conventions.md](Docs/Conventions.md).
