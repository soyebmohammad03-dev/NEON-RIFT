using System.Text;
using NeonRift.Missions;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Development tool: plays a mission end to end in Play Mode through the same interfaces a player uses — the
    /// autopilot drives a <see cref="DrivingRoute"/>, stops at whatever interactable comes into focus and holds the
    /// interact input until it completes, then drives on. Records a timeline of objectives, security, heat, world
    /// events and HUD messages, plus collisions and stalls, as a text report.
    /// </summary>
    public sealed class NightRunValidator : MonoBehaviour
    {
        public enum Scenario
        {
            /// <summary>Short risky route in (alley gate hack), then the expressway checkpoint race out.</summary>
            AlleyInExpresswayOut,
            /// <summary>Long boulevard route in, then back out through the alley (re-hacking the slammed gate).</summary>
            BoulevardInAlleyOut,
            /// <summary>Alley in, steal the core, then park: the trace timer must fail the mission.</summary>
            TraceTimeout
        }

        private enum Stage { Idle, Driving, Stopping, Holding, Waiting, Parked, Done }

        [SerializeField] private DrivingRoute alleyInExpresswayOut;
        [SerializeField] private DrivingRoute boulevardInAlleyOut;
        [Tooltip("Speed below which the car counts as stopped for an interaction, km/h.")]
        [SerializeField] private float stoppedKph = 2f;
        [Tooltip("Pause after an interaction so gates can open, s.")]
        [SerializeField] private float settleSeconds = 1.6f;
        [Tooltip("Abort if the car makes no progress for this long while driving, s.")]
        [SerializeField] private float stallSeconds = 8f;
        [Tooltip("Adds a position/speed sample to the report this often while driving (0 = off), s.")]
        [SerializeField] private float traceInterval;

        private readonly StringBuilder log = new();
        private readonly ScriptedDrivingInput input = new();
        private MissionDirector director;
        private VehicleController vehicle;
        private RouteAutopilot autopilot;
        private Scenario scenario;
        private Stage stage = Stage.Idle;
        private float stageTime, startTime, stallTime;
        private bool held;
        private int collisions, interactions;
        private float maxImpact;
        private Vector3 lastProgressPosition;
        private Interactable holding;
        private DrivingRoute route;
        private float nextTrace;

        public bool Running => stage is not (Stage.Idle or Stage.Done);
        public string Report { get; private set; } = string.Empty;

        public bool Begin(MissionDirector missionDirector, Scenario run, float trace = 0f)
        {
            traceInterval = trace;
            director = missionDirector;
            vehicle = director != null ? director.Player : null;
            route = run == Scenario.BoulevardInAlleyOut ? boulevardInAlleyOut : alleyInExpresswayOut;
            if (vehicle == null || route == null || director.Progress == null)
            {
                Report = "[NightRunValidator] Not ready: needs a running mission, a player vehicle and the route.";
                return false;
            }
            scenario = run;
            autopilot = new RouteAutopilot(route, vehicle) { SpeedScale = 1f, PlannedDeceleration = 7f };
            foreach (var receiver in vehicle.GetComponentsInChildren<IVehicleInputReceiver>()) receiver.SetInputSource(input);
            director.InteractionInputOverride = () => held;
            director.Progress.ObjectiveStarted += o => Note(o != null ? $"objective → {o.Id} ({o.Title}){(director.Progress.HasTimer ? $" timer {director.Progress.TimeRemaining:0.0}s" : string.Empty)}" : "objectives done");
            director.Progress.SecurityChanged += s => Note($"security → {s}");
            director.Progress.HeatAdded += (a, r) => Note($"heat +{a:0.00} ({r}) → {director.Progress.Heat:0.00}");
            director.Progress.PhaseChanged += p => Note($"phase → {p}{(p == MissionPhase.Failed ? $" ({director.Progress.FailReason})" : string.Empty)}");
            director.World.EventRaised += e => Note($"event '{e}'");
            director.World.Announced += (t, tone) => Note($"hud [{tone}] {t}");
            vehicle.Collided += c =>
            {
                collisions++;
                maxImpact = Mathf.Max(maxImpact, c.RelativeSpeed);
            };
            log.Clear();
            collisions = interactions = 0;
            maxImpact = 0f;
            startTime = Time.time;
            lastProgressPosition = vehicle.transform.position;
            Note($"begin {run} with {vehicle.name}");
            Enter(Stage.Driving);
            return true;
        }

        private void Enter(Stage next)
        {
            stage = next;
            stageTime = 0f;
            stallTime = 0f;
        }

        private void Update()
        {
            if (!Running) return;
            stageTime += Time.deltaTime;
            var progress = director.Progress;
            if (progress == null) { Finish("mission ended (scene exit)"); return; }
            float kph = vehicle.Telemetry.SpeedKph;
            if (progress.Phase is MissionPhase.Completed or MissionPhase.Failed)
            {
                input.Current = Hold();
                held = false;
                if (stageTime > 0.5f || stage != Stage.Parked) Finish(progress.Phase.ToString());
                return;
            }

            switch (stage)
            {
                case Stage.Driving:
                    if (director.Focused != null && director.Focused.Current == Interactable.State.Available)
                    {
                        Note($"stopping for {director.Focused.Id} at {kph:0} km/h");
                        Enter(Stage.Stopping);
                        break;
                    }
                    if (scenario == Scenario.TraceTimeout && progress.Security == SecurityLevel.Lockdown)
                    {
                        Note("parking until the trace completes");
                        Enter(Stage.Parked);
                        break;
                    }
                    input.Current = autopilot.ReadInput();
                    if (traceInterval > 0f && Time.time >= nextTrace)
                    {
                        nextTrace = Time.time + traceInterval;
                        var t = vehicle.Telemetry;
                        Note($"trace v {kph:0} km/h (advisory {route.SpeedAt(autopilot.RouteIndex) * 3.6f:0}) idx {autopilot.RouteIndex} " +
                             $"thr {input.Current.Throttle:0.00} brk {input.Current.Brake:0.00} steer {input.Current.Steer:0.00} slip {t.MaxSlip:0.00}");
                    }
                    CheckStall();
                    break;
                case Stage.Stopping:
                    input.Current = Hold();
                    if (kph < stoppedKph)
                    {
                        holding = director.Focused;
                        Enter(Stage.Holding);
                    }
                    break;
                case Stage.Holding:
                    // Like a player: off the brake (holding it at a standstill selects reverse), handbrake on, hold Interact.
                    input.Current = Hold();
                    held = true;
                    if (holding == null || holding.Current == Interactable.State.Completed)
                    {
                        held = false;
                        interactions++;
                        Note($"interaction done after {stageTime:0.0}s");
                        Enter(Stage.Waiting);
                    }
                    else if (!holding.PlayerInside) Finish($"left the {holding.Id} zone before finishing");
                    else if (stageTime > 15f) Finish("interaction did not complete");
                    break;
                case Stage.Waiting:
                case Stage.Parked:
                    input.Current = Hold();
                    if (stage == Stage.Waiting && stageTime >= settleSeconds) Enter(Stage.Driving);
                    break;
            }
        }

        /// <summary>
        /// Brake while rolling forwards, handbrake for the last few km/h and at a standstill. Holding the brake pedal
        /// near a standstill selects reverse, and in reverse the brake pedal drives backwards.
        /// </summary>
        private DrivingInput Hold() => vehicle.Telemetry.ForwardSpeed > 2f ? new DrivingInput { Brake = 1f } : new DrivingInput { Handbrake = true };

        private void CheckStall()
        {
            Vector3 p = vehicle.transform.position;
            if ((p - lastProgressPosition).sqrMagnitude > 4f)
            {
                lastProgressPosition = p;
                stallTime = 0f;
                return;
            }
            stallTime += Time.deltaTime;
            if (stallTime > stallSeconds) Finish($"stalled at {p}");
        }

        private void Note(string text)
        {
            var p = vehicle != null ? vehicle.transform.position : Vector3.zero;
            log.AppendLine($"  t={Time.time - startTime,6:0.0}s ({p.x,6:0},{p.z,6:0})  {text}");
        }

        private void Finish(string reason)
        {
            if (stage == Stage.Done) return;
            Note($"finish: {reason}");
            stage = Stage.Done;
            held = false;
            if (director != null) director.InteractionInputOverride = null;
            var progress = director != null ? director.Progress : null;
            var report = new StringBuilder();
            report.AppendLine($"[NightRunValidator] {scenario}: {reason}");
            if (progress != null)
                report.AppendLine($"  result {progress.Phase}, elapsed {progress.Elapsed:0.0}s, heat {progress.Heat:0.00}, security {progress.Security}, " +
                                  $"objective {progress.ObjectiveIndex}/{progress.Definition.Objectives.Count}, interactions {interactions}, " +
                                  $"collisions {collisions} (max impact {maxImpact:0.0} m/s)");
            report.Append(log);
            Report = report.ToString();
            Debug.Log(Report);
        }

#if UNITY_EDITOR
        public void EditorConfigure(DrivingRoute alleyRoute, DrivingRoute boulevardRoute)
        {
            alleyInExpresswayOut = alleyRoute;
            boulevardInAlleyOut = boulevardRoute;
        }
#endif
    }
}
