// A team logo on a wall as a hologram, in the style of the flag banners
// (HologramBanner): dark glass with the logo in light. The logo's edges are cut
// sharp at any distance from its alpha (with screen-space antialiasing, not the
// blurry far mips), with a white-hot outline round every shape over the team
// colour, scan lines, a faint grid, a glowing frame and the odd flicker.
// WorldMaterials puts it on TFC's team emblem textures (fitted to each face).
Shader "FortressUnited/HoloEmblem"
{
    Properties
    {
        [HDR] _Color ("Team colour", Color) = (0.07, 0.65, 0.97, 1)
        [HDR] _Core ("Hot colour", Color) = (0.75, 1, 1, 1)
        _Logo ("Logo (alpha)", 2D) = "black" {}
        _Glass ("Glass colour", Color) = (0.01, 0.015, 0.025, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "HoloEmblem"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Logo); SAMPLER(sampler_Logo);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _Core, _Glass;
                float4 _Logo_ST;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                // The logo's alpha, sampled from the sharpest mip that isn't aliased, then cut at
                // its middle with a one-pixel soft edge: crisp at any distance
                float a = SAMPLE_TEXTURE2D_GRAD(_Logo, sampler_Logo, uv, ddx(uv) * 0.5, ddy(uv) * 0.5).a;
                float w = max(fwidth(a), 1e-3);
                float body = smoothstep(0.5 - w, 0.5 + w, a);
                // The outline: a thin band just inside each shape's edge
                float edge = body * (1 - smoothstep(0.62 - w, 0.62 + w * 2, a));

                float t = _Time.y;
                float scan = 0.82 + 0.18 * sin(uv.y * 340 - t * 6);
                float flicker = 0.92 + 0.08 * step(0.985, frac(sin(floor(t * 12) * 12.9898) * 43758.5453));
                float2 g = abs(frac(uv * 24) - 0.5);
                float grid = (1 - smoothstep(0.46, 0.5, max(g.x, g.y))) * 0.06;
                // A glowing frame round the panel
                float2 e = min(uv, 1 - uv);
                float fw = max(fwidth(uv.x), fwidth(uv.y));
                float frame = 1 - smoothstep(0.012, 0.012 + fw * 1.5, min(e.x, e.y));
                float frameIn = (1 - smoothstep(0.03, 0.03 + fw * 1.5, min(e.x, e.y))) * (1 - frame) * 0.25;

                half3 col = _Glass.rgb;
                col += _Color.rgb * (body * 0.9 * scan + grid + frameIn) * flicker;
                col += _Core.rgb * (edge * 1.6 + frame * 0.9) * flicker;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
