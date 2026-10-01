using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeonRift.EditorTools
{
    /// <summary>
    /// glTFast's clearcoat Shader Graph outputs invalid colour (flat cyan, i.e. NaN red) at runtime in this
    /// URP setup. Renderers using it are remapped to project-owned copies on the plain glTF PBR graph.
    /// Revisit when the car-paint look-dev pass moves car paint to URP Complex Lit clear coat.
    /// </summary>
    public static class GltfMaterialFixer
    {
        private const string ClearcoatShaderToken = "Clearcoat";
        private const string SafeShaderName = "Shader Graphs/glTF-pbrMetallicRoughness";

        /// <summary>Returns how many material slots were remapped.</summary>
        public static int RemapClearcoat(IEnumerable<Renderer> renderers, string outputFolder)
        {
            var safe = Shader.Find(SafeShaderName);
            if (safe == null) throw new System.InvalidOperationException($"Shader '{SafeShaderName}' not found.");

            var cache = new Dictionary<Material, Material>();
            int count = 0;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.shader.name.Contains(ClearcoatShaderToken)) continue;
                    if (!cache.TryGetValue(m, out var copy))
                    {
                        Vehicles.VehiclePrefabBuilder.EnsureFolder(outputFolder);
                        var path = $"{outputFolder}/{Sanitize(m.name)}.mat";
                        copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (copy == null)
                        {
                            copy = new Material(m) { name = Path.GetFileNameWithoutExtension(path) };
                            copy.shader = safe;
                            AssetDatabase.CreateAsset(copy, path);
                        }
                        else
                        {
                            copy.CopyPropertiesFromMaterial(m);
                            copy.shader = safe;
                            EditorUtility.SetDirty(copy);
                        }
                        cache[m] = copy;
                    }
                    mats[i] = copy;
                    changed = true;
                    count++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            return count;
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace('.', '_');
        }
    }
}
