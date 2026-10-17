using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The headland's exposed stretch: every so often a sea hits the breakwater and throws spray over the wall onto the
    /// road. Amber beacons at both ends flash for <see cref="warning"/> seconds before each burst (the same warning
    /// language as the wave sections), the burst rolls along the wall, and the roar plays. Spray is visual only (the
    /// water on the road is a shallow flood zone); intervals shorten as the storm builds.
    /// </summary>
    public sealed class HarborSprayBursts : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] bursts = new ParticleSystem[0];
        [SerializeField] private Renderer[] beacons = new Renderer[0];
        [SerializeField] private AudioSource roar;
        [SerializeField] private StormController storm;
        [SerializeField] private Vector2 interval = new(9f, 16f);
        [SerializeField] private float warning = 2.5f;

        private float next, rollT = -1f;
        private int rolled;
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public int Bursts { get; private set; }
        public bool Warning => next - Time.time < warning && next > Time.time;

        private void OnEnable() => next = Time.time + Random.Range(interval.x, interval.y);

        private void Update()
        {
            float intensity = storm != null ? storm.Intensity : 0.8f;
            if (rollT < 0f && Time.time >= next)
            {
                rollT = 0f;
                rolled = 0;
                Bursts++;
                if (roar != null) { roar.pitch = Random.Range(0.85f, 1.1f); roar.Play(); }
                next = Time.time + Random.Range(interval.x, interval.y) * Mathf.Lerp(1.4f, 0.8f, intensity);
            }
            if (rollT >= 0f)
            {
                // The sea runs along the wall: each emitter fires 0.25 s after the previous one.
                rollT += Time.deltaTime;
                while (rolled < bursts.Length && rollT >= rolled * 0.25f)
                {
                    if (bursts[rolled] != null) bursts[rolled].Emit(Mathf.RoundToInt(Mathf.Lerp(180f, 320f, intensity)));
                    rolled++;
                }
                if (rolled >= bursts.Length && rollT > 1.5f) rollT = -1f;
            }
            block ??= new MaterialPropertyBlock();
            bool on = Warning && Mathf.Repeat(Time.time * 2.6f, 1f) < 0.5f || rollT >= 0f && Mathf.Repeat(Time.time * 4f, 1f) < 0.5f;
            foreach (var b in beacons)
            {
                if (b == null) continue;
                b.GetPropertyBlock(block);
                block.SetColor(EmissionColor, on ? new Color(4f, 1.9f, 0.1f) : new Color(0.08f, 0.03f, 0f));
                b.SetPropertyBlock(block);
            }
        }

#if UNITY_EDITOR
        public void EditorSetStorm(StormController s) => storm = s;
#endif
    }
}
