// Harbor Rain navigation light / beacon: a camera-facing glow that keeps a minimum on-screen size (a real light at sea
// stays a visible point at a kilometre, unlike a 1 m cube) and is only partly swallowed by the storm fog (point lights
// punch through haze far more than the unlit hull around them). Additive, no shadows, no depth write.
Shader "NeonRift/HarborBeacon"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (3, 3, 3, 1)
        _Size ("Size at close range (m)", Float) = 0.8
        _MinAngle ("Minimum angular size (rad)", Float) = 0.0035
        _FogPierce ("Fog density multiplier", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Size, _MinAngle, _FogPierce;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fade : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float3 toCam = _WorldSpaceCameraPos - centre;
                float dist = length(toCam);
                float size = max(_Size, dist * _MinAngle);
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 pos = centre + (right * v.positionOS.x + up * v.positionOS.y) * size * 2.0;
                o.positionCS = TransformWorldToHClip(pos);
                o.uv = v.uv;
                // Exponential-squared fog at a fraction of the scene density (unity_FogParams.x = density / sqrt(ln 2)).
                float f = unity_FogParams.x * dist * _FogPierce;
                o.fade = (half)(exp2(-f * f) * saturate(dist / 3.0));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 d = i.uv * 2 - 1;
                float r = length(d);
                half core = saturate(1 - r * 3.0);
                half halo = pow(saturate(1 - r), 3) * 0.35;
                return half4(_Color.rgb * (core + halo) * i.fade, 1);
            }
            ENDHLSL
        }
    }
}
