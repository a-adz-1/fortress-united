using System;
using System.Collections.Generic;
using System.Globalization;
using FortressUnited.Core.Maps;
using FortressUnited.Core.Movement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// Real lights from what the mapper placed: GoldSrc maps are mostly lit by
    /// their light textures (glowing fixture faces), so each fixture gets a
    /// point light in its own colour (nearby ones merged), as do glow sprites
    /// (env_glow) and any light entities. Also works out each team's base (from
    /// its spawn points), so surfaces and lights there take the team's colour.
    /// </summary>
    public static class MapLights
    {
        const float Inch = 0.0254f;
        const int MaxLights = 320;

        /// <summary>Each team's base centre (xyz) and whether it has one (w), for the shaders.</summary>
        public static readonly Vector4[] TeamCentres = new Vector4[4];

        public static void Build(MapLoader map, Light sun)
        {
            FindTeams(map.Bsp);
            UseMapSun(map.Bsp, sun);

            // Fixture faces, merged on a 2 m grid: summed area, area-weighted colour and position
            var cells = new Dictionary<Vector3Int, (float area, Vector3 pos, Vector3 colour, Vector3 normal)>();
            var colourOf = new Dictionary<int, Vector3?>();
            foreach (var f in map.Bsp.Faces)
            {
                if (f.TexInfo < 0 || f.TexInfo >= map.Bsp.TexInfos.Length)
                    continue;
                int tex = map.Bsp.TexInfos[f.TexInfo].Texture;
                if (tex < 0 || tex >= map.Bsp.Textures.Length || !IsLight(map.Bsp.Textures[tex].Name))
                    continue;
                if (!colourOf.TryGetValue(tex, out var col))
                    colourOf[tex] = col = LightColour(map.TextureData(tex));
                if (col == null)
                    continue;
                Vector3 centre = Vector3.zero;
                foreach (var p in f.Points)
                    centre += GoldSrcSpace.ToUnity(p);
                centre /= f.Points.Length;
                var plane = map.Bsp.Planes[f.Plane];
                Vector3 n = GoldSrcSpace.DirectionToUnity(f.BackSide ? -plane.Normal : plane.Normal);
                float area = Area(f.Points) * Inch * Inch;
                var key = Vector3Int.FloorToInt(centre / 2f);
                cells.TryGetValue(key, out var c);
                cells[key] = (c.area + area, c.pos + centre * area, c.colour + col.Value * area, c.normal + n * area);
            }

            var lights = new List<(Vector3 pos, Color colour, float range, float intensity)>();
            foreach (var c in cells.Values)
            {
                if (c.area <= 0) continue;
                // Out in the room, not hard against the fixture (point light falls off with the
                // square of distance, so a light at the fixture burns the wall beside it)
                Vector3 face = c.pos / c.area, n = c.normal.normalized;
                float room = Physics.Raycast(face + n * 0.05f, n, out var hit, 6f) ? hit.distance : 6f;
                Vector3 pos = face + n * Mathf.Clamp(room * 0.4f, 0.3f, 1.4f);
                var colour = (Vector3)(c.colour / c.area);
                float size = Mathf.Sqrt(c.area);
                lights.Add((pos, ToColour(colour), Mathf.Clamp(7 + size * 6, 7, 20), Mathf.Clamp(0.7f + size * 0.7f, 0.7f, 2.2f)));
            }
            // Glow sprites and light entities
            foreach (var e in map.Bsp.Entities)
            {
                string cls = e.ClassName;
                Vector3 at = GoldSrcSpace.ToUnity(ParseVec(e.Get("origin")));
                if (cls == "env_glow")
                {
                    var rc = ParseVec(e.Get("rendercolor"));
                    var colour = rc.X + rc.Y + rc.Z > 0 ? new Color(rc.X / 255f, rc.Y / 255f, rc.Z / 255f) : new Color(1f, 0.85f, 0.6f);
                    lights.Add((at, colour, 4, 1.4f));
                }
                else if (cls == "light" || cls == "light_spot")
                {
                    var l = ParseVec4(e.Get("_light"));
                    var colour = new Color(l.x / 255f, l.y / 255f, l.z / 255f);
                    float bright = l.w > 0 ? l.w : 200;
                    lights.Add((at, colour, Mathf.Clamp(bright * 0.05f, 4, 18), Mathf.Clamp(bright / 150f, 0.8f, 3f)));
                }
            }

            // Brightest first, so a capped list keeps the ones that matter
            lights.Sort((a, b) => (b.intensity * b.range).CompareTo(a.intensity * a.range));
            var root = new GameObject("Map Lights").transform;
            root.SetParent(map.transform, false);
            for (int i = 0; i < lights.Count && i < MaxLights; i++)
            {
                var (pos, colour, range, intensity) = lights[i];
                var l = new GameObject("Light").AddComponent<Light>();
                l.transform.SetParent(root, false);
                l.transform.position = pos;
                l.type = LightType.Point;
                l.color = TeamTint(pos, colour);
                l.range = range;
                l.intensity = intensity;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
            }
            Debug.Log($"Fortress United: {Math.Min(lights.Count, MaxLights)} map lights ({cells.Count} fixtures)");
        }

        static bool IsLight(string name) =>
            name.IndexOf('~') >= 0 || name.StartsWith("light", StringComparison.OrdinalIgnoreCase);

        /// <summary>A fixture's light: the mean of its brightest pixels (not its frame).</summary>
        static Vector3? LightColour(MipTexture mip)
        {
            if (mip == null || mip.Rgba == null)
                return null;
            var px = new List<(float lum, Vector3 c)>();
            for (int i = 0; i + 3 < mip.Rgba.Length; i += 4)
            {
                var c = new Vector3(mip.Rgba[i], mip.Rgba[i + 1], mip.Rgba[i + 2]) / 255f;
                px.Add((c.x * 0.3f + c.y * 0.59f + c.z * 0.11f, c));
            }
            px.Sort((a, b) => b.lum.CompareTo(a.lum));
            Vector3 sum = Vector3.zero;
            int n = Math.Max(1, px.Count / 4);
            for (int i = 0; i < n; i++)
                sum += px[i].c;
            return sum / n;
        }

        /// <summary>Lights keep their hue but are lifted to full brightness (a fixture's texels are rarely white).</summary>
        static Color ToColour(Vector3 c)
        {
            float m = Mathf.Max(c.x, Mathf.Max(c.y, c.z), 0.01f);
            return new Color(c.x / m, c.y / m, c.z / m);
        }

        static float Area(Vec3[] pts)
        {
            float a = 0;
            for (int k = 1; k + 1 < pts.Length; k++)
            {
                Vector3 p0 = new Vector3(pts[0].X, pts[0].Y, pts[0].Z);
                Vector3 p1 = new Vector3(pts[k].X, pts[k].Y, pts[k].Z);
                Vector3 p2 = new Vector3(pts[k + 1].X, pts[k + 1].Y, pts[k + 1].Z);
                a += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
            }
            return a;
        }

        // ------------------------------------------------------------ teams

        /// <summary>Each team's base: the middle of its spawn points. Shared with the shaders.</summary>
        static void FindTeams(BspFile bsp)
        {
            var sum = new Vector3[4];
            var count = new int[4];
            foreach (var e in bsp.Entities)
            {
                if (e.ClassName != "info_player_teamspawn" && e.ClassName != "i_p_t")
                    continue;
                if (!int.TryParse(e.Get("team_no"), out int t) || t < 1 || t > 4)
                    continue;
                sum[t - 1] += GoldSrcSpace.ToUnity(ParseVec(e.Get("origin")));
                count[t - 1]++;
            }
            for (int t = 0; t < 4; t++)
                TeamCentres[t] = count[t] > 0 ? (Vector4)(sum[t] / count[t]) + new Vector4(0, 0, 0, 1) : Vector4.zero;
            Shader.SetGlobalVectorArray("_TeamCentres", TeamCentres);
            var paint = new Vector4[4];
            var glow = new Vector4[4];
            for (int t = 0; t < 4; t++)
            {
                var team = (TeamColours.Team)(t + 1);
                paint[t] = TeamColours.PaintFor(team);
                glow[t] = TeamColours.Glow(TeamColours.For(team), 2.5f);
            }
            Shader.SetGlobalVectorArray("_TeamPaint", paint);
            Shader.SetGlobalVectorArray("_TeamGlow", glow);
        }

        /// <summary>
        /// Which team's base a point is in (0-3), and how surely (0 in the
        /// middle ground, 1 well inside). The same rule as the shaders use.
        /// </summary>
        public static (int team, float weight) TeamAt(Vector3 p)
        {
            int zone = MapEntities.ZoneTeam(p);          // a mapper's fu_teamzone wins
            if (zone >= 0)
                return (zone, 1);
            int best = -1, second = -1;
            float d1 = float.MaxValue, d2 = float.MaxValue;
            for (int t = 0; t < 4; t++)
            {
                if (TeamCentres[t].w < 0.5f) continue;
                float d = Vector3.Distance(p, TeamCentres[t]);
                if (d < d1) { d2 = d1; second = best; d1 = d; best = t; }
                else if (d < d2) { d2 = d; second = t; }
            }
            if (best < 0 || second < 0)
                return (-1, 0);
            return (best, Mathf.Clamp01(((d2 - d1) / Mathf.Max(d1 + d2, 1) - 0.1f) * 30));
        }

        /// <summary>Plain (white-ish) lights in a base lean towards its team's colour.</summary>
        static Color TeamTint(Vector3 pos, Color c)
        {
            var (team, w) = TeamAt(pos);
            if (team < 0)
                return c;
            Color.RGBToHSV(c, out _, out float sat, out _);
            var tc = TeamColours.For((TeamColours.Team)(team + 1));
            return Color.Lerp(c, tc, w * 0.55f * (1 - sat));
        }

        // ------------------------------------------------------------ sun

        /// <summary>The map's own sun angle (light_environment's pitch), in daylight with a hint of the map's colour.</summary>
        static void UseMapSun(BspFile bsp, Light sun)
        {
            if (sun == null)
                return;
            var settings = MapSettings.Current;
            if (settings.Sun.HasValue)
            {
                sun.transform.rotation = Quaternion.Euler(settings.Sun.Value.x, -settings.Sun.Value.y + 90, 0);
                if (settings.SunColor.HasValue) sun.color = settings.SunColor.Value;
                return;
            }
            foreach (var e in bsp.Entities)
            {
                if (e.ClassName != "light_environment")
                    continue;
                float pitch = float.TryParse(e.Get("pitch"), NumberStyles.Float, CultureInfo.InvariantCulture, out var pv) ? pv : -50;
                var angles = ParseVec(e.Get("angles"));
                float yaw = angles.Y;
                // GoldSrc pitch is negative looking down; kept between mid-morning and near noon
                float elevation = Mathf.Clamp(-pitch, 35, 65);
                sun.transform.rotation = Quaternion.Euler(elevation, -yaw + 90, 0);
                var l = ParseVec4(e.Get("_light"));
                if (l.x + l.y + l.z > 0)
                    sun.color = Color.Lerp(sun.color, ToColour(new Vector3(l.x, l.y, l.z)), 0.12f);   // a hint of the map's sun colour, not its full tint
                if (settings.SunColor.HasValue) sun.color = settings.SunColor.Value;
                return;
            }
        }

        // ------------------------------------------------------------ parsing

        static Vec3 ParseVec(string s)
        {
            var v = ParseVec4(s);
            return new Vec3(v.x, v.y, v.z);
        }

        static Vector4 ParseVec4(string s)
        {
            var p = (s ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var r = Vector4.zero;
            for (int i = 0; i < p.Length && i < 4; i++)
                if (float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    r[i] = f;
            return r;
        }
    }
}
