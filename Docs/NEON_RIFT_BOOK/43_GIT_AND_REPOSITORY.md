# 43 · Git and Repository

> [Index](00_INDEX.md) · Previous: [42 Build and Release](42_BUILD_AND_RELEASE.md) · Next: [44 Assets and Licensing](44_ASSETS_AND_LICENSING.md)

## Repository

| | |
|---|---|
| Remote | `https://github.com/soyebmohammad03-dev/NEON-RIFT` (public) |
| Branch | `main` (single branch) |
| Author | All commits are authored by **Soyeb Mohammad** (the first two under the local name `soyebmohammad`) |
| Guide | `Docs/SourceControl.md` |

## Git LFS and merging

- `.gitattributes` has 49 LFS rules: models (FBX, GLB, OBJ, blend), textures, audio, video, EXR reflection probes, screenshots, zips, and the generated district meshes (`Assets/_NeonRift/Art/District/Meshes/*.asset`). About 4,900 files are stored in LFS.
- Unity YAML (scenes, prefabs, assets, materials, mixers, playables, lighting) is text with `merge=unityyamlmerge`. Register Unity's SmartMerge once per clone (`Docs/SourceControl.md`):

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
```

```bash
git config merge.unityyamlmerge.driver "'<Unity.app>/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
```

## Never committed (`.gitignore`)

`Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`, `UserSettings/`, `MemoryCaptures/`, `Recordings/`, `ProfilerCaptures/`, IDE and solution files (`*.csproj`, `*.sln`, `*.slnx`), `SourceArchives/` (original downloaded archives) and the stray `My project/` folder.

## History (29 commits, oldest at the bottom)

| Commit | Date | Summary |
|---|---|---|
| `602c7130` | 2026-10-04 | Rival heists and qualifying, drone pursuit, Car Select exit fix, busier intro |
| `26d7db2d` | 2026-10-04 | Pack-car traffic, licensed music, pause menu and settings, full-route performance |
| `3ba63dd6` | 2026-10-04 | Adaptive procedural score, voice priorities and master limiter |
| `90cdba32` | 2026-10-04 | Mission presentation: operation briefing, split stamps, graded debrief |
| `c9e7ae9a` | 2026-10-04 | City life: pooled ambient traffic, plaza crowds, security drones |
| `3e96fbee` | 2026-10-04 | URP visual pass: restrained lockdown grade, security red, rear-glass fix |
| `f9925bb9` | 2026-10-04 | Car Select premium pass |
| `cb96f3e3` | 2026-10-04 | Cinematic intro rework: 84 s story through the crew garage |
| `c54fd8b0` | 2026-10-04 | Three-car heist and rival collision fix |
| `a70e49b8` | 2026-10-04 | Security gate hack: five-stage controller hack with physical locks |
| `54ac9c00` | 2026-10-04 | Data Core heist: multi-stage interactions, extraction chamber |
| `4b6b6a76` | 2026-10-04 | Car Select: generated crew garage, turntable, departure |
| `1a11bafc` | 2026-10-04 | North-up minimap and block-interior city fill |
| `c4cfa0a6` | 2026-10-03 | Opening cinematic (first version) |
| `5573b5c9` | 2026-10-03 | Camera and HUD polish |
| `795c55d3` | 2026-10-03 | LODs and performance |
| `2355d226` | 2026-10-03 | City detail |
| `62487243` | 2026-10-03 | City lighting and atmosphere: tower facades, crowns, ground haze |
| `2b072ff7` | 2026-10-03 | Docs: rival validation runs |
| `4e6a57a0` | 2026-10-03 | Rival AI: traffic awareness, path occupancy, blocked-route memory |
| `b187d1af` | 2026-10-03 | Input: verify and harden keyboard/gamepad driving |
| `dcf63430` | 2026-10-03 | Night look foundation |
| `16285452` | 2026-10-03 | Licence and ownership notice, source-control guide |
| `62ebb897` | 2026-10-03 | Night Run city: five districts, rival AI, night lighting, navigator HUD |
| `de7bbb83` | 2026-10-02 | Night Run vertical slice |
| `3d5ae392` | 2026-10-02 | Engine audio |
| `9e77107f` | 2026-10-02 | Vehicle physics |
| `97530885` | 2026-10-02 | Asset intake |
| `be0ec0be` | 2026-10-02 | Phase 1: project foundation |

**History note** (`Docs/SourceControl.md`): five early commits (`be0ec0b`, `9753088`, `9e77107`, `3d5ae39`, `de7bbb8`) carry a `Co-Authored-By: Claude` trailer added by the AI coding tool used during development, and were published unchanged on purpose. Later commits carry no such trailer.

## Project licence

`LICENSE.md` at the repository root (ownership notice, added in `16285452`).
