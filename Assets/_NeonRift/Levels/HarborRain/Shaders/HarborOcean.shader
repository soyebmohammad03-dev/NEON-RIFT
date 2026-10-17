// Harbor Rain storm ocean (URP, opaque). All motion is on the GPU (HarborWater.hlsl):
//  - four Gerstner swells travelling inland (−z), scaled by the storm and damped toward the seawall,
//  - vertices landward of the wall (_HR_ShoreZ) sink under the quay so the sea never pokes through land.
// The big breaking waves are separate meshes (HarborBreaker); this surface only shows what they leave behind: foam
// trails behind a broken set and churned white water against the wall (_HR_SurgeFoam), and it lights up under the
// seawall floodlights, the harbour lights (specular streaks) and lightning.
Shader "NeonRift/HarborOcean"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.01, 0.024, 0.03, 1)
        _ShallowColor ("Crest body", Color) = (0.035, 0.075, 0.08, 1)
        _FoamColor ("Foam", Color) = (0.66, 0.71, 0.74, 1)
        _SkyColor ("Sky reflection tint", Color) = (0.07, 0.08, 0.095, 1)
        _FoamTex ("Foam (R lace, G patches, B streaks)", 2D) = "black" {}
        _Roughness ("Perceptual roughness", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
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
                half4 _DeepColor, _ShallowColor, _FoamColor, _SkyColor;
                float4 _FoamTex_ST;
                float _Roughness;
            CBUFFER_END
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 foam : TEXCOORD2;    // x = crest foam, y = wall proximity, z = foam left by broken waves
                float fogFactor : TEXCOORD3;
            };

            // Foam left on the surface by the breaking sets: a trail behind each broken crest, white water at the wall.
            float SetFoam(float2 p)
            {
                float f = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float4 s = _HR_Surge[i];
                    float4 sf = _HR_SurgeFoam[i];
                    if (sf.x + sf.z <= 0.001) continue;
                    float across = (p.x - s.x) / max(s.w, 1);
                    float lateral = exp(-across * across * 1.6);
                    float behind = p.y - s.y;
                    float trail = sf.x * saturate(behind / 6) * saturate(1 - behind / max(sf.y, 1)) * lateral;
                    float wall = sf.z * saturate(1 - (p.y - _HR_ShoreZ) / 45) * exp(-across * across * 0.9);
                    f = max(f, max(trail, wall));
                }
                return f;
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                float2 p = world.xz;
                float crest, near, cx, cz, nx, nz;
                float3 d = SeaDisplace(p, crest, near);
                float3 dx = SeaDisplace(p + float2(1.5, 0), cx, nx);
                float3 dz = SeaDisplace(p + float2(0, 1.5), cz, nz);
                float3 pos = float3(p.x, _HR_Level, p.y) + d;
                float3 px = float3(p.x + 1.5, _HR_Level, p.y) + dx;
                float3 pz = float3(p.x, _HR_Level, p.y + 1.5) + dz;
                float3 n = normalize(cross(pz - pos, px - pos));
                if (p.y < _HR_ShoreZ - 1.0) pos.y = _HR_Level - 6.0;   // under the land
                o.positionWS = pos;
                o.normalWS = n;
                o.foam = float3(saturate(crest * 2.0 + near * 0.2 * _HR_Storm), saturate(1 - (p.y - _HR_ShoreZ) / 14), SetFoam(p));
                o.positionCS = TransformWorldToHClip(pos);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _HR_Time;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                // Fine ripple normal from two scrolling noise fields (rain-pocked, wind-blown surface).
                float2 q = i.positionWS.xz;
                float e = 0.35;
                float n0 = WFbm(q * 0.35 + float2(t * 0.6, -t));
                float2 g1 = float2(n0 - WFbm(q * 0.35 + float2(e + t * 0.6, -t)), n0 - WFbm(q * 0.35 + float2(t * 0.6, e - t)));
                float m0 = WNoise(q * 1.7 - t * 2.1);
                float2 g2 = float2(m0 - WNoise(q * 1.7 + float2(e, 0) - t * 2.1), m0 - WNoise(q * 1.7 + float2(0, e) - t * 2.1));
                half3 n = normalize(i.normalWS + half3(g1.x + g2.x * 0.5, 0, g1.y + g2.y * 0.5) * 1.5);

                half fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
                half3 r = reflect(-v, n);
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 env = SkyReflection(r, i.positionWS, screenUV, (half)_Roughness, _SkyColor.rgb);
                half crest = saturate(i.foam.x);
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb * 1.6, saturate(crest * 1.4 + (1 - n.y) * 2.2));

                WaterLighting L = GatherWaterLights(i.positionWS, n, v, screenUV, 200);
                half3 col = body * (1 - fresnel) + env * fresnel + body * L.diffuse * 0.12 + L.spec;

                // Whitecaps on crests, wind-driven streaks (stretched along the swell's travel), the churning band along
                // the wall and the foam left by the breaking sets, all shaped by the foam texture.
                half3 ft = FoamSample(TEXTURE2D_ARGS(_FoamTex, sampler_FoamTex), float2(q.x * 0.05 + q.y * 0.02, q.y * 0.012 + t * 0.03));
                half streak = saturate(ft.b * 2.2 - 1.1) * lerp(0.15, 0.5, saturate(_HR_Storm)) * (0.4 + ft.g * 0.8);
                half cap = saturate((crest - 0.25) * 2.0);
                half amount = saturate(max(max(cap, streak), max(i.foam.y * lerp(0.3, 0.65, saturate(_HR_Storm)), i.foam.z)));
                half foam = FoamMask(TEXTURE2D_ARGS(_FoamTex, sampler_FoamTex), q, float2(0.25, -1), amount);
                half3 foamLit = _FoamColor.rgb * (L.diffuse * 0.9 + 0.12) + L.spec * 0.15;
                col = lerp(col, foamLit, foam * 0.92);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
