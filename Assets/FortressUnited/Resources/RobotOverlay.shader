// Effects drawn over a robot's armour, additively, as a second material
// pushed out slightly along the normals (RobotFx.cs drives it):
// - hits: rings of the team colour running out from each hit point
// - heat: an orange glow pulsing over the body while overheating
// - virus: green rows of corrupted data flickering across the body
// - glitch: team-coloured rows flickering while concussed or powering down
// - teleport: a bright band sweeping up the body as it beams out
Shader "FortressUnited/RobotOverlay"
{
    Properties
    {
        [HDR] _Color ("Team colour", Color) = (0.24, 2.08, 2.39, 1)
        [HDR] _HeatColor ("Heat colour", Color) = (3, 0.9, 0.2, 1)
        [HDR] _VirusColor ("Virus colour", Color) = (0.4, 2.5, 0.6, 1)
        _Inflate ("Push out along normals (m)", Float) = 0.004
        _RingSpeed ("Ring speed (m/s)", Float) = 2.2
        _RingWidth ("Ring width (m)", Float) = 0.05
        _Life ("Ripple life (s)", Float) = 0.6
        _Flash ("Rim flash", Float) = 0
        _Heat ("Heat", Range(0, 1)) = 0
        _Virus ("Virus", Range(0, 1)) = 0
        _Glitch ("Glitch", Range(0, 1)) = 0
        _Teleport ("Teleport progress (-1 off)", Float) = -1
        _BodyY ("Body bottom and top (world Y)", Vector) = (0, 2, 0, 0)
        _Now ("Current time (set by RobotFx)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "RobotOverlay"
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
                half4 _HeatColor;
                half4 _VirusColor;
                float _Inflate;
                float _RingSpeed;
                float _RingWidth;
                float _Life;
                float _Flash;
                float _Heat;
                float _Virus;
                float _Glitch;
                float _Teleport;
                float4 _BodyY;
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

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            // Rows that light up at random (about `amount` of them), changing `rate` times a second
            float Rows(float y, float t, float rowsPerMetre, float rate, float amount)
            {
                float row = floor(y * rowsPerMetre);
                float tick = floor(t * rate);
                return step(1.0 - amount, Hash(row * 17.13 + tick * 3.71));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                float t = _Now;
                float y = input.positionWS.y;

                // Hits
                float team = 0;
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float age = t - _HitPos[k].w;
                    if (age < 0 || age > _Life)
                        continue;
                    float fade = 1.0 - age / _Life;
                    float d = distance(input.positionWS, _HitPos[k].xyz);
                    float r = age * _RingSpeed;
                    float x = (d - r) / _RingWidth;
                    float ring = exp(-x * x);
                    float bands = 0.6 + 0.4 * sin(d * 140.0 - age * 45.0);   // fine ripples behind the front
                    float wake = saturate(1.0 - d / max(r, 0.001)) * 0.25 * bands;
                    float spot = exp(-d * d / 0.003) * fade * 1.5;
                    team += (ring * bands + wake + spot) * fade * fade * _HitStrength[k];
                }
                team += _Flash * (0.08 + rim * 1.2);

                // Concussion / power-down glitch: a few flickering rows
                team += _Glitch * Rows(y, t, 30.0, 14.0, 0.12 * _Glitch) * 0.6;

                // Beaming out: a bright front sweeping up, shimmering lines below it
                if (_Teleport >= 0)
                {
                    float front = lerp(_BodyY.x - 0.1, _BodyY.y + 0.1, _Teleport);
                    float fx = (y - front) / 0.05;
                    float lines = step(0.5, frac(y * 50.0 - t * 6.0));
                    float below = step(y, front);
                    team += exp(-fx * fx) * 3.0 + below * (0.35 + 0.5 * lines) * (1.0 - _Teleport * 0.5) + rim * 0.6;
                }

                // Overheating: a slow orange pulse, strongest at the edges
                float heat = _Heat * (0.15 + rim * 0.9) * (0.75 + 0.25 * sin(t * 6.0));

                // Virus: rows of green corrupted data, with the odd bright block
                float virus = 0;
                if (_Virus > 0)
                {
                    float rows = Rows(y, t, 40.0, 10.0, 0.18 * _Virus);
                    float cols = step(0.5, Hash(floor(input.positionWS.x * 30.0 + input.positionWS.z * 30.0) + floor(t * 10.0)));
                    virus = _Virus * rows * (0.4 + 0.6 * cols) * 0.7;
                }

                return half4(_Color.rgb * team + _HeatColor.rgb * heat + _VirusColor.rgb * virus, 1);
            }
            ENDHLSL
        }
    }
}
