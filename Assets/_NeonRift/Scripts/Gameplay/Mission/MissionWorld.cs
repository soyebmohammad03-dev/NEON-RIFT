using System;
using System.Collections.Generic;
using NeonRift.Missions;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// What scene objects can see of a running mission: the player vehicle, security state, world events and
    /// HUD countdowns. Created by the <see cref="MissionDirector"/> per attempt and handed to every
    /// <see cref="IMissionWorldComponent"/>, so mechanics talk through event ids instead of references to each other.
    /// </summary>
    public sealed class MissionWorld
    {
        private readonly MissionProgress progress;
        private readonly List<(object owner, string label, float endTime)> countdowns = new();

        public VehicleController Player { get; }
        public Rigidbody PlayerBody => Player != null ? Player.Body : null;
        public SecurityLevel Security => progress.Security;
        public float Heat => progress.Heat;
        public MissionPhase Phase => progress.Phase;
        /// <summary>Where a lockdown starts spreading from (the theft location), world space.</summary>
        public Vector3 AlertOrigin { get; set; }

        /// <summary>A world event id was raised (by mission data or by a scene object).</summary>
        public event Action<string> EventRaised;
        public event Action<SecurityLevel> SecurityChanged;
        public event Action<string, MessageTone> Announced;
        internal event Action<MissionZone> ZoneEntered;
        internal event Action<Interactable> InteractionCompleted;

        public MissionWorld(MissionProgress progress, VehicleController player)
        {
            this.progress = progress ?? throw new ArgumentNullException(nameof(progress));
            Player = player;
        }

        public bool IsPlayer(Collider other) =>
            other != null && PlayerBody != null && other.attachedRigidbody == PlayerBody;

        public void Raise(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;
            Debug.Log($"[Mission] event '{eventId}'");
            EventRaised?.Invoke(eventId);
        }

        public void Announce(string text, MessageTone tone) => Announced?.Invoke(text, tone);

        public void AddHeat(float amount, string reason) => progress.AddHeat(amount, reason);

        /// <summary>True if the current objective is waiting for <paramref name="targetId"/>.</summary>
        public bool IsObjectiveTarget(string targetId) => progress.IsCurrentTarget(targetId);

        /// <summary>True if any objective of the mission uses <paramref name="targetId"/>.</summary>
        public bool IsMissionTarget(string targetId)
        {
            foreach (var o in progress.Definition.Objectives)
                if (o != null && o.TargetId == targetId) return true;
            return false;
        }

        /// <summary>Shows "<paramref name="label"/> 12s" on the HUD until <paramref name="endTime"/> (Time.time) or until cleared.</summary>
        public void SetCountdown(object owner, string label, float endTime)
        {
            ClearCountdown(owner);
            countdowns.Add((owner, label, endTime));
        }

        public void ClearCountdown(object owner) => countdowns.RemoveAll(c => c.owner == owner);

        /// <summary>The countdown that ends first, if any.</summary>
        public bool TryGetNextCountdown(out string label, out float remaining)
        {
            label = null;
            remaining = float.MaxValue;
            foreach (var c in countdowns)
            {
                float r = c.endTime - Time.time;
                if (r >= 0f && r < remaining) { remaining = r; label = c.label; }
            }
            return label != null;
        }

        internal void NotifySecurityChanged(SecurityLevel level) => SecurityChanged?.Invoke(level);
        internal void NotifyZoneEntered(MissionZone zone) => ZoneEntered?.Invoke(zone);
        internal void NotifyInteractionCompleted(Interactable interactable) => InteractionCompleted?.Invoke(interactable);

        /// <summary>True if <paramref name="eventId"/> is in <paramref name="list"/>.</summary>
        public static bool Matches(string[] list, string eventId)
        {
            if (list == null) return false;
            foreach (var e in list) if (e == eventId) return true;
            return false;
        }
    }
}
