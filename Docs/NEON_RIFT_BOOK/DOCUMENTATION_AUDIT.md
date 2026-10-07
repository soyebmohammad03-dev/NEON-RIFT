# Documentation Audit

> [Index](00_INDEX.md)

Audit of `Docs/NEON_RIFT_BOOK/` against the repository at commit `602c7130` (Unity 6000.6.4f1), carried out on 2026-10-08.

## Method

1. **Inventory:** all 198 C# files were parsed for their types and XML summaries; method signatures with line numbers were extracted; scenes were parsed for component counts and project MonoBehaviours; prefabs, data assets, UI files, shaders, packages, project settings and the input-actions asset were listed.
2. **Source reading:** the key runtime and builder code was read directly (game flow, spawn, vehicle step, tyre model, rival director and driver, mission state machine, director, interaction definitions, builders for gates, barriers, lighting, rivals, intro shots and cues).
3. **Automated reference checks** over every chapter:
   - **831** backticked file paths checked for existence. Only intentional exceptions remain: git-ignored or local folders (`Temp/`, `Logs/`, `Builds/`), folder names given relative to a stated parent (`Engine_EV/`, `Mission/`), and the three `Docs/Licenses/*.png` files (see Findings).
   - **427** `Class.Member` references checked against the defining source files: **0 missing**.
   - Bare backticked identifiers checked against the code and asset names. The only names not found are Unity engine types (`WheelCollider`, `ParticleSystem`, `EventSystem`, `InputSystemUIInputModule`, `UnityStats`) and the scene object name `RivalWorkFx_n`, all used correctly.
   - Every `](…​.md)` link between chapters resolves.

## System coverage

| System | Documented? | Primary files verified? | Scene verified? | Data verified? | Cross-references verified? |
|---|---|---|---|---|---|
| Boot, flow, scenes | ✔ (05, 06) | ✔ `GameRoot`, `GameFlow`, `BootstrapLoader` | ✔ build list, Bootstrap contents | ✔ `GameConfig.asset` | ✔ |
| City generation | ✔ (07) | ✔ `NightRunBuilder.Build` order read | ✔ NightRun component counts | ✔ `CityLayout` data | ✔ |
| Buildings and environment | ✔ (08) | ✔ | ✔ | ✔ building catalog and prefabs | ✔ |
| Roads and navigation | ✔ (09) | ✔ `RoadGraph`, `CityNavigation` | ✔ | ✔ network asset, gate ids | ✔ |
| Vehicle system and physics | ✔ (10, 11) | ✔ `VehicleController.Step` read | ✔ | ✔ profile values read from assets | ✔ |
| Vehicle audio | ✔ (12) | ✔ | — | ✔ audio profiles, generated folders | ✔ |
| Vehicle visuals | ✔ (13) | ✔ `UpdateVisuals`, lights, LODs | — | ✔ prefabs | ✔ |
| Catalog and data | ✔ (14) | ✔ | — | ✔ ids, names, categories, licences from assets | ✔ |
| Car Select | ✔ (15) | ✔ | ✔ CarSelect components | ✔ UXML element names | ✔ |
| Input | ✔ (16) | ✔ | — | ✔ bindings parsed from `.inputactions`; M binding in `GameRoot` | ✔ |
| Rival AI | ✔ (17) | ✔ `RivalDirector`, `RacerDriver.ReadInput` | ✔ 5 race markers | ✔ task data, profiles | ✔ |
| Traffic | ✔ (18) | ✔ | ✔ | ✔ | ✔ |
| Mission system | ✔ (19) | ✔ `MissionProgress` read in full | ✔ 2 zones, 3 interactables | ✔ `Mission_NightRun.asset` read | ✔ |
| Heist interactions | ✔ (20) | ✔ stage definitions read from the builder | ✔ | ✔ | ✔ |
| Security gate | ✔ (21) | ✔ | ✔ | ✔ | ✔ |
| Data Core | ✔ (22) | ✔ (phases from the class summary and method list) | ✔ | ✔ | ✔ |
| Heat and lockdown | ✔ (23) | ✔ event wiring grepped in the builder | ✔ 5 barriers, 12 cameras, 35 light groups, 2 alarms | ✔ | ✔ |
| Environmental interactions | ✔ (24) | ✔ | ✔ | — | ✔ |
| Lighting | ✔ (25) | ✔ `BuildLighting`, GI settings | ✔ 862 lights, 63 probes | ✔ RP assets, volumes | ✔ |
| Weather / atmosphere | ✔ (26): rain stated **not implemented** | ✔ | — | ✔ | ✔ |
| Water and effects | ✔ (27): water stated **not implemented** | ✔ asphalt puddle code | — | — | ✔ |
| Particles and VFX | ✔ (28): **no ParticleSystem anywhere** (scenes, prefabs, scripts checked) | ✔ | ✔ | — | ✔ |
| Audio | ✔ (29) | ✔ | ✔ limiter in NightRun and CarSelect | ✔ mixer config, audio set fields, clip lists | ✔ |
| Music | ✔ (30) | ✔ | — | ✔ | ✔ |
| Cinematic intro | ✔ (31) | ✔ shot table and cue table read from `IntroBuilder` | ✔ 25 Animators, 1 PlayableDirector | ✔ Timeline, settings | ✔ |
| Timeline and cinematic camera | ✔ (32) | ✔ | ✔ | ✔ | ✔ |
| UI and HUD | ✔ (33) | ✔ setters with line numbers | — | ✔ UXML element names | ✔ |
| Minimap | ✔ (34) | ✔ | — | — | ✔ |
| Mission presentation | ✔ (35) | ✔ | — | ✔ announcements from the builder | ✔ |
| Results and debrief | ✔ (36) | ✔ `MissionGrade.Evaluate` read | — | — | ✔ |
| Animation systems | ✔ (37) | ✔ | ✔ | ✔ | ✔ |
| Camera | ✔ (38) | ✔ serialized defaults + builder overrides | ✔ | — | ✔ |
| Performance | ✔ (39): numbers only from recorded docs | ✔ tools | — | ✔ RP asset settings | ✔ |
| Testing | ✔ (40, 41) | ✔ 98 methods / 125 cases counted | — | — | ✔ |
| Build and release | ✔ (42) | ✔ `PerfBuild` | ✔ build list | ✔ project settings | ✔ |
| Git and repository | ✔ (43) | — | — | ✔ 29 commits, LFS rules | ✔ |
| Assets and licensing | ✔ (44) | — | — | ✔ every `ATTRIBUTION.txt`, `LICENSE.md`, definition licences | ✔ |

## Checks requested

| Check | Result |
|---|---|
| Every major system has a chapter | ✔ |
| Every chapter points to actual files | ✔ (automated) |
| Every important system has a "where is the code" section | ✔ |
| No fictional files or classes | ✔ (automated, 0 missing members) |
| No obsolete implementation presented as current | ✔. Obsolete items in older notes are flagged (below) |
| Unity and package information correct | ✔ from `ProjectVersion.txt` and `manifest.json`; unused packages marked |
| Scene names correct | ✔ Bootstrap, Frontend, CarSelect, Dev/TestTrack, NightRun |
| Controls correct | ✔ parsed from the input asset and `GameRoot` |
| Vehicle names correct | ✔ SLS AMG, SLS AMG GT3, Terzo Millennio (catalog); AMG GT3 excluded |
| Mission flow correct | ✔ from `Mission_NightRun.asset` and `CreateMissionData` |
| Rival systems correct | ✔ current `RivalTask` system (not the removed `RivalOrder`) |
| Licensing correct | ✔ copied from attribution files; unknown licences stated as unknown |

## Findings (outside the book)

1. **Older notes describe superseded behaviour.** In `Docs/Decisions.md` D26 (rival goals per objective via `rivalGoalId`, removed in `602c7130`); parts of `Docs/NightRun.md` (rival orders, crew roles NORTH GATE OVERWATCH / EXPRESSWAY SCOUT, "4 small particle systems", rivals parking in the S7 car park); the first section of `Docs/Intro.md` (62 s / 18 cameras, the earlier version); `Docs/Architecture.md` (Frontend list without `ShowroomEngine`); and the README ("heading-up minimap"). Per the brief, these files were **not modified**; the book documents the current code.
2. **Licence screenshots deleted in the working tree.** `Docs/Licenses/*.png` are committed (in `602c7130`) but currently deleted in the local working copy. This pre-existing change was left untouched and is not part of the documentation commit.
3. **Licences not recorded** for the generic passenger car pack and the two music tracks; `LICENSE.md` does not list the drones, the car pack or the music.
4. **Installed but unused packages:** Splines, ProBuilder, AI Navigation.

## Could not be fully traced

- **Exact per-frame behaviour inside `DataCoreChamber`** (668 lines) is described from its class summary, method structure and the recorded heist documentation, not line by line.
- **`CityInfill` and `CityProps` internals** are summarised at the algorithm level (method names verified); numeric counts come from the recorded build logs in `Docs/City.md`.
- **Performance figures** are reproduced from recorded measurements; none were re-measured for this book, and no player-build profile exists after the latest content additions.
- **Mixer snapshot dB offsets** are taken from `Docs/Audio.md`; the `.mixer` asset itself was not decoded.

## PDF edition (`NEON_RIFT_COMPLETE_TECHNICAL_BOOK.pdf`)

The PDF is compiled from the Markdown chapters in this folder. Its content is the same as theirs; only the presentation changes. Notes on the compilation:

- **Verification status.** The 831-path and 427-member checks above were made against `602c7130`. The commits since then change only `README.md` and `Docs/` (`git diff --stat 602c7130 HEAD` shows no code or asset change), so those results still apply. The PDF was not re-verified line by line. Two checks were made during compilation:
  - **Paths:** Find It Fast and the Q&A cards spell out the `S/` and `D/` shorthand, and expand 35 bare file names (for example `CityLayout.cs`) to full paths when exactly one tracked file has that name. All **98** full paths shown in those two sections exist in the repository.
  - **Destinations:** every chapter, part and figure in the TOC has a link destination in the PDF.
- **Added diagrams.** Three diagrams have no Markdown source: 17.2 Rival heist flow, 21.1 Security Gate hack flow and 22.2 Data Core heist flow. Each one only restates the steps, events, durations and ids written in the text of its chapter. The intro timeline (31.2) is drawn from the shot and cue tables in chapter 31.
- **Layout-only changes.**
  - Some diagrams are laid out left-to-right instead of top-to-bottom (or the reverse) so they fit the page.
  - The 10-column physics-profile table in chapter 14 is transposed.
  - The shot table in chapter 31 is shown as one card per shot. The column "Objects" is labelled "Action / on screen".
  - Section headings that carried their own number ("1. High-level shape") use the book's section number instead.
  - The "A. Table of contents" of `00_INDEX.md` is replaced by the PDF's own Contents.
- **Licensing at a glance** (chapter 44) regroups the chapter's existing licence facts into categories. It adds no new licence information.
- **Screenshots** come from `Docs/Screenshots/`: real captures made with the project's capture tools during development. Some may predate small later changes. No image was created or edited for the book.
- **Discrepancies found while compiling:** none in the technical content. The cue table and the shot table in chapter 31 agree on the 25 shots (shots 19–24 being the montage). The question counts (36), figure counts and chapter list (48 plus two appendices) match the Markdown.
