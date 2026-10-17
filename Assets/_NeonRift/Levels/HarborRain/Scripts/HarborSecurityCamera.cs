using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Pole-mounted surveillance camera: the head sweeps, and a target inside its range and field of view with a clear
    /// line of sight is "seen" (the LED turns red). Restricted-area cameras go offline when the security gate is
    /// bypassed. Harbor Rain only; Night Run's <c>SecurityCamera</c> is bound to its own mission world.
    /// </summary>
    public sealed class HarborSecurityCamera : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Renderer led;
        [SerializeField] private bool restricted = true;
        [SerializeField] private float range = 42f;
        [SerializeField] private float fieldOfView = 34f;
        [SerializeField] private float sweep = 55f;
        [SerializeField] private float period = 8f;
        [SerializeField] private LayerMask blocking;

        public bool Restricted => restricted;
        public bool Offline { get; private set; }
        public bool Seeing { get; private set; }

        private float baseYaw, phase;
        private bool tracking, reacting;
        private Vector3 reactTarget;
        private Vector3 trackTarget;
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            if (head != null) baseYaw = head.eulerAngles.y;
            phase = (transform.position.x * 0.13f + transform.position.z * 0.07f) % 6.28f;
        }

        public void ResetCamera() { Offline = false; Seeing = false; tracking = false; reacting = false; SetLed(); }

        /// <summary>A looped (offline) camera still turns: it swings onto the crane job with a slow amber LED (the feed shows a loop).</summary>
        public void React(bool on, Vector3 target) { reacting = on; reactTarget = target; }
        public bool Reacting => reacting;

        public void SetOffline(bool offline) { Offline = offline; Seeing = false; SetLed(); }

        /// <summary>Points the head at <paramref name="target"/> when it is in range (after the theft every camera follows the car).</summary>
        public void Track(bool on, Vector3 target) { tracking = on; trackTarget = target; }

        /// <summary>True when the target is in range, inside the view cone and not hidden behind a container or wall.</summary>
        public bool Check(Vector3 target)
        {
            Seeing = false;
            if (Offline || head == null) { SetLed(); return false; }
            Vector3 eye = head.position;
            Vector3 to = target + Vector3.up * 0.8f - eye;
            if (to.magnitude < range && Vector3.Angle(head.forward, to) < fieldOfView * 0.5f)
                Seeing = !Physics.Raycast(eye, to.normalized, to.magnitude - 2.5f, blocking, QueryTriggerInteraction.Ignore);
            SetLed();
            return Seeing;
        }

        private void Update()
        {
            if (head == null) return;
            if (Offline)
            {
                if (!reacting) return;
                var to = reactTarget - head.position;
                if (to.sqrMagnitude > 1f) head.rotation = Quaternion.Slerp(head.rotation, Quaternion.LookRotation(to.normalized), 1f - Mathf.Exp(-1.5f * Time.deltaTime));
                block ??= new MaterialPropertyBlock();
                if (led != null)
                {
                    led.GetPropertyBlock(block);
                    block.SetColor(EmissionColor, Mathf.Repeat(Time.time * 0.9f, 1f) < 0.5f ? new Color(3f, 1.4f, 0.1f) : new Color(0.05f, 0.02f, 0f));
                    led.SetPropertyBlock(block);
                }
                return;
            }
            Quaternion want;
            var flat = trackTarget - head.position;
            if (tracking && flat.magnitude < range * 1.4f)
                want = Quaternion.LookRotation(flat.normalized);
            else
                want = Quaternion.Euler(28f, baseYaw + Mathf.Sin((Time.time + phase) * Mathf.PI * 2f / period) * sweep, 0f);
            head.rotation = Quaternion.Slerp(head.rotation, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        private void SetLed()
        {
            if (led == null) return;
            block ??= new MaterialPropertyBlock();
            led.GetPropertyBlock(block);
            block.SetColor(EmissionColor, Offline ? new Color(0.02f, 0.02f, 0.02f) : Seeing ? new Color(8f, 0.3f, 0.2f) : new Color(0.2f, 2.5f, 0.8f));
            led.SetPropertyBlock(block);
        }
    }
}
