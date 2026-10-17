using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// A tower searchlight: sweeps a sector while the port is calm; on <see cref="Track"/> it slews (at a motor's pace,
    /// never snapping) to hold a target — the lifted container at the theft, then the player's car — and goes back to
    /// sweeping on <see cref="Sweep"/>. The head yaws, the aim child pitches.
    /// </summary>
    public sealed class HarborSearchlight : MonoBehaviour
    {
        [SerializeField] private Transform aim;
        [SerializeField] private float sweepDegreesPerSecond = 16f;
        [SerializeField] private float slewDegreesPerSecond = 55f;
        [SerializeField] private float restPitch = 24f;

        private Transform target;
        private Vector3 targetPoint;
        private bool tracking;
        private float yaw, pitch;

        public bool Tracking => tracking;
        /// <summary>Angle between the beam and the target, degrees (0 = on target).</summary>
        public float Error { get; private set; } = 180f;

        private void Awake() { yaw = transform.eulerAngles.y; pitch = restPitch; }

        public void Track(Transform t, Vector3 fallback) { target = t; targetPoint = fallback; tracking = true; }
        public void Sweep() { tracking = false; target = null; }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (tracking)
            {
                var p = target != null ? target.position : targetPoint;
                var d = p - transform.position;
                float wantYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float wantPitch = Mathf.Clamp(Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -10f, 70f);
                yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, slewDegreesPerSecond * dt);
                pitch = Mathf.MoveTowards(pitch, wantPitch, slewDegreesPerSecond * 0.6f * dt);
                Error = Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) + Mathf.Abs(pitch - wantPitch);
            }
            else
            {
                yaw += sweepDegreesPerSecond * dt;
                pitch = Mathf.MoveTowards(pitch, restPitch + Mathf.Sin(Time.time * 0.31f) * 6f, 10f * dt);
                Error = 180f;
            }
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (aim != null) aim.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform aimChild, float sweep) { aim = aimChild; sweepDegreesPerSecond = sweep; }
#endif
    }
}
