using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// Modern materials for old map textures, by texture name. The table
    /// (Resources/MapMaterials.csv, built by tools/materials/make_mapping.py)
    /// gives each texture a material from the library (CC0 PBR sets fetched by
    /// tools/materials/fetch.py into Resources/WorldMaterials) and a mode:
    /// <list type="bullet">
    /// <item>replace: the material instead, tinted to the old texture's colour so maps keep their palette</item>
    /// <item>detail: the old texture with the material's grain, relief and sheen over it, so
    /// stripes, panel lines and team markings stay</item>
    /// <item>keep: the old texture as it was</item>
    /// </list>
    /// Liquids ('!' textures) get the animated water shader. Without the
    /// library (not fetched yet) everything falls back to the old textures.
    /// </summary>
    public static class WorldMaterials
    {
        const float Inch = 0.0254f;     // one GoldSrc unit, and one texel at scale 1

        [Serializable] sealed class Entry { public string id; public float size; public float[] mean; public float smoothness; public float metallic; }
        [Serializable] sealed class Library { public Entry[] items; }

        static Dictionary<string, (string material, string mode)> table;
        static Dictionary<string, Entry> library;

        static void Init()
        {
            if (table != null)
                return;
            table = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            library = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            var csv = Resources.Load<TextAsset>("MapMaterials");
            if (csv != null)
                foreach (var line in csv.text.Split('\n'))
                {
                    var p = line.Trim().Split(',');
                    if (p.Length == 3 && p[0] != "texture")
                        table[p[0]] = (p[1], p[2]);
                }
            var lib = Resources.Load<TextAsset>("WorldMaterials/library");
            if (lib != null)
                foreach (var e in JsonUtility.FromJson<Library>(lib.text).items)
                    library[e.id] = e;
        }

        /// <summary>The table's entry for a texture; animated/random variants ("+0name", "-1name") fall back to the base name.</summary>
        static bool Lookup(string name, out string material, out string mode)
        {
            material = mode = null;
            if (table.TryGetValue(name, out var e) ||
                (name.Length > 2 && (name[0] == '+' || name[0] == '-') && table.TryGetValue(name.Substring(2), out e)))
            {
                (material, mode) = e;
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
            if (!Lookup(name, out var id, out var mode) || mode == "keep" || string.IsNullOrEmpty(id) ||
                !library.TryGetValue(id, out var entry) || original == null)
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
            mat.SetColor("_DeepColor", new Color(old.x, old.y, old.z) * 0.6f);
            mat.SetColor("_ShallowColor", new Color(old.x, old.y, old.z) * 1.6f + new Color(0.02f, 0.05f, 0.05f));
            if (name.IndexOf("lava", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mat.SetFloat("_Glow", 1);
                mat.SetFloat("_WaveSpeed", 0.08f);
                mat.SetFloat("_WaveScale", 2.5f);
            }
            else if (name.IndexOf("slime", StringComparison.OrdinalIgnoreCase) >= 0)
                mat.SetFloat("_Clarity", 0.1f);
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
