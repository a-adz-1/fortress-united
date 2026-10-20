using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The team colours, in one place. Blue and red are measured from the
    /// reference concept art (the red and blue MIRV grenades, 2026-10-20);
    /// teams 3 and 4 are gold and violet (chosen from a four-team mockup).
    /// Two per team:
    ///   glow  (lights, LEDs, effects): blue 17,167,247  red 241,71,66  gold 255,196,33  violet 204,61,255
    ///   paint (team paint on models):  blue 15,150,232  red 182,31,31  gold 214,160,20  violet 150,40,200
    /// No four bright colours stay distinct for red-green colour blindness,
    /// so players can switch to the colour-blind palette (Palette), and
    /// every team also has its own logo.
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

        public static readonly Color Gold = new Color(1f, 196 / 255f, 33 / 255f);
        public static readonly Color GoldCore = new Color(1f, 237 / 255f, 168 / 255f);
        public static readonly Color GoldPaint = new Color(214 / 255f, 160 / 255f, 20 / 255f);
        public static readonly Color Violet = new Color(204 / 255f, 61 / 255f, 1f);
        public static readonly Color VioletCore = new Color(237 / 255f, 191 / 255f, 1f);
        public static readonly Color VioletPaint = new Color(150 / 255f, 40 / 255f, 200 / 255f);

        /// <summary>TFC's team numbers.</summary>
        public enum Team { Blue = 1, Red = 2, Gold = 3, Violet = 4 }

        public enum PaletteKind { Normal, ColourBlind }

        /// <summary>
        /// The display palette, a per-player setting (saved). The colour-blind
        /// palette picks four colours that stay apart for red-green and
        /// blue-yellow colour blindness (tested by simulation): sky blue,
        /// vermillion, white and deep violet, in team order.
        /// </summary>
        public static PaletteKind Palette
        {
            get => (PaletteKind)PlayerPrefs.GetInt("FortressUnited.Palette", 0);
            set { PlayerPrefs.SetInt("FortressUnited.Palette", (int)value); PlayerPrefs.Save(); }
        }

        static readonly Color[] ColourBlind =
        {
            new Color(86 / 255f, 180 / 255f, 233 / 255f),   // sky blue
            new Color(213 / 255f, 94 / 255f, 0),            // vermillion
            new Color(235 / 255f, 235 / 255f, 235 / 255f),  // white
            new Color(110 / 255f, 50 / 255f, 170 / 255f),   // deep violet
        };

        /// <summary>A team's glow colour in the player's palette.</summary>
        public static Color For(Team team)
        {
            if (Palette == PaletteKind.ColourBlind)
                return ColourBlind[(int)team - 1];
            switch (team)
            {
                case Team.Red: return Red;
                case Team.Gold: return Gold;
                case Team.Violet: return Violet;
                default: return Blue;
            }
        }

        public static Color CoreFor(Team team)
        {
            if (Palette == PaletteKind.ColourBlind)
                return Color.Lerp(ColourBlind[(int)team - 1], Color.white, 0.6f);
            switch (team)
            {
                case Team.Red: return RedCore;
                case Team.Gold: return GoldCore;
                case Team.Violet: return VioletCore;
                default: return BlueCore;
            }
        }

        public static Color PaintFor(Team team)
        {
            switch (team)
            {
                case Team.Red: return RedPaint;
                case Team.Gold: return GoldPaint;
                case Team.Violet: return VioletPaint;
                default: return BluePaint;
            }
        }

        // Two-team shorthands (red or blue)
        public static Color For(bool red) => For(red ? Team.Red : Team.Blue);
        public static Color CoreFor(bool red) => CoreFor(red ? Team.Red : Team.Blue);
        public static Color PaintFor(bool red) => PaintFor(red ? Team.Red : Team.Blue);

        /// <summary>
        /// The team's logo: a white shape on transparent, from
        /// Resources/Teams/Logo_Red.png or Logo_Blue.png (rendered from
        /// Art/Logos/*.svg). Everything that shows a logo uses this, so
        /// replacing that one file changes it everywhere.
        /// </summary>
        public static Texture2D Logo(bool red) => Logo(red ? Team.Red : Team.Blue);

        /// <summary>A team's logo (Resources/Teams/Logo_&lt;Team&gt;.png); gold and violet logos are still to design.</summary>
        public static Texture2D Logo(Team team) => Resources.Load<Texture2D>("Teams/Logo_" + team);

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
