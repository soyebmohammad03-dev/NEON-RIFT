// Harbor Rain breaking wave (URP, opaque, double-sided). One mesh per seawall section: columns across the wave (x)
// and rows along its cross-section (s, from the seaward back slope over the crest to the toe of the face). The
// cross-section is a Catmull-Rom curve through 16 control points written each frame by HarborBreaker.cs — a broad
// swell offshore, a steepening face, a lip that throws forward and curls into a tube, then a plunge that collapses into
// a low turbulent bore — with a second set for the wave's flanks, which lag behind the centre so the break peels
// along the wall. The curve rides on the same Gerstner swell as the open sea (HarborWater.hlsl) and its edges sink
// under the sea surface, so the two meet without a seam. Foam: white water along the lip, streaks running down the
// face, the whole mass going white as it collapses, and old foam on the back slope.
Shader "NeonRift/HarborBreaker"
{
    Properties
    {
        _DeepColor ("Body", Color) = (0.012, 0.03, 0.034, 1)
        _ThinColor ("Translucent crest", Color) = (0.06, 0.17, 0.16, 1)
        _FoamColor ("Foam", Color) = (0.72, 0.76, 0.78, 1)
        _SkyColor ("Sky reflection tint", Color) = (0.07, 0.08, 0.095, 1)
        _FoamTex ("Foam (R lace, G patches, B streaks)", 2D) = "black" {}
        _Roughness ("Perceptual roughness", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+11" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

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
                half4 _DeepColor, _ThinColor, _FoamColor, _SkyColor;
                float4 _FoamTex_ST;
                float _Roughness;
            CBUFFER_END
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);

            // Per renderer (MaterialPropertyBlock).
            float4 _Prof[16];   // control point i: xy = centre (forward m, height m), zw = flank
            float4 _BrA;        // centre x, crest z, half width, wall face z
            float4 _BrB;        // height, curl 0..1, collapse 0..1, old foam 0..1
            float4 _BrC;        // flank lag (m), seed, sink (m), foam trail 0..1

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 data : TEXCOORD2;    // x = s, y = |x| / half width, z = envelope, w = height above sea
                float fogFactor : TEXCOORD3;
            };

            float2 Ctrl(int i, float e) { return lerp(_Prof[i].xy, _Prof[i].zw, e); }

            float2 Profile(float s, float e)
            {
                float fi = saturate(s) * 15.0;
                int i = min((int)floor(fi), 14);
                float u = fi - i;
                float2 p0 = Ctrl(max(i - 1, 0), e), p1 = Ctrl(i, e), p2 = Ctrl(i + 1, e), p3 = Ctrl(min(i + 2, 15), e);
                float u2 = u * u, u3 = u2 * u;
                return 0.5 * (2 * p1 + (p2 - p0) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (3 * p1 - p0 - 3 * p2 + p3) * u3);
            }

            float3 BreakerPoint(float x, float s, out float env, out float height)
            {
                float hw = max(_BrA.z, 1);
                float xn = abs(x) / hw;
                env = 1 - smoothstep(0.5, 1.0, xn);
                float e = smoothstep(0.1, 1.0, xn);
                float2 P = Profile(s, e);
                float t = _HR_Time;
                // The crest is never a ruler line: its height and lead wander along the wave.
                float n1 = WNoise(float2(x * 0.035 + _BrC.y, t * 0.12)) - 0.5;
                float n2 = WNoise(float2(x * 0.16 + _BrC.y * 3.1, t * 0.35)) - 0.5;
                // An A-frame peak: highest where it breaks first, lower toward the flanks, never a ruler line.
                P.y *= lerp(1.0, 0.6, smoothstep(0.1, 0.9, xn)) * (1 + n1 * 0.34 + n2 * 0.12);
                P.x += n1 * 6.0 + n2 * 1.2;
                float wx = _BrA.x + x;
                float wz = max(_BrA.y - P.x + _BrC.x * xn * xn, _BrA.w);
                float crest, near;
                float3 d = SeaDisplace(float2(wx, wz), crest, near);
                height = env * P.y;
                float y = _HR_Level + d.y + height + (1 - env) * -0.8 - _BrC.z;
                return float3(wx + d.x * (1 - env), y, wz + d.z * (1 - env));
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float x = v.positionOS.x, s = v.uv.x;
                float env, h, e1, h1, e2, h2;
                float3 p = BreakerPoint(x, s, env, h);
                float3 px = BreakerPoint(x + 0.8, s, e1, h1);
                float3 ps = BreakerPoint(x, s + 0.004, e2, h2);
                o.positionWS = p;
                o.normalWS = normalize(cross(px - p, ps - p));
                o.data = float4(s, abs(x) / max(_BrA.z, 1), env, h);
                o.positionCS = TransformWorldToHClip(p);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, half facing : VFACE) : SV_Target
            {
                float t = _HR_Time;
                float s = i.data.x, env = i.data.z;
                float H = max(_BrB.x, 0.1), curl = _BrB.y, collapse = _BrB.z;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 n = normalize(i.normalWS) * (facing > 0 ? 1 : -1);
                // Fine surface texture: wind ripples and turbulence (stronger on the face and in the collapse).
                float2 q = float2(i.positionWS.x, i.positionWS.y * 1.3 + i.positionWS.z);
                float e = 0.3;
                float n0 = WFbm(q * 0.5 + float2(0, t * 1.6));
                float2 g = float2(n0 - WFbm(q * 0.5 + float2(e, t * 1.6)), n0 - WFbm(q * 0.5 + float2(0, e + t * 1.6)));
                half steep = saturate(1 - n.y);          // geometric steepness, before the ripples
                half3 tx = normalize(cross(n, half3(0, 0, 1)) + 1e-4);
                half3 ty = cross(n, tx);
                n = normalize(n + (tx * g.x + ty * g.y) * (1.2 + collapse * 1.5));

                // Where we are on the cross-section (control point index): back slope < 5 < lip (6..10) < face (11..14).
                float idx = s * 15;
                half lip = smoothstep(4.6, 6.0, idx) * (1 - smoothstep(10.0, 11.2, idx));
                half face = smoothstep(10.0, 11.5, idx);
                half back = 1 - smoothstep(3.5, 5.0, idx);
                half thin = saturate(lip * (0.35 + curl * 0.8 + steep * 0.4) + face * curl * 0.5);

                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                WaterLighting L = GatherWaterLights(i.positionWS, n, v, screenUV, 180);
                half fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
                half3 env3 = SkyReflection(reflect(-v, n), i.positionWS, screenUV, (half)_Roughness, _SkyColor.rgb);
                // Heavy dark water; light comes through the thin crest and lip (green-grey), and the face catches the
                // floodlights on the wall.
                half3 body = _DeepColor.rgb * (0.35 + L.diffuse * 1.4) + _ThinColor.rgb * (L.wrap + L.diffuse * 0.4) * thin * 1.8;
                half3 col = body * (1 - fresnel) + env3 * fresnel + L.spec;

                // Foam. Whitewater along the lip once the crest starts to spill; streaks dragged down the face; the whole
                // mass turning white as it collapses; older foam lying on the back slope.
                half heightK = saturate(i.data.w / (H * 0.5));
                half lipFoam = lip * saturate(curl * 1.3 + heightK * 0.15) * (0.55 + 0.45 * heightK);
                half3 fs = FoamSample(TEXTURE2D_ARGS(_FoamTex, sampler_FoamTex), float2(i.positionWS.x * 0.035, s * 4.0 - t * 0.45));
                half faceFoam = face * saturate(fs.b * 2.4 - 0.95) * (0.3 + curl * 0.8) + lip * steep * saturate(fs.b * 2 - 0.8) * 0.5;
                // A steepening crest starts to spill before it curls: a ragged whitecap along the top.
                half spill = smoothstep(0.6, 0.95, heightK) * saturate((steep - 0.25) * 2.5) * saturate(H / 5) * (1 - back);
                lipFoam = max(lipFoam, spill * 0.85);
                // Storm whitecaps ride the top of the set from far out (what makes it readable at night before it breaks).
                half cap = smoothstep(0.82, 0.98, saturate(i.data.w / H)) * saturate(H / 3) * saturate(0.6 + curl) * (1 - back * 0.6);
                lipFoam = max(lipFoam, cap * (0.5 + 0.5 * fs.g));
                half backFoam = back * _BrB.w * (0.6 + 0.4 * fs.g);
                half amount = saturate(max(max(lipFoam, faceFoam), max(backFoam, collapse * (0.75 + 0.25 * lip))) + _BrC.w * back);
                half foam = FoamMask(TEXTURE2D_ARGS(_FoamTex, sampler_FoamTex), float2(i.positionWS.x, i.positionWS.z + i.positionWS.y * 0.7), float2(0.15, -1.6), amount);
                // Dense white water on the crest and lip, broken up by the foam texture rather than thresholded away.
                half breakup = saturate(fs.g * 0.7 + fs.r * 0.6 + 0.2);
                foam = max(foam, saturate(lipFoam * 1.5 - 0.2) * breakup);
                // Light on foam is clamped: next to a floodlight it should read white, not bloom into a glowing blob.
                half3 foamLit = _FoamColor.rgb * (min(L.diffuse, 2.2) * 0.75 + min(L.wrap, 1.5) * 0.3 + 0.1) + min(L.spec, 1.0) * 0.1;
                col = lerp(col, foamLit, foam * 0.95);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
