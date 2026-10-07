# 36 · Results and Debrief

> [Index](00_INDEX.md) · Previous: [35 Mission Presentation](35_OBJECTIVES_AND_MISSION_PRESENTATION.md) · Next: [37 Animation Systems](37_ANIMATION_SYSTEMS.md)

## When it appears

`MissionProgress.PhaseChanged` → Completed or Failed → `MissionDirector.OnPhaseChanged` (`Assets/_NeonRift/Scripts/Gameplay/Mission/MissionDirector.cs:357`):

1. `FinishPosition = rivals.FinishPosition()` (rivals already extracted + 1).
2. `RunSession.RecordResult(Progress.ToResult())` (`RunResult`: outcome, time, security).
3. Mixer → **Results** snapshot; the score outro plays (success or failure), or a plain cue.
4. Banner EXTRACTED / TRACED; the player car is parked (`ParkPlayer`).
5. After `resultsDelay` (2 s): `BuildDebrief` → `MissionHud.ShowResults` (560). The driving HUD fades out behind it.

## The debrief (`MissionDirector.BuildDebrief`, line 656)

| Section | Content |
|---|---|
| Header | DEBRIEF · NIGHT RUN; **MISSION COMPLETE** / **MISSION FAILED**; reason: "EXTRACTION CONFIRMED · DATA CORE SECURED · QUALIFIED", or "DATA CORE SECURED · NOT QUALIFIED · ONLY THE FIRST 2 OUT ADVANCE", or the fail reason |
| Grade badge | Letter S/A/B/C/D and points (completed runs only) |
| RUN | TIME, POSITION n/3, QUALIFYING (TOP 2 · QUALIFIED / NOT QUALIFIED), HEAT %, SECURITY, HARD CONTACTS, OBJECTIVES n/4 |
| SPLITS | Cumulative time per objective (`--` for ones not reached) |
| CREWS | Finishing order: crews that extracted first (QUALIFIED if in the first two, else EXTRACTED), the player (QUALIFIED / NOT QUALIFIED), crews still running (STILL RUNNING); "-- YOU · TRACED" on a fail |
| Best | FIRST CLEAR · PERSONAL BEST SET, NEW PERSONAL BEST · x FASTER, or PERSONAL BEST m:ss.cc · +gap |
| Buttons | **RETRY (E)** → `Retry` → `Flow.StartMission()`; **GARAGE (Esc)** → `Continue` → `Flow.GoToCarSelect()` |

Rows count in, then the grade lands (USS animation). A log line records it, e.g. `[Mission] debrief: P1 qualified, grade A (77: pace 33, quiet 12, position 20, clean 13) …`.

## The grade (`MissionGrade`, `Assets/_NeonRift/Scripts/Missions/MissionGrade.cs`)

Pure logic, out of **100**:

| Part | Max | Rule (`Evaluate`) |
|---|---|---|
| Pace | 40 | Full at or under par (`ParTime`, Night Run **115 s**), zero at 1.6 × par (184 s), linear between |
| Quiet | 25 | 25 × (1 − final heat) |
| Position | 20 | (field − position) / (field − 1): 1st = 20, last = 0; full marks with no race |
| Clean | 15 | 15 × (1 − contacts / 6): 0 at 6 hard contacts (a hard contact is a collision at ≥ 4 m/s, `contactSpeed`) |

Letters (`LetterFor`): **S ≥ 90, A ≥ 75, B ≥ 60, C ≥ 45, otherwise D**. A failed run gets no grade.

Worked example (from a real run): 3:20 time → pace 0; heat 73 % → quiet 7; 2nd of 3 → position 10; 12 contacts → clean 0; total 17 = **D**.

## Records (`MissionRecords`, `Scripts/Missions/MissionRecords.cs`)

PlayerPrefs keys `NeonRift.Best.<missionId>.time`, `.grade`, `.qualified`. Only completed runs count; a slower run can still raise the best grade (`Submit`). `MarkQualified` / `HasQualified` record qualifying. `FormatTime` gives m:ss.cc, rounded to centiseconds first.

## Tests

`MissionTests.Grade_RewardsPaceQuietPositionAndCleanDriving`, `Grade_LettersFollowTheScoreBands`, `Records_KeepTheBestTimeAndScore`, `Records_FormatTimeRoundsToCentiseconds`.
