// Harbor Rain moving water on land (URP, transparent, double-sided). One shader for three uses:
//  - the green water that comes OVER the seawall (a ribbon along the overtopping path: up the wall face, across the
//    apron, launched over the parapet and down onto the road edge),
//  - the flood that sweeps across the road behind it and then drains (a grid over the road),
//  - standing puddles (radial mask) that spread after a wave and are pocked by rain.
// Mesh contract: uv0.x = distance along the flow (0 at the source, 1 at the far end), uv0.y = across (0..1), the
// vertex normal is the direction the water thickens in. Per renderer (MaterialPropertyBlock):
//  _SheetA = front (uv.x reached by the leading edge), tail (uv.x the water has left), thickness m, foam 0..1
//  _SheetB = seed, flow speed (m/s, negative = back toward the source), drain 0..1, ragged edge amount
//  _SheetC = puddle mode (0/1), puddle radius 0..1, opacity, rain ripples 0..1
Shader "NeonRift/HarborSheet"
{
    Properties
    {
        _WaterColor ("Water", Color) = (0.05, 0.07, 0.075, 1)
        _FoamColor ("Foam", Color) = (0.7, 0.74, 0.76, 1)
        _SkyColor ("Sky reflection tint", Color) = (0.07, 0.08, 0.095, 1)
        _FoamTex ("Foam (R lace, G patches, B streaks)", 2D) = "black" {}
        _Length ("Flow length (m, for texture scale)", Float) = 20
        _Width ("Width (m, for texture scale)", Float) = 70
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-20" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HarborWater.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterColor, _FoamColor, _SkyColor;
                float4 _FoamTex_ST;
                float _Length, _Width;
            CBUFFER_END
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            float4 _SheetA, _SheetB, _SheetC;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            // Leading edge position across the sheet: fingers and lobes rather than a straight line.
            float FrontAt(float across)
            {
                float j = _SheetB.w;
                return _SheetA.x + (WNoise(float2(across * _Width * 0.09 + _SheetB.x, 0.5)) - 0.5) * 0.22 * j
                                 + (WNoise(float2(across * _Width * 0.33 + _SheetB.x * 2.3, 1.5)) - 0.5) * 0.08 * j;
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                float t = _HR_Time;
                float s = v.uv.x, front = FrontAt(v.uv.y);
                // Thickness: a rolling bore just behind the front, thinning behind it and toward the tail.
                float ahead = front - s;
                float covered = saturate(ahead * 12) * saturate((s - _SheetA.y) * 10);
                float bore = exp(-ahead * ahead * 90) * 0.8;
                float surge = (0.55 + bore) * covered * (1 - _SheetB.z * 0.8);
                float ripple = (WNoise(float2(v.uv.y * _Width * 0.25, s * _Length * 0.4 - t * abs(_SheetB.y) * 0.4)) - 0.5) * 0.35;
                float h = _SheetA.z * surge * (1 + ripple);
                if (_SheetC.x > 0.5) h = _SheetA.z;   // puddles: flat
                p += n * h;
                o.positionWS = p;
                o.normalWS = n;
                o.uv = v.uv;
                o.positionCS = TransformWorldToHClip(p);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, half facing : VFACE) : SV_Target
            {
                float t = _HR_Time;
                float s = i.uv.x, across = i.uv.y;
                float2 w = float2(across * _Width, s * _Length);
                bool puddle = _SheetC.x > 0.5;

                // Coverage: behind the ragged front, ahead of the tail; draining breaks the water into patches that
                // survive longest in the gutters (far end) and at the scuppers (source end).
                float front = FrontAt(across);
                float cover = saturate((front - s) * 30) * saturate((s - _SheetA.y) * 25);
                half3 ft = FoamSample(TEXTURE2D_ARGS(_FoamTex, sampler_FoamTex), w * 0.035 + float2(0.13, 0.71));
                float lowSpots = saturate(max(smoothstep(0.8, 1.0, s), 1 - smoothstep(0.0, 0.12, s)) * 0.6 + ft.g * 0.55 + WNoise(w * 0.12) * 0.25);
                cover *= saturate((lowSpots - _SheetB.z) * 6 + (1 - _SheetB.z));
                if (puddle)
                {
                    float2 c = float2(across, s) * 2 - 1;
                    float rr = length(c) + (WNoise(w * 0.6 + _SheetB.x) - 0.5) * 0.35;
                    cover = saturate((_SheetC.y - rr) * 8);
                }
                clip(cover - 0.02);

                // Surface: flow ripples moving with the water, rain rings where it is shallow / still.
                float3 n = normalize(i.normalWS) * (facing > 0 ? 1 : -1);
                float speed = _SheetB.y;
                // Fine ripples stretched along the flow: the lights smear into streaks instead of blobs.
                float2 fq = float2(w.x * 1.8, w.y * 0.9 - t * speed * 0.9);
                float n0 = WFbm(fq);
                float2 g = float2(n0 - WFbm(fq + float2(0.15, 0)), n0 - WFbm(fq + float2(0, 0.15))) * (0.35 + saturate(abs(speed) / 4) * 0.9);
                g += RainRings(i.positionWS.xz, t) * _SheetC.w * (1 - saturate(abs(speed) / 3));
                half3 tx = normalize(cross(n, half3(0, 0, 1)) + 1e-4);
                half3 ty = cross(n, tx);
                n = normalize(n + (tx * g.x + ty * g.y) * 1.3);

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                WaterLighting L = GatherWaterLights(i.positionWS, n, v, screenUV, 320);
                half fresnel = 0.03 + 0.97 * pow(1 - saturate(dot(n, v)), 5);
                half3 env = SkyReflection(reflect(-v, n), i.positionWS, screenUV, (half)0.08, _SkyColor.rgb);
                // Thin water over asphalt is mostly a mirror: dark body, strong reflections and light streaks.
                half3 col = _WaterColor.rgb * (0.3 + min(L.diffuse, 2.0) * 0.08) * (1 - fresnel) + env * (0.25 + fresnel) + min(L.spec, 4.0);

                // Foam: a bright churned band at the leading edge, patches and lace behind it, streaks dragged along
                // the flow, fading as the water slows and drains.
                float edge = exp(-pow((front - s) * 9, 2));   // a wider churned bore at the leading edge
                half3 fs = FoamSample(TEXTURE2D_ARGS(_FoamTex, sampler_FoamTex), float2(w.x * 0.03, w.y * 0.012 - t * speed * 0.012));
                half streaks = saturate(fs.b * 2.4 - 0.85) * saturate(abs(speed) / 3);
                half amount = saturate(_SheetA.w * (0.16 + edge * 0.95) + streaks * 0.45 * _SheetA.w);
                amount *= 1 - _SheetB.z * 0.6;
                half foam = puddle ? 0 : FoamMask(TEXTURE2D_ARGS(_FoamTex, sampler_FoamTex), w, float2(0, -speed * 0.3), amount);
                half3 lightOnFoam = min(L.diffuse, 2.0);
                half3 foamLit = _FoamColor.rgb * (lerp(dot(lightOnFoam, half3(0.333, 0.333, 0.333)).xxx, lightOnFoam, 0.4) * 0.45 + min(L.wrap, 1.5) * 0.18 + 0.1);   // desaturated: foam reads white-grey, not sodium orange
                col = lerp(col, foamLit, foam * 0.9);
                half alpha = saturate(_SheetC.z * (0.45 + fresnel * 0.5) + foam * 0.9) * cover;
                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
