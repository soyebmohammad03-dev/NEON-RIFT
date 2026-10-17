using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.HarborRain
{
    /// <summary>
    /// The open sea: one procedural grid (dense near the camera, ~3.5 m spacing, widening to ~150 m at a 6 km radius) that
    /// follows the camera in 8 m steps. All wave motion is in <c>NeonRift/HarborOcean</c> on the GPU; this component only
    /// feeds it (and the breakers / road water, through <see cref="HarborWater"/>) the clock, sea level, storm intensity,
    /// lightning flash and the foam each wave section leaves on the sea.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HarborOcean : MonoBehaviour
    {
        [SerializeField] private int resolution = 240;
        [SerializeField] private float radius = 6000f;
        [SerializeField, Range(0.02f, 0.5f)] private float linearShare = 0.08f;
        [SerializeField] private float snap = 8f;
        [SerializeField] private StormController storm;
        [SerializeField] private HarborWaves waves;
        [SerializeField] private float level = -2.2f;
        [SerializeField] private float shoreZ = 300f;

        private static readonly int SurgeId = Shader.PropertyToID("_HR_Surge");
        private static readonly int SurgeFoamId = Shader.PropertyToID("_HR_SurgeFoam");
        private readonly Vector4[] surges = new Vector4[4], foams = new Vector4[4];
        private Transform follow;

        public int VertexCount { get; private set; }
        public Vector4 Surge(int i) => surges[i];

        private void Awake()
        {
            var mesh = new Mesh { name = "HR_OceanGrid", indexFormat = IndexFormat.UInt32 };
            int n = resolution + 1;
            var verts = new Vector3[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                verts[z * n + x] = new Vector3(Map(x / (float)resolution), 0f, Map(z / (float)resolution));
            var tris = new int[resolution * resolution * 6];
            int k = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int a = z * n + x;
                tris[k++] = a; tris[k++] = a + n; tris[k++] = a + 1;
                tris[k++] = a + 1; tris[k++] = a + n; tris[k++] = a + n + 1;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(radius * 2f, 200f, radius * 2f));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            VertexCount = verts.Length;
        }

        /// <summary>Grid coordinate 0..1 → metres: linear near the centre, cubic toward the horizon.</summary>
        private float Map(float u)
        {
            float s = u * 2f - 1f;
            return radius * (linearShare * s + (1f - linearShare) * s * s * s);
        }

        private void LateUpdate()
        {
            if (follow == null && Camera.main != null) follow = Camera.main.transform;
            if (follow != null)
            {
                var p = follow.position;
                transform.position = new Vector3(Mathf.Round(p.x / snap) * snap, 0f, Mathf.Round(p.z / snap) * snap);
            }
        }

        // Early in the frame, so the breakers and spray evaluate the same clock the GPU will draw with.
        private void Update()
        {
            HarborWater.Push(storm != null ? storm.Intensity : 0.5f, storm != null ? storm.Flash : 0f, level, shoreZ);
            for (int i = 0; i < surges.Length; i++)
            {
                surges[i] = waves != null ? waves.Surge(i) : Vector4.zero;
                foams[i] = waves != null ? waves.SurgeFoam(i) : Vector4.zero;
            }
            Shader.SetGlobalVectorArray(SurgeId, surges);
            Shader.SetGlobalVectorArray(SurgeFoamId, foams);
        }
    }
}
