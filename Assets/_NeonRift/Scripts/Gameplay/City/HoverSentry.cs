using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// A large sentry drone holding station over the Data Core compound: a slow bob and a slow scanning yaw. Pure
    /// presentation (always on, intro and missions); one transform per sentry.
    /// </summary>
    public sealed class HoverSentry : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float bob = 0.6f;
        [SerializeField, Min(0.1f)] private float bobPeriod = 4.5f;
        [Tooltip("Scan sweep either side of the start heading, degrees, and its period, s.")]
        [SerializeField] private Vector2 scan = new(35f, 14f);

        private Vector3 home;
        private float yaw, phase;

        private void Awake()
        {
            home = transform.position;
            yaw = transform.eulerAngles.y;
            phase = (home.x * 0.37f + home.z * 0.11f) % 6.28f;
        }

        private void Update()
        {
            float t = Time.time + phase;
            transform.SetPositionAndRotation(home + Vector3.up * (Mathf.Sin(t * Mathf.PI * 2f / bobPeriod) * bob),
                Quaternion.Euler(0f, yaw + Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.1f, scan.y)) * scan.x, 0f));
        }
    }
}
