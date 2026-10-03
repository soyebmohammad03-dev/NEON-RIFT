// Distant skyline backdrop (rings of tower silhouettes 1.4–2.4 km out). Unlit and unfogged on purpose: each ring carries
// its own haze (vertex colour alpha), so distance reads as layers that dissolve into the sky glow instead of fog
// flattening every tower to one colour. Windows are procedural (no textures) and filtered by screen-space footprint, so
// sub-pixel windows average to a lit-fraction glow instead of shimmering. Aviation lights blink.
//   uv0: facade metres (x along the facade, y up from the ground)
//   uv1: x = per-tower seed, y = kind (0 = facade, 1 = aviation light)
//   colour: rgb unused, a = haze (0 = near ring, 1 = fully dissolved)
Shader "NeonRift/SkylineBackdrop"
{
    Properties
    {
        _SilhouetteColor ("Silhouette", Color) = (0.012, 0.012, 0.016, 1)
        _HazeColor ("Haze (match the fog)", Color) = (0.09, 0.065, 0.06, 1)
        [HDR] _WarmWindow ("Warm Window", Color) = (1.4, 0.9, 0.5, 1)
        [HDR] _CoolWindow ("Cool Window", Color) = (0.75, 0.9, 1.15, 1)
        [HDR] _AccentWindow ("Accent", Color) = (1.6, 0.35, 0.9, 1)
        _LitFraction ("Lit Fraction", Range(0, 1)) = 0.28
        _WindowSize ("Window Cell (m)", Vector) = (4.5, 3.6, 0, 0)
        [HDR] _AviationColor ("Aviation Light", Color) = (6, 0.25, 0.15, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SilhouetteColor, _HazeColor, _WarmWindow, _CoolWindow, _AccentWindow, _AviationColor;
                float4 _WindowSize;
                half _LitFraction;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 data : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 data : TEXCOORD1; half haze : TEXCOORD2; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.data = input.data;
                o.haze = input.color.a;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half haze = input.haze;
                if (input.data.y > 0.5)
                {
                    // Aviation light: slow blink, phase per tower.
                    half blink = step(0.55, frac(_Time.y * 0.55 + input.data.x * 7.31));
                    return half4(lerp(_AviationColor.rgb * blink, _HazeColor.rgb, haze * 0.6), 1);
                }

                float2 cell = input.uv / _WindowSize.xy;
                float2 id = floor(cell);
                float2 f = frac(cell);
                float seed = input.data.x;
                // Whole floors go dark together sometimes (offices), single windows otherwise.
                float floorDark = step(0.82, Hash(float2(id.y, seed * 31.0)));
                float r = Hash(id + seed * 17.0);
                float lit = step(r, _LitFraction) * (1.0 - floorDark);
                float pick = Hash(id.yx + seed * 5.0);
                half3 tint = pick < 0.6 ? _WarmWindow.rgb : (pick < 0.94 ? _CoolWindow.rgb : _AccentWindow.rgb);
                half brightness = 0.35 + 0.65 * Hash(id * 1.7 + seed);
                float frame = step(0.18, f.x) * step(f.x, 0.82) * step(0.22, f.y) * step(f.y, 0.78);
                half3 windows = tint * brightness * lit * frame;

                // Footprint filtering: as a cell shrinks below ~2 px, fade to the average glow of a lit facade.
                float2 footprint = fwidth(cell);
                half detail = saturate(1.6 - max(footprint.x, footprint.y) * 2.2);
                half3 average = (_WarmWindow.rgb * 0.6 + _CoolWindow.rgb * 0.34 + _AccentWindow.rgb * 0.06) * 0.68 * _LitFraction * 0.82 * 0.36;
                windows = lerp(average, windows, detail);

                half3 colour = _SilhouetteColor.rgb + windows * (1.0 - haze * 0.85);
                colour = lerp(colour, _HazeColor.rgb, haze);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 Vert(Attributes input) : SV_POSITION { return TransformObjectToHClip(input.positionOS.xyz); }
            half Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
