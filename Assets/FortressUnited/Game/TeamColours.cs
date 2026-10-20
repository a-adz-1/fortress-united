using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The team colours, in one place, measured from the reference concept
    /// art (the red and blue conc grenades):
    ///   blue glow RGB 24, 208, 239 (hue 189), hot core 155, 255, 255
    ///   red glow  RGB 225, 37, 38  (hue 0),   hot core 255, 156, 148
    /// tools/meshy/recolour_team.py shifts paint in model textures to match.
    /// Brighten with Glow(), not by multiplying: multiplying past 1 clips the
    /// strong channel first and shifts the hue (red turns salmon).
    /// </summary>
    public static class TeamColours
    {
        public static readonly Color Blue = new Color(24 / 255f, 208 / 255f, 239 / 255f);
        /// <summary>The hot centre of blue glows (screens, arc cores).</summary>
        public static readonly Color BlueCore = new Color(155 / 255f, 1f, 1f);
        public static readonly Color Red = new Color(225 / 255f, 37 / 255f, 38 / 255f);
        /// <summary>The hot centre of red glows.</summary>
        public static readonly Color RedCore = new Color(1f, 156 / 255f, 148 / 255f);

        public static Color For(bool red) => red ? Red : Blue;
        public static Color CoreFor(bool red) => red ? RedCore : BlueCore;

        /// <summary>
        /// The team's logo: a white shape on transparent, from
        /// Resources/Teams/Logo_Red.png or Logo_Blue.png (rendered from
        /// Art/Logos/*.svg). Everything that shows a logo uses this, so
        /// replacing that one file changes it everywhere.
        /// </summary>
        public static Texture2D Logo(bool red) => Resources.Load<Texture2D>(red ? "Teams/Logo_Red" : "Teams/Logo_Blue");

        static Texture2D tintedRed, tintedBlue;

        /// <summary>The logo coloured in the team colour (for decals, which can't tint).</summary>
        public static Texture2D TintedLogo(bool red)
        {
            var cached = red ? tintedRed : tintedBlue;
            if (cached != null)
                return cached;
            var src = Logo(red);
            if (src == null)
                return null;
            var px = src.GetPixels32();
            var c = (Color32)For(red);
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(c.r, c.g, c.b, px[i].a);
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true) { name = src.name + "_Tinted", wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(true);
            if (red) tintedRed = t; else tintedBlue = t;
            return t;
        }

        /// <summary>
        /// An HDR glow of the colour whose brightest channel is the given
        /// intensity, keeping the hue (bloom makes values over 1 glow).
        /// </summary>
        public static Color Glow(Color c, float intensity)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max <= 0) return Color.black;
            float k = intensity / max;
            return new Color(c.r * k, c.g * k, c.b * k, 1);
        }
    }
}
