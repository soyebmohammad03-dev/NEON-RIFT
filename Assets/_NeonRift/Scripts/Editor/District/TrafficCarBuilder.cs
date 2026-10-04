using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NeonRift.EditorTools.Vehicles;
using NeonRift.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// Turns the generic passenger car pack (one FBX: ten bodies in a ring, each with four loose wheels) into ten
    /// traffic prefabs: body and its four nearest wheels re-framed so the car sits on the ground at the origin facing
    /// +Z, URP materials from the pack's textures, a box collider, a Rigidbody and a <see cref="TrafficCar"/>
    /// (raycast suspension, lane driving, engine loop and impact sounds). Layer: Traffic.
    /// </summary>
    public static class TrafficCarBuilder
    {
        public const string PackFolder = "Assets/ThirdParty/Sketchfab/Vehicles/generic-passenger-car-pack";
        public const string PrefabFolder = "Assets/_NeonRift/Prefabs/Traffic";
        private const string MaterialFolder = "Assets/_NeonRift/Art/Traffic";

        // Body name in the FBX → texture prefix and whether the derived forward has to be flipped.
        private static readonly (string body, string kind, bool flip)[] Cars =
        {
            ("Compact Body", "Compact", false), ("Coupe Body", "Coupe", false), ("Hatchback Body", "Hatchback", false),
            ("minivan body", "Minivan", false), ("Offroad Body", "Offroad", false), ("Pickup Body", "Pickup", false),
            ("Sedan Body", "Sedan", false), ("Sport body", "Sport", false), ("SUV Body", "SUV", false), ("Wagon Body", "Wagon", false),
        };

        /// <summary>Kinds whose derived forward points at the tail (fixed after a visual check).</summary>
        public static readonly HashSet<string> Flipped = new();

        public static List<GameObject> Build(StringBuilder log, AudioClip engineLoop, AudioClip[] impacts)
        {
            VehiclePrefabBuilder.EnsureFolder(PrefabFolder);
            VehiclePrefabBuilder.EnsureFolder(MaterialFolder);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{PackFolder}/source/fab.fbx");
            if (fbx == null) { log.AppendLine("  traffic: car pack FBX missing"); return new List<GameObject>(); }
            var wheels = fbx.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("Wheel")).ToList();
            var glass = Lit("Traffic_PackGlass", new Color(0.04f, 0.05f, 0.06f), null, 0f, 0.95f);
            var optics = Lit("Traffic_PackOptics", new Color(0.9f, 0.9f, 0.85f), Tex("lights"), 0f, 0.9f);
            optics.EnableKeyword("_EMISSION");
            optics.SetTexture("_EmissionMap", Tex("lights"));
            optics.SetColor("_EmissionColor", new Color(1.6f, 1.45f, 1.2f));
            optics.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            int layer = LayerMask.NameToLayer("Traffic");
            var prefabs = new List<GameObject>();
            foreach (var (bodyName, kind, flip) in Cars)
            {
                var bodyFilter = fbx.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m => m.name == bodyName);
                if (bodyFilter == null) { log.AppendLine($"  traffic: {bodyName} missing"); continue; }
                var bodyT = bodyFilter.transform;
                Vector3 bodyCentre = bodyFilter.GetComponent<Renderer>().bounds.center;
                // The four wheels nearest the body (they are laid out around it in the pack).
                var own = wheels.OrderBy(w => Flat(w.GetComponent<Renderer>().bounds.center - bodyCentre).sqrMagnitude).Take(4).ToList();
                Vector3 centre = own.Aggregate(Vector3.zero, (a, w) => a + w.GetComponent<Renderer>().bounds.center) / own.Count;
                // Axle = the wheel mesh's thin axis (local X); forward is perpendicular to it.
                Vector3 axle = Flat(own[0].transform.TransformDirection(Vector3.right)).normalized;
                Vector3 forward = Vector3.Cross(axle, Vector3.up).normalized;
                if (flip || Flipped.Contains(kind)) forward = -forward;
                var frame = Matrix4x4.TRS(new Vector3(centre.x, 0f, centre.z), Quaternion.LookRotation(forward, Vector3.up), Vector3.one).inverse;

                var root = new GameObject($"PackCar_{kind}") { layer = layer };
                var model = new GameObject("Model") { layer = layer };
                model.transform.SetParent(root.transform, false);
                var body = Copy(bodyFilter, model.transform, frame, layer);
                var bodyMat = Lit($"Traffic_Pack{kind}", Color.white, BodyTexture(kind), 0f, 0.62f);
                var metallic = Tex($"{kind}_Metallic");
                if (metallic != null) { bodyMat.SetTexture("_MetallicGlossMap", metallic); bodyMat.EnableKeyword("_METALLICSPECGLOSSMAP"); bodyMat.SetFloat("_Smoothness", 0.62f); }
                body.GetComponent<MeshRenderer>().sharedMaterials = bodyFilter.GetComponent<MeshRenderer>().sharedMaterials
                    .Select(m => m == null ? bodyMat : m.name.StartsWith("Glass") ? glass : m.name.StartsWith("Optics") ? optics : bodyMat).ToArray();

                var wheelTs = new List<Transform>();
                float radius = 0.35f;
                foreach (var w in own)
                {
                    // Each wheel gets a pivot at its hub so it can spin about the axle.
                    var hubWorld = w.GetComponent<Renderer>().bounds.center;
                    var pivot = new GameObject("Wheel") { layer = layer };
                    pivot.transform.SetParent(model.transform, false);
                    pivot.transform.localPosition = frame.MultiplyPoint3x4(hubWorld);
                    var wheel = Copy(w, pivot.transform, Matrix4x4.Translate(-pivot.transform.localPosition) * frame, layer);
                    string letter = w.sharedMesh.name.Length > 6 ? w.sharedMesh.name.Substring(6, 1) : "A";
                    var wheelMat = Lit($"Traffic_PackWheel_{letter}", Color.white, Tex($"wheel_{letter}_Diffuse"), 0f, 0.35f);
                    wheel.GetComponent<MeshRenderer>().sharedMaterials = Enumerable.Repeat(wheelMat, w.GetComponent<MeshRenderer>().sharedMaterials.Length).ToArray();
                    radius = w.sharedMesh.bounds.size.y * 0.5f;
                    wheelTs.Add(pivot.transform);
                }
                // Ground the car: lowest wheel point at y = 0.
                float lowest = wheelTs.Min(t => t.localPosition.y) - radius;
                model.transform.localPosition = new Vector3(0f, -lowest, 0f);

                // Collider over the body only (not the wheels): the suspension holds the car up.
                var b = new Bounds();
                bool first = true;
                // From the vertices themselves: some pack meshes are baked at an angle, so their local bounds overstate the car.
                foreach (var r in body.GetComponentsInChildren<MeshRenderer>())
                {
                    // Read-only mesh data works on the pack's non-readable import (Mesh.vertices does not, and left
                    // the colliders zero-sized when the meshes were not already cached as readable).
                    using var data = Mesh.AcquireReadOnlyMeshData(r.GetComponent<MeshFilter>().sharedMesh);
                    using var vertices = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetVertices(vertices);
                    foreach (var vertex in vertices)
                    {
                        var p = root.transform.InverseTransformPoint(r.transform.TransformPoint(vertex));
                        if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                    }
                }
                var box = root.AddComponent<BoxCollider>();
                float clearance = radius * 0.9f;
                box.center = new Vector3(b.center.x, (b.max.y + clearance) * 0.5f, b.center.z);
                box.size = new Vector3(b.size.x * 0.96f, b.max.y - clearance, b.size.z * 0.97f);

                var rb = root.AddComponent<Rigidbody>();
                rb.mass = kind is "Pickup" or "SUV" or "Minivan" or "Offroad" ? 1900f : kind is "Sport" or "Coupe" ? 1350f : 1450f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.centerOfMass = new Vector3(0f, radius + 0.25f, b.center.z);
                rb.isKinematic = true;

                var source = root.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 1f;
                source.minDistance = 6f;
                source.maxDistance = 90f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.dopplerLevel = 0.4f;
                source.priority = 200;
                source.volume = 0f;
                var sfx = root.AddComponent<AudioSource>();
                sfx.playOnAwake = false;
                sfx.spatialBlend = 1f;
                sfx.minDistance = 8f;
                sfx.maxDistance = 120f;
                sfx.priority = 150;

                float halfTrack = wheelTs.Max(t => Mathf.Abs(t.localPosition.x));
                float halfBase = wheelTs.Max(t => Mathf.Abs(t.localPosition.z));
                var car = root.AddComponent<TrafficCar>();
                car.EditorConfigure(rb, wheelTs.ToArray(), radius, new Vector2(halfTrack, halfBase), b.size, source, sfx, engineLoop, impacts,
                    new[] { body.GetComponent<MeshRenderer>() });

                string path = $"{PrefabFolder}/PackCar_{kind}.prefab";
                prefabs.Add(PrefabUtility.SaveAsPrefabAsset(root, path));
                Object.DestroyImmediate(root);
            }
            log.AppendLine($"  traffic cars: {prefabs.Count} prefabs from the generic passenger car pack ({PrefabFolder})");
            return prefabs;
        }

        private static GameObject Copy(MeshFilter source, Transform parent, Matrix4x4 frame, int layer)
        {
            var go = new GameObject(source.name) { layer = layer };
            go.transform.SetParent(parent, false);
            var m = frame * source.transform.localToWorldMatrix;
            go.transform.localPosition = m.GetColumn(3);
            go.transform.localRotation = m.rotation;
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private static Texture2D BodyTexture(string kind)
        {
            var guids = AssetDatabase.FindAssets(kind, new[] { $"{PackFolder}/textures" });
            // The colour map is "<Kind><Colour>.png" (no underscore), e.g. SedanYellow.
            return guids.Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileNameWithoutExtension(p).StartsWith(kind) && !Path.GetFileNameWithoutExtension(p).Contains("_"))
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault();
        }

        private static Texture2D Tex(string name)
        {
            var guids = AssetDatabase.FindAssets(name, new[] { $"{PackFolder}/textures" });
            return guids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, System.StringComparison.OrdinalIgnoreCase))
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault();
        }

        private static Material Lit(string name, Color colour, Texture2D map, float metallic, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", colour);
            m.SetTexture("_BaseMap", map);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
