# The connected campaign: Night Run → Harbor Rain

Neon Rift ships two ways from the same project:

| Entry scene | What it runs | Config |
|---|---|---|
| `Assets/_NeonRift/Scenes/Bootstrap.unity` | **The original standalone game**: opening, title, three-car selection, Night Run. Unchanged by the campaign. | `Data/Config/GameConfig.asset` (no campaign) |
| `Assets/_NeonRift/Levels/Campaign/Scenes/CampaignBootstrap.unity` | **The connected campaign**: Night Run, then the portal to Harbor Rain. | `Levels/Campaign/Data/GameConfig_Campaign.asset` (+ `CampaignDefinition.asset`) |

`CampaignBootstrap` is a copy of `Bootstrap` whose `GameRoot` points at the campaign config. Scenes ask
`context.Campaign`; in the standalone bootstrap it is `null` and every scene behaves exactly as before.
`BootstrapLoader` recognises both bootstraps, so pressing Play in either never loads the other.

## Flow

```
CampaignBootstrap
  └─ Night Run opening (first launch; WATCH INTRO replays it)
       └─ Harbor Rain teaser: intercepted transmission before the title (Space / Start skips it)
  └─ Title ▸ START CAMPAIGN / PLAY NIGHT RUN
       └─ Level 1 car selection: all cars, the Harbor Rain cars shown LOCKED
            └─ NIGHT RUN
                 └─ Debrief. Success AND top-two finish (Night Run's existing qualifying rule)?
                      ├─ no  → RETRY / GARAGE as usual (never a portal on failure, retry, pause or quit)
                      └─ yes → ENTER THE RIFT ▸
                           └─ Portal sequence on the ENTRY ROAD: result card, the rift opens, the car drives in
                                └─ HARBOR RAIN arrival: the car bursts out of a rift at the south perimeter,
                                   the camera rises over the port
                                     └─ Unlock card: "OPERATION NIGHT RUN: COMPLETE", the real result,
                                        "NEW OPERATION UNLOCKED: HARBOR RAIN", Harbor Rain cars unlocked
                                          └─ Level 2 car selection: every present car available
                                               └─ Harbor Rain trailer (7 narrated shots + briefing; skippable)
                                                    └─ HARBOR RAIN mission
                                                         └─ Debrief ▸ CONTINUE ▸ Campaign results
                                                              ▸ Replay Harbor Rain / Change car / Play Night Run / Title
```

Once Night Run has been qualified, the title also offers **CONTINUE · HARBOR RAIN** (or **PLAY HARBOR RAIN** after
completion), which goes straight to the Level 2 selection.

## Unlock rules

| Car | Level 1 selection | Level 2 selection |
|---|---|---|
| SLS AMG, SLS AMG GT3, Terzo Millennio (Night Run cars) | available | available |
| Harbor Rain cars (F40 LB-WORKS, 296 Speciale A) | always **LOCKED** ("Harbor Rain car") | locked until Night Run is qualified, then available |

Rival crews in Night Run are always the Night Run cars. Unlocks are written when the unlock card is shown.

The two Harbor Rain cars are **local-only third-party models** (not in the public repository; see
[ThirdParty.md](ThirdParty.md)). In a checkout without them, their catalog slots are simply skipped: the selections
show the cars that are present, the unlock card omits the "cars unlocked" line, and everything else is unchanged.
This case has its own playtest (`Playtest: Without Harbor Rain Cars`).

## Saved progress

`PlayerPrefs` key `NeonRift.Campaign.Save`: one versioned JSON record (`Scripts/Game/Campaign/CampaignProgress.cs`),
separate from every standalone key, so the campaign never changes the standalone game's records.

| Field | Set when |
|---|---|
| `nightRunCompleted`, `qualifiedForHarbor`, `level1Vehicle`, Night Run grade / time / position | the Night Run debrief appears |
| `portalCompleted` | the Harbor Rain arrival starts |
| `harborVehiclesUnlocked` | the unlock card appears (requires qualification) |
| `level2Vehicle` | a car is confirmed in the Level 2 selection |
| `harborCompleted`, Harbor Rain grade / time | extraction |

Missing data starts a fresh campaign; corrupt or newer-version data is logged and replaced by a fresh campaign; a
partial record is made consistent (a later flag implies its prerequisites). **Title ▸ Options ▸ Reset campaign
progress** clears it. The opening's "seen" flag is separate (`NeonRift.Campaign.IntroSeen`).

**Development shortcuts** (editor and development builds only; `Debug.isDebugBuild` is false in release APKs): on
the campaign title, **F9** opens the Level 2 selection and **F8** plays the portal arrival, both marking the save as
qualified with grade `DEV`.

## Spoken lines and subtitles

Every spoken line is one record — speaker, text, audio clip — so a line's audio and its subtitle come from the same
text and cannot drift:

- **Campaign lines** (`CampaignDefinition.Lines`, 13): the teaser transmission, THE CONTACT (portal, unlock, trailer
  narration), CONTROL (arrival, campaign complete). Generated from the text in `CampaignBuilder.Lines`.
- **Harbor Rain radio** (`HarborRadio`, 53 lines: CONTROL, OVERWATCH, PORT SECURITY, PORT POLICE), generated from
  `HarborRainAudio.Lines`.
- **Night Run** has no spoken dialogue: its messages are text only and are not subtitles.

Display: campaign lines use the campaign overlay's subtitle box (speaker + text, bottom centre, moved below any
centre card); radio lines use Harbor Rain's radio card. Both report to one `Subtitles` channel and follow one
setting, **Settings ▸ Subtitles** (on by default). Subtitles are cleared on every skip and every scene change.

The audio was generated with **macOS System Voices** (`say`), which Apple licenses for personal, non-commercial use
only and not for publishing. The recordings are therefore **not in the public repository**. Without them every line
still plays as a subtitle (timed from its length); on a Mac, `Neon Rift ▸ Campaign ▸ Build Campaign` regenerates
the campaign lines and `Neon Rift ▸ Harbor Rain ▸ Build Scene` the radio lines.

## Presentation pieces

| Piece | Code | Notes |
|---|---|---|
| Harbor Rain teaser | `Scripts/Intro/IntroHarborTeaser.cs`, `IntroSceneEntry` | six frames captured in the real Harbor Rain level, drift, scanlines, static, one subtitled radio line; runs at the title point of the opening's Timeline |
| Portal | `Scripts/Gameplay/Campaign/PortalRift.cs`, shader `Levels/Campaign/Shaders/NeonRiftPortal.shader` | ring + premultiplied core (the far side is hidden, so cars vanish into it / emerge from it), sparks, light, sounds |
| Night Run portal sequence | `Scripts/Gameplay/Campaign/NightRunPortalSequence.cs` | staged on the ENTRY ROAD (283 m clear straight found from the road graph); traffic cleared; the car drives in with real physics; the check records whether it physically crossed the plane |
| Harbor Rain arrival, trailer, results, pause | `Levels/HarborRain/Scripts/HarborCampaign.cs` | trailer briefing lists `HarborRainMission.Objectives` (the real steps); skip and watch end in the same `StartCampaignOperation()` |
| Overlay | `Scripts/Game/Campaign/CampaignOverlay.cs` | subtitles, cards, captions, flash/fade, letterbox, choice menu; created by `GameRoot` in the campaign bootstrap only |

## Rebuilding campaign data

`Neon Rift ▸ Campaign ▸ Build Campaign (assets, voice, bootstrap scene)` regenerates the two car selections, the
Harbor Rain mission entry, the campaign config and intro settings, the voice lines (macOS only), the portal
materials and sounds, the teaser frames (from `Logs/HarborReview/teaser_*.png`, captured with the Harbor Rain review
tool's `teaser` section), the campaign bootstrap scene, and appends the campaign scenes to the editor scene list
after the existing ones.
