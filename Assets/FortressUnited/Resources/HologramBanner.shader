// A holographic flag banner: a translucent pennant in the team colour with
// the team logo (Resources/Teams/Logo_*.png), scanlines, a faint grid,
// glowing edges and the odd flicker. It waves gently, more away from the
// pole (u = 0 at the pole). Two-sided; the logo is mirrored on the back so
// it reads correctly from both sides. HologramBanner.cs drives it.
Shader "FortressUnited/HologramBanner"
{
    Properties
    {
        [HDR] _Color ("Team colour", Color) = (0.07, 0.65, 0.97, 1)
        [HDR] _Core ("Hot colour", Color) = (0.6, 1, 1, 1)
        _Logo ("Logo (alpha)", 2D) = "black" {}
        _LogoRect ("Logo rect in banner UV (x, y, w, h)", Vector) = (0.12, 0.38, 0.76, 0.5)
        _Tip ("Height of the pennant's point (UV)", Float) = 0.22
        _Wave ("Wave amount (m)", Float) = 0.03
        _Now ("Time (set by script)", Float) = 0
        _Fill ("Panel opacity", Range(0, 1)) = 0.28
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Logo); SAMPLER(sampler_Logo);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Core;
                float4 _Logo_ST;
                float4 _LogoRect;
                float _Tip;
                float _Wave;
                float _Now;
                float _Fill;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = i.positionOS.xyz;
                // Wave: nothing at the pole, more towards the free edge
                float u = i.uv.x;
                p += i.normalOS * sin(u * 5.0 - _Now * 2.2 + i.uv.y * 1.5) * _Wave * u;
                o.positionCS = TransformObjectToHClip(p);
                o.uv = i.uv;
                return o;
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float2 uv = i.uv;
                // Pennant shape: the bottom comes to a point in the middle
                float vcut = _Tip * abs(uv.x * 2.0 - 1.0);
                clip(uv.y - vcut);

                // Edges glow (sides, top, and along the V)
                float edge = min(min(uv.x, 1.0 - uv.x), 1.0 - uv.y);
                edge = min(edge, (uv.y - vcut) * 0.7);
                float rim = saturate(1.0 - edge / 0.035);

                float scan = 0.75 + 0.25 * sin(uv.y * 220.0 + _Now * 6.0);
                float grid = (step(0.97, frac(uv.x * 14.0)) + step(0.97, frac(uv.y * 24.0))) * 0.25;
                // A band of interference drifting up now and then
                float band = saturate(1.0 - abs(frac(_Now * 0.15) * 1.6 - 0.3 - uv.y) / 0.04) * 0.6;
                float flicker = 0.92 + 0.08 * Hash(floor(_Now * 12.0));

                // Logo, mirrored on the back face
                float2 luv = (uv - _LogoRect.xy) / _LogoRect.zw;
                if (!front) luv.x = 1.0 - luv.x;
                float inside = step(0.0, luv.x) * step(luv.x, 1.0) * step(0.0, luv.y) * step(luv.y, 1.0);
                float logo = SAMPLE_TEXTURE2D(_Logo, sampler_Logo, luv).a * inside;

                float a = (_Fill * scan + grid * 0.5 + rim * 0.9 + band * 0.3) * flicker;
                half3 col = _Color.rgb * a;
                // Logo: the team colour, bright, with a touch of the hot core
                col = lerp(col, lerp(_Color.rgb * 1.5, _Core.rgb, 0.3), logo * flicker);
                a = saturate(a + logo * 0.95);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
