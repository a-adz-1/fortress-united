// Energy-shield hit effect for robot armour: glowing rings that run out
// across the surface from each hit point, a hot spot where it landed, and a
// rim flash for heavy hits. Drawn additively as a second material over the
// body, pushed out slightly along the normals. HitRipple.cs feeds the hits.
Shader "FortressUnited/ShieldRipple"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.6, 1.4, 2.5, 1)
        _Inflate ("Push out along normals (m)", Float) = 0.004
        _RingSpeed ("Ring speed (m/s)", Float) = 2.2
        _RingWidth ("Ring width (m)", Float) = 0.05
        _Life ("Ripple life (s)", Float) = 0.6
        _Flash ("Rim flash", Float) = 0
        _Now ("Current time (set by HitRipple)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ShieldRipple"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Inflate;
                float _RingSpeed;
                float _RingWidth;
                float _Life;
                float _Flash;
                float _Now;
            CBUFFER_END

            // Up to four hits at once: xyz world position, w the time it landed
            float4 _HitPos[4];
            float4 _HitStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz) + o.normalWS * _Inflate;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                float t = _Now;

                float glow = 0;
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float age = t - _HitPos[k].w;
                    if (age < 0 || age > _Life)
                        continue;
                    float life = age / _Life;
                    float fade = 1.0 - life;
                    float d = distance(input.positionWS, _HitPos[k].xyz);
                    float r = age * _RingSpeed;
                    float x = (d - r) / _RingWidth;
                    float ring = exp(-x * x);
                    float bands = 0.6 + 0.4 * sin(d * 140.0 - age * 45.0);   // fine ripples behind the front
                    float wake = saturate(1.0 - d / max(r, 0.001)) * 0.25 * bands;
                    float spot = exp(-d * d / 0.003) * fade * 1.5;
                    glow += (ring * bands + wake + spot) * fade * fade * _HitStrength[k];
                }
                glow += _Flash * (0.08 + rim * 1.2);
                return half4(_Color.rgb * glow, 1);
            }
            ENDHLSL
        }
    }
}
