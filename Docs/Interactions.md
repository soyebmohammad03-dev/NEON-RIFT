# Interactions (multi-stage)

Terminals, uplinks, gates and any other "stop the car and work the device" mechanic share one framework. Missions use it through data. No mission-specific code is involved.

| Piece | Assembly | Role |
|---|---|---|
| `InteractionStep` | Missions | One stage: label, detail, kind, duration, checkpoint flag, Timing window/misses/heat, Sustain interference points/response/rollback, world events on start and complete, progress cues |
| `InteractionRun` | Missions | Pure C# runner, driven by `Tick(dt, held, engaged)`. Unit-tested (`InteractionTests`) |
| `InteractionDefinition` | Gameplay | Asset: verb, device name, speed limit, stages, lockout, fail heat and message, tension level. With no stages it is a single Hold of `holdSeconds` |
| `Interactable` | Gameplay | Scene object (trigger zone): availability by objective and world events, runs the stages, raises events and heat, locks out on failure, reports `Feedback` |
| `TerminalDisplay` | Gameplay | The device's physical screens (`NeonRift/TerminalScreen`), light and 3D blips |
| `TerminalReadout` | Gameplay | HUD panel: stage list, active stage widget, data stream, overall progress |

## Stage kinds

| Kind | Player does | Notes |
|---|---|---|
| `Auto` | Stays stopped in the zone | Machinery and handshakes |
| `Hold` | Holds **E** | Releasing bleeds progress (`decayPerSecond`) |
| `Timing` | Presses **E** while the sweeping cursor is inside the window | A miss adds `missHeat`, moves the window and counts toward `maxMisses` (0 = unlimited). Reaching the limit fails the interaction: lockout plus fail heat |
| `Sustain` | Stays stopped; presses **E** when interference hits | Interference stalls progress for `responseSeconds`. Unanswered, it rolls back `rollback` of the stage. It never fails |

Engaged means inside the trigger and slower than the definition's `maxSpeedKph`. Leaving or moving for more than 0.6 s cancels the run back to the last `checkpoint` stage reached. The press that starts a run never counts as a Timing answer.

Stages raise world events (`eventOnStart`, `eventOnComplete`, and `cues` at progress points). Barriers, lights, cameras, screens, signals and machinery react to those ids, exactly as they react to mission events.

## Presentation

- **HUD:** `TerminalReadout` shows the device header, link state (STANDBY, LINK ACTIVE, SIGNAL WEAK, LOCKOUT, COMPLETE), numbered stages (OK / ·· / --), and the active stage with its own widget:
  - Hold: a bar.
  - Timing: a track with the window and cursor, which turns green while the cursor is inside.
  - Sustain: an amber interference alarm with a re-sync countdown.
  - Every stage also shows a hex data stream, an instruction line and overall percent.

  The panel flashes green on stage completion, red on a miss, and amber on interference. During Sustain stages the screen edge glows (cyan, or amber under interference).
- **World:** `TerminalDisplay` drives screens through a MaterialPropertyBlock: stage strip, scrolling rows, progress bar and scan line. Its colour follows state (standby cyan, working, alarm red, complete green, lockout). Screens tear on misses and interference.
- **Audio:** stage start and complete blips, denied, interference, re-sync, lockout and cancel cues (`MissionAudioSet`). The tension bed builds with the definition's `tension` × progress. Event stingers (`core.acquired`, `core.breached`) are data on the audio set.

## Adding a device

1. Create an `InteractionDefinition` with stages (or build one in code like `NightRunBuilder.CoreTerminalDefinition`).
2. Put an `Interactable` on a trigger collider (Trigger layer). Give it an id, its completion events, and enable/disable/re-arm events.
3. Optionally add a `TerminalDisplay` with screens using `District_TerminalScreen` (opaque) or `District_TerminalHolo` (additive).
4. Point an objective at it (`ObjectiveKind.Interact`), or leave it as an optional device that only raises world events.
