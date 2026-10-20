using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// The futuristic look for old maps, by texture name. The table
    /// (Resources/MapMaterials.csv, built by tools/materials/make_mapping.py)
    /// gives each texture a mode:
    /// <list type="bullet">
    /// <item>panel / metal: the procedural sci-fi surface (SciFiSurface.shader): white
    /// ceramic walls, graphite floor plating, lit ceilings; team textures get team
    /// bands and glowing seams; metal is smaller, darker panelling for doors and trims</item>
    /// <item>replace: a CC0 PBR material (tools/materials/fetch.py) tinted to the old
    /// texture's colour, for rock, ground and other natural surfaces</item>
    /// <item>light / sign: the old texture glowing; logo: our team logo glowing (TFC's
    /// team emblems); glass: see-through; keep: unchanged</item>
    /// </list>
    /// Liquids ('!' textures) get the animated water shader.
    /// </summary>
    public static class WorldMaterials
    {
        const float Inch = 0.0254f;     // one GoldSrc unit, and one texel at scale 1

        [Serializable] sealed class Entry { public string id; public float size; public float[] mean; public float smoothness; public float metallic; }
        [Serializable] sealed class Library { public Entry[] items; }

        static Dictionary<string, (string material, string mode, string team)> table;
        static Dictionary<string, Entry> library;

        static void Init()
        {
            if (table != null)
                return;
            table = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
            library = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            var csv = Resources.Load<TextAsset>("MapMaterials");
            if (csv != null)
                foreach (var line in csv.text.Split('\n'))
                {
                    var p = line.Trim().Split(',');
                    if (p.Length >= 3 && p[0] != "texture")
                        table[p[0]] = (p[1], p[2], p.Length > 3 ? p[3] : "");
                }
            var lib = Resources.Load<TextAsset>("WorldMaterials/library");
            if (lib != null)
                foreach (var e in JsonUtility.FromJson<Library>(lib.text).items)
                    library[e.id] = e;
        }

        /// <summary>The table's entry for a texture; animated/random variants ("+0name", "-1name") fall back to the base name.</summary>
        static bool Lookup(string name, out string material, out string mode, out string team)
        {
            material = mode = team = null;
            if (table.TryGetValue(name, out var e) ||
                (name.Length > 2 && (name[0] == '+' || name[0] == '-') && table.TryGetValue(name.Substring(2), out e)))
            {
                (material, mode, team) = e;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Upgrades a material built from an old texture. original is the old
        /// texture's pixels (sRGB bytes) for its mean colour; texWidth/Height its size.
        /// </summary>
        public static void Apply(Material mat, string name, Texture2D original, byte[] rgba, int texWidth, int texHeight)
        {
            Init();
            if (name.StartsWith("!"))
            {
                MakeWater(mat, name, rgba);
                return;
            }
            if (!Lookup(name, out var id, out var mode, out var team) || mode == "keep")
                return;
            switch (mode)
            {
                case "panel": MakePanels(mat, team, rgba, metal: false); return;
                case "metal": MakePanels(mat, team, rgba, metal: true); return;
                case "light": MakeLightPanel(mat, rgba); return;
                case "sign":
                    if (!MakeNeonSign(mat, name, rgba, texWidth, texHeight))
                        MakeHoloSign(mat, rgba, texWidth, texHeight);
                    return;
                case "glass": MakeGlass(mat, team); return;
                case "logo": MakeLogo(mat, team); return;
            }
            if (string.IsNullOrEmpty(id) || !library.TryGetValue(id, out var entry) || original == null)
                return;
            var color = Resources.Load<Texture2D>($"WorldMaterials/{id}_Color");
            var normal = Resources.Load<Texture2D>($"WorldMaterials/{id}_Normal");
            if (color == null)
                return;
            // One tile of the material covers its real size: the old texture spans width x height units
            var tiling = new Vector2(texWidth * Inch / entry.size, texHeight * Inch / entry.size);

            if (mode == "replace")
            {
                mat.SetTexture("_BaseMap", color);
                mat.mainTexture = color;
                mat.SetTextureScale("_BaseMap", tiling);
                mat.SetColor("_BaseColor", Tint(MeanLinear(rgba), entry.mean));
                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }
                var mask = Resources.Load<Texture2D>($"WorldMaterials/{id}_Mask");
                if (mask != null)
                {
                    mat.SetTexture("_MetallicGlossMap", mask);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetTexture("_OcclusionMap", mask);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                    mat.SetFloat("_Smoothness", 1);
                }
            }
            else
            {
                var detail = Resources.Load<Texture2D>($"WorldMaterials/{id}_Detail");
                if (detail != null)
                {
                    mat.SetTexture("_DetailAlbedoMap", detail);
                    mat.SetTextureScale("_DetailAlbedoMap", tiling);
                    mat.SetFloat("_DetailAlbedoMapScale", 1);
                }
                if (normal != null)
                {
                    mat.SetTexture("_DetailNormalMap", normal);
                    mat.SetFloat("_DetailNormalMapScale", 1);
                }
                mat.EnableKeyword("_DETAIL_MULX2");
                mat.SetFloat("_Smoothness", entry.smoothness * 0.8f);
                // Mostly non-metal: the map's own colours carry the look, and until maps have
                // baked reflection probes full metal only has the sky to reflect
                mat.SetFloat("_Metallic", entry.metallic * 0.3f);
            }
        }

        /// <summary>Per channel, how far the old texture's colour is from the material's, mostly applied.</summary>
        static Color Tint(Vector3 old, float[] mean)
        {
            if (mean == null || mean.Length < 3)
                return Color.white;
            var c = new Color(
                Mathf.Clamp(old.x / Mathf.Max(0.01f, mean[0]), 0.15f, 2f),
                Mathf.Clamp(old.y / Mathf.Max(0.01f, mean[1]), 0.15f, 2f),
                Mathf.Clamp(old.z / Mathf.Max(0.01f, mean[2]), 0.15f, 2f));
            return Color.Lerp(Color.white, c, 0.85f);
        }

        static Vector3 MeanLinear(byte[] rgba)
        {
            if (rgba == null || rgba.Length < 4)
                return new Vector3(0.3f, 0.3f, 0.3f);
            double r = 0, g = 0, b = 0;
            int n = 0;
            for (int i = 0; i + 3 < rgba.Length; i += 16)    // every 4th pixel is plenty
            {
                r += Mathf.GammaToLinearSpace(rgba[i] / 255f);
                g += Mathf.GammaToLinearSpace(rgba[i + 1] / 255f);
                b += Mathf.GammaToLinearSpace(rgba[i + 2] / 255f);
                n++;
            }
            return new Vector3((float)(r / n), (float)(g / n), (float)(b / n));
        }

        static Shader panels;

        static TeamColours.Team? TeamOf(string team) =>
            team == "blue" ? TeamColours.Team.Blue : team == "red" ? TeamColours.Team.Red :
            team == "gold" || team == "yellow" ? TeamColours.Team.Gold : team == "violet" || team == "green" ? TeamColours.Team.Violet :
            (TeamColours.Team?)null;

        /// <summary>
        /// The futuristic panelled surface (SciFiSurface.shader). Team surfaces get
        /// the team's paint on more panels and its glow in the seams; the old
        /// texture's brightness keeps dark areas dark.
        /// </summary>
        static void MakePanels(Material mat, string team, byte[] rgba, bool metal)
        {
            if (panels == null)
                panels = Shader.Find("FortressUnited/SciFiSurface");
            if (panels == null)
                return;
            mat.shader = panels;
            var old = MeanLinear(rgba);
            float lum = 0.2126f * old.x + 0.7152f * old.y + 0.0722f * old.z;
            mat.SetFloat("_Tone", Mathf.Clamp(Mathf.Sqrt(lum / 0.12f), 0.55f, 1.1f));
            mat.SetFloat("_Metal", metal ? 1 : 0);
            var t = TeamOf(team);
            if (t.HasValue)
            {
                mat.SetColor("_AccentColor", TeamColours.PaintFor(t.Value) * 0.9f);
                mat.SetFloat("_Accent", 1);
                mat.SetColor("_GlowColor", TeamColours.Glow(TeamColours.For(t.Value), 2.5f));
                mat.SetFloat("_GlowAmount", 2.5f);
            }
        }

        /// <summary>
        /// A light fixture as a modern LED panel: a thin dark metal bezel round a frosted
        /// diffuser that glows in the old fixture's colour (the mean of its brightest
        /// pixels), a touch brighter in the middle, with a fine bright rim. The loader
        /// stretches each fixture face's UVs to 0..1, so the bezel follows its edges.
        /// </summary>
        static void MakeLightPanel(Material mat, byte[] rgba)
        {
            // The fixture's light colour, lifted to full brightness
            Color light = new Color(1f, 0.95f, 0.88f);
            if (rgba != null && rgba.Length >= 16)
            {
                var lums = new List<(float l, int i)>();
                for (int i = 0; i + 3 < rgba.Length; i += 4)
                    lums.Add((rgba[i] * 0.3f + rgba[i + 1] * 0.59f + rgba[i + 2] * 0.11f, i));
                lums.Sort((a, b) => b.l.CompareTo(a.l));
                float r = 0, g = 0, b = 0;
                int n = Mathf.Max(1, lums.Count / 4);
                for (int k = 0; k < n; k++) { int i = lums[k].i; r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; }
                float m = Mathf.Max(r, Mathf.Max(g, b), 1);
                light = new Color(r / m, g / m, b / m);
                light = Color.Lerp(light, Color.white, 0.25f);          // LEDs read a little whiter than old bulbs
            }
            if (!lightPanels.TryGetValue(light, out var tex))
                lightPanels[light] = tex = LightPanelTexture(light);
            mat.SetTexture("_BaseMap", tex.albedo);
            mat.mainTexture = tex.albedo;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
            mat.SetFloat("_Smoothness", 0.7f);
            mat.SetFloat("_Metallic", 0.3f);
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", tex.emission);
            mat.SetColor("_EmissionColor", Color.white * 1.9f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        static readonly Dictionary<Color, (Texture2D albedo, Texture2D emission)> lightPanels = new Dictionary<Color, (Texture2D, Texture2D)>();

        static (Texture2D albedo, Texture2D emission) LightPanelTexture(Color light)
        {
            const int S = 256;
            const float bezel = 0.075f, soft = 0.012f, rim = 0.012f;
            var alb = new Color32[S * S];
            var emi = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                    float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));       // distance to the panel edge
                    float diffuser = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(bezel, bezel + soft, edge));
                    float rimLine = Mathf.Clamp01(1 - Mathf.Abs(edge - bezel - soft - rim * 0.5f) / (rim * 0.5f));
                    float centre = 1 - 0.18f * Mathf.Clamp01(new Vector2(u - 0.5f, v - 0.5f).magnitude * 2);
                    float glow = diffuser * (0.82f * centre + 0.18f * rimLine);
                    Color e = light * glow;
                    emi[y * S + x] = new Color32((byte)(255 * e.r), (byte)(255 * e.g), (byte)(255 * e.b), 255);
                    // Albedo: dark brushed bezel, pale frosted diffuser
                    Color a = Color.Lerp(new Color(0.09f, 0.095f, 0.11f), new Color(0.85f, 0.86f, 0.88f), diffuser);
                    alb[y * S + x] = new Color32((byte)(255 * a.r), (byte)(255 * a.g), (byte)(255 * a.b), 255);
                }
            Texture2D Make(Color32[] px, string name)
            {
                var t = new Texture2D(S, S, TextureFormat.RGBA32, true)
                {
                    name = name, filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp,
                };
                t.SetPixels32(px);
                t.Apply(true, true);
                return t;
            }
            return (Make(alb, "Light Panel"), Make(emi, "Light Panel Glow"));
        }

        /// <summary>Light fixtures and signs: the old texture lit from within.</summary>
        static void Glow(Material mat, Texture2D original, float strength)
        {
            if (original == null)
                return;
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", original);
            mat.SetColor("_EmissionColor", Color.white * strength);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetFloat("_Smoothness", 0.85f);
        }

        /// <summary>The look a texture gets ("panel", "logo", ...), or null if it isn't in the table.</summary>
        public static string ModeOf(string name)
        {
            Init();
            return Lookup(name, out _, out var mode, out _) ? mode : null;
        }

        /// <summary>Our team logo glowing on a dark glossy panel (the loader fits it to each face).</summary>
        static void MakeLogo(Material mat, string team)
        {
            var t = TeamOf(team) ?? TeamColours.Team.Blue;
            var logo = TeamColours.Logo(t);
            if (logo == null)
                return;
            // The logo's own artwork drawn on a dark glossy panel, lit just enough to read
            // as a screen: a strong glow blooms it into a blob
            mat.SetTexture("_BaseMap", logo);
            mat.mainTexture = logo;
            // The artwork is white: tint it in the team's colour
            var c = TeamColours.For(t);
            mat.SetColor("_BaseColor", c * 0.45f);
            mat.SetFloat("_Smoothness", 0.8f);
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", logo);
            mat.SetColor("_EmissionColor", c * 0.8f);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
        }

        /// <summary>
        /// How standard signs are drawn: "neon" (re-lettered as neon tubes; unknown
        /// signs fall back to hologram) or "hologram" (every sign as a holographic
        /// display traced from the old artwork; may suit some maps better).
        /// </summary>
        public static string SignStyle = "neon";

        /// <summary>A standard sign re-lettered in neon (NeonSigns) on dark gloss; false if it isn't one.</summary>
        static bool MakeNeonSign(Material mat, string name, byte[] rgba, int w, int h)
        {
            if (SignStyle != "neon")
                return false;
            var tex = NeonSigns.Draw(name, rgba, w, h);
            if (tex == null)
                return false;
            // Unlit: the sign is its own light (black backing, glowing tubes bright enough to bloom)
            mat.shader = Shader.Find("Universal Render Pipeline/Unlit");
            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            mat.SetColor("_BaseColor", Color.white * 2.4f);
            // Rendered images run bottom to top; map textures top to bottom
            mat.SetTextureScale("_BaseMap", new Vector2(1, -1));
            mat.SetTextureOffset("_BaseMap", new Vector2(0, 1));
            return true;
        }

        /// <summary>
        /// A sign redrawn as a holographic display: the old sign's text and symbols
        /// (everything that stands out from its background colour) glow on dark glass,
        /// with faint scan lines and an edge line. Team signs glow in their team's
        /// colour (from the text's own colour), yellow signs amber, the rest cyan. The
        /// symbol shapes are upscaled and sharpened so edges stay clean.
        /// </summary>
        static void MakeHoloSign(Material mat, byte[] rgba, int w, int h)
        {
            if (rgba == null || rgba.Length < w * h * 4)
                return;
            // Background: the most typical colour (per-channel median)
            int[] bg = new int[3];
            for (int ch = 0; ch < 3; ch++)
            {
                var hist = new int[256];
                for (int i = ch; i < w * h * 4; i += 4) hist[rgba[i]]++;
                int half = w * h / 2, sum = 0;
                for (int v = 0; v < 256; v++) { sum += hist[v]; if (sum >= half) { bg[ch] = v; break; } }
            }
            var mask = new float[w * h];
            double mr = 0, mg = 0, mb = 0, mn = 0;
            for (int k = 0; k < w * h; k++)
            {
                int i = k * 4;
                float d = Mathf.Max(Mathf.Abs(rgba[i] - bg[0]), Mathf.Max(Mathf.Abs(rgba[i + 1] - bg[1]), Mathf.Abs(rgba[i + 2] - bg[2]))) / 255f;
                mask[k] = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.15f, 0.35f, d));
                if (mask[k] > 0.5f) { mr += rgba[i]; mg += rgba[i + 1]; mb += rgba[i + 2]; mn++; }
            }
            // The glow colour, from the symbols' own colour
            Color glow = new Color(0.45f, 0.9f, 1f) * 1.6f;
            if (mn > 0)
            {
                float r = (float)(mr / mn) / 255f, g = (float)(mg / mn) / 255f, b = (float)(mb / mn) / 255f;
                if (b > r * 1.3f && b > g) glow = TeamColours.Glow(TeamColours.For(TeamColours.Team.Blue), 1.6f);
                else if (r > g * 1.4f && r > b * 1.4f) glow = TeamColours.Glow(TeamColours.For(TeamColours.Team.Red), 1.6f);
                else if (r > 0.55f && g > 0.45f && b < 0.35f) glow = new Color(1f, 0.7f, 0.2f) * 1.6f;
            }
            // Upscale the symbol mask, sharpening its edges
            int k2 = Mathf.Clamp(512 / Mathf.Max(w, h), 1, 4);
            int W = w * k2, H = h * k2;
            var px = new Color32[W * H];
            Color edgeCol = glow * 0.5f;
            for (int y = 0; y < H; y++)
            {
                float sy = (y + 0.5f) / k2 - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(sy), 0, h - 1), y1 = Mathf.Min(y0 + 1, h - 1);
                float fy = Mathf.Clamp01(sy - y0);
                float scan = 0.82f + 0.18f * ((y / 2) % 2);
                for (int x = 0; x < W; x++)
                {
                    float sx = (x + 0.5f) / k2 - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(sx), 0, w - 1), x1 = Mathf.Min(x0 + 1, w - 1);
                    float fx = Mathf.Clamp01(sx - x0);
                    float m = Mathf.Lerp(Mathf.Lerp(mask[y0 * w + x0], mask[y0 * w + x1], fx), Mathf.Lerp(mask[y1 * w + x0], mask[y1 * w + x1], fx), fy);
                    m = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.35f, 0.65f, m));
                    bool edge = x < 2 || y < 2 || x >= W - 2 || y >= H - 2;
                    Color c = glow * (m * scan) + glow * 0.05f;               // the glass glows faintly all over
                    if (edge) c = Color.Lerp(c, edgeCol, 0.8f);
                    // Stored as LDR; brightness comes from the material's emission strength
                    float top = Mathf.Max(c.r, Mathf.Max(c.g, c.b), 1f);
                    px[y * W + x] = new Color32((byte)(255 * c.r / top), (byte)(255 * c.g / top), (byte)(255 * c.b / top), 255);
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true)
            {
                name = "Holo Sign", filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Repeat,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            mat.SetTexture("_BaseMap", null);
            mat.SetColor("_BaseColor", new Color(0.015f, 0.02f, 0.03f));
            mat.SetFloat("_Smoothness", 0.92f);
            mat.SetFloat("_Metallic", 0);
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", tex);
            mat.SetColor("_EmissionColor", Color.white * 1.5f);
        }

        /// <summary>Windows: tinted, see-through and glossy.</summary>
        static void MakeGlass(Material mat, string team)
        {
            var t = TeamOf(team);
            var tint = t.HasValue ? TeamColours.PaintFor(t.Value) : new Color(0.55f, 0.75f, 0.85f);
            mat.SetTexture("_BaseMap", null);
            mat.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 0.25f));
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetFloat("_Smoothness", 0.95f);
        }

        static Shader water;

        /// <summary>Water, slime and lava: the animated water shader in the old texture's colour.</summary>
        static void MakeWater(Material mat, string name, byte[] rgba)
        {
            if (water == null)
                water = Shader.Find("FortressUnited/Water");
            if (water == null)
                return;
            var old = MeanLinear(rgba);
            mat.shader = water;
            // Clear bright water: a clean teal, leaning a little towards the old texture's colour
            var oldColour = new Color(old.x, old.y, old.z);
            mat.SetColor("_ShallowColor", Color.Lerp(new Color(0.12f, 0.5f, 0.55f), oldColour * 2f, 0.25f));
            mat.SetColor("_DeepColor", Color.Lerp(new Color(0.03f, 0.2f, 0.28f), oldColour, 0.2f));
            mat.SetFloat("_Clarity", 0.5f);
            if (name.IndexOf("lava", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mat.SetColor("_ShallowColor", oldColour * 2.5f);
                mat.SetColor("_DeepColor", oldColour);
                mat.SetFloat("_Glow", 1);
                mat.SetFloat("_WaveSpeed", 0.08f);
                mat.SetFloat("_WaveScale", 2.5f);
            }
            else if (name.IndexOf("slime", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mat.SetColor("_ShallowColor", oldColour * 1.8f);
                mat.SetColor("_DeepColor", oldColour * 0.7f);
                mat.SetFloat("_Clarity", 0.1f);
            }
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
