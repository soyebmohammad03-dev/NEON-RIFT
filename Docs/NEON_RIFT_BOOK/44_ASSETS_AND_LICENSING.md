# 44 · Assets and Licensing

> [Index](00_INDEX.md) · Previous: [43 Git and Repository](43_GIT_AND_REPOSITORY.md) · Next: [45 Known Limitations](45_KNOWN_LIMITATIONS.md)

Licence information below is copied from the repository's own records: each folder's `ATTRIBUTION.txt`, `Docs/ThirdParty.md`, `LICENSE.md` and the `AssetLicense` fields on the vehicle definitions. Nothing here is inferred.

## First-party content

`LICENSE.md`: "Copyright (c) 2026 Soyeb Mohammad. All rights reserved." This covers the source code, scenes, generated meshes and textures, the signage font (`PixelFont`), shaders, UI and **all audio not listed as third-party** (the engines, sirens, cues, score and intro sounds are synthesised in-house). The repository is public for viewing; no open-source licence is granted.

## Third-party assets

| Asset | Source | Licence | Location | Usage | Commercial restriction |
|---|---|---|---|---|---|
| 2010 Mercedes SLS AMG (Dave Love SketchFab) | sketchfab.com/3d-models/2010-mercedes-sls-amg-fa3fd5eeea674f37bb03283f2c53d563 | CC-BY-4.0 | `Assets/ThirdParty/Sketchfab/Vehicles/Mercedes_SLS_AMG_2010/` | Player/rival car (catalog) | Commercial allowed with attribution |
| Mercedes SLS GT3 (Dave Love SketchFab) | …/mercedes-sls-gt3-5ec93bea4816494cbe3e7333bbfca5f6 | CC-BY-4.0 | `…/Vehicles/Mercedes_SLS_GT3/` | Player/rival car (catalog) | Commercial allowed with attribution |
| ( FREE ) Lamborghini Terzo Millennio (SDC PERFORMANCE) | …/free-lamborghini-terzo-millennio-7ad3dffa9d344c3c978eafcc220cb709 | **CC-BY-NC-4.0** | `…/Vehicles/Lamborghini_Terzo_Millennio/` | Player/rival car (catalog) | **Non-commercial only** |
| Mercedes-AMG GT3 Red Bull Racing (VTX) | …/mercedes-amg-gt3-red-bull-racing-035f061cb6624effbc483d5f9b5ecaba | **CC-BY-NC-SA-4.0** | `…/Vehicles/Mercedes_AMG_GT3_RedBull/` | Prefab built, **kept out of the catalog** (material defects) | **Non-commercial, share-alike** |
| Generic passenger car pack (10 cars) | Supplied by the project owner | **Not recorded** (no licence or author file in the folder; `Docs/ThirdParty.md`: "Record the source and licence here before any distribution") | `…/Vehicles/generic-passenger-car-pack/` | City traffic (`Prefabs/Traffic/PackCar_*`) and intro traffic | **Unknown** |
| [FREE] London Skyscraper (99.Miles) | …/free-london-skyscraper-52b73f6ea18a440cb42734840d5edc72 | CC-BY-4.0 | `Assets/ThirdParty/Sketchfab/Buildings/London_Skyscraper/` | Hero landmark (Spire Plaza) | Commercial allowed with attribution |
| Singapore Office Skyscraper [FREE] (99.Miles) | …/singapore-office-skyscraper-free-2305f0fe03ba44229a1f768dcb48bd8f | CC-BY-4.0 | `…/Buildings/Singapore_Office_Skyscraper/` | Hero towers | Commercial allowed with attribution |
| Asian Themed Low Poly Night City Buildings (99.Miles) | …/asian-themed-low-poly-night-city-buildings-9f0343aff4814b758dc6e905aba5b5e0 | CC-BY-4.0 (Sketchfab page) | `…/Buildings/Asian_Night_City_Buildings/` | 19 tower prefabs | Commercial allowed with attribution |
| Low Poly Night City Building Skyline (99.Miles) | …/low-poly-night-city-building-skyline-b0035b8713b048bb8ddf311ee67c28c8 | CC-BY-4.0 (Sketchfab page) | `…/Buildings/Night_City_Building_Skyline/` | Catalog entry only (clusters removed from the city) | Commercial allowed with attribution |
| Mech Drone (Willy Decarpentrie) | …/mech-drone-8d06874aac5246c59edb4adbe3606e0e | CC BY 4.0 | `Assets/ThirdParty/Sketchfab/Drones/Mech_Drone/` | Security drones, intro drones (`PF_Drone_Mech`) | Commercial allowed with attribution |
| Buster Drone (LaVADraGoN) | …/buster-drone-294e79652f494130ad2ab00a13fdbafd | CC BY 4.0 | `…/Drones/Buster_Drone/` | Compound sentries (`PF_Drone_Buster`) | Commercial allowed with attribution |
| Drone Design (3DHaupt) | …/drone-design-592c49fb806c42dba309e4d7a31d4a57 | **CC BY-NC-ND 4.0** | `…/Drones/Drone_Design/` | **Not used in the game**; kept unmodified for reference | **Non-commercial, no derivatives** |
| `machine-speed-sport-aggressive-electronic.mp3` | Supplied by the project owner | **Not recorded** (no licence file) | `Assets/ThirdParty/SoundAudio/` | Gameplay music | **Unknown** |
| `danger-chaos-cinematic-trailer-hybrid.mp3` | Supplied by the project owner | **Not recorded** (no licence file) | `Assets/ThirdParty/SoundAudio/` | Intro soundtrack | **Unknown** |
| dasy444 "city at night low poly skyscrapers" | Sketchfab | Sketchfab "Free Standard" | Not imported (archive in `SourceArchives/`, git-ignored) | **Rejected**: photo textures of unverifiable provenance | — |

Each Sketchfab model folder except the passenger car pack has an **`ATTRIBUTION.txt`** (title, author, source, licence, origin and the changes made). The drone licence pages were captured as `Docs/Licenses/mech_drone_sketchfab_licence.png`, `buster_drone_sketchfab_licence.png` and `drone_design_sketchfab_licence.png` (committed in `602c7130`).

## How licences travel with content (Decision D14)

`AssetLicense` (`Assets/_NeonRift/Scripts/Core/AssetLicense.cs`: `LicenseId`, `Author`, `SourceUrl`, `CommercialUseAllowed`, `AttributionLine`) is stored on every `VehicleDefinition` and `BuildingDefinition`. `ProjectValidator.Warnings()` lists non-commercial ones so a release build can exclude them. `VehicleModelTests.Catalog_ExcludesAmgGt3_AndFlagsNonCommercialLicences` checks the catalog.

## Rules (Decision D9, `Docs/Conventions.md`)

Vendor packs keep their own import root and are never edited in place. Changes (orientation, scale, material copies, split wheel meshes, baked drone meshes) are made in `_NeonRift` prefabs and assets.

## Trademarks

Mercedes-Benz/AMG, Lamborghini, Red Bull, Lukoil, Blancpain, Michelin and other liveries on the car models are third-party trademarks. A commercial release would need de-branded liveries and fictional names (`Docs/ThirdParty.md`, `LICENSE.md`).

## Overall commercial status

Because the **Terzo Millennio** (CC-BY-NC) is in the player catalog, and three supplied assets have no recorded licence, **the project as it stands must not be used commercially** (`LICENSE.md` says the same about the non-commercial models).

## Gaps

- `LICENSE.md`'s third-party table lists the cars and buildings but **not** the two CC BY drones, the passenger car pack or the music.
- The source and licence of the passenger car pack and the two music tracks still need to be recorded.
