using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A flag's holographic banner showing the team logo. Builds its own
    /// pennant mesh (u = 0 along the pole, v = 0 at the bottom) unless it
    /// is put on an existing banner mesh with UVs laid out the same way.
    /// The logo comes from TeamColours.Logo, so swapping the logo file
    /// changes every banner.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class HologramBanner : MonoBehaviour
    {
        public bool Red;
        Material material;
        static readonly int NowId = Shader.PropertyToID("_Now");

        /// <summary>A new banner hanging from a pole: width out from the pole (+X), height down from the top (-Y).</summary>
        public static HologramBanner Create(Transform parent, Vector3 topAtPole, float width, float height, bool red)
        {
            var go = new GameObject("Banner", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = topAtPole;
            go.GetComponent<MeshFilter>().sharedMesh = Pennant(width, height, 16, 20);
            var b = go.AddComponent<HologramBanner>();
            b.Set(red);
            return b;
        }

        public void Set(bool red)
        {
            Red = red;
            if (material == null)
            {
                material = new Material(Shader.Find("FortressUnited/HologramBanner")) { name = "HologramBanner" };
                var r = GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            material.SetColor("_Color", TeamColours.Glow(TeamColours.For(red), 1.3f));
            material.SetColor("_Core", TeamColours.Glow(TeamColours.CoreFor(red), 1.2f));
            material.SetTexture("_Logo", TeamColours.Logo(red));
        }

        void Update()
        {
            if (material != null)
                material.SetFloat(NowId, Time.time);
        }

        /// <summary>A flat grid (in the XY plane, facing -Z) for the pennant; the V is cut in the shader.</summary>
        static Mesh Pennant(float width, float height, int cols, int rows)
        {
            var verts = new Vector3[(cols + 1) * (rows + 1)];
            var uvs = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= cols; x++)
                {
                    int i = y * (cols + 1) + x;
                    float u = (float)x / cols, v = (float)y / rows;
                    verts[i] = new Vector3(u * width, (v - 1) * height, 0);
                    uvs[i] = new Vector2(u, v);
                    normals[i] = Vector3.back;
                }
            var tris = new int[cols * rows * 6];
            int t = 0;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int i = y * (cols + 1) + x;
                    tris[t++] = i; tris[t++] = i + cols + 1; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + cols + 1; tris[t++] = i + cols + 2;
                }
            var mesh = new Mesh { name = "Pennant", vertices = verts, uv = uvs, normals = normals, triangles = tris };
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 0.2f);   // room for the wave
            return mesh;
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }
}
