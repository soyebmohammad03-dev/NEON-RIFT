# Third-party dependencies and asset audit

Rules: the project owner supplies building and vehicle models. Everything else must be free (Unity registry, free Asset Store, or an open licence). No paid dependencies.
Third-party content lives under `Assets/ThirdParty/<Source>/` and is never edited in place. Each asset folder has an `ATTRIBUTION.txt`. Neon Rift prefabs, materials and generated meshes live under `Assets/_NeonRift/`.
Re-run the full intake with **Neon Rift ▸ Assets ▸ Run Third-Party Intake** (`Scripts/Editor/ThirdPartyIntake.cs`).

## Unity packages (Unity Registry, free)

| Package | Version | Purpose |
|---|---|---|
| com.unity.render-pipelines.universal | 17.6.0 | Rendering (URP) |
| com.unity.inputsystem | 1.19.0 | Input |
| com.unity.cinemachine | 6.6.0 | Cameras |
| com.unity.splines | 2.9.1 | Road / route / AI paths |
| com.unity.probuilder | 6.1.2 | Blockout geometry |
| com.unity.cloud.gltfast | 6.20.0 | glTF/GLB import (Sketchfab models) — added in asset-intake phase |
| com.unity.ugui | 2.6.0 | EventSystem / input module |
| com.unity.timeline | 6.6.0 | Cinematics |
| com.unity.ai.navigation | 2.0.12 | Navigation |
| com.unity.test-framework | 1.8.0 | Tests |
| com.unity.ide.rider / ide.visualstudio | — | IDE integration |
| com.unity.collab-proxy | 2.12.4 | Unity Version Control client (optional) |
| com.unity.ai.assistant / ai.inference / pipeline | — | Unity AI tooling used by the editor MCP bridge |

Tooling outside Unity: Git LFS 3.8.0 (official GitHub release, installed to `~/.local/bin`).

## Licence summary

| Asset | Author | Licence | Commercial use | Status |
|---|---|---|---|---|
| [2010 Mercedes SLS AMG](https://sketchfab.com/3d-models/2010-mercedes-sls-amg-fa3fd5eeea674f37bb03283f2c53d563) | Dave Love SketchFab | CC-BY-4.0 | Yes, with attribution | In catalog |
| [Mercedes SLS GT3](https://sketchfab.com/3d-models/mercedes-sls-gt3-5ec93bea4816494cbe3e7333bbfca5f6) | Dave Love SketchFab | CC-BY-4.0 | Yes, with attribution | In catalog |
| [( FREE ) Lamborghini Terzo Millennio](https://sketchfab.com/3d-models/free-lamborghini-terzo-millennio-7ad3dffa9d344c3c978eafcc220cb709) | SDC PERFORMANCE | **CC-BY-NC-4.0** | **No** | In catalog, flagged |
| [Mercedes-AMG GT3 Red Bull Racing](https://sketchfab.com/3d-models/mercedes-amg-gt3-red-bull-racing-035f061cb6624effbc483d5f9b5ecaba) | VTX | **CC-BY-NC-SA-4.0** | **No** | Prefab built, **kept out of catalog** |
| [[FREE] London Skyscraper](https://sketchfab.com/3d-models/free-london-skyscraper-52b73f6ea18a440cb42734840d5edc72) | 99.Miles | CC-BY-4.0 | Yes, with attribution | In catalog |
| [Singapore Office Skyscraper [FREE]](https://sketchfab.com/3d-models/singapore-office-skyscraper-free-2305f0fe03ba44229a1f768dcb48bd8f) | 99.Miles | CC-BY-4.0 | Yes, with attribution | In catalog |
| [Asian Themed Low Poly Night City Buildings](https://sketchfab.com/3d-models/asian-themed-low-poly-night-city-buildings-9f0343aff4814b758dc6e905aba5b5e0) | 99.Miles | CC-BY-4.0 (Sketchfab page) | Yes, with attribution | In catalog |
| [Low Poly Night City Building Skyline](https://sketchfab.com/3d-models/low-poly-night-city-building-skyline-b0035b8713b048bb8ddf311ee67c28c8) | 99.Miles | CC-BY-4.0 (Sketchfab page) | Yes, with attribution | In catalog |
| [city at night low poly skyscrapers](https://sketchfab.com/3d-models/city-at-night-low-poly-skyscrapers-dc1294de66194054961c16aa74fda2cb) | dasy444 | Sketchfab "Free Standard" | Unclear | **Rejected** |

Sources: the GLB licences come from each file's embedded glTF `asset.extras`. The three ZIP packs carry no licence file; their licences were read from the Sketchfab model pages on 2026-10-02.

**Trademarks.** CC licences cover the 3D model's copyright only. Mercedes-Benz/AMG, Lamborghini, Red Bull, Lukoil, Blancpain, Michelin and other logos and liveries are third-party trademarks. A commercial release needs de-branded liveries and fictional names.

**Non-commercial assets.** The Terzo Millennio and the AMG GT3 must not ship in a commercial build. `ProjectValidator.Warnings()` lists them.

## Audio

All game audio is generated in-house by `VehicleAudioGenerator` and `MissionAudioGenerator` (procedural synthesis, deterministic: engines, tyres, sirens, klaxon, barrier motor/slam, city ambience, hack loop, HUD cues). No third-party sound files are used and none were downloaded. See [Audio.md](Audio.md) for free, clearly licensed sources if recorded sounds are wanted later.

## District art (Night Run)

All district textures (asphalt, pavement, facades, shopfronts, signage, billboards, glow shapes) are generated in-house by `DistrictTextures`, including the signage font (`PixelFont`, a first-party 5×7 bitmap font). Shaders `NeonRift/NightSky` and `NeonRift/AdditiveGlow` are first-party. No third-party textures, fonts or packages were added; the only external models used are the audited buildings above.

## Vehicle audit

| | SLS AMG 2010 | SLS GT3 | Terzo Millennio | AMG GT3 (Red Bull) |
|---|---|---|---|---|
| File | GLB 14 MB | GLB 16 MB | GLB 9 MB | GLB 57 MB |
| Triangles | 178k | 176k | 108k | **497k** |
| Source scale / axis | real scale, +Z fwd | real scale, +Z fwd | **0.74× scale**, +Z fwd | real scale, **nose −X** |
| Size after prep (W×H×L m) | 2.08×1.26×4.64 | 2.05×1.65×4.85 | 2.25×1.02×4.80 | 2.05×1.24×4.65 |
| Wheelbase | 2.68 m | 2.68 m | 2.81 m | 2.63 m |
| Wheel meshes | merged ×4 → split | merged ×4 → split | rims per wheel, tyres per axle → split | rims/tyres partly merged → split |
| Tyre radius F / R | 0.340 / 0.346 | 0.340 / 0.355 | 0.330 / 0.350 | 0.340 / 0.355 |
| Motion-blur rims | yes (hidden at rest) | yes (hidden at rest) | yes (hidden at rest) | no |
| Brake discs / calipers | yes / yes (static) | yes / yes (static) | — | yes / yes (static) |
| Glass | yes (+ damage overlay, hidden) | yes (+ damage overlay, hidden) | yes | yes — **renders cyan** |
| Head / tail lamp meshes | 2 / 1 | 2 / 1 | 1 / 3 | 2 / 1 |
| Interior | full | full race cockpit | partial | full race cockpit |
| LODs | none | none (only "LodA") | none | none |
| Materials | glTFast URP graphs; 1 clearcoat remapped | 1 clearcoat remapped | OK | 15 clearcoat slots remapped; livery speckle |
| Defects | — | — | animation rig parts displaced (3 hidden incl. rear-wing panel); no official specs | glass + livery render errors at runtime |
| Colliders | two-box body hull (vehicle-physics phase) | two-box | two-box | two-box (prefab only) |
| Verdict | **Hero car, ready for physics** | **Ready** | **Usable, non-commercial** | **Excluded** until materials rebuilt + LOD |

Car Select stats are measured from each car's physics profile on the vehicle bench (vehicle-physics phase), not quoted from manufacturers. The SLS AMG profile uses published mass, power, torque, gearing and weight split; the SLS GT3 profile is estimated for a typical BoP race car; the Terzo Millennio is a concept car with no official figures, so its profile is a fictional EV interpretation.

## Building audit

| Asset | Format | Tris | Size (W×H×D m) | Pivot fix | Materials | Tier | Collider |
|---|---|---|---|---|---|---|---|
| London Skyscraper | GLB 46 MB | 143k | 37×166×51 | sidewalk top → y=0 (source −4.13) | glTFast graphs; glass, lit lobby interior, emissive; 3 clearcoat remapped | Hero (one-off landmark) | box |
| Singapore Office | GLB 10 MB | 26k | 59×106×56 | base → y=0 (source floats 2.2 m) | glTFast; interior floors behind glass; 1 clearcoat remapped | Hero | box |
| Asian Night City (19 bldgs) | FBX + 2×2K atlases | 10–1.5k each | 24–90 wide, 56–300 tall | per building | rebuilt URP Lit: baked-light emissive, packed smoothness, B2 alpha-clip; additive roof flares | 11 Midground (≥200 tris), 8 Skyline | box (Midground only) |
| Night City Skyline cluster | FBX + 2K maps | 6.1k | 353×148×312 | ×10 scale fix | rebuilt URP Lit with normal + emissive | Skyline | none |
| dasy444 "city at night" | FBX + photo textures | 2k | — | — | — | **Rejected** | — |

Notes:
- Nothing supplied is good enough for a **roadside** row except London and Singapore. The Asian and Skyline packs are baked-light, low-poly backgrounds; they look flat up close (behind ~150 m they work well). A believable driving corridor needs more hero and roadside buildings (a request for the owner).
- **Rejected: dasy444 pack.** Its textures are photographs that appear to come from the web (Pinterest-sized JPEGs, an aerial city photo, a real shop shutter with a phone number). Their provenance cannot be verified, so a licence chain is impossible. It's also a 2k-triangle photo-projection model. The archive is kept in `SourceArchives/` (git-ignored) and was not imported.
- The ×10 skyline scale is inferred from window rows (~35 storeys on a 14.8 m mesh ⇒ ~3.5 m/storey after scaling).
- The 12 skyline towers are one merged mesh; they can only be placed as a cluster.
- The Asian pack's `WINDOW_MASK` textures were kept for the window-lighting shader in the lighting phase.
- `.blend`, `.blend1`, `.obj` and `.mtl` source files were not imported. Unity would try to open the `.blend` files with Blender.

## Known issues carried forward

1. **NaN pixels on smooth/emissive surfaces at runtime** (flat cyan). Mitigated by *Stop NaN* on all scene cameras and by remapping glTFast clearcoat materials. Root cause to be found in the lighting phase using the Rendering Debugger.
2. **No LODs** on any supplied model. Cars need LODs before AI traffic; London (143k tris) needs one before placing more than once.
3. **AMG GT3 materials** need a rebuild (glass, livery).
4. **Car paint has no clear coat** after the remap. Restore it with URP Complex Lit during car look-dev.
