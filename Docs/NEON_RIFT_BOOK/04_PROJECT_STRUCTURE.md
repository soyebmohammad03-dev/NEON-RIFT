# 04 · Project Structure

> [Index](00_INDEX.md) · Previous: [03 Tech Stack](03_TECH_STACK.md) · Next: [05 Game Flow](05_GAME_FLOW.md)

Only folders that exist in the repository are listed. All first-party content lives under `Assets/_NeonRift/`. Vendor content lives under `Assets/ThirdParty/` and is never edited in place (Decision D9).

## Top level

```
Neon Rift/
├─ Assets/
│  ├─ _NeonRift/          first-party content (code, data, art, audio, scenes, UI)
│  └─ ThirdParty/         vendor models and music, each at its own import root
├─ Docs/                  design and technical notes (this book: Docs/NEON_RIFT_BOOK/)
├─ Packages/manifest.json Unity packages
├─ ProjectSettings/       Unity project settings
├─ README.md
├─ LICENSE.md
├─ .gitattributes         Git LFS + UnityYAMLMerge rules
└─ .gitignore             Library/, Temp/, Logs/, Builds/, UserSettings/, SourceArchives/ …
```

Local-only folders that are **not committed** (in `.gitignore`): `Library/`, `Temp/`, `Logs/` (test and playtest reports), `Builds/` (e.g. `Builds/PerfMac`), `UserSettings/`, `SourceArchives/` (original downloaded archives), `My project/`.

## `Assets/_NeonRift/`

```
_NeonRift/
├─ Art/
│  ├─ Buildings/          URP materials and packed textures for the building packs
│  ├─ District/
│  │  ├─ Materials/       ≈120 generated district materials (District_*, Drone_*, Crowd_* …)
│  │  ├─ Meshes/          ≈8,880 generated mesh assets (blocks, props, beacons) — Git LFS
│  │  └─ Textures/        generated PNG textures (asphalt, facades, signage, glow shapes)
│  ├─ Garage/             Car Select garage materials, meshes, textures (planning wall)
│  ├─ Materials/DevTrack/ test-track materials
│  ├─ Shaders/            NeonRift_*.shader (6 custom shaders)
│  ├─ Traffic/            traffic car materials (Traffic_Pack*)
│  └─ Vehicles/PF_Vehicle_*/  split wheel meshes per car
├─ Audio/
│  ├─ Generated/          synthesised clips: Engine_V8Road, Engine_V8Race, Engine_EV, Engine_Pops,
│  │                      Tyres, Road, Mechanical, Impacts, Mission, Intro
│  └─ Mixer/NeonRiftMixer.mixer
├─ Data/                  ScriptableObjects (see the data index in 00_INDEX.md)
│  ├─ Audio/AudioMixerConfig.asset
│  ├─ Config/GameConfig.asset, IntroSettings.asset
│  ├─ Intro/Intro_NightRun.playable, Intro_Surveillance.asset
│  ├─ Missions/Mission_NightRun.asset, Mission_DevTestTrack.asset, Interaction_*.asset, MissionAudio_NightRun.asset
│  ├─ Physics/PM_VehicleBody.asset
│  ├─ Racing/Racer_Vex.asset, Racer_Kade.asset
│  ├─ Vehicles/VehicleCatalog.asset, Vehicle_*.asset, Physics/, Audio/, ImportSetups/
│  └─ World/RoadNetwork_NightRun.asset, BuildingCatalog.asset, Building_*.asset
├─ Prefabs/
│  ├─ Buildings/          22 PF_Bld_* (Asian Night pack, London, Singapore, skyline cluster)
│  ├─ District/           PF_Prop_StreetLight_{Led,Sodium,Warm}, PF_Prop_JerseyBarrier, PF_Prop_SecurityBeacon
│  ├─ Drones/             PF_Drone_Mech, PF_Drone_Buster
│  ├─ Traffic/            PackCar_{Compact,Coupe,Hatchback,Minivan,Offroad,Pickup,Sedan,Sport,SUV,Wagon}
│  └─ Vehicles/           PF_Vehicle_MercedesSLS, _MercedesSLSGT3, _LamborghiniTerzo, _MercedesAMGGT3
├─ Scenes/
│  ├─ Bootstrap.unity     always loaded (GameRoot, LoadingOverlay, EventSystem)
│  ├─ Frontend.unity      title screen
│  ├─ CarSelect.unity (+ CarSelect/ probe data)  generated crew garage
│  ├─ NightRun.unity (+ NightRun/ 63 reflection-probe EXRs)  generated city + mission + intro
│  └─ Dev/TestTrack.unity physics validation track
├─ Scripts/               13 assemblies, one folder each (see 02_ARCHITECTURE.md)
│  ├─ Audio/ Core/ Frontend/ Game/ Gameplay/{City,Mission,Navigation,Racing}/ Input/ Intro/
│  │  Missions/ Rendering/ Vehicles/{Audio,Physics}/ World/
│  ├─ Editor/{Audio,District,Frontend,Intro,TestTrack,Validation,Vehicles,World}/
│  └─ Tests/EditMode/
├─ Settings/
│  ├─ Input/NeonRiftControls.inputactions
│  └─ Rendering/          PC_/Mobile_ RP assets and renderers, NightRun_* and Garage volumes,
│                         NightRun_Lighting.lighting, Garage_CameraBlends.asset
└─ UI/                    UXML/USS: TitleScreen, CarSelect, MissionHud, IntroOverlay, LoadingOverlay,
                          NeonRift.uss (shared theme), NeonRiftTheme.tss, NeonRiftPanelSettings.asset
```

## `Assets/ThirdParty/`

```
ThirdParty/
├─ Sketchfab/
│  ├─ Vehicles/   Mercedes_SLS_AMG_2010, Mercedes_SLS_GT3, Lamborghini_Terzo_Millennio,
│  │              Mercedes_AMG_GT3_RedBull (excluded from the catalog), generic-passenger-car-pack
│  ├─ Buildings/  London_Skyscraper, Singapore_Office_Skyscraper, Asian_Night_City_Buildings, Night_City_Building_Skyline
│  └─ Drones/     Mech_Drone, Buster_Drone, Drone_Design (not used)
└─ SoundAudio/    machine-speed-sport-aggressive-electronic.mp3, danger-chaos-cinematic-trailer-hybrid.mp3
```

Every Sketchfab model folder except the passenger car pack has an `ATTRIBUTION.txt`. See [44 Assets and Licensing](44_ASSETS_AND_LICENSING.md).

## `Docs/`

| File | Topic |
|---|---|
| `Architecture.md`, `Decisions.md` (D1–D27), `Conventions.md` | Architecture notes, decision records, coding rules |
| `City.md`, `Environment.md` | City generation, night look, LODs |
| `NightRun.md`, `Interactions.md` | Mission, heist, gate, rivals |
| `VehiclePhysics.md`, `Audio.md` | Vehicle model, audio |
| `Intro.md` | Cinematic intro and Car Select |
| `Performance.md` | Profiling and optimisation |
| `ThirdParty.md`, `Licenses/` | Asset audit and licences |
| `SourceControl.md` | Git, LFS, authorship |
| `Screenshots/`, `Audio/` | Captures and recorded mix excerpts |
| `NEON_RIFT_BOOK/` | This book |

The older notes were written phase by phase. Where they disagree with the code (for example the rival "orders" that were replaced by rival tasks), this book follows the code and says so.

## Naming conventions (from `Docs/Conventions.md`)

| Kind | Pattern | Example |
|---|---|---|
| Vehicle definition | `Vehicle_<Name>` | `Vehicle_MercedesSLS` |
| Mission | `Mission_<Name>` | `Mission_NightRun` |
| Prefab | `PF_<Category>_<Name>` | `PF_Vehicle_MercedesSLS`, `PF_Drone_Mech` |
| Material | `<Area>_<Surface>` | `District_BeaconCyan` |
| Namespace | assembly name | `NeonRift.Gameplay` |

Units are metres, kilograms and seconds (km/h for display only). Vehicle prefabs face +Z with the pivot at ground centre.
