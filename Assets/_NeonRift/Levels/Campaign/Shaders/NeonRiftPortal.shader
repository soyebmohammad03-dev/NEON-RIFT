// Campaign portal (the rift between Night Run and Harbor Rain): additive, unlit, animated.
// _Kind 0 = the swirling core disc (UV centre = portal centre), 1 = the energy ring (torus: u around, v across the tube),
// 2 = sparks (particles, vertex colour).
Shader "NeonRift/Portal"
{
    Properties
    {
        [HDR] _ColorA ("Colour A", Color) = (0.2, 2.6, 4.0, 1)
        [HDR] _ColorB ("Colour B", Color) = (3.6, 0.5, 3.2, 1)
        _Intensity ("Intensity", Float) = 1
        _Open ("Open (0..1)", Range(0, 1)) = 1
        _Kind ("Kind (0 core, 1 ring)", Float) = 0
        _Speed ("Speed", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        // Ring: additive (One One). Core: premultiplied (One OneMinusSrcAlpha) so the opening hides whatever is
        // beyond it — a car driving in disappears into it, a car coming out emerges from it.
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Name "PortalUnlit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                half _Intensity;
                half _Open;
                half _Kind;
                half _Speed;
                half _SrcBlend;
                half _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; half4 color : COLOR; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                half3 col;
                half alpha = 0;
                if (_Kind > 1.5)
                {
                    // Sparks (particles): a soft hot dot tinted by the particle colour.
                    float d = length(i.uv * 2 - 1);
                    float spot = saturate(1 - d);
                    col = i.color.rgb * i.color.a * (spot * spot * 2.5 + pow(spot, 8) * 3);
                }
                else if (_Kind < 0.5)
                {
                    float2 p = i.uv * 2 - 1;
                    float r = length(p);
                    float a = atan2(p.y, p.x);
                    float open = saturate(_Open);
                    float edge = open * 0.98;
                    // Spiral arms pulled into the centre, broken up by drifting noise.
                    float arms = sin(a * 5 + r * 13 - t * 4.2) * 0.5 + 0.5;
                    float n = vnoise(float2(a * 2.2 + t * 0.6, r * 6 - t * 2.4));
                    float swirl = saturate(arms * 0.65 + n * 0.55);
                    float rim = smoothstep(edge - 0.32, edge, r) * (1 - smoothstep(edge, edge + 0.02, r));
                    float body = (1 - smoothstep(0, edge, r)) * 0.55 + 0.25;
                    float mask = 1 - smoothstep(edge - 0.01, edge + 0.01, r);
                    half3 c = lerp(_ColorA.rgb, _ColorB.rgb, swirl);
                    col = c * (swirl * body + rim * 1.6) * mask + _ColorA.rgb * pow(saturate(1 - r / max(edge, 1e-3)), 6) * 1.5 * mask;
                    // Nearly opaque inside (a dark well behind the light), so the far side is not seen through it.
                    col += half3(0.004, 0.006, 0.02) * mask;
                    alpha = mask * 0.94 * saturate(_Open * 3);
                }
                else
                {
                    // Energy streaming round the ring, brightest along the tube's centre line.
                    float bands = pow(frac(i.uv.x * 14 - t * 1.6), 3);
                    float across = 1 - abs(i.uv.y * 2 - 1);
                    float flick = 0.75 + 0.25 * vnoise(float2(i.uv.x * 40, t * 6));
                    col = lerp(_ColorA.rgb, _ColorB.rgb, bands) * (0.35 + across * 0.9) * flick + _ColorB.rgb * bands * across * 0.8;
                    col *= saturate(_Open * 1.4);
                }
                col *= _Intensity;
                col = MixFogColor(col, half3(0, 0, 0), i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
