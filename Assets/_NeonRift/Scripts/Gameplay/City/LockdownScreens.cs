using System;
using System.Collections.Generic;
using NeonRift.Missions;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Billboards, bus-shelter adverts and street screens that the security grid takes over in a lockdown: each one
    /// switches to the warning material as the lockdown wave reaches it (distance from the theft / wave speed), and
    /// goes back to its advert when security calms.
    /// </summary>
    public sealed class LockdownScreens : MonoBehaviour, IMissionWorldComponent
    {
        [SerializeField] private Renderer[] screens = Array.Empty<Renderer>();
        [SerializeField] private Material warning;
        [SerializeField, Min(1f)] private float waveSpeed = 140f;
        [Tooltip("Grid anomaly: screens near the origin stutter between their advert and the warning.")]
        [SerializeField] private string[] glitchOn = Array.Empty<string>();
        [SerializeField, Min(10f)] private float glitchRadius = 420f;

        private Material[] original;
        private readonly List<(float time, int index)> pending = new();
        private MissionWorld world;
        private readonly List<int> glitching = new();
        private float nextGlitch;

        private void Awake()
        {
            original = new Material[screens.Length];
            for (int i = 0; i < screens.Length; i++) if (screens[i] != null) original[i] = screens[i].sharedMaterial;
            enabled = false;
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.SecurityChanged += OnSecurityChanged;
            world.EventRaised += OnWorldEvent;
            Restore();
        }

        private void OnWorldEvent(string eventId)
        {
            if (!MissionWorld.Matches(glitchOn, eventId) || world.Security == SecurityLevel.Lockdown) return;
            glitching.Clear();
            for (int i = 0; i < screens.Length; i++)
                if (screens[i] != null && Vector3.Distance(screens[i].bounds.center, world.AlertOrigin) < glitchRadius) glitching.Add(i);
            enabled = glitching.Count > 0;
        }

        public void Unbind()
        {
            if (world != null)
            {
                world.SecurityChanged -= OnSecurityChanged;
                world.EventRaised -= OnWorldEvent;
            }
            world = null;
        }

        private void OnSecurityChanged(SecurityLevel level)
        {
            if (level != SecurityLevel.Lockdown) { Restore(); return; }
            StopGlitch();
            pending.Clear();
            for (int i = 0; i < screens.Length; i++)
                if (screens[i] != null)
                    pending.Add((Time.time + Vector3.Distance(screens[i].bounds.center, world.AlertOrigin) / waveSpeed, i));
            enabled = true;
        }

        private void StopGlitch()
        {
            foreach (int i in glitching) if (screens[i] != null) screens[i].sharedMaterial = original[i];
            glitching.Clear();
        }

        private void Restore()
        {
            StopGlitch();
            pending.Clear();
            enabled = false;
            for (int i = 0; i < screens.Length; i++) if (screens[i] != null) screens[i].sharedMaterial = original[i];
        }

        private void Update()
        {
            if (glitching.Count > 0 && Time.time >= nextGlitch)
            {
                nextGlitch = Time.time + 0.09f;
                // A few screens at a time flip, so the stutter ripples rather than strobing in sync.
                for (int k = 0; k < glitching.Count; k++)
                {
                    int i = glitching[k];
                    if (screens[i] == null) continue;
                    float h = Mathf.Repeat(Mathf.Sin((Time.time * 7.1f + i * 12.9898f)) * 43758.5453f, 1f);
                    screens[i].sharedMaterial = h < 0.22f ? warning : original[i];
                }
                return;
            }
            for (int k = pending.Count - 1; k >= 0; k--)
            {
                if (Time.time < pending[k].time) continue;
                screens[pending[k].index].sharedMaterial = warning;
                pending.RemoveAt(k);
            }
            if (pending.Count == 0) enabled = false;
        }

#if UNITY_EDITOR
        public void EditorConfigure(Renderer[] screenRenderers, Material warningMaterial)
        {
            screens = screenRenderers ?? Array.Empty<Renderer>();
            warning = warningMaterial;
        }

        public void EditorConfigureEvents(string[] glitchEvents) => glitchOn = glitchEvents ?? Array.Empty<string>();
#endif
    }
}
