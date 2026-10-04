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

## Block interiors (October 2026)

Before this pass every block was a thin ring of podium street walls around a bare 15 cm pavement slab: about 139 ha of empty interior that showed in every aerial shot and above the podiums from the street (`height_before` below: yellow = bare slab).

`CityInfill` now fills whatever is left behind the street walls, the towers, the lots and the sidewalks:

1. **Free space** on a 2 m grid per block. It excludes podium, tower and hero footprints (+1.5 m), lots, reserved areas, and the sidewalk band plus 2 m.
2. **Parcels**: the largest free rectangle (histogram method), split down to 58 m (70 m in the harbor), repeated until under 120 m². Leftover slivers become two- or three-storey annexes or back-of-house clutter.
3. **Use per parcel** from the zone's mix. Each use has a reason to be there:

| Use | Where | What is built |
|---|---|---|
| Back buildings | all zones | Masses split by 3 m service passages, taller than the street wall (S7 14–32 m, Spire 20–46 m, Kowloon/Lowtown 12–28 m), stepped tiers, parapets, rooftop plant, lit service doors |
| Service yard | S7, Kowloon, Lowtown, Harbor | Lot asphalt, fence with a gate, dumpsters, pallets, crates, barrels, loading bays with vans or box trucks, sodium wall lights |
| Surface car park | all but Outer | Bay rows on both sides of 6.5 m aisles, parked cars (~58 % occupancy), lamp posts, pay station, PARKING sign |
| Multi-storey car park | S7, Spire | 2–4 open decks on columns, upstand walls, lit soffits, cars on every deck, lamps on the roof |
| Pocket park | Spire, Lowtown | Lawn, clipped hedge, crossing paths, trees, benches, path bollard lights |
| Courtyard | S7, Spire, Kowloon | Paving, a lit fountain basin, planter trees, benches, bollard lights |
| Utility compound | all | Fenced substation: transformers with fins and insulators, cabinets with LEDs, cable tray, DANGER sign, pole light |
| Warehouse | Harbor | Corrugated shed with skylights, dock platform, roller doors with wall packs, bay lines, box trucks at the docks |
| Storage yard | Harbor | Container stacks in rows with aisles, fence, flood masts |

**Alleys** (Service Alley, Fish Alley) also got back-of-house walls: drain and service pipes, extract vents, caged security lights with their pools, fire-exit doors under green signs, and power cables slung across from one side.

Result of the current build: 119 of 139 ha filled. 707 building parcels, 69 service yards, 27 surface car parks, 13 multi-storey car parks, 24 parks, 30 courtyards, 26 utility compounds, 31 warehouses, 17 storage yards, 1,802 parked vehicles, 385 yard lamps.

| Before (yellow = bare slab) | After |
|---|---|
| ![](Screenshots/CityInfill/height_before.jpg) | ![](Screenshots/CityInfill/height_after.jpg) |

| Sector 7 from the air | Lowtown |
|---|---|
| ![](Screenshots/CityInfill/aerial_sector7.jpg) | ![](Screenshots/CityInfill/aerial_lowtown.jpg) |

| Car park, yard and park inside a Lowtown block | Fish Alley |
|---|---|
| ![](Screenshots/CityInfill/lot_parking_park.jpg) | ![](Screenshots/CityInfill/fish_alley.jpg) |

### Cost rules

- Masses go into the block's combined meshes, one renderer per material, as before. A building is a 10-triangle box plus its parapet.
- Furniture, fences and parked vehicles go to a new **Clutter** layer (12). It is emitted in 64 m cells, and the camera culls it beyond **90 m**, so the distance test works on small bounds. Cars and vans cast no shadows; trucks do.
- Interior light is **emissive only**: lamp heads and brighter `District_YardPool*` ground pools. No real-time lights are added, so the street `LightBudget` is unchanged.
- One simple box collider per mass, vehicle, dumpster, fence run or container stack. All of them are on Environment, none on the road.
- Each block's infill has its own seed, so changing the infill never reshuffles the street walls.
- Generated mesh assets are smaller: vertex colour is now 8-bit, and tangents are written only for normal-mapped materials. The district mesh folder is 238 MB with the infill (it was 148 MB without it, in the old format).

Spawn view (W Avenue, editor): 4.06 M triangles (2.94 M before), 231 SetPass, 7.3 k draws, 611 shadow casters. Most of the increase is the shadow and depth passes over the new masses. The route profile is in the performance section.

Validation after the rebuild: `BoulevardInAlleyOut` **Completed** in 102 s (the fast timing): 0 player collisions, 0 rival contacts. EditMode 107/107.

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

## City life

Traffic uses real cars with physics. Pedestrians and drones stay lightweight. Built by `NightRunBuilder.CityLife.cs`.

| System | What it is | Rules |
|---|---|---|
| `CityTraffic` + `TrafficCar` | Cars from the **generic passenger car pack** (10 models assembled into prefabs by `TrafficCarBuilder`: body + its four wheels re-framed, URP materials from the pack's textures, box collider, Rigidbody 1350–1900 kg). **14 roaming** + **26 parked**, tinted variants | **Physics:** four raycast springs (ride height, pitch and roll), lateral tyre grip up to ~0.95 g, drive/brake force, yaw steering toward the lane. **Roaming:** kerb-side lane of the road graph (no alleys, service roads, tunnels or the Data Core compound), slow for corners, keep a braking distance from anything ahead (other traffic, the player, the rivals: forward sphere cast), **give way at junctions** when the player or a rival will reach it within ~3.5 s, pull over and stop in a lockdown. **Parked:** 2,073 bays recorded from the parking lots and garage decks (they replaced the static block cars). The nearest 26 to the player are filled; parked cars are kinematic until something hits them. **Collisions:** the Traffic layer ignores itself (traffic never collides with traffic) but collides with the player, the rivals and the world. A hit makes the car dynamic, shunts and spins it with the momentum share of the hitting car, plays an impact sound by strength and stops it driving for 2.5–7 s, after which it rejoins the nearest road. **Pooling:** cars are only placed or recycled out of the camera's view (no popping): far away, overturned, stuck, or disturbed and left behind. **Sound:** a 3D engine loop pitched by speed (low voice priority) and impact one-shots. Rivals' obstacle probes include the Traffic layer |
| `CrowdGroups` | 62 groups, 270 static figures (Detail layer) on plazas, the night market and pavements | A group steps out of sight when the player comes within 16 m; it never stands in the road. A slow sway; six groups are checked per frame |
| `SecurityDrones` | 4 drones with red and blue strobes and a searchlight spot | Dormant until `core.extract.trace` or a lockdown. They then lift off from the Data Core and orbit a point that trails the player at altitude, sweeping the street |

Validation (Play Mode): every heist scenario completes with traffic live, 14 roaming and 26 parked, 29 cars within 150 m of the player. A scripted crash into a parking row woke four parked cars: they were shoved, took 2–4 hits each and came to rest upright. The scripted validation driver follows a fixed line and does not steer around traffic, so its runs now include some traffic contacts. A player steers around them. The validation driver brakes for traffic on its line (gently, so following rivals can react).

Two causes found and fixed on the way:
- **Stranded at the compound gate.** The approach road ends at the gated compound, which traffic is excluded from. Cars drove up the spur and stopped at its end, blocking the player and VEX. Dead ends are now pruned from the traffic network: nodes with one drivable edge are removed repeatedly until none remain.
- **Crossing in front of the player.** Traffic now gives way at junctions to the player and the rivals.

Final regression series with traffic (`MissionSeries`, Play Mode):

| Run | Scenario | Result | Player collisions | Rival contacts |
|---|---|---|---|---|
| 1 | BoulevardInAlleyOut | Completed 132.5 s | 0 | 0 |
| 2 | AlleyInExpresswayOut | Ends at the sealed expressway checkpoint (by design) | 2 (the barrier) | 0 |
| 3 | BoulevardInAlleyOut | Completed 127.6 s | 0 | 0 |

![Pack traffic at a junction](Screenshots/Life/pack_traffic_junction.png)
![Parked pack cars after a crash](Screenshots/Life/pack_parked_crash.png)
![The ten pack cars](Screenshots/Life/pack_lineup.png)
![Drones over the compound](Screenshots/Life/drones_lockdown.png)
![Plaza crowd](Screenshots/Life/crowd_plaza.png)

## Performance (desktop, measured in the editor)

Profiler, Night Run start (three cars, about 37 budgeted lamps on): PlayerLoop ≈ 10 ms per frame (the rest of the editor frame is EditorLoop overhead). 7 SRP batches, 165 SetPass calls, 82 draw calls, 3.4 M triangles. Triangles are the next lever: the London tower alone is 143 k, and towers have no LODs yet.


The build log prints podiums, towers, lamps and signals. The scene has about 11.6 k GameObjects, about 3.3 k renderers (one combined mesh per block and material, static batching flags) and about 6.4 k simple colliders. Real-time lights are capped by the budget; mission lights stay off until lockdown. Generated meshes are stored in Git LFS.
