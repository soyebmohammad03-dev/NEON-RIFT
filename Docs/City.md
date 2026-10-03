# The Night Run city

Sector 7 used to be the whole map. It is now the hero district at the centre of a 1.14 × 1.40 km city of five districts, all generated from data by **Neon Rift ▸ Night Run ▸ Build Night Run (district + mission)**. Change `CityLayout`, not the scene.

```
 z 700 ┌──────────── Skyline Drive ──────────────────────────────────┐
       │  SPIRE HEIGHTS   Spire Av                │  Dock Rd │ E Ring │
   480 ├──── Spire Boulevard ─(Spire Plaza + monument)────────────────┤
   300 ├──── North Boulevard ──┬─ Access Rd ─┬────────────┼──────────┤
       │ KOWLOON   W │ SECTOR 7│  DATA CORE  │ E Expwy    │ HARBOR   │
       │ (night mkt, │ (car    │  compound   │  (median,  │ YARDS    │
    60 │  arcade)  A │  park)  │  alley      ├─ Freight Ln + SKYWAY ──┤
  −100 ├──── Market Street ────┴─────────────┼── [HARBOR CHECKPOINT] ─┤
  −320 ├──── South Street ───────────────────┼ Yard Rd ┼ (containers)│
       │ LOWTOWN (construction site)  Kiln St│ ▼ Rift Gate tunnel     │
  −520 ├──── Lowtown Road ──────────────────────────────┼────────────┤
  −700 └──── Southern Perimeter ────────────────────────┴────────────┘
     x −380      −200        0      160            320      560     760
```

## Districts

| District | Character | Street walls | Lamps | Towers |
|---|---|---|---|---|
| **Sector 7** (hero) | The original mission district, curated | Shopfront podiums, neon awnings, blade signs, kerb neon | LED | Hand-placed Singapore heroes + Asian towers |
| **Spire Heights** | Corporate high-rise, wide boulevards | Tall podiums, curtain-wall glass, LED fins, lobbies | LED | London Skyscraper (closes Spire Plaza), 4 Singapore offices, tall Asian towers |
| **Kowloon Market** | Dense, narrow, loud | Short podiums, residential facades with AC units, signs everywhere, overhead cables and lantern strings | Warm | Low/mid Asian towers |
| **Harbor Yards** | Industrial | Warehouses: corrugated cladding, roller doors, wall packs, loading-bay markings | Sodium | Few |
| **Lowtown** | Residential, worn | Mid-rise with fire escapes, AC units, water tanks | Sodium | Mid Asian towers |

The ring beyond the perimeter roads is a backdrop of street walls and skyline-tier towers (no colliders); six skyline clusters sit beyond it.

## Landmarks (navigation)

- **Data Core** beam (objective), **Spire monument** (72 m glass needle with holo rings, Spire Plaza), **Harbor Skyway** (elevated, neon-edged), **gantry cranes** (container yard, aviation lights), **tower crane** (Lowtown construction site), **Kowloon Market arches** (Lantern Arcade).
- District gateway gantries (`SPIRE HEIGHTS ↑`, `KOWLOON MARKET`, `HARBOR YARDS`, `LOWTOWN`) and escape gantries (`EXTRACTION`, `RIFT GATE`, `SKYWAY`).

## Street detail

Generated per block side and zone (`CityProps`): kerb stones, bins, benches, hydrants, utility cabinets, vending machines, planters with trees, food carts, dumpsters, crates, pallets, barrels, bus shelters with adverts; facade AC units, drain pipes, fire escapes, LED fins; rooftop plant, fans, water tanks, antennas with aviation lights. Roads: edge/centre/lane lines, double solid on arterials, stop lines, zebras, manholes, drains, hatched skyway median. Junctions: traffic signals on mast arms (far-side right of every approach) plus pole heads, street-name blades, corner bollards. Expressway: concrete median barrier.

## Road network

`CityLayout.BuildNetwork` writes `Data/World/RoadNetwork_NightRun.asset` (69 nodes, 104 edges): street names, widths, lanes, medians, classes (arterial, street, alley, service, skyway, tunnel) and gate ids. `RoadGraph` finds shortest routes between any two positions; `CityNavigation` adds live gate state (closed = impassable, counting down = +450 m) and a `Version` that changes whenever a gate moves.

Used by: the HUD route and minimap, the location readout (street + district), the AI drivers, and the race standings.

## Lighting

The night look (sky, fog, skyline rings, lamp levels, Volume grade) and its before/after shots are documented in [Environment.md](Environment.md). Summary:

- **Night grade:** cool sky ambient with warm street bounce, 0.16 moon (soft shadows to 90 m), navy sky with a low sodium-brown light-pollution band, exp² fog in the colour of that glow, three skyline rings beyond the city. HDR grading, ACES, bloom threshold 1.0 / intensity 0.38, split toning, SSAO, 4× MSAA + SMAA High.
- **Lamps:** 787 street and flood lamps (600–680 cd), each with a disabled spot light. `LightBudget` keeps the 56 nearest (front-weighted) on and fades them, and gives the 2 nearest in front soft shadows. Every lamp also has an emissive head, a ground pool and a haze cone, so distant streets read without lights.
- **Cars:** `VehicleLights` gives every car headlights and a brake/reverse tail light; the player's left headlight casts shadows.
- **Reflections:** 62 baked box probes along the arterials and Sector 7 streets (128 px in Sector 7/Spire, 64 px elsewhere).

## Lockdown: the city reacts

| System | Calm | Lockdown |
|---|---|---|
| Gates (`SecurityBarrier`) | Alley gate locked (hackable), skyway gate closed | Alley slams; compound gate 12 s, expressway checkpoint 30 s, **harbor checkpoint 22 s**; **skyway gate opens** for the crew |
| Security strips (`SecurityLightGroup`) | Cyan kerb and crown lines | Red, spreading from the theft at 140 m/s |
| Screens (`LockdownScreens`) | Billboards, shelter adverts | Hazard warning, same wave |
| Traffic signals (`TrafficSignalNetwork`) | Green/amber/red cycle | Whole grid flashes red |
| Cameras (`SecurityCamera`, 12) | Idle (green LED) | Track the player in range and line of sight; 1.1 s in view = +8 % heat (cooldown 14 s) |
| Alarm | — | 10 sirens, beacons on gates |
| HUD | Cyan route | Magenta route, closed gates red on the minimap, lockdown frame |

## Performance (desktop, measured in the editor)

Profiler, Night Run start (three cars, about 37 budgeted lamps on): PlayerLoop ≈ 10 ms per frame (the rest of the editor frame is EditorLoop overhead). 7 SRP batches, 165 SetPass calls, 82 draw calls, 3.4 M triangles. Triangles are the next lever: the London tower alone is 143 k, and towers have no LODs yet.


The build log prints podiums, towers, lamps and signals. The scene has about 11.6 k GameObjects, about 3.3 k renderers (one combined mesh per block and material, static batching flags) and about 6.4 k simple colliders. Real-time lights are capped by the budget; mission lights stay off until lockdown. Generated meshes are stored in Git LFS.
