# 34 · Minimap

> [Index](00_INDEX.md) · Previous: [33 UI and HUD](33_UI_AND_HUD.md) · Next: [35 Objectives and Mission Presentation](35_OBJECTIVES_AND_MISSION_PRESENTATION.md)

## What it is

A **north-up** minimap in the HUD's `minimap-frame`, centred on the player. The map **never rotates**: north (+Z) is always up and east (+X) is right. Only the player arrow and the rival chevrons turn with their world headings. A white tick on the rim marks north.

> The README's feature list still says "heading-up minimap". That was the earlier version; the current code is north-up (`Docs/NightRun.md`, *Minimap (north-up)*).

## Code

| Class | File | Role |
|---|---|---|
| `CityMinimap` (a UI Toolkit `VisualElement`) | `Assets/_NeonRift/Scripts/Gameplay/Mission/CityMinimap.cs` | Draws with **`Painter2D`** in `Draw` (75): roads by class, closed gates in red, the GPS route, the objective marker, rival chevrons, the player arrow (`Arrow`); helpers `OutOfReach`, `Clamp`, `Dot`. Setters `SetNetwork` (42), `SetView(position, heading, lockdown)` (49), `SetRoute` (56), `SetTarget` (62), `SetRivals` (69). Default `Range` 240 m |
| `MinimapProjection` (readonly struct, pure maths) | `Scripts/Gameplay/Mission/MinimapProjection.cs` | `ToMap(world)` (panel y grows downward, so north is −y), `ToWorld(map)`, `Scale` (pixels per metre = width / 2 / Range), `Heading(forward)` (compass degrees: 0 = north, 90 = east, clockwise), `MarkerRotation` |
| `MissionHud.SetRoadNetwork`, `SetNavigator` | `Scripts/Gameplay/Mission/MissionHud.cs:251`, `:258` | Hands the network and the gate-closed predicate (`CityNavigation.IsBlocked`) to the minimap; pushes view, route and target |
| `MissionDirector.UpdateNavigator` | `Scripts/Gameplay/Mission/MissionDirector.cs:512` | Re-plans the route every 0.5 s or when gates change; collects rival positions and headings (index loop, no boxing) |

## Map source

There is no pre-rendered map texture: the map is **drawn live from the road graph** (`RoadNetwork_NightRun.asset`). Line widths come from road class, and closed gates turn red in real time.

## World-to-map conversion

```
scale   = size.x / 2 / range
map.x   = size.x / 2 + (world.x − centre.x) · scale
map.y   = size.y / 2 − (world.z − centre.z) · scale      (panel y grows downward)
heading = atan2(forward.x, forward.z) in degrees, normalised to 0…360 (0 = north, clockwise)
```

## Markers and route

| Marker | Behaviour |
|---|---|
| Player | Arrow at the centre, rotated to its compass heading |
| Rivals | Small orange arrows with their own headings |
| Objective | Marker at its true position; **off-map objectives stay clamped to the rim** in their true direction |
| GPS route | The `RoadPath` from `CityNavigation.Plan`: cyan normally, **magenta in a lockdown** |
| Closed gates | Red road segments |

## Validation

- `MinimapTests` (`Assets/_NeonRift/Scripts/Tests/EditMode/MinimapTests.cs`): `North_IsUp_And_East_IsRight`, `Projection_DoesNotDependOnPlayerHeading` (0 / −90 / 90 / 180 / 270), `ToWorld_RoundTrips`, `Heading_IsCompassClockwiseFromNorth`, `PlayerArrow_PointsAlongWorldHeading`.
- Play Mode: `InputPlaytest.RunMinimapScript` drove straight, left, right and a sustained left with real keyboard events; headings logged 0° → 250° → 267° → 128°, and W Avenue stayed vertical on the map in every capture (`Docs/Screenshots/CityInfill/minimap_heading250.jpg`, `minimap_heading128.jpg`).
