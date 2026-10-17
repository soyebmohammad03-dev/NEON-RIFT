using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// Makes a scripted port vehicle (a HarborLife mover) read as a machine: wheels turn with the distance it covers,
    /// front wheels steer into its turns, brake lights come on as it slows or stands, the amber beacon flashes while it
    /// moves, and the cab sits down a little under braking. Pure presentation; the mover still owns the path.
    /// </summary>
    public sealed class HarborVehicleDressing : MonoBehaviour
    {
        [SerializeField] private Transform[] wheels = new Transform[0];
        [SerializeField] private Transform[] steered = new Transform[0];
        [SerializeField] private float wheelRadius = 0.5f;
        [SerializeField] private Renderer[] brakeLights = new Renderer[0];
        [SerializeField] private Renderer beacon;
        [SerializeField] private Transform body;

        private Vector3 last;
        private float speed, lastYaw, spin, steer, pitch;
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor"), BeaconColor = Shader.PropertyToID("_Color");

        public float Speed => speed;

        private void OnEnable() { last = transform.position; lastYaw = transform.eulerAngles.y; }

        private void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var p = transform.position;
            float moved = Vector3.Dot(p - last, transform.forward);
            float v = moved / dt;
            float accel = (v - speed) / dt;
            speed = Mathf.Lerp(speed, v, 1f - Mathf.Exp(-8f * dt));
            float yawRate = Mathf.DeltaAngle(lastYaw, transform.eulerAngles.y) / dt;
            last = p; lastYaw = transform.eulerAngles.y;
            spin += moved / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg;
            steer = Mathf.MoveTowards(steer, Mathf.Clamp(yawRate * 0.6f, -32f, 32f), 60f * dt);
            foreach (var w in wheels) if (w != null) w.localRotation = Quaternion.Euler(spin, 0f, 0f);
            foreach (var w in steered) if (w != null) w.localRotation = Quaternion.Euler(0f, steer, 0f);
            pitch = Mathf.Lerp(pitch, Mathf.Clamp(-accel * 0.25f, -1.2f, 1.2f), 1f - Mathf.Exp(-5f * dt));
            if (body != null) body.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            block ??= new MaterialPropertyBlock();
            bool braking = accel < -0.4f || Mathf.Abs(speed) < 0.3f;
            foreach (var r in brakeLights)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                var c = braking ? new Color(4.5f, 0.2f, 0.12f) : new Color(1.2f, 0.05f, 0.03f);
                block.SetColor(EmissionColor, c); block.SetColor(BeaconColor, c);
                r.SetPropertyBlock(block);
            }
            if (beacon != null)
            {
                bool on = Mathf.Abs(speed) > 0.3f && Mathf.Repeat(Time.time * 1.6f, 1f) < 0.45f;
                beacon.GetPropertyBlock(block);
                var c = on ? new Color(4f, 1.8f, 0.1f) : new Color(0.15f, 0.06f, 0f);
                block.SetColor(EmissionColor, c); block.SetColor(BeaconColor, c);
                beacon.SetPropertyBlock(block);
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform[] spinWheels, Transform[] steerPivots, float radius, Renderer[] brakes, Renderer amber, Transform cab)
        {
            wheels = spinWheels; steered = steerPivots; wheelRadius = radius; brakeLights = brakes; beacon = amber; body = cab;
        }
#endif
    }
}
