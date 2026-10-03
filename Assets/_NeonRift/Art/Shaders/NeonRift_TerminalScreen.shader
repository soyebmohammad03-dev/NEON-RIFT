// Procedural terminal / hologram readout. No textures: a header bar, a stage strip (one segment per stage, the
// active one blinking), rows of scrolling "text" blocks, a progress bar and a scan line, all tinted by the state
// colour. Driven per renderer through a MaterialPropertyBlock by TerminalDisplay / DataCoreChamber, so every screen
// shares one material. Blend is a property: opaque for physical screens, additive (One One) for holograms.
Shader "NeonRift/TerminalScreen"
{
    Properties
    {
        [HDR] _Color ("State Colour", Color) = (0.3, 1.6, 2.2, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _Steps ("Stage Count", Float) = 4
        _Step ("Active Stage", Float) = 0
        _Mode ("Mode (0 idle, 1 working, 2 done, 3 alarm)", Float) = 0
        _Glitch ("Glitch", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
        _Brightness ("Brightness", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Screen"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Progress, _Steps, _Step, _Mode, _Glitch, _Seed, _Brightness;
                float _SrcBlend, _DstBlend, _ZWrite, _Cull;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Inside(float2 uv, float4 r) { return step(r.x, uv.x) * step(uv.x, r.z) * step(r.y, uv.y) * step(uv.y, r.w); }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 uv = input.uv;
                // Glitch: horizontal bands tear sideways.
                float band = floor(uv.y * 24.0);
                uv.x += _Glitch * (Hash(float2(band, floor(t * 24.0) + _Seed)) - 0.5) * 0.18;

                float working = step(0.5, _Mode) * step(_Mode, 1.5);
                float v = 0.05;
                float2 edge = min(uv, 1.0 - uv);
                v = max(v, step(min(edge.x, edge.y), 0.025) * 0.55);

                // Header bar with a moving tick.
                float header = Inside(uv, float4(0.06, 0.85, 0.94, 0.92));
                v = max(v, header * (0.45 + 0.35 * step(frac(uv.x * 6.0 - t * 0.5), 0.15)));

                // Stage strip: done = full, active = blinking, pending = dim.
                if (Inside(uv, float4(0.06, 0.71, 0.94, 0.79)) > 0.0)
                {
                    float seg = (uv.x - 0.06) / 0.88 * max(_Steps, 1.0);
                    float idx = floor(seg);
                    float gap = step(0.1, frac(seg));
                    float lit = idx < _Step ? 1.0 : (abs(idx - _Step) < 0.5 ? 0.35 + 0.65 * step(0.5, frac(t * 3.0)) * working + 0.3 * (1.0 - working) : 0.12);
                    v = max(v, gap * lit);
                }

                // Text rows: blocks that scroll fast while working, slowly otherwise.
                if (Inside(uv, float4(0.06, 0.2, 0.94, 0.66)) > 0.0)
                {
                    float rows = 6.0;
                    float ry = (uv.y - 0.2) / 0.46 * rows;
                    float r = floor(ry);
                    float cx = floor((uv.x - 0.06) / 0.88 * 28.0);
                    float scroll = floor(t * lerp(0.6, 9.0, working));
                    float lineLength = 6.0 + Hash(float2(r + scroll * 3.0, _Seed)) * 22.0;
                    float on = step(0.32, Hash(float2(cx + _Seed * 13.0, r + scroll * 7.0))) * step(cx, lineLength);
                    on *= step(0.22, frac(ry)) * step(frac(ry), 0.78);
                    v = max(v, on * 0.5);
                }

                // Progress bar.
                if (Inside(uv, float4(0.06, 0.07, 0.94, 0.14)) > 0.0)
                {
                    float p = (uv.x - 0.06) / 0.88;
                    v = max(v, p < _Progress ? 1.0 : 0.1);
                }

                // Alarm mode: the whole screen pulses.
                float alarm = step(2.5, _Mode);
                v += alarm * 0.25 * (0.5 + 0.5 * sin(t * 12.0));

                // Scan line and CRT rows.
                float scan = frac(t * lerp(0.25, 0.9, working) + _Seed * 0.13);
                v += 0.3 * exp(-abs(uv.y - scan) * 60.0);
                v *= 0.86 + 0.14 * sin(input.uv.y * 420.0);
                v *= 1.0 - _Glitch * 0.35 * step(0.7, Hash(float2(floor(t * 30.0), band)));

                half3 colour = _Color.rgb * v * _Brightness;
                // Opaque screens fade into the fog colour; additive holograms fade to nothing.
                colour = _DstBlend > 0.5 ? MixFogColor(colour, half3(0, 0, 0), input.fog) : MixFog(colour, input.fog);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
