using System.Collections.Generic;
using UnityEngine;

namespace NeonRift.EditorTools.District
{
    /// <summary>
    /// One <see cref="MeshBuilder"/> per material for a group of geometry (a block, a landmark), emitted as one static
    /// renderer per material: the district's batching unit. Builders are created on first use.
    /// </summary>
    public sealed class BlockMeshes
    {
        private readonly Dictionary<Material, (MeshBuilder builder, bool shadows)> builders = new();
        private readonly List<Material> order = new();

        public MeshBuilder this[Material material] => Get(material, true);

        public MeshBuilder Unshadowed(Material material) => Get(material, false);

        private MeshBuilder Get(Material material, bool shadows)
        {
            if (!builders.TryGetValue(material, out var entry))
            {
                entry = (new MeshBuilder(), shadows);
                builders[material] = entry;
                order.Add(material);
            }
            return entry.builder;
        }

        /// <summary>Saves each non-empty builder as "{prefix}_{material}" and adds a renderer under <paramref name="parent"/>.</summary>
        public List<Renderer> Emit(Transform parent, string prefix, int layer)
        {
            var renderers = new List<Renderer>();
            foreach (var material in order)
            {
                var (builder, shadows) = builders[material];
                if (builder.IsEmpty) continue;
                string name = material.name.Replace("District_", string.Empty);
                var go = DistrictKit.Renderer(name, parent, DistrictKit.SaveMesh(builder.ToMesh($"{prefix}_{name}", material.IsKeywordEnabled("_NORMALMAP")), $"{prefix}_{name}"), material, layer, shadows);
                renderers.Add(go.GetComponent<Renderer>());
            }
            return renderers;
        }
    }
}
