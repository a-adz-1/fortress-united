using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FortressUnited.Game
{
    /// <summary>
    /// Map signs as neon: the standard TFC signs' wording is re-lettered cleanly
    /// as glowing tubes (a pale core in a coloured outline; bloom adds the halo)
    /// on dark backing, and arrow signs become neon arrows pointing the same way
    /// as the original. Each sign is drawn once, at load, into its own texture
    /// by a UI rig far from the map.
    /// </summary>
    public static class NeonSigns
    {
        const string Arrow = "→";      // drawn turned to the original arrow's direction

        /// <summary>The wording of the standard signs (TFC's WADs), by texture name.</summary>
        static readonly Dictionary<string, string> Words = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["sign_team1"] = "BLUE\nTEAM", ["sign_team2"] = "RED\nTEAM",
            ["sign_bat"] = "BATTLEMENTS " + Arrow, ["sign_resup1"] = "RESUPPLY", ["sign_capt"] = "CAPTURE\nPOINT",
            ["sign_elev"] = "ELEVATOR", ["sign_flag"] = "FLAG", ["sign_flag2"] = "FLAG ROOM",
            ["sign_up"] = "UPPER LEVEL", ["sign_low"] = "LOWER LEVEL", ["sign_base"] = "BASEMENT",
            ["sign_ramp1"] = "RAMP\nACCESS", ["sign_ramp2"] = "BASEMENT", ["sign_water1"] = "WATER\nACCESS",
            ["sign_redbase"] = "RED BASE", ["sign_bluebase"] = "BLUE BASE",
            ["exitlft"] = "EXIT", ["exitrt"] = "EXIT", ["sign_exit"] = "EXIT", ["exit1"] = "EXIT",
        };

        static readonly HashSet<string> Arrows = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "sign_arw1", "sign_arw2", "sign_arw3", "sign_arw4", "sign_arw5", "sign_arw6", "sign_arw7", "sign_arw8",
            "cyb_arrow", "scroll_arrow2",
        };

        static int rigs;

        /// <summary>Which way the standard arrow signs point (degrees, counter-clockwise from right).</summary>
        static readonly Dictionary<string, float> ArrowAngles = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["sign_arw1"] = 180, ["sign_arw2"] = -90, ["sign_arw3"] = 0, ["sign_arw4"] = 90,
            ["sign_arw5"] = 45, ["sign_arw8"] = -135,
        };

        public static bool Has(string name) => Words.ContainsKey(name) || Arrows.Contains(name);

        /// <summary>The neon colour for a sign: team signs in their team's colour, exits red, the rest warm white.</summary>
        static Color ColourFor(string name, string words)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("blue") || n.EndsWith("team1") || (words != null && words.StartsWith("BLUE")))
                return TeamColours.For(TeamColours.Team.Blue);
            if (n.Contains("red") || n.EndsWith("team2") || (words != null && words.StartsWith("RED")))
                return TeamColours.For(TeamColours.Team.Red);
            if (words == "EXIT")
                return new Color(1f, 0.22f, 0.2f);
            if (n.StartsWith("sign_arw") || n.Contains("arrow"))
                return new Color(1f, 0.62f, 0.12f);        // direction arrows in amber
            return new Color(0.55f, 0.95f, 1f);           // cool white-cyan
        }

        /// <summary>Draws the sign's neon texture (sized to the old texture's shape), or null if it isn't a known sign.</summary>
        public static Texture2D Draw(string name, byte[] rgba, int w, int h)
        {
            bool arrow = Arrows.Contains(name);
            if (!arrow && !Words.ContainsKey(name))
                return null;
            string words = arrow ? Arrow : Words[name];
            Color neon = ColourFor(name, words);
            float angle = !arrow ? 0 : ArrowAngles.TryGetValue(name, out var known) ? known : ArrowAngle(rgba, w, h);

            int W = Mathf.Clamp(w * 8, 256, 1024), H = Mathf.Clamp(h * 8, 128, 1024);
            return DrawText(words, neon, W, H, angle, arrow, name);
        }

        /// <summary>
        /// Draws any wording as neon (a mapper's fu_sign, or a standard sign) into a
        /// W x H texture; "->" in the wording becomes an arrow. Ready a couple of frames later.
        /// </summary>
        public static Texture2D DrawText(string words, Color neon, int W, int H, float angle = 0, bool arrow = false, string name = "fu_sign")
        {
            words = words.Replace("->", Arrow).Replace("\\n", "\n");
            var rt = new RenderTexture(W, H, 16, RenderTextureFormat.ARGB32)
            {
                name = "Neon " + name,
                filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp,
            };
            var rig = new GameObject("Neon Sign Rig");
            rig.transform.position = new Vector3(0, -60000 - 3000 * rigs++, 0);   // apart, so no sign's camera sees another
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(rig.transform, false);
            cam.orthographic = true;
            cam.orthographicSize = H / 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << 5;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50;
            cam.targetTexture = rt;
            cam.enabled = false;

            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(rig.transform, false);
            canvasGo.transform.localPosition = new Vector3(0, 0, 10);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(W, H);

            var go = new GameObject("Text", typeof(Text));
            go.layer = 5;
            go.transform.SetParent(canvasGo.transform, false);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = words;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 8;
            t.resizeTextMaxSize = arrow ? (int)(Mathf.Min(W, H) * 0.9f) : (int)(H * 0.42f);
            t.lineSpacing = 0.95f;
            t.color = Color.Lerp(Color.white, neon, 0.35f);                 // the tube's hot core
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(W * 0.86f, H * 0.8f);
            rect.localRotation = Quaternion.Euler(0, 0, angle);
            // The tube's coloured glass: two outlines round the core
            float stroke = Mathf.Max(2, H / 60f);
            var o1 = go.AddComponent<Outline>();
            o1.effectColor = neon;
            o1.effectDistance = new Vector2(stroke, stroke);
            var o2 = go.AddComponent<Outline>();
            o2.effectColor = new Color(neon.r, neon.g, neon.b, 0.5f);
            o2.effectDistance = new Vector2(stroke * 2, stroke * 2);

            // Drawn a couple of frames later, once the UI has built the text, and copied into a
            // plain texture (a render texture drawn once doesn't reliably keep its contents)
            var sign = new Texture2D(W, H, TextureFormat.RGBA32, true)
            {
                name = "Neon " + name, filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp,
            };
            sign.SetPixels32(new Color32[W * H]);      // black until drawn
            sign.Apply(true);
            var soon = rig.AddComponent<RenderSoon>();
            soon.Camera = cam;
            soon.Target = sign;
            return sign;
        }

        /// <summary>Renders the sign's camera once the text exists, then removes the rig.</summary>
        sealed class RenderSoon : MonoBehaviour
        {
            public Camera Camera;
            public Texture2D Target;
            int frames;

            void LateUpdate()
            {
                if (++frames < 2)
                    return;
                Canvas.ForceUpdateCanvases();
                Camera.Render();
                var rtex = Camera.targetTexture;
                var was = RenderTexture.active;
                RenderTexture.active = rtex;
                Target.ReadPixels(new Rect(0, 0, rtex.width, rtex.height), 0, 0);
                RenderTexture.active = was;
                Target.Apply(true);                     // with mip levels for distant signs
                // DEMO_DUMP_SIGNS=folder saves each drawn sign, for checking
                var dump = System.Environment.GetEnvironmentVariable("DEMO_DUMP_SIGNS");
                if (!string.IsNullOrEmpty(dump))
                {
                    System.IO.Directory.CreateDirectory(dump);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dump, Target.name + ".png"), Target.EncodeToPNG());
                }
                Camera.targetTexture = null;
                rtex.Release();
                Destroy(rtex);
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Which way the old arrow points (degrees, counter-clockwise from right, in the
        /// drawn texture's y-up frame): an arrow's head is its heavy end, so the symbol's
        /// centre of mass sits off its middle towards the head.
        /// </summary>
        static float ArrowAngle(byte[] rgba, int w, int h)
        {
            if (rgba == null || rgba.Length < w * h * 4)
                return 0;
            // Symbol: whatever differs from the background (per-channel median)
            int[] bg = new int[3];
            for (int ch = 0; ch < 3; ch++)
            {
                var hist = new int[256];
                for (int i = ch; i < w * h * 4; i += 4) hist[rgba[i]]++;
                int half = w * h / 2, sum = 0;
                for (int v = 0; v < 256; v++) { sum += hist[v]; if (sum >= half) { bg[ch] = v; break; } }
            }
            float cx = 0, cy = 0, n = 0;
            float minX = w, maxX = 0, minY = h, maxY = 0;
            int mx = w / 8, my = h / 8;      // ignore the sign's frame
            for (int y = my; y < h - my; y++)
                for (int x = mx; x < w - mx; x++)
                {
                    int i = (y * w + x) * 4;
                    int d = Mathf.Max(Mathf.Abs(rgba[i] - bg[0]), Mathf.Max(Mathf.Abs(rgba[i + 1] - bg[1]), Mathf.Abs(rgba[i + 2] - bg[2])));
                    if (d < 60) continue;
                    cx += x; cy += y; n++;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            if (n < 4)
                return 0;
            float dx = cx / n - (minX + maxX) * 0.5f;
            float dyDown = cy / n - (minY + maxY) * 0.5f;     // texture rows run top to bottom
            float a = Mathf.Atan2(-dyDown, dx) * Mathf.Rad2Deg;
            return Mathf.Round(a / 45f) * 45f;
        }
    }
}
