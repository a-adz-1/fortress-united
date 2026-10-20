// Water, slime and lava for map liquids ('!' textures; WorldMaterials sets it up):
// - ripples: two layers of moving noise bend the surface normal
// - reflections of the sky and reflection probes, stronger at grazing angles
// - sun glints on the ripples
// - a see-through body in the old texture's colour (deeper looking straight down)
// - lava glows (_Glow) and churns slower
Shader "FortressUnited/Water"
{
    Properties
    {
        _ShallowColor ("Shallow colour", Color) = (0.1, 0.35, 0.4, 1)
        _DeepColor ("Deep colour", Color) = (0.02, 0.08, 0.12, 1)
        _Clarity ("See-through (0 opaque)", Range(0, 1)) = 0.35
        _WaveScale ("Ripple size (m)", Float) = 1.6
        _WaveSpeed ("Ripple speed", Float) = 0.35
        _WaveStrength ("Ripple strength", Range(0, 1)) = 0.35
        _Glow ("Glow (lava)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _Clarity;
                float _WaveScale;
                float _WaveSpeed;
                half _WaveStrength;
                half _Glow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }

            // Height of the ripples: two layers drifting different ways, plus a finer one
            float height(float2 p, float t)
            {
                return noise(p + float2(t, t * 0.6)) * 0.5
                     + noise(p * 1.9 + float2(-t * 0.8, t * 0.3)) * 0.35
                     + noise(p * 4.3 + float2(t * 0.4, -t)) * 0.15;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                float t = _Time.y * _WaveSpeed;
                // Ripples in the plane of the surface (world XZ for floors, the right plane for walls)
                float3 a = abs(n);
                float2 p = (a.y > a.x && a.y > a.z ? i.positionWS.xz : a.x > a.z ? i.positionWS.zy : i.positionWS.xy) / _WaveScale;
                float e = 0.05;
                float h0 = height(p, t);
                float2 slope = float2(height(p + float2(e, 0), t) - h0, height(p + float2(0, e), t) - h0) / e;
                float3 tx = normalize(cross(n, abs(n.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 ty = cross(n, tx);
                float3 wn = normalize(n - (tx * slope.x + ty * slope.y) * _WaveStrength * 0.25);

                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                float facing = saturate(dot(wn, view));
                half fresnel = 0.03 + 0.97 * pow(1 - facing, 5);

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 lit = sun.color * (saturate(dot(n, sun.direction)) * sun.shadowAttenuation * 0.6 + 0.2) + SampleSH(n);
                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, facing) * lit;
                // Reflections stay above the horizon on a level surface (ripples mustn't show the ground below the sky)
                float3 rdir = reflect(-view, wn);
                if (n.y > 0.7) rdir.y = max(rdir.y, 0.03);
                half3 refl = GlossyEnvironmentReflection(normalize(rdir), 0.06, 1);
                half3 glint = sun.color * sun.shadowAttenuation * pow(saturate(dot(wn, normalize(sun.direction + view))), 400) * 4;

                half3 col = lerp(body, refl, fresnel * (1 - _Glow)) + glint * (1 - _Glow);
                // Lava: bright churning crust, hottest in the ripple troughs
                col += _ShallowColor.rgb * _Glow * (1.5 + 2.5 * (1 - h0));
                half alpha = saturate(lerp(1 - _Clarity, 1, fresnel) + _Glow);
                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
