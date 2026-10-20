using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The sniper rifle's laser dot, which everyone can see (TFC's
    /// laserdot), in the shooter's team colour: the reference red or blue
    /// from TeamColours, never a separate red. A bright core with a soft
    /// halo where the aim meets a surface, a faint beam, and a little light.
    /// Point it each frame with Aim(); it hides when nothing is in reach.
    /// </summary>
    public sealed class SniperDot : MonoBehaviour
    {
        public bool Red;
        public float Range = 8192 * 0.0254f;      // TFC traces the dot 8192 units
        public float Size = 0.06f;

        Transform core, halo;
        Material coreMat, haloMat;
        Light glow;
        LineRenderer beam;
        static Texture2D soft;

        public static SniperDot Create(bool red)
        {
            var dot = new GameObject("Sniper Dot").AddComponent<SniperDot>();
            dot.Red = red;
            dot.Build();
            return dot;
        }

        void Build()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            core = Quad("Core", out coreMat, shader, TeamColours.Glow(TeamColours.CoreFor(Red), 4f), Size);
            halo = Quad("Halo", out haloMat, shader, TeamColours.Glow(TeamColours.For(Red), 2f), Size * 4);
            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.type = LightType.Point;
            glow.color = TeamColours.For(Red);
            glow.range = 1.2f;
            glow.intensity = 1.5f;
            glow.shadows = LightShadows.None;
            beam = gameObject.AddComponent<LineRenderer>();
            beam.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            beam.positionCount = 2;
            beam.widthMultiplier = 0.006f;
            var c = TeamColours.For(Red);
            beam.startColor = new Color(c.r, c.g, c.b, 0);
            beam.endColor = new Color(c.r, c.g, c.b, 0.25f);
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Transform Quad(string name, out Material mat, Shader shader, Color colour, float size)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(q.GetComponent<Collider>());
            q.name = name;
            q.transform.SetParent(transform, false);
            q.transform.localScale = Vector3.one * size;
            mat = new Material(shader) { name = name };
            mat.SetTexture("_BaseMap", Soft());
            mat.mainTexture = Soft();
            mat.SetColor("_BaseColor", colour);
            mat.color = colour;
            // Additive, so it glows over anything
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 2);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 50;
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q.transform;
        }

        static Texture2D Soft()
        {
            if (soft != null) return soft;
            const int n = 64;
            soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DotSoft" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                    soft.SetPixel(x, y, new Color(1, 1, 1, a * a * a));
                }
            soft.Apply();
            return soft;
        }

        /// <summary>Points the dot from the rifle along a direction; hides it when nothing is hit.</summary>
        public void Aim(Vector3 from, Vector3 direction, Camera viewer)
        {
            bool hit = Physics.Raycast(from, direction, out var h, Range);
            core.gameObject.SetActive(hit);
            halo.gameObject.SetActive(hit);
            glow.enabled = hit;
            beam.enabled = hit;
            if (!hit)
                return;
            transform.position = h.point + h.normal * 0.01f;
            // Face the viewer so it always reads as a dot
            var look = viewer != null ? viewer.transform.position - transform.position : h.normal;
            core.rotation = halo.rotation = Quaternion.LookRotation(-look);
            float pulse = 1 + 0.12f * Mathf.Sin(Time.time * 18);
            halo.localScale = Vector3.one * Size * 4 * pulse;
            beam.SetPosition(0, from);
            beam.SetPosition(1, h.point);
        }
    }
}
