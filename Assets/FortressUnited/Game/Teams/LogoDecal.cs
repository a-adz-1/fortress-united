using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FortressUnited.Game
{
    /// <summary>
    /// Projects a team logo onto whatever surface is under it (URP decal),
    /// so any model can carry the current logo: put a marker where it goes
    /// and attach one. Uses TeamColours.TintedLogo, so swapping the logo
    /// file changes every decal. Needs the decal feature on the renderer
    /// (Fortress United > Set Up Render Pipeline adds it).
    /// </summary>
    public static class LogoDecal
    {
        static Shader shader;

        /// <param name="at">Where the logo goes; it projects along this transform's forward (+Z).</param>
        /// <param name="size">Width and height of the logo in metres.</param>
        public static DecalProjector Attach(Transform at, Vector2 size, bool red, float depth = 0.1f)
        {
            if (shader == null)
                shader = Shader.Find("Shader Graphs/Decal");
            var go = new GameObject("Team Logo");
            go.transform.SetParent(at, false);
            var d = go.AddComponent<DecalProjector>();
            var m = new Material(shader) { name = "TeamLogo" };
            m.SetTexture("Base_Map", TeamColours.TintedLogo(red));
            d.material = m;
            d.size = new Vector3(size.x, size.y, depth);
            d.pivot = new Vector3(0, 0, depth * 0.5f);
            d.fadeFactor = 1;
            return d;
        }
    }
}
