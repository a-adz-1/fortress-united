using UnityEngine;
using UnityEngine.UI;

namespace FortressUnited.Game
{
    /// <summary>
    /// A faint haze of static over your own view while concussed (a system
    /// glitch, for a robot). Kept light on purpose: at full strength it is
    /// a soft grain, never enough to hide anything, and it fades out with
    /// the concussion.
    /// </summary>
    public sealed class ScreenHaze : MonoBehaviour
    {
        [Range(0, 1)] public float Amount;
        [Tooltip("Opacity at full strength.")]
        public float MaxOpacity = 0.09f;
        public float FramesPerSecond = 20;

        RawImage image;
        Texture2D noise;
        Color32[] pixels;
        float nextFrame;
        const int W = 192, H = 108;
        readonly System.Random random = new System.Random();

        public static ScreenHaze Create()
        {
            var go = new GameObject("Screen Haze", typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var haze = go.AddComponent<ScreenHaze>();
            haze.Build();
            return haze;
        }

        void Build()
        {
            noise = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, name = "Static" };
            pixels = new Color32[W * H];
            var go = new GameObject("Static", typeof(RawImage));
            go.transform.SetParent(transform, false);
            image = go.GetComponent<RawImage>();
            image.texture = noise;
            image.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            image.enabled = false;
        }

        void Update()
        {
            bool on = Amount > 0.01f;
            image.enabled = on;
            if (!on)
                return;
            image.color = new Color(1, 1, 1, MaxOpacity * Amount);
            if (Time.unscaledTime < nextFrame)
                return;
            nextFrame = Time.unscaledTime + 1 / FramesPerSecond;

            // Grey grain, with a faint brighter band rolling down now and then
            float band = Mathf.Repeat(Time.unscaledTime * 0.35f, 1.4f) * H;
            for (int y = 0; y < H; y++)
            {
                float lift = Mathf.Clamp01(1 - Mathf.Abs(y - (H - band)) / 6f) * 60;
                for (int x = 0; x < W; x++)
                {
                    byte v = (byte)Mathf.Min(255, random.Next(40, 220) + lift);
                    pixels[y * W + x] = new Color32(v, v, v, 255);
                }
            }
            noise.SetPixels32(pixels);
            noise.Apply(false);
        }

        void OnDestroy()
        {
            if (noise != null)
                Destroy(noise);
        }
    }
}
