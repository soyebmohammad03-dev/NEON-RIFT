// Night sky for the city: zenith-to-horizon gradient with a warm/magenta light-pollution band and sparse stars.
// Cheap (no textures), works as a URP skybox material.
Shader "NeonRift/NightSky"
{
    Properties
    {
        [HDR] _ZenithColor ("Zenith", Color) = (0.01, 0.012, 0.035, 1)
        [HDR] _HorizonColor ("Horizon", Color) = (0.16, 0.06, 0.2, 1)
        [HDR] _GlowColor ("City Glow", Color) = (0.45, 0.12, 0.4, 1)
        _GlowHeight ("City Glow Height", Range(0.01, 0.5)) = 0.12
        _HorizonBlend ("Horizon Blend", Range(0.05, 1)) = 0.35
        _GroundColor ("Below Horizon", Color) = (0.02, 0.015, 0.03, 1)
        _StarDensity ("Star Density", Range(0, 1)) = 0.35
        _StarBrightness ("Star Brightness", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GlowColor, _GroundColor;
                half _GlowHeight, _HorizonBlend, _StarDensity, _StarBrightness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float h = d.y;
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, saturate(pow(saturate(h / _HorizonBlend), 0.6)));
                sky += _GlowColor.rgb * exp(-max(h, 0) / _GlowHeight);
                // Stars: one candidate per cell of a direction grid, faded out near the hazy horizon.
                float3 cell = floor(d * 220.0);
                float r = Hash(cell);
                float star = step(1.0 - _StarDensity * 0.02, r) * saturate((h - 0.15) * 3.0);
                float3 local = frac(d * 220.0) - 0.5;
                star *= saturate(1.0 - length(local) * 3.0);
                sky += star * _StarBrightness * (0.5 + 0.5 * Hash(cell + 7.0));
                half3 colour = h < 0 ? lerp(_HorizonColor.rgb + _GlowColor.rgb, _GroundColor.rgb, saturate(-h * 8.0)) : sky;
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
