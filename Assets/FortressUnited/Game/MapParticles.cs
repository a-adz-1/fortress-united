using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// Particle effects mappers place with fu_particles, by name: steam, smoke,
    /// sparks, embers, dust, drips, hologram, fire. Keys: effect, rendercolor
    /// (overrides the effect's colour), rate and size (multipliers), radius
    /// (units, the area it comes from), angles (the direction it goes; up by
    /// default). Sparks, fire and holograms also light their surroundings.
    /// </summary>
    public static class MapParticles
    {
        public static readonly string[] Effects = { "steam", "smoke", "sparks", "embers", "dust", "drips", "hologram", "fire" };

        static Texture2D soft;
        static Material alphaMat, addMat;

        public static GameObject Make(string effect, Vector3 direction, Color? colour, float rate, float size, float radius)
        {
            effect = effect.ToLowerInvariant();
            if (System.Array.IndexOf(Effects, effect) < 0)
                return null;
            var go = new GameObject("fu_particles " + effect);
            go.transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0 ? direction : Vector3.up,
                                                            Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;
            var col = ps.colorOverLifetime;
            var sizeLife = ps.sizeOverLifetime;
            var noise = ps.noise;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12;
            shape.radius = Mathf.Max(0.02f, radius);
            col.enabled = true;
            bool additive = false;
            Color c;
            switch (effect)
            {
                case "steam":
                    c = colour ?? new Color(0.92f, 0.95f, 1f, 0.32f);
                    Basic(main, 2.5f, 0.6f, 0.4f * size);
                    emission.rateOverTime = 12 * rate;
                    Grow(sizeLife, 3.2f);
                    Fade(col, c, c);
                    Turbulent(noise, 0.4f);
                    break;
                case "smoke":
                    c = colour ?? new Color(0.24f, 0.24f, 0.27f, 0.5f);
                    Basic(main, 4f, 0.4f, 0.6f * size);
                    emission.rateOverTime = 8 * rate;
                    Grow(sizeLife, 3.5f);
                    Fade(col, c, c);
                    Turbulent(noise, 0.3f);
                    break;
                case "sparks":
                    c = colour ?? new Color(1f, 0.7f, 0.3f, 1f);
                    additive = true;
                    Basic(main, 0.6f, 4.5f, 0.035f * size);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
                    main.gravityModifier = 1f;
                    shape.angle = 35;
                    emission.rateOverTime = 0;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0.2f, (short)(15 * rate), (short)(40 * rate), 0, 1.6f) });
                    Fade(col, Color.white, c);
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.04f;
                    renderer.lengthScale = 1;
                    Lamp(go, c, 1.6f, 3.5f, "flicker");
                    break;
                case "embers":
                    c = colour ?? new Color(1f, 0.45f, 0.1f, 1f);
                    additive = true;
                    Basic(main, 3.2f, 0.5f, 0.045f * size);
                    emission.rateOverTime = 8 * rate;
                    Fade(col, c, c);
                    Turbulent(noise, 0.6f);
                    break;
                case "dust":
                    c = colour ?? new Color(1f, 0.98f, 0.92f, 0.35f);
                    Basic(main, 8f, 0.05f, 0.025f * size);
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = Vector3.one * Mathf.Max(1, radius * 2);
                    emission.rateOverTime = 5 * rate;
                    Fade(col, c, c);
                    Turbulent(noise, 0.12f);
                    break;
                case "drips":
                    c = colour ?? new Color(0.6f, 0.75f, 0.9f, 0.7f);
                    Basic(main, 1.2f, 0f, 0.025f * size);
                    main.gravityModifier = 1f;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    emission.rateOverTime = 3 * rate;
                    Fade(col, c, c);
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.06f;
                    break;
                case "hologram":
                    c = colour ?? new Color(0.35f, 0.9f, 1f, 1f);
                    additive = true;
                    Basic(main, 2f, 0.45f, 0.05f * size);
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = Mathf.Max(0.3f, radius);
                    emission.rateOverTime = 24 * rate;
                    Fade(col, c, c);
                    Lamp(go, c, 0.8f, 3f, "flicker");
                    break;
                default: // fire
                    c = colour ?? new Color(1f, 0.55f, 0.15f, 1f);
                    additive = true;
                    Basic(main, 0.8f, 1.1f, 0.5f * size);
                    emission.rateOverTime = 30 * rate;
                    shape.angle = 8;
                    Grow(sizeLife, 0.2f);
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0), new GradientColorKey(c, 0.4f), new GradientColorKey(new Color(0.7f, 0.1f, 0.05f), 1) },
                              new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
                    col.color = g;
                    Turbulent(noise, 0.5f);
                    Lamp(go, c, 2f, 5f, "candle");
                    break;
            }
            renderer.sharedMaterial = Material(additive);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return go;
        }

        static void Basic(ParticleSystem.MainModule main, float life, float speed, float size)
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life * 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed * 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        }

        static void Grow(ParticleSystem.SizeOverLifetimeModule s, float end)
        {
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, end));
        }

        /// <summary>Fades in quickly, fades out at the end, from one colour to another.</summary>
        static void Fade(ParticleSystem.ColorOverLifetimeModule col, Color from, Color to)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0), new GradientColorKey(to, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(from.a, 0.12f), new GradientAlphaKey(to.a * 0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        static void Turbulent(ParticleSystem.NoiseModule n, float strength)
        {
            n.enabled = true;
            n.strength = strength;
            n.frequency = 0.6f;
            n.scrollSpeed = 0.3f;
        }

        static void Lamp(GameObject go, Color c, float intensity, float range, string effect)
        {
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = new Vector3(0, 0, 0.3f);
            l.type = LightType.Point;
            l.color = new Color(c.r, c.g, c.b);
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.gameObject.AddComponent<LightEffect>().Effect = effect;
        }

        static Material Material(bool additive)
        {
            if (soft == null)
            {
                const int S = 64;
                soft = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "Soft Particle", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = new Vector2(x - S / 2f + 0.5f, y - S / 2f + 0.5f).magnitude / (S / 2f);
                        byte a = (byte)(255 * Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d));
                        px[y * S + x] = new Color32(255, 255, 255, a);
                    }
                soft.SetPixels32(px);
                soft.Apply(true);
            }
            ref Material m = ref additive ? ref addMat : ref alphaMat;
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = additive ? "Particles Add" : "Particles Alpha" };
                m.SetTexture("_BaseMap", soft);
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", additive ? 2 : 0);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                if (additive) m.SetColor("_BaseColor", new Color(2f, 2f, 2f, 1));
            }
            return m;
        }
    }
}
