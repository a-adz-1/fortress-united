using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A sweeping ECG trace drawn into a small texture, like a hospital
    /// monitor: the line is written left to right and erases just ahead of
    /// itself. Used by the medikit screen and the chest screens on bodies.
    /// </summary>
    public sealed class EcgTrace
    {
        public readonly Texture2D Texture;
        public float SweepSeconds = 3f;   // time for the trace to cross the screen
        public bool Grid = true;

        readonly int w, h;
        readonly Color32[] pixels;
        int x;
        float lastY = -1;
        float pixelDebt;

        static readonly Color32 Background = new Color32(3, 13, 16, 255);
        static readonly Color32 GridColour = new Color32(10, 40, 45, 255);
        public static readonly Color32 Live = new Color32(90, 255, 220, 255);
        public static readonly Color32 Flat = new Color32(255, 70, 70, 255);

        public EcgTrace(int width, int height)
        {
            w = width;
            h = height;
            Texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "ECG" };
            pixels = new Color32[w * h];
            for (int i = 0; i < w; i++)
                Erase(i);
            Texture.SetPixels32(pixels);
            Texture.Apply(false);
        }

        /// <summary>Advances the sweep by dt, sampling the vitals' ECG.</summary>
        public void Step(PlayerVitals vitals, float dt)
        {
            pixelDebt += dt * w / SweepSeconds;
            int steps = Mathf.Min(w, (int)pixelDebt);
            pixelDebt -= steps;
            if (steps == 0)
                return;
            float sampleDt = SweepSeconds / w;
            Color32 line = vitals.Dead ? Flat : Live;
            int thick = Mathf.Max(1, h / 60);
            for (int s = 0; s < steps; s++)
            {
                float v = vitals.EcgSample(sampleDt);
                float y = Mathf.Clamp(h * 0.3f + v * h * 0.6f, thick, h - 1 - thick);
                Erase((x + Mathf.Max(3, w / 40)) % w);
                if (lastY < 0 || x == 0) lastY = y;
                int y0 = (int)Mathf.Min(lastY, y) - thick + 1, y1 = (int)Mathf.Max(lastY, y) + thick - 1;
                for (int py = Mathf.Max(0, y0); py <= Mathf.Min(h - 1, y1); py++)
                {
                    pixels[py * w + x] = line;
                    if (x + 1 < w) pixels[py * w + x + 1] = line;
                }
                lastY = y;
                x = (x + 1) % w;
            }
            Texture.SetPixels32(pixels);
            Texture.Apply(false);
        }

        void Erase(int col)
        {
            int gx = Mathf.Max(1, w / 16), gy = Mathf.Max(1, h / 6);
            for (int y = 0; y < h; y++)
                pixels[y * w + col] = Grid && (col % gx == 0 || y % gy == 0) ? GridColour : Background;
        }
    }
}
