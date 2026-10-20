using System;
using System.Collections.Generic;
using System.IO;
using FortressUnited.Core.Maps;
using FortressUnited.Core.Movement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// Loads a compiled map (.bsp) at runtime for the greybox: one mesh per
    /// texture for the world, child objects for brush entities (doors,
    /// walls, water), markers for point entities, and the BspWorld that
    /// gameplay collides with. Textures come from the map itself or from
    /// WAD files in the search folders; nothing is copied into the project.
    /// </summary>
    public sealed class MapLoader : MonoBehaviour
    {
        [Tooltip("Path to a .bsp file. Leave empty to use the last map opened from the Fortress United menu.")]
        public string MapPath = "";

        [Tooltip("Folders searched for .wad texture files (the map's own folder's parent is added automatically).")]
        public List<string> WadFolders = new List<string>();

        public BspFile Bsp { get; private set; }
        public BspWorld World { get; private set; }

        /// <summary>PlayerPrefs key for the last map opened from the editor menu.</summary>
        public const string LastMapKey = "FortressUnited.LastMap";

        // Faces that are never drawn
        static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sky", "aaatrigger", "trigger", "clip", "clipbevel", "origin", "null", "skip", "hint", "bevel",
        };

        // Brush entities that aren't drawn
        static readonly HashSet<string> InvisibleClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "trigger_multiple", "trigger_once", "trigger_push", "trigger_hurt", "trigger_teleport",
            "trigger_changelevel", "trigger_gravity", "trigger_transition", "func_ladder", "func_nobuild",
            "func_nogrenades", "func_spawnguard", "i_t_g", "info_tfgoal", "func_friction",
        };

        readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        TextureSource textures;

        public bool Load()
        {
            string path = string.IsNullOrEmpty(MapPath) ? PlayerPrefs.GetString(LastMapKey, "") : MapPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"Fortress United: no map to load ('{path}'). Use Fortress United > Open Map...");
                return false;
            }

            Bsp = BspFile.Load(path);
            World = new BspWorld(Bsp);

            var folders = new List<string>(WadFolders);
            string mapsDir = Path.GetDirectoryName(path);
            string gameDir = Path.GetDirectoryName(mapsDir);
            if (gameDir != null)
            {
                folders.Add(gameDir);
                string root = Path.GetDirectoryName(gameDir);
                if (root != null)
                    folders.Add(Path.Combine(root, "valve"));
            }
            textures = new TextureSource(Bsp, folders);

            var worldRoot = new GameObject("World");
            worldRoot.transform.SetParent(transform, false);
            BuildModel(0, worldRoot.transform);

            var entityRoot = new GameObject("Entities");
            entityRoot.transform.SetParent(transform, false);
            foreach (var b in World.Brushes)
            {
                if (InvisibleClasses.Contains(b.ClassName))
                    continue;
                var go = new GameObject($"{b.ClassName} *{b.Model}");
                go.transform.SetParent(entityRoot.transform, false);
                go.transform.localPosition = GoldSrcSpace.ToUnity(b.Origin);
                BuildModel(b.Model, go.transform);
            }

            foreach (var e in Bsp.Entities)
            {
                string cls = e.ClassName;
                if (cls == "worldspawn" || e.Get("model").StartsWith("*"))
                    continue;
                var marker = new GameObject(cls);
                marker.transform.SetParent(entityRoot.transform, false);
                marker.transform.localPosition = GoldSrcSpace.ToUnity(ParseVec(e.Get("origin")));
                var info = marker.AddComponent<EntityInfo>();
                info.ClassName = cls;
                foreach (var k in e.KeyOrder)
                    info.Keys.Add($"{k} = {e.Properties[k]}");
            }

            Debug.Log($"Fortress United: loaded {Path.GetFileName(path)} ({Bsp.Faces.Length} faces, {Bsp.Entities.Count} entities)");
            return true;
        }

        static Vec3 ParseVec(string s)
        {
            var p = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return Vec3.Zero;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            float.TryParse(p[0], System.Globalization.NumberStyles.Float, c, out float x);
            float.TryParse(p[1], System.Globalization.NumberStyles.Float, c, out float y);
            float.TryParse(p[2], System.Globalization.NumberStyles.Float, c, out float z);
            return new Vec3(x, y, z);
        }

        /// <summary>Builds one mesh per texture for a BSP model under parent.</summary>
        void BuildModel(int model, Transform parent)
        {
            var m = Bsp.Models[model];
            var byTexture = new Dictionary<int, List<BspFile.Face>>();
            for (int i = 0; i < m.NumFaces; i++)
            {
                var f = Bsp.Faces[m.FirstFace + i];
                if (f.TexInfo < 0 || f.TexInfo >= Bsp.TexInfos.Length)
                    continue;
                int tex = Bsp.TexInfos[f.TexInfo].Texture;
                if (tex < 0 || tex >= Bsp.Textures.Length || Hidden.Contains(Bsp.Textures[tex].Name))
                    continue;
                if (!byTexture.TryGetValue(tex, out var list))
                    byTexture[tex] = list = new List<BspFile.Face>();
                list.Add(f);
            }

            foreach (var kv in byTexture)
            {
                var go = new GameObject(Bsp.Textures[kv.Key].Name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = BuildMesh(kv.Value, kv.Key);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialFor(kv.Key);
                mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            }
        }

        Mesh BuildMesh(List<BspFile.Face> faces, int tex)
        {
            var texture = Bsp.Textures[tex];
            float w = Math.Max(1, texture.Width), h = Math.Max(1, texture.Height);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            foreach (var f in faces)
            {
                var ti = Bsp.TexInfos[f.TexInfo];
                var plane = Bsp.Planes[f.Plane];
                Vec3 n = f.BackSide ? -plane.Normal : plane.Normal;
                Vector3 un = GoldSrcSpace.DirectionToUnity(n);
                int first = verts.Count;
                foreach (var p in f.Points)
                {
                    verts.Add(GoldSrcSpace.ToUnity(p));
                    norms.Add(un);
                    float u = (Vec3.Dot(p, ti.S) + ti.SOffset) / w;
                    float v = (Vec3.Dot(p, ti.T) + ti.TOffset) / h;
                    uvs.Add(new Vector2(u, v));
                }
                // Fan triangulation; wind so the front faces the normal in Unity space
                for (int k = 1; k + 1 < f.Points.Length; k++)
                {
                    Vector3 a = verts[first], b = verts[first + k], c = verts[first + k + 1];
                    // Unity: a triangle faces where Cross(b - a, c - a) points (clockwise seen from the front)
                    bool facing = Vector3.Dot(Vector3.Cross(b - a, c - a), un) > 0;
                    if (facing)
                    {
                        tris.Add(first); tris.Add(first + k); tris.Add(first + k + 1);
                    }
                    else
                    {
                        tris.Add(first); tris.Add(first + k + 1); tris.Add(first + k);
                    }
                }
            }

            var mesh = new Mesh { indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        Material MaterialFor(int tex)
        {
            if (materials.TryGetValue(tex, out var mat))
                return mat;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = Bsp.Textures[tex].Name };
            var mip = textures.Get(tex);
            if (mip != null)
            {
                var t2d = new Texture2D(mip.Width, mip.Height, TextureFormat.RGBA32, true)
                {
                    name = mip.Name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = 4,
                };
                t2d.SetPixelData(mip.Rgba, 0);
                t2d.Apply(true, true);
                mat.SetTexture("_BaseMap", t2d);
                mat.mainTexture = t2d;
                if (mip.Transparent)
                {
                    mat.SetFloat("_AlphaClip", 1);
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cull", (float)CullMode.Off);
                }
            }
            else
            {
                // Missing texture: a neutral grey so the geometry still reads
                mat.color = new Color(0.55f, 0.55f, 0.55f);
            }
            mat.SetFloat("_Smoothness", 0.15f);
            materials[tex] = mat;
            return mat;
        }
    }
}
