using NeonRift.Game;
using UnityEngine;

namespace NeonRift.Gameplay
{
    /// <summary>
    /// The rift between the two operations (campaign only), built at runtime: an energy ring and a swirling core
    /// (<c>NeonRift/Portal</c> materials), sparks drawn into the centre, a pulsing light that washes the street, and the
    /// open / hum / enter sounds. <see cref="Open"/> drives it from closed (0) to fully open (1). The portal faces
    /// +Z (cars enter travelling along -Z, from its front).
    /// </summary>
    public sealed class PortalRift : MonoBehaviour
    {
        public const float Radius = 5.2f;
        public const float CentreHeight = 4.3f;

        private Material ring, core;
        private Transform ringT, coreT;
        private ParticleSystem sparks;
        private Light glow;
        private AudioSource hum, oneShots;
        private float open;
        private CampaignDefinition def;

        public float Openness => open;
        /// <summary>World point at the centre of the opening.</summary>
        public Vector3 Centre => transform.position + Vector3.up * CentreHeight;

        public static PortalRift Create(CampaignDefinition definition, Vector3 groundPosition, Quaternion facing, AudioMixerConfigRef mixer)
        {
            var go = new GameObject("CampaignPortal");
            go.transform.SetPositionAndRotation(groundPosition, facing);
            var p = go.AddComponent<PortalRift>();
            p.Build(definition, mixer);
            return p;
        }

        private void Build(CampaignDefinition definition, AudioMixerConfigRef mixer)
        {
            def = definition;
            ring = def.PortalRing != null ? new Material(def.PortalRing) : null;
            core = def.PortalCore != null ? new Material(def.PortalCore) : null;

            ringT = Part("Ring", Torus(Radius, 0.32f, 96, 14), ring);
            coreT = Part("Core", Disc(Radius * 0.99f, 96), core);
            // A second, larger and fainter ring for depth.
            var halo = Part("Halo", Torus(Radius + 0.55f, 0.12f, 96, 8), ring);
            halo.localScale = Vector3.one;

            var lightGo = new GameObject("PortalLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, CentreHeight, 1.2f);
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 26f;
            glow.color = new Color(0.45f, 0.8f, 1f);
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;

            var sparkGo = new GameObject("PortalSparks");
            sparkGo.transform.SetParent(transform, false);
            sparkGo.transform.localPosition = new Vector3(0f, CentreHeight, 0f);
            sparks = sparkGo.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(-5.5f, -2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.95f, 1f), new Color(1f, 0.45f, 0.95f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 600;
            var emission = sparks.emission;
            emission.rateOverTime = 0f;
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Radius;
            shape.radiusThickness = 0.05f;
            shape.rotation = Vector3.zero;
            var vel = sparks.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.z = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            // All orbital curves in the same (two-constant) mode, as Unity requires.
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            var size = sparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = sparkGo.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = def.PortalSparks;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.6f;
            sparks.Play();

            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = def.PortalHum;
            hum.loop = true;
            hum.spatialBlend = 0.65f;
            hum.minDistance = 8f;
            hum.maxDistance = 120f;
            hum.volume = 0f;
            hum.ignoreListenerPause = false;
            oneShots = gameObject.AddComponent<AudioSource>();
            oneShots.spatialBlend = 0.4f;
            oneShots.minDistance = 10f;
            oneShots.maxDistance = 200f;
            if (mixer.Group != null) hum.outputAudioMixerGroup = oneShots.outputAudioMixerGroup = mixer.Group;
            if (hum.clip != null) hum.Play();
            SetOpen(0f);
        }

        private Transform Part(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, CentreHeight, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        public void PlayOpen() { if (def.PortalOpen != null) oneShots.PlayOneShot(def.PortalOpen, 1f); }
        public void PlayEnter() { if (def.PortalEnter != null) oneShots.PlayOneShot(def.PortalEnter, 1f); }
        public void PlayArrive() { if (def.PortalArrive != null) oneShots.PlayOneShot(def.PortalArrive, 1f); }

        /// <summary>0 = closed (invisible), 1 = fully open.</summary>
        public void SetOpen(float value)
        {
            open = Mathf.Clamp01(value);
            float e = 1f - Mathf.Pow(1f - open, 3f);
            if (ringT != null) ringT.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, e);
            if (coreT != null) coreT.localScale = Vector3.one * Mathf.Lerp(0.02f, 1f, e);
            if (ring != null) { ring.SetFloat("_Open", open); ring.SetFloat("_Intensity", Mathf.Lerp(0f, 1.25f, e)); }
            if (core != null) { core.SetFloat("_Open", e); core.SetFloat("_Intensity", Mathf.Lerp(0f, 1f, e)); }
            if (glow != null) glow.intensity = Mathf.Lerp(0f, 260f, e);
            if (hum != null) hum.volume = 0.8f * e;
            if (sparks != null)
            {
                var emission = sparks.emission;
                emission.rateOverTime = 180f * e;
                var shape = sparks.shape;
                shape.radius = Mathf.Max(0.1f, Radius * e);
            }
        }

        private float pulse;

        /// <summary>A brief surge (a car passing through).</summary>
        public void Surge() => pulse = 1f;

        private void Update()
        {
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 1.4f);
            if (glow != null && open > 0f)
                glow.intensity = Mathf.Lerp(0f, 260f, open) * (0.85f + 0.15f * Mathf.Sin(Time.time * 7.3f)) + pulse * 900f;
            if (coreT != null) coreT.localRotation = Quaternion.Euler(0f, 0f, -Time.time * 40f);
            if (core != null && pulse > 0f) core.SetFloat("_Intensity", 1f + pulse * 2.5f);
        }

        private void OnDestroy()
        {
            if (ring != null) Destroy(ring);
            if (core != null) Destroy(core);
        }

        /// <summary>Signed distance of <paramref name="point"/> in front of the portal plane (positive = not yet through).</summary>
        public float Front(Vector3 point) => Vector3.Dot(point - transform.position, transform.forward);

        // ---------------- Meshes ----------------

        private static Mesh Torus(float major, float minor, int segments, int sides)
        {
            var v = new Vector3[(segments + 1) * (sides + 1)];
            var uv = new Vector2[v.Length];
            var tris = new int[segments * sides * 6];
            int k = 0;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var centre = new Vector3(Mathf.Cos(a) * major, Mathf.Sin(a) * major, 0f);
                var outward = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    v[i * (sides + 1) + j] = centre + (outward * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b)) * minor;
                    uv[i * (sides + 1) + j] = new Vector2(i / (float)segments, j / (float)sides);
                }
            }
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * (sides + 1) + j, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                    tris[k++] = a0; tris[k++] = b0; tris[k++] = a1;
                    tris[k++] = a1; tris[k++] = b0; tris[k++] = b1;
                }
            var m = new Mesh { name = "PortalTorus", vertices = v, uv = uv, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh Disc(float radius, int segments)
        {
            var v = new Vector3[segments + 2];
            var uv = new Vector2[v.Length];
            var tris = new int[segments * 3];
            v[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
                uv[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
            }
            for (int i = 0; i < segments; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = i + 2; tris[i * 3 + 2] = i + 1; }
            var m = new Mesh { name = "PortalCore", vertices = v, uv = uv, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>The mixer group the portal's sounds play through (kept tiny so callers need not reference the audio assembly types).</summary>
    public readonly struct AudioMixerConfigRef
    {
        public readonly UnityEngine.Audio.AudioMixerGroup Group;
        public AudioMixerConfigRef(UnityEngine.Audio.AudioMixerGroup group) => Group = group;
    }
}
