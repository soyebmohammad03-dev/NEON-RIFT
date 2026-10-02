# Technical decisions

Short records of choices that shape the codebase. Add new entries at the bottom.

### D1 — Rebuild from scratch, recording is flow reference only
The lost prototype's gameplay recording defines mission flow, pacing and UI concepts. Its physics, visuals, lighting and audio are explicitly not the quality bar.

### D2 — Persistent Bootstrap scene + additive content scenes
No `DontDestroyOnLoad`. Long-lived objects live in Bootstrap; content scenes are swapped underneath. This makes ownership explicit and allows background-loading later.

### D3 — Context injection instead of singletons
`GameRoot` is the only composition root. Scenes receive a `GameContext` through `ISceneEntryPoint.Enter`. No static service access, which also matters because **domain reload on entering Play Mode is disabled** in this project — static state would leak between play sessions.

### D4 — ScriptableObject data for content
Vehicles (`VehicleDefinition` + `VehicleCatalog`) and missions (`MissionDefinition`) are assets. Adding a car or a mission is a data change, not a code change. Stable string ids are used for saves and lookups.

### D5 — Generated Input System wrapper
`NeonRiftControls` is generated from the `.inputactions` asset (typed, no string lookups). Maps: `Driving`, `Menu`. Each scene enables only the maps it needs. UI navigation uses the `InputSystemUIInputModule` defaults. Project-wide actions are not used.

### D6 — UI Toolkit for screens
UXML/USS screens with one shared theme (`UI/NeonRift.uss`) and one `PanelSettings` (1920×1080 reference, match 0.5). Text-based assets diff and merge cleanly.

### D7 — Vehicle physics is our own, deferred to the vehicle phase
Raycast-suspension controller with an engine/gearbox model, written in the vehicle phase and validated on the test track. Nothing in Phase 1 pretends to drive. The engine RPM model will also drive the continuous engine audio.

### D8 — 100 Hz fixed timestep
Suspension and tyre forces need a small integration step; 0.01 s is the baseline. Re-evaluate on mobile profiling.

### D9 — Third-party isolation
Vendor packs keep their own import root and are never edited in place. Changes go into `_NeonRift` (prefab variants, copied materials). Building and vehicle models are provided by the project owner; every other external resource must be free.

### D11 — glTFast for GLB, nested source prefabs
Sketchfab GLBs are imported with Unity's free glTFast package. Our prefabs nest the untouched source model and override only transforms and renderer enable states, so re-importing a source updates every prefab. Wheel geometry merged across corners is split into generated meshes owned by `_NeonRift`.

### D12 — Clearcoat materials remapped
glTFast's clearcoat Shader Graph produced NaN (flat cyan) output at runtime. Prefabs use project-owned copies on the plain glTF PBR graph. Car-paint clear coat returns via URP Complex Lit in look-dev.

### D13 — Stop NaN on all cameras
Some smooth/emissive surfaces still produce NaNs at runtime. URP *Stop NaN* is enabled on every scene camera as a safeguard. Finding the source is a lighting-phase task (see ThirdParty.md, known issues).

### D14 — Licence metadata travels with content
`VehicleDefinition` and `BuildingDefinition` carry an `AssetLicense`. Non-commercial assets may be used in development; the validator lists them so a release build can exclude them.

### D10 — Git + Git LFS
Plain Git with LFS rules in `.gitattributes` for binary assets, and UnityYAMLMerge for scenes and prefabs. Unity's `collab-proxy` package stays installed for optional Unity Version Control but is not used as the source of truth.

### D15 — Raycast vehicle with implicit wheel spin
Suspension uses rays along the suspension axis; 5 rays per wheel sample the tyre's rolling profile so sharp edges are rolled onto. Wheel spin is integrated implicitly against tyre stiffness (secant or local slope, whichever is larger), which removes the classic low-speed slip oscillation and keeps braking/traction stable at 100 Hz without sub-stepping. Brake-held wheels use static friction shared across held wheels, so cars park on slopes without creep. No Unity `WheelCollider`: its tyre model can't be made to match the profile data and it hides state that audio and AI need.

### D16 — Physical units in vehicle data
Profiles use real units (kg, Nm, Hz, damping ratio, friction coefficients, C·A). Spring rates derive from natural frequency and corner mass, so changing mass does not detune the ride. Static ride height always equals the modelled ride height (springs are preloaded to match).

### D17 — Deterministic vehicle bench
Vehicle behaviour is verified in an isolated editor physics scene stepped manually (`VehicleTestBench`). Tests run every catalog car through launch, braking, top speed, grip, keyboard step-steer, bumps, wall impact and slope hold in seconds, without entering Play Mode.

### D18 — Chase camera drives a Cinemachine camera directly
`VehicleChaseCamera` computes the pose itself (heading swing toward travel direction, look-ahead, speed FOV, acceleration lag, sphere-cast collision) and writes it to a `CinemachineCamera` before the brain updates. Cinemachine stays the blending layer for future cinematic cameras. The camera never damps body roll or pitch.

### D19 — Layered engine loops driven by telemetry, synthesised in-house
Engine sound is a set of seamless loops at five rpm points × on/off load per engine, crossfaded with equal-power laws and pitched by rpm (`EngineSoundModel`). Every loop is started once; only volume and pitch change afterwards (`VehicleAudio.PlayCalls` proves it). Clips are synthesised by `VehicleAudioGenerator` from a physical model (firing angles, exhaust banks, pipe resonances), so there are no licence questions and recorded loops can replace them later by data only.

### D20 — Mixer authored by tool; snapshots below user volumes
The mixer is built by `AudioMixerBuilder` (reflection on Unity's internal mixer controller, editor only). User volumes are exposed on parent groups; state snapshots (Menu, Results, Lockdown, Ducked) act on leaf groups so the two never conflict.
