using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>Alternating emergency strobes (patrol light bars, gate beacons): two renderers and an optional light.</summary>
    public sealed class HarborBlinker : MonoBehaviour
    {
        [SerializeField] private Renderer a;
        [SerializeField] private Renderer b;
        [SerializeField] private Light lightSource;
        [SerializeField] private Color colourA = new(6f, 0.2f, 0.15f);
        [SerializeField] private Color colourB = new(0.2f, 0.5f, 7f);
        [SerializeField] private float rate = 2.4f;

        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor"), BeaconColor = Shader.PropertyToID("_Color");

        private void Update()
        {
            block ??= new MaterialPropertyBlock();
            bool first = Mathf.Repeat(Time.time * rate, 1f) < 0.5f;
            Set(a, first ? colourA : colourA * 0.05f);
            Set(b, first ? colourB * 0.05f : colourB);
            if (lightSource != null) lightSource.color = first ? new Color(1f, 0.15f, 0.1f) : new Color(0.15f, 0.3f, 1f);
        }

        private void Set(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionColor, c);
            block.SetColor(BeaconColor, c);   // HarborBeacon glows read _Color
            r.SetPropertyBlock(block);
        }
    }
}
