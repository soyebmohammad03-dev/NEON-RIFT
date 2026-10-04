# Performance: full-route profile

The whole Night Run route was measured end to end: the BoulevardInAlleyOut validation run (spawn → boulevard → Data Core heist → lockdown → alley gate hack → Rift Gate), with three cars, ambient traffic, crowds, drones and the adaptive score active.

## Tools

| Tool | What it measures |
|---|---|
| `RoutePerf` (editor, Play Mode) | Rides along with the validator. Per frame: PlayerLoop and main-thread time, GPU time (`FrameTimingManager`), GC allocation, SetPass calls, triangles, shadow casters, enabled real-time lights, mission phase and position. Writes `Logs/Perf/<prefix>_seconds.csv` and `_summary.md` with avg / p95 / p99 per segment (approach, heist, escape) |
| `GpuBench` (editor, Play Mode) | Controlled A/B test: the scene is frozen in the lockdown escape, each URP variant is applied in turn, given 120 frames to settle (shaders compiled synchronously), then 300 frames of GPU time are averaged. Three interleaved rounds per variant. The pipeline asset is restored afterwards |
| Unity Profiler (via MCP) | Hotspot attribution: GC allocation and time per sample, drilled down to individual scripts |
| `PerfRouteRunner` + `PerfBuild` (player) | `Neon Rift ▸ Validation ▸ Build Perf Player (macOS)` builds a release player. Running it with `-perfroute <folder>` boots straight into the mission, drives the same route, writes `perfroute_summary.md` / `perfroute_seconds.csv` and quits |

The editor's batch and draw-call counters read 0 under URP's SRP Batcher and GPU Resident Drawer. SetPass calls are the meaningful CPU-side render counter. Editor numbers include editor overhead and drift between runs (the same route measured 48 and 39 fps average an hour apart, with no thermal warnings). Decisions were therefore made only on interleaved A/B measurements and profiler attribution, and absolute numbers come from the player build.

## Findings and fixes

### 1. Per-frame managed allocations (fixed)

The profiler attributed the game's own per-frame GC to `MissionDirector.Update` (≈0.5–0.8 KB/frame) and `VehicleAudio.Update` (≈0.1 KB per car per frame). Causes:

- the race-gap line ("88 M BEHIND KADE") rebuilt every frame with `Split`/`Trim`;
- the interaction prompt string rebuilt every frame;
- HUD setters building strings every frame: the heat label, the street name (`ToUpperInvariant`), the route length, one string per race row;
- `foreach` over `IReadOnlyList<T>` boxing an enumerator (rival positions, standings, `VehicleAudio` wheels, `RacerDriver` traffic and peers).

All of these now build strings only when the displayed value changes and loop with indices. The editor's "GC Allocated In Frame" counter stays near 11 KB because it includes the editor loop (and the profiling harness itself). The profiler shows the game side at ~1.6 KB/frame, nearly all inside URP / UI Toolkit internals.

### 2. GPU: soft shadow filtering (fixed)

`GpuBench` in the lockdown escape (frozen view, 1080p capture):

| Variant | GPU avg | GPU median | vs baseline |
|---|---|---|---|
| baseline (soft shadows High, MSAA 4×) | 21.06 ms | 18.45 ms | — |
| MSAA 2× | 19.60 | 18.41 | −7 % |
| soft shadows off | 14.48 | 11.22 | −31 % |
| soft shadow quality Medium | 21.48 | 22.04 | +2 % |
| **soft shadow quality Low** | **13.32** | **10.97** | **−37 %** |

An earlier round (gpu2) agreed: soft shadows off −34 %, MSAA 2× −12 %, SSAO off −11 %.

**Fix:** soft-shadow filtering is now Low (`NightRunBuilder.ConfigurePipeline`). Shadows stay soft. A pixel diff of the same frozen frame at High and Low gives a mean difference of 0.48/255, and 0.24 % of pixels change by more than 10 per channel. No visible change (`Docs/Screenshots/Performance/soft_high_vs_low.png`, High left, Low right).

**Kept:** MSAA 4× (2× saved 7–12 %, inconsistently, and softens the thin neon edges the look depends on). SSAO stays (−11 % when off, but the night look depends on its contact shading).

### 3. Audio voices (fixed in the audio phase)

52 playing sources against 32 real voices virtualised the music and ambience. Real voices are now 64, and priorities are explicit (see Audio.md).

## Editor route profile (Play Mode, 1080p render, M-series Mac)

PlayerLoop = game work per frame without the editor loop.

| Segment | PlayerLoop avg | p95 | p99 | GPU avg | SetPass | Tris | Shadow casters | Lights |
|---|---|---|---|---|---|---|---|---|
| Approach | 12.3 ms | 17.4 | 24.8 | 14.4 ms | 176 | 3.5 M | 666 | 60 |
| Heist | 12.7 ms | 20.3 | 28.2 | 13.3 ms | 131 | 2.7 M | 364 | 39 |
| Escape (lockdown) | 16.1 ms | 25.5 | 35.8 | 15.6 ms | 112 | 2.6 M | 462 | 65 |

(perf3, before the soft-shadow change; the GPU bench above is the controlled comparison.) The CPU side of a frame breaks down as: scripts ≈0.6 ms Update + 0.8 ms FixedUpdate (vehicle physics, three cars), rendering ≈5–6 ms on the main thread. The rest is URP culling and submission for ~2.6–3.5 M triangles.

## Player build route profile

Release macOS player, Apple M3, 1920×1080 windowed, V-sync off, full BoulevardInAlleyOut route, `-perfroute` (the editor was open in the background). Measured before the pack-car traffic replaced the block traffic.

| Segment | Frames | FPS avg | Frame avg | p95 | p99 | CPU main | CPU render | GPU avg | GPU p95 | GC/frame |
|---|---|---|---|---|---|---|---|---|---|---|
| Approach | 1297 | 35 | 28.9 ms | 47.1 | 92.9 | 26.2 ms | 17.0 ms | 23.5 ms | 33.8 | 0 |
| Heist | 1271 | 38 | 26.0 ms | 38.4 | 52.9 | 24.2 ms | 16.3 ms | 17.5 ms | 30.7 | 0 |
| Escape (lockdown) | 2229 | 39 | 25.4 ms | 39.1 | 57.4 | 23.7 ms | 14.8 ms | 22.1 ms | 30.2 | 0 |
| **Whole route** | 4797 | **38** | 26.5 ms | 41.4 | 61.7 | 24.5 ms | 15.8 ms | 21.3 ms | 32.5 | **0** |

108 hitches over 50 ms (scene warm-up and the theft beat). No managed allocations in any frame of the player build. The M3 at 1080p is balanced between CPU and GPU (~22–25 ms each). The next levers are LODs on the towers and fewer real-time lights in the lockdown (see below). As requested, no further graphics optimisation was done.

## Remaining hotspots

- Mission start: the first 2–3 s after the scene loads have 60–300 ms frames (first use of shaders and the city's meshes). A shader-variant warm-up collection would smooth this.
- Triangles: towers have no LODs (2.6–4.2 M triangles in view). LODs on the tall towers are the next lever on both CPU submission and GPU.
- Real-time lights peak at about 70 during the lockdown (beacons, strobes, drone searchlights) on top of the 56-lamp budget.
