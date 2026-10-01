using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.Frontend
{
    /// <summary>
    /// Presents one vehicle on a turntable. Instances are presentation-only: physics is frozen,
    /// colliders disabled, and the model is grounded and centred on the turntable.
    /// </summary>
    public sealed class VehicleShowroom : MonoBehaviour
    {
        [SerializeField] private Transform turntable;
        [Tooltip("Degrees per second while idle.")]
        [SerializeField] private float idleSpinSpeed = 12f;
        [Tooltip("Degrees per pixel dragged (scaled by 1080p reference height).")]
        [SerializeField] private float dragSensitivity = 0.35f;
        [Tooltip("Degrees per second at full stick/key deflection.")]
        [SerializeField] private float axisSpinSpeed = 120f;
        [Tooltip("How quickly drag momentum decays (per second).")]
        [SerializeField] private float momentumDamping = 4f;
        [Tooltip("Seconds after user input before idle spin resumes.")]
        [SerializeField] private float idleResumeDelay = 2.5f;

        private GameObject current;
        private float angularVelocity;
        private float lastUserInputTime = float.NegativeInfinity;

        public void Show(VehicleDefinition definition)
        {
            if (current != null) Destroy(current);
            current = null;
            if (definition == null || definition.ShowroomPrefab == null) return;

            current = Instantiate(definition.ShowroomPrefab, turntable);
            current.name = $"Showroom_{definition.Id}";
            MakePresentationOnly(current);
            CentreOnTurntable(current.transform);
        }

        /// <summary>Pointer drag in screen pixels.</summary>
        public void Drag(float deltaPixels)
        {
            float degrees = -deltaPixels * dragSensitivity * (1080f / Mathf.Max(1, Screen.height));
            turntable.Rotate(0f, degrees, 0f, Space.World);
            angularVelocity = degrees / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            lastUserInputTime = Time.unscaledTime;
        }

        /// <summary>Continuous axis input (-1..1) from keyboard or gamepad.</summary>
        public void Spin(float axis)
        {
            if (Mathf.Abs(axis) < 0.05f) return;
            angularVelocity = -axis * axisSpinSpeed;
            lastUserInputTime = Time.unscaledTime;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool idle = Time.unscaledTime - lastUserInputTime > idleResumeDelay;
            float target = idle ? idleSpinSpeed : 0f;
            angularVelocity = Mathf.Lerp(angularVelocity, target, 1f - Mathf.Exp(-momentumDamping * dt));
            turntable.Rotate(0f, angularVelocity * dt, 0f, Space.World);
        }

        private static void MakePresentationOnly(GameObject instance)
        {
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
        }

        private void CentreOnTurntable(Transform model)
        {
            var renderers = System.Array.FindAll(model.GetComponentsInChildren<Renderer>(), r => r.enabled);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            var offset = turntable.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.position += offset;
        }
    }
}
