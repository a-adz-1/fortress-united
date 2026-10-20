using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// Particle effects for robots, made in code: sparks (hot, additive,
    /// streaking, falling) and steam (soft puffs that rise and spread).
    /// </summary>
    public static class FxParticles
    {
        static Material sparkMaterial, steamMaterial;
        static Texture2D dot;

        /// <summary>A spark emitter; fire it with Emit(count).</summary>
        public static ParticleSystem Sparks(Transform parent, Color colour)
        {
            var ps = Make("Sparks", parent, SparkMaterial());
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
            main.startColor = new ParticleSystem.MinMaxGradient(colour, Color.Lerp(colour, Color.white, 0.7f));
            main.gravityModifier = 1.2f;
            main.maxParticles = 200;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.04f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(Color.white, 0.6f));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.5f;
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.bounce = 0.3f;
            collision.lifetimeLoss = 0.3f;
            collision.quality = ParticleSystemCollisionQuality.Low;
            return ps;
        }

        /// <summary>A steam emitter; set its emission rate to vent.</summary>
        public static ParticleSystem Steam(Transform parent)
        {
            var ps = Make("Steam", parent, SteamMaterial());
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor = new Color(0.9f, 0.92f, 0.95f, 0.35f);
            main.gravityModifier = -0.08f;
            main.maxParticles = 150;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25;
            shape.radius = 0.08f;
            shape.rotation = new Vector3(-90, 0, 0);   // vent upwards
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 3.5f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(Color.white, 0.2f));
            var emission = ps.emission;
            emission.rateOverTime = 0;
            ps.Play();
            return ps;
        }

        static ParticleSystem Make(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.loop = true;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static Gradient Fade(Color c, float holdUntil)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, holdUntil), new GradientAlphaKey(0, 1) });
            return g;
        }

        static Texture2D Dot()
        {
            if (dot != null)
                return dot;
            const int n = 32;
            dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                    dot.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
            dot.Apply();
            return dot;
        }

        /// <summary>URP particle material, transparent; additive for glowing sparks.</summary>
        static Material Particle(string name, bool additive, float brightness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = name };
            m.SetTexture("_BaseMap", Dot());
            m.mainTexture = Dot();
            m.SetColor("_BaseColor", new Color(brightness, brightness, brightness, 1));
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        static Material SparkMaterial() => sparkMaterial != null ? sparkMaterial : sparkMaterial = Particle("Sparks", true, 4);
        static Material SteamMaterial() => steamMaterial != null ? steamMaterial : steamMaterial = Particle("Steam", false, 1);
    }
}
