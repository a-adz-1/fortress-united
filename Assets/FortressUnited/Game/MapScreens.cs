using System.Collections.Generic;
using FortressUnited.Core.Maps;
using UnityEngine;
using UnityEngine.UI;

namespace FortressUnited.Game
{
    /// <summary>
    /// Wall screens in each team's base showing the match live (MatchInfo):
    /// the team's logo and base name, the clock, the score, both flags' state
    /// and a scrolling news ticker. One feed is drawn per team (a UI rig far
    /// from the map with its own camera) and shared by all that team's screens.
    /// Screens are placed automatically on big, flat, indoor walls with open
    /// floor in front, well inside the base, at eye height, spaced apart.
    /// </summary>
    public sealed class MapScreens : MonoBehaviour
    {
        const float Inch = 0.0254f;
        public static readonly List<Transform> Placed = new List<Transform>();

        public const float ScreenWidth = 1.6f, ScreenHeight = 0.9f;
        const int PerTeam = 4;
        const float Spacing = 7;

        sealed class Feed
        {
            public int Team;
            public RenderTexture Texture;
            public Text Clock, Ticker;
            public Text[] Scores, FlagRows;
        }

        readonly List<Feed> feeds = new List<Feed>();

        public static MapScreens Place(MapLoader map)
        {
            Placed.Clear();
            var host = new GameObject("Wall Screens").AddComponent<MapScreens>();
            host.transform.SetParent(map.transform, false);
            for (int t = 0; t < 4; t++)
            {
                if (MapLights.TeamCentres[t].w < 0.5f)
                    continue;
                var spots = host.FindSpots(map, t);
                if (spots.Count == 0)
                    continue;
                var feed = host.MakeFeed(t);
                foreach (var (pos, normal) in spots)
                    host.MakeScreen(feed, pos, normal);
            }
            Debug.Log($"Fortress United: {Placed.Count} wall screens");
            return host;
        }

        // ------------------------------------------------------------ placing

        List<(Vector3 pos, Vector3 normal)> FindSpots(MapLoader map, int team)
        {
            var bsp = map.Bsp;
            Vector3 centre = MapLights.TeamCentres[team];
            var candidates = new List<(float score, Vector3 pos, Vector3 normal)>();
            var model = bsp.Models[0];
            for (int i = 0; i < model.NumFaces; i++)
            {
                var f = bsp.Faces[model.FirstFace + i];
                if (f.TexInfo < 0 || f.TexInfo >= bsp.TexInfos.Length) continue;
                int tex = bsp.TexInfos[f.TexInfo].Texture;
                if (tex < 0 || tex >= bsp.Textures.Length) continue;
                string name = bsp.Textures[tex].Name.ToLowerInvariant();
                if (name.StartsWith("sky") || name.StartsWith("{") || name.StartsWith("!") || name.Contains("~") ||
                    name.StartsWith("sign") || name.StartsWith("team") || name == "aaatrigger" || name == "clip")
                    continue;
                var plane = bsp.Planes[f.Plane];
                Vector3 n = GoldSrcSpace.DirectionToUnity(f.BackSide ? -plane.Normal : plane.Normal);
                if (Mathf.Abs(n.y) > 0.05f) continue;                       // walls only
                Vector3 across = Vector3.Cross(Vector3.up, n).normalized;
                float minH = float.MaxValue, maxH = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                Vector3 any = GoldSrcSpace.ToUnity(f.Points[0]);
                foreach (var p in f.Points)
                {
                    var u = GoldSrcSpace.ToUnity(p);
                    float h = Vector3.Dot(u, across);
                    minH = Mathf.Min(minH, h); maxH = Mathf.Max(maxH, h);
                    minY = Mathf.Min(minY, u.y); maxY = Mathf.Max(maxY, u.y);
                }
                if (maxH - minH < ScreenWidth + 0.6f || maxY - minY < 2.2f) continue;
                // A point on the wall's plane, centred across it
                float planeD = Vector3.Dot(any, n);
                Vector3 mid = across * ((minH + maxH) * 0.5f) + n * planeD;
                mid.y = minY + 1.0f;
                if (!Physics.Raycast(mid + n * 0.3f + Vector3.up * 0.5f, Vector3.down, out var floor, 3f))
                    continue;
                Vector3 pos = new Vector3(mid.x, floor.point.y + 1.75f, mid.z);
                if (pos.y + ScreenHeight * 0.5f + 0.1f > maxY || pos.y - ScreenHeight * 0.5f < minY) continue;
                var (who, weight) = MapLights.TeamAt(pos);
                if (who != team || weight < 0.6f) continue;
                float dist = Vector3.Distance(pos, centre);
                if (dist > 35) continue;
                if (!Physics.Raycast(pos + n * 0.3f, Vector3.up, 14f)) continue;           // indoors
                if (Physics.Raycast(pos + n * 0.05f, n, 3f)) continue;                      // open space in front
                if (!Flat(pos, n, across)) continue;
                candidates.Add((dist, pos, n));
            }
            candidates.Sort((a, b) => a.score.CompareTo(b.score));
            var chosen = new List<(Vector3, Vector3)>();
            foreach (var c in candidates)
            {
                bool clear = true;
                foreach (var p in Placed)
                    if (Vector3.Distance(p.position, c.pos) < Spacing) { clear = false; break; }
                foreach (var (p, _) in chosen)
                    if (Vector3.Distance(p, c.pos) < Spacing) { clear = false; break; }
                if (!clear) continue;
                chosen.Add((c.pos, c.normal));
                if (chosen.Count >= PerTeam) break;
            }
            return chosen;
        }

        /// <summary>The wall is flat behind the whole screen (nothing sticking out, no doorway).</summary>
        static bool Flat(Vector3 pos, Vector3 n, Vector3 across)
        {
            for (int k = 0; k < 9; k++)
            {
                float x = (k % 3 - 1) * (ScreenWidth * 0.5f + 0.05f), y = (k / 3 - 1) * (ScreenHeight * 0.5f + 0.05f);
                Vector3 from = pos + across * x + Vector3.up * y + n * 0.3f;
                if (!Physics.Raycast(from, -n, out var hit, 0.5f) || Mathf.Abs(hit.distance - 0.3f) > 0.03f)
                    return false;
            }
            return true;
        }

        void MakeScreen(Feed feed, Vector3 pos, Vector3 normal)
        {
            var root = new GameObject("Screen").transform;
            root.SetParent(transform, false);
            root.position = pos + normal * 0.01f;
            root.rotation = Quaternion.LookRotation(normal, Vector3.up);   // forward out of the wall

            var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(bezel.GetComponent<Collider>());     // now, not at the end of the frame: map raycasts run straight after
            bezel.transform.SetParent(root, false);
            bezel.transform.localScale = new Vector3(ScreenWidth + 0.1f, ScreenHeight + 0.1f, 0.06f);
            bezel.transform.localPosition = new Vector3(0, 0, 0.03f);
            var frame = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            frame.SetColor("_BaseColor", new Color(0.05f, 0.055f, 0.065f));
            frame.SetFloat("_Smoothness", 0.7f);
            frame.SetFloat("_Metallic", 0.6f);
            bezel.GetComponent<Renderer>().sharedMaterial = frame;

            var glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(glass.GetComponent<Collider>());
            glass.transform.SetParent(root, false);
            glass.transform.localScale = new Vector3(ScreenWidth, ScreenHeight, 1);
            glass.transform.localPosition = new Vector3(0, 0, 0.062f);
            glass.transform.localRotation = Quaternion.Euler(0, 180, 0);    // quads face -Z; turn it to face out
            var screen = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { mainTexture = feed.Texture };
            screen.SetTexture("_BaseMap", feed.Texture);
            screen.SetColor("_BaseColor", Color.white * 1.6f);              // bright enough to bloom a little
            glass.GetComponent<Renderer>().sharedMaterial = screen;
            glass.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The screen's glow on the wall and floor around it
            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = new Vector3(0, -0.1f, 0.6f);
            light.type = LightType.Point;
            light.color = Color.Lerp(Color.white, TeamColours.For((TeamColours.Team)(feed.Team + 1)), 0.6f);
            light.range = 3f;
            light.intensity = 0.9f;
            light.shadows = LightShadows.None;
            Placed.Add(root);
        }

        // ------------------------------------------------------------ the feed

        const int W = 1024, H = 576;
        static int rigs;

        Feed MakeFeed(int team)
        {
            var t = (TeamColours.Team)(team + 1);
            // Mipmapped and filtered, so text and lines stay smooth when the screen is seen small or at an angle
            var feed = new Feed { Team = team, Texture = new RenderTexture(W, H, 16, RenderTextureFormat.ARGB32)
            {
                name = "Screen " + t, useMipMap = true, autoGenerateMips = true,
                filterMode = FilterMode.Trilinear, anisoLevel = 8,
            } };
            var rig = new GameObject("Screen Feed " + t);
            rig.transform.SetParent(transform, false);
            rig.transform.position = new Vector3(0, -30000 - 3000 * rigs++, 0);   // far enough apart that no feed sees another
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(rig.transform, false);
            cam.orthographic = true;
            cam.orthographicSize = H / 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.012f, 0.025f, 0.04f, 1);
            cam.cullingMask = 1 << 5;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50;
            cam.targetTexture = feed.Texture;

            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(rig.transform, false);
            canvasGo.transform.localPosition = new Vector3(0, 0, 10);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(W, H);
            var c = canvasGo.transform;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Color glow = TeamColours.For(t), dim = glow * 0.6f;
            dim.a = 1;

            // Frame lines and header
            Box(c, new Vector2(0, H / 2f - 4), new Vector2(W, 8), glow);
            Box(c, new Vector2(0, -H / 2f + 4), new Vector2(W, 8), glow);
            Box(c, new Vector2(0, 168), new Vector2(W - 60, 2), dim);
            var logo = TeamColours.Logo(t);
            if (logo != null)
            {
                var img = new GameObject("Logo", typeof(RawImage));
                img.layer = 5;
                img.transform.SetParent(c, false);
                img.GetComponent<RawImage>().texture = logo;
                ((RectTransform)img.transform).sizeDelta = new Vector2(100, 100);
                img.transform.localPosition = new Vector3(-440, 222, 0);
            }
            Label(c, font, t.ToString().ToUpperInvariant() + " BASE", 46, Color.white, new Vector2(-370, 222), TextAnchor.MiddleLeft, 500);
            feed.Clock = Label(c, font, "30:00", 50, glow, new Vector2(470, 222), TextAnchor.MiddleRight, 300);

            // Scores
            int teams = Mathf.Clamp(MatchInfo.Teams, 2, 4);
            feed.Scores = new Text[teams];
            for (int i = 0; i < teams; i++)
            {
                var tt = (TeamColours.Team)(i + 1);
                float x = teams == 2 ? (i == 0 ? -220 : 220) : -360 + i * 240;
                Label(c, font, tt.ToString().ToUpperInvariant(), 30, TeamColours.For(tt), new Vector2(x, 120), TextAnchor.MiddleCenter, 240);
                feed.Scores[i] = Label(c, font, "0", 150, TeamColours.For(tt), new Vector2(x, 30), TextAnchor.MiddleCenter, 240);
            }
            if (teams == 2)
                Label(c, font, ":", 120, Color.white * 0.7f, new Vector2(0, 40), TextAnchor.MiddleCenter, 80);

            // Flags
            feed.FlagRows = new Text[teams];
            for (int i = 0; i < teams; i++)
                feed.FlagRows[i] = Label(c, font, "", 30, Color.white, new Vector2(-470, -95 - i * 44), TextAnchor.MiddleLeft, 940);

            // Ticker band
            Box(c, new Vector2(0, -238), new Vector2(W, 52), new Color(glow.r, glow.g, glow.b, 0.22f));
            feed.Ticker = Label(c, font, "", 28, Color.white, new Vector2(0, -238), TextAnchor.MiddleLeft, 4000);
            ((RectTransform)feed.Ticker.transform).pivot = new Vector2(0, 0.5f);
            feeds.Add(feed);
            return feed;
        }

        void Update()
        {
            float now = Time.time;
            bool blink = Mathf.Repeat(now, 1f) < 0.6f;
            foreach (var feed in feeds)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(MatchInfo.TimeLeft));
                feed.Clock.text = $"{left / 60:00}:{left % 60:00}";
                for (int i = 0; i < feed.Scores.Length; i++)
                {
                    feed.Scores[i].text = MatchInfo.Scores[i].ToString();
                    var tt = (TeamColours.Team)(i + 1);
                    string who = tt.ToString().ToUpperInvariant() + " FLAG";
                    var row = feed.FlagRows[i];
                    switch (MatchInfo.Flags[i])
                    {
                        case MatchInfo.FlagState.Home:
                            row.text = $"{who}    SECURE";
                            row.color = new Color(0.45f, 1f, 0.6f);
                            break;
                        case MatchInfo.FlagState.Taken:
                            row.text = $"{who}    TAKEN  {(MatchInfo.Carriers[i] ?? "").ToUpperInvariant()}";
                            row.color = blink ? new Color(1f, 0.3f, 0.25f) : new Color(0.6f, 0.15f, 0.12f);
                            break;
                        default:
                            row.text = $"{who}    DROPPED";
                            row.color = new Color(1f, 0.75f, 0.2f);
                            break;
                    }
                }
                feed.Ticker.text = string.Join("     //     ", MatchInfo.Ticker);
                float width = Mathf.Max(W, feed.Ticker.preferredWidth);
                feed.Ticker.transform.localPosition = new Vector3(W / 2f - Mathf.Repeat(now * 140, width + W), -238, 0);
            }
        }

        static void Box(Transform parent, Vector2 pos, Vector2 size, Color colour)
        {
            var go = new GameObject("Box", typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = colour;
            ((RectTransform)go.transform).sizeDelta = size;
            go.transform.localPosition = pos;
        }

        static Text Label(Transform parent, Font font, string text, int size, Color colour, Vector2 pos, TextAnchor anchor, float width)
        {
            var go = new GameObject("Text", typeof(Text));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = colour;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, size * 1.3f);
            if (anchor == TextAnchor.MiddleLeft) rt.pivot = new Vector2(0, 0.5f);
            else if (anchor == TextAnchor.MiddleRight) rt.pivot = new Vector2(1, 0.5f);
            go.transform.localPosition = pos;
            return t;
        }
    }
}
