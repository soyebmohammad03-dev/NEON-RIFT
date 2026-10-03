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
    /// physics profile and audio), gives each a <see cref="RacerDriver"/>, sends them to the race marker each objective
    /// names (<see cref="ObjectiveDefinition.RivalGoalId"/>), and keeps live standings by route distance to the current
    /// objective. Rivals never complete the mission; the player's finishing position counts rivals already extracted.
    /// </summary>
    public sealed class RivalDirector : MonoBehaviour, IMissionWorldComponent
    {
        [Serializable]
        public struct Slot
        {
            public VehicleSpawnPoint spawn;
            public RacerProfile profile;
        }

        public sealed class Racer
        {
            public string Name;
            public VehicleDefinition Definition;
            public VehicleController Car;
            public RacerDriver Driver;
            public float Remaining = float.MaxValue;
            public float FinishTime = -1f;
            public bool IsPlayer;
            /// <summary>Body contacts with another car this mission (rival AI quality metric).</summary>
            public int VehicleContacts;
            /// <summary>True once this objective's goal has been handed to the driver (arrival before that is the previous leg's).</summary>
            public bool OnLeg;
            /// <summary>Arriving at the current goal finishes the race (extraction).</summary>
            public bool FinishOnArrival;
            /// <summary>Radio line shown when the rival reaches its current goal (once).</summary>
            public string ArrivalMessage;
            /// <summary>Which marker the rival is heading for (validation reports).</summary>
            public string GoalId;
            /// <summary>Via points still to drive through before the goal, and the order's hold flag for the goal.</summary>
            public readonly Queue<Vector3> Via = new();
            public Vector3 Goal;
            public bool HoldAtGoal;
            public bool Finished => FinishTime >= 0f;
        }

        [SerializeField] private Slot[] slots = Array.Empty<Slot>();
        [SerializeField] private CityNavigation navigation;
        [SerializeField] private RaceMarker[] markers = Array.Empty<RaceMarker>();
        [SerializeField, Min(0.05f)] private float standingsInterval = 0.25f;

        private readonly List<Racer> rivals = new();
        private readonly List<VehicleController> traffic = new();
        private readonly List<Racer> standings = new();
        private readonly Racer player = new() { Name = "YOU", IsPlayer = true };
        private readonly RoadPath scratch = new();
        private MissionWorld world;
        private Vector3? raceTarget;
        private bool finalLeg;
        private float startAt = -1f, nextStandings, legStart;
        private ObjectiveDefinition current;
        private readonly List<(float time, RivalOrder order)> orders = new();

        public IReadOnlyList<Racer> Rivals => rivals;
        /// <summary>Player and rivals, leader first (extracted rivals ahead in finishing order).</summary>
        public IReadOnlyList<Racer> Standings => standings;
        public int PlayerPosition { get; private set; } = 1;
        public int Count => rivals.Count + 1;
        public bool HasRivals => rivals.Count > 0;
        public bool RaceActive => raceTarget.HasValue && HasRivals;

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
                var racer = new Racer { Name = $"{slot.profile.DisplayName} · {def.DisplayName.ToUpperInvariant()}", Definition = def, Car = car, Driver = driver };
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
            foreach (var r in rivals) if (r.Car != null) Destroy(r.Car.gameObject);
            rivals.Clear();
            traffic.Clear();
            standings.Clear();
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.ObjectiveStarted += OnObjectiveStarted;
            world.EventRaised += OnWorldEvent;
            orders.Clear();
            player.Car = world.Player;
            if (player.Car != null && !traffic.Contains(player.Car)) traffic.Add(player.Car);
            foreach (var r in rivals) r.Driver.PlayerCar = player.Car;
            player.FinishTime = -1f;
            foreach (var r in rivals)
            {
                r.FinishTime = -1f;
                r.Via.Clear();
                r.OnLeg = r.FinishOnArrival = false;
                r.ArrivalMessage = r.GoalId = null;
                r.Driver.Hold();
            }
        }

        public void Unbind()
        {
            if (world != null)
            {
                world.ObjectiveStarted -= OnObjectiveStarted;
                world.EventRaised -= OnWorldEvent;
            }
            world = null;
            raceTarget = null;
            orders.Clear();
        }

        private void OnObjectiveStarted(ObjectiveDefinition objective, Vector3? target, bool last)
        {
            current = objective;
            raceTarget = target;
            finalLeg = last;
            legStart = Time.time;
            bool hasGoal = objective != null && !string.IsNullOrEmpty(objective.RivalGoalId);
            startAt = hasGoal ? Time.time + objective.RivalStartDelay : -1f;
            // Mission over: everyone stops. An objective without a rival goal leaves the crews on their current orders.
            if (objective == null)
            {
                orders.Clear();
                foreach (var r in rivals) if (!r.Finished) r.Driver.Hold();
            }
        }

        private void OnWorldEvent(string eventId)
        {
            if (world == null || world.Mission == null) return;
            foreach (var o in world.Mission.RivalOrders)
                if (o.eventId == eventId) orders.Add((Time.time + o.delay, o));
        }

        /// <summary>Hands each rival its goal from <paramref name="order"/>.</summary>
        private void Apply(RivalOrder order)
        {
            for (int i = 0; i < rivals.Count; i++)
            {
                var r = rivals[i];
                if (r.Finished) continue;
                var route = order.RouteFor(i);
                if (route.Length == 0) continue;
                string id = route[^1];
                var marker = Marker(id);
                if (marker == null) { Debug.LogError($"[Rivals] No race marker '{id}'.", this); continue; }
                r.Via.Clear();
                for (int v = 0; v < route.Length - 1; v++)
                {
                    var via = Marker(route[v]);
                    if (via != null) r.Via.Enqueue(via.Slot(i));
                    else Debug.LogError($"[Rivals] No race marker '{route[v]}'.", this);
                }
                r.Goal = marker.Slot(i);
                r.HoldAtGoal = order.hold;
                if (r.Via.Count > 0) r.Driver.SetGoal(r.Via.Peek(), hold: false);
                else r.Driver.SetGoal(r.Goal, order.hold);
                r.OnLeg = true;
                r.FinishOnArrival = order.finish;
                r.GoalId = id;
                r.ArrivalMessage = order.MessageFor(i, r.Driver.Profile.DisplayName);
                Debug.Log($"[Rivals] {r.Name} → {id} ({order.eventId})");
            }
        }

        private RaceMarker Marker(string id)
        {
            foreach (var m in markers) if (m != null && m.Id == id) return m;
            return null;
        }

        private void Update()
        {
            if (world == null) return;
            if (startAt >= 0f && Time.time >= startAt)
            {
                startAt = -1f;
                var marker = Marker(current.RivalGoalId);
                if (marker == null) Debug.LogError($"[Rivals] No race marker '{current.RivalGoalId}'.", this);
                else for (int i = 0; i < rivals.Count; i++)
                    if (!rivals[i].Finished)
                    {
                        rivals[i].Driver.SetGoal(marker.Slot(i), hold: true);
                        rivals[i].OnLeg = true;
                        rivals[i].FinishOnArrival = finalLeg;
                        rivals[i].GoalId = current.RivalGoalId;
                        rivals[i].ArrivalMessage = null;
                    }
            }
            for (int k = 0; k < orders.Count;)
            {
                if (Time.time < orders[k].time) { k++; continue; }
                var order = orders[k].order;
                orders.RemoveAt(k);
                Apply(order);
            }
            foreach (var r in rivals)
            {
                // Through a via point: on to the next (a generous radius keeps the car rolling through it).
                if (r.Via.Count > 0 && r.Car != null && (r.Driver.AtGoal || Vector3.Distance(r.Car.Body.position, r.Via.Peek()) < 14f))
                {
                    r.Via.Dequeue();
                    if (r.Via.Count > 0) r.Driver.SetGoal(r.Via.Peek(), hold: false);
                    else r.Driver.SetGoal(r.Goal, r.HoldAtGoal);
                    continue;
                }
                if (r.Via.Count > 0 || !r.OnLeg || r.Finished || !r.Driver.AtGoal || world.Phase != MissionPhase.Running) continue;
                if (r.ArrivalMessage != null)
                {
                    world.Announce(r.ArrivalMessage, MessageTone.Info);
                    r.ArrivalMessage = null;
                }
                if (r.FinishOnArrival)
                {
                    r.FinishTime = Time.time - legStart;
                    world.Announce($"{r.Name} EXTRACTED", MessageTone.Warning);
                }
            }
            if (Time.time >= nextStandings)
            {
                nextStandings = Time.time + standingsInterval;
                UpdateStandings();
            }
        }

        private float RouteRemaining(VehicleController car)
        {
            if (car == null || car.Body == null || !raceTarget.HasValue || navigation == null) return float.MaxValue;
            return navigation.Plan(car.Body.position, raceTarget.Value, scratch) ? scratch.Length : Vector3.Distance(car.Body.position, raceTarget.Value) * 1.4f;
        }

        private void UpdateStandings()
        {
            standings.Clear();
            if (!RaceActive) return;
            player.Remaining = RouteRemaining(player.Car);
            standings.Add(player);
            foreach (var r in rivals)
            {
                r.Remaining = r.Finished ? 0f : RouteRemaining(r.Car);
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
                              $"yields {r.Driver.Yields}, lane holds {r.Driver.LaneHolds}, goal {r.GoalId ?? "-"}, state {r.Driver.State}, at goal {r.Driver.AtGoal}, finished {r.Finished}, remaining {(r.Finished ? 0f : r.Remaining):0} m");
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
