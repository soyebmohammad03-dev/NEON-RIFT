# Controls

Bindings live in `Assets/_NeonRift/Settings/Input/NeonRiftControls.inputactions` (Driving and Menu maps). Harbor Rain
adds two of its own actions in code (device and garage) without changing the shared asset. Touch controls feed a
virtual gamepad, so they use the same bindings.

## Driving

| Action | Keyboard | Gamepad | Touch |
|---|---|---|---|
| Throttle | W / ↑ | Right trigger | GAS |
| Brake (hold at a stop to reverse) | S / ↓ | Left trigger | BRAKE |
| Steer | A D / ← → | Left stick | ◀ ▶ arrows, or tilt (Settings ▸ Steering) |
| Handbrake | Space | B / Circle | HANDBRAKE |
| Interact: hack, extract, open a puzzle, confirm in puzzles | E | A / Cross | HACK |
| Recover (right the car) | R | Select | RECOVER |
| Pause menu | Esc | Start | ❚❚ |
| Music on / off (anywhere) | M | Y / Triangle | MUSIC |
| Telemetry overlay (development builds) | F3 | — | — |

**Harbor Rain extras**

| Action | Keyboard | Gamepad |
|---|---|---|
| Device / message log | Tab | Y (also toggles music) |
| Garage — standalone Harbor Rain only (after a run or while paused) | G | X / Square |
| Puzzles | steer to move left/right, throttle/brake for up/down, Interact to confirm, Handbrake to cancel | same |

## Menus and cinematics

| Action | Keyboard | Gamepad | Touch |
|---|---|---|---|
| Navigate | ↑ ↓ / W S, ← → / Q E | D-pad / left stick, shoulders | tap |
| Confirm | Enter | A | tap (START on the title) |
| Back | Esc | B | tap BACK |
| Skip the opening / teaser / portal / trailer | Space | Start | SKIP (opening) or ❚❚ (in a level) |
| Continue from a campaign card | Enter or E | A | tap anywhere |
| Car Select: rotate the car | drag, right stick | right stick | drag |

## Settings

Title ▸ OPTIONS and the pause menu share the Settings page: master, music, effects and interface volume, music
on/off, **subtitles** (on by default), graphics quality, fullscreen, V-sync, and on touch devices steering mode
(touch / tilt), tilt sensitivity and tilt calibration. Settings are saved between sessions.

The touch layer and tilt steering were built for Android but have **not been tested on a physical device**.
