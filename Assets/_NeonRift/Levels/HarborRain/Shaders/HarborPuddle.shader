// Harbor Rain standing puddles (URP, opaque): near-black still water that mirrors the lights, pocked by expanding
// rain rings that grow denser with the storm. Used by every batched puddle on the port's roads.
Shader "NeonRift/HarborPuddle"
{
    Properties
    {
        _WaterColor ("Water", Color) = (0.012, 0.015, 0.018, 1)
        _SkyColor ("Sky reflection tint", Color) = (0.06, 0.07, 0.085, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+5" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HarborWater.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterColor, _SkyColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fogFactor : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // 2.2: gentler rings and a broader highlight — lamps now smear into coherent streaks across the water instead
                // of breaking into a field of sharp glints (which read as gold glitter on the ground).
                float2 g = RainRings(i.positionWS.xz, _HR_Time) * lerp(0.22, 0.42, saturate(_HR_Storm));
                g += float2(WNoise(i.positionWS.xz * 2.1 + _HR_Time * 0.7) - 0.5, WNoise(i.positionWS.zx * 2.3 - _HR_Time * 0.6) - 0.5) * 0.03;
                half3 n = normalize(half3(g.x, 1, g.y));
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                WaterLighting L = GatherWaterLights(i.positionWS, n, v, screenUV, 70);
                half fresnel = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
                half3 env = SkyReflection(reflect(-v, n), i.positionWS, screenUV, (half)0.2, _SkyColor.rgb);
                half3 col = _WaterColor.rgb * (0.5 + L.diffuse * 0.2) * (1 - fresnel) + env * fresnel + min(L.spec * 0.6, 2.5);
                return half4(MixFog(col, i.fogFactor), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
