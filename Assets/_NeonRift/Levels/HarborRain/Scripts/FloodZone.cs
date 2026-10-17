using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// A predefined stretch of road that floods during the escape. No fluid simulation: a water surface rises to a
    /// target level, a pre-built low-grip <see cref="NeonRift.Vehicles.DrivingSurface"/> collider (3 cm above the
    /// road, so the wheels read it) switches on, and a vehicle in deep water is slowed by a drag force. A deep zone
    /// can close: barricades at its ends switch on (only while the player is not inside, so nobody is trapped).
    /// </summary>
    public sealed class FloodZone : MonoBehaviour
    {
        [SerializeField] private string label = "ROAD";
        [Tooltip("Ground-plane area of the zone (local XZ, centred on this transform).")]
        [SerializeField] private Vector2 size = new(40f, 14f);
        [SerializeField] private Transform water;
        [SerializeField] private Renderer waterRenderer;
        [Tooltip("Low-grip surface collider, off until the zone floods.")]
        [SerializeField] private Collider wetSurface;
        [Tooltip("Barricades that close a deep zone (off until closure).")]
        [SerializeField] private GameObject closure;
        public Collider[] ClosureColliders => closure != null ? closure.GetComponentsInChildren<Collider>(true) : new Collider[0];
        [Tooltip("Amber beacons that flash while the zone is flooding.")]
        [SerializeField] private Renderer[] beacons = new Renderer[0];
        [Tooltip("Rushing-water loop, louder as the water rises (optional).")]
        [SerializeField] private AudioSource rush;
        [SerializeField] private float shallowLevel = 0.12f;
        [SerializeField] private float deepLevel = 0.5f;
        [Tooltip("Horizontal drag in deep water, 1/s (fraction of speed removed per second at full depth).")]
        [SerializeField] private float deepDrag = 0.9f;

        public string Label => label;
        public bool Flooding { get; private set; }
        public bool Deep { get; private set; }
        public bool Closed { get; private set; }
        public float Level { get; private set; }
        public float DeepLevel => deepLevel;
        /// <summary>World-space ground rectangle (zones are axis-aligned), for the minimap.</summary>
        public Rect Area => new(transform.position.x - size.x * 0.5f, transform.position.z - size.y * 0.5f, size.x, size.y);
        /// <summary>Water deep enough to slow a car hard.</summary>
        public bool Dangerous => Level >= 0.25f;

        private float targetLevel, riseRate;
        private bool closePending;
        private MaterialPropertyBlock block;
        private Vector2 scroll;
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private void Awake() => ResetZone();

        public void ResetZone()
        {
            Flooding = Deep = Closed = closePending = false;
            Level = targetLevel = 0f;
            if (wetSurface != null) wetSurface.enabled = false;
            if (closure != null) closure.SetActive(false);
            SetWater(0f);
            SetBeacons(false);
        }

        /// <summary>Starts flooding: shallow water over <paramref name="seconds"/>, or deep water that then closes the road.</summary>
        public void Flood(bool deep, float seconds)
        {
            Flooding = true;
            Deep |= deep;
            targetLevel = Deep ? deepLevel : shallowLevel;
            riseRate = Mathf.Max(0.01f, (targetLevel - Level) / Mathf.Max(0.1f, seconds));
            if (wetSurface != null) wetSurface.enabled = true;
            if (Deep) closePending = true;
        }

        public bool Contains(Vector3 world)
        {
            var local = transform.InverseTransformPoint(world);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f;
        }

        /// <summary>Slows a body driving through deep water (called by the mission for the player's vehicle).</summary>
        public void ApplyDrag(Rigidbody body, float dt)
        {
            if (body == null || Level < 0.2f || !Contains(body.position)) return;
            float depth = Mathf.InverseLerp(0.2f, deepLevel, Level);
            var v = body.linearVelocity;
            var horizontal = new Vector3(v.x, 0f, v.z);
            body.AddForce(-horizontal * (deepDrag * depth), ForceMode.Acceleration);
        }

        public void Tick(float dt, Vector3 playerPosition)
        {
            if (Flooding && Level < targetLevel) SetWater(Mathf.MoveTowards(Level, targetLevel, riseRate * dt));
            if (closePending && Level >= deepLevel * 0.9f && !Contains(playerPosition))
            {
                closePending = false;
                Closed = true;
                if (closure != null) closure.SetActive(true);
            }
            SetBeacons(Flooding && Mathf.Repeat(Time.time * (Deep ? 2.4f : 1.4f), 1f) < 0.5f);
            if (waterRenderer != null && Level > 0f)
            {
                scroll += new Vector2(0.03f, 0.05f) * dt;
                block ??= new MaterialPropertyBlock();
                waterRenderer.GetPropertyBlock(block);
                block.SetVector(BaseMapSt, new Vector4(size.x / 6f, size.y / 6f, scroll.x, scroll.y));
                waterRenderer.SetPropertyBlock(block);
            }
        }

        private void SetWater(float level)
        {
            Level = level;
            if (rush != null)
            {
                rush.volume = Mathf.Clamp01(level / Mathf.Max(0.05f, deepLevel)) * 0.8f;
                if (level > 0.01f && !rush.isPlaying) rush.Play();
                else if (level <= 0.01f && rush.isPlaying) rush.Stop();
            }
            if (water == null) return;
            water.gameObject.SetActive(level > 0.005f);
            var p = water.localPosition;
            water.localPosition = new Vector3(p.x, level, p.z);
        }

        private void SetBeacons(bool on)
        {
            block ??= new MaterialPropertyBlock();
            foreach (var r in beacons)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionColor, on ? new Color(14f, 6f, 0.4f) : new Color(0.3f, 0.12f, 0f));
                r.SetPropertyBlock(block);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(new Vector3(0f, 0.25f, 0f), new Vector3(size.x, 0.5f, size.y));
        }
    }
}
