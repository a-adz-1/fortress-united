using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The team colours, in one place. Blue is the cyan glow of the conc
    /// grenade concept (hue 189: RGB 24, 208, 239, with near-white cores);
    /// tools/meshy/recolour_blue.py shifts the blue painted into models to
    /// the same hue.
    /// </summary>
    public static class TeamColours
    {
        public static readonly Color Blue = new Color(24 / 255f, 208 / 255f, 239 / 255f);
        /// <summary>The hot centre of blue glows (screens, arc cores).</summary>
        public static readonly Color BlueCore = new Color(155 / 255f, 1f, 1f);
        public static readonly Color Red = new Color(1f, 0.3f, 0.25f);

        public static Color For(bool red) => red ? Red : Blue;
    }
}
