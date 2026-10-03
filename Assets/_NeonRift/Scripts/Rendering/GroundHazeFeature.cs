using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace NeonRift.Rendering
{
    /// <summary>
    /// Draws <see cref="GroundHaze"/> after opaques and the sky, before transparents: one full-screen triangle that reads
    /// the camera depth texture and alpha-blends haze over the colour target. No extra colour copy.
    /// </summary>
    public sealed class GroundHazeFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader shader;

        private Material material;
        private HazePass pass;

        public override void Create()
        {
            pass = new HazePass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            var haze = VolumeManager.instance.stack.GetComponent<GroundHaze>();
            if (haze == null || !haze.IsActive()) return;
            if (material == null)
            {
                if (shader == null) shader = Shader.Find("Hidden/NeonRift/GroundHaze");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            pass.Setup(material, haze);
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

#if UNITY_EDITOR
        public void EditorConfigure(Shader hazeShader) => shader = hazeShader;
#endif

        private sealed class HazePass : ScriptableRenderPass
        {
            private static readonly int ParamsId = Shader.PropertyToID("_HazeParams");
            private static readonly int ColorId = Shader.PropertyToID("_HazeColor");

            private sealed class PassData
            {
                public Material Material;
            }

            private Material material;

            public void Setup(Material hazeMaterial, GroundHaze haze)
            {
                material = hazeMaterial;
                material.SetVector(ParamsId, new Vector4(haze.density.value, haze.baseHeight.value, haze.falloff.value, haze.startDistance.value));
                var c = haze.color.value;
                material.SetVector(ColorId, new Vector4(c.r, c.g, c.b, haze.maxOpacity.value));
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.activeColorTexture.IsValid()) return;
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Neon Rift Ground Haze", out var data);
                data.Material = material;
                builder.UseTexture(resources.cameraDepthTexture);
                builder.SetRenderAttachment(resources.activeColorTexture, 0);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, 3));
            }
        }
    }
}
