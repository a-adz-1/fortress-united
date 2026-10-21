using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// Animates a light's brightness: flicker (a failing tube), pulse (slow
    /// breathing), strobe (on-off), candle (soft wavering, for fire). Used by
    /// fu_light and by particle effects that light their surroundings.
    /// </summary>
    public sealed class LightEffect : MonoBehaviour
    {
        public string Effect = "none";
        public float Speed = 1;
        float baseIntensity, seed;
        Light lamp;

        void Start()
        {
            lamp = GetComponent<Light>();
            baseIntensity = lamp != null ? lamp.intensity : 1;
            seed = Random.value * 100;
        }

        void Update()
        {
            if (lamp == null) return;
            float t = Time.time * Speed + seed;
            float k = 1;
            switch (Effect)
            {
                case "flicker":
                    // Mostly on, with ragged drop-outs
                    float n = Mathf.PerlinNoise(t * 9, seed);
                    k = n < 0.32f ? 0.15f : (n < 0.4f ? 0.6f : 1);
                    break;
                case "pulse":
                    k = 0.55f + 0.45f * Mathf.Sin(t * 2.2f);
                    break;
                case "strobe":
                    k = Mathf.Repeat(t * 2.5f, 1) < 0.18f ? 1.6f : 0;
                    break;
                case "candle":
                    k = 0.8f + 0.25f * Mathf.PerlinNoise(t * 6, seed) - 0.08f * Mathf.PerlinNoise(t * 17, seed + 3);
                    break;
            }
            lamp.intensity = baseIntensity * k;
        }
    }
}
