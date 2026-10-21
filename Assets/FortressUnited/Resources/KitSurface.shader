// Map surfaces in an Asset Store kit's art (WorldMaterials uses it when a kit is
// imported): modules cut from the kit's texture atlases, repeated along each face in
// its own plane. Walls use a wall module that starts at the floor below the face
// (uv1.y, from the map loader), floors and ceilings their own modules. The kit's red
// accents are repainted in the base's team colour (steel outside the bases), and its
// glow maps light up.
Shader "FortressUnited/KitSurface"
{
    Properties
    {
        _WallAlb ("Wall albedo", 2D) = "white" {}
        _WallNrm ("Wall normal", 2D) = "bump" {}
        _WallMet ("Wall metal", 2D) = "black" {}
        _WallIllum ("Wall glow", 2D) = "black" {}
        _WallCrop ("Wall module in the atlas (u0 v0 u1 v1)", Vector) = (0, 0, 1, 1)
        _WallModule ("Wall module size (m)", Vector) = (3.6, 3.6, 0, 0)
        _FloorAlb ("Floor albedo", 2D) = "white" {}
        _FloorNrm ("Floor normal", 2D) = "bump" {}
        _FloorMet ("Floor metal", 2D) = "black" {}
        _FloorCrop ("Floor module (u0 v0 u1 v1)", Vector) = (0, 0, 0.5, 0.5)
        _CeilCrop ("Ceiling module (u0 v0 u1 v1)", Vector) = (0, 0.5, 0.5, 1)
        _FloorModule ("Floor module size (m)", Float) = 2.4
        _Tone ("Brightness", Float) = 1
        _Accent ("Team paint on this texture (0/1)", Float) = 0
        _AccentColor ("Its team paint", Color) = (0.06, 0.59, 0.91, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_WallAlb); SAMPLER(sampler_WallAlb);
            TEXTURE2D(_WallNrm); TEXTURE2D(_WallMet); TEXTURE2D(_WallIllum);
            TEXTURE2D(_FloorAlb); SAMPLER(sampler_FloorAlb);
            TEXTURE2D(_FloorNrm); TEXTURE2D(_FloorMet);

            CBUFFER_START(UnityPerMaterial)
                float4 _WallAlb_ST, _FloorAlb_ST;
                float4 _WallCrop, _WallModule, _FloorCrop, _CeilCrop;
                float _FloorModule, _Tone, _Accent;
                half4 _AccentColor;
            CBUFFER_END

            float4 _TeamCentres[4];
            float4 _TeamPaint[4];
            float4 _TeamZoneMin[16];
            float4 _TeamZoneMax[16];

            void TeamAt(float3 p, out int team, out float weight)
            {
                team = -1; weight = 0;
                for (int z = 0; z < 16; z++)
                {
                    if (_TeamZoneMax[z].w < 0.5) continue;
                    if (all(p >= _TeamZoneMin[z].xyz) && all(p <= _TeamZoneMax[z].xyz)) { team = (int)_TeamZoneMax[z].w - 1; weight = 1; return; }
                }
                float d1 = 1e9, d2 = 1e9; int best = -1;
                for (int t = 0; t < 4; t++)
                {
                    if (_TeamCentres[t].w < 0.5) continue;
                    float d = distance(p, _TeamCentres[t].xyz);
                    if (d < d1) { d2 = d1; d1 = d; best = t; } else if (d < d2) d2 = d;
                }
                if (best < 0 || d2 > 1e8) return;
                team = best;
                weight = saturate(((d2 - d1) / max(d1 + d2, 1) - 0.1) * 30);
            }

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 face : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                float floorY : TEXCOORD3;
                half fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.floorY = v.face.y;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalOS);
                float3 a = abs(n);
                int kind = n.y > 0.7 ? 1 : (n.y < -0.7 ? 2 : 0);
                float3 tu, tv;
                if (a.y > a.x && a.y > a.z) { tu = float3(1, 0, 0); tv = float3(0, 0, 1); }
                else if (a.x > a.z) { tu = float3(0, 0, sign(n.x)); tv = float3(0, 1, 0); }
                else { tu = float3(-sign(n.z), 0, 0); tv = float3(0, 1, 0); }

                // Module coordinates: walls start at the floor below the face; floors and ceilings tile
                float2 m;
                float4 crop;
                if (kind == 0)
                {
                    m = float2(dot(i.positionOS, tu) / _WallModule.x, (i.positionOS.y - i.floorY) / _WallModule.y);
                    crop = _WallCrop;
                }
                else
                {
                    m = float2(dot(i.positionOS, tu), dot(i.positionOS, tv)) / _FloorModule;
                    crop = kind == 1 ? _FloorCrop : _CeilCrop;
                }
                float2 size = crop.zw - crop.xy;
                // Walls taller than one module: above the first, repeat only the module's upper
                // part (its plain panelling), so floor-level details (lights, vents) stay at the floor
                float vy = (kind == 0 && m.y > 1) ? 0.45 + 0.55 * frac((m.y - 1) / 0.55) : frac(m.y);
                float2 uv = crop.xy + float2(frac(m.x), vy) * size;
                // Gradients from the unwrapped coordinates, so mip levels don't jump at module seams
                float2 gx = ddx(m) * size, gy = ddy(m) * size;

                half4 alb; half3 nrm; half met; half3 illum = 0;
                if (kind == 0)
                {
                    alb = SAMPLE_TEXTURE2D_GRAD(_WallAlb, sampler_WallAlb, uv, gx, gy);
                    nrm = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_WallNrm, sampler_WallAlb, uv, gx, gy));
                    met = SAMPLE_TEXTURE2D_GRAD(_WallMet, sampler_WallAlb, uv, gx, gy).r;
                    illum = SAMPLE_TEXTURE2D_GRAD(_WallIllum, sampler_WallAlb, uv, gx, gy).rgb;
                }
                else
                {
                    alb = SAMPLE_TEXTURE2D_GRAD(_FloorAlb, sampler_FloorAlb, uv, gx, gy);
                    nrm = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_FloorNrm, sampler_FloorAlb, uv, gx, gy));
                    met = SAMPLE_TEXTURE2D_GRAD(_FloorMet, sampler_FloorAlb, uv, gx, gy).r;
                }

                // The kit's red accents in the team's colour (steel between the bases)
                half3 c = alb.rgb;
                float red = saturate((c.r - max(c.g, c.b) * 1.6) * 6) * step(0.25, c.r);
                int team; float teamW;
                TeamAt(i.positionWS, team, teamW);
                half3 paint = _Accent > 0.5 ? _AccentColor.rgb : (team >= 0 ? _TeamPaint[team].rgb : half3(0.3, 0.32, 0.35));
                paint = lerp(half3(0.3, 0.32, 0.35), paint, _Accent > 0.5 ? 1 : teamW);
                half lum = dot(c, half3(0.3, 0.59, 0.11));
                c = lerp(c, paint * (lum / 0.27), red);
                c *= _Tone;

                float3 nOS = normalize(n + tu * nrm.x + tv * nrm.y);
                float3 normalWS = normalize(TransformObjectToWorldNormal(nOS));

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalWS;
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fog;
                input.bakedGI = SampleSH(normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = 1;

                SurfaceData s = (SurfaceData)0;
                s.albedo = c;
                s.metallic = met * 0.6;
                s.smoothness = 0.25 + 0.4 * met;
                s.occlusion = 1;
                s.emission = illum * 2.5;
                s.alpha = 1;
                half4 col = UniversalFragmentPBR(input, s);
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
