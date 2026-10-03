using UnityEngine;

namespace NeonRift.Vehicles
{
    /// <summary>
    /// Real lights for a car at night, placed from its <see cref="VehicleRig"/>: two headlight spots (the player's
    /// left one casts shadows), and a red tail light that brightens under braking and turns white in reverse.
    /// AI cars use cheaper settings (no shadows, shorter range). Reads the controller's input; changes nothing else.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleLights : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float headlightIntensity = 260f;
        [SerializeField, Min(1f)] private float headlightRange = 70f;
        [SerializeField, Range(10f, 120f)] private float headlightAngle = 58f;
        [SerializeField] private Color headlightColour = new(0.92f, 0.95f, 1f);
        [SerializeField, Min(0f)] private float tailIntensity = 1.2f;
        [SerializeField, Min(0f)] private float brakeIntensity = 5f;

        private Light[] heads = new Light[0];
        private Light tail;
        private VehicleController controller;

        /// <summary>Creates the lights. <paramref name="detailed"/> = player car (shadows, full range).</summary>
        public void Build(bool detailed)
        {
            controller = GetComponent<VehicleController>();
            var rig = GetComponent<VehicleRig>();
            Vector3 size = rig != null ? rig.Dimensions : new Vector3(2f, 1.3f, 4.6f);
            foreach (var l in heads) if (l != null) Destroy(l.gameObject);
            if (tail != null) Destroy(tail.gameObject);

            heads = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var l = Make($"Headlight{i}", new Vector3(side * size.x * 0.33f, size.y * 0.45f, size.z * 0.5f - 0.15f), Quaternion.Euler(4f, side * 2f, 0f));
                l.type = LightType.Spot;
                l.color = headlightColour;
                l.intensity = detailed ? headlightIntensity : headlightIntensity * 0.6f;
                l.range = detailed ? headlightRange : headlightRange * 0.6f;
                l.spotAngle = headlightAngle;
                l.innerSpotAngle = headlightAngle * 0.45f;
                l.shadows = detailed && i == 0 ? LightShadows.Soft : LightShadows.None;
                heads[i] = l;
            }
            tail = Make("TailLight", new Vector3(0f, size.y * 0.5f, -size.z * 0.5f - 0.3f), Quaternion.identity);
            tail.type = LightType.Point;
            tail.color = new Color(1f, 0.05f, 0.05f);
            tail.range = detailed ? 7f : 5f;
            tail.intensity = tailIntensity;
            tail.shadows = LightShadows.None;
        }

        /// <summary>Switches the headlight beams (cinematics: the "lights on" moment). Tail light is unaffected.</summary>
        public void SetHeadlights(bool on)
        {
            foreach (var l in heads) if (l != null) l.enabled = on;
        }

        private Light Make(string name, Vector3 local, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.SetLocalPositionAndRotation(local, rotation);
            return go.AddComponent<Light>();
        }

        private void Update()
        {
            if (tail == null || controller == null) return;
            var t = controller.Telemetry;
            bool reversing = t.Gear < 0;
            tail.color = reversing ? new Color(1f, 0.92f, 0.85f) : new Color(1f, 0.05f, 0.05f);
            float target = reversing ? brakeIntensity * 0.6f : t.BrakeInput > 0.05f || t.Handbrake ? brakeIntensity : tailIntensity;
            tail.intensity = Mathf.MoveTowards(tail.intensity, target, 40f * Time.deltaTime);
        }
    }
}
