# 21 · Security Gate (alley gate hack)

> [Index](00_INDEX.md) · Previous: [20 Heist System](20_HEIST_SYSTEM.md) · Next: [22 Data Core](22_DATA_CORE.md)

## What it is

The **Service Alley** (x = 160, z −100…60) is the short, risky route into the Data Core compound. A locked sliding gate across it at (160, −20) must be **hacked** from a kiosk on the west pavement. A successful hack opens the gate but logs **+35 % heat**. In a lockdown the gate **slams shut** and can be hacked again for another +35 %.

## Pieces and code

| Piece | Class | File | Built by |
|---|---|---|---|
| Sliding gate panel, countdown, warning, slam | `SecurityBarrier` | `Assets/_NeonRift/Scripts/Gameplay/Mission/SecurityBarrier.cs` | `NightRunBuilder.BuildAlleyGate` → `BuildBarrier("AlleyGate", …)` (`Scripts/Editor/District/NightRunBuilder.cs:427`) |
| Bolts, status lamps, lamp bar, status hologram, lock audio, camera framing | `GateLockSystem` | `Scripts/Gameplay/Mission/GateLockSystem.cs` | `NightRunBuilder.BuildGateLocks` (`NightRunBuilder.Heist.cs:155`) |
| Hack zone (Trigger, 10 × 5 × 46 m) | `Interactable` id `alley_gate_terminal` | `Scripts/Gameplay/Mission/Interactable.cs` | `BuildAlleyGate` |
| Kiosk screens | `TerminalDisplay` | `Scripts/Gameplay/Mission/TerminalDisplay.cs` | `TerminalKiosk` (`NightRunBuilder.Heist.cs:97`) |
| Hack data | `InteractionDefinition` `Interaction_HackGate.asset` | `Assets/_NeonRift/Data/Missions/` | `ConfigureGateHackDefinition` (`NightRunBuilder.Heist.cs:138`) |
| Graph edge | gate id `alley_gate` | `CityLayout.Blockers` | `BuildNavigation` |

## The hack (`Interaction_HackGate`)

`EditorConfigure("HACK", hold 0, maxSpeed 10 km/h, decay 0.6, heatOnComplete 0.35, "GATE BREACH LOGGED", "GATE RELEASED")`. Device "S7 GRID // GATE CONTROLLER". Lockout 5 s, fail heat 0.12 ("GATE CONTROLLER LOCKOUT · TRACE LOGGED"), tension 0.35. Event ids use **`{id}`**, expanded to `alley_gate_terminal` at runtime, so any gate terminal can reuse the definition.

| # | Stage | Kind | Duration | What the world does |
|---|---|---|---|---|
| 1 | CONNECTING | Auto | 0.9 s | `{id}.hack`: lock lamps and the lamp bar go amber and pulse, bolts chatter, the gate status hologram works |
| 2 | AUTHENTICATING | **Hold E** | 1.3 s | "Replaying a maintenance key — hold E"; kiosk screens scroll |
| 3 | BYPASSING SECURITY | **Timing** | 1.2 s / sweep | Window 0.26 at 0.58, **3 misses** allowed, **+5 % heat per miss** (raises Alert), screens tear, denied buzz. Three misses → `{id}.lockout`: lamps flash red, 5 s lockout, +12 % heat |
| 4 | OVERRIDE | Auto | 0.8 s | "Override accepted — controller is ours" |
| 5 | GATE RELEASE | Auto | 1.1 s | `{id}.unlock`: four bolts withdraw into the housing (ratchet + pneumatic sigh), lamps turn green |

On completion the interactable raises **`alley.gate.open`** (the barrier's `openOn`), and the panel slides open. Heat +35 % is applied (`heatOnComplete`).

**Cancelling:** driving off (over 10 km/h, or out of the zone for 0.6 s) drops the link ("CONNECTION LOST"). `{id}.abort` (the definition's cancel event) returns the locks to sealed red; the next attempt starts from CONNECTING.

## Physical gate (`SecurityBarrier`)

Alley gate parameters: width 10 m, one panel, **closed at start**, `closeOn` = `lockdown`, `openOn` = `alley.gate.open`, warning 1 s, travel 1.5 m.

General behaviour (`SecurityBarrier`): closing is **staged** so the player can read it coming: an optional countdown (shortened by heat, shown on the HUD via `MissionWorld.SetCountdown`), then a **warning** phase with strobing lights and a klaxon, then the **kinematic** panels slide (they push cars, never teleport them) and slam. Methods: `Close` (120), `Open` (133), `Update` / `FixedUpdate` (`MovePanels`), `SetWarning`, `Finish`. State is read by `CityNavigation` (`IsOpen`, `IsClosed`, `SecondsUntilClosing`) to re-route.

## Lock presentation (`GateLockSystem`)

| Field / method | Behaviour |
|---|---|
| `hackOn`, `abortOn`, `lockoutOn`, `unlockOn`, `releasedOn`, `sealOn` | Event lists (take the hack's own stage events) |
| Colours | sealed red (2.2, 0.12, 0.2), hacking amber (2.6, 1.2, 0.15), released green (0.2, 2.4, 0.9) |
| `bolts`, `boltSeconds` 0.5 | Bolts slide into / out of the post (`PlaceBolts`) |
| `sealDelay` 2.6 s | After a lockdown slam the bolts re-seat with a latch clunk and the terminal **re-arms** |
| `UpdateCamera` | While hacked or releasing and the car is within `frameRange` 40 m, the chase camera rises and frames the gate, staying near-straight behind the car so it does not swing into the alley walls |

## Lockdown re-hack

The interactable is configured with **re-arm on `lockdown`** (`Interactable.EditorConfigure(..., new[] { EventLockdown }, ...)`). After the slam the hack is available again (+35 % heat). The `BoulevardInAlleyOut` validation scenario uses exactly this escape.

## Validation

`NightRunValidator` scenario `AlleyInFailRetry`: three deliberate misses → lockout (+5 % ×3 and +12 % heat, Alert); a 5 s wait; restart and drive off mid-bypass → CONNECTION LOST and abort; then a full hack → bolts released → gate open (+35 %). `GateTrick` in `Scripts/Gameplay/Mission/NightRunValidator.cs:201` performs the scripted misses. Screenshots: `Docs/Screenshots/Gate/`.
