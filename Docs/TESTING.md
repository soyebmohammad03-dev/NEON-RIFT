# Testing

All tests run **inside the Unity editor on macOS**. There is no Unity CI (a Unity licence would have to be stored as
a GitHub secret); GitHub Actions runs only the repository checks described at the end.

**What the automated runs are and are not.** The playtests and validators drive the real game through its normal
paths (the same input interfaces, flow transitions and UI handlers a player uses) and take Game-view screenshots.
Night Run is completed by the project's route autopilot (`NightRunValidator`, scenario `BoulevardInAlleyOut` with the
SLS AMG) and Harbor Rain by its review autopilot, which teleports between stages and solves the puzzles
programmatically. They are not human play sessions and not device tests.

## EditMode tests

`Neon Rift ▸ Tests ▸ Run EditMode Tests (write report)` → `Logs/TestResults-EditMode.txt`
(or *Window ▸ General ▸ Test Runner ▸ EditMode*).

| Assembly | Covers |
|---|---|
| `NeonRift.Tests.EditMode` (`Scripts/Tests/EditMode`) | foundation and project wiring, vehicle physics bench, audio models, missions and records, intro, input, city, rivals |
| `NeonRift.Campaign.Tests` (`Levels/Campaign/Tests`, 17 tests) | save round trip, fresh / invalid / future / partial saves, qualification rule, Level 1 / Level 2 lock rules, the five-car selections, standalone catalog untouched, campaign config and bootstrap wiring, scene list and APK scene list, every campaign line subtitled from the same text as its audio, every Harbor Rain radio line has subtitle text (and audio when present), subtitle setting and clearing, teaser and portal assets |

In a public checkout three campaign tests report **Ignored** with a reason, because the two Harbor Rain cars and the
voice recordings are local-only; the text and wiring parts still run.

## Play Mode playtests

`Neon Rift ▸ Campaign ▸ Playtest: …` (`Levels/Campaign/Editor/CampaignPlaytest.cs`). Each enters Play Mode from the
relevant bootstrap, writes `Logs/Campaign/playtest_<scenario>.txt` and screenshots in `Logs/Campaign/shots/`.
*Full Campaign* resets the campaign save first.

| Playtest | Checks |
|---|---|
| Full Campaign | opening plays; skip lands on the teaser; teaser subtitled, played once, cleared; Level 1 selection has five cars, the Harbor Rain cars locked and unstartable; Night Run uses the chosen car; autopilot completes and qualifies (up to 3 attempts; no portal unless qualified); save records it; portal: the car physically crosses the plane; arrival; save: portal + unlock; unlock card; Level 2: each car available; trailer: all shots, narration subtitled; operation starts with the chosen car; Harbor Rain extracted; save: completed; campaign results then choices; the save reloads intact; no stale subtitle or card on the title |
| Skip Trailer + Subtitles | development entry → Level 2 selection; trailer skip starts the same operation with the same car and clears subtitles; restart keeps the car; radio lines reach the subtitle log; subtitles off / on |
| Original Night Run Bootstrap | no campaign; no teaser; three cars; Night Run starts; no portal offered |
| Saved Progress After Restart | progress survives a restart; the title offers Harbor Rain; Harbor Rain cars still Level-2-only; title → Level 2 selection |
| Without Harbor Rain Cars | simulates a public checkout in memory: unlock card, three-car Level 2 selection, Harbor Rain starts with the chosen car |

## Validators

| Tool | Menu | Output |
|---|---|---|
| Harbor Rain validator (whole level: garage, puzzles, crane, alarm, police, floods, waves, extraction, restart, audio, frame time) | `Harbor Rain ▸ Validate (Play Mode)` | `Logs/HarborRainValidation.txt` |
| Project validator (configs, catalogs, scene list) | `Validate Project` | console |
| Night Run mission series | `NightRunValidator` / `MissionSeries` (see [NEON_RIFT_BOOK/40](NEON_RIFT_BOOK/40_TESTING_AND_VALIDATION.md)) | `Logs/Playtest/` |

## Results

Owner's machine, Apple silicon, Unity 6000.6.4f1, editor Play Mode. Logs are local (`Logs/` is not committed).

| Run | Date | Result |
|---|---|---|
| EditMode tests, after the release changes | 2026-10-18 | **142 passed, 0 failed** (local checkout with all assets) |
| Playtest: Without Harbor Rain Cars | 2026-10-18 | 4/4, 0 console errors |
| Playtest: Full Campaign, final (after the last gameplay change) | 2026-10-17 | 39/39, 0 console errors; Night Run P2, qualified, grade B; the car physically drove through the portal; Harbor Rain extracted, grade S |
| Playtest: Full Campaign, three earlier runs | 2026-10-17 | 39/39 each (one run had 9 console errors from the portal particles, fixed before the final run) |
| Playtest: Skip Trailer + Subtitles | 2026-10-17 | 14/14 |
| Playtest: Original Night Run Bootstrap | 2026-10-17 | 6/6 |
| Playtest: Saved Progress After Restart | 2026-10-17 | 4/4 |
| Harbor Rain validator, 3 consecutive runs after the crane fix | 2026-10-17 | all passed, 0 console errors; run 2 deliberately throttled to 8 fps (frames up to 203 ms) |
| Campaign APK build (release) | 2026-10-17 | succeeded, 0 errors (see [BUILDING.md](BUILDING.md#last-build-owners-machine-2026-10-17)) |

The crane fix in those validator runs: the gantry crane used to stall at long frame times (its axis controller
limit-cycled around the target, so a safety net forced it to "locked" without descending). It now simulates in
fixed ≤ 1/60 s sub-steps, and the validator records every step the crane enters (an event), not one sample per frame.

**Not verified:** a physical Android device or emulator, human play-throughs, the sound mix by ear, and a fresh
clone of the public repository opened in Unity (the public-checkout case was simulated in memory).

## Repository checks (GitHub Actions)

`.github/workflows/repository-checks.yml` runs `.github/scripts/repo_checks.py` on every push and pull request. It
needs no Unity licence and does not download LFS objects. It checks that:

- every relative link and image in the Markdown documentation resolves to a file in the repository;
- no tracked file exceeds GitHub's 100 MB limit;
- the binary types routed to Git LFS (models, textures, audio, archives) are committed as LFS pointers;
- the local-only assets (Harbor Rain cars, work boat, voice recordings) are not tracked;
- every first-level folder under `Assets/ThirdParty/<Source>/` carries an `ATTRIBUTION.txt`.
