using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The team colours, in one place, measured from the reference concept
    /// art: the red and blue MIRV grenades (2026-10-20). Two per team:
    ///   glow  (lights, LEDs, effects): blue 17, 167, 247 (hue 200), red 241, 71, 66 (hue 1)
    ///   paint (team paint on models):  blue 15, 150, 232 (hue 202), red 182, 31, 31 (hue 0)
    /// Models with painted team markings use the paint colour (tools/meshy/
    /// recolour_team.py shifts their textures to it); anything that glows uses
    /// the glow colour. Brighten glows with Glow(), not by multiplying:
    /// multiplying past 1 clips a channel and shifts the hue (red turns salmon).
    /// </summary>
    public static class TeamColours
    {
        public static readonly Color Blue = new Color(17 / 255f, 167 / 255f, 247 / 255f);
        /// <summary>The hot centre of blue glows (screens, arc cores).</summary>
        public static readonly Color BlueCore = new Color(160 / 255f, 222 / 255f, 1f);
        public static readonly Color Red = new Color(241 / 255f, 71 / 255f, 66 / 255f);
        /// <summary>The hot centre of red glows.</summary>
        public static readonly Color RedCore = new Color(1f, 170 / 255f, 160 / 255f);

        /// <summary>Team paint on models (hazard stripes, panels).</summary>
        public static readonly Color BluePaint = new Color(15 / 255f, 150 / 255f, 232 / 255f);
        public static readonly Color RedPaint = new Color(182 / 255f, 31 / 255f, 31 / 255f);

        public static Color For(bool red) => red ? Red : Blue;
        public static Color CoreFor(bool red) => red ? RedCore : BlueCore;
        public static Color PaintFor(bool red) => red ? RedPaint : BluePaint;

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
