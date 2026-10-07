# 45 · Known Limitations

> [Index](00_INDEX.md) · Previous: [44 Assets and Licensing](44_ASSETS_AND_LICENSING.md) · Next: [46 Developer Quick Reference](46_DEVELOPER_QUICK_REFERENCE.md)

An honest list, collected from `Docs/*.md` and checks made while writing this book.

## Not implemented

| Feature | Notes |
|---|---|
| Rain, weather states, water bodies, splashes | Only an "after rain" asphalt material look ([26](26_WEATHER_AND_ATMOSPHERE.md), [27](27_WATER_AND_EFFECTS.md)) |
| Particle effects (smoke, sparks, debris) | No `ParticleSystem` anywhere ([28](28_PARTICLES_AND_VFX.md)) |
| Vehicle damage, destructible props | Collisions give sound, shake and contact counts only |
| Animated pedestrians or cranes | Crowds are static silhouettes that sway; cranes are static |
| Global illumination | Baked and realtime GI both off; real-time lights + baked reflection probes |
| More missions / "next level" | Only Night Run. Qualifying is recorded (`MissionRecords.HasQualified`), but nothing unlocks yet |
| Manual gear shifting | `Gearbox.Shift(int)` exists as a hook; there is no input binding |
| Saved car choice between launches | `RunSession` is memory only |
| Traffic obeying signals | Signals are visual; traffic yields at junctions to the player and rivals |
| Pre-rendered intro video | The `IntroSettings.Mode.Video` path is implemented but untested (no clip) |

## Vehicle physics (`Docs/VehiclePhysics.md`)

No unsprung mass (no wheel hop); no tyre relaxation, temperature or wear; a 1.5 m/s low-speed slip floor; differentials are speed-coupling approximations; a two-box body collider; no anti-dive or anti-squat; the GT3 scrapes 10 cm bumps at ~55 km/h; collision events fire only in Play Mode.

## AI

- Rivals occasionally make racing contact with the player where routes cross (e.g. at the compound gate). Timing-dependent contacts are logged (`[Rivals] contact …`).
- Kade's pace varies with traffic on Lantern Street; the rubber band is deliberately small (±5–6 %).
- The validation driver `RouteAutopilot` is not race AI (no countersteer, fixed braking assumption).

## Audio

Procedural engines are convincing but not recordings of real cars; mild formant shift between layers; the EV is silent at standstill; no occlusion or reverb zones; **the score balance and the intro mix were measured but not judged by ear**.

## Visual

- Towers have no LODs (2.6–4.2 M triangles in view at times).
- Car paint has no clear coat (glTFast clearcoat remapped, D12); NaN safeguards (Stop NaN) remain on cameras (D13).
- In the editor, the first play after a rebuild can show flat-cyan materials while shaders compile asynchronously (not in builds).
- The garage exterior is a plain box (`Docs/Intro.md`).

## Performance

- No player-build profile recorded after the pack traffic, rival heists, drones and intro city life were added.
- Mission start hitches on first shader use; about 70 real-time lights at the lockdown peak.

## Data and licensing

- Three supplied assets (the passenger car pack and two music tracks) have no recorded licence; `LICENSE.md` doesn't yet list the drones, the car pack or the music.
- The Terzo Millennio (CC-BY-NC) is in the catalog, so the project is non-commercial as it stands.

## Documentation drift

Some older notes describe superseded behaviour (rival "orders" and `rivalGoalId`, crew roles NORTH GATE OVERWATCH / EXPRESSWAY SCOUT, the heading-up minimap in the README, "4 small particle systems", "62 s Timeline / 18 shot cameras" in the first part of `Docs/Intro.md`, D26 in `Docs/Decisions.md`). This book documents the current code. See [DOCUMENTATION_AUDIT](DOCUMENTATION_AUDIT.md).
