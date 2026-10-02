// Additive, unlit, fog-aware glow for light shafts, objective beacons and volumetric-looking cones.
// The texture's alpha (or luminance) shapes the glow; vertex colour multiplies it; fog fades it to nothing.
Shader "NeonRift/AdditiveGlow"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.3, 2, 3, 1)
        _MainTex ("Shape (A)", 2D) = "white" {}
        _EdgeSoftness ("View Edge Softness", Range(0, 4)) = 1.5
        _ScrollSpeed ("Vertical Scroll", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Glow"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;
                half _EdgeSoftness;
                float _ScrollSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = TRANSFORM_TEX(input.uv, _MainTex) + float2(0, _Time.y * _ScrollSpeed);
                o.color = input.color;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half shape = tex.a * max(tex.r, max(tex.g, tex.b));
                // Fade surfaces seen edge-on so cylinders read as soft volumes, not hard tubes.
                half facing = abs(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half edge = _EdgeSoftness > 0 ? pow(facing, _EdgeSoftness) : 1;
                half3 colour = _Color.rgb * input.color.rgb * input.color.a * shape * edge;
                colour *= ComputeFogIntensity(input.fog);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
