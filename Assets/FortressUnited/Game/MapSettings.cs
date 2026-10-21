using System.Globalization;
using FortressUnited.Core.Maps;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A map's look settings, from keys on its worldspawn (set in TrenchBroom
    /// on the map's World entity; see docs/mapping-guide.md). Every key is
    /// optional; a map without any gets the defaults.
    /// <list type="bullet">
    /// <item>fu_restyle: 1 restyles the map's textures (default), 0 keeps them as they are
    /// (lighting, atmosphere, screens and detail still apply)</item>
    /// <item>fu_time: day (default), dusk or night</item>
    /// <item>fu_sun: "pitch yaw" in degrees (overrides light_environment)</item>
    /// <item>fu_suncolor: "r g b" (0-255)</item>
    /// <item>fu_fog: fog density (0 for none; default 0.0025)</item>
    /// <item>fu_signs: neon (default) or hologram</item>
    /// <item>fu_sky: auto (default: the map's own sky if it's dark, i.e. space or night; else
    /// the modern day sky; a starfield becomes the modern space sky), map (always the map's own),
    /// procedural (always the modern day sky), space (the modern space sky)</item>
    /// <item>fu_ribs, fu_rails, fu_frames, fu_screens: 0 turns that automatic extra off</item>
    /// </list>
    /// </summary>
    public sealed class MapSettings
    {
        public bool Restyle = true;
        public string Time = "day";
        public Vector2? Sun;          // pitch (down, degrees), yaw
        public Color? SunColor;
        public float Fog = 0.0025f;
        public string Signs = "neon";
        public string Sky = "auto";
        public string SkyName = "";
        public bool Ribs = true, Rails = true, Frames = true, Screens = true;

        /// <summary>The settings of the map being played.</summary>
        public static MapSettings Current = new MapSettings();

        public static MapSettings From(BspFile bsp)
        {
            var s = new MapSettings();
            MapEntity world = null;
            foreach (var e in bsp.Entities)
                if (e.ClassName == "worldspawn") { world = e; break; }
            if (world == null)
                return s;
            s.Restyle = Flag(world, "fu_restyle", true);
            string time = world.Get("fu_time").ToLowerInvariant();
            if (time == "dusk" || time == "night" || time == "day") s.Time = time;
            var sun = Floats(world.Get("fu_sun"));
            if (sun.Length >= 2) s.Sun = new Vector2(sun[0], sun[1]);
            var sc = Floats(world.Get("fu_suncolor"));
            if (sc.Length >= 3) s.SunColor = new Color(sc[0] / 255f, sc[1] / 255f, sc[2] / 255f);
            var fog = Floats(world.Get("fu_fog"));
            if (fog.Length >= 1) s.Fog = Mathf.Max(0, fog[0]);
            string sky = world.Get("fu_sky").ToLowerInvariant();
            if (sky == "map" || sky == "procedural" || sky == "auto" || sky == "space") s.Sky = sky;
            s.SkyName = world.Get("skyname");
            string signs = world.Get("fu_signs").ToLowerInvariant();
            if (signs == "neon" || signs == "hologram") s.Signs = signs;
            s.Ribs = Flag(world, "fu_ribs", true);
            s.Rails = Flag(world, "fu_rails", true);
            s.Frames = Flag(world, "fu_frames", true);
            s.Screens = Flag(world, "fu_screens", true);
            return s;
        }

        static bool Flag(MapEntity e, string key, bool fallback)
        {
            string v = e.Get(key).Trim();
            return v == "" ? fallback : v != "0";
        }

        static float[] Floats(string s)
        {
            var parts = (s ?? "").Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<float>();
            foreach (var p in parts)
                if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    list.Add(f);
            return list.ToArray();
        }
    }
}
