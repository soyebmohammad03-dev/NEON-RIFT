// Facade shader for the low-poly kit towers (Asian Night pack). The kit texture keeps the architecture (bands, fins,
// crowns); on top of it every vertical face gets a procedural office window grid in WORLD space, so it works on static
// batched geometry and stays consistent across towers of any size:
//   - floors 3.6 m, bays 3.2 m; each face gets its own seed, lit fraction and office/residential mix
//   - occasional dark floors and fully lit floors; warm, cool and fluorescent interiors, rare colour accents
//   - unlit windows read as dark glass with a faint sky reflection; a lobby band on the ground floor
//   - architectural uplight scallops near the ground, plus a light-pollution bounce that fades with height
// Windows are filtered by screen footprint (like NeonRift/SkylineBackdrop) so distant facades average to a glow
// instead of shimmering. Lit by SH ambient and the main light; fogged with URP fog.
Shader "NeonRift/TowerFacade"
{
    Properties
    {
        _BaseMap ("Kit Albedo", 2D) = "white" {}
        _BaseColor ("Facade Tint", Color) = (0.62, 0.64, 0.7, 1)
        _EmissionMap ("Kit Emission", 2D) = "black" {}
        _KitEmission ("Kit Emission Strength", Range(0, 3)) = 0.55
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5

        _WindowSize ("Window Cell (bay m, floor m)", Vector) = (3.2, 3.6, 0, 0)
        _LitFraction ("Lit Fraction", Range(0, 1)) = 0.3
        _WindowStrength ("Window Strength", Range(0, 1)) = 1
        [HDR] _WarmWindow ("Warm Interior", Color) = (1.55, 0.98, 0.56, 1)
        [HDR] _CoolWindow ("Cool Interior", Color) = (0.8, 0.92, 1.12, 1)
        [HDR] _FluoroWindow ("Fluorescent Office", Color) = (0.92, 1.12, 1.0, 1)
        [HDR] _AccentWindow ("Accent", Color) = (1.4, 0.38, 0.85, 1)
        _GlassColor ("Dark Glass", Color) = (0.018, 0.022, 0.03, 1)
        [HDR] _LobbyColor ("Lobby", Color) = (1.8, 1.35, 0.9, 1)

        [HDR] _UplightColor ("Uplight", Color) = (1.1, 0.8, 0.5, 1)
        _UplightHeight ("Uplight Height (m)", Float) = 16
        _UplightSpacing ("Uplight Spacing (m)", Float) = 8
        [HDR] _CityBounce ("Light-Pollution Bounce", Color) = (0.55, 0.45, 0.38, 1)
        _BounceHeight ("Bounce Falloff (m)", Float) = 130
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _WarmWindow, _CoolWindow, _FluoroWindow, _AccentWindow, _GlassColor, _LobbyColor, _UplightColor, _CityBounce;
            float4 _WindowSize;
            half _KitEmission, _Cutoff, _LitFraction, _WindowStrength;
            float _UplightHeight, _UplightSpacing, _BounceHeight;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

        void ClipAlpha(float2 uv)
        {
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - _Cutoff);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
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
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                #if defined(_ALPHATEST_ON)
                    clip(tex.a - _Cutoff);
                #endif
                float3 n = normalize(input.normalWS);
                float3 pos = input.positionWS;
                half3 albedo = tex.rgb * _BaseColor.rgb;

                Light mainLight = GetMainLight();
                half3 lighting = SampleSH(n) + mainLight.color * saturate(dot(n, mainLight.direction));
                half bounce = exp(-max(pos.y, 0.0) / _BounceHeight);
                half3 colour = albedo * (lighting + _CityBounce.rgb * bounce);
                colour += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _KitEmission;

                half vertical = 1.0 - smoothstep(0.35, 0.65, abs(n.y));
                if (vertical > 0.001)
                {
                    bool xFacing = abs(n.x) > abs(n.z);
                    float side = xFacing ? sign(n.x) : sign(n.z);
                    float u = xFacing ? pos.z * side : -pos.x * side;
                    float plane = xFacing ? pos.x : pos.z;
                    float faceSeed = floor(plane * 0.25) * 1.37 + side * 11.0 + (xFacing ? 53.0 : 0.0);

                    float2 cell = float2(u, pos.y) / _WindowSize.xy;
                    float2 id = floor(cell);
                    float2 f = frac(cell);

                    // Per face: how busy it is and whether it reads as offices (cool/fluorescent) or homes (warm).
                    float faceLit = _LitFraction * (0.5 + 1.0 * Hash(float2(faceSeed, 3.1)));
                    bool offices = Hash(float2(faceSeed, 8.7)) < 0.62;
                    // Lights come on by suite (runs of 3-5 bays on one floor), then a few single windows differ.
                    float suiteWidth = offices ? 4.0 : 2.0;
                    float suite = floor(id.x / suiteWidth);
                    float suiteOn = step(Hash(float2(suite * 1.91 + faceSeed, id.y * 0.73)), faceLit);
                    float single = Hash(id + faceSeed * 0.37);
                    float floorDark = step(0.88, Hash(float2(id.y, faceSeed)));
                    float floorBright = step(0.965, Hash(float2(id.y + 7.0, faceSeed)));
                    float lit = max(suiteOn * step(0.14, single) * (1.0 - floorDark), floorBright * step(0.05, single));
                    lit = max(lit, step(single, faceLit * 0.12));   // the odd late worker in a dark suite
                    float pick = Hash(float2(suite, id.y) * 1.3 + faceSeed);
                    half3 tint = offices
                        ? (pick < 0.55 ? _FluoroWindow.rgb : (pick < 0.92 ? _CoolWindow.rgb : _WarmWindow.rgb))
                        : (pick < 0.74 ? _WarmWindow.rgb : (pick < 0.985 ? _CoolWindow.rgb : _AccentWindow.rgb));
                    half brightness = (0.22 + 0.55 * Hash(float2(suite, id.y) * 1.7 + faceSeed)) * (0.85 + 0.3 * Hash(id * 2.3 + faceSeed));
                    // Interior read up close: ceiling lights brighter, desks and partitions darker, blinds on some windows.
                    brightness *= lerp(0.55, 1.0, smoothstep(0.3, 0.45, f.y)) * (0.85 + 0.15 * f.y);
                    float blinds = step(Hash(id * 0.71 + faceSeed), 0.28);
                    brightness *= lerp(1.0, 0.7 + 0.3 * step(0.45, frac(f.y * 12.0)), blinds);

                    // Glass inside the mullions; the ground floor is a taller lobby.
                    bool lobby = id.y < 0.5;
                    float frame = lobby
                        ? step(0.04, f.x) * step(f.x, 0.96) * step(0.12, f.y) * step(f.y, 0.92)
                        : step(0.08, f.x) * step(f.x, 0.92) * step(0.22, f.y) * step(f.y, 0.86) * (1.0 - step(abs(f.x - 0.5), 0.018));
                    half3 glass = _GlassColor.rgb + SampleSH(reflect(-GetWorldSpaceNormalizeViewDir(pos), n)) * 0.04;
                    half3 window = lobby ? _LobbyColor.rgb * (0.55 + 0.45 * Hash(id + faceSeed)) : lerp(glass, tint * brightness, lit);

                    // Footprint filtering: sub-pixel cells fade to the facade's average (lit fraction of glass + mullions).
                    float2 footprint = fwidth(cell);
                    half detail = saturate(1.6 - max(footprint.x, footprint.y) * 2.2);
                    half3 averageTint = offices ? (_FluoroWindow.rgb * 0.55 + _CoolWindow.rgb * 0.35 + _WarmWindow.rgb * 0.1)
                                                : (_WarmWindow.rgb * 0.72 + _CoolWindow.rgb * 0.24 + _AccentWindow.rgb * 0.04);
                    half3 averageWindow = lerp(_GlassColor.rgb, averageTint * 0.36, saturate(faceLit * 0.95 + 0.04));
                    half glassCover = 0.53;
                    // Lit rooms spill a little light onto their mullions and spandrels.
                    half3 spill = tint * brightness * lit * 0.05 * (1.0 - frame);
                    half3 detailed = lerp(colour, window, frame) + spill;
                    half3 averaged = lerp(colour, averageWindow, glassCover);
                    half3 facade = lerp(averaged, detailed, detail);
                    colour = lerp(colour, facade, vertical * _WindowStrength);

                    // Architectural uplights: soft scallops washing up the lowest floors.
                    float scallop = pow(saturate(cos(u * 6.2831853 / _UplightSpacing) * 0.5 + 0.5), 3.0);
                    half wash = exp(-max(pos.y, 0.0) / _UplightHeight) * scallop * vertical;
                    colour += (albedo + 0.04) * _UplightColor.rgb * wash;
                }

                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - p);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, n, lightDirection));
                #if UNITY_REVERSED_Z
                    c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = c;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                ClipAlpha(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                ClipAlpha(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                ClipAlpha(input.uv);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
