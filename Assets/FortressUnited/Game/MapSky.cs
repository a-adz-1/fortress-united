using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A map's own skybox (worldspawn "skyname": six images in gfx/env, TGA or
    /// BMP, as GoldSrc reads them), loaded from the game folders at run time and
    /// drawn as Unity's six-sided sky. Space and night maps need theirs: a
    /// starfield can't be replaced by a daytime sky.
    /// </summary>
    public static class MapSky
    {
        // GoldSrc's sky sides, and the Unity six-sided sky slot each fills
        static readonly (string side, string slot)[] Sides =
        {
            ("ft", "_FrontTex"), ("bk", "_BackTex"), ("lf", "_LeftTex"), ("rt", "_RightTex"), ("up", "_UpTex"), ("dn", "_DownTex"),
        };

        /// <summary>The map's sky as a material and its average brightness (0-1), or null if its images aren't found.</summary>
        public static (Material sky, float brightness)? Load(string skyname, IEnumerable<string> gameDirs)
        {
            if (string.IsNullOrWhiteSpace(skyname))
                return null;
            var mat = new Material(Shader.Find("Skybox/6 Sided")) { name = "Map sky " + skyname };
            double lum = 0;
            int found = 0;
            foreach (var (side, slot) in Sides)
            {
                var tex = Find(skyname + side, gameDirs);
                if (tex == null)
                    return null;
                if (side == "up" || side == "dn")
                    tex = Rotate(tex, side == "up" ? 1 : -1);      // GoldSrc stores the top and bottom turned a quarter
                tex.wrapMode = TextureWrapMode.Clamp;
                mat.SetTexture(slot, tex);
                lum += Brightness(tex);
                found++;
            }
            // GoldSrc's front faces along +X (Unity +Z after the axis change) once turned a quarter
            mat.SetFloat("_Rotation", 90);
            return (mat, (float)(lum / found));
        }

        static Texture2D Find(string name, IEnumerable<string> gameDirs)
        {
            foreach (var dir in gameDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string env = Path.Combine(dir, "gfx", "env");
                if (!Directory.Exists(env)) continue;
                foreach (var f in Directory.GetFiles(env))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (!n.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    try
                    {
                        var t = ext == ".tga" ? ReadTga(File.ReadAllBytes(f)) : ext == ".bmp" ? ReadBmp(File.ReadAllBytes(f)) : null;
                        if (t != null) { t.name = n; return t; }
                    }
                    catch (Exception) { /* unreadable: try the next */ }
                }
            }
            return null;
        }

        static float Brightness(Texture2D t)
        {
            var px = t.GetPixels32(0);
            double s = 0;
            for (int i = 0; i < px.Length; i += 7)
                s += (px[i].r * 0.3 + px[i].g * 0.59 + px[i].b * 0.11) / 255.0;
            return (float)(s / ((px.Length + 6) / 7));
        }

        static Texture2D Rotate(Texture2D t, int quarter)
        {
            int w = t.width, h = t.height;
            var src = t.GetPixels32();
            var dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int nx = quarter > 0 ? h - 1 - y : y, ny = quarter > 0 ? x : w - 1 - x;
                    dst[ny * h + nx] = src[y * w + x];
                }
            var r = new Texture2D(h, w, TextureFormat.RGBA32, true) { name = t.name };
            r.SetPixels32(dst);
            r.Apply(true);
            return r;
        }

        // ------------------------------------------------------------ image readers

        /// <summary>TGA: true-colour (24/32-bit), plain or run-length encoded; colour-mapped 8-bit too.</summary>
        static Texture2D ReadTga(byte[] d)
        {
            int idLen = d[0], cmapType = d[1], type = d[2];
            int cmapStart = d[3] | d[4] << 8, cmapLen = d[5] | d[6] << 8, cmapBits = d[7];
            int w = d[12] | d[13] << 8, h = d[14] | d[15] << 8, bpp = d[16], desc = d[17];
            int p = 18 + idLen;
            Color32[] cmap = null;
            if (cmapType == 1)
            {
                int cb = cmapBits / 8;
                cmap = new Color32[cmapStart + cmapLen];
                for (int i = 0; i < cmapLen; i++, p += cb)
                    cmap[cmapStart + i] = new Color32(d[p + 2], d[p + 1], d[p], 255);
            }
            int bytes = bpp / 8;
            var px = new Color32[w * h];
            bool rle = type >= 9;
            int n = 0;
            Color32 Read(ref int q)
            {
                Color32 c;
                if (cmap != null) c = cmap[d[q]];
                else if (bytes >= 3) c = new Color32(d[q + 2], d[q + 1], d[q], bytes == 4 ? d[q + 3] : (byte)255);
                else c = new Color32(d[q], d[q], d[q], 255);
                q += bytes;
                return c;
            }
            while (n < w * h)
            {
                if (!rle) { px[n++] = Read(ref p); continue; }
                int head = d[p++];
                int count = (head & 0x7f) + 1;
                if ((head & 0x80) != 0)
                {
                    var c = Read(ref p);
                    for (int k = 0; k < count && n < w * h; k++) px[n++] = c;
                }
                else
                    for (int k = 0; k < count && n < w * h; k++) px[n++] = Read(ref p);
            }
            bool topDown = (desc & 0x20) != 0;
            return Make(px, w, h, flip: topDown);
        }

        /// <summary>BMP: 8-bit paletted or 24-bit, bottom-up as usual.</summary>
        static Texture2D ReadBmp(byte[] d)
        {
            int dataOff = BitConverter.ToInt32(d, 10);
            int headerSize = BitConverter.ToInt32(d, 14);
            int w = BitConverter.ToInt32(d, 18), h = BitConverter.ToInt32(d, 22);
            int bpp = BitConverter.ToInt16(d, 28);
            bool topDown = h < 0;
            h = Math.Abs(h);
            Color32[] pal = null;
            if (bpp == 8)
            {
                pal = new Color32[256];
                int po = 14 + headerSize;
                for (int i = 0; i < 256 && po + 3 < dataOff; i++, po += 4)
                    pal[i] = new Color32(d[po + 2], d[po + 1], d[po], 255);
            }
            int stride = ((w * bpp / 8) + 3) & ~3;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = dataOff + y * stride + x * bpp / 8;
                    px[y * w + x] = bpp == 8 ? pal[d[o]] : new Color32(d[o + 2], d[o + 1], d[o], 255);
                }
            return Make(px, w, h, flip: topDown);
        }

        /// <summary>A texture from rows stored bottom-up (Unity's order); flip for top-down data.</summary>
        static Texture2D Make(Color32[] px, int w, int h, bool flip)
        {
            if (flip)
            {
                var f = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    Array.Copy(px, y * w, f, (h - 1 - y) * w, w);
                px = f;
            }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }
    }
}
