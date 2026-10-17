# Changelog

Dates are commit dates (2026). Earlier entries summarise the commit history on `main`.

## Campaign release — 2026-10-18

**Connected two-level campaign**
- New campaign bootstrap (`Levels/Campaign/Scenes/CampaignBootstrap.unity`) with its own config, save and opening;
  the standalone Night Run bootstrap is unchanged.
- Harbor Rain teaser in the Night Run opening (intercepted transmission, separately skippable).
- Night Run qualification (top two crews) opens **ENTER THE RIFT**: a portal sequence on the road out of the city,
  the car drives in under its own physics and emerges at the port.
- Unlock card quoting the real Night Run result; Level 2 five-car selection; the two Harbor Rain cars are locked in
  Level 1 and unlocked for Level 2 after qualifying.
- Harbor Rain trailer (narrated shots of the real level, real objectives and controls, skippable) leading straight
  into the mission; campaign results and replay choices; campaign pause menu in Harbor Rain.
- Saved progression (`NeonRift.Campaign.Save`) with validation of missing, invalid and partial data; title entry to
  Harbor Rain once unlocked; reset option; development shortcuts in debug builds only.
- Unified subtitles for every spoken line with a Settings toggle (on by default).
- Campaign APK build script (`com.soyebmohammad.neonrift.campaign`, installs beside Night Run), build manifest,
  restoration of build side-effect files; the standalone APK script is pinned to its own scene list.

**Harbor Rain (Level 2)**
- Kowloon Bay container port in a storm: manifest puzzle, security-gate bypass, gantry crane container lift onto the
  crew truck, alarm and lockdown, drones and searchlights, port police pursuit with roadblocks and re-routing,
  floods, a storm-surge coast road and extraction; original port truck, container trailer and forklift models.
- Crane reliability: fixed-step crane simulation (no more stalls at long frame times); event-driven validator log.
- The missing "crane delivered" radio line, bench-measured stats for the Harbor Rain cars, brighter light pools.

**Repository**
- New README, campaign / installation / controls / building / testing docs, changelog, contributing and security
  guides, issue and pull request templates, and a GitHub Actions repository check.
- Licensing review: assets with unverified or incompatible rights (two Harbor Rain cars, a work boat) and the macOS
  System Voice recordings are kept out of the public repository; the project runs without them.

## Android build — 2026-10-15
- Touch controls (virtual gamepad), optional tilt steering with calibration, landscape APK settings and build script.

## Technical book — 2026-10-08
- `Docs/NEON_RIFT_BOOK`: the complete technical documentation, also as one PDF.

## Night Run — 2026-10-02 to 2026-10-04
- Project foundation, asset intake and audit, vehicle physics with a deterministic bench, synthesised engine audio.
- Night Run vertical slice, then the full five-district city, rival AI, night lighting, navigator HUD and minimap.
- Opening cinematic, crew-garage Car Select, Data Core heist and security-gate hack, three-crew heist with rival
  heists and top-two qualifying, drone pursuit.
- City life (traffic, crowds, drones), mission presentation and graded debrief, adaptive score, owner-supplied music
  tracks, pause menu and settings, performance work.
