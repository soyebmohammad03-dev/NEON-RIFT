# Conventions

## Code
- Namespace = assembly name (`NeonRift.Vehicles`, …). One public type per file, file named after the type.
- `[SerializeField] private` fields with read-only properties; no public mutable fields on components.
- No singletons or static mutable state (domain reload is disabled). Pass dependencies in through `GameContext` or serialized references.
- Log prefix per system: `[GameFlow]`, `[CarSelect]`, `[Mission]` …
- Tunable numbers belong in ScriptableObjects or serialized fields with tooltips and units, not literals.

## Assets
| Kind | Pattern | Example |
|---|---|---|
| Vehicle definition | `Vehicle_<Name>` | `Vehicle_Spectre` |
| Mission | `Mission_<Name>` | `Mission_NightRun` |
| Material | `<Area>_<Surface>` | `DevTrack_Asphalt` |
| Prefab | `PF_<Category>_<Name>` | `PF_Vehicle_Spectre` |
| Scene | PascalCase | `CarSelect` |

- Units: metres, kilograms, seconds, km/h for display only.
- Vehicle prefabs: +Z forward, pivot at ground centre, real-world scale.

## Third-party assets
- Import into their own root folder (`Assets/<Vendor>/…`); never move or edit vendor files.
- To change a vendor asset, create a prefab variant or material copy under `_NeonRift`.
- Record every external asset (source, licence, purpose) in `Docs/ThirdParty.md` when it is added.

## Source control
- Install Git LFS before committing binaries; `.gitattributes` already routes models, textures, audio and video to LFS.
- Unity YAML merges: configure UnityYAMLMerge as the `unityyamlmerge` merge driver (see Unity manual "Smart merge").
- Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/` or build output.
