using UnityEngine;
using UnityEngine.UI;

namespace FortressUnited.Game
{
    /// <summary>
    /// Draws a PlayerVitals readout (heart rate, live ECG trace, SpO2,
    /// temperature, blood pressure, status) into a render texture every
    /// frame, for screens on models such as the medikit. The UI rig lives
    /// far from the map with its own camera, so nothing else sees it.
    /// </summary>
    public sealed class VitalsDisplay : MonoBehaviour
    {
        public PlayerVitals Vitals;
        public int Width = 288, Height = 720;

        public RenderTexture Texture { get; private set; }
        public Material ScreenMaterial { get; private set; }

        static int instances;

        Text heartRate, spo2, temp, bp, status;
        EcgTrace ecg;
        Image statusBar;

        static readonly Color Teal = new Color(0.35f, 1f, 0.9f);
        static readonly Color Dim = new Color(0.25f, 0.6f, 0.6f);
        static readonly Color Warn = new Color(1f, 0.55f, 0.2f);
        static readonly Color Alarm = new Color(1f, 0.25f, 0.25f);

        void Awake()
        {
            Texture = new RenderTexture(Width, Height, 16, RenderTextureFormat.ARGB32) { name = "VitalsDisplay" };
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            ScreenMaterial = new Material(unlit) { name = "VitalsScreen", mainTexture = Texture };

            // Rig far away from the map, one per display
            var rig = new GameObject("VitalsDisplayRig");
            rig.transform.SetParent(transform, false);
            rig.transform.position = new Vector3(0, -20000 - 100 * instances++, 0);

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(rig.transform, false);
            cam.orthographic = true;
            cam.orthographicSize = Height / 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.05f, 0.06f, 1);
            cam.cullingMask = 1 << 5;          // UI layer only
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50;
            cam.targetTexture = Texture;

            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(rig.transform, false);
            canvasGo.transform.localPosition = new Vector3(0, 0, 10);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(Width, Height);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            float y = Height / 2f - 30;

            Label(canvasGo.transform, font, "VITALS", 22, Dim, new Vector2(0, y), TextAnchor.MiddleCenter);
            y -= 60;
            Label(canvasGo.transform, font, "HR", 22, Dim, new Vector2(-95, y), TextAnchor.MiddleLeft);
            heartRate = Label(canvasGo.transform, font, "72", 64, Teal, new Vector2(30, y), TextAnchor.MiddleRight);
            Label(canvasGo.transform, font, "bpm", 18, Dim, new Vector2(105, y - 10), TextAnchor.MiddleLeft);
            y -= 120;

            // ECG trace
            ecg = new EcgTrace(256, 120);
            var ecgGo = new GameObject("ECG", typeof(RawImage));
            ecgGo.layer = 5;
            ecgGo.transform.SetParent(canvasGo.transform, false);
            var raw = ecgGo.GetComponent<RawImage>();
            raw.texture = ecg.Texture;
            ((RectTransform)ecgGo.transform).sizeDelta = new Vector2(Width - 24, 150);
            ecgGo.transform.localPosition = new Vector3(0, y, 0);
            y -= 120;

            spo2 = Row(canvasGo.transform, font, "SpO₂", ref y);
            temp = Row(canvasGo.transform, font, "TEMP", ref y);
            bp = Row(canvasGo.transform, font, "BP", ref y);

            y -= 30;
            var bar = new GameObject("StatusBar", typeof(Image));
            bar.layer = 5;
            bar.transform.SetParent(canvasGo.transform, false);
            statusBar = bar.GetComponent<Image>();
            statusBar.color = new Color(0.1f, 0.4f, 0.4f, 0.6f);
            ((RectTransform)bar.transform).sizeDelta = new Vector2(Width - 30, 54);
            bar.transform.localPosition = new Vector3(0, y, 0);
            status = Label(canvasGo.transform, font, "STABLE", 26, Teal, new Vector2(0, y), TextAnchor.MiddleCenter);
        }

        Text Row(Transform parent, Font font, string name, ref float y)
        {
            Label(parent, font, name, 22, Dim, new Vector2(-95, y), TextAnchor.MiddleLeft);
            var value = Label(parent, font, "", 34, Teal, new Vector2(115, y), TextAnchor.MiddleRight);
            y -= 64;
            return value;
        }

        static Text Label(Transform parent, Font font, string text, int size, Color color, Vector2 pos, TextAnchor anchor)
        {
            var go = new GameObject(text, typeof(Text));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(200, size + 10);
            rt.pivot = anchor == TextAnchor.MiddleLeft ? new Vector2(0, 0.5f)
                     : anchor == TextAnchor.MiddleRight ? new Vector2(1, 0.5f) : new Vector2(0.5f, 0.5f);
            go.transform.localPosition = pos;
            return t;
        }

        void Update()
        {
            if (Vitals == null)
                return;
            float dt = Time.deltaTime;
            Vitals.Update(dt);

            ecg.Step(Vitals, dt);

            // Numbers
            int hr = Mathf.RoundToInt(Vitals.HeartRate);
            heartRate.text = Vitals.Flatlined ? "--" : hr.ToString();
            heartRate.color = Vitals.Dead || hr > 180 ? Alarm : hr > 140 ? Warn : Teal;
            spo2.text = Vitals.Dead ? "--" : $"{Mathf.RoundToInt(Vitals.SpO2)}%";
            spo2.color = Vitals.SpO2 < 90 ? Alarm : Vitals.SpO2 < 95 ? Warn : Teal;
            temp.text = $"{Vitals.Temperature:0.0}°C";
            temp.color = Vitals.Temperature > 39 ? Alarm : Vitals.Temperature > 37.5f ? Warn : Teal;
            bp.text = Vitals.Dead ? "--/--" : $"{Vitals.Systolic}/{Vitals.Diastolic}";

            string st = Vitals.Status;
            status.text = st;
            bool alarm = st == "FLATLINE" || st == "BURNING" || st == "CRITICAL" || st == "HYPOXIA";
            bool warn = st == "CONCUSSED" || st == "INFECTED" || st == "MAX EFFORT" || st == "ELEVATED";
            Color c = alarm ? Alarm : warn ? Warn : Teal;
            // Alarms blink
            if (alarm && Mathf.Repeat(Time.time, 0.8f) > 0.5f)
                c *= 0.45f;
            status.color = c;
            statusBar.color = new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 0.7f);
        }

        void OnDestroy()
        {
            if (Texture != null)
                Texture.Release();
        }
    }
}
