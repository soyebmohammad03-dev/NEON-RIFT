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
                car.Collided += c => { if (c.Other != null && c.Other.GetComponentInParent<VehicleController>() != null) racer.VehicleContacts++; };
                rivals.Add(racer);
                traffic.Add(car);
            }
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
            player.Car = world.Player;
            if (player.Car != null && !traffic.Contains(player.Car)) traffic.Add(player.Car);
            foreach (var r in rivals) r.Driver.PlayerCar = player.Car;
            player.FinishTime = -1f;
            foreach (var r in rivals) { r.FinishTime = -1f; r.Driver.Hold(); }
        }

        public void Unbind()
        {
            if (world != null) world.ObjectiveStarted -= OnObjectiveStarted;
            world = null;
            raceTarget = null;
        }

        private void OnObjectiveStarted(ObjectiveDefinition objective, Vector3? target, bool last)
        {
            current = objective;
            raceTarget = target;
            finalLeg = last;
            legStart = Time.time;
            foreach (var r in rivals) r.OnLeg = false;
            startAt = objective != null && !string.IsNullOrEmpty(objective.RivalGoalId) ? Time.time + objective.RivalStartDelay : -1f;
            if (startAt < 0f) foreach (var r in rivals) if (!r.Finished) r.Driver.Hold();
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
                    }
            }
            foreach (var r in rivals)
                if (finalLeg && r.OnLeg && !r.Finished && r.Driver.AtGoal && world.Phase == MissionPhase.Running)
                {
                    r.FinishTime = Time.time - legStart;
                    world.Announce($"{r.Name} EXTRACTED", MessageTone.Warning);
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
                              $"yields {r.Driver.Yields}, state {r.Driver.State}, at goal {r.Driver.AtGoal}, remaining {(r.Finished ? 0f : r.Remaining):0} m");
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
