using System.Collections.Generic;
using System.Linq;
using NeonRift.Vehicles;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools.Vehicles
{
    /// <summary>
    /// Distance LODs for a gameplay vehicle, built from the renderers it already has (no new meshes):
    /// <list type="bullet">
    /// <item>LOD0: everything (the player's car in the chase camera, close-ups);</item>
    /// <item>LOD1 below 18 % screen height: no interior, engine bay, brakes or damage glass, and no small non-light parts;</item>
    /// <item>LOD2 below 5 %: the big body panels, glass, wheels and lights only;</item>
    /// <item>culled below 0.8 %.</item>
    /// </list>
    /// Classification is by material name and renderer size, so it works for every car in the catalog. Lights are kept
    /// in every level, so a distant rival's head and tail lights never pop.
    /// </summary>
    public static class VehicleLodSetup
    {
        private static readonly string[] Interior = { "int_", "interior", "seat", "dash", "carpet", "alcantara", "steering", "volante", "pedal" };
        private static readonly string[] Hidden = { "mechanic", "brake", "engine", "damage", "exhaust_inner", "suspension", "chassis" };
        private static readonly string[] Lights = { "light", "lamp", "led", "emiss", "indicator" };

        public const float Lod1Height = 0.18f, Lod2Height = 0.05f, CullHeight = 0.008f;

        public readonly struct Summary
        {
            public readonly int Lod0, Lod1, Lod2;
            public readonly long Tris0, Tris1, Tris2;

            public Summary(int lod0, int lod1, int lod2, long tris0, long tris1, long tris2)
            {
                Lod0 = lod0; Lod1 = lod1; Lod2 = lod2; Tris0 = tris0; Tris1 = tris1; Tris2 = tris2;
            }

            public override string ToString() => $"LOD0 {Lod0} renderers / {Tris0} tris, LOD1 {Lod1} / {Tris1}, LOD2 {Lod2} / {Tris2}";
        }

        [MenuItem("Neon Rift/Vehicles/Apply Vehicle LODs To Catalog Prefabs")]
        private static void ApplyToCatalogFromMenu() => Debug.Log(ApplyToCatalog());

        public static string ApplyToCatalog()
        {
            var log = new System.Text.StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:VehicleCatalog"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<VehicleCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var vehicle in catalog.Vehicles)
                {
                    if (vehicle == null || vehicle.GameplayPrefab == null) continue;
                    string path = AssetDatabase.GetAssetPath(vehicle.GameplayPrefab);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        log.AppendLine($"{vehicle.DisplayName}: {Apply(root)}");
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            return log.ToString();
        }

        /// <summary>Adds (or rebuilds) the LODGroup on a vehicle root.</summary>
        public static Summary Apply(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true)
                                .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.TryGetComponent(out MeshFilter f) && f.sharedMesh != null)
                                .ToList();
            var lod1 = new List<Renderer>();
            var lod2 = new List<Renderer>();
            foreach (var r in renderers)
            {
                string mats = string.Join("|", r.sharedMaterials.Where(m => m != null).Select(m => m.name.ToLowerInvariant()));
                bool light = Lights.Any(mats.Contains) || r.sharedMaterials.Any(m => m != null && m.IsKeywordEnabled("_EMISSION"));
                bool wheel = r.transform.GetComponentsInParent<Transform>(true).Any(t => t.name.StartsWith("Wheel_"));
                bool interior = Interior.Any(mats.Contains);
                bool hidden = Hidden.Any(mats.Contains);
                float size = r.GetComponent<MeshFilter>().sharedMesh.bounds.size.magnitude * r.transform.lossyScale.x;

                if (light || ((wheel || !interior && !hidden) && (size >= 0.35f || wheel))) lod1.Add(r);
                if (light || (wheel && !mats.Contains("brake")) || (!interior && !hidden && size >= 2.5f)) lod2.Add(r);
            }

            if (!root.TryGetComponent(out LODGroup group)) group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(new[]
            {
                new LOD(Lod1Height, renderers.Cast<Renderer>().ToArray()),
                new LOD(Lod2Height, lod1.ToArray()),
                new LOD(CullHeight, lod2.ToArray())
            });
            group.RecalculateBounds();
            return new Summary(renderers.Count, lod1.Count, lod2.Count, Tris(renderers), Tris(lod1), Tris(lod2));
        }

        private static long Tris(IEnumerable<Renderer> renderers)
        {
            long t = 0;
            foreach (var r in renderers)
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                for (int i = 0; i < mesh.subMeshCount; i++) t += mesh.GetIndexCount(i) / 3;
            }
            return t;
        }
    }
}
