using System.Collections.Generic;
using System.Linq;
using NeonRift.Gameplay;
using NeonRift.Vehicles;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonRift.EditorTools.TestTrack
{
    /// <summary>
    /// Builds the connected physics-validation loop on the dev test track and wires the scene's vehicle camera and
    /// dev tools. Re-runnable: the "ValidationRoute" object is rebuilt from scratch each time.
    /// Layout (clockwise, 1.55 km): acceleration straight → braking zone → 12° banked R60 hairpin → slalom →
    /// speed bumps → braking zone → R15 tight corner (gravel trap + tyre wall beyond) → R30 corner → start.
    /// </summary>
    public static class ValidationRouteBuilder
    {
        public const string ScenePath = "Assets/_NeonRift/Scenes/Dev/TestTrack.unity";
        private const string ArtFolder = "Assets/_NeonRift/Art/Materials/DevTrack";
        private const string RouteName = "ValidationRoute";

        private const float HalfWidth = 6f;
        private const float RoadHeight = 0.02f;
        private const float SkirtDepth = 0.3f;
        private const float BankAngle = 12f;
        private const float BankTransition = 30f;
        private const float AdvisoryGrip = 0.95f;
        private const float MaxAdvisorySpeed = 70f;
        private static readonly Vector3 Start = new(-300f, 0f, -150f);

        private enum Kind { Straight, Arc }

        private struct Segment
        {
            public string Name;
            public Kind Kind;
            public float Length;      // straight length, m
            public float Radius;      // arc radius, m
            public float Angle;       // arc angle, degrees (positive = right turn)
            public float Bank;        // degrees, arcs only
            public float Speed;       // advisory speed override, m/s (0 = from geometry)
        }

        private struct Sample
        {
            public Vector3 Position;  // centre line at base height
            public Vector3 Forward;
            public float Distance;
            public float Curvature;   // 1/m, + = right
            public int Segment;
        }

        private static readonly Segment[] Layout =
        {
            new() { Name = "Acceleration", Kind = Kind.Straight, Length = 450f },
            new() { Name = "Braking zone", Kind = Kind.Straight, Length = 150f },
            new() { Name = "Banked turn", Kind = Kind.Arc, Radius = 60f, Angle = 180f, Bank = BankAngle },
            new() { Name = "Exit straight", Kind = Kind.Straight, Length = 70f },
            new() { Name = "Slalom", Kind = Kind.Straight, Length = 160f },
            new() { Name = "Speed bumps", Kind = Kind.Straight, Length = 150f, Speed = 15f },
            new() { Name = "Braking zone 2", Kind = Kind.Straight, Length = 235f },
            new() { Name = "Tight corner", Kind = Kind.Arc, Radius = 15f, Angle = 90f },
            new() { Name = "Short straight", Kind = Kind.Straight, Length = 75f },
            new() { Name = "Medium corner", Kind = Kind.Arc, Radius = 30f, Angle = 90f },
        };

        // Slalom cones (metres into the slalom section) and the driving line's weave amplitude.
        private static readonly float[] SlalomCones = { 20f, 38f, 56f, 74f, 92f, 110f, 128f };
        private const float SlalomAmplitude = 1.8f;
        private const float SlalomPitch = 18f;
        // Speed bumps (metres into the bump section): 0 = full width, -1 = left half, 1 = right half.
        private static readonly (float at, int side)[] Bumps = { (15f, 0), (37f, 0), (59f, 0), (81f, 0), (103f, 0), (122f, -1), (138f, 1) };
        private const float BumpHeight = 0.1f;
        private const float BumpRadius = 0.3f;

        [MenuItem("Neon Rift/Test Track/Build Validation Route")]
        public static void BuildFromMenu()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            Debug.Log(Build(scene));
        }

        public static string Build(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == RouteName) Object.DestroyImmediate(root);

            var samples = Walk(out var sectionStarts);
            float length = samples[samples.Count - 1].Distance;

            var route = new GameObject(RouteName);
            SceneManager.MoveGameObjectToScene(route, scene);

            var asphalt = AssetDatabase.LoadAssetAtPath<Material>($"{ArtFolder}/DevTrack_Asphalt.mat");
            var marking = AssetDatabase.LoadAssetAtPath<Material>($"{ArtFolder}/DevTrack_Marking.mat");
            var gravelMat = LitMaterial("DevTrack_Gravel", new Color(0.55f, 0.48f, 0.36f));
            var tyreWallMat = LitMaterial("DevTrack_TyreWall", new Color(0.08f, 0.08f, 0.09f));
            var boardMat = LitMaterial("DevTrack_BrakeBoard", new Color(0.9f, 0.9f, 0.88f));

            var roadMesh = SaveMesh(BuildRoadMesh(samples, sectionStarts), "ValidationRoute_Road");
            var road = MeshObject("Road", route.transform, roadMesh, asphalt, "Drivable");
            road.AddComponent<MeshCollider>().sharedMesh = roadMesh;

            var linesMesh = SaveMesh(BuildLinesMesh(samples, sectionStarts), "ValidationRoute_Lines");
            MeshObject("Markings", route.transform, linesMesh, marking, "Drivable");

            BuildSlalom(route.transform, samples, sectionStarts[4], scene);
            BuildBumps(route.transform, samples, sectionStarts[5], scene);
            BuildBrakeBoards(route.transform, samples, sectionStarts[2], boardMat);
            BuildBrakeBoards(route.transform, samples, sectionStarts[7], boardMat);
            BuildRunoff(route.transform, samples, sectionStarts[7], gravelMat, tyreWallMat);

            var drivingRoute = route.AddComponent<DrivingRoute>();
            BuildDrivingLine(drivingRoute, samples, sectionStarts);

            string scenery = ConfigureScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"[ValidationRoute] {length:0} m loop, {samples.Count} samples, sections: " +
                   string.Join(", ", Enumerable.Range(0, Layout.Length).Select(i => $"{Layout[i].Name}@{sectionStarts[i]:0}")) +
                   "\n" + scenery;
        }

        // ---------------- Geometry ----------------

        private static List<Sample> Walk(out float[] sectionStarts)
        {
            var samples = new List<Sample>();
            sectionStarts = new float[Layout.Length];
            Vector3 p = Start;
            Vector3 f = Vector3.right;
            float s = 0f;
            for (int i = 0; i < Layout.Length; i++)
            {
                var seg = Layout[i];
                sectionStarts[i] = s;
                if (seg.Kind == Kind.Straight)
                {
                    int n = Mathf.CeilToInt(seg.Length / 2f);
                    for (int k = 0; k < n; k++)
                    {
                        samples.Add(new Sample { Position = p, Forward = f, Distance = s, Curvature = 0f, Segment = i });
                        float step = seg.Length / n;
                        p += f * step;
                        s += step;
                    }
                }
                else
                {
                    float sign = Mathf.Sign(seg.Angle);
                    Vector3 right = Vector3.Cross(Vector3.up, f);
                    Vector3 centre = p + right * (seg.Radius * sign);
                    float arc = Mathf.Abs(seg.Angle) * Mathf.Deg2Rad * seg.Radius;
                    int n = Mathf.CeilToInt(arc / 1f);
                    Vector3 radial = p - centre;
                    Vector3 f0 = f;
                    for (int k = 0; k < n; k++)
                    {
                        float a = seg.Angle * k / n;
                        var rot = Quaternion.AngleAxis(a, Vector3.up);
                        samples.Add(new Sample { Position = centre + rot * radial, Forward = rot * f0, Distance = s + arc * k / n, Curvature = sign / seg.Radius, Segment = i });
                    }
                    var end = Quaternion.AngleAxis(seg.Angle, Vector3.up);
                    p = centre + end * radial;
                    f = end * f0;
                    s += arc;
                }
            }
            // Close the loop exactly on the start sample.
            samples.Add(new Sample { Position = Start, Forward = Vector3.right, Distance = s, Curvature = 0f, Segment = 0 });
            if (Vector3.Distance(p, Start) > 0.05f) Debug.LogWarning($"[ValidationRoute] Loop does not close: {p} vs {Start}");
            return samples;
        }

        /// <summary>
        /// Bank angle (degrees, + raises the left edge) at a route distance. The bank is fully built over the whole arc;
        /// it is eased in and out on the straights either side, so no part of the curve loses its banking.
        /// </summary>
        private static float BankAt(float s, float[] sectionStarts)
        {
            float bank = 0f;
            for (int i = 0; i < Layout.Length; i++)
            {
                if (Layout[i].Kind != Kind.Arc || Layout[i].Bank == 0f) continue;
                float a = sectionStarts[i];
                float b = a + Mathf.Abs(Layout[i].Angle) * Mathf.Deg2Rad * Layout[i].Radius;
                float up = Mathf.InverseLerp(a - BankTransition, a, s);
                float down = Mathf.InverseLerp(b + BankTransition, b, s);
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Min(up, down));
                bank = Mathf.Max(bank, Layout[i].Bank * t * Mathf.Sign(Layout[i].Angle));
            }
            return bank;
        }

        /// <summary>Road surface height at a lateral offset (m, + = right) for a bank angle. The inner edge stays at road height.</summary>
        private static float HeightAt(float lateral, float bank)
        {
            if (Mathf.Abs(bank) < 1e-3f) return RoadHeight;
            float t = Mathf.Tan(Mathf.Abs(bank) * Mathf.Deg2Rad);
            float fromInner = bank > 0f ? HalfWidth - lateral : HalfWidth + lateral;
            return RoadHeight + fromInner * t;
        }

        private static Vector3 Surface(in Sample s, float lateral, float bank, float lift = 0f)
        {
            Vector3 right = Vector3.Cross(Vector3.up, s.Forward);
            Vector3 p = s.Position + right * lateral;
            p.y = HeightAt(lateral, bank) + lift;
            return p;
        }

        private static Mesh BuildRoadMesh(List<Sample> samples, float[] starts)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var top = new List<int>();
            var sides = new List<int>();
            int n = samples.Count;
            for (int i = 0; i < n; i++)
            {
                var s = samples[i];
                float bank = BankAt(s.Distance, starts);
                Vector3 l = Surface(s, -HalfWidth, bank), r = Surface(s, HalfWidth, bank);
                float vCoord = s.Distance / 6f;
                v.Add(l); uv.Add(new Vector2(0f, vCoord));
                v.Add(r); uv.Add(new Vector2(HalfWidth * 2f / 6f, vCoord));
                // Sides: a vertical skirt where the road is at ground level; a drivable embankment where it is raised
                // (the outside of the banked turn), so a car running wide slides down a slope instead of off a ledge.
                Vector3 outward = Vector3.Cross(Vector3.up, s.Forward);
                v.Add(l); uv.Add(new Vector2(0f, vCoord));
                v.Add(SideFoot(l, -outward)); uv.Add(new Vector2(0.2f, vCoord));
                v.Add(r); uv.Add(new Vector2(0f, vCoord));
                v.Add(SideFoot(r, outward)); uv.Add(new Vector2(0.2f, vCoord));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 6, b = (i + 1) * 6;
                Quad(top, a + 0, b + 0, b + 1, a + 1);
                Quad(sides, a + 3, b + 3, b + 2, a + 2);
                Quad(sides, a + 4, b + 4, b + 5, a + 5);
            }
            var mesh = new Mesh { name = "ValidationRoute_Road", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 1;
            var all = new List<int>(top);
            all.AddRange(sides);
            mesh.SetTriangles(all, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Embankment slope (horizontal run per metre of height) beside raised road.</summary>
        private const float EmbankmentRun = 2.5f;

        private static Vector3 SideFoot(Vector3 edge, Vector3 outward)
        {
            float height = edge.y - RoadHeight;
            if (height < 0.05f) return new Vector3(edge.x, -SkirtDepth, edge.z);
            Vector3 foot = edge + outward * ((edge.y + SkirtDepth) * EmbankmentRun);
            foot.y = -SkirtDepth;
            return foot;
        }

        private static Mesh BuildLinesMesh(List<Sample> samples, float[] starts)
        {
            var v = new List<Vector3>();
            var tris = new List<int>();
            const float lift = 0.006f;
            // Edge lines.
            foreach (float side in new[] { -1f, 1f })
            {
                float inner = side * (HalfWidth - 0.45f), outer = side * (HalfWidth - 0.3f);
                int first = v.Count;
                foreach (var s in samples)
                {
                    float bank = BankAt(s.Distance, starts);
                    v.Add(Surface(s, Mathf.Min(inner, outer), bank, lift));
                    v.Add(Surface(s, Mathf.Max(inner, outer), bank, lift));
                }
                for (int i = 0; i < samples.Count - 1; i++)
                {
                    int a = first + i * 2, b = first + (i + 1) * 2;
                    Quad(tris, a, b, b + 1, a + 1);
                }
            }
            // Start line and 100 m markers on the acceleration straight, braking-zone markers every 25 m.
            var marks = new List<(float s, float width)> { (0f, 1.2f) };
            for (float d = 100f; d < starts[2]; d += 100f) marks.Add((d, 0.3f));
            foreach (var (s, width) in marks)
            {
                var sample = samples[ClosestIndex(samples, s)];
                Vector3 along = sample.Forward * (width * 0.5f);
                Vector3 l = Surface(sample, -HalfWidth + 0.3f, 0f, lift), r = Surface(sample, HalfWidth - 0.3f, 0f, lift);
                int a = v.Count;
                v.Add(l - along); v.Add(l + along); v.Add(r + along); v.Add(r - along);
                Quad(tris, a, a + 1, a + 2, a + 3);
            }
            var mesh = new Mesh { name = "ValidationRoute_Lines", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
        }

        private static int ClosestIndex(List<Sample> samples, float distance)
        {
            int best = 0;
            for (int i = 1; i < samples.Count; i++)
                if (Mathf.Abs(samples[i].Distance - distance) < Mathf.Abs(samples[best].Distance - distance)) best = i;
            return best;
        }

        private static Sample At(List<Sample> samples, float distance)
        {
            int i = ClosestIndex(samples, distance);
            var s = samples[i];
            s.Position += s.Forward * (distance - s.Distance);
            s.Distance = distance;
            return s;
        }

        // ---------------- Features ----------------

        private static void BuildSlalom(Transform parent, List<Sample> samples, float sectionStart, Scene scene)
        {
            var group = new GameObject("Slalom").transform;
            group.SetParent(parent, false);
            var template = FindInScene(scene, "Slalom_18m/Cone_0");
            for (int i = 0; i < SlalomCones.Length; i++)
            {
                var s = At(samples, sectionStart + SlalomCones[i]);
                GameObject cone;
                if (template != null)
                {
                    cone = Object.Instantiate(template, group);
                    cone.transform.localScale = template.transform.lossyScale;
                }
                else
                {
                    cone = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    cone.transform.SetParent(group, false);
                    cone.transform.localScale = new Vector3(0.4f, 0.35f, 0.4f);
                    cone.AddComponent<Rigidbody>().mass = 3f;
                }
                cone.name = $"Cone_{i}";
                cone.transform.position = s.Position + Vector3.up * (RoadHeight + 0.35f);
            }
        }

        private static void BuildBumps(Transform parent, List<Sample> samples, float sectionStart, Scene scene)
        {
            var group = new GameObject("SpeedBumps").transform;
            group.SetParent(parent, false);
            var material = FindInScene(scene, "Bumps/Bump_0")?.GetComponent<MeshRenderer>()?.sharedMaterial;
            foreach (var (at, side) in Bumps)
            {
                var s = At(samples, sectionStart + at);
                float width = side == 0 ? HalfWidth * 2f + 1f : HalfWidth;
                Vector3 right = Vector3.Cross(Vector3.up, s.Forward);
                var bump = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bump.name = side == 0 ? $"Bump_{at:0}m" : $"HalfBump_{at:0}m_{(side < 0 ? "Left" : "Right")}";
                bump.layer = LayerMask.NameToLayer("Drivable");
                bump.transform.SetParent(group, false);
                Object.DestroyImmediate(bump.GetComponent<CapsuleCollider>());
                bump.transform.position = s.Position + right * (side * HalfWidth * 0.5f) + Vector3.up * (RoadHeight + BumpHeight - BumpRadius);
                bump.transform.rotation = Quaternion.LookRotation(s.Forward, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);
                bump.transform.localScale = new Vector3(BumpRadius * 2f, width * 0.5f, BumpRadius * 2f);
                var col = bump.AddComponent<CapsuleCollider>();
                col.direction = 1;
                col.radius = 0.5f;
                col.height = 2f;
                if (material != null) bump.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        /// <summary>Boards at 150, 100 and 50 m before a corner on both sides of the road.</summary>
        private static void BuildBrakeBoards(Transform parent, List<Sample> samples, float cornerStart, Material material)
        {
            var group = new GameObject($"BrakeBoards_{cornerStart:0}").transform;
            group.SetParent(parent, false);
            foreach (float before in new[] { 150f, 100f, 50f })
            {
                var s = At(samples, cornerStart - before);
                Vector3 right = Vector3.Cross(Vector3.up, s.Forward);
                int stripes = Mathf.RoundToInt(before / 50f);
                foreach (float side in new[] { -1f, 1f })
                {
                    var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    board.name = $"Board_{before:0}m";
                    board.layer = LayerMask.NameToLayer("Environment");
                    board.transform.SetParent(group, false);
                    board.transform.position = s.Position + right * (side * (HalfWidth + 3f)) + Vector3.up * (0.4f + stripes * 0.35f);
                    board.transform.rotation = Quaternion.LookRotation(s.Forward, Vector3.up);
                    board.transform.localScale = new Vector3(1.2f, 0.8f + stripes * 0.7f, 0.08f);
                    board.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
        }

        /// <summary>Gravel trap straight ahead of the tight corner with a tyre wall behind it.</summary>
        private static void BuildRunoff(Transform parent, List<Sample> samples, float cornerStart, Material gravel, Material tyre)
        {
            var group = new GameObject("Runoff_TightCorner").transform;
            group.SetParent(parent, false);
            var entry = At(samples, cornerStart);
            Vector3 f = entry.Forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            const float trapLength = 60f, trapWidth = 30f;
            Vector3 centre = entry.Position + f * (HalfWidth + 2f + trapLength * 0.5f) - right * 2f;

            var trap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trap.name = "GravelTrap";
            trap.layer = LayerMask.NameToLayer("Drivable");
            trap.transform.SetParent(group, false);
            trap.transform.SetPositionAndRotation(new Vector3(centre.x, -0.04f, centre.z), Quaternion.LookRotation(f, Vector3.up));
            trap.transform.localScale = new Vector3(trapWidth, 0.1f, trapLength);
            trap.GetComponent<MeshRenderer>().sharedMaterial = gravel;
            trap.AddComponent<DrivingSurface>().EditorConfigure(0.45f, 12f);

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "TyreWall";
            wall.layer = LayerMask.NameToLayer("Environment");
            wall.transform.SetParent(group, false);
            wall.transform.SetPositionAndRotation(centre + f * (trapLength * 0.5f + 1f) + Vector3.up * 0.5f, Quaternion.LookRotation(f, Vector3.up));
            wall.transform.localScale = new Vector3(trapWidth, 1f, 1.2f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = tyre;
        }

        private static void BuildDrivingLine(DrivingRoute route, List<Sample> samples, float[] starts)
        {
            var points = new List<Vector3>();
            var banks = new List<float>();
            var overrides = new List<float>();
            float slalomStart = starts[4];
            float firstCone = slalomStart + SlalomCones[0], lastCone = slalomStart + SlalomCones[SlalomCones.Length - 1];
            float length = samples[samples.Count - 1].Distance;
            for (float d = 0f; d < length - 1f; d += 2f)
            {
                var s = At(samples, d);
                float lateral = 0f;
                if (d > firstCone - SlalomPitch && d < lastCone + SlalomPitch)
                {
                    float envelope = Mathf.Clamp01(Mathf.Min(d - (firstCone - SlalomPitch), lastCone + SlalomPitch - d) / SlalomPitch);
                    lateral = SlalomAmplitude * envelope * Mathf.Cos(Mathf.PI * (d - firstCone) / SlalomPitch);
                }
                float bank = BankAt(d, starts);
                points.Add(Surface(s, lateral, bank));
                banks.Add(bank);
                overrides.Add(Layout[s.Segment].Speed);
            }

            // Advisory speed from the driving line's own curvature (so the slalom weave counts), with banking.
            var speeds = new List<float>();
            const int span = 3;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[(i - span + points.Count) % points.Count], b = points[i], c = points[(i + span) % points.Count];
                float curvature = Curvature(new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z));
                float speed = MaxAdvisorySpeed;
                if (curvature > 1e-4f)
                {
                    float tan = Mathf.Tan(Mathf.Abs(banks[i]) * Mathf.Deg2Rad);
                    speed = Mathf.Sqrt(VehicleUnits.Gravity / curvature * (AdvisoryGrip + tan) / Mathf.Max(0.1f, 1f - AdvisoryGrip * tan));
                }
                if (overrides[i] > 0f) speed = Mathf.Min(speed, overrides[i]);
                speeds.Add(Mathf.Min(speed, MaxAdvisorySpeed));
            }

            var sections = new List<DrivingRoute.Section>();
            for (int i = 0; i < Layout.Length; i++) sections.Add(new DrivingRoute.Section { name = Layout[i].Name, startDistance = starts[i] });
            route.EditorConfigure(points, speeds, sections);
        }

        /// <summary>Curvature (1/m) of the circle through three points.</summary>
        private static float Curvature(Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Vector2.Distance(a, b), bc = Vector2.Distance(b, c), ca = Vector2.Distance(c, a);
            float area2 = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
            float denominator = ab * bc * ca;
            return denominator > 1e-6f ? 2f * area2 / denominator : 0f;
        }

        // ---------------- Scene wiring ----------------

        private static string ConfigureScene(Scene scene)
        {
            var log = new System.Text.StringBuilder();

            // Spawn on the start straight, facing along the route.
            foreach (var sp in InScene<VehicleSpawnPoint>(scene))
            {
                sp.transform.SetPositionAndRotation(Start + Vector3.right * 20f + Vector3.up * RoadHeight, Quaternion.LookRotation(Vector3.right, Vector3.up));
                var spSo = new SerializedObject(sp);
                spSo.FindProperty("dropHeight").floatValue = 0.05f;
                spSo.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"spawn moved to {sp.transform.position}");
            }

            // The skidpad disc had a CapsuleCollider scaled 120×0.02×120: PhysX made it a 60 m-radius sphere.
            var skidpad = FindInScene(scene, "Skidpad_r40/Surface");
            if (skidpad != null && skidpad.TryGetComponent(out CapsuleCollider capsule))
            {
                Object.DestroyImmediate(capsule);
                skidpad.AddComponent<MeshCollider>().sharedMesh = skidpad.GetComponent<MeshFilter>().sharedMesh;
                log.AppendLine("skidpad: capsule collider replaced with a mesh collider");
            }

            // The head-on crash wall was placed beyond the end of the 1 km straight (z 900) and the ground (z 600),
            // i.e. over empty space. Stand it on the end of the straight's surface.
            var headOn = FindInScene(scene, "CrashBarriers/HeadOnWall");
            if (headOn != null && headOn.transform.position.z > 899f)
            {
                var p = headOn.transform.position;
                headOn.transform.position = new Vector3(p.x, p.y, 898f);
                log.AppendLine("head-on wall moved onto the end of the straight (z 898)");
            }

            // Grass everywhere off the tarmac: lower grip, more drag. The recovery area.
            var ground = FindInScene(scene, "Ground");
            if (ground != null)
            {
                if (!ground.TryGetComponent(out DrivingSurface surface)) surface = ground.AddComponent<DrivingSurface>();
                surface.EditorConfigure(0.6f, 4f);
                log.AppendLine("ground: grass surface (grip 0.6, rolling ×4)");
            }

            // Camera: replace the Cinemachine follow/aim behaviours with the vehicle chase camera.
            var entry = InScene<MissionSceneEntry>(scene).FirstOrDefault();
            var followCam = FindInScene(scene, "FollowCamera");
            if (followCam != null)
            {
                foreach (var c in followCam.GetComponents<CinemachineComponentBase>()) Object.DestroyImmediate(c);
                if (!followCam.TryGetComponent(out VehicleChaseCamera chase)) chase = followCam.AddComponent<VehicleChaseCamera>();
                var cmCam = followCam.GetComponent<CinemachineCamera>();
                cmCam.Follow = null;
                cmCam.LookAt = null;
                cmCam.Lens.NearClipPlane = 0.1f;
                cmCam.Lens.FarClipPlane = 3000f;
                if (entry != null)
                {
                    var so = new SerializedObject(entry);
                    so.FindProperty("chaseCamera").objectReferenceValue = chase;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                log.AppendLine("camera: VehicleChaseCamera on FollowCamera");
            }

            // Dev tools: telemetry overlay and recorder.
            var dev = FindInScene(scene, "DevTools") ?? new GameObject("DevTools");
            if (dev.scene != scene) SceneManager.MoveGameObjectToScene(dev, scene);
            if (!dev.TryGetComponent(out VehicleDebugHud hud)) hud = dev.AddComponent<VehicleDebugHud>();
            if (!dev.TryGetComponent(out VehicleTelemetryLog _)) dev.AddComponent<VehicleTelemetryLog>();
            if (entry != null)
            {
                var so = new SerializedObject(entry);
                so.FindProperty("debugHud").objectReferenceValue = hud;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return log.ToString();
        }

        private static IEnumerable<T> InScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));

        /// <summary>Finds "Root/Child" from a scene root, or "Child/Grandchild" nested under any root.</summary>
        private static GameObject FindInScene(Scene scene, string path)
        {
            int slash = path.IndexOf('/');
            string head = slash < 0 ? path : path.Substring(0, slash);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == head)
                {
                    var t = slash < 0 ? root.transform : root.transform.Find(path.Substring(slash + 1));
                    if (t != null) return t.gameObject;
                }
                var nested = root.transform.Find(path);
                if (nested != null) return nested.gameObject;
            }
            return null;
        }

        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, string layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer(layer);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.isStatic = true;
            return go;
        }

        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{ArtFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material LitMaterial(string name, Color colour)
        {
            string path = $"{ArtFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
