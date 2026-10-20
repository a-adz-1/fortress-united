// The futuristic map surface (WorldMaterials puts it on walls, floors and ceilings):
// procedural panelling in each face's own plane, so it needs no textures and
// suits any brush map. Per panel (by a hash of its cell):
// - chamfered seams with real relief and shadowing in the grooves
// - some split in two; walls get vent grilles, floors grip plating, ceilings light strips
// - a share painted in the accent colour (the team's, on team surfaces)
// - a share of seams glowing (the team's glow, or a cool white)
// Laid out in object space, so doors and lifts carry their pattern with them.
Shader "FortressUnited/SciFiSurface"
{
    Properties
    {
        _WallColor ("Wall", Color) = (0.7, 0.72, 0.76, 1)
        _FloorColor ("Floor", Color) = (0.13, 0.14, 0.155, 1)
        _CeilColor ("Ceiling", Color) = (0.36, 0.38, 0.41, 1)
        _AccentColor ("Accent paint", Color) = (0.16, 0.17, 0.19, 1)
        _Accent ("Team paint (0 none, 1 team bands)", Range(0, 1)) = 0
        [HDR] _GlowColor ("Glow", Color) = (1.4, 2.2, 3.0, 1)
        _GlowAmount ("Glow line strength", Range(0, 3)) = 0.25
        _LightStrips ("Share of ceiling panels lit", Range(0, 1)) = 0.22
        _WallPanel ("Wall panel size (m)", Vector) = (2.4, 1.2, 0, 0)
        _FloorPanel ("Floor panel size (m)", Vector) = (1.6, 1.6, 0, 0)
        _Tone ("Brightness from the old texture", Float) = 1
        _Metal ("Metal (trims, doors)", Range(0, 1)) = 0
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
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WallColor, _FloorColor, _CeilColor, _AccentColor, _GlowColor;
                half _Accent, _GlowAmount, _LightStrips, _Tone, _Metal;
                float4 _WallPanel, _FloorPanel;
            CBUFFER_END

            // Team bases (MapLights): centre xyz, w = 1 if the team has one; their paint and glow
            float4 _TeamCentres[4];
            float4 _TeamPaint[4];
            float4 _TeamGlow[4];

            // Which team's base this point is in, and how surely (0 in the middle ground)
            void TeamAt(float3 p, out int team, out float weight)
            {
                team = -1; weight = 0;
                float d1 = 1e9, d2 = 1e9; int best = -1;
                for (int t = 0; t < 4; t++)
                {
                    if (_TeamCentres[t].w < 0.5) continue;
                    float d = distance(p, _TeamCentres[t].xyz);
                    if (d < d1) { d2 = d1; d1 = d; best = t; }
                    else if (d < d2) d2 = d;
                }
                if (best < 0 || d2 > 1e8) return;
                team = best;
                weight = saturate(((d2 - d1) / max(d1 + d2, 1) - 0.1) * 5);
            }

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalOS);
                float3 a = abs(n);
                // Which kind of surface, and its plane: u along it, v up it (walls) or across (floors)
                int kind = n.y > 0.7 ? 1 : (n.y < -0.7 ? 2 : 0);     // 0 wall, 1 floor, 2 ceiling
                float3 tu, tv;
                if (a.y > a.x && a.y > a.z) { tu = float3(1, 0, 0); tv = float3(0, 0, 1); }
                else if (a.x > a.z) { tu = float3(0, 0, 1); tv = float3(0, 1, 0); }
                else { tu = float3(1, 0, 0); tv = float3(0, 1, 0); }
                float2 p = float2(dot(i.positionOS, tu), dot(i.positionOS, tv));
                float2 size = kind == 0 ? _WallPanel.xy : _FloorPanel.xy;
                if (_Metal > 0.5) size *= float2(0.5, 1);

                // Panel cell, sometimes split in two
                float2 q = p / size;
                float2 cell = floor(q);
                float h = hash(cell);
                float2 split = h < 0.18 ? float2(1, 2) : (h < 0.28 ? float2(2, 1) : float2(1, 1));
                float2 fs = frac(q) * split;
                float2 id = cell * 3 + floor(fs);
                float2 f = frac(fs);
                float2 cs = size / split;                    // this panel's size (m)
                float h2 = hash(id + 7.1), h3 = hash(id + 3.7), h4 = hash(id + 11.3), h5 = hash(id + 5.9);

                // Distance to the panel's edge (m) and which way the edge is
                float dx = min(f.x, 1 - f.x) * cs.x, dy = min(f.y, 1 - f.y) * cs.y;
                float d = min(dx, dy);
                float2 toEdge = dx < dy ? float2(f.x < 0.5 ? -1 : 1, 0) : float2(0, f.y < 0.5 ? -1 : 1);
                float aa = max(fwidth(d), 1e-4);
                float fade = saturate(1.6 - aa * 25);            // seams widen with distance, fading only far off
                float gw = max(0.005, aa * 0.9);
                float groove = (1 - smoothstep(gw - aa, gw + aa, d)) * fade;
                float bevel = (1 - smoothstep(0.006, 0.022, d)) * (1 - groove) * fade;

                half3 albedo = kind == 0 ? _WallColor.rgb : (kind == 1 ? _FloorColor.rgb : _CeilColor.rgb);
                half smooth = kind == 1 ? 0.55 : 0.62;
                half metal = kind == 1 ? 0.2 : 0.0;
                if (_Metal > 0.5) { albedo = half3(0.3, 0.31, 0.33); smooth = 0.58; metal = 0.45; }
                // Team bands: every third row of wall panels is painted, the same height all over the map:
                // in a team's base its colour (team textures always), elsewhere none
                int team; float teamW;
                TeamAt(i.positionWS, team, teamW);
                half3 bandColour = _Accent > 0.5 ? _AccentColor.rgb : (team >= 0 ? _TeamPaint[team].rgb * 0.9 : albedo);
                half bandW = _Accent > 0.5 ? 1 : teamW;
                half3 seamGlow = _Accent > 0.5 ? _GlowColor.rgb : lerp(_GlowColor.rgb, team >= 0 ? _TeamGlow[team].rgb : _GlowColor.rgb, teamW);
                half seamStrength = _Accent > 0.5 ? _GlowAmount : lerp(_GlowAmount, 2.5, teamW);
                // Team textures band every third row; base walls every fifth, so tall facades aren't striped
                float period = _Accent > 0.5 ? 3 : 5;
                float row = fmod(fmod(cell.y, period) + period, period);
                if (kind == 0 && row < 0.5 && bandW > 0.01) { albedo = lerp(albedo, bandColour, bandW); smooth = 0.5; }
                else if (kind == 0 && h3 < 0.12) albedo *= 0.86;      // the odd panel a shade darker
                // Slight per-panel shade and soft wear
                albedo *= (0.97 + 0.05 * h5) * (0.97 + 0.04 * noise(p * 1.7));
                albedo *= _Tone;

                float2 bump = toEdge * bevel * 0.9;                // chamfer leans the normal towards the seam
                half occl = 1 - groove * 0.85 - bevel * 0.15;
                half3 glow = 0;
                float2 c = (f - 0.5) * cs;                        // position in the panel from its centre (m)

                if (kind == 0 && h2 < 0.05 && cs.x > 0.6 && cs.y > 0.5)
                {
                    // Vent grille: an inset rectangle of horizontal slats
                    float2 half_ = cs * 0.5 - 0.12;
                    float inside = step(abs(c.x), half_.x) * step(abs(c.y), half_.y);
                    float slat = frac(c.y / 0.035);
                    albedo = lerp(albedo, albedo * lerp(0.25, 0.6, step(0.45, slat)), inside);
                    bump += float2(0, (slat < 0.45 ? -0.6 : 0.4)) * inside * fade;
                    occl *= lerp(1, 0.6, inside);
                    smooth = lerp(smooth, 0.35, inside);
                }
                else if (kind == 1 && h2 < 0.2)
                {
                    // Grip plating: a grid of small raised studs
                    float2 g = frac(c / 0.05) - 0.5;
                    float r = length(g);
                    float stud = 1 - smoothstep(0.18, 0.28, r);
                    bump += g * stud * 2.0 * fade;
                    albedo *= 1 + 0.25 * stud;
                }
                else if (kind == 2 && h2 < _LightStrips)
                {
                    // Ceiling light strip down the panel's middle
                    float strip = (1 - smoothstep(0.05, 0.06, abs(c.y))) * step(abs(c.x), cs.x * 0.5 - 0.15);
                    glow += half3(2.4, 2.6, 2.8) * strip;
                    albedo = lerp(albedo, 0.9, strip);
                }
                // A continuous glowing seam along the top of each band row (team glow; faint and cool elsewhere)
                if (kind == 0)
                {
                    float fy = frac(q.y);
                    float toSeam = row < 0.5 ? (1 - fy) * size.y : (row < 1.5 ? fy * size.y : 1);
                    float lw = max(0.004, fwidth(q.y) * size.y * 0.8);      // at least most of a pixel, so it never breaks into dashes
                    float seamLine = 1 - smoothstep(lw, lw * 2, toSeam);
                    glow += seamGlow * seamLine * seamStrength * fade;
                }
                // Indicator lights: a little row of coloured dots near the corner of some wall panels
                if (kind == 0 && h5 < 0.07 && cs.x > 0.5)
                {
                    float2 corner = c - float2(cs.x * 0.5 - 0.09, -cs.y * 0.5 + 0.08);
                    float k = floor(corner.x / -0.035);
                    float2 dotP = float2(corner.x + (k + 0.5) * 0.035, corner.y);
                    float dot_ = (1 - smoothstep(0.006, 0.009, length(dotP))) * step(0, k) * step(k, 2.5);
                    float pick = hash(id + k * 1.7);
                    half3 dc = pick < 0.4 ? half3(3.0, 0.9, 0.15) : (pick < 0.7 ? half3(0.3, 3.0, 0.6) : half3(3.0, 0.25, 0.2));
                    float blink = step(0.25, frac(_Time.y * (0.3 + pick) + pick * 7));
                    glow += dc * dot_ * blink;
                }
                float3 nOS = normalize(n + (tu * bump.x + tv * bump.y));
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
                s.albedo = albedo;
                s.metallic = metal;
                s.smoothness = smooth * (1 - groove * 0.6);
                albedo *= 1 - groove * 0.7;                       // seams read as dark lines
                s.albedo = albedo;
                s.occlusion = occl;
                s.emission = glow;
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
