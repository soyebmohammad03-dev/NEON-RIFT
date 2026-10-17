using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The crew's transfer truck (tractor and skeletal trailer) parked in the restricted bay. After the gantry sets KX-7
    /// on its trailer, it waits until the player's car is well clear, then drives out through the north gate and away
    /// west along the quay road at a lorry's pace (kinematic, eased, turning along its path) while the police chase the
    /// player — and stops out of sight. Reset puts it back.
    /// </summary>
    public sealed class HarborMuleTruck : MonoBehaviour
    {
        [SerializeField] private Vector3[] path = new Vector3[0];
        [SerializeField] private float speed = 7f;
        [SerializeField] private Light[] headlights = new Light[0];
        [SerializeField] private Renderer[] beacons = new Renderer[0];

        private Vector3 startPos;
        private Quaternion startRot;
        private Transform cargo;
        private int next;
        private float v;
        private bool going;
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public bool Loaded => cargo != null;
        public bool Going => going;
        public bool Arrived { get; private set; }

        private void Awake() { startPos = transform.position; startRot = transform.rotation; }

        public void ResetTruck()
        {
            if (cargo != null) cargo.SetParent(null, true);
            cargo = null;
            going = Arrived = false;
            next = 0; v = 0f;
            transform.SetPositionAndRotation(startPos, startRot);
            foreach (var l in headlights) if (l != null) l.enabled = false;
        }

        /// <summary>The box has been set down on the trailer: it now travels with the truck.</summary>
        public void Load(Transform box)
        {
            cargo = box;
            if (box != null) box.SetParent(transform, true);
            foreach (var l in headlights) if (l != null) l.enabled = true;
        }

        public void Go() { if (!going && !Arrived) { going = true; next = 0; } }

        private void Update()
        {
            block ??= new MaterialPropertyBlock();
            bool flash = going && Mathf.Repeat(Time.time * 2f, 1f) < 0.5f;
            foreach (var b in beacons)
            {
                if (b == null) continue;
                b.GetPropertyBlock(block); block.SetColor(EmissionColor, flash ? new Color(4f, 1.8f, 0.1f) : new Color(0.2f, 0.08f, 0f)); b.SetPropertyBlock(block);
            }
            if (!going || next >= path.Length) return;
            float dt = Time.deltaTime;
            var p = transform.position;
            var to = path[next] - p; to.y = 0f;
            float remaining = to.magnitude;
            for (int i = next + 1; i < path.Length; i++) remaining += Vector3.Distance(path[i - 1], path[i]);
            float want = Mathf.Min(speed, Mathf.Sqrt(2f * 1.2f * remaining));
            v = Mathf.MoveTowards(v, want, 1.4f * dt);
            if (to.magnitude < 3f) { next++; if (next >= path.Length) { going = false; Arrived = true; } return; }
            // Drive like a vehicle: steer toward the next point at a lorry's turning radius (~8 m), move along the heading.
            var dir = to.normalized;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), Mathf.Rad2Deg * v / 8f * dt);
            transform.position = p + transform.forward * v * dt;
        }

#if UNITY_EDITOR
        public void EditorConfigure(Vector3[] route, Light[] lights, Renderer[] lamps) { path = route; headlights = lights; beacons = lamps; }
#endif
    }
}
