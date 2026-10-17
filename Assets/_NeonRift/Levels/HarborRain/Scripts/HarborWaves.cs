using System;
using NeonRift.Vehicles;
using UnityEngine;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The storm-exposed coast road: staged breaking waves at three low sections of the seawall. Each section runs a
    /// repeatable cycle, every layer driven from the same clock so geometry, foam, spray and physics agree:
    /// <list type="number">
    /// <item>CALM — floodlit sea, beacons steady amber.</item>
    /// <item>BUILDING — a swell rises ~260 m out and runs in (<see cref="HarborBreaker"/>); beacons and wigwags flash,
    /// the surge horn sounds.</item>
    /// <item>APPROACH — the swell steepens, the face rises, the lip throws forward and curls; spray tears off the crest;
    /// the lip plunges just short of the wall.</item>
    /// <item>IMPACT — the collapsing wave hits the wall: a spray sheet up the face, peeling outward along the section.</item>
    /// <item>WASHING — green water climbs over the parapet and falls onto the road; a flood sheet sweeps inland with a
    /// foaming front. Cars in it get a bounded push along the flow and less grip.</item>
    /// <item>RECEDING — the water slows, turns back toward the sea and breaks into patches that drain through the
    /// gutters and scuppers; puddles are left larger than before.</item>
    /// </list>
    /// Later cycles are bigger and the calm gaps shorter as the storm intensity rises. Forces are capped accelerations
    /// applied at the centre of mass, so the car slides and recovers rather than flips.
    /// </summary>
    public sealed class HarborWaves : MonoBehaviour
    {
        public enum State { Calm, Building, Approach, Impact, Washing, Receding }

        [Serializable]
        public sealed class Section
        {
            public string label = "SEAWALL";
            public Rect road;
            public HarborBreaker breaker;
            public Renderer overtop;
            public Renderer flood;
            public Renderer[] puddles = Array.Empty<Renderer>();
            public ParticleSystem plume;
            public ParticleSystem mist;
            public ParticleSystem droplets;
            public ParticleSystem spouts;
            public AudioSource impact;
            public AudioSource rush;
            public AudioSource horn;
            public AudioSource drain;
            public Collider slickSurface;
            public Collider slickShallow;
            public Renderer[] beacons = Array.Empty<Renderer>();
            public Renderer[] wigwags = Array.Empty<Renderer>();
            public Light flash;
            public Light[] floodlights = Array.Empty<Light>();
            [NonSerialized] public State state;
            [NonSerialized] public float t, calm, strength, peak;
            [NonSerialized] public int cycles;
            // Live water state (what the renderers, the physics and the mission read).
            [NonSerialized] public float distance, height, curl, collapse;
            [NonSerialized] public float overFront, overTail, overThick;
            [NonSerialized] public float floodFront, floodDepth, floodFlow, floodDrain, floodFoam;
            [NonSerialized] public float wallFoam, trailFoam, puddleGrow, plumeSpread;
            [NonSerialized] public bool plumeDone;
            /// <summary>Largest water acceleration this section's flood applied to the player's car this cycle (m/s²).</summary>
            [NonSerialized] public float peakPush;
        }

        [SerializeField] private Section[] sections = Array.Empty<Section>();
        [SerializeField] private AudioClip[] impactClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip approachClip;
        [SerializeField] private AudioClip hornClip;
        [SerializeField] private StormController storm;
        [Tooltip("Crest distance from the wall face (m) when the swell first rises.")]
        [SerializeField] private float startDistance = 260f;
        [SerializeField] private float buildSeconds = 6f, approachSeconds = 9f, impactSeconds = 0.45f, washSeconds = 4.5f, recedeSeconds = 4f;
        [SerializeField] private Vector2 calmSeconds = new(16f, 7f);
        [Tooltip("Flood sheet: seaward edge (z) and length inland (m). uv.x of the flood mesh runs along this.")]
        [SerializeField] private float floodZ = 287f, floodLength = 22f, floodHalfWidth = 38f;
        [Header("Water forces (accelerations, m/s²)")]
        [SerializeField] private float flowCoupling = 2.4f;
        [SerializeField] private float maxAlong = 3.5f, maxLateral = 2.4f;

        public Section[] Sections => sections;
        public bool Running { get; private set; }
        public float Intensity { get; set; } = 0.5f;
        /// <summary>Raised on every state change (section, new state).</summary>
        public event Action<Section, State> Changed;
        /// <summary>Wet lens amount for the HUD (0..1).</summary>
        public float LensWater { get; private set; }
        /// <summary>Largest water acceleration applied to the player's car since the waves were reset (validation).</summary>
        public float PeakPush { get; private set; }
        public float LastDepthAtCar { get; private set; }

        private MaterialPropertyBlock block;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int SheetA = Shader.PropertyToID("_SheetA");
        private static readonly int SheetB = Shader.PropertyToID("_SheetB");
        private static readonly int SheetC = Shader.PropertyToID("_SheetC");

        /// <summary>Surge for the ocean shader: (centre x, crest z, height m, half width m); zero when calm.</summary>
        public Vector4 Surge(int i)
        {
            if (i >= sections.Length) return Vector4.zero;
            var s = sections[i];
            if (s.state == State.Calm && s.wallFoam <= 0.01f) return Vector4.zero;
            return new Vector4(s.road.center.x, HarborWater.ShoreZ + s.distance, s.height, s.road.width * 0.7f);
        }

        /// <summary>Foam the ocean shows around a section: trail behind the broken crest, length, wall foam, curl.</summary>
        public Vector4 SurgeFoam(int i)
        {
            if (i >= sections.Length) return Vector4.zero;
            var s = sections[i];
            return new Vector4(s.trailFoam, 30f + s.collapse * 60f, s.wallFoam, s.curl);
        }

        private void Awake() => ResetWaves();

        public void ResetWaves()
        {
            Running = false;
            LensWater = 0f;
            PeakPush = 0f;
            for (int i = 0; i < sections.Length; i++)
            {
                var s = sections[i];
                s.state = State.Calm;
                s.t = 0f;
                s.cycles = 0;
                s.calm = 4f + i * 5f;
                s.wallFoam = s.trailFoam = s.puddleGrow = 0f;
                s.floodDepth = s.floodFoam = 0f;
                s.floodDrain = 1f;
                Apply(s, 0f);
            }
        }

        public void Begin() => Running = true;

        /// <summary>Validation / scripting: start this section's cycle now.</summary>
        public void Trigger(int index)
        {
            if (index < 0 || index >= sections.Length) return;
            var s = sections[index];
            if (s.state == State.Calm) { s.calm = 0f; Running = true; }
        }

        /// <summary>True while the section's road is (or is about to be) swept: routing treats it as closed.</summary>
        public static bool Dangerous(Section s) => s.state is State.Approach or State.Impact or State.Washing || s.state == State.Building && s.t > 2f;

        /// <summary>Water depth (m) and flow velocity at a point on the coast road.</summary>
        public bool Sample(Vector3 p, out float depth, out Vector3 flow, out Section section)
        {
            depth = 0f; flow = Vector3.zero; section = null;
            foreach (var s in sections)
            {
                if (s.floodDepth <= 0.001f) continue;
                float across = Mathf.Abs(p.x - s.road.center.x) / floodHalfWidth;
                if (across >= 1f) continue;
                float u = (floodZ - p.z) / floodLength;
                if (u < -0.05f || u > s.floodFront) continue;
                float lateral = 1f - Smooth(0.6f, 1f, across);
                float bore = Mathf.Exp(-(s.floodFront - u) * (s.floodFront - u) * 90f) * 0.8f;
                float d = s.floodDepth * (0.55f + bore) * lateral * (1f - s.floodDrain * 0.85f);
                if (d <= depth) continue;
                depth = d;
                section = s;
                // Inland surge (−z, a little downwind), or the slow backwash toward the sea.
                flow = new Vector3(s.floodFlow > 0f ? -s.floodFlow * 0.12f : 0f, 0f, -s.floodFlow);
            }
            return depth > 0.01f;
        }

        public bool Washing(Vector3 p, out Section section)
        {
            if (Sample(p, out float d, out _, out section) && d > 0.06f && section.state is State.Washing or State.Receding) return true;
            section = null;
            return false;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            LensWater = Mathf.MoveTowards(LensWater, 0f, dt * 0.3f);
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            foreach (var s in sections)
            {
                foreach (var l in s.floodlights)
                    if (l != null) l.enabled = Vector3.Distance(eye, l.transform.position) < 520f;
                if (!Running && s.state == State.Calm) { Apply(s, Time.time); continue; }
                s.t += dt;
                switch (s.state)
                {
                    case State.Calm:
                        if (Running && s.t >= s.calm)
                        {
                            s.strength = Mathf.Clamp01(0.55f + Intensity * 0.35f + s.cycles * 0.06f);
                            s.peak = 5.5f + s.strength * 3.5f;
                            Go(s, State.Building);
                        }
                        break;
                    case State.Building: if (s.t >= buildSeconds) Go(s, State.Approach); break;
                    case State.Approach: if (s.t >= approachSeconds) Go(s, State.Impact); break;
                    case State.Impact: if (s.t >= impactSeconds) Go(s, State.Washing); break;
                    case State.Washing: if (s.t >= washSeconds * (0.8f + s.strength * 0.4f)) Go(s, State.Receding); break;
                    case State.Receding:
                        if (s.t >= recedeSeconds)
                        {
                            s.cycles++;
                            s.calm = Mathf.Lerp(calmSeconds.x, calmSeconds.y, Intensity) * UnityEngine.Random.Range(0.8f, 1.25f);
                            Go(s, State.Calm);
                        }
                        break;
                }
                Apply(s, Time.time);
                if (cam != null && s.state is State.Impact or State.Washing && s.t < 1.6f)
                {
                    // Spray drifting over the road wets the lens near the wall.
                    float d = Vector2.Distance(new Vector2(eye.x, eye.z), new Vector2(s.road.center.x, s.road.yMax + 6f));
                    if (d < 80f) LensWater = Mathf.Max(LensWater, Mathf.InverseLerp(80f, 15f, d) * (0.6f + s.strength * 0.4f));
                }
            }
        }

        private void Go(Section s, State next)
        {
            s.state = next;
            s.t = 0f;
            switch (next)
            {
                case State.Building:
                    if (s.horn != null && hornClip != null) s.horn.PlayOneShot(hornClip, 0.85f);
                    break;
                case State.Approach:
                    if (s.impact != null && approachClip != null) s.impact.PlayOneShot(approachClip, 0.9f);
                    break;
                case State.Impact:
                    s.plumeSpread = 0f;
                    s.plumeDone = false;
                    if (s.flash != null) { s.flash.enabled = true; s.flash.intensity = 70f * (0.6f + s.strength * 0.4f); }   // a glint on the white water, not a blast
                    if (s.impact != null && impactClips.Length > 0) s.impact.PlayOneShot(impactClips[UnityEngine.Random.Range(0, impactClips.Length)], 0.8f + s.strength * 0.2f);
                    break;
                case State.Washing:
                    if (s.rush != null) { s.rush.pitch = 1f; s.rush.Play(); }
                    s.peakPush = 0f;
                    break;
                case State.Receding:
                    if (s.drain != null) s.drain.Play();
                    break;
                case State.Calm:
                    if (s.rush != null) s.rush.Stop();
                    break;
            }
            Changed?.Invoke(s, next);
        }

        private static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        private void Apply(Section s, float time)
        {
            float k = s.strength, dt = Time.deltaTime;
            float peak = s.peak > 0f ? s.peak : 6f;

            // ---- The breaking wave.
            bool wave = true;
            float back, front, sink = 0f, oldFoam, trail = 0f;
            switch (s.state)
            {
                case State.Building:
                {
                    float u = s.t / buildSeconds;
                    s.distance = Mathf.Lerp(startDistance, 170f, u);
                    s.height = Mathf.Lerp(0.4f, 2.4f, Smooth(0f, 1f, u));
                    s.curl = 0f; s.collapse = 0f;
                    break;
                }
                case State.Approach:
                {
                    float u = s.t / approachSeconds;
                    s.distance = 170f * (1f - u) * (1f + 0.3f * u);
                    float d = s.distance;
                    s.height = d > 45f ? Mathf.Lerp(2.4f, peak * 0.8f, Smooth(170f, 45f, d)) : Mathf.Lerp(peak * 0.8f, peak, Smooth(45f, 0f, d));
                    s.curl = Smooth(42f, 10f, d);
                    s.collapse = Smooth(9f, 0f, d) * 0.5f;
                    break;
                }
                case State.Impact:
                    s.distance = 0f; s.height = peak; s.curl = 1f;
                    s.collapse = Mathf.Lerp(0.5f, 1f, s.t / impactSeconds);
                    break;
                case State.Washing:
                    // The spent bore rebounds off the wall and sinks back into the sea.
                    s.distance = s.t * 3f;
                    s.height = peak * Mathf.Exp(-s.t * 1.1f);
                    s.collapse = 1f;
                    sink = s.t * 1.3f;
                    wave = sink < 3.5f;
                    break;
                default:
                    wave = false;
                    break;
            }
            float dist = s.distance;
            back = Mathf.Lerp(45f, 75f, Smooth(40f, 240f, dist));
            front = Mathf.Lerp(9f, 55f, Smooth(25f, 200f, dist));
            oldFoam = Mathf.Clamp01(0.12f + s.curl * 0.5f + s.collapse * 0.4f);
            if (s.state == State.Washing) trail = 1f;
            if (s.breaker != null)
            {
                if (wave) s.breaker.Pose(dist, s.height, back, front, s.curl, s.collapse, sink, oldFoam, trail);
                else s.breaker.Hide();
            }

            // ---- Foam the sea keeps: a trail behind the broken crest, churned white water against the wall.
            float trailTarget = s.state is State.Approach or State.Impact or State.Washing ? Mathf.Max(s.curl * 0.8f, s.collapse) : 0f;
            s.trailFoam = Mathf.MoveTowards(s.trailFoam, trailTarget, dt * (trailTarget > s.trailFoam ? 2f : 0.12f));
            float wallTarget = s.state is State.Impact or State.Washing ? 1f : s.state == State.Receding ? 0.6f : 0f;
            s.wallFoam = Mathf.MoveTowards(s.wallFoam, wallTarget, dt * (wallTarget > s.wallFoam ? 3f : 0.08f));

            // ---- Wall impact: a spray sheet up the face, peeling outward from the centre (where the crest hit first).
            if (s.state == State.Impact || s.state == State.Washing && s.t < 0.6f)
            {
                float prev = s.plumeSpread;
                s.plumeSpread = Mathf.Min(1f, s.plumeSpread + dt / 0.9f);
                if (s.plume != null) EmitPlume(s, prev, s.plumeSpread, k);
            }
            if (s.flash != null && s.flash.enabled)
            {
                s.flash.intensity = Mathf.MoveTowards(s.flash.intensity, 0f, dt * 90f);
                if (s.flash.intensity <= 0.01f) s.flash.enabled = false;
            }

            // ---- Green water over the parapet (uv.x of the ribbon: wall face 0 → road edge 1).
            switch (s.state)
            {
                case State.Impact:
                    s.overFront = Mathf.Lerp(0f, 0.25f, s.t / impactSeconds); s.overTail = 0f; s.overThick = 0.6f + k * 0.5f;
                    break;
                case State.Washing:
                    s.overFront = Mathf.Min(1.1f, 0.25f + s.t / 0.75f);
                    s.overTail = Smooth(1.2f, 2.8f, s.t) * 1.1f;
                    s.overThick = (0.6f + k * 0.5f) * Mathf.Lerp(1f, 0.35f, Smooth(0.4f, 2.4f, s.t));
                    break;
                default:
                    s.overFront = 0f; s.overTail = 1.2f; s.overThick = 0f;
                    break;
            }
            SetSheet(s.overtop, s.overFront, s.overTail, s.overThick, 1f, 12f, 0f, 1f, 0f, 0f, 0.8f, 0f, s.road.center.x * 0.013f);

            // ---- Flood across the road: sweeps inland, slows, turns back toward the sea and drains.
            switch (s.state)
            {
                case State.Washing:
                {
                    float u = Smooth(0.5f, 2.3f, s.t);
                    s.floodFront = Mathf.Lerp(0f, 1.05f, 1f - (1f - u) * (1f - u));
                    s.floodDepth = (0.16f + k * 0.16f) * Mathf.Lerp(1f, 0.55f, Smooth(1.5f, 4.5f, s.t));
                    s.floodFlow = Mathf.Lerp(8f, 0.5f, Smooth(0.6f, 3.2f, s.t));
                    s.floodDrain = 0f;
                    s.floodFoam = Mathf.Lerp(1f, 0.45f, Smooth(0.8f, 3.5f, s.t));
                    s.puddleGrow = Mathf.Max(s.puddleGrow, Smooth(1f, 3f, s.t));
                    break;
                }
                case State.Receding:
                    s.floodFront = 1.05f;
                    s.floodFlow = -1.6f * (1f - s.t / recedeSeconds);
                    s.floodDrain = Smooth(0f, recedeSeconds, s.t);
                    s.floodFoam = Mathf.Lerp(0.45f, 0.1f, s.t / recedeSeconds);
                    break;
                case State.Calm:
                    s.floodDrain = Mathf.MoveTowards(s.floodDrain, 1f, dt * 0.5f);
                    if (s.floodDrain >= 1f) s.floodDepth = 0f;
                    s.puddleGrow = Mathf.MoveTowards(s.puddleGrow, 0f, dt / 25f);
                    break;
                default:
                    if (s.state != State.Impact) { s.floodFront = 0f; s.floodDepth = 0f; s.floodDrain = 1f; }
                    break;
            }
            bool wet = s.floodDepth > 0.001f && s.floodDrain < 0.995f;
            SetSheet(wet ? s.flood : null, s.floodFront, 0f, s.floodDepth * 0.6f, s.floodFoam, s.floodFlow, s.floodDrain, 1f, 0f, 0f, 0.75f, 0.6f, s.road.center.x * 0.029f);
            if (s.flood != null && !wet) s.flood.enabled = false;
            for (int i = 0; i < s.puddles.Length; i++)
                SetSheet(s.puddles[i], 2f, 0f, 0.015f, 0f, 0f, 0f, 0f, 1f, Mathf.Lerp(0.55f, 0.95f, s.puddleGrow), 0.9f, 1f, i * 1.7f);

            // ---- Grip: deep low-grip surface under the moving flood, a milder one while it drains.
            ResizeSlick(s.slickSurface, s.state == State.Washing && s.floodDepth > 0.08f, s.floodFront, s);
            ResizeSlick(s.slickShallow, s.state == State.Receding || s.state == State.Washing && s.floodDepth <= 0.08f, 1.05f, s);

            // ---- Mist drifting downwind, droplets raining onto the road, scuppers pouring back into the sea.
            if (s.mist != null)
            {
                var e = s.mist.emission;
                e.rateOverTime = s.state is State.Impact ? 16f : s.state == State.Washing ? 5f * (1f - s.t / washSeconds) : 0.4f;
            }
            if (s.droplets != null)
            {
                var e = s.droplets.emission;
                e.rateOverTime = s.state == State.Washing && s.t < 1.4f ? 420f * (0.6f + k * 0.4f) * (1f - s.t / 1.4f) : 0f;
            }
            if (s.spouts != null)
            {
                var e = s.spouts.emission;
                e.rateOverTime = s.state == State.Receding ? 70f * (1f - s.t / recedeSeconds) : s.state == State.Washing && s.t > 1.5f ? 70f : 0f;
            }
            if (s.drain != null && s.drain.isPlaying)
            {
                // Gurgling grates while the road drains, fading out over the first seconds of the calm.
                s.drain.volume = s.state == State.Receding ? Mathf.Lerp(0.2f, 0.7f, s.t / recedeSeconds) : Mathf.MoveTowards(s.drain.volume, 0f, dt * 0.15f);
                if (s.state == State.Calm && s.drain.volume <= 0.01f) s.drain.Stop();
            }
            if (s.rush != null)
            {
                s.rush.volume = s.state == State.Washing ? 0.95f : s.state == State.Receding ? 0.6f * (1f - s.t / recedeSeconds) : 0f;
                s.rush.pitch = s.state == State.Receding ? Mathf.Lerp(0.85f, 0.65f, s.t / recedeSeconds) : 1f;
            }

            // ---- Beacons: steady amber when calm, flashing amber while building, flashing red when dangerous.
            block ??= new MaterialPropertyBlock();
            bool on = Mathf.Repeat(time * 3f, 1f) < 0.5f;
            Color c = s.state switch
            {
                State.Calm => new Color(0.8f, 0.45f, 0.05f),
                State.Building => on ? new Color(6f, 3f, 0.2f) : new Color(0.4f, 0.2f, 0f),
                State.Receding => new Color(3f, 1.6f, 0.1f),
                _ => on ? new Color(7f, 0.5f, 0.3f) : new Color(0.6f, 0.03f, 0.02f)
            };
            foreach (var r in s.beacons) SetEmission(r, c);
            // Storm-sign wigwags: alternate pairs while the section is live, dark when calm.
            bool live = s.state != State.Calm;
            bool wig = Mathf.Repeat(time * 1.6f, 1f) < 0.5f;
            for (int i = 0; i < s.wigwags.Length; i++)
                SetEmission(s.wigwags[i], live ? ((i % 2 == 0) == wig ? new Color(9f, 4.6f, 0.4f) : new Color(0.25f, 0.12f, 0f)) : new Color(0.25f, 0.12f, 0f));
        }

        private void SetEmission(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionColor, c);
            r.SetPropertyBlock(block);
        }

        private void SetSheet(Renderer r, float frontU, float tailU, float thick, float foam, float flow, float drain, float ragged,
                              float puddle, float radius, float opacity, float rain, float seed = 0f)
        {
            if (r == null) return;
            bool show = puddle > 0.5f || thick > 0.005f && frontU > tailU + 0.01f;
            r.enabled = show;
            if (!show) return;
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetVector(SheetA, new Vector4(frontU, tailU, thick, foam));
            block.SetVector(SheetB, new Vector4(seed + r.transform.position.x * 0.137f, flow, drain, ragged));
            block.SetVector(SheetC, new Vector4(puddle, radius, opacity, rain));
            r.SetPropertyBlock(block);
        }

        private void ResizeSlick(Collider c, bool on, float frontU, Section s)
        {
            if (c == null) return;
            c.enabled = on;
            if (!on || c is not BoxCollider box) return;
            // Cover the road from the wall side to the flood front only (the collider has no rotation or scale).
            float z1 = s.road.yMax, z0 = Mathf.Clamp(floodZ - Mathf.Clamp01(frontU) * floodLength, s.road.yMin, z1 - 0.5f);
            box.center = new Vector3(box.center.x, box.center.y, (z0 + z1) * 0.5f - box.transform.position.z);
            box.size = new Vector3(box.size.x, box.size.y, z1 - z0);
        }

        private void EmitPlume(Section s, float from, float to, float k)
        {
            // Particles leave the wall face between the previous and current spread, symmetric about the centre.
            float half = s.road.width * 0.5f + 20f;
            int n = Mathf.RoundToInt((to - from) * (480f + k * 300f));
            Vector3 wind = storm != null ? storm.Wind : new Vector3(-6f, 0f, -8f);
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                float u = Mathf.Lerp(from, to, UnityEngine.Random.value);
                float x = s.road.center.x + (UnityEngine.Random.value < 0.5f ? -1f : 1f) * u * half;
                float lift = (1f - u * 0.55f) * (0.75f + k * 0.35f);
                // A thin sheet thrown straight up the wall face (little sideways spread), then carried landward by the gale.
                ep.position = new Vector3(x, UnityEngine.Random.Range(-0.8f, 0.8f), HarborWater.ShoreZ + UnityEngine.Random.Range(0.4f, 1.6f));
                ep.velocity = new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(11f, 26f) * lift, -UnityEngine.Random.Range(0.5f, 3f)) + wind * 0.2f;
                // One in four is a big slow billow that forms the crown of the burst; the rest are the thrown sheet.
                bool crown = UnityEngine.Random.value < 0.25f;
                if (crown) ep.velocity = new Vector3(ep.velocity.x, ep.velocity.y * 0.55f, ep.velocity.z * 0.6f);
                ep.startSize = crown ? UnityEngine.Random.Range(4f, 7f) : UnityEngine.Random.Range(1.8f, 4.6f);
                ep.startLifetime = crown ? UnityEngine.Random.Range(2.4f, 3.6f) : UnityEngine.Random.Range(1.6f, 2.8f);
                // Per-particle alpha stays ≤ 0.34 (crown ≤ 0.18), so even dense spray never stacks into a solid blob.
                ep.startColor = new Color(0.88f, 0.9f, 0.92f, crown ? UnityEngine.Random.Range(0.1f, 0.18f) : UnityEngine.Random.Range(0.18f, 0.34f));
                ep.rotation = UnityEngine.Random.Range(0f, 360f);
                s.plume.Emit(ep, 1);
            }
        }

        /// <summary>
        /// Physics on the player's car while it is in floodwater: the water drags the car toward the flow's velocity in
        /// proportion to depth, split into a component along the car (drag, capped) and across it (the shove, capped).
        /// Applied at the centre of mass: no torque, so it cannot roll or flip the car.
        /// </summary>
        public void ApplyForces(VehicleController car, float dt)
        {
            if (car == null || car.Body == null) return;
            var body = car.Body;
            if (!Sample(body.position, out float depth, out var flow, out var s)) { LastDepthAtCar = 0f; return; }
            LastDepthAtCar = depth;
            var v = body.linearVelocity;
            var flat = new Vector3(v.x, 0f, v.z);
            var a = (flow - flat) * (depth * flowCoupling);
            var fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
            float along = Mathf.Clamp(Vector3.Dot(a, fwd), -maxAlong, maxAlong * 0.4f);
            var lateral = Vector3.ClampMagnitude(a - fwd * Vector3.Dot(a, fwd), maxLateral);
            var push = fwd * along + lateral;
            body.AddForce(push, ForceMode.Acceleration);
            PeakPush = Mathf.Max(PeakPush, push.magnitude);
            s.peakPush = Mathf.Max(s.peakPush, push.magnitude);
            LensWater = Mathf.Max(LensWater, Mathf.Clamp01(depth * 2.5f) * 0.5f);
        }
    }
}
