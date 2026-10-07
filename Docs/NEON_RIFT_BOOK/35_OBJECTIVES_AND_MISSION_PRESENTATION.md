# 35 · Objectives and Mission Presentation

> [Index](00_INDEX.md) · Previous: [34 Minimap](34_MINIMAP.md) · Next: [36 Results and Debrief](36_RESULTS_AND_DEBRIEF.md)

## The operation briefing card

| | |
|---|---|
| **When** | First frame of the mission, once the rival crews are on the grid (`briefingPending` in `MissionDirector.Begin`) |
| **Built by** | `MissionDirector.BuildBriefing` (`Assets/_NeonRift/Scripts/Gameplay/Mission/MissionDirector.cs:631`) → `MissionHud.Briefing` |
| **Shown by** | `MissionHud.ShowBriefing` (504); rows reveal one by one; the objective panel and minimap step aside while it is up |
| **Contents** | Kicker "OPERATION · SECTOR 7", the mission name, tagline ("INFILTRATE SECTOR 7 · STEAL THE DATA CORE · GET OUT") and briefing text from `MissionDefinition`; the four objective titles; the crew list (YOU · car = DRIVER, then each rival with `MissionDefinition.CrewRoleFor(i)`: "SPIRE RELAY · OWN JOB", "KOWLOON RELAY · OWN JOB"); personal best and best grade (`MissionRecords`); "DRIVE TO BEGIN" |
| **Leaves** | After `briefingSeconds` (9 s), or once the player passes `briefingExitKph` (60 km/h) after 2.5 s (`UpdateBriefing`, 611) |

## Objective display

| Moment | What happens | Code |
|---|---|---|
| Objective starts | Title, detail and "n/4" slide in; the target's beacon shows; the waypoint and minimap target update; objective cue sound (from objective 2 on) | `MissionDirector.OnObjectiveStarted` → `MissionHud.SetObjective`; `IMissionTarget.SetObjectiveActive` |
| Distance | Route distance to the objective | `MissionHud.SetObjectiveDistance` |
| Completion | Stamp "OBJECTIVE COMPLETE" with the **split time** (e.g. `0:48.20`), then the next objective slides in | `OnObjectiveCompleted` → `MissionHud.StampObjective` |
| Timed objective | Trace timer; heat removes time | `MissionHud.SetTimer` |
| Announcements | Data-driven HUD lines and banners per world event (`MissionAnnouncement`: event id, text, tone, delay, banner) | `MissionDirector.OnWorldEvent` → `RunAnnouncements` → `Toast` / `Banner` |

## Message tones (`Scripts/Missions/MessageTone.cs`)

`Info`, `Warning`, `Danger`, `Success`: the colour of toasts and banners.

## Key Night Run announcements (from `NightRunBuilder.CreateMissionData`)

| Event | Text |
|---|---|
| `mission.start` (+4.5 s) | SECTOR 7 GRID ONLINE · FOLLOW THE CYAN BEACON |
| `mission.start` (+9 s) | RIVAL CREWS RUNNING THEIR OWN JOBS · FIRST TWO OUT QUALIFY |
| `alley.gate.open` | ALLEY GATE OPEN |
| `core.arrive` | FACILITY SECURITY ACTIVE · INTRUDER SCAN RUNNING |
| `core.security.off` | LOCAL SECURITY OFFLINE · CORE CHAMBER OPENING |
| `core.extract.begin` | EXTRACTION RUNNING · HOLD POSITION |
| `grid.anomaly` | S7 GRID: ANOMALOUS DATA FLOW · NODES REROUTING |
| `core.extract.trace` | COUNTER-INTRUSION ONLINE · CAMERAS REBOOTING |
| `core.acquired` | **banner** DATA ACQUIRED / S7 CORE PACKAGE · 2.4 TB SECURED |
| `core.breached` | **banner** SECURITY BREACH DETECTED / SECTOR 7 LOCKDOWN INITIATED; then CAMERAS ARMED · STAY OUT OF SIGHT |
| `escape.start` | EXTRACTION: RIFT GATE, SOUTH-EAST TUNNEL · CREW: SKYWAY GATE OPEN · HARBOR ROUTE CLEAR · ALLEY GATE CAN BE RE-HACKED · COSTS HEAT |

Rival radio lines ("VEX: SPIRE RELAY CRACKED", "KADE HAS A CORE PACKAGE · RUNNING FOR THE GATE", "VEX EXTRACTED", qualifying warnings) come from `RivalDirector` through `MissionWorld.Announce`.

## Presentation beats

| Beat | Code |
|---|---|
| Data Core framing camera | `DataCoreChamber.UpdateCamera` |
| Theft beat (camera swing, letterbox, slow motion 0.45 for 2.6 s) | `MissionDirector.StartBeat` / `UpdateBeat` / `EndBeat` |
| Lockdown frame, magenta route, red gates | `MissionHud.SetSecurity`, `CityMinimap` |
| End banner (EXTRACTED / TRACED), then the debrief after 2 s | `OnPhaseChanged` → `UpdateResults` |

Screenshots: `Docs/Screenshots/Presentation/briefing.png`, `debrief.png`.
