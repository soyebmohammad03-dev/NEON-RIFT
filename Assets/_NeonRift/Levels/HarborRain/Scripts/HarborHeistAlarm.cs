using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The restricted area's theft alarm, coordinated: rotating beacons on the fence light one after another round the
    /// compound (a 1.2 s chase), then strobe in two alternating halves; the bell starts; the yard floodlights shift from
    /// work white to emergency red-amber and pulse. Off (beacons dark, floods white) until <see cref="Trigger"/>.
    /// </summary>
    public sealed class HarborHeistAlarm : MonoBehaviour
    {
        [SerializeField] private Renderer[] beacons = new Renderer[0];
        [SerializeField] private Transform[] reflectors = new Transform[0];
        [SerializeField] private Light[] beaconLights = new Light[0];
        [SerializeField] private Light[] floods = new Light[0];
        [SerializeField] private AudioSource bell;
        [SerializeField] private Color floodWork = new(0.86f, 0.92f, 1f), floodAlarm = new(1f, 0.32f, 0.16f);

        private float started = -1f;
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private float[] floodBase = new float[0];

        public bool Active => started >= 0f;
        public float Elapsed => Active ? Time.time - started : 0f;

        private void Awake()
        {
            floodBase = new float[floods.Length];
            for (int i = 0; i < floods.Length; i++) if (floods[i] != null) floodBase[i] = floods[i].intensity;
            ResetAlarm();
        }

        public void Trigger()
        {
            if (Active) return;
            started = Time.time;
            if (bell != null) bell.Play();
        }

        public void ResetAlarm()
        {
            started = -1f;
            if (bell != null) bell.Stop();
            Apply(-1f);
        }

        private void Update() { if (Active) Apply(Time.time - started); }

        private void Apply(float t)
        {
            block ??= new MaterialPropertyBlock();
            int n = beacons.Length;
            for (int i = 0; i < n; i++)
            {
                float level;
                if (t < 0f) level = 0.02f;
                else if (t < 1.2f) level = t * n / 1.2f >= i ? 1f : 0.02f;                     // chase round the compound
                else level = (Mathf.Repeat(t * 1.8f, 1f) < 0.5f) == (i % 2 == 0) ? 1f : 0.12f; // two alternating halves
                var r = beacons[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionColor, new Color(4.5f, 0.35f, 0.1f) * level);
                r.SetPropertyBlock(block);
                if (i < reflectors.Length && reflectors[i] != null && t >= 0f) reflectors[i].Rotate(0f, 400f * Time.deltaTime, 0f, Space.Self);
            }
            for (int i = 0; i < beaconLights.Length; i++)
                if (beaconLights[i] != null)
                {
                    beaconLights[i].enabled = t >= 0f;
                    beaconLights[i].intensity = t < 0f ? 0f : 14f + 10f * Mathf.Sin(t * 9f + i);
                }
            for (int i = 0; i < floods.Length; i++)
            {
                if (floods[i] == null) continue;
                float k = t < 0f ? 0f : Mathf.Clamp01(t / 1.5f);
                floods[i].color = Color.Lerp(floodWork, floodAlarm, k);
                floods[i].intensity = i < floodBase.Length ? floodBase[i] * (t < 0f ? 1f : Mathf.Lerp(1f, 0.55f + 0.25f * Mathf.Sin(t * 3.4f), k)) : floods[i].intensity;
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Renderer[] lamps, Transform[] spin, Light[] lampLights, Light[] floodLights, AudioSource bellSource)
        {
            beacons = lamps; reflectors = spin; beaconLights = lampLights; floods = floodLights; bell = bellSource;
        }
#endif
    }
}
