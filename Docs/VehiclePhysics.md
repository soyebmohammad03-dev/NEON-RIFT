# Vehicle physics

The driving model for Neon Rift. Everything a car does comes from one `VehiclePhysicsProfile` (data) applied to a prefab's `VehicleRig` (geometry) by one `VehicleController` (code). Player, AI and replay drivers all feed the same `IVehicleInputSource`.

## Model

| Part | Implementation | Code |
|---|---|---|
| Body | `Rigidbody` at 100 Hz. Mass, centre of mass (from front weight share + CG height), box inertia × per-axis scale, two-box body collider with low-friction material, continuous collision. | `VehicleController` |
| Suspension | Per-wheel raycast suspension. 5 rays per wheel sample the tyre's rolling profile (±40°), so a sharp edge is rolled onto rather than stepped up. Spring rate from natural frequency, preloaded so the static load sits the wheel exactly at the model's ride height. Separate bump/rebound damping ratios with a **digressive knee** (real dampers blow off at high shaft speed). Bump stop in series with tyre radial stiffness. Anti-roll bar per axle. Force acts along the contact normal. | `VehicleWheel` |
| Tyres | Combined-slip model: slip ratio and slip angle are normalised by their peaks and combined, so braking/accelerating in a corner trades grip (friction ellipse). Sine rise to peak, Gaussian fall to sliding grip, load sensitivity, rolling resistance, per-surface grip (`DrivingSurface`). | `TyreModel` |
| Wheel spin | Integrated **implicitly** against the tyre's stiffness (larger of local slope and secant through zero slip) so it is stable at every speed and cannot overshoot through zero slip. A stopped, braked wheel uses static friction: the car holds on a slope without creeping. | `VehicleWheel.UpdateTyre` |
| Engine | Torque curve × peak torque, soft rev limiter, engine braking (regen for EV), inertia that sets rev rate and is reflected onto the driven wheels through the gearing. | `Drivetrain`, `EngineSettings` |
| Clutch | Slips at launch (holds launch rpm), locks once the wheels catch up, opens for shifts (with rev-matching) and the handbrake. | `Drivetrain` |
| Gearbox | Automatic with hysteresis, kick-down and minimum shift interval; decisions use wheel-locked rpm. Reverse = hold brake at standstill (never while the handbrake is held). `Shift(int)` is the hook for manual/paddle shifting. | `Gearbox` |
| Differentials | RWD/FWD/AWD with front torque share; limited-slip coupling per driven axle (0 open … 1 locked). | `VehicleController.ApplyDifferentials` |
| Brakes | Total torque + front bias, rear handbrake. ABS caps each wheel's torque at its available tyre force (including engine braking/regen already in use). | `VehicleWheel` |
| Steering | Rate-limited (slower at speed), speed-dependent lock that follows the grip-limited angle (kinematic angle + peak front slip), Ackermann. Full keyboard lock at speed means "at the limit", not "plough". | `SteeringSystem` |
| Aero | Drag (CdA) at the CoM; front/rear downforce (ClA) at the axles. | `VehicleController.ApplyAerodynamics` |
| Assists | Traction control (feed-forward capacity limit + slip-speed feedback), speed limiter. | `VehicleController.UpdateTractionControl` |

Nothing moves the transform directly. `Teleport`/`Recover` are the only pose writes (spawn, reset after a roll-over).

## Telemetry (for audio, HUD, camera, AI)

`VehicleController.Telemetry` (`VehicleTelemetry`) after every physics step: speed (m/s, km/h, forward, normalised to predicted top speed), raw and effective throttle/brake/steer, steer angle, engine rpm (+ normalised), engine torque and load (−1…1, negative on the overrun), gear, shifting flag, clutch, shift counter, driven-wheel rpm, max combined slip / slip ratio / slip angle, skid speed (tyre-squeal driver), grounded wheel count, longitudinal/lateral g, yaw rate, traction-control factor.

Per wheel (`VehicleController.Wheels`): grounded, load, compression, bump-stop flag, rpm, slip ratio, slip speed, slip angle, combined slip, tyre force, surface grip.

Events: `Gearbox.GearChanged(from, to)`, `VehicleController.Collided(VehicleCollision)` (impulse, closing speed, point, normal, other collider).

## Tuning

Profiles live in `Data/Vehicles/Physics/`. Values are physical: mass in kg, torque in Nm, spring as natural frequency (Hz), damping as a fraction of critical, grip as friction coefficients, aero as C·A in m². Each profile's *Tuning Notes* says which figures are published and which are estimates.

Workflow:
1. Edit the profile (Play Mode changes apply on the next `Configure`, or re-enter Play Mode).
2. **Neon Rift ▸ Vehicles ▸ Measure Vehicle Performance** runs every catalog car on the deterministic bench (~10 s) and logs settle, 0-100/0-200/¼ mile, top speed, 100-0 and 200-0 braking, ramp-steer grip, keyboard step-steer at 80/160/220 km/h and speed bumps.
3. Run the EditMode tests (**Neon Rift ▸ Tests ▸ Run EditMode Tests**). The physics tests enforce stability bands for every catalog car.
4. Drive the validation route (Play `Scenes/Dev/TestTrack`, F3 toggles the telemetry overlay, R recovers).

`VehiclePerformanceProbe.TraceStepSteer` / `TraceBrake` print per-step telemetry for deeper investigation.

## Test track: validation route

`Neon Rift ▸ Test Track ▸ Build Validation Route` (re-runnable) generates a 1.55 km clockwise loop south of the original measurement pads:

| Section | Purpose |
|---|---|
| Acceleration (450 m, 100 m markers) | launch, shifts, traction control |
| Braking zone (150 m, boards at 150/100/50 m) | high-speed braking stability, ABS |
| Banked turn (R60, 180°, 12° bank eased in/out over 30 m on the straights, drivable outer embankment) | sustained load, banking |
| Slalom (7 cones, 18 m) | transient response, weight transfer |
| Speed bumps (5 full-width 10 cm, then one left-only and one right-only) | suspension, damping, roll excitation |
| Braking zone 2 (boards at 150/100/50 m) | braking from mid speed into a slow corner |
| Tight corner (R15, 90°) | low-speed rotation, understeer, traction out |
| Medium corner (R30, 90°) | mid-speed balance |
| Runoff | gravel trap (grip 0.45, high drag) and tyre wall beyond the tight corner; the whole ground plane is grass (grip 0.6) |

`DrivingRoute` stores the driving line (weaving ±1.8 m through the slalom) with advisory speeds computed from the line's own curvature and banking, and section names. `RouteAutopilot` drives it through the normal input interface. It is used for automated validation laps and is the starting point for AI drivers. `VehicleTelemetryLog` summarises a run per section.

`MissionSceneEntry` returns a car that falls below `outOfBoundsHeight` (−30 m) to the spawn point; R (`ResetVehicle`) rights a car where it is.

## Validation results (vehicle-physics phase)

Bench (flat asphalt, 100 Hz), from **Measure Vehicle Performance**:

| | SLS AMG | SLS GT3 | Terzo Millennio |
|---|---|---|---|
| Layout | RWD, 7-speed DCT | RWD, 6-speed sequential, slicks, wing | AWD single-speed EV |
| Rest: residual motion / ride-height error | 0.0 mm/s / 0.1 mm | 0.0 mm/s / 0.0 mm | 0.0 mm/s / 0.3 mm |
| 0-100 / 0-200 km/h | 4.4 s / 11.6 s | 3.7 s / 9.6 s | 3.0 s / 7.3 s |
| Top speed (predicted) | 312 km/h (317, limiter) | 278 km/h (284) | 314 km/h (320, limiter) |
| 100-0 km/h | 38.3 m (1.03 g) | 30.5 m (1.29 g) | 34.0 m (1.16 g) |
| 200-0 km/h, heading change | 142 m, 0.0° | 108 m, 0.0° | 130 m, 0.2° |
| Steady grip 60 / 120 km/h | 0.92 / 0.97 g | 1.19 / 1.29 g | 1.06 / 1.13 g |
| Full keyboard step-steer 80/160/220 km/h, peak body slip | 2.9° / 5.5° / 6.0°, no spin | 1.1° / 3.3° / 3.5° | 2.6° / 5.1° / 5.6° |
| 10 cm sharp bumps at 60 km/h: pitch, wheels off ground | 1.3°, none | 3.4°, brief (stiff race suspension) | 1.4°, none |
| Validation-route lap (autopilot) | 65.7 s | 64.2 s | 62.3 s |

Play Mode on `TestTrack` (real game loop, autopilot through `IVehicleInputSource`): full laps with no NaNs, no airborne wheels outside the speed bumps, no cone contacts after the line fix; collision events, recovery after a roll-over, and the Title → Car Select → Test Track flow verified.

## Known limitations

- Wheels have no unsprung mass: wheel hop and tramp are not modelled; the digressive damper and multi-ray contact stand in for the smoothing a real unsprung wheel gives.
- No tyre relaxation length and no temperature/wear. Low-speed slip uses a 1.5 m/s regularisation floor.
- Brake-locked wheels at walking pace (< ~8 km/h) stop turning slightly before the car (ABS hands over to friction, as in real cars).
- Differentials are speed-coupling approximations, not torque-sensing.
- Body collider is two boxes; the rear wing of the GT3 and mirrors are not separate colliders.
- No anti-dive/anti-squat geometry: the soft road-car front reaches its bump stop under 0.9 g braking from 200 km/h.
- The GT3 (12 cm splitter, 7.5 cm bump travel) scrapes 10 cm speed bumps at ~55 km/h, as a real GT3 would.
- `RouteAutopilot` is a validation driver (pure pursuit + braking-distance planner), not race AI: it has no countersteer or racing line, and assumes 6 m/s² braking.
- Collision events (`Collided`) fire only in Play Mode; the bench checks impacts by outcome.
- Stats shown in Car Select are measured from these profiles on the bench; they are not manufacturer claims (the Terzo Millennio has no official figures at all).
