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

        private Material[] original;
        private readonly List<(float time, int index)> pending = new();
        private MissionWorld world;

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
            Restore();
        }

        public void Unbind()
        {
            if (world != null) world.SecurityChanged -= OnSecurityChanged;
            world = null;
        }

        private void OnSecurityChanged(SecurityLevel level)
        {
            if (level != SecurityLevel.Lockdown) { Restore(); return; }
            pending.Clear();
            for (int i = 0; i < screens.Length; i++)
                if (screens[i] != null)
                    pending.Add((Time.time + Vector3.Distance(screens[i].bounds.center, world.AlertOrigin) / waveSpeed, i));
            enabled = true;
        }

        private void Restore()
        {
            pending.Clear();
            enabled = false;
            for (int i = 0; i < screens.Length; i++) if (screens[i] != null) screens[i].sharedMaterial = original[i];
        }

        private void Update()
        {
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
#endif
    }
}
