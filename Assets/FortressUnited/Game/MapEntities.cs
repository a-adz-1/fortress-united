using System;
using System.Collections.Generic;
using System.Globalization;
using FortressUnited.Core.Maps;
using FortressUnited.Core.Movement;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// Fortress United's mapper entities (FortressUnited.fgd; docs/mapping-guide.md):
    /// <list type="bullet">
    /// <item>fu_sign (point): a neon sign with the text typed into it, facing its angle</item>
    /// <item>fu_screen (point): a live match screen; placing any turns off automatic screens</item>
    /// <item>fu_teamzone (brush): this volume is a team's base (team bands, light tint, screens)</item>
    /// <item>fu_norail (brush): no automatic railings in here</item>
    /// <item>fu_nodetail (brush): no automatic railings, ribs or door frames in here</item>
    /// <item>fu_light (point): a light with real shadows, point or spot, with effects</item>
    /// <item>fu_prop (point): a model from the prop library (PropLibrary)</item>
    /// <item>fu_particles (point): a particle effect (MapParticles)</item>
    /// </list>
    /// The brush volumes aren't drawn and don't collide.
    /// </summary>
    public static class MapEntities
    {
        public const float Inch = 0.0254f;

        public static readonly List<Bounds> NoRail = new List<Bounds>();
        public static readonly List<Bounds> NoDetail = new List<Bounds>();
        /// <summary>Team volumes: bounds and team index (0 blue, 1 red, 2 gold, 3 violet).</summary>
        public static readonly List<(Bounds box, int team)> TeamZones = new List<(Bounds, int)>();

        public static readonly string[] VolumeClasses = { "fu_teamzone", "fu_norail", "fu_nodetail" };

        /// <summary>Reads the volumes (before lights, screens and detail are built).</summary>
        public static void ReadVolumes(MapLoader map)
        {
            NoRail.Clear(); NoDetail.Clear(); TeamZones.Clear();
            foreach (var b in map.World.Brushes)
            {
                if (Array.IndexOf(VolumeClasses, b.ClassName) < 0) continue;
                var m = map.Bsp.Models[b.Model];
                Vector3 a = GoldSrcSpace.ToUnity(m.Mins + b.Origin), c = GoldSrcSpace.ToUnity(m.Maxs + b.Origin);
                var box = new Bounds((a + c) * 0.5f, new Vector3(Mathf.Abs(a.x - c.x), Mathf.Abs(a.y - c.y), Mathf.Abs(a.z - c.z)));
                switch (b.ClassName)
                {
                    case "fu_norail": NoRail.Add(box); break;
                    case "fu_nodetail": NoDetail.Add(box); NoRail.Add(box); break;
                    case "fu_teamzone":
                        if (int.TryParse(b.Entity.Get("team_no"), out int t) && t >= 1 && t <= 4)
                            TeamZones.Add((box, t - 1));
                        break;
                }
            }
            // The surface shader reads team volumes too (up to 16)
            var mins = new Vector4[16];
            var maxs = new Vector4[16];
            for (int i = 0; i < TeamZones.Count && i < 16; i++)
            {
                mins[i] = TeamZones[i].box.min;
                maxs[i] = TeamZones[i].box.max;
                maxs[i].w = TeamZones[i].team + 1;      // 0 = unused
            }
            Shader.SetGlobalVectorArray("_TeamZoneMin", mins);
            Shader.SetGlobalVectorArray("_TeamZoneMax", maxs);
        }

        public static bool In(List<Bounds> volumes, Vector3 p)
        {
            foreach (var v in volumes)
                if (v.Contains(p)) return true;
            return false;
        }

        /// <summary>The team volume a point is in, or -1.</summary>
        public static int ZoneTeam(Vector3 p)
        {
            foreach (var (box, team) in TeamZones)
                if (box.Contains(p)) return team;
            return -1;
        }

        // ------------------------------------------------------------ signs and screens

        /// <summary>Builds the mapper's neon signs (fu_sign).</summary>
        public static int BuildSigns(MapLoader map)
        {
            int n = 0;
            foreach (var e in map.Bsp.Entities)
            {
                if (e.ClassName != "fu_sign") continue;
                string text = e.Get("text");
                if (string.IsNullOrWhiteSpace(text)) continue;
                Vector3 at = GoldSrcSpace.ToUnity(Vec(e.Get("origin")));
                Vector3 facing = Facing(e);
                float height = Num(e.Get("height"), 32) * Inch;
                Color colour = SignColour(e, at);
                // Size the panel to the wording: about 0.62 of the height per letter on the longest line
                string[] lines = text.Replace("\\n", "\n").Split('\n');
                int longest = 1;
                foreach (var l in lines) longest = Math.Max(longest, l.Length);
                float width = Mathf.Max(height, height * 0.62f * longest / Mathf.Max(1, lines.Length) + height * 0.3f);
                int H = 256, W = Mathf.Clamp(Mathf.RoundToInt(H * width / height), 128, 2048);
                var tex = NeonSigns.DrawText(text, colour, W, H);

                var root = new GameObject("fu_sign " + text.Replace("\n", " ")).transform;
                root.SetParent(map.transform, false);
                root.position = at;
                root.rotation = Quaternion.LookRotation(facing, Vector3.up);
                if (Num(e.Get("backing"), 1) > 0)
                {
                    var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    UnityEngine.Object.DestroyImmediate(back.GetComponent<Collider>());
                    back.transform.SetParent(root, false);
                    back.transform.localScale = new Vector3(width + 0.06f, height + 0.06f, 0.03f);
                    back.transform.localPosition = new Vector3(0, 0, -0.016f);
                    var bm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    bm.SetColor("_BaseColor", new Color(0.02f, 0.022f, 0.028f));
                    bm.SetFloat("_Smoothness", 0.9f);
                    back.GetComponent<Renderer>().sharedMaterial = bm;
                }
                var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.DestroyImmediate(face.GetComponent<Collider>());
                face.transform.SetParent(root, false);
                face.transform.localScale = new Vector3(width, height, 1);
                face.transform.localPosition = new Vector3(0, 0, 0.002f);
                face.transform.localRotation = Quaternion.Euler(0, 180, 0);   // quads face -Z; turn it to face out
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", Color.white * 2.4f);
                // Additive-ish: black is see-through when there's no backing
                if (Num(e.Get("backing"), 1) <= 0)
                {
                    m.SetFloat("_Surface", 1);
                    m.SetFloat("_Blend", 2);           // additive
                    m.SetFloat("_ZWrite", 0);
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                var r = face.GetComponent<Renderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // The sign lights the wall round it a little
                var glow = new GameObject("Glow").AddComponent<Light>();
                glow.transform.SetParent(root, false);
                glow.transform.localPosition = new Vector3(0, 0, 0.9f);
                glow.type = LightType.Point;
                glow.color = colour;
                glow.range = Mathf.Max(2.5f, width * 1.5f);
                glow.intensity = 0.35f;
                glow.shadows = LightShadows.None;
                n++;
            }
            return n;
        }

        /// <summary>
        /// The mapper's lights (fu_light): point or spot, with real soft shadows and an
        /// optional effect. Keys follow Hammer's light entities: _light "r g b brightness",
        /// _cone / _cone2 (spot inner / outer angle), angles (spot direction), plus range,
        /// shadows and effect.
        /// </summary>
        public static int BuildLights(MapLoader map)
        {
            int n = 0;
            foreach (var e in map.Bsp.Entities)
            {
                if (e.ClassName != "fu_light") continue;
                var l = new GameObject("fu_light").AddComponent<Light>();
                l.transform.SetParent(map.transform, false);
                l.transform.position = GoldSrcSpace.ToUnity(Vec(e.Get("origin")));
                var c = Vec4(e.Get("_light"));
                l.color = c.x + c.y + c.z > 0 ? new Color(c.x / 255f, c.y / 255f, c.z / 255f) : Color.white;
                l.intensity = (c.w > 0 ? c.w : 200) / 100f;
                l.range = Num(e.Get("range"), 400) * Inch;
                bool spot = e.Get("type").Equals("spot", StringComparison.OrdinalIgnoreCase) || e.Get("_cone") != "";
                if (spot)
                {
                    l.type = LightType.Spot;
                    float outer = Num(e.Get("_cone2"), Num(e.Get("_cone"), 30) + 15);
                    l.spotAngle = Mathf.Clamp(outer * 2, 1, 179);
                    l.innerSpotAngle = Mathf.Clamp(Num(e.Get("_cone"), 30) * 2, 0, l.spotAngle);
                    l.transform.rotation = Quaternion.LookRotation(Aim(e), Vector3.up);
                }
                else
                    l.type = LightType.Point;
                l.shadows = Num(e.Get("shadows"), 1) > 0 ? LightShadows.Soft : LightShadows.None;
                l.shadowBias = 0.02f;
                l.shadowNormalBias = 0.3f;
                l.renderMode = LightRenderMode.ForcePixel;
                string effect = e.Get("effect").ToLowerInvariant();
                if (effect != "" && effect != "none")
                {
                    var fx = l.gameObject.AddComponent<LightEffect>();
                    fx.Effect = effect;
                    fx.Speed = Num(e.Get("speed"), 1);
                }
                n++;
            }
            return n;
        }

        /// <summary>
        /// The mapper's props (fu_prop): a model from the prop library by name, standing on
        /// the entity's origin and facing its yaw; scale; glow accent in the team colour
        /// (team_no, or the base it's in) or rendercolor. Props don't collide.
        /// </summary>
        public static int BuildProps(MapLoader map)
        {
            int n = 0;
            foreach (var e in map.Bsp.Entities)
            {
                if (e.ClassName != "fu_prop") continue;
                Vector3 at = GoldSrcSpace.ToUnity(Vec(e.Get("origin")));
                var prop = PropLibrary.Make(e.Get("model"), AccentColour(e, at));
                if (prop == null)
                {
                    Debug.LogWarning($"Fortress United: fu_prop model '{e.Get("model")}' isn't in the prop library");
                    continue;
                }
                prop.name = "fu_prop " + e.Get("model");
                prop.transform.SetParent(map.transform, false);
                prop.transform.position = at;
                prop.transform.rotation = Quaternion.LookRotation(Facing(e), Vector3.up);
                prop.transform.localScale = Vector3.one * Num(e.Get("scale"), 1);
                n++;
            }
            return n;
        }

        /// <summary>The mapper's particle effects (fu_particles).</summary>
        public static int BuildParticles(MapLoader map)
        {
            int n = 0;
            foreach (var e in map.Bsp.Entities)
            {
                if (e.ClassName != "fu_particles") continue;
                Vector3 at = GoldSrcSpace.ToUnity(Vec(e.Get("origin")));
                bool aimed = e.Get("angles") != "" || e.Get("pitch") != "";
                var rc = Vec(e.Get("rendercolor"));
                Color? colour = rc.X + rc.Y + rc.Z > 0 ? new Color(rc.X / 255f, rc.Y / 255f, rc.Z / 255f, 1) : (Color?)null;
                var fx = MapParticles.Make(e.Get("effect", "steam"), aimed ? Aim(e) : Vector3.up, colour,
                                           Num(e.Get("rate"), 1), Num(e.Get("size"), 1), Num(e.Get("radius"), 4) * Inch);
                if (fx == null)
                {
                    Debug.LogWarning($"Fortress United: fu_particles effect '{e.Get("effect")}' isn't known");
                    continue;
                }
                fx.transform.SetParent(map.transform, false);
                fx.transform.position = at;
                n++;
            }
            return n;
        }

        static Color AccentColour(MapEntity e, Vector3 at)
        {
            var rc = Vec(e.Get("rendercolor"));
            if (rc.X + rc.Y + rc.Z > 0)
                return new Color(rc.X / 255f, rc.Y / 255f, rc.Z / 255f);
            if (int.TryParse(e.Get("team_no"), out int t) && t >= 1 && t <= 4)
                return TeamColours.For((TeamColours.Team)t);
            var (team, weight) = MapLights.TeamAt(at);
            return team >= 0 && weight > 0.5f ? TeamColours.For((TeamColours.Team)(team + 1)) : new Color(0.55f, 0.95f, 1f);
        }

        /// <summary>
        /// Where a point entity aims, as GoldSrc lights read it: angles "pitch yaw roll"
        /// (or a separate "pitch" key), with pitch -90 straight down and 90 straight up.
        /// </summary>
        public static Vector3 Aim(MapEntity e)
        {
            var a = Vec(e.Get("angles"));
            float pitch = a.X, yaw = a.Y;
            string p1 = e.Get("pitch");
            if (p1 != "") pitch = Num(p1, pitch);
            string y1 = e.Get("angle");
            if (y1 != "") yaw = Num(y1, yaw);
            float pr = pitch * Mathf.Deg2Rad, yr = yaw * Mathf.Deg2Rad;
            var d = new Vec3(Mathf.Cos(pr) * Mathf.Cos(yr), Mathf.Cos(pr) * Mathf.Sin(yr), Mathf.Sin(pr));
            return GoldSrcSpace.DirectionToUnity(d).normalized;
        }

        static Vector4 Vec4(string s)
        {
            var p = (s ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var r = Vector4.zero;
            for (int i = 0; i < p.Length && i < 4; i++)
                if (float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) r[i] = f;
            return r;
        }

        /// <summary>The mapper's screen positions (fu_screen): position, facing, team, width (m).</summary>
        public static List<(Vector3 pos, Vector3 facing, int team, float width)> Screens(MapLoader map)
        {
            var list = new List<(Vector3, Vector3, int, float)>();
            foreach (var e in map.Bsp.Entities)
            {
                if (e.ClassName != "fu_screen") continue;
                Vector3 at = GoldSrcSpace.ToUnity(Vec(e.Get("origin")));
                int team = int.TryParse(e.Get("team_no"), out int t) && t >= 1 && t <= 4 ? t - 1 : Math.Max(0, MapLights.TeamAt(at).team);
                list.Add((at, Facing(e), team, Num(e.Get("width"), 64) * Inch));
            }
            return list;
        }

        static Color SignColour(MapEntity e, Vector3 at)
        {
            var rc = Vec(e.Get("rendercolor"));
            if (rc.X + rc.Y + rc.Z > 0)
                return new Color(rc.X / 255f, rc.Y / 255f, rc.Z / 255f);
            if (int.TryParse(e.Get("team_no"), out int t) && t >= 1 && t <= 4)
                return TeamColours.For((TeamColours.Team)t);
            return new Color(0.55f, 0.95f, 1f);
        }

        /// <summary>Which way a point entity faces (its yaw, as GoldSrc stores it in angles or angle).</summary>
        public static Vector3 Facing(MapEntity e)
        {
            var a = Vec(e.Get("angles"));
            float yaw = a.Y;
            string single = e.Get("angle");
            if (single != "" && float.TryParse(single, NumberStyles.Float, CultureInfo.InvariantCulture, out float y1))
                yaw = y1;
            float r = yaw * Mathf.Deg2Rad;
            return GoldSrcSpace.DirectionToUnity(new Vec3(Mathf.Cos(r), Mathf.Sin(r), 0)).normalized;
        }

        public static Vec3 Vec(string s)
        {
            var p = (s ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            float x = 0, y = 0, z = 0;
            if (p.Length > 0) float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            if (p.Length > 1) float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
            if (p.Length > 2) float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            return new Vec3(x, y, z);
        }

        public static float Num(string s, float fallback) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : fallback;
    }
}
