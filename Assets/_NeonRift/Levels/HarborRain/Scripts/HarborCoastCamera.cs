using NeonRift.Gameplay;
using Unity.Cinemachine;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Harbor Rain's framing on the sea road, layered on top of the shared chase camera (which is not modified): runs
    /// after <see cref="VehicleChaseCamera"/> and before the Cinemachine brain, and offsets the pose it produced.
    /// On the coast road the camera rides higher and a little inland so the sea shows over the seawall beside the road.
    /// When a set is coming in nearby it eases wider, higher and round toward the sea to put the swell, the breaking
    /// crest and the car in one frame, then eases back after the impact. The player keeps full control throughout, and
    /// the car stays in frame (the aim point never moves more than ~40 % of the way to the wave).
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public sealed class HarborCoastCamera : MonoBehaviour
    {
        [SerializeField] private HarborWaves waves;
        [Tooltip("Coast road area (x, z) where the sea framing applies.")]
        [SerializeField] private Rect coastArea = new(884f, 255f, 1040f, 46f);
        [Tooltip("Further sea roads (the headland and the pier) and the direction of the open sea from each.")]
        [SerializeField] private Rect[] seaRoads = { new(1890f, 290f, 36f, 250f), new(1918f, 536f, 380f, 34f) };
        [SerializeField] private Vector3[] seaSides = { Vector3.left, Vector3.forward };
        [Header("Sea road framing")]
        [SerializeField] private float rise = 1.6f;
        [SerializeField] private float pullBack = 1.6f;
        [Tooltip("Degrees the view swings from down-the-road toward the sea on the coast road.")]
        [SerializeField] private float seaSwing = 9f;
        [Header("Wave reveal")]
        [SerializeField] private float revealRise = 2.4f;
        [SerializeField] private float revealPullBack = 3.5f;
        [Tooltip("Fraction of the angle to the wave the view swings through at full reveal (capped at 55°).")]
        [SerializeField, Range(0f, 0.8f)] private float revealSwing = 0.65f;
        [SerializeField] private float revealRange = 240f;
        [Tooltip("Lens zoom at full reveal (field of view multiplier): the set reads bigger without moving closer.")]
        [SerializeField, Range(0.6f, 1f)] private float revealZoom = 0.86f;

        private float coast, reveal;
        private Vector3 seaSide = Vector3.forward;
        private Vector3 wavePoint;

        /// <summary>0..1: how much of the wave reveal is applied (validation / tour).</summary>
        public float Reveal => reveal;
        public float Coast => coast;

        private void LateUpdate()
        {
            var entry = HarborRainEntry.Active;
            var chase = entry != null && entry.Driving != null ? entry.Driving.ChaseCamera : null;
            if (chase == null || chase.Target == null) return;
            var car = chase.Target.transform.position;
            float dt = Time.deltaTime;
            // Directed beats (theft, extraction framing) own the camera: stand aside.
            bool free = !chase.InBeat && chase.FramingWeight <= 0.01f;
            bool onCoast = free && coastArea.Contains(new Vector2(car.x, car.z));
            for (int i = 0; i < seaRoads.Length && !onCoast; i++)
                if (free && seaRoads[i].Contains(new Vector2(car.x, car.z))) { onCoast = true; seaSide = i < seaSides.Length ? seaSides[i] : Vector3.forward; }
            if (free && coastArea.Contains(new Vector2(car.x, car.z))) seaSide = Vector3.forward;
            coast = Mathf.MoveTowards(coast, onCoast ? 1f : 0f, dt / 1.2f);

            // The nearest live wave in front of / beside the car.
            float want = 0f;
            if (onCoast && waves != null)
            {
                foreach (var s in waves.Sections)
                {
                    bool live = s.state == HarborWaves.State.Building && s.t > 1.5f || s.state is HarborWaves.State.Approach or HarborWaves.State.Impact
                                || s.state == HarborWaves.State.Washing && s.t < 1.2f;
                    if (!live || s.breaker == null || !s.breaker.Visible) continue;
                    float d = Mathf.Abs(s.road.center.x - car.x);
                    if (d > revealRange) continue;
                    float w = Mathf.InverseLerp(revealRange, revealRange * 0.5f, d);
                    if (w <= want) continue;
                    want = w;
                    var crest = s.breaker.Point(0f, HarborBreaker.CrestS);
                    // Aim at the crest's base so the face and the wall both read; never above 10 m.
                    wavePoint = new Vector3(Mathf.Lerp(car.x, crest.x, 0.8f), Mathf.Min(crest.y * 0.6f, 10f), crest.z);
                }
            }
            reveal = Mathf.MoveTowards(reveal, want, dt / (want > reveal ? 0.9f : 1.4f));
            float c = Smooth(coast), r = Smooth(reveal) * c;
            if (c <= 0.001f) return;

            // Rebuild the pose as an orbit around the car: swing the view direction from straight down the road toward
            // the sea (a little on the coast road, up to half-way to the wave during a reveal), stand back along it and
            // rise. The car stays on the line of sight, so it never leaves the frame.
            var t = chase.transform;
            Vector3 flatBack = Vector3.ProjectOnPlane(t.position - car, Vector3.up);
            float dist = Mathf.Max(3f, flatBack.magnitude);
            Vector3 road = flatBack.sqrMagnitude > 0.01f ? -flatBack.normalized : Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            float height = t.position.y - car.y;
            Vector3 toWave = Vector3.ProjectOnPlane(wavePoint - car, Vector3.up);
            Vector3 view = Vector3.RotateTowards(road, seaSide, seaSwing * Mathf.Deg2Rad * c, 0f);
            if (r > 0.001f && toWave.sqrMagnitude > 1f)
            {
                float angle = Vector3.Angle(road, toWave.normalized) * revealSwing;
                view = Vector3.RotateTowards(view, toWave.normalized, Mathf.Min(angle, 55f) * Mathf.Deg2Rad * r, 0f);
            }
            Vector3 pos = car - view * (dist + pullBack * c + revealPullBack * r) + Vector3.up * (height + rise * c + revealRise * r);
            Vector3 look = car + view * (9f + 5f * r) + Vector3.up * (1f + 0.8f * r);
            // Blend from the chase pose so entering / leaving the coast is seamless.
            pos = Vector3.Lerp(t.position, pos, c);
            look = Vector3.Lerp(t.position + t.forward * 12f, look, c);
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            if (r > 0.001f && t.TryGetComponent(out CinemachineCamera vcam))
            {
                var lens = vcam.Lens;
                lens.FieldOfView *= Mathf.Lerp(1f, revealZoom, r);
                vcam.Lens = lens;
            }
        }

        private static float Smooth(float x) => x * x * (3f - 2f * x);
    }
}
