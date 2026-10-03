// Full-screen ground haze for GroundHazeFeature: analytic exponential height fog integrated along each view ray, starting
// at a clear-foreground distance. Sky pixels are left alone (the sky shader has its own horizon glow).
//   _HazeParams: x = density at base height (1/m), y = base height (m), z = falloff (m per e), w = start distance (m)
//   _HazeColor:  rgb = haze colour, a = maximum opacity
Shader "Hidden/NeonRift/GroundHaze"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "GroundHaze"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _HazeParams;
            float4 _HazeColor;

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    if (depth <= 0.000001) return 0;
                #else
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                    if (depth >= 0.999999) return 0;
                #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 ray = world - _WorldSpaceCameraPos;
                float distance = length(ray);
                float start = _HazeParams.w;
                if (distance <= start) return 0;

                // Segment from the start distance to the surface; density a·exp(−(y − h0)/H) integrated along it.
                float3 from = _WorldSpaceCameraPos + ray * (start / distance);
                float segment = distance - start;
                float h = _HazeParams.z;
                float y0 = from.y - _HazeParams.y;
                float dy = world.y - from.y;
                float k = dy / h;
                float shape = abs(k) > 0.0001 ? (1.0 - exp(-k)) / k : 1.0;
                float optical = _HazeParams.x * segment * exp(-y0 / h) * shape;
                float amount = min(1.0 - exp(-max(optical, 0.0)), _HazeColor.a);
                return half4(_HazeColor.rgb, amount);
            }
            ENDHLSL
        }
    }
}
