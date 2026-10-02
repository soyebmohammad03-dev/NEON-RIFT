using System;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// Presentation of the Data Core: rings spin up and the core brightens with hack progress; once breached it
    /// turns to the alarm colour and the rings lurch. Reads its <see cref="Interactable"/>; no gameplay logic.
    /// </summary>
    public sealed class DataCoreVisual : MonoBehaviour, IMissionWorldComponent
    {
        [SerializeField] private Interactable uplink;
        [Tooltip("World event that marks the core as breached.")]
        [SerializeField] private string breachedEvent = "core.breached";
        [SerializeField] private Transform[] rings = Array.Empty<Transform>();
        [SerializeField] private Renderer[] glowRenderers = Array.Empty<Renderer>();
        [SerializeField] private Light coreLight;
        [SerializeField, ColorUsage(false, true)] private Color idleColor = new(0.3f, 2.2f, 3.2f);
        [SerializeField, ColorUsage(false, true)] private Color hackColor = new(2.5f, 4f, 5f);
        [SerializeField, ColorUsage(false, true)] private Color breachedColor = new(5f, 0.25f, 0.6f);
        [SerializeField] private float idleSpin = 25f;
        [SerializeField] private float hackSpin = 420f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MissionWorld world;
        private Material material;
        private bool breached;
        private float spin, breachTime;

        private void Awake()
        {
            if (glowRenderers.Length > 0 && glowRenderers[0] != null)
            {
                material = new Material(glowRenderers[0].sharedMaterial) { name = glowRenderers[0].sharedMaterial.name + " (runtime)" };
                foreach (var r in glowRenderers) if (r != null) r.sharedMaterial = material;
            }
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        public void Bind(MissionWorld missionWorld)
        {
            world = missionWorld;
            world.EventRaised += OnWorldEvent;
            breached = false;
        }

        public void Unbind()
        {
            if (world != null) world.EventRaised -= OnWorldEvent;
            world = null;
        }

        private void OnWorldEvent(string eventId)
        {
            if (eventId != breachedEvent) return;
            breached = true;
            breachTime = Time.time;
        }

        private void Update()
        {
            float progress = uplink != null ? uplink.Progress : 0f;
            Color colour;
            float targetSpin;
            if (breached)
            {
                float flash = Mathf.Clamp01(1f - (Time.time - breachTime) * 1.5f);
                colour = Color.Lerp(breachedColor, Color.white * 6f, flash) * (0.75f + 0.25f * Mathf.Sin(Time.time * 9f));
                targetSpin = idleSpin * 3f;
            }
            else
            {
                colour = Color.Lerp(idleColor, hackColor, progress);
                if (uplink != null && uplink.InUse) colour *= 0.85f + 0.15f * Mathf.Sin(Time.time * 20f);
                targetSpin = Mathf.Lerp(idleSpin, hackSpin, progress * progress);
            }
            spin = Mathf.MoveTowards(spin, targetSpin, 600f * Time.deltaTime);
            for (int i = 0; i < rings.Length; i++)
                if (rings[i] != null) rings[i].Rotate(0f, spin * Time.deltaTime * (i % 2 == 0 ? 1f : -1.4f), 0f, Space.Self);
            if (material != null) material.SetColor(EmissionColor, colour);
            if (coreLight != null)
            {
                float max = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
                coreLight.color = max > 0f ? colour / max : Color.black;
                coreLight.intensity = 4f + max * 2f;
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(Interactable uplinkInteractable, Transform[] spinningRings, Renderer[] glow, Light light)
        {
            uplink = uplinkInteractable;
            rings = spinningRings;
            glowRenderers = glow;
            coreLight = light;
        }
#endif
    }
}
