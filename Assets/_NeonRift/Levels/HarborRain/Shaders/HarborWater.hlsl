// Harbor Rain water: code shared by the open sea (HarborOcean), the breaking waves (HarborBreaker) and the water that
// comes over the seawall (HarborSheet), so the three agree on swell, foam, colour and lighting where they meet.
// Globals are written every frame by HarborOcean.cs / HarborWaves.cs.
#ifndef HARBOR_WATER_INCLUDED
#define HARBOR_WATER_INCLUDED

float _HR_Time;          // Time.time (CPU and GPU evaluate the same waves)
float _HR_Storm;         // storm intensity 0..1
float _HR_Lightning;     // lightning flash 0..1
float _HR_Level;         // still-water level (m)
float _HR_ShoreZ;        // seawall face (world z); open sea is +z
float4 _HR_Surge[4];     // per wave section: centre x, crest z, height m, half width m
float4 _HR_SurgeFoam[4]; // per wave section: trail foam 0..1, trail length m, wall foam 0..1, curl 0..1

float WHash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
float WNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(WHash(i), WHash(i + float2(1, 0)), u.x), lerp(WHash(i + float2(0, 1)), WHash(i + 1), u.x), u.y);
}
float WFbm(float2 p) { return WNoise(p) * 0.5 + WNoise(p * 2.07 + 13.1) * 0.3 + WNoise(p * 4.3 + 7.7) * 0.2; }

// Four Gerstner swells running inland (−z). Mirrored on the CPU in HarborBreaker.Swell().
float3 WGerstner(float4 wave, float2 p, float t, float amp, inout float crest)
{
    float k = 6.28318 / wave.w;
    float c = sqrt(9.8 / k);
    float2 d = normalize(wave.xy);
    float f = k * (dot(d, p) - c * t);
    float a = wave.z / k * amp;
    crest += saturate(sin(f) * 1.2 - 0.25) * wave.z * amp;
    return float3(d.x * a * cos(f), a * sin(f), d.y * a * cos(f));
}

// Sea surface displacement at a still-water point p (x, z). Swells are damped toward the wall (shallower water,
// sheltered), so the big breaking sets read as events rather than as constant hills.
float3 SeaDisplace(float2 p, out float crest, out float near)
{
    float t = _HR_Time;
    float storm = saturate(_HR_Storm);
    float shore = p.y - _HR_ShoreZ;
    float amp = lerp(0.5, 0.95, storm) * lerp(0.38, 1.0, saturate(shore / 170.0));
    crest = 0;
    float3 o = WGerstner(float4(0.15, -1, 0.2, 90), p, t, amp, crest);
    o += WGerstner(float4(-0.35, -1, 0.18, 52), p, t * 1.05, amp, crest);
    o += WGerstner(float4(0.6, -1, 0.15, 23), p, t, amp, crest);
    o += WGerstner(float4(-0.8, -0.6, 0.12, 11), p, t * 1.1, amp * 0.8, crest);
    // Reflected chop off the wall: short, confused waves in the first 40 m.
    near = saturate(1 - shore / 40.0);
    o.y += near * (sin(p.x * 0.31 + t * 2.3) * sin(p.y * 0.47 - t * 1.9)) * 0.3 * amp;
    return o;
}

// Rain rings on standing water: one expanding ring per cell, two layers. Returns a normal offset (x, z).
float2 RainRings(float2 p, float t)
{
    float2 g = 0;
    [unroll] for (int k = 0; k < 2; k++)
    {
        float sc = k == 0 ? 1.6 : 2.7;
        float2 c = floor(p * sc), f = frac(p * sc);
        float2 o = float2(WHash(c + k * 7.1), WHash(c + 3.3 + k)) * 0.6 + 0.2;
        float age = frac(t * (1.1 + k * 0.4) + WHash(c + 9.7 + k));
        float2 d = f - o;
        float r = length(d) + 1e-4;
        float radius = age * 0.45;
        float ring = sin(saturate((r - radius) * 25 + 1.57) * 6.283) * saturate(1 - abs(r - radius) * 18) * (1 - age) * (1 - age);
        g += d / r * ring * 0.6;
    }
    return g;
}

// Foam texture: R = lacy bubble network, G = dense patches, B = long streaks (generated, tileable).
half3 FoamSample(TEXTURE2D_PARAM(tex, samp), float2 uv) { return SAMPLE_TEXTURE2D(tex, samp, uv).rgb; }

// Turns a foam coverage amount (0..1) into a broken-up mask using the foam texture at two scales, so foam thins into
// lace and holes instead of a uniform grey film.
half FoamMask(TEXTURE2D_PARAM(tex, samp), float2 worldXZ, float2 flow, half amount)
{
    float t = _HR_Time;
    half3 a = FoamSample(TEXTURE2D_ARGS(tex, samp), worldXZ * 0.045 + flow * t * 0.045);
    half3 b = FoamSample(TEXTURE2D_ARGS(tex, samp), worldXZ * 0.11 - flow.yx * t * 0.03 + 0.37);
    half lace = a.r * 0.6 + b.r * 0.4;
    half patches = a.g * 0.5 + b.g * 0.5;
    // Thin foam keeps only the lace; dense foam fills the patches in.
    half m = saturate((lace + patches * amount * 1.4) * amount * 1.9 - (1 - amount) * 0.55);
    return saturate(m);
}

half3 WaterSpecular(Light light, half3 n, half3 v, half power, half gain)
{
    half3 h = normalize(light.direction + v);
    half s = pow(saturate(dot(n, h)), power) * gain;
    return light.color * (s * light.distanceAttenuation * light.shadowAttenuation);
}

struct WaterLighting
{
    half3 spec;     // specular streaks (sharp)
    half3 diffuse;  // diffuse light on foam / white water
    half3 wrap;     // wrapped light for the translucent crest
};

// Moon, every directional / local light that reaches the surface (Forward and Forward+), and ambient.
WaterLighting GatherWaterLights(float3 positionWS, half3 n, half3 v, float2 screenUV, half power)
{
    WaterLighting o;
    Light main = GetMainLight(TransformWorldToShadowCoord(positionWS));
    // The moon: sparse, sharp glints (a broad sheen off every ripple read as blue noise over the whole sea).
    o.spec = WaterSpecular(main, n, v, power * 2.5, 0.9);
    half ndl = dot(n, main.direction);
    o.diffuse = main.color * saturate(ndl) * main.shadowAttenuation + SampleSH(n);
    o.wrap = main.color * saturate(ndl * 0.5 + 0.5) * 0.4 + SampleSH(half3(0, 1, 0)) * 0.5;
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = screenUV;
#if defined(_ADDITIONAL_LIGHTS)
    #if USE_CLUSTER_LIGHT_LOOP
    for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
    {
        Light l = GetAdditionalLight(li, positionWS, half4(1, 1, 1, 1));
        o.spec += WaterSpecular(l, n, v, power * 0.7, 3);
        o.diffuse += l.color * saturate(dot(n, l.direction));
        o.wrap += l.color * saturate(dot(n, l.direction) * 0.5 + 0.5) * 0.5;
    }
    #endif
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light l = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        o.spec += WaterSpecular(l, n, v, power * 0.4, 1.6);
        half3 c = l.color * l.distanceAttenuation;
        o.diffuse += c * saturate(dot(n, l.direction));
        o.wrap += c * saturate(dot(n, l.direction) * 0.5 + 0.5) * 0.6;
    LIGHT_LOOP_END
#endif
    // Lightning lights every surface facing the sky.
    half sky = saturate(n.y * 0.6 + 0.4);
    o.diffuse += _HR_Lightning * half3(0.75, 0.82, 1.0) * sky * 1.4;
    o.wrap += _HR_Lightning * half3(0.75, 0.82, 1.0) * 0.8;
    return o;
}

// Reflection of the storm sky: the reflection probe plus a lighter haze toward the horizon (grazing angles), so the
// sea separates from the sky and land instead of reading as a black void. Lightning brightens the reflection.
half3 SkyReflection(half3 r, float3 positionWS, float2 screenUV, half roughness, half3 skyTint)
{
    half3 env = GlossyEnvironmentReflection(r, positionWS, roughness, (half)1, screenUV);
    half horizon = pow(1 - saturate(abs(r.y)), 5);
    return env * 1.05 + skyTint * (0.55 + horizon * 1.8) + _HR_Lightning * half3(0.5, 0.56, 0.7) * (0.6 + horizon);
}

#endif
