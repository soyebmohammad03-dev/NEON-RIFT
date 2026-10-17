using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// One breaking wave in front of a seawall section, drawn by <c>NeonRift/HarborBreaker</c>. The mesh is a grid of
    /// columns across the wave and rows along its cross-section; every frame <see cref="Pose"/> turns the wave's state
    /// (distance from the wall, height, curl, collapse) into 16 cross-section control points for the centre and 16 for the
    /// flanks, which lag so the break peels along the wall. <see cref="Point"/> evaluates the same surface on the CPU
    /// (identical maths to the shader) so the spray leaves the real crest. Also emits wind-blown spray off the lip and the
    /// splash where the lip lands.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HarborBreaker : MonoBehaviour
    {
        public const int Points = 16;
        /// <summary>Cross-section parameter of the crest and of the lip's leading edge.</summary>
        public const float CrestS = 5f / 15f, LipS = 8f / 15f, TipS = 9f / 15f;

        [SerializeField] private float centreX;
        [SerializeField] private float wallZ = 300.4f;
        [SerializeField] private float halfWidth = 64f;
        [SerializeField] private float flankLag = 10f;
        [SerializeField] private int columns = 64;
        [SerializeField] private int rows = 112;
        [SerializeField] private float seed = 3f;
        [Header("Spray")]
        [SerializeField] private ParticleSystem crestSpray;
        [SerializeField] private ParticleSystem plungeSplash;
        [SerializeField] private StormController storm;

        private readonly Vector4[] prof = new Vector4[Points];
        private readonly Vector2[] centre = new Vector2[Points], flank = new Vector2[Points];
        private MaterialPropertyBlock block;
        private MeshRenderer meshRenderer;
        private float crestZ, height, curl, collapse, sink, oldFoam, trail, lastCollapse;
        private bool visible;

        private static readonly int ProfId = Shader.PropertyToID("_Prof");
        private static readonly int AId = Shader.PropertyToID("_BrA");
        private static readonly int BId = Shader.PropertyToID("_BrB");
        private static readonly int CId = Shader.PropertyToID("_BrC");

        public float CentreX => centreX;
        public float HalfWidth => halfWidth;
        public float CrestZ => crestZ;
        public float Height => height;
        public float Curl => curl;
        public float Collapse => collapse;
        public bool Visible => visible;
        public int TriangleCount { get; private set; }

#if UNITY_EDITOR
        public void EditorConfigure(float x, float wall, float half, ParticleSystem spray, ParticleSystem splash, StormController stormController, float noiseSeed)
        {
            centreX = x; wallZ = wall; halfWidth = half; crestSpray = spray; plungeSplash = splash; storm = stormController; seed = noiseSeed;
        }
#endif

        private void Awake()
        {
            var mesh = new Mesh { name = "HR_BreakerGrid" };
            int nx = columns + 1, ns = rows + 1;
            var verts = new Vector3[nx * ns];
            var uvs = new Vector2[nx * ns];
            for (int j = 0; j < ns; j++)
            for (int i = 0; i < nx; i++)
            {
                float u = i / (float)columns * 2f - 1f;
                // Denser columns toward the middle, where the curl is.
                float x = Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), 1.25f) * halfWidth;
                verts[j * nx + i] = new Vector3(x, 0f, 0f);
                uvs[j * nx + i] = new Vector2(j / (float)rows, u);
            }
            var tris = new int[columns * rows * 6];
            int k = 0;
            for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                int a = j * nx + i;
                tris[k++] = a; tris[k++] = a + nx; tris[k++] = a + 1;
                tris[k++] = a + 1; tris[k++] = a + nx; tris[k++] = a + nx + 1;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            // Positions are computed in world space on the GPU: bounds cover the wave's whole run in.
            mesh.bounds = new Bounds(new Vector3(centreX, 6f, wallZ + 160f), new Vector3(halfWidth * 2f + 20f, 40f, 360f));
            TriangleCount = tris.Length / 3;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Hide();
        }

        public void Hide()
        {
            visible = false;
            height = curl = collapse = 0f;
            if (meshRenderer != null) meshRenderer.enabled = false;
        }

        /// <summary>
        /// Sets the wave for this frame. <paramref name="distance"/>: crest to the wall face (m); <paramref name="h"/>:
        /// crest height above the sea (m); back / front: slope lengths (m); <paramref name="c"/>: curl 0..1;
        /// <paramref name="k"/>: collapse 0..1; <paramref name="sinkMetres"/>: lowers the whole wave as it dies.
        /// </summary>
        public void Pose(float distance, float h, float back, float front, float c, float k, float sinkMetres, float backFoam, float foamTrail)
        {
            crestZ = wallZ + distance;
            height = h; curl = c; collapse = k; sink = sinkMetres; oldFoam = backFoam; trail = foamTrail;
            Build(h, back, front, c, k, centre);
            Build(h * 0.82f, back, front * 1.2f, Mathf.Clamp01(c * 1.4f - 0.55f), Mathf.Clamp01(k * 1.3f - 0.35f), flank);
            for (int i = 0; i < Points; i++) prof[i] = new Vector4(centre[i].x, centre[i].y, flank[i].x, flank[i].y);
            visible = true;
            meshRenderer.enabled = true;
            block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            block.SetVectorArray(ProfId, prof);
            block.SetVector(AId, new Vector4(centreX, crestZ, halfWidth, wallZ));
            block.SetVector(BId, new Vector4(h, c, k, oldFoam));
            block.SetVector(CId, new Vector4(flankLag, seed, sink, trail));
            meshRenderer.SetPropertyBlock(block);
        }

        // Normalised cross-sections: back slope (× back length, × height) shared by all shapes; the front as a plain slope
        // (× front length) and as a curling lip and tube (× height). The last point of each sinks under the sea.
        private static readonly float[] BackF = { -1.7f, -1f, -0.55f, -0.25f, -0.08f, 0f };
        private static readonly float[] BackY = { 0f, 0.04f, 0.3f, 0.66f, 0.93f, 1f };
        private static readonly float[] SlopeF = { 0.10f, 0.22f, 0.34f, 0.45f, 0.55f, 0.65f, 0.75f, 0.86f, 1.0f, 1.6f };
        private static readonly float[] SlopeY = { 0.95f, 0.84f, 0.70f, 0.56f, 0.44f, 0.33f, 0.22f, 0.12f, 0.03f, 0f };
        private static readonly float[] CurlF = { 0.30f, 0.56f, 0.70f, 0.66f, 0.56f, 0.38f, 0.16f, 0.06f, 0.30f, 0.9f };
        private static readonly float[] CurlY = { 0.985f, 0.86f, 0.62f, 0.40f, 0.56f, 0.70f, 0.58f, 0.30f, 0.02f, 0f };
        private const float Sunk = -0.9f;

        private static void Build(float h, float back, float front, float c, float k, Vector2[] o)
        {
            float cs = c * c * (3f - 2f * c);
            for (int i = 0; i < 6; i++) o[i] = new Vector2(BackF[i] * back, i == 0 ? Sunk : BackY[i] * h);
            for (int j = 0; j < 10; j++)
            {
                float y0 = j == 9 ? Sunk : SlopeY[j] * h, y1 = j == 9 ? Sunk : CurlY[j] * h;
                o[6 + j] = new Vector2(Mathf.Lerp(SlopeF[j] * front, CurlF[j] * h, cs), Mathf.Lerp(y0, y1, cs));
            }
            if (k <= 0f) return;
            // Collapse: the lip plunges forward and the mass turns into a low, rounded bore of white water.
            float hb = Mathf.Max(1.2f, h * 0.3f), fb = 3f + h * 0.3f, shift = h * 0.5f;
            float ks = k * k * (3f - 2f * k);
            for (int i = 0; i < 6; i++)
            {
                var b = new Vector2(BackF[i] * back * 0.7f + shift, i == 0 ? Sunk : BackY[i] * hb);
                o[i] = Vector2.Lerp(o[i], b, ks);
            }
            for (int j = 0; j < 10; j++)
            {
                var b = new Vector2(SlopeF[j] * fb + shift, j == 9 ? Sunk : SlopeY[j] * hb);
                o[6 + j] = Vector2.Lerp(o[6 + j], b, ks);
            }
        }

        private Vector2 Ctrl(int i, float e) => Vector2.Lerp(centre[i], flank[i], e);

        private Vector2 Profile(float s, float e)
        {
            float fi = Mathf.Clamp01(s) * 15f;
            int i = Mathf.Min(Mathf.FloorToInt(fi), 14);
            float u = fi - i;
            Vector2 p0 = Ctrl(Mathf.Max(i - 1, 0), e), p1 = Ctrl(i, e), p2 = Ctrl(i + 1, e), p3 = Ctrl(Mathf.Min(i + 2, 15), e);
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        private static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>World position of the wave surface at lateral offset <paramref name="x"/> (m) and cross-section
        /// parameter <paramref name="s"/> — the same evaluation as the vertex shader.</summary>
        public Vector3 Point(float x, float s)
        {
            float xn = Mathf.Abs(x) / Mathf.Max(halfWidth, 1f);
            float env = 1f - Smooth(0.5f, 1f, xn);
            float e = Smooth(0.1f, 1f, xn);
            var p = Profile(s, e);
            float t = HarborWater.Time;
            float n1 = HarborWater.Noise(new Vector2(x * 0.035f + seed, t * 0.12f)) - 0.5f;
            float n2 = HarborWater.Noise(new Vector2(x * 0.16f + seed * 3.1f, t * 0.35f)) - 0.5f;
            p.y *= Mathf.Lerp(1f, 0.6f, Smooth(0.1f, 0.9f, xn)) * (1f + n1 * 0.34f + n2 * 0.12f);
            p.x += n1 * 6f + n2 * 1.2f;
            float wx = centreX + x;
            float wz = Mathf.Max(crestZ - p.x + flankLag * xn * xn, wallZ);
            var d = HarborWater.SeaDisplace(new Vector2(wx, wz));
            float y = HarborWater.Level + d.y + env * p.y + (1f - env) * -0.8f - sink;
            return new Vector3(wx + d.x * (1f - env), y, wz + d.z * (1f - env));
        }

        private void Update()
        {
            if (!visible) { lastCollapse = 0f; return; }
            float dt = Time.deltaTime;
            Vector3 wind = storm != null ? storm.Wind : new Vector3(-6f, 0f, -8f);
            // Spray torn off the lip by the onshore gale: more as the crest steepens and curls.
            if (crestSpray != null && height > 2.5f && collapse < 0.9f)
            {
                float rate = (Mathf.InverseLerp(2.5f, 7f, height) * 40f + curl * 160f) * (1f - collapse);
                int n = Mathf.FloorToInt(rate * dt + Random.value);
                var ep = new ParticleSystem.EmitParams();
                for (int i = 0; i < n; i++)
                {
                    float x = (Random.value * 2f - 1f) * halfWidth * 0.6f;
                    ep.position = Point(x, Mathf.Lerp(CrestS, LipS, Random.value * curl));
                    ep.velocity = wind * Random.Range(0.35f, 0.7f) + new Vector3(0f, Random.Range(1.5f, 5f), -Random.Range(2f, 6f) * (0.5f + curl));
                    ep.startSize = Random.Range(0.8f, 2.6f);
                    ep.startLifetime = Random.Range(0.9f, 1.8f);
                    ep.startColor = new Color(0.85f, 0.88f, 0.9f, Random.Range(0.15f, 0.3f));
                    crestSpray.Emit(ep, 1);
                }
            }
            // The lip lands: white water thrown up all along the plunge line — thin, separate jets (per-particle alpha is
            // low, so a burst can never stack into one opaque ball), strongest where the wave peaks.
            if (plungeSplash != null && collapse > 0.04f && lastCollapse <= 0.04f)
            {
                var ep = new ParticleSystem.EmitParams();
                for (int i = 0; i < 140; i++)
                {
                    float x = (Random.value * 2f - 1f) * halfWidth * 0.85f;
                    float peak = 1f - Mathf.Abs(x) / halfWidth * 0.7f;
                    var p = Point(x, TipS);
                    p.y = Mathf.Max(p.y, HarborWater.Level + 0.5f);
                    ep.position = p;
                    ep.velocity = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(5f, 13f) * peak, -Random.Range(3f, 7f)) + wind * 0.25f;
                    ep.startSize = Random.Range(1.4f, 3.6f);
                    ep.startLifetime = Random.Range(1.1f, 2f);
                    ep.startColor = new Color(0.85f, 0.88f, 0.9f, Random.Range(0.12f, 0.26f));
                    plungeSplash.Emit(ep, 1);
                }
            }
            lastCollapse = collapse;
        }
    }
}
