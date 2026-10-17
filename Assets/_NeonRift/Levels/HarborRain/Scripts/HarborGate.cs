using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>Boom-barrier checkpoint: the arm swings up to open; a blocker collider closes the lane when it is down.</summary>
    public sealed class HarborGate : MonoBehaviour
    {
        [SerializeField] private Transform arm;
        [SerializeField] private Collider blocker;
        [SerializeField] private Renderer[] lamps = new Renderer[0];
        [SerializeField] private bool startOpen;
        [SerializeField] private float swingSeconds = 1.2f;

        public bool IsOpen { get; private set; }
        private float t;
        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private void Awake() => ResetGate();

        public void ResetGate()
        {
            IsOpen = startOpen;
            t = IsOpen ? 1f : 0f;
            Apply();
        }

        public void SetOpen(bool open) => IsOpen = open;

        private void Update()
        {
            float target = IsOpen ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;
            t = Mathf.MoveTowards(t, target, Time.deltaTime / swingSeconds);
            Apply();
        }

        private void Apply()
        {
            if (arm != null) arm.localRotation = Quaternion.Euler(0f, 0f, Mathf.SmoothStep(0f, 82f, t));
            if (blocker != null) blocker.enabled = t < 0.5f;
            block ??= new MaterialPropertyBlock();
            foreach (var r in lamps)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionColor, IsOpen ? new Color(0.2f, 4f, 1.4f) : new Color(5f, 0.2f, 0.12f));
                r.SetPropertyBlock(block);
            }
        }
    }
}
