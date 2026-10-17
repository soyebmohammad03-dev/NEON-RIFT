using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.HarborRain.EditorTools
{
    /// <summary>
    /// Mesh writer for the port truck (Harbor Rain 2.2): one mesh with a submesh per material, smooth normals on round
    /// parts (tubes, revolved tyres and rims) and hard edges on boxes and extrusions. Every face is wound from the normal
    /// it should have, so callers only state geometry. UVs are in metres / <see cref="Tile"/> (planar per face, or
    /// around / along for round parts), so tiling textures keep their real-world scale. <see cref="M"/> transforms
    /// everything written after it is set.
    /// </summary>
    internal sealed class HarborTruckMesh
    {
        private readonly List<Vector3> v = new(), n = new();
        private readonly List<Vector2> uv = new();
        private readonly List<Material> order = new();
        private readonly Dictionary<Material, List<int>> tris = new();

        public Matrix4x4 M = Matrix4x4.identity;
        public float Tile = 1f;

        public bool IsEmpty => v.Count == 0;
        public IReadOnlyList<Material> Materials => order;

        private List<int> T(Material m)
        {
            if (!tris.TryGetValue(m, out var list)) { tris[m] = list = new List<int>(); order.Add(m); }
            return list;
        }

        private int V(Vector3 p, Vector3 normal, Vector2 u)
        {
            v.Add(M.MultiplyPoint3x4(p));
            n.Add(M.MultiplyVector(normal).normalized);
            uv.Add(u);
            return v.Count - 1;
        }

        private static Vector2 Planar(Vector3 p, Vector3 normal, float tile)
        {
            var a = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector2(p.z, p.y) / tile;
            if (a.y >= a.z) return new Vector2(p.x, p.z) / tile;
            return new Vector2(p.x, p.y) / tile;
        }

        /// <summary>A flat quad facing <paramref name="normal"/> (corner order may be either way round).</summary>
        public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), normal) < 0f) (b, d) = (d, b);
            normal = normal.normalized;
            var t = T(m);
            int i = V(a, normal, Planar(a, normal, Tile)), j = V(b, normal, Planar(b, normal, Tile)), k = V(c, normal, Planar(c, normal, Tile)), l = V(d, normal, Planar(d, normal, Tile));
            t.Add(i); t.Add(j); t.Add(k);
            t.Add(i); t.Add(k); t.Add(l);
        }

        /// <summary>A flat quad with explicit UVs (decals, labels).</summary>
        public void QuadUV(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), normal) < 0f) { (b, d) = (d, b); (ub, ud) = (ud, ub); }
            normal = normal.normalized;
            var t = T(m);
            int i = V(a, normal, ua), j = V(b, normal, ub), k = V(c, normal, uc), l = V(d, normal, ud);
            t.Add(i); t.Add(j); t.Add(k);
            t.Add(i); t.Add(k); t.Add(l);
        }

        public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) (b, c) = (c, b);
            normal = normal.normalized;
            var t = T(m);
            t.Add(V(a, normal, Planar(a, normal, Tile))); t.Add(V(b, normal, Planar(b, normal, Tile))); t.Add(V(c, normal, Planar(c, normal, Tile)));
        }

        /// <summary>Axis-aligned box (local to <see cref="M"/>); <paramref name="bottom"/> false skips the underside.</summary>
        public void Box(Material m, Vector3 min, Vector3 max, bool bottom = true)
        {
            Vector3 P(int x, int y, int z) => new(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
            Quad(m, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), Vector3.right);
            Quad(m, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), Vector3.left);
            Quad(m, P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), Vector3.up);
            if (bottom) Quad(m, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3.down);
            Quad(m, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), Vector3.forward);
            Quad(m, P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0), Vector3.back);
        }

        /// <summary>A box centred on <paramref name="centre"/>, rotated by <paramref name="r"/>.</summary>
        public void OBox(Material m, Vector3 centre, Vector3 size, Quaternion r)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + r * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 X = r * Vector3.right, Y = r * Vector3.up, Z = r * Vector3.forward;
            Quad(m, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), X);
            Quad(m, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -X);
            Quad(m, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Y);
            Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -Y);
            Quad(m, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Z);
            Quad(m, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), -Z);
        }

        /// <summary>A beam of <paramref name="size"/> (x = width, y = height) running from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public void Beam(Material m, Vector3 a, Vector3 b, Vector2 size, Vector3? up = null)
        {
            var d = b - a;
            var r = Quaternion.LookRotation(d.normalized, up ?? (Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up));
            OBox(m, (a + b) * 0.5f, new Vector3(size.x, size.y, d.magnitude), r);
        }

        /// <summary>
        /// A polygon in the (z, y) plane (convex, any winding) extruded along x from <paramref name="x0"/> to <paramref name="x1"/>.
        /// Used for tapered outriggers, the gooseneck web, mudguard lips.
        /// </summary>
        public void Prism(Material m, Vector2[] zy, float x0, float x1, bool caps = true)
        {
            var c = Vector2.zero;
            foreach (var p in zy) c += p;
            c /= zy.Length;
            for (int i = 0; i < zy.Length; i++)
            {
                var p = zy[i]; var q = zy[(i + 1) % zy.Length];
                var e = q - p;
                var nrm = new Vector2(-e.y, e.x);
                if (Vector2.Dot(nrm, (p + q) * 0.5f - c) < 0f) nrm = -nrm;
                Quad(m, new Vector3(x0, p.y, p.x), new Vector3(x1, p.y, p.x), new Vector3(x1, q.y, q.x), new Vector3(x0, q.y, q.x), new Vector3(0f, nrm.y, nrm.x));
            }
            if (!caps) return;
            for (int i = 1; i + 1 < zy.Length; i++)
            {
                Vector3 A(float x, Vector2 p) => new(x, p.y, p.x);
                Tri(m, A(x1, zy[0]), A(x1, zy[i]), A(x1, zy[i + 1]), Vector3.right);
                Tri(m, A(x0, zy[0]), A(x0, zy[i]), A(x0, zy[i + 1]), Vector3.left);
            }
        }

        private static void Basis(Vector3 axis, out Vector3 e1, out Vector3 e2)
        {
            e1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            e2 = Vector3.Cross(axis, e1);   // Cross(e1, e2) = axis
        }

        /// <summary>
        /// A surface of revolution about <paramref name="axis"/> through <paramref name="centre"/>. <paramref name="profile"/>
        /// is (distance along the axis, radius); each profile segment gets its own ring (hard edge across the profile,
        /// smooth around it). Faces point to the right of the profile direction: walk it outward-side-on (e.g. along the
        /// axis for a tube's outside, radius → 0 at the far end for an end cap).
        /// </summary>
        public void Revolve(Material m, Vector3 centre, Vector3 axis, Vector2[] profile, int segments, float arc = 360f, float arcStart = 0f)
        {
            axis = axis.normalized;
            Basis(axis, out var e1, out var e2);
            var t = T(m);
            float vAcc = 0f;
            for (int k = 0; k + 1 < profile.Length; k++)
            {
                var p0 = profile[k]; var p1 = profile[k + 1];
                float ds = p1.x - p0.x, dr = p1.y - p0.y, len = Mathf.Sqrt(ds * ds + dr * dr);
                if (len < 1e-5f) continue;
                int start = v.Count;
                for (int j = 0; j <= segments; j++)
                {
                    float a = (arcStart + arc * j / segments) * Mathf.Deg2Rad;
                    var radial = e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a);
                    var nrm = (radial * ds - axis * dr).normalized;
                    if (Vector3.Dot(nrm, nrm) < 0.5f) nrm = radial;
                    float u = arc * Mathf.Deg2Rad * Mathf.Max(p0.y, p1.y) * j / segments / Tile;
                    V(centre + axis * p0.x + radial * p0.y, nrm, new Vector2(u, vAcc / Tile));
                    V(centre + axis * p1.x + radial * p1.y, nrm, new Vector2(u, (vAcc + len) / Tile));
                }
                vAcc += len;
                for (int j = 0; j < segments; j++)
                {
                    int a0 = start + j * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    // Right of the profile direction (axis → radius) is the outward side when the profile runs along +axis.
                    t.Add(a0); t.Add(a1); t.Add(b1);
                    t.Add(a0); t.Add(b1); t.Add(b0);
                }
            }
        }

        /// <summary>A round tube from <paramref name="a"/> to <paramref name="b"/> (smooth sides, flat caps).</summary>
        public void Tube(Material m, Vector3 a, Vector3 b, float r, int segments = 10, bool caps = true)
        {
            float len = Vector3.Distance(a, b);
            if (len < 1e-4f) return;
            var prof = caps ? new[] { new Vector2(0f, 0f), new Vector2(0f, r), new Vector2(len, r), new Vector2(len, 0f) } : new[] { new Vector2(0f, r), new Vector2(len, r) };
            Revolve(m, a, b - a, prof, segments);
        }

        /// <summary>A smooth tube along a polyline (hoses, cables), with a ring at every point.</summary>
        public void Hose(Material m, IList<Vector3> points, float r, int segments = 8)
        {
            var t = T(m);
            int start = v.Count;
            float along = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                var fwd = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                Basis(fwd, out var e1, out var e2);
                if (i > 0) along += Vector3.Distance(points[i - 1], points[i]);
                for (int j = 0; j <= segments; j++)
                {
                    float a = j / (float)segments * Mathf.PI * 2f;
                    var radial = e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a);
                    V(points[i] + radial * r, radial, new Vector2(j / (float)segments, along / Tile));
                }
            }
            int ring = segments + 1;
            for (int i = 0; i + 1 < points.Count; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a0 = start + i * ring + j, a1 = a0 + 1, b0 = a0 + ring, b1 = b0 + 1;
                    t.Add(a0); t.Add(a1); t.Add(b1);
                    t.Add(a0); t.Add(b1); t.Add(b0);
                }
        }

        /// <summary>
        /// Skins a sequence of closed rings (same point count, consistent order) into a smooth surface — the cab shell,
        /// tanks, fairings. Normals are smoothed across the surface and point away from each ring's centroid.
        /// <paramref name="capEnd"/> closes the last ring with a fan (a roof), <paramref name="capStart"/> the first.
        /// </summary>
        public void Loft(Material m, IList<Vector3[]> rings, bool capStart = false, bool capEnd = false)
        {
            var t = T(m);
            int cnt = rings[0].Length, start = v.Count;
            float vAcc = 0f;
            for (int i = 0; i < rings.Count; i++)
            {
                var ring = rings[i];
                var c = Vector3.zero;
                foreach (var p in ring) c += p;
                c /= cnt;
                if (i > 0) vAcc += Vector3.Distance(rings[i - 1][0], ring[0]);
                float uAcc = 0f;
                var prev = rings[Mathf.Max(i - 1, 0)]; var next = rings[Mathf.Min(i + 1, rings.Count - 1)];
                for (int j = 0; j <= cnt; j++)
                {
                    int jj = j % cnt;
                    if (j > 0) uAcc += Vector3.Distance(ring[j - 1], ring[jj]);
                    var along = ring[(jj + 1) % cnt] - ring[(jj - 1 + cnt) % cnt];
                    var up = next[jj] - prev[jj];
                    var nrm = Vector3.Cross(along, up).normalized;
                    var outward = ring[jj] - c;
                    if (Vector3.Dot(nrm, outward) < 0f) nrm = -nrm;
                    if (nrm.sqrMagnitude < 0.5f) nrm = outward.normalized;
                    V(ring[jj], nrm, new Vector2(uAcc / Tile, vAcc / Tile));
                }
            }
            int stride = cnt + 1;
            // Winding from the first quad: front side must face its normal.
            for (int i = 0; i + 1 < rings.Count; i++)
                for (int j = 0; j < cnt; j++)
                {
                    int a = start + i * stride + j, b = a + 1, d = a + stride, e = d + 1;
                    var wa = v[a]; var nrm = n[a] + n[b] + n[d] + n[e];
                    bool flip = Vector3.Dot(Vector3.Cross(v[b] - wa, v[e] - wa), nrm) < 0f;
                    if (!flip) { t.Add(a); t.Add(b); t.Add(e); t.Add(a); t.Add(e); t.Add(d); }
                    else { t.Add(a); t.Add(e); t.Add(b); t.Add(a); t.Add(d); t.Add(e); }
                }
            void Cap(Vector3[] ring, Vector3 nrmHint)
            {
                var c = Vector3.zero;
                foreach (var p in ring) c += p;
                c /= cnt;
                // Local transform already applied by V for the ring points above; caps go through V too.
                var save = M;
                for (int j = 0; j < cnt; j++)
                {
                    var p0 = ring[j]; var p1 = ring[(j + 1) % cnt];
                    M = save;
                    Tri(m, c, p0, p1, nrmHint);
                }
            }
            if (capEnd) { var r = rings[rings.Count - 1]; var below = rings[rings.Count - 2]; Cap(r, Centroid(r) - Centroid(below)); }
            if (capStart) { var r = rings[0]; var above = rings[1]; Cap(r, Centroid(r) - Centroid(above)); }
        }

        private static Vector3 Centroid(Vector3[] ring) { var c = Vector3.zero; foreach (var p in ring) c += p; return c / ring.Length; }

        /// <summary>
        /// A horizontal rounded rectangle at height <paramref name="y"/>: x from −<paramref name="hw"/> to +hw, z from
        /// <paramref name="z0"/> (back) to <paramref name="z1"/> (front), corner radii <paramref name="rBack"/> / <paramref name="rFront"/>.
        /// Counter-clockwise seen from above, starting at the front centre; always 4 × (<paramref name="seg"/> + 1) + 4 points.
        /// </summary>
        public static Vector3[] RoundRectXZ(float y, float hw, float z0, float z1, float rBack, float rFront, int seg)
        {
            var pts = new List<Vector3>();
            void Arc(float cx, float cz, float r, float a0)
            {
                for (int k = 0; k <= seg; k++)
                {
                    float a = (a0 + 90f * k / seg) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(cx + Mathf.Cos(a) * r, y, cz + Mathf.Sin(a) * r));
                }
            }
            pts.Add(new Vector3(0f, y, z1));
            Arc(-hw + rFront, z1 - rFront, rFront, 90f);
            pts.Add(new Vector3(-hw, y, (z0 + z1) * 0.5f));
            Arc(-hw + rBack, z0 + rBack, rBack, 180f);
            pts.Add(new Vector3(0f, y, z0));
            Arc(hw - rBack, z0 + rBack, rBack, 270f);
            pts.Add(new Vector3(hw, y, (z0 + z1) * 0.5f));
            Arc(hw - rFront, z1 - rFront, rFront, 0f);
            return pts.ToArray();
        }

        /// <summary>Copies another writer's geometry in, through this writer's <see cref="M"/>, optionally remapping materials.</summary>
        public void Append(HarborTruckMesh other, System.Func<Material, Material> remap = null)
        {
            foreach (var m in other.order)
            {
                var t = T(remap != null ? remap(m) : m);
                foreach (int i in other.tris[m]) t.Add(V(other.v[i], other.n[i], other.uv[i]));
            }
        }

        public int Triangles { get { int c = 0; foreach (var l in tris.Values) c += l.Count / 3; return c; } }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++) mesh.SetTriangles(tris[order[i]], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
