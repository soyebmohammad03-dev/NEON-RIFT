using System;
using System.Collections.Generic;
using NeonRift.Audio;
using NeonRift.Missions;
using NeonRift.Vehicles;
using NeonRift.World;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Rival crews in a mission: spawns the cars the player did not pick through the normal spawn path (same prefab,
    /// physics profile and audio), gives each a <see cref="RacerDriver"/>, and runs each crew through its own heist
    /// (<see cref="MissionDefinition.RivalTasks"/>: relay hack, uplink, extraction) in parallel with the player. Live
    /// standings compare everyone's whole remaining job, not just the next leg: route metres still to drive plus the
    /// work still to do (converted to metres at <see cref="workMetresPerSecond"/>). The first crews out qualify.
    /// </summary>
    public sealed class RivalDirector : MonoBehaviour, IMissionWorldComponent
    {
        [Serializable]
        public struct Slot
        {
            public VehicleSpawnPoint spawn;
            public RacerProfile profile;
            [Tooltip("Shown at the car while the crew works a task (hack beacon and light).")]
            public GameObject workFx;
        }

        public sealed class Racer
        {
            public string Name;
            /// <summary>Crew name alone ("VEX").</summary>
            public string Crew;
            public VehicleDefinition Definition;
            public VehicleController Car;
            public RacerDriver Driver;
            public GameObject WorkFx;
            /// <summary>Whole remaining job in route metres (work converted), for standings.</summary>
            public float Remaining = float.MaxValue;
            /// <summary>Mission time the crew extracted, s (-1 = still running).</summary>
            public float FinishTime = -1f;
            public bool IsPlayer;
            /// <summary>Body contacts with another car this mission (rival AI quality metric).</summary>
            public int VehicleContacts;
            /// <summary>Current task index (-1 = not started).</summary>
            public int Task = -1;
            public bool Working;
            public float WorkEnd, WorkTotal;
            /// <summary>Which marker the rival is heading for (validation reports).</summary>
            public string GoalId;
            /// <summary>Via points still to drive through before the goal.</summary>
            public readonly Queue<Vector3> Via = new();
            public Vector3 Goal;
            /// <summary>Goal position per task (route metres between them are cached at bind).</summary>
            public Vector3[] Goals = Array.Empty<Vector3>();
            public float[] LegMetres = Array.Empty<float>();
            /// <summary>Race-panel status ("HACKING RELAY 40%", "TO UPLINK", "EXTRACTED").</summary>
            public string Status { get; private set; } = string.Empty;
            /// <summary>Race-panel row: crew and status (rebuilt only when the status changes).</summary>
            public string Row { get; private set; }
            private int percent = -1;

            public void SetStatus(string status)
            {
                percent = -1;
                if (status == Status && Row != null) return;
                Status = status ?? string.Empty;
                Row = IsPlayer || Status.Length == 0 ? Crew : $"{Crew}  ·  {Status}";
            }

            /// <summary>Work progress in 10 % steps, so the row text changes a few times per task, not every frame.</summary>
            public void SetWorkStatus(string label, float progress)
            {
                int p = Mathf.Clamp(Mathf.FloorToInt(progress * 10f), 0, 9) * 10;
                if (p == percent) return;
                SetStatus($"{label} {p}%");
                percent = p;
            }
            public bool Finished => FinishTime >= 0f;
        }

        [SerializeField] private Slot[] slots = Array.Empty<Slot>();
        [SerializeField] private CityNavigation navigation;
        [SerializeField] private RaceMarker[] markers = Array.Empty<RaceMarker>();
        [SerializeField, Min(0.05f)] private float standingsInterval = 0.25f;
        [Tooltip("Work time to route metres for standings (about a car's average speed through the district), m/s.")]
        [SerializeField, Min(1f)] private float workMetresPerSecond = 22f;
        [Tooltip("Estimated seconds the player spends on each interaction objective (standings only).")]
        [SerializeField, Min(0f)] private float playerWorkSeconds = 9f;

        private readonly List<Racer> rivals = new();
        private readonly List<VehicleController> traffic = new();
        private readonly List<Racer> standings = new();
        private readonly Racer player = new() { Name = "YOU", Crew = "YOU", IsPlayer = true };
        private readonly RoadPath scratch = new();
        private MissionWorld world;
        private Vector3? playerTarget;
        private bool running, over;
        private float startAt = -1f, nextStandings;
        private float[] playerLegs = Array.Empty<float>();
        private bool[] playerWork = Array.Empty<bool>();
        private readonly System.Random random = new();

        public IReadOnlyList<Racer> Rivals => rivals;
        /// <summary>Player and rivals, leader first (extracted crews ahead in finishing order).</summary>
        public IReadOnlyList<Racer> Standings => standings;
        public int PlayerPosition { get; private set; } = 1;
        public int Count => rivals.Count + 1;
        public bool HasRivals => rivals.Count > 0;
        public bool RaceActive => running && HasRivals;
        /// <summary>Crews already extracted (rivals only).</summary>
        public int FinishedCount { get { int n = 0; foreach (var r in rivals) if (r.Finished) n++; return n; } }

        /// <summary>Spawns up to <paramref name="max"/> rivals. Call before the mission starts.</summary>
        public void Spawn(IReadOnlyList<VehicleDefinition> vehicles, int max)
        {
            Despawn();
            if (vehicles == null) return;
            for (int i = 0; i < vehicles.Count && rivals.Count < Mathf.Min(max, slots.Length); i++)
            {
                var def = vehicles[i];
                var slot = slots[rivals.Count];
                if (def == null || slot.spawn == null || slot.profile == null) continue;
                var car = slot.spawn.Spawn(def, $"Rival_{def.Id}");
                if (car == null) continue;
                var driver = new RacerDriver(car, slot.profile, navigation) { Traffic = traffic };
                foreach (var receiver in car.GetComponentsInChildren<IVehicleInputReceiver>()) receiver.SetInputSource(driver);
                if (car.TryGetComponent(out VehicleAudio audio)) audio.SetPlayerView(false);
                var racer = new Racer
                {
                    Name = $"{slot.profile.DisplayName} · {def.DisplayName.ToUpperInvariant()}", Crew = slot.profile.DisplayName,
                    Definition = def, Car = car, Driver = driver, WorkFx = slot.workFx
                };
                if (racer.WorkFx != null) racer.WorkFx.SetActive(false);
                car.Collided += c =>
                {
                    var other = c.Other != null ? c.Other.GetComponentInParent<VehicleController>() : null;
                    if (other == null) return;
                    racer.VehicleContacts++;
                    var mine = car.Body.linearVelocity;
                    var theirs = other.Body != null ? other.Body.linearVelocity : Vector3.zero;
                    Debug.Log($"[Rivals] contact {car.name} × {other.name} at {c.Point:F0}: {c.RelativeSpeed:0.0} m/s closing, " +
                              $"speeds {mine.magnitude:0.0}/{theirs.magnitude:0.0} m/s, angle {Vector3.Angle(mine, theirs):0}°, state {driver.State} {driver.DebugState}");
                };
                rivals.Add(racer);
                traffic.Add(car);
            }
            var peers = new List<RacerDriver>();
            foreach (var r in rivals) peers.Add(r.Driver);
            foreach (var r in rivals) r.Driver.Peers = peers;
            Debug.Log($"[Rivals] spawned {rivals.Count} rival(s).");
        }

        private void Despawn()
        {
            foreach (var r in rivals)
            {
                if (r.Car != null) Destroy(r.Car.gameObject);
                if (r.WorkFx != null) r.WorkFx.SetActive(false);
            }
            rivals.Clear();
            traffic.Clear();
            standings.Clear();
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.ObjectiveStarted += OnObjectiveStarted;
            running = over = false;
            startAt = -1f;
            playerTarget = null;
            player.Car = world.Player;
            player.FinishTime = -1f;
            player.SetStatus(null);
            if (player.Car != null && !traffic.Contains(player.Car)) traffic.Add(player.Car);
            foreach (var r in rivals)
            {
                r.Driver.PlayerCar = player.Car;
                r.FinishTime = -1f;
                r.Task = -1;
                r.Working = false;
                r.Via.Clear();
                r.GoalId = null;
                r.SetStatus("STAGING");
                if (r.WorkFx != null) r.WorkFx.SetActive(false);
                r.Driver.Hold();
            }
            PlanRoutes();
        }

        public void Unbind()
        {
            if (world != null) world.ObjectiveStarted -= OnObjectiveStarted;
            world = null;
            running = false;
            playerTarget = null;
            foreach (var r in rivals) if (r.WorkFx != null) r.WorkFx.SetActive(false);
        }

        /// <summary>Caches the route length of every leg (each crew's tasks, the player's objectives) once per attempt.</summary>
        private void PlanRoutes()
        {
            var tasks = world.Mission != null ? world.Mission.RivalTasks : null;
            for (int i = 0; i < rivals.Count; i++)
            {
                var r = rivals[i];
                int n = tasks?.Count ?? 0;
                r.Goals = new Vector3[n];
                r.LegMetres = new float[n];
                Vector3 from = r.Car != null ? r.Car.Body.position : transform.position;
                for (int k = 0; k < n; k++)
                {
                    var route = tasks[k].RouteFor(i);
                    float metres = 0f;
                    foreach (var id in route)
                    {
                        var m = Marker(id);
                        if (m == null) { Debug.LogError($"[Rivals] No race marker '{id}'.", this); continue; }
                        Vector3 p = m.Slot(i);
                        metres += RouteMetres(from, p);
                        from = p;
                    }
                    r.Goals[k] = from;
                    r.LegMetres[k] = metres;
                }
            }
            var positions = world.ObjectivePositions;
            var objectives = world.Mission != null ? world.Mission.Objectives : null;
            int count = objectives?.Count ?? 0;
            playerLegs = new float[count];
            playerWork = new bool[count];
            for (int k = 0; k < count; k++)
            {
                playerWork[k] = objectives[k] != null && objectives[k].Kind == ObjectiveKind.Interact;
                if (k > 0 && k < positions.Count && positions[k].HasValue && positions[k - 1].HasValue)
                    playerLegs[k] = RouteMetres(positions[k - 1].Value, positions[k].Value);
            }
        }

        private float RouteMetres(Vector3 from, Vector3 to) =>
            navigation != null && navigation.Plan(from, to, scratch) ? scratch.Length : Vector3.Distance(from, to) * 1.4f;

        private void OnObjectiveStarted(ObjectiveDefinition objective, Vector3? target, bool last)
        {
            playerTarget = target;
            if (objective != null && !running && !over)
            {
                running = true;
                startAt = Time.time + (world.Mission != null ? world.Mission.RivalStartDelay : 0f);
            }
            // Mission over: everyone stops where they are.
            if (objective == null)
            {
                over = true;
                running = false;
                foreach (var r in rivals)
                {
                    if (!r.Finished) r.Driver.Hold();
                    if (r.WorkFx != null) r.WorkFx.SetActive(false);
                }
            }
        }

        /// <summary>Sends rival <paramref name="i"/> to task <paramref name="k"/> (or nowhere when the list is done).</summary>
        private void BeginTask(int i, int k)
        {
            var r = rivals[i];
            var tasks = world.Mission.RivalTasks;
            r.Task = k;
            r.Working = false;
            r.Via.Clear();
            if (k >= tasks.Count) { r.Driver.Hold(); r.SetStatus(null); return; }
            var task = tasks[k];
            var route = task.RouteFor(i);
            for (int v = 0; v < route.Length - 1; v++)
            {
                var via = Marker(route[v]);
                if (via != null) r.Via.Enqueue(via.Slot(i));
            }
            r.Goal = r.Goals[k];
            r.GoalId = route.Length > 0 ? route[^1] : null;
            bool hold = task.HasWork || task.finish;
            if (r.Via.Count > 0) r.Driver.SetGoal(r.Via.Peek(), hold: false);
            else r.Driver.SetGoal(r.Goal, hold);
            r.SetStatus(task.driveLabel);
            Debug.Log($"[Rivals] {r.Name} → task {k} {r.GoalId} at {world.Elapsed:0.0}s");
        }

        private RaceMarker Marker(string id)
        {
            foreach (var m in markers) if (m != null && m.Id == id) return m;
            return null;
        }

        private void Update()
        {
            if (world == null || !running) return;
            if (startAt >= 0f)
            {
                if (Time.time < startAt) return;
                startAt = -1f;
                for (int i = 0; i < rivals.Count; i++) BeginTask(i, 0);
            }
            var tasks = world.Mission.RivalTasks;
            for (int i = 0; i < rivals.Count; i++)
            {
                var r = rivals[i];
                if (r.Finished || r.Task < 0 || r.Task >= tasks.Count || r.Car == null) continue;
                var task = tasks[r.Task];
                // Through a via point: on to the next (a generous radius keeps the car rolling through it).
                if (r.Via.Count > 0)
                {
                    if (r.Driver.AtGoal || Vector3.Distance(r.Car.Body.position, r.Via.Peek()) < 14f)
                    {
                        r.Via.Dequeue();
                        if (r.Via.Count > 0) r.Driver.SetGoal(r.Via.Peek(), hold: false);
                        else r.Driver.SetGoal(r.Goal, task.HasWork || task.finish);
                    }
                    continue;
                }
                if (r.Working)
                {
                    if (r.WorkFx != null) r.WorkFx.transform.position = r.Car.Body.position;
                    float left = r.WorkEnd - Time.time;
                    r.SetWorkStatus(task.workLabel, 1f - Mathf.Clamp01(left / r.WorkTotal));
                    if (left > 0f) continue;
                    r.Working = false;
                    if (r.WorkFx != null) r.WorkFx.SetActive(false);
                    string done = task.MessageFor(i, r.Crew);
                    if (done != null) world.Announce(done, MessageTone.Warning);
                    BeginTask(i, r.Task + 1);
                    continue;
                }
                bool arrived = r.Driver.AtGoal || (!task.HasWork && !task.finish && Vector3.Distance(r.Car.Body.position, r.Goal) < 14f);
                if (!arrived) continue;
                if (task.finish)
                {
                    r.FinishTime = world.Elapsed;
                    r.SetStatus("EXTRACTED");
                    world.Announce($"{r.Crew} EXTRACTED", MessageTone.Warning);
                    Debug.Log($"[Rivals] {r.Name} extracted at {r.FinishTime:0.0}s");
                    OnRivalFinished();
                }
                else if (task.HasWork)
                {
                    r.Working = true;
                    float jitter = 1f + task.workJitter * (float)(random.NextDouble() * 2.0 - 1.0);
                    r.WorkTotal = Mathf.Max(0.5f, task.workSeconds * jitter);
                    r.WorkEnd = Time.time + r.WorkTotal;
                    if (r.WorkFx != null)
                    {
                        r.WorkFx.transform.position = r.Car.Body.position;
                        r.WorkFx.SetActive(true);
                    }
                }
                else BeginTask(i, r.Task + 1);
            }
            if (Time.time >= nextStandings)
            {
                nextStandings = Time.time + standingsInterval;
                UpdateStandings();
            }
        }

        /// <summary>Warns the player when the qualifying places are filling up without them.</summary>
        private void OnRivalFinished()
        {
            int places = world.Mission != null ? world.Mission.QualifyingPlaces : 0;
            if (places <= 0 || player.Finished) return;
            int extracted = FinishedCount;
            if (extracted == places) world.Announce("QUALIFYING CLOSED  ·  NO PLACE LEFT FOR YOU", MessageTone.Danger);
            else if (extracted == places - 1) world.Announce($"{places - extracted} QUALIFYING PLACE LEFT", MessageTone.Danger);
        }

        /// <summary>Whole job left for a rival: this leg's route, the work in hand, then every later task.</summary>
        private float RivalRemaining(Racer r, IReadOnlyList<RivalTask> tasks)
        {
            if (r.Finished) return 0f;
            if (r.Task < 0) return Sum(r.LegMetres, 0) + WorkMetres(tasks, 0);
            if (r.Task >= tasks.Count) return 0f;
            float here = r.Working ? Mathf.Max(0f, r.WorkEnd - Time.time) * workMetresPerSecond
                                   : RouteMetres(r.Car.Body.position, r.Goal) + tasks[r.Task].workSeconds * workMetresPerSecond;
            return here + Sum(r.LegMetres, r.Task + 1) + WorkMetres(tasks, r.Task + 1);
        }

        private float WorkMetres(IReadOnlyList<RivalTask> tasks, int from)
        {
            float s = 0f;
            for (int k = from; k < tasks.Count; k++) s += tasks[k].workSeconds;
            return s * workMetresPerSecond;
        }

        private static float Sum(float[] values, int from)
        {
            float s = 0f;
            for (int k = from; k < values.Length; k++) s += values[k];
            return s;
        }

        /// <summary>Whole job left for the player: route to the current objective, then every later objective leg.</summary>
        private float PlayerRemaining()
        {
            if (player.Car == null || !playerTarget.HasValue) return float.MaxValue;
            int k = Mathf.Max(0, world.ObjectiveIndex);
            float metres = RouteMetres(player.Car.Body.position, playerTarget.Value);
            for (int j = k; j < playerLegs.Length; j++)
            {
                if (j > k) metres += playerLegs[j];
                if (playerWork[j]) metres += playerWorkSeconds * workMetresPerSecond;
            }
            return metres;
        }

        private void UpdateStandings()
        {
            standings.Clear();
            if (!RaceActive) return;
            var tasks = world.Mission.RivalTasks;
            player.Remaining = PlayerRemaining();
            standings.Add(player);
            foreach (var r in rivals)
            {
                r.Remaining = RivalRemaining(r, tasks);
                standings.Add(r);
                // Data-driven rubber band: positive gap = rival behind the player.
                r.Driver.PaceScale = r.Driver.Profile.RubberBand(r.Remaining - player.Remaining);
            }
            standings.Sort((a, b) =>
            {
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                if (a.Finished) return a.FinishTime.CompareTo(b.FinishTime);
                return a.Remaining.CompareTo(b.Remaining);
            });
            PlayerPosition = standings.IndexOf(player) + 1;
        }

        /// <summary>One line per rival: AI quality counters for validation reports.</summary>
        public string DescribeAi()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in rivals)
                sb.AppendLine($"  rival {r.Name}: car contacts {r.VehicleContacts}, reversals {r.Driver.Reversals}, resets {r.Driver.Recoveries}, " +
                              $"yields {r.Driver.Yields}, lane holds {r.Driver.LaneHolds}, task {r.Task} goal {r.GoalId ?? "-"} ({r.Status}), state {r.Driver.State}, at goal {r.Driver.AtGoal}, " +
                              $"finished {(r.Finished ? $"{r.FinishTime:0.0}s" : "no")}, remaining {(r.Finished ? 0f : r.Remaining):0} m");
            return sb.ToString();
        }

        /// <summary>Final position when the player extracts: rivals already out are ahead.</summary>
        public int FinishPosition()
        {
            int ahead = 0;
            foreach (var r in rivals) if (r.Finished) ahead++;
            return ahead + 1;
        }

        /// <summary>Gap to the racer just ahead of (or behind, if leading) the player, route metres.</summary>
        public bool TryGetGap(out string otherName, out float metres, out bool ahead)
        {
            otherName = null;
            metres = 0f;
            ahead = false;
            if (standings.Count < 2) return false;
            int i = PlayerPosition - 1;
            var other = i > 0 ? standings[i - 1] : standings[1];
            ahead = i > 0;
            otherName = other.Name;
            metres = Mathf.Abs(other.Remaining - player.Remaining);
            return true;
        }

#if UNITY_EDITOR
        public void EditorConfigure(Slot[] rivalSlots, CityNavigation nav, RaceMarker[] raceMarkers)
        {
            slots = rivalSlots;
            navigation = nav;
            markers = raceMarkers;
        }
#endif
    }
}
