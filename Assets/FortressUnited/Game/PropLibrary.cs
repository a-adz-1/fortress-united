using System.Collections.Generic;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The props mappers place with fu_prop, by name. Any model in
    /// Resources/Props/&lt;name&gt; (a prefab or model, e.g. one made in Meshy)
    /// is used as it is, as are an imported Asset Store pack's pieces
    /// (kit_&lt;name&gt;, registered by AssetPackProps into Resources/ThirdParty); otherwise the built-in sci-fi props are built here from
    /// simple shapes in the maps' panel style, with a glow accent:
    /// crate, crate_tall, barrel, console, pipes, tank, antenna, container,
    /// pillar, light_post. Props stand on their origin (the bottom middle) and
    /// face their yaw; they don't collide (put a clip brush round them).
    /// </summary>
    public static class PropLibrary
    {
        public static readonly string[] BuiltIn =
            { "crate", "crate_tall", "barrel", "console", "pipes", "tank", "antenna", "container", "pillar", "light_post" };

        static Material hull, trim;
        static readonly Dictionary<Color, Material> glows = new Dictionary<Color, Material>();

        /// <summary>Builds a prop; null if the name isn't known.</summary>
        public static GameObject Make(string name, Color glow)
        {
            var custom = Resources.Load<GameObject>("Props/" + name) ?? Resources.Load<GameObject>("ThirdParty/Props/" + name);
            if (custom != null)
                return Object.Instantiate(custom);
            var root = new GameObject(name);
            switch (name.ToLowerInvariant())
            {
                case "crate": Crate(root.transform, new Vector3(0.9f, 0.9f, 0.9f), glow); break;
                case "crate_tall": Crate(root.transform, new Vector3(0.9f, 1.8f, 0.9f), glow); break;
                case "barrel": Barrel(root.transform, glow); break;
                case "console": Console(root.transform, glow); break;
                case "pipes": Pipes(root.transform, glow); break;
                case "tank": Tank(root.transform, glow); break;
                case "antenna": Antenna(root.transform, glow); break;
                case "container": Container(root.transform, glow); break;
                case "pillar": Pillar(root.transform, glow); break;
                case "light_post": LightPost(root.transform, glow); break;
                default: Object.Destroy(root); return null;
            }
            foreach (var c in root.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
            return root;
        }

        // ------------------------------------------------------------ materials

        static Material Hull()
        {
            if (hull == null)
            {
                hull = new Material(Shader.Find("FortressUnited/SciFiSurface")) { name = "Prop Hull" };
                hull.SetFloat("_Metal", 1);
                hull.SetFloat("_GlowAmount", 0);
            }
            return hull;
        }

        static Material Trim()
        {
            if (trim == null)
            {
                trim = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Prop Trim" };
                trim.SetColor("_BaseColor", new Color(0.09f, 0.095f, 0.11f));
                trim.SetFloat("_Metallic", 0.5f);
                trim.SetFloat("_Smoothness", 0.5f);
            }
            return trim;
        }

        static Material Glow(Color c)
        {
            if (!glows.TryGetValue(c, out var m))
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Prop Glow" };
                m.SetColor("_BaseColor", c * 2.2f);
                glows[c] = m;
            }
            return m;
        }

        static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        // ------------------------------------------------------------ the props

        /// <summary>A cargo crate: panelled body, dark corner posts, a glowing band round the middle.</summary>
        static void Crate(Transform t, Vector3 s, Color glow)
        {
            Part(t, PrimitiveType.Cube, new Vector3(0, s.y / 2, 0), s * 0.96f, Hull());
            const float post = 0.07f;
            foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                    Part(t, PrimitiveType.Cube, new Vector3(x * s.x / 2, s.y / 2, z * s.z / 2) - new Vector3(x, 0, z) * post / 2,
                         new Vector3(post, s.y, post), Trim());
            Part(t, PrimitiveType.Cube, new Vector3(0, s.y * 0.5f, 0), new Vector3(s.x * 0.98f, 0.03f, s.z * 0.98f), Glow(glow));
            Part(t, PrimitiveType.Cube, new Vector3(0, s.y - 0.02f, 0), new Vector3(s.x, 0.04f, s.z), Trim());
        }

        static void Barrel(Transform t, Color glow)
        {
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.45f, 0), new Vector3(0.58f, 0.45f, 0.58f), Hull());
            foreach (float y in new[] { 0.08f, 0.82f })
                Part(t, PrimitiveType.Cylinder, new Vector3(0, y, 0), new Vector3(0.62f, 0.03f, 0.62f), Trim());
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.025f, 0.6f), Glow(glow));
        }

        /// <summary>A control console: a slanted desk with a glowing screen and button rows.</summary>
        static void Console(Transform t, Color glow)
        {
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.45f, 0), new Vector3(1.2f, 0.9f, 0.6f), Hull());
            var desk = Part(t, PrimitiveType.Cube, new Vector3(0, 0.98f, 0.05f), new Vector3(1.2f, 0.06f, 0.55f), Trim(),
                            Quaternion.Euler(-20, 0, 0));
            Part(t, PrimitiveType.Cube, new Vector3(0, 1.32f, -0.22f), new Vector3(0.9f, 0.55f, 0.05f), Trim(), Quaternion.Euler(-10, 0, 0));
            Part(t, PrimitiveType.Cube, new Vector3(0, 1.32f, -0.19f), new Vector3(0.82f, 0.47f, 0.01f), Glow(glow * 0.6f), Quaternion.Euler(-10, 0, 0));
            for (int i = 0; i < 6; i++)
                Part(t, PrimitiveType.Cube, new Vector3(-0.4f + i * 0.16f, 1.02f, 0.15f), new Vector3(0.06f, 0.015f, 0.04f),
                     Glow(i % 3 == 0 ? new Color(1f, 0.5f, 0.1f) : glow), Quaternion.Euler(-20, 0, 0));
        }

        /// <summary>A run of three pipes (2 m), on brackets, one with a glowing seam.</summary>
        static void Pipes(Transform t, Color glow)
        {
            var along = Quaternion.Euler(0, 0, 90);
            for (int i = 0; i < 3; i++)
                Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.25f + i * 0.28f, 0), new Vector3(0.22f, 1f, 0.22f), Hull(), along);
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.81f, 0), new Vector3(0.235f, 1f, 0.03f), Glow(glow), along);
            foreach (float x in new[] { -0.8f, 0.8f })
                Part(t, PrimitiveType.Cube, new Vector3(x, 0.5f, 0), new Vector3(0.08f, 1f, 0.3f), Trim());
        }

        /// <summary>A storage tank: tall cylinder with ribs, a glowing ring and a gauge light.</summary>
        static void Tank(Transform t, Color glow)
        {
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 1.4f, 0), new Vector3(1.6f, 1.4f, 1.6f), Hull());
            Part(t, PrimitiveType.Sphere, new Vector3(0, 2.8f, 0), new Vector3(1.6f, 0.5f, 1.6f), Hull());
            foreach (float y in new[] { 0.3f, 1.4f, 2.5f })
                Part(t, PrimitiveType.Cylinder, new Vector3(0, y, 0), new Vector3(1.66f, 0.04f, 1.66f), Trim());
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 2.0f, 0), new Vector3(1.64f, 0.03f, 1.64f), Glow(glow));
        }

        /// <summary>An antenna mast with a blinking red beacon.</summary>
        static void Antenna(Transform t, Color glow)
        {
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.15f, 0), new Vector3(0.6f, 0.3f, 0.6f), Hull());
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 2.2f, 0), new Vector3(0.08f, 2f, 0.08f), Trim());
            foreach (float y in new[] { 1.6f, 2.6f, 3.4f })
                Part(t, PrimitiveType.Cube, new Vector3(0, y, 0), new Vector3(0.6f - y * 0.1f, 0.03f, 0.03f), Trim());
            var beacon = Part(t, PrimitiveType.Sphere, new Vector3(0, 4.25f, 0), Vector3.one * 0.12f, Glow(new Color(1f, 0.15f, 0.1f)));
            var l = new GameObject("Beacon").AddComponent<Light>();
            l.transform.SetParent(beacon, false);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.15f, 0.1f);
            l.range = 4;
            l.intensity = 1.5f;
            l.gameObject.AddComponent<LightEffect>().Effect = "strobe";
        }

        /// <summary>A shipping container (6 m): panelled sides, door end frame, a glow strip along the top.</summary>
        static void Container(Transform t, Color glow)
        {
            var s = new Vector3(2.4f, 2.6f, 6f);
            Part(t, PrimitiveType.Cube, new Vector3(0, s.y / 2, 0), s, Hull());
            foreach (float z in new[] { -1f, 1f })
                Part(t, PrimitiveType.Cube, new Vector3(0, s.y / 2, z * s.z / 2), new Vector3(s.x + 0.06f, s.y + 0.06f, 0.1f), Trim());
            foreach (float x in new[] { -1f, 1f })
                Part(t, PrimitiveType.Cube, new Vector3(x * (s.x / 2 + 0.01f), s.y - 0.25f, 0), new Vector3(0.02f, 0.04f, s.z * 0.9f), Glow(glow));
        }

        static void Pillar(Transform t, Color glow)
        {
            Part(t, PrimitiveType.Cube, new Vector3(0, 1.75f, 0), new Vector3(0.6f, 3.5f, 0.6f), Hull());
            foreach (float y in new[] { 0.05f, 3.45f })
                Part(t, PrimitiveType.Cube, new Vector3(0, y, 0), new Vector3(0.8f, 0.1f, 0.8f), Trim());
            foreach (float x in new[] { -1f, 1f })
                Part(t, PrimitiveType.Cube, new Vector3(x * 0.305f, 1.75f, 0), new Vector3(0.02f, 2.6f, 0.05f), Glow(glow));
        }

        /// <summary>A street light: a post with a glowing head that lights the ground.</summary>
        static void LightPost(Transform t, Color glow)
        {
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 1.6f, 0), new Vector3(0.12f, 1.6f, 0.12f), Trim());
            Part(t, PrimitiveType.Cube, new Vector3(0, 3.25f, 0.3f), new Vector3(0.25f, 0.1f, 0.8f), Trim());
            Part(t, PrimitiveType.Cube, new Vector3(0, 3.19f, 0.4f), new Vector3(0.18f, 0.02f, 0.55f), Glow(Color.Lerp(glow, Color.white, 0.6f)));
            var l = new GameObject("Lamp").AddComponent<Light>();
            l.transform.SetParent(t, false);
            l.transform.localPosition = new Vector3(0, 3.1f, 0.45f);
            l.transform.localRotation = Quaternion.Euler(90, 0, 0);
            l.type = LightType.Spot;
            l.spotAngle = 110;
            l.innerSpotAngle = 60;
            l.range = 9;
            l.intensity = 2.5f;
            l.color = Color.Lerp(glow, Color.white, 0.6f);
            l.shadows = LightShadows.Soft;
        }
    }
}
