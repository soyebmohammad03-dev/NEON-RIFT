# 41 · Input Playtesting

> [Index](00_INDEX.md) · Previous: [40 Testing and Validation](40_TESTING_AND_VALIDATION.md) · Next: [42 Build and Release](42_BUILD_AND_RELEASE.md)

## Why it exists

In October 2026 "WASD does nothing" turned out to be the editor's Input System backend having **lost all devices** (`InputSystem.devices.Count == 0`), not a code bug. `InputPlaytest` was written to drive the **real keyboard device** from tooling, so the full input path is exercised: device → action maps → enable/disable lifecycle → `PlayerDrivingInput` → `VehicleController`.

## `InputPlaytest` (`Assets/_NeonRift/Scripts/Editor/Validation/InputPlaytest.cs`)

| Member | What it does |
|---|---|
| `Hold(keys…)`, `Release(keys…)`, `ReleaseAll()`, `Tap(key)`, `Push(gamepad values)` | Write **state events** into the real `Keyboard` / `Gamepad` devices (the same path as physical presses) |
| `Step` | One timed step: hold exactly these keys (and gamepad values) for a duration, then log a snapshot |
| `RunStandardScript` | Drives W, D, Space, arrows, etc., and logs vehicle state per step |
| `RunMinimapScript` | Straight, left, right, sustained left: logs headings and captures the minimap |
| `Run`, `Tick`, `Advance`, `Finish`, `Snapshot`, `InteractState` | Sequencer on the editor loop; writes **`Logs/InputPlaytest.txt`** |

Requirement: the Game view (the Unity app) must be in front, or Unity's background behaviour resets the keyboard.

## Recorded uses

- Driving script (README): W, D, Space and the arrows move the car.
- Pause menu (`Docs/NightRun.md`): Esc paused (time scale 0, listener paused); ↓↓↓ Enter opened Settings; M switched music off (mixer −80 dB, setting saved, HUD hint amber); Esc ↑ Enter opened Controls; Esc Esc resumed (time scale 1).
- Minimap: headings 0° → 250° → 267° → 128°; W Avenue stayed vertical on the map.

## Related

- Unit tests of bindings: `InputTests` ([40](40_TESTING_AND_VALIDATION.md)).
- Device diagnostics at mission start: `PlayerDrivingInput.DescribeDevices` logs `[Input] Driving controls enabled. Devices: …` ([16](16_PLAYER_INPUT.md)).
