# Opening cinematic

> October 2026: the intro was reworked into an 84 s story set around the crew's garage. See **The story** below. The older 62 s shot table further down is kept for history.

Game launch → **intro** (real-time, in the city) → **NEON RIFT / NIGHT RUN** title → Enter / A → the camera pushes into the hero car → **Car Select** → Night Run. Original work throughout: no footage, music, dialogue or characters from other games.

## How it fits the game flow

- `GameState.Intro` and `IGameFlow.PlayIntro()`. On boot, `GameFlow` plays the intro when `IntroSettings.ShouldPlayOnBoot()` says so; otherwise it goes to the normal Title screen, which now has a **WATCH INTRO** button.
- The intro runs **in the Night Run scene** (`GameConfig.introScene`), so it shows the real city: lamps, signals, haze and towers. The 18 MB scene is not duplicated. `GameFlow` enters scenes through the root component that matches the state: `IntroSceneEntry` (an `IIntroEntryPoint`) for the intro, `MissionSceneEntry` for missions. The intro rig is inert during missions: shots, overlay and stage lights are off, and the director doesn't play on awake. This is covered by a test, and spawn-view stats are unchanged.
- The cars in the lineup are the catalog's gameplay prefabs, spawned through the normal `VehicleSpawnPoint` path: same models, physics, lights and engine audio. The hero car (middle) is the session's selected car, or the catalog default. The two rivals drive away with the normal `RacerDriver` AI.
- Nothing persists except the "seen" flag. `Exit()` restores the time scale, removes the lineup and disables the rig. Verified: intro → Car Select → Night Run gives a normal mission (HUD visible, time scale 1, the selected car drives, the others are rivals).

## Settings (`Data/Config/IntroSettings.asset`)

| Field | Default | Meaning |
|---|---|---|
| Policy | FirstLaunchOnly | `Always` (good while authoring), `FirstLaunchOnly` (stored in PlayerPrefs `NeonRift.IntroSeen`), `Never` |
| Skippable | on | Space / Start skips to the title, even on the first launch |
| Mode | Realtime | `Video` + a `VideoClip` plays a pre-rendered intro instead of shots 1–9, then the real-time title takes over. **This path is implemented but untested: there is no rendered clip yet** |

Menu: **Neon Rift ▸ Intro ▸ Reset 'Intro Seen'** shows it again on the next launch.

## Building it

`IntroBuilder` generates the rig and the Timeline from a shot table in code (`Neon Rift ▸ Intro ▸ Build Intro`). It also runs as part of every city rebuild.

- **Timeline** (`Data/Intro/Intro_NightRun.playable`, 62 s):
  - one animation track per shot camera (eased position plus a look direction keyed as a quaternion);
  - a Cinemachine track that cuts and blends between the 18 shot cameras;
  - an `IntroCueTrack` for story beats;
  - six audio tracks.
- **Cues** (`IntroCueKind`): `FadeIn`, `Dip` (soft cut), `Card` (text card), `Title`, `Surveillance` (CCTV overlay and grade), `Headlights`, `Ignition`, `Departure`, `Flash`, `SkipHint`, `Letterbox`. One-shot cues are also applied by Skip, so skipping lands in a consistent world state.
- **World anchors are read from the built city:**
  - the Data Core from the mission director's alert origin;
  - the compound gate by name;
  - the signal shot from the actual signal lens geometry on W Avenue;
  - the rivals' departure goal from the `core_staging` race marker.

### The story (84 s, October 2026 rework)

The intro now tells the premise without exposition: a crew in a hidden garage on W Avenue goes after the Sector 7 Data Core. It ends inside that same garage, which is also the Car Select hall.

| Time (s) | Section | Shots and beats |
|---|---|---|
| 0–9 | Black, then the city | Fade from black to the whole city at night; distant ambience and drone |
| 9–16 | Down through the skyline | A long descent between towers towards W Avenue |
| 16–25 | Street life | Low dolly up W Avenue (shopfronts, steam), then signals and signs on Market Street |
| 25–31 | Security infrastructure | A real street camera (read from the scene), the compound gate. Card: *IN NEON RIFT, INFORMATION IS POWER. / AND THE GRID WATCHES EVERY STREET.* |
| 31–39.5 | The Data Core | The compound from above, the core. Card: *AT ITS HEART: THE SECTOR 7 DATA CORE. / EVERY SECRET IN THE CITY PASSES THROUGH IT.* |
| 39.5–43.5 | Surveillance | CCTV view of an anonymous roller door on W Avenue: *UNUSUAL ACTIVITY · W AVENUE · UNIT 7* |
| 43.5–52.5 | The hidden garage | The door from the street, then inside: the hall's lights strike bank by bank (flicker, clunks), three cars. Card: *TONIGHT, A CREW GOES IN FOR IT. / EXTRACT THE PACKAGE. OUTRUN THE LOCKDOWN.* |
| 52.5–60 | The cars | The hero's headlight comes on, a slide along it, a rival car in its bay |
| 60–64 | Crew preparation | The planning wall: a map generated from the real road data with the route to the core, and a core schematic. Card: *THREE CARS. ONE RUN. / NO SECOND CHANCE.* Ignition |
| 64–71 | Rolling out | The roller door lifts onto the street; the two bay cars drive out and up W Avenue (real physics and engines, `WaypointDriver`) |
| 71–75.2 | The grid notices | Six 0.7 s cuts with flashes: gate, signal, core, a camera (*VEHICLE MOVEMENT · SECTOR 7 PERIMETER*), the avenue, the compound |
| 75.2–84 | Title | Across W Avenue, looking into the open, lit garage where the hero waits on the turntable: **NEON RIFT**, **NIGHT RUN**, then **PRESS ENTER · A** |
| Confirm | Into Car Select | A 4.6 s move through the garage door, round the hero, ending exactly on Car Select's opening frame (same garage, same lens), a short fade, then Car Select opens on that close-up and pulls back |

### The crew garage in the city

The Car Select hall (`GarageBuilder.BuildShell`) now also stands in the city, on the west side of W Avenue, with its roller door facing the avenue (`NightRunBuilder.Garage`, a reserved lot in `CityLayout`). The same building is in both scenes, so leaving the intro for Car Select is a match cut, not a jump to a different place.

- The walls, roof and door collide, and the turntable carries a car.
- It has its own box-projected reflection probe, baked with the hall lit.
- The hall's 19 lights are intro-only. During a mission they are off, so they cost nothing and never leak through the walls. A door lamp and two street lamps outside stay on.
- `CrewGarage` strikes the light banks and lifts the door. `IntroSceneEntry` stands the hero on the turntable and the two rivals in the bays, facing the door.
- **The Night Run now starts just outside the garage door** (spawn (3.5, −238)). Car Select's departure drives the chosen car out of that door, and the mission picks it up on the avenue.

### Validation (Play Mode)

- Fresh boot → the full intro played to the title (84 s timeline, 25 shots). Frames were captured at every section and the problems found were fixed: shots inside buildings, wrongly mirrored bay headings that pinned the rivals beside the door, a dark exterior, and a weak title framing.
- Both bay cars drove out and were 200 m up W Avenue by the title.
- Confirm → push through the door to the headlight close-up → Car Select opened cut, not blended, on the matching close-up, held through the fade and pulled back to the hero shot.
- Car Select: Right Arrow ×2 (SLS AMG → GT3 → Terzo), Enter → the turntable swings to the door, the Terzo drives out, loading → Night Run with the **Terzo** as the player at the new spawn. The SLS AMG (VEX) and GT3 (KADE) took their roles.
- Mission regression from the new spawn: 2× `BoulevardInAlleyOut` completed, 0 collisions, 0 rival contacts.
- A Skip pressed before the Timeline's first evaluation is now deferred a frame. Before this, it was overwritten when the graph was created.

| | | |
|---|---|---|
| ![](Screenshots/Intro2/01_03.0s.jpg) | ![](Screenshots/Intro2/02_12.5s.jpg) | ![](Screenshots/Intro2/03_22.5s.jpg) |
| ![](Screenshots/Intro2/04_26.5s.jpg) | ![](Screenshots/Intro2/05_37.0s.jpg) | ![](Screenshots/Intro2/06_41.0s.jpg) |
| ![](Screenshots/Intro2/07_48.5s.jpg) | ![](Screenshots/Intro2/08_53.5s.jpg) | ![](Screenshots/Intro2/09_58.5s.jpg) |
| ![](Screenshots/Intro2/10_61.0s.jpg) | ![](Screenshots/Intro2/11_67.0s.jpg) | ![](Screenshots/Intro2/12_68.5s.jpg) |
| ![](Screenshots/Intro2/13_80.5s.jpg) | ![](Screenshots/Intro2/14_match_00_4.3s.jpg) (end of the push-in) | ![](Screenshots/Intro2/15_match_05_7.0s.jpg) (Car Select, opening) |

**Known limits:**
- The street-level shots have no traffic or people yet (city life is a later phase).
- The garage exterior is a plain box.
- The audio balance has not been judged by ear.

## Audio

Original and procedural (`IntroAudioGenerator`, `Audio/Generated/Intro`):

- **Sound effects:** riser, impact, whoosh, scanner beeps, ignition.
- **Music:** the game's own theme from `ScoreComposer` (see Audio.md): `Intro_Bed` (pad, from 0.5 s to the title) and `Intro_Groove` (pulse and arpeggio, from 62.6 s). The groove's `clipIn` puts it on the bed's bar and chord.
- **Ambience:** the existing city ambience.
- **Engines:** the cars' own engine audio starts at the ignition cue.

Routed through the mixer: ambience → Ambience, bed/groove/riser/hits/scan → Music, ignition → Sfx. The soundtrack sources have priority 0: the garage line-up's engine loops fill the voice limit.

## Validation (Play Mode, October 2026)

| Check | Result |
|---|---|
| First launch from Bootstrap | Intro played automatically and held on the title at t = 57.5 s |
| Enter at the title | `[Intro] confirm: pushing in to the hero car` → Car Select; "seen" flag set |
| Car Select → Night Run after the intro | Right Arrow → SLS GT3, Enter → Night Run. The GT3 is the player, the SLS AMG and Terzo are rivals, HUD visible, time scale 1, keyboard driving script passes (W, D, Space, arrows) |
| Second launch | Booted to the normal Title (intro already seen); WATCH INTRO replayed it to the title |
| Skip | Space pressed at t = 14.4 s → jumped to the title shot, rivals gone, time scale 1, title reached |
| Tests | `IntroTests`: policy logic, video fallback, config wiring, scene/Timeline wiring and inertness |

Frames from a real playback:

| | |
|---|---|
| ![](Screenshots/Intro/intro_02_10.0s.jpg) | ![](Screenshots/Intro/intro_03_16.0s.jpg) |
| ![](Screenshots/Intro/intro_04_22.5s.jpg) | ![](Screenshots/Intro/intro_06_29.0s.jpg) |
| ![](Screenshots/Intro/intro_07_33.0s.jpg) | ![](Screenshots/Intro/intro_09_44.0s.jpg) |
| ![](Screenshots/Intro/intro_10_46.9s.jpg) | ![](Screenshots/Intro/intro_01_50.6s.jpg) |
| ![](Screenshots/Intro/intro_02_51.4s.jpg) | ![](Screenshots/Intro/title_57.5s.jpg) |

Not yet verified by ear: the mix balance of the intro audio. The clips are generated and bound, and the playback ran, but levels have not been listened to on speakers.

## Car Select garage (October 2026)

Car Select is now a generated crew garage (`GarageBuilder`, **Neon Rift ▸ Car Select ▸ Build Garage**):

- **The hall:** polished concrete, steel columns and trusses, soft-box strips, wall washers and tube fittings, a workbench, tool chests, a tyre rack and a planning wall of screens.
- **The cars:** the selected car sits on a turntable under a key light and two rims. The other two catalog cars are parked in the background bays as silhouettes.
- **The roller door** opens onto a lit street.
- **Data:** each car has a category, its drivetrain (from the physics profile) and a measured 100–0 km/h braking distance (`UpdateDisplayStats`).
- **Switching cars:** the stage dims, the new car comes up with its headlights, a brake-light pulse and an engine blip. `ShowroomEngine` plays the car's own recorded loops, and the camera blends to a low side shot and back.
- **Confirm:** the turntable swings the car to the door, the door lifts, the engine revs, the brakes release and the car pulls out under a follow camera. Then a fade, and the Night Run loads.
- **From the intro,** Car Select opens on a headlight close-up and pulls back to the hero shot (`RunSession.ArrivedFromIntro`). This is wired but not yet verified end to end.

Verified in Play Mode:
- switching SLS AMG → SLS GT3 → Terzo;
- departure frames captured on the game clock (`PlaytestCapture`);
- confirming with the Terzo loaded the Night Run with the Terzo as the player;
- showroom engine idle at 800 rpm on the Engine group, and a blip to 4,640 rpm crossfading to the 5000 rpm on-load loop.

Not checked by ear.

| SLS AMG | SLS GT3 | Terzo | Departure |
|---|---|---|---|
| ![](Screenshots/CarSelect/cs9.jpg) | ![](Screenshots/CarSelect/cs7_gt3.jpg) | ![](Screenshots/CarSelect/cs10_terzo.jpg) | ![](Screenshots/CarSelect/departure.jpg) |

## Car Select premium pass (October 2026)

- **Composition:** the hero camera is closer and lower (27° lens). The Cinemachine composer places the car a little left of centre, so it fills the frame between the name block and a slimmer specs panel instead of sitting behind it.
- **Switching:**
  - The outgoing car loses its lights while the stage drops almost to black and the turntable whips it away, accelerating to 720°/s.
  - The incoming car spins in from 150° off and eases into its pose as the stage lights return.
  - Its DRLs come on, then the headlight beams, with a brake-light pulse and an engine blip from its own recordings.
- **UI:** stat bars ease to their new length and the numbers count from the previous car's values to the new ones over 0.5 s, for example 4.2 s → 3.7 s and 299 → 278 km/h. The info block slides out and back in around the change.
- **Unchanged:** category, drivetrain, power, weight, measured 0–100, top speed, handling and 100–0 braking, the description, the opening close-up after the intro, and the departure through the roller door with the follow camera.

| Hero framing | Switching SLS AMG → SLS AMG GT3 (frames at 0.1–2.2 s) |
|---|---|
| ![](Screenshots/CarSelect2/hero.jpg) | ![](Screenshots/CarSelect2/switch_sequence.jpg) |
