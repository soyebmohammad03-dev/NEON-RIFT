using NeonRift.Missions;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.Gameplay
{
    /// <summary>Blends a lockdown grade (red push, heavier vignette) over the base night grade when security escalates.</summary>
    public sealed class SecurityPostEffects : MonoBehaviour, IMissionWorldComponent
    {
        [SerializeField] private Volume alertVolume;
        [SerializeField, Range(0f, 1f)] private float alertWeight = 0.25f;
        [SerializeField] private Volume lockdownVolume;
        [SerializeField, Min(0.05f)] private float blendSeconds = 1.2f;
        [Tooltip("Delay before the lockdown grade starts (lets the detection beat land first), s.")]
        [SerializeField, Min(0f)] private float lockdownDelay = 0.6f;

        private MissionWorld world;
        private float alertTarget, lockdownTarget, wait;

        private void Awake()
        {
            if (alertVolume != null) alertVolume.weight = 0f;
            if (lockdownVolume != null) lockdownVolume.weight = 0f;
            enabled = false;
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.SecurityChanged += OnSecurityChanged;
            OnSecurityChanged(world.Security);
        }

        public void Unbind()
        {
            if (world != null) world.SecurityChanged -= OnSecurityChanged;
            world = null;
        }

        private void OnSecurityChanged(SecurityLevel level)
        {
            alertTarget = level == SecurityLevel.Alert ? alertWeight : 0f;
            lockdownTarget = level == SecurityLevel.Lockdown ? 1f : 0f;
            wait = level == SecurityLevel.Lockdown ? lockdownDelay : 0f;
            enabled = true;
        }

        private void Update()
        {
            if (wait > 0f) { wait -= Time.deltaTime; return; }
            float step = Time.deltaTime / blendSeconds;
            bool done = Step(alertVolume, alertTarget, step) & Step(lockdownVolume, lockdownTarget, step);
            if (done) enabled = false;
        }

        private static bool Step(Volume v, float target, float step)
        {
            if (v == null) return true;
            v.weight = Mathf.MoveTowards(v.weight, target, step);
            return Mathf.Approximately(v.weight, target);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Volume alert, Volume lockdown)
        {
            alertVolume = alert;
            lockdownVolume = lockdown;
        }
#endif
    }
}
