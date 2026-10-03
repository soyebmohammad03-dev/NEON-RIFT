// Night sky for the city: zenith-to-horizon gradient, a warm light-pollution band, a slow two-scale overcast lit from
// below by the city (with moonlit thin edges), a moon with a soft halo (direction from _MoonDirection, set by the district builder from the
// moonlight), and sparse stars that only show through gaps in the cloud. Procedural (no textures), URP skybox.
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
        _MoonDirection ("Moon Direction (towards the moon)", Vector) = (0.4, 0.5, 0.75, 0)
        [HDR] _MoonColor ("Moon", Color) = (3, 3.2, 3.6, 1)
        _MoonSize ("Moon Size", Range(0.001, 0.05)) = 0.012
        [HDR] _MoonHaloColor ("Moon Halo", Color) = (0.08, 0.1, 0.16, 1)
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.55
        [HDR] _CloudColor ("Cloud (lit by the city)", Color) = (0.11, 0.07, 0.09, 1)
        _CloudSpeed ("Cloud Drift", Range(0, 0.05)) = 0.004
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
                half4 _ZenithColor, _HorizonColor, _GlowColor, _GroundColor, _MoonColor, _MoonHaloColor, _CloudColor;
                float4 _MoonDirection;
                half _GlowHeight, _HorizonBlend, _StarDensity, _StarBrightness, _MoonSize, _CloudCover, _CloudSpeed;
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

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(float3(i, 1.0)), b = Hash(float3(i + float2(1, 0), 1.0));
                float c = Hash(float3(i + float2(0, 1), 1.0)), d = Hash(float3(i + float2(1, 1), 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { s += a * Noise(p); p = p * 2.03 + 11.7; a *= 0.5; }
                return s;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float h = d.y;
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, saturate(pow(saturate(h / _HorizonBlend), 0.6)));
                half glow = exp(-max(h, 0) / _GlowHeight);
                sky += _GlowColor.rgb * glow;

                // Overcast layer: project onto a plane above the city, drift slowly.
                float2 uv = d.xz / max(h + 0.12, 0.05) * 1.6 + _Time.y * _CloudSpeed * float2(1.0, 0.35);
                // Two scales: broad weather banks modulate the finer overcast, so the sky has structure, not uniform noise.
                float n = Fbm(uv) * 0.7 + Fbm(uv * 0.23 + 5.3) * 0.3;
                float cover = saturate((n - (1.0 - _CloudCover)) * 2.4);
                cover *= saturate(h * 6.0 + 0.2);
                // Clouds glow with the city below: brighter and warmer near the horizon, faint overhead.
                half3 cloud = _CloudColor.rgb * (0.35 + 1.4 * glow) + _GlowColor.rgb * glow * 0.4;

                // Moon and halo (the halo also lights nearby cloud).
                float3 moonDir = normalize(_MoonDirection.xyz);
                float cosAngle = dot(d, moonDir);
                float disc = smoothstep(1.0 - _MoonSize, 1.0 - _MoonSize * 0.7, cosAngle);
                half3 halo = _MoonHaloColor.rgb * pow(saturate(cosAngle), 64.0) + _MoonHaloColor.rgb * 0.4 * pow(saturate(cosAngle), 8.0);
                cloud += halo * 2.0;
                // Thin cloud edges near the moon are lit through (silver lining); thick cores stay dark.
                cloud += _MoonHaloColor.rgb * 3.0 * pow(saturate(cosAngle), 24.0) * saturate(1.0 - cover) * 2.0;

                // Stars: one candidate per cell of a direction grid, hidden by the hazy horizon and the clouds.
                float3 cell = floor(d * 220.0);
                float r = Hash(cell);
                float star = step(1.0 - _StarDensity * 0.02, r) * saturate((h - 0.15) * 3.0);
                float3 local = frac(d * 220.0) - 0.5;
                star *= saturate(1.0 - length(local) * 3.0);
                half3 clear = sky + halo + disc * _MoonColor.rgb + star * _StarBrightness * (0.5 + 0.5 * Hash(cell + 7.0));
                half3 colour = lerp(clear, cloud + disc * _MoonColor.rgb * 0.15, cover);
                colour = h < 0 ? lerp(_HorizonColor.rgb + _GlowColor.rgb, _GroundColor.rgb, saturate(-h * 8.0)) : colour;
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
