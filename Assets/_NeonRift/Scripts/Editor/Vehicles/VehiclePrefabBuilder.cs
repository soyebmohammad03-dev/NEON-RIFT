using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeonRift.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Vehicles
{
    /// <summary>
    /// Builds a vehicle prefab from a <see cref="VehicleModelSetup"/>: orients and scales the source model,
    /// grounds it at the root pivot, splits wheel geometry into four pivoted wheels and records a <see cref="VehicleRig"/>.
    /// The source model stays an untouched nested prefab; only renderer enable states are overridden.
    /// </summary>
    public static class VehiclePrefabBuilder
    {
        public const string PrefabFolder = "Assets/_NeonRift/Prefabs/Vehicles";
        public const string GeneratedFolder = "Assets/_NeonRift/Art/Vehicles";
        public const string BodyMaterialPath = "Assets/_NeonRift/Data/Physics/PM_VehicleBody.asset";
        /// <summary>Wheels must never hit their own car, triggers or showroom props.</summary>
        private static readonly string[] NonGroundLayers = { "Vehicle", "Trigger", "Showroom", "Ignore Raycast" };

        [MenuItem("Neon Rift/Vehicles/Build All Vehicle Prefabs")]
        public static void BuildAllFromMenu()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:VehicleModelSetup"))
            {
                var setup = AssetDatabase.LoadAssetAtPath<VehicleModelSetup>(AssetDatabase.GUIDToAssetPath(guid));
                try { Debug.Log("[VehicleBuilder] " + Build(setup).Summary); }
                catch (Exception e) { Debug.LogError($"[VehicleBuilder] {setup.name}: {e.Message}"); }
            }
        }

        public sealed class Report
        {
            public string PrefabPath;
            public Vector3 Size;
            public float Scale;
            public readonly List<string> Wheels = new();
            public int Headlights, Taillights, Hidden, Triangles, RemappedMaterials;
            public string Colliders;
            public string Summary =>
                $"{Path.GetFileNameWithoutExtension(PrefabPath)} size={Size:F2} scale={Scale:F3} tris={Triangles} head={Headlights} tail={Taillights} hidden={Hidden} clearcoatRemapped={RemappedMaterials}\n  colliders: {Colliders}\n  " +
                string.Join("\n  ", Wheels);
        }

        private sealed class Part
        {
            public MeshRenderer Renderer;
            public WheelPosition Position;
            public bool IsStatic, IsBlur;
            public readonly Dictionary<int, List<int>> TrianglesBySubMesh = new();
        }

        public static Report Build(VehicleModelSetup setup)
        {
            if (setup.sourceModel == null) throw new InvalidOperationException("No source model.");
            var report = new Report();
            var root = new GameObject(setup.prefabName);
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(setup.sourceModel, root.transform);
                body.name = "Body";
                // Compose with the model's own root transform (glTF exports often carry an axis conversion there).
                body.transform.localRotation = Quaternion.Euler(setup.bodyRotation) * body.transform.localRotation;
                var sourceScale = body.transform.localScale;

                foreach (var a in body.GetComponentsInChildren<Animation>(true)) { a.playAutomatically = false; a.enabled = false; }
                foreach (var a in body.GetComponentsInChildren<Animator>(true)) a.enabled = false;

                var renderers = body.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in renderers)
                    if (Matches(r, setup.hiddenKeywords)) { r.enabled = false; report.Hidden++; }

                var visible = renderers.Where(r => r.enabled).ToList();
                report.RemappedMaterials = GltfMaterialFixer.RemapClearcoat(visible, $"{GeneratedFolder}/{setup.prefabName}/Materials");
                var wheelRenderers = visible.Where(r => Matches(r, setup.wheelKeywords)).ToList();
                if (wheelRenderers.Count == 0) throw new InvalidOperationException("No renderers matched the wheel keywords.");

                if (setup.targetWheelDiameter > 0f)
                {
                    float measured = QuadrantBounds(wheelRenderers, setup).Values.Max(b => b.size.y);
                    report.Scale = setup.targetWheelDiameter / measured;
                    body.transform.localScale = sourceScale * report.Scale;
                }
                else report.Scale = 1f;

                var all = Encapsulate(visible);
                body.transform.localPosition -= new Vector3(all.center.x, all.min.y, all.center.z);
                all = Encapsulate(visible);
                report.Size = all.size;
                report.Triangles = visible.Sum(r => (int)TriangleCount(r));

                var wheelBounds = QuadrantBounds(wheelRenderers, setup);
                if (wheelBounds.Count != 4)
                    throw new InvalidOperationException($"Expected 4 wheel corners, found {wheelBounds.Count}: " +
                        string.Join("; ", wheelBounds.Select(kv => $"{kv.Key} {kv.Value.center:F2}/{kv.Value.size:F2}")) +
                        $" | body offset {body.transform.localPosition:F2} | parts: " + string.Join(", ", wheelRenderers.Select(r => r.name + "@" + r.bounds.center.ToString("F2"))));

                var meshes = new List<Mesh>();
                var wheelsRoot = new GameObject("Wheels").transform;
                wheelsRoot.SetParent(root.transform, false);
                var rigs = new List<WheelRig>();

                foreach (WheelPosition pos in Enum.GetValues(typeof(WheelPosition)))
                {
                    var b = wheelBounds[pos];
                    var pivot = new GameObject("Wheel_" + Abbrev(pos)).transform;
                    pivot.SetParent(wheelsRoot, false);
                    pivot.localPosition = b.center;
                    var spin = new GameObject("Spin").transform;
                    spin.SetParent(pivot, false);
                    var blur = new List<Renderer>();

                    foreach (var part in SplitParts(wheelRenderers, setup).Where(p => p.Position == pos))
                    {
                        var mesh = ExtractMesh(part, b.center, $"{setup.prefabName}_{Abbrev(pos)}_{part.Renderer.name}", out var materials);
                        meshes.Add(mesh);
                        var go = new GameObject(part.Renderer.name);
                        go.transform.SetParent(part.IsStatic ? pivot : spin, false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = materials;
                        if (part.IsBlur) { mr.enabled = false; blur.Add(mr); }
                    }

                    rigs.Add(new WheelRig
                    {
                        Position = pos, Pivot = pivot, Spin = spin,
                        Radius = b.size.y * 0.5f, Width = b.size.x,
                        MotionBlurRenderers = blur.ToArray()
                    });
                    report.Wheels.Add($"{pos}: centre={b.center:F3} radius={b.size.y * 0.5f:F3} width={b.size.x:F3}");
                }

                foreach (var r in wheelRenderers) r.enabled = false;

                var lamps = visible.Where(r => r.enabled && Matches(r, setup.lightKeywords)).ToList();
                var head = lamps.Where(r => r.bounds.center.z > 0.5f).Cast<Renderer>().ToArray();
                var tail = lamps.Where(r => r.bounds.center.z < -0.5f).Cast<Renderer>().ToArray();
                report.Headlights = head.Length;
                report.Taillights = tail.Length;

                root.AddComponent<VehicleRig>().EditorConfigure(body.transform, rigs.ToArray(), head, tail, all.size);
                report.Colliders = AddPhysics(root, visible.Where(r => !wheelRenderers.Contains(r)).ToList(), all);
                Audio.VehicleAudioPrefabs.AddAudio(root);

                string meshFolder = $"{GeneratedFolder}/{setup.prefabName}";
                EnsureFolder(meshFolder);
                SaveMeshes(meshes, $"{meshFolder}/{setup.prefabName}_WheelMeshes.asset");

                EnsureFolder(PrefabFolder);
                report.PrefabPath = $"{PrefabFolder}/{setup.prefabName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, report.PrefabPath, out bool ok);
                if (!ok) throw new InvalidOperationException("Saving prefab failed.");
                return report;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Adds the rigidbody (kinematic until <see cref="VehicleController.Configure"/>), the controller and a
        /// two-box body collider fitted to the body mesh: a lower hull from the sill line to the belt line and a cabin.
        /// </summary>
        private static string AddPhysics(GameObject root, List<MeshRenderer> bodyRenderers, Bounds overall)
        {
            int vehicleLayer = LayerMask.NameToLayer("Vehicle");
            if (vehicleLayer < 0) throw new InvalidOperationException("Layer 'Vehicle' is missing.");

            var points = new List<Vector3>();
            foreach (var r in bodyRenderers)
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) points.Add(m.MultiplyPoint3x4(v));
            }
            if (points.Count == 0) throw new InvalidOperationException("No body vertices for the collider.");

            float height = overall.size.y;
            float belt = overall.min.y + height * 0.55f;
            float sill = Mathf.Max(0.12f, Percentile(points.Select(p => p.y), 0.02f));
            var lower = Box(points, p => true, 0.01f, 0.99f, 0.005f, 0.995f);
            var upper = points.Where(p => p.y > belt).ToList();
            var cabin = upper.Count > 0 ? Box(upper, p => true, 0.05f, 0.95f, 0.05f, 0.95f) : default;
            float roof = upper.Count > 0 ? Percentile(upper.Select(p => p.y), 0.995f) : belt + 0.3f;

            var collision = new GameObject("Collision");
            collision.transform.SetParent(root.transform, false);
            var material = BodyMaterial();
            var hull = collision.AddComponent<BoxCollider>();
            hull.center = new Vector3(0f, (sill + belt) * 0.5f, lower.center);
            hull.size = new Vector3(lower.width, belt - sill, lower.length);
            hull.sharedMaterial = material;
            var cab = collision.AddComponent<BoxCollider>();
            cab.center = new Vector3(0f, (belt + roof) * 0.5f, cabin.center);
            cab.size = new Vector3(cabin.width, roof - belt, cabin.length);
            cab.sharedMaterial = material;

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.mass = 1500f;

            var controller = root.AddComponent<VehicleController>();
            var so = new SerializedObject(controller);
            so.FindProperty("groundLayers").intValue = ~LayerMask.GetMask(NonGroundLayers);
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = vehicleLayer;
            return $"hull {hull.size:F2}@{hull.center:F2}, cabin {cab.size:F2}@{cab.center:F2}";
        }

        private struct Span1D { public float center, width, length; }

        private static Span1D Box(List<Vector3> points, Func<Vector3, bool> filter, float xLo, float xHi, float zLo, float zHi)
        {
            var sel = points.Where(filter).ToList();
            float x0 = Percentile(sel.Select(p => p.x), xLo), x1 = Percentile(sel.Select(p => p.x), xHi);
            float z0 = Percentile(sel.Select(p => p.z), zLo), z1 = Percentile(sel.Select(p => p.z), zHi);
            return new Span1D { center = (z0 + z1) * 0.5f, width = Mathf.Max(Mathf.Abs(x0), Mathf.Abs(x1)) * 2f, length = z1 - z0 };
        }

        private static float Percentile(IEnumerable<float> values, float q)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
        }

        private static PhysicsMaterial BodyMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BodyMaterialPath);
            if (material != null) return material;
            EnsureFolder(Path.GetDirectoryName(BodyMaterialPath).Replace('\\', '/'));
            // Low friction so scraping a wall slides the car along it instead of snagging; no bounce.
            material = new PhysicsMaterial("PM_VehicleBody")
            {
                dynamicFriction = 0.2f,
                staticFriction = 0.25f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            AssetDatabase.CreateAsset(material, BodyMaterialPath);
            return material;
        }

        private static bool Matches(Renderer r, string[] keywords)
        {
            if (keywords == null || keywords.Length == 0) return false;
            var text = HierarchyPath(r.transform) + "|" + string.Join("|", r.sharedMaterials.Where(m => m).Select(m => m.name));
            return keywords.Any(k => !string.IsNullOrWhiteSpace(k) && text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static WheelPosition Classify(Vector3 p) =>
            p.z >= 0f ? (p.x < 0f ? WheelPosition.FrontLeft : WheelPosition.FrontRight)
                      : (p.x < 0f ? WheelPosition.RearLeft : WheelPosition.RearRight);

        private static string Abbrev(WheelPosition p) => p switch
        {
            WheelPosition.FrontLeft => "FL",
            WheelPosition.FrontRight => "FR",
            WheelPosition.RearLeft => "RL",
            _ => "RR"
        };

        private static List<Part> SplitParts(List<MeshRenderer> renderers, VehicleModelSetup setup)
        {
            var parts = new List<Part>();
            foreach (var r in renderers)
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var m = r.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                bool isStatic = Matches(r, setup.staticWheelKeywords);
                bool isBlur = Matches(r, setup.motionBlurWheelKeywords);
                var byPos = new Dictionary<WheelPosition, Part>();
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var tris = mesh.GetTriangles(sm);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        var c = (m.MultiplyPoint3x4(verts[tris[i]]) + m.MultiplyPoint3x4(verts[tris[i + 1]]) + m.MultiplyPoint3x4(verts[tris[i + 2]])) / 3f;
                        var pos = Classify(c);
                        if (!byPos.TryGetValue(pos, out var part))
                            byPos[pos] = part = new Part { Renderer = r, Position = pos, IsStatic = isStatic, IsBlur = isBlur };
                        if (!part.TrianglesBySubMesh.TryGetValue(sm, out var list)) part.TrianglesBySubMesh[sm] = list = new List<int>();
                        list.Add(tris[i]); list.Add(tris[i + 1]); list.Add(tris[i + 2]);
                    }
                }
                parts.AddRange(byPos.Values);
            }
            return parts;
        }

        /// <summary>World-space bounds of the spinning wheel geometry at each corner.</summary>
        private static Dictionary<WheelPosition, Bounds> QuadrantBounds(List<MeshRenderer> renderers, VehicleModelSetup setup)
        {
            var result = new Dictionary<WheelPosition, Bounds>();
            foreach (var part in SplitParts(renderers, setup).Where(p => !p.IsStatic))
            {
                var mesh = part.Renderer.GetComponent<MeshFilter>().sharedMesh;
                var m = part.Renderer.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                foreach (var idx in part.TrianglesBySubMesh.Values.SelectMany(x => x))
                {
                    var p = m.MultiplyPoint3x4(verts[idx]);
                    if (result.TryGetValue(part.Position, out var b)) { b.Encapsulate(p); result[part.Position] = b; }
                    else result[part.Position] = new Bounds(p, Vector3.zero);
                }
            }
            return result;
        }

        private static Mesh ExtractMesh(Part part, Vector3 origin, string name, out Material[] materials)
        {
            var src = part.Renderer.GetComponent<MeshFilter>().sharedMesh;
            var m = part.Renderer.transform.localToWorldMatrix;
            bool mirrored = m.determinant < 0f;
            var srcVerts = src.vertices;
            var srcNormals = src.normals;
            var srcTangents = src.tangents;
            var srcColors = src.colors;
            var uvChannels = new List<List<Vector4>>();
            for (int ch = 0; ch < 4; ch++)
            {
                if (!src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch)) break;
                var list = new List<Vector4>();
                src.GetUVs(ch, list);
                uvChannels.Add(list);
            }

            var remap = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color>();
            var uvs = uvChannels.Select(_ => new List<Vector4>()).ToList();
            var subMeshes = new List<List<int>>();
            var mats = new List<Material>();

            foreach (var kv in part.TrianglesBySubMesh.OrderBy(k => k.Key))
            {
                var indices = new List<int>(kv.Value.Count);
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    int s = kv.Value[i];
                    if (!remap.TryGetValue(s, out int d))
                    {
                        d = verts.Count;
                        remap[s] = d;
                        verts.Add(m.MultiplyPoint3x4(srcVerts[s]) - origin);
                        if (srcNormals.Length > 0) normals.Add(m.MultiplyVector(srcNormals[s]).normalized);
                        if (srcTangents.Length > 0)
                        {
                            var t = srcTangents[s];
                            var tv = m.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                            tangents.Add(new Vector4(tv.x, tv.y, tv.z, mirrored ? -t.w : t.w));
                        }
                        if (srcColors.Length > 0) colors.Add(srcColors[s]);
                        for (int ch = 0; ch < uvChannels.Count; ch++) uvs[ch].Add(uvChannels[ch][s]);
                    }
                    indices.Add(d);
                }
                if (mirrored)
                    for (int i = 0; i < indices.Count; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                subMeshes.Add(indices);
                var shared = part.Renderer.sharedMaterials;
                mats.Add(kv.Key < shared.Length ? shared[kv.Key] : shared.LastOrDefault());
            }

            var mesh = new Mesh { name = name };
            if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            if (normals.Count == verts.Count) mesh.SetNormals(normals);
            if (tangents.Count == verts.Count) mesh.SetTangents(tangents);
            if (colors.Count == verts.Count) mesh.SetColors(colors);
            for (int ch = 0; ch < uvs.Count; ch++) mesh.SetUVs(ch, uvs[ch]);
            mesh.subMeshCount = subMeshes.Count;
            for (int i = 0; i < subMeshes.Count; i++) mesh.SetTriangles(subMeshes[i], i);
            mesh.RecalculateBounds();
            materials = mats.ToArray();
            return mesh;
        }

        private static Bounds Encapsulate(IEnumerable<Renderer> renderers)
        {
            Bounds? b = null;
            foreach (var r in renderers)
            {
                if (b == null) b = r.bounds;
                else { var x = b.Value; x.Encapsulate(r.bounds); b = x; }
            }
            return b ?? new Bounds();
        }

        private static long TriangleCount(Renderer r)
        {
            var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return 0;
            long n = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) n += mesh.GetIndexCount(i) / 3;
            return n;
        }

        private static void SaveMeshes(List<Mesh> meshes, string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(meshes[0], path);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], path);
            AssetDatabase.SaveAssets();
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
