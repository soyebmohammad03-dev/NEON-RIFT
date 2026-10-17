using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The emergency kit on a Harbor Rain police car: a roof lightbar (red on the driver's side, blue on the passenger
    /// side) with a quad-flash pattern, one small real light per colour, a 3D siren that switches between wail and yelp,
    /// and a pillar searchlight used while the unit is searching. Off on patrol. Emissive levels stay modest (they are
    /// seen through rain and bloom), and the lights cast no shadows.
    /// </summary>
    public sealed class HarborPoliceCar : MonoBehaviour
    {
        public enum Lights { Off, Respond, Search }

        [SerializeField] private Renderer[] red = new Renderer[0];
        [SerializeField] private Renderer[] blue = new Renderer[0];
        [SerializeField] private Light redLight, blueLight, searchlight;
        [SerializeField] private AudioSource siren;
        [SerializeField] private AudioClip wail, yelp;
        [SerializeField] private float flashIntensity = 3.2f;
        [SerializeField] private float lightIntensity = 42f;
        [SerializeField] private NeonRift.Vehicles.VehicleLights headlights;

        public Lights Mode { get; private set; }
        public bool Yelping { get; private set; }
        /// <summary>Per-car phase so a group of units does not flash in lock-step.</summary>
        public float Phase { get; set; }

        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Red = new(1f, 0.05f, 0.03f), Blue = new(0.05f, 0.25f, 1f);

        private void OnEnable() { if (headlights == null) headlights = GetComponentInParent<NeonRift.Vehicles.VehicleLights>(); Apply(0f, 0f); }

        public void Set(Lights mode, bool sirenOn, bool urgent)
        {
            Mode = mode;
            if (siren == null) return;
            var clip = urgent && yelp != null ? yelp : wail;
            if (!sirenOn) { if (siren.isPlaying) siren.Stop(); return; }
            if (siren.clip != clip || !siren.isPlaying)
            {
                siren.clip = clip;
                siren.loop = true;
                siren.Play();
            }
            Yelping = clip == yelp;
        }

        private void Update()
        {
            if (Mode == Lights.Off) { Apply(0f, 0f); if (searchlight != null) searchlight.enabled = false; if (headlights != null) headlights.SetHeadlightLevel(1f); return; }
            // Quad flash: red ×2, blue ×2 in 0.8 s; while searching a slower alternate.
            float rate = Mode == Lights.Search ? 0.9f : 1.25f;
            float u = Mathf.Repeat(Time.time * rate + Phase, 1f);
            float r, b;
            if (Mode == Lights.Search) { r = u < 0.5f ? 1f : 0.05f; b = u < 0.5f ? 0.05f : 1f; }
            else
            {
                int slot = (int)(u * 8f);
                bool on = slot % 2 == 0;
                r = slot < 4 && on ? 1f : 0.04f;
                b = slot >= 4 && on ? 1f : 0.04f;
            }
            Apply(r, b);
            if (searchlight != null) searchlight.enabled = Mode == Lights.Search;
            // Responding: the headlights pulse (a unit on a call reads differently from one on patrol, even head-on).
            if (headlights != null) headlights.SetHeadlightLevel(Mode == Lights.Respond ? (Mathf.Repeat(Time.time * 2.4f + Phase, 1f) < 0.5f ? 1f : 0.55f) : 1f);
        }

        private void Apply(float r, float b)
        {
            block ??= new MaterialPropertyBlock();
            void Set(Renderer[] list, Color c, float level)
            {
                foreach (var x in list)
                {
                    if (x == null) continue;
                    x.GetPropertyBlock(block);
                    block.SetColor(EmissionColor, c * (flashIntensity * level));
                    x.SetPropertyBlock(block);
                }
            }
            Set(red, Red, r);
            Set(blue, Blue, b);
            if (redLight != null) { redLight.enabled = r > 0.5f; redLight.intensity = lightIntensity * r; }
            if (blueLight != null) { blueLight.enabled = b > 0.5f; blueLight.intensity = lightIntensity * b; }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Renderer[] redLamps, Renderer[] blueLamps, Light r, Light b, Light search, AudioSource source, AudioClip wailClip, AudioClip yelpClip)
        {
            red = redLamps; blue = blueLamps; redLight = r; blueLight = b; searchlight = search; siren = source; wail = wailClip; yelp = yelpClip;
        }
#endif
    }
}
