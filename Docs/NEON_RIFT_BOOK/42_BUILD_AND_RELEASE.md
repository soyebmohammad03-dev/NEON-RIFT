# 42 · Build and Release

> [Index](00_INDEX.md) · Previous: [41 Input Playtesting](41_INPUT_PLAYTESTING.md) · Next: [43 Git and Repository](43_GIT_AND_REPOSITORY.md)

## Requirements

| | |
|---|---|
| Unity | **6000.6.4f1** (or a later 6000.6.x; `ProjectSettings/ProjectVersion.txt`) |
| Packages | Resolved automatically from `Packages/manifest.json` ([03](03_TECH_STACK.md)) |
| Git LFS | **Required** before cloning. About 4,900 files are LFS objects (models, textures, audio, EXR probes, screenshots, generated district meshes) under 49 `.gitattributes` LFS rules |
| Input | Input System only (`activeInputHandler: 1`) |
| Product | "Neon Rift: Night Run", version 0.1.0, company "DefaultCompany" (`ProjectSettings/ProjectSettings.asset`) |

## Opening and running the project

```bash
git lfs install
```

```bash
git clone https://github.com/soyebmohammad03-dev/NEON-RIFT.git
```

1. Open the folder in Unity 6000.6.x (the Hub resolves the packages).
2. Open `Assets/_NeonRift/Scenes/Bootstrap.unity` and press Play: the intro (first launch) or the title → Car Select → Night Run.
3. Or press Play in `NightRun.unity` to jump straight into the mission with the first catalog car (`BootstrapLoader` + `GameFlow.AdoptScene`).
4. **Neon Rift ▸ Validate Project** checks config, catalogs and build scenes.
5. To see the intro again: **Neon Rift ▸ Intro ▸ Reset 'Intro Seen'**, or set `Data/Config/IntroSettings.asset` Policy to `Always`.

## Scenes in the build (`ProjectSettings/EditorBuildSettings.asset`, all enabled)

| Index | Scene |
|---|---|
| 0 | `Assets/_NeonRift/Scenes/Bootstrap.unity` |
| 1 | `Assets/_NeonRift/Scenes/Frontend.unity` |
| 2 | `Assets/_NeonRift/Scenes/CarSelect.unity` |
| 3 | `Assets/_NeonRift/Scenes/Dev/TestTrack.unity` |
| 4 | `Assets/_NeonRift/Scenes/NightRun.unity` |

`NightRunBuilder.RegisterInProject` keeps the build list and `GameConfig` wired. The dev mission (`Mission_DevTestTrack`) can be marked `developmentOnly` so release builds hide it (`GameConfig.IsPlayable`).

## Regenerating content (if needed)

| Content | Menu |
|---|---|
| Night Run city, mission, intro, probes | Neon Rift ▸ Night Run ▸ Build Night Run (district + mission) (about 40 s) |
| Car Select garage | Neon Rift ▸ Car Select ▸ Build Garage |
| Vehicle prefabs and catalog | Neon Rift ▸ Assets ▸ Run Third-Party Intake / Rebuild Vehicle Prefabs And Catalog |
| Vehicle LODs | Neon Rift ▸ Vehicles ▸ Apply Vehicle LODs To Catalog Prefabs |
| Car Select stats | Neon Rift ▸ Vehicles ▸ Update Car Select Stats From Physics |
| Audio | Neon Rift ▸ Audio ▸ Generate Vehicle Audio / Generate Mission Audio / Build Audio Mixer / Add Vehicle Audio To Prefabs; Neon Rift ▸ Intro ▸ Generate Intro Audio |
| Test track route | Neon Rift ▸ Test Track ▸ Build Validation Route |

## Building a player

- **Standard:** File ▸ Build Profiles, the target platform (desktop), the scenes above. The PC quality level uses `PC_RPAsset`; Mobile has its own RP asset.
- **Performance player:** **Neon Rift ▸ Validation ▸ Build Perf Player (macOS)** → `PerfBuild.Build` (`Assets/_NeonRift/Scripts/Editor/Validation/PerfBuild.cs`): enabled build scenes, `BuildTarget.StandaloneOSX`, `BuildOptions.None` (a release, not development), output under `Builds/PerfMac` (git-ignored). Run it with `-perfroute <folder>` for the automated route profile ([39](39_PERFORMANCE.md)).

Only a macOS player build is documented as built and measured in the repository. No CI pipeline is configured.

## Release checks

- Non-commercial assets: the **Terzo Millennio** (CC-BY-NC-4.0) is in the catalog, the **AMG GT3** (CC-BY-NC-SA) is excluded, and the **Drone Design** model (CC-BY-NC-ND) is unused. `ProjectValidator.Warnings()` lists non-commercial definitions. The two music tracks and the passenger car pack have **no licence recorded** ([44](44_ASSETS_AND_LICENSING.md)).
- Trademarks (Mercedes-Benz/AMG, Lamborghini and liveries) would need de-branding for a commercial release (`Docs/ThirdParty.md`).
