// The space sky for space maps (MapAtmosphere uses it when a map's sky is a starfield):
// drawn per pixel, so it's sharp at any resolution, with
// - stars in three layers, sized to stay at least a pixel (no shimmer), in varied colours,
//   the brightest bright enough to bloom
// - a Milky Way band with dark dust lanes
// - coloured nebula clouds
// - a banded gas giant lit by the sun (_SunDir), with an atmosphere rim and a dark night side
// - the sun's disc and glare
Shader "FortressUnited/SpaceSky"
{
    Properties
    {
        _SunDir ("Direction to the sun", Vector) = (0.4, 0.5, 0.6, 0)
        _SunColor ("Sun colour", Color) = (1, 0.92, 0.85, 1)
        _PlanetDir ("Direction to the planet", Vector) = (-0.6, 0.35, -0.7, 0)
        _PlanetSize ("Planet radius (radians)", Float) = 0.42
        _PlanetA ("Planet bands A", Color) = (0.86, 0.62, 0.42, 1)
        _PlanetB ("Planet bands B", Color) = (0.32, 0.5, 0.6, 1)
        _Atmos ("Planet atmosphere", Color) = (0.45, 0.75, 1, 1)
        _NebulaA ("Nebula A", Color) = (0.55, 0.18, 0.75, 1)
        _NebulaB ("Nebula B", Color) = (0.1, 0.55, 0.65, 1)
        _Nebula ("Nebula strength", Float) = 0.5
        _Stars ("Star brightness", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SunDir, _PlanetDir;
                half4 _SunColor, _PlanetA, _PlanetB, _Atmos, _NebulaA, _NebulaB;
                float _PlanetSize, _Nebula, _Stars;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float4 hash4(float3 p)
            {
                return float4(hash(p), hash(p + 17.31), hash(p + 41.77), hash(p + 73.13));
            }
            float noise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x), lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x), lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }
            float fbm(float3 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 5; k++) { s += a * noise(p); p = p * 2.03 + 11.7; a *= 0.5; }
                return s;
            }

            // One layer of stars: one possible star per grid cell, at least a pixel wide
            half3 Stars(float3 d, float scale, float density, float pixel)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float4 h = hash4(cell);
                if (h.z > density) return 0;
                float3 star = cell + 0.2 + 0.6 * h.xyw;
                float dist = length(p - star) / scale;                     // angle from the star (radians)
                float size = lerp(0.0004, 0.0016, h.x * h.x);
                float r = max(size, pixel * 0.75);
                float k = exp(-dist * dist / (r * r)) * (size * size) / (r * r);
                float bright = 0.4 + 6 * pow(h.y, 12);                    // a few very bright ones bloom
                half3 tint = h.w < 0.33 ? half3(1, 0.78, 0.55) : (h.w < 0.75 ? half3(1, 0.97, 0.94) : half3(0.7, 0.82, 1));
                return tint * k * bright;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float pixel = max(length(fwidth(d)), 1e-5);
                float3 sun = normalize(_SunDir.xyz);

                // Milky Way: a band round a tilted great circle, dense with faint stars, crossed by dust
                float3 galaxy = normalize(float3(0.32, 0.82, 0.47));
                float b = dot(d, galaxy);
                float band = exp(-b * b * 28);
                float dust = smoothstep(0.42, 0.68, fbm(d * 7 + 3.1));
                half3 col = half3(0.62, 0.56, 0.5) * band * (0.05 + 0.08 * fbm(d * 14)) * (1 - 0.75 * dust);

                // Nebula clouds, stronger near the band
                float n1 = fbm(d * 2.4 + 7.3), n2 = fbm(d * 5.1 + 1.7);
                float cloud = pow(saturate((n1 - 0.38) * 2.2), 2.2) * (0.35 + 0.65 * band);
                col += lerp(_NebulaA.rgb, _NebulaB.rgb, saturate(n2 * 1.6 - 0.3)) * cloud * _Nebula * 0.5;

                // Stars: three layers, more in the band, fewer through dust and bright cloud
                // Sparse: a clear black sky with stars in it, thicker along the Milky Way
                float density = 0.05 + 0.25 * band;
                half3 stars = Stars(d, 110, density * 1.6, pixel) * 1.4 + Stars(d, 260, density, pixel) * 0.8 + Stars(d, 560, density * 0.7, pixel) * 0.5;
                col += stars * _Stars * (1 - 0.6 * dust * band) * (1 - 0.5 * saturate(cloud * 2));

                // The sun: disc and glare
                float s = dot(d, sun);
                col += _SunColor.rgb * (smoothstep(0.99975, 0.99985, s) * 30 + pow(saturate(s), 900) * 4 + pow(saturate(s), 40) * 0.12);

                // The gas giant
                float3 pd = normalize(_PlanetDir.xyz);
                float3 t = normalize(cross(pd, abs(pd.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 bt = cross(t, pd);
                float2 uv = float2(dot(d, t), dot(d, bt)) / sin(_PlanetSize);
                float r2 = dot(uv, uv);
                float front = dot(d, pd);
                if (front > 0)
                {
                    if (r2 < 1)
                    {
                        float z = sqrt(1 - r2);
                        float3 N = normalize(uv.x * t + uv.y * bt - z * pd);
                        float3 axis = normalize(bt + t * 0.35);
                        float lat = dot(N, axis);
                        float swirl = fbm(N * 3 + 2) * 2.2;
                        float bands = 0.5 + 0.5 * sin(lat * 22 + swirl) * (0.7 + 0.3 * sin(lat * 7.0 + 1.3));
                        half3 surface = lerp(_PlanetA.rgb, _PlanetB.rgb, bands) * (0.85 + 0.3 * fbm(N * 18));
                        float lit = saturate(dot(N, sun) * 1.1 + 0.05);
                        float rim = pow(1 - z, 3);
                        half3 p = surface * lit * 0.9 + _Atmos.rgb * rim * saturate(dot(N, sun) + 0.35) * 1.2;
                        // The planet hides what's behind it; its night side stays faintly visible
                        col = p + surface * 0.015;
                    }
                    else
                    {
                        // Thin atmosphere halo outside the disc, on the lit side
                        float edge = sqrt(r2) - 1;
                        float3 N = normalize(uv.x * t + uv.y * bt);
                        col += _Atmos.rgb * exp(-edge * 40) * saturate(dot(N, sun) + 0.3) * 0.8;
                    }
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
