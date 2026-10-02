using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Accumulates quads for one material, so a whole block's facades become a single mesh (one draw per material).
    /// UVs are in metres divided by the texture's real-world tile size, so textures keep their scale on any box.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<Color> colours = new();
        private readonly List<int> triangles = new();

        public bool IsEmpty => vertices.Count == 0;
        public int VertexCount => vertices.Count;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color? colour = null)
        {
            // a-b-c-d is a loop whose front side is Cross(b − a, d − a); Unity's front faces wind clockwise on screen.
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int k = 0; k < 4; k++) { normals.Add(n); colours.Add(colour ?? Color.white); }
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
        }

        /// <summary>Single triangle; front side is Cross(b − a, c − a).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            for (int k = 0; k < 3; k++) { normals.Add(n); colours.Add(Color.white); }
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
        }

        /// <summary>
        /// Box whose front face (local +Z) sits on <paramref name="frontBottom"/> and extends <paramref name="size"/>.z backwards.
        /// Side faces go to this builder; the top face goes to <paramref name="top"/> (skipped if null).
        /// </summary>
        public void Box(Vector3 frontBottom, Quaternion rotation, Vector3 size, Vector2 tile, Vector2 offset, MeshBuilder top, Vector2 topTile,
                        bool back = true, float vStart = 0f)
        {
            float hx = size.x * 0.5f;
            Vector3 P(float x, float y, float z) => frontBottom + rotation * new Vector3(x, y, z);
            float h = size.y, d = size.z;
            Vector2 UV(float u, float v) => new(u / tile.x + offset.x, (v + vStart) / tile.y + offset.y);
            // Front (+Z)
            Quad(P(-hx, 0, 0), P(hx, 0, 0), P(hx, h, 0), P(-hx, h, 0), UV(0, 0), UV(size.x, 0), UV(size.x, h), UV(0, h));
            // Back (−Z)
            if (back) Quad(P(hx, 0, -d), P(-hx, 0, -d), P(-hx, h, -d), P(hx, h, -d), UV(0, 0), UV(size.x, 0), UV(size.x, h), UV(0, h));
            // Right (+X) and left (−X)
            Quad(P(hx, 0, 0), P(hx, 0, -d), P(hx, h, -d), P(hx, h, 0), UV(size.x, 0), UV(size.x + d, 0), UV(size.x + d, h), UV(size.x, h));
            Quad(P(-hx, 0, -d), P(-hx, 0, 0), P(-hx, h, 0), P(-hx, h, -d), UV(-d, 0), UV(0, 0), UV(0, h), UV(-d, h));
            if (top == null) return;
            Vector2 T(Vector3 world) => new(world.x / topTile.x, world.z / topTile.y);
            Vector3 a = P(-hx, h, 0), b = P(hx, h, 0), c = P(hx, h, -d), e = P(-hx, h, -d);
            top.Quad(a, b, c, e, T(a), T(b), T(c), T(e));
        }

        /// <summary>Axis-aligned box from min to max, every face (used for slabs, walls, props).</summary>
        public void Cuboid(Vector3 min, Vector3 max, float tile, bool bottom = false, Color? colour = null)
        {
            Vector3 s = max - min;
            Vector2 U(float u, float v) => new(u / tile, v / tile);
            Vector3 p000 = min, p100 = new(max.x, min.y, min.z), p010 = new(min.x, max.y, min.z), p110 = new(max.x, max.y, min.z);
            Vector3 p001 = new(min.x, min.y, max.z), p101 = new(max.x, min.y, max.z), p011 = new(min.x, max.y, max.z), p111 = max;
            Quad(p001, p101, p111, p011, U(min.x, min.y), U(max.x, min.y), U(max.x, max.y), U(min.x, max.y), colour);          // +Z
            Quad(p100, p000, p010, p110, U(-max.x, min.y), U(-min.x, min.y), U(-min.x, max.y), U(-max.x, max.y), colour);     // −Z
            Quad(p101, p100, p110, p111, U(max.z, min.y), U(min.z, min.y) + new Vector2(s.z / tile * 2f, 0), U(min.z, max.y) + new Vector2(s.z / tile * 2f, 0), U(max.z, max.y), colour); // +X
            Quad(p000, p001, p011, p010, U(min.z, min.y), U(max.z, min.y), U(max.z, max.y), U(min.z, max.y), colour);          // −X
            Quad(p011, p111, p110, p010, U(min.x, max.z), U(max.x, max.z), U(max.x, min.z), U(min.x, min.z), colour);          // top
            if (bottom) Quad(p000, p100, p101, p001, U(min.x, min.z), U(max.x, min.z), U(max.x, max.z), U(min.x, max.z), colour);
        }

        /// <summary>Oriented box (centre, size, yaw), every face, uv scaled by <paramref name="tile"/>.</summary>
        public void OrientedBox(Vector3 centre, Vector3 size, Quaternion rotation, float tile, Color? colour = null)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector2 U(float u, float v) => new(u / tile, v / tile);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), U(0, 0), U(size.x, 0), U(size.x, size.y), U(0, size.y), colour);
            Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), U(0, 0), U(size.x, 0), U(size.x, size.y), U(0, size.y), colour);
            Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), U(0, 0), U(size.z, 0), U(size.z, size.y), U(0, size.y), colour);
            Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), U(0, 0), U(size.z, 0), U(size.z, size.y), U(0, size.y), colour);
            Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), U(0, 0), U(size.x, 0), U(size.x, size.z), U(0, size.z), colour);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), U(0, 0), U(size.x, 0), U(size.x, size.z), U(0, size.z), colour);
        }

        /// <summary>Flat quad facing <paramref name="rotation"/>·+Z, centred, with an explicit UV rect (signs).</summary>
        public void Panel(Vector3 centre, Quaternion rotation, float width, float height, Rect uv, bool rotateUv = false)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            Vector3 P(float x, float y) => centre + rotation * new Vector3(x, y, 0f);
            Vector2 a = new(uv.xMin, uv.yMin), b = new(uv.xMax, uv.yMin), c = new(uv.xMax, uv.yMax), d = new(uv.xMin, uv.yMax);
            // Seen from the front (+Z of rotation) local −X is the viewer's right, so u runs from +X to −X.
            // Rotated: text reads bottom-to-top (blade signs).
            if (rotateUv) Quad(P(-hw, -hh), P(hw, -hh), P(hw, hh), P(-hw, hh), a, new Vector2(uv.xMin, uv.yMax), c, new Vector2(uv.xMax, uv.yMin));
            else Quad(P(-hw, -hh), P(hw, -hh), P(hw, hh), P(-hw, hh), b, a, d, c);
        }

        /// <summary>Open or capped cylinder around +Y.</summary>
        public void Cylinder(Vector3 bottomCentre, float radius, float height, int segments, bool capTop, float uTile = 1f)
        {
            for (int s = 0; s < segments; s++)
            {
                float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 d0 = new(Mathf.Sin(a0), 0f, Mathf.Cos(a0)), d1 = new(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                Vector3 p0 = bottomCentre + d0 * radius, p1 = bottomCentre + d1 * radius;
                float u0 = s / (float)segments * uTile, u1 = (s + 1) / (float)segments * uTile;
                Quad(p0, p1, p1 + Vector3.up * height, p0 + Vector3.up * height, new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), new Vector2(u0, 1));
                if (capTop)
                {
                    Vector3 top = bottomCentre + Vector3.up * height;
                    Tri(top, p0 + Vector3.up * height, p1 + Vector3.up * height, new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f + d0.x * 0.5f, 0.5f + d0.z * 0.5f), new Vector2(0.5f + d1.x * 0.5f, 0.5f + d1.z * 0.5f));
                }
            }
        }

        /// <summary>Torus around +Y.</summary>
        public void Torus(Vector3 centre, float major, float minor, int majorSegments, int minorSegments)
        {
            for (int i = 0; i < majorSegments; i++)
                for (int j = 0; j < minorSegments; j++)
                {
                    Vector3 P(int a, int b)
                    {
                        float u = a / (float)majorSegments * Mathf.PI * 2f, v = b / (float)minorSegments * Mathf.PI * 2f;
                        Vector3 ring = new(Mathf.Sin(u), 0f, Mathf.Cos(u));
                        return centre + ring * (major + minor * Mathf.Cos(v)) + Vector3.up * (minor * Mathf.Sin(v));
                    }
                    Vector2 T(int a, int b) => new(a / (float)majorSegments * 8f, b / (float)minorSegments);
                    Quad(P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1), T(i, j), T(i + 1, j), T(i + 1, j + 1), T(i, j + 1));
                }
        }

        /// <summary>Flat ring on the XZ plane facing up.</summary>
        public void Ring(Vector3 centre, float inner, float outer, int segments)
        {
            for (int s = 0; s < segments; s++)
            {
                float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 d0 = new(Mathf.Sin(a0), 0f, Mathf.Cos(a0)), d1 = new(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                Quad(centre + d0 * inner, centre + d0 * outer, centre + d1 * outer, centre + d1 * inner,
                     new Vector2(s, 0), new Vector2(s, 1), new Vector2(s + 1, 1), new Vector2(s + 1, 0));
            }
        }

        /// <summary>Flat quad on the ground (y up) from two corners.</summary>
        public void Ground(Vector3 min, Vector3 max, float tile)
        {
            Vector3 a = new(min.x, min.y, min.z), b = new(min.x, min.y, max.z), c = new(max.x, min.y, max.z), d = new(max.x, min.y, min.z);
            Quad(a, b, c, d, new Vector2(a.x, a.z) / tile, new Vector2(b.x, b.z) / tile, new Vector2(c.x, c.z) / tile, new Vector2(d.x, d.z) / tile);
        }

        /// <summary>Flat strip on the ground between two points (lane markings), facing up.</summary>
        public void Strip(Vector3 from, Vector3 to, float width, Color? colour = null)
        {
            Vector3 dir = (to - from).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * (width * 0.5f);
            float len = Vector3.Distance(from, to);
            Quad(from - side, to - side, to + side, from + side, new Vector2(0, 0), new Vector2(0, len), new Vector2(1, len), new Vector2(1, 0), colour);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
