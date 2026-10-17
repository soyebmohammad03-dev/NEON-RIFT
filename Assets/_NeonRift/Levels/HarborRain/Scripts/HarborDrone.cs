using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Security drone (the project's licensed drone model under this transform): orbits a patrol point with a
    /// searchlight before the theft, spots a car under it, and after the theft chases the player at altitude, lighting
    /// the car. Kinematic movement only (no physics), one spotlight each.
    /// </summary>
    public sealed class HarborDrone : MonoBehaviour
    {
        public enum Mode { Patrol, Chase, Offline }

        [SerializeField] private Light searchlight;
        [SerializeField] private Vector3 patrolCentre;
        [SerializeField] private float patrolRadius = 45f;
        [SerializeField] private float patrolHeight = 22f;
        [SerializeField] private float patrolSpeed = 9f;
        [SerializeField] private float chaseHeight = 15f;
        [SerializeField] private float chaseSpeed = 34f;
        [SerializeField] private float spotRadius = 13f;

        public Mode Current { get; private set; }
        /// <summary>The searchlight is on the player (spotted on patrol, or locked during a chase).</summary>
        public bool Locked { get; private set; }

        private float angle;
        private Vector3 velocity;
        private Transform target;

        private void Awake() => ResetDrone();

        public void ResetDrone()
        {
            angle = (patrolCentre.x + patrolCentre.z) * 0.01f + transform.GetSiblingIndex();
            transform.position = PatrolPoint();
            velocity = Vector3.zero;
            SetMode(Mode.Patrol, null);
        }

        public void SetMode(Mode mode, Transform chaseTarget)
        {
            Current = mode;
            target = chaseTarget;
            Locked = false;
            if (searchlight != null)
            {
                searchlight.enabled = mode != Mode.Offline;
                searchlight.color = mode == Mode.Chase ? new Color(1f, 0.35f, 0.3f) : new Color(0.75f, 0.9f, 1f);
            }
        }

        /// <summary>Patrol: is <paramref name="position"/> inside the searchlight's ground circle?</summary>
        public bool Spots(Vector3 position)
        {
            if (Current != Mode.Patrol) return false;
            var d = position - transform.position;
            d.y = 0f;
            return Locked = d.magnitude < spotRadius;
        }

        private Vector3 PatrolPoint() =>
            patrolCentre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * patrolRadius + Vector3.up * patrolHeight;

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 goal;
            float speed;
            if (Current == Mode.Chase && target != null)
            {
                goal = target.position + Vector3.up * chaseHeight - target.forward * 6f;
                speed = chaseSpeed;
                var flat = target.position - transform.position;
                flat.y = 0f;
                Locked = flat.magnitude < 22f;
            }
            else if (Current == Mode.Patrol)
            {
                angle += patrolSpeed / Mathf.Max(5f, patrolRadius) * dt;
                goal = PatrolPoint();
                speed = patrolSpeed * 2f;
            }
            else
            {
                goal = transform.position;
                speed = 0f;
            }
            // Critically damped approach with a top speed: smooth, never overshoots into geometry.
            var desired = Vector3.ClampMagnitude((goal - transform.position) * 1.6f, speed);
            velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-3f * dt));
            transform.position += velocity * dt;
            var heading = new Vector3(velocity.x, 0f, velocity.z);
            if (heading.sqrMagnitude > 0.5f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(heading) * Quaternion.Euler(Mathf.Clamp(heading.magnitude, 0f, 15f), 0f, 0f), 1f - Mathf.Exp(-4f * dt));
            if (searchlight != null && Current != Mode.Offline)
            {
                Vector3 look = Current == Mode.Chase && target != null ? target.position : transform.position + Vector3.down * 10f + velocity * 0.4f;
                searchlight.transform.rotation = Quaternion.LookRotation(look - searchlight.transform.position);
            }
        }
    }
}
