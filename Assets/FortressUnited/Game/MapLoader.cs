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

        [Tooltip("Swap old textures for the modern material library (WorldMaterials); off shows the originals.")]
        public bool UseWorldMaterials = true;

        public BspFile Bsp { get; private set; }
        /// <summary>A map texture's pixels (from the map or its WADs), or null.</summary>
        public MipTexture TextureData(int tex) => textures?.Get(tex);
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
            TJunctions.Fix(Bsp);       // close the hairline cracks between polygons
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
            BuildModel(0, worldRoot.transform, solid: true);

            var entityRoot = new GameObject("Entities");
            entityRoot.transform.SetParent(transform, false);
            foreach (var b in World.Brushes)
            {
                if (InvisibleClasses.Contains(b.ClassName))
                    continue;
                var go = new GameObject($"{b.ClassName} *{b.Model}");
                go.transform.SetParent(entityRoot.transform, false);
                go.transform.localPosition = GoldSrcSpace.ToUnity(b.Origin);
                BuildModel(b.Model, go.transform, b.Solid);
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
        /// <param name="solid">Adds mesh colliders, for physics objects such as bodies (players use the BSP hulls).</param>
        void BuildModel(int model, Transform parent, bool solid)
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
                // Water, slime and lava ('!' textures) aren't solid
                if (solid && !Bsp.Textures[kv.Key].Name.StartsWith("!"))
                    go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
        }

        Mesh BuildMesh(List<BspFile.Face> faces, int tex)
        {
            var texture = Bsp.Textures[tex];
            float w = Math.Max(1, texture.Width), h = Math.Max(1, texture.Height);
            // Logos are fitted to each face (centred, aspect kept) rather than tiled the old way
            string look = UseWorldMaterials ? WorldMaterials.ModeOf(texture.Name) : null;
            bool fit = look == "logo";
            bool stretch = look == "light" || look == "sign";      // light panels and signs fill their face
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var sizes = new List<Vector2>();      // each face's narrower width (m), for the surface shader
            var tris = new List<int>();
            var logoBounds = fit || stretch ? LogoGroups(faces, w, h) : null;

            bool seeThrough = texture.Name.StartsWith("{");
            // Lights, signs and logos are often thin brushes laid flush on a wall or ceiling;
            // drawn at the same depth they flicker against it, so they sit 4 mm proud
            string mode = UseWorldMaterials ? WorldMaterials.ModeOf(texture.Name) : null;
            bool decal = mode == "light" || mode == "sign" || mode == "logo";
            for (int fi = 0; fi < faces.Count; fi++)
            {
                var f = faces[fi];
                // The thin edges of a see-through grate slab show stray bits of its pattern
                // flush against whatever it meets (window bars flickered at the lintel)
                if (seeThrough && NarrowWidth(f) < 0.1f)
                    continue;
                var ti = Bsp.TexInfos[f.TexInfo];
                var plane = Bsp.Planes[f.Plane];
                Vec3 n = f.BackSide ? -plane.Normal : plane.Normal;
                Vector3 un = GoldSrcSpace.DirectionToUnity(n);
                int first = verts.Count;
                foreach (var p in f.Points)
                {
                    verts.Add(GoldSrcSpace.ToUnity(p) + (decal ? un * 0.004f : Vector3.zero));
                    norms.Add(un);
                    float u = (Vec3.Dot(p, ti.S) + ti.SOffset) / w;
                    float v = (Vec3.Dot(p, ti.T) + ti.TOffset) / h;
                    uvs.Add(new Vector2(u, v));
                }
                if (fit)
                    FitUVs(uvs, first, logoBounds[fi]);
                else if (stretch)
                    StretchUVs(uvs, first, logoBounds[fi]);
                // How wide the face is across its narrower direction: small faces (window
                // reveals, trims, sills) get a plain finish rather than shrunken detail
                Vector3 axisA = Vector3.Cross(un, Mathf.Abs(un.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 axisB = Vector3.Cross(un, axisA);
                float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
                for (int k = first; k < verts.Count; k++)
                {
                    float a = Vector3.Dot(verts[k], axisA), bb = Vector3.Dot(verts[k], axisB);
                    minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
                    minB = Mathf.Min(minB, bb); maxB = Mathf.Max(maxB, bb);
                }
                float narrow = Mathf.Min(maxA - minA, maxB - minB);
                for (int k = first; k < verts.Count; k++)
                    sizes.Add(new Vector2(narrow, 0));
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
            mesh.SetUVs(1, sizes);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();     // for the materials' normal and detail maps
            return mesh;
        }

        /// <summary>A face's width across its narrower direction (m).</summary>
        float NarrowWidth(BspFile.Face f)
        {
            var plane = Bsp.Planes[f.Plane];
            Vector3 un = GoldSrcSpace.DirectionToUnity(plane.Normal);
            Vector3 a = Vector3.Cross(un, Mathf.Abs(un.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 b = Vector3.Cross(un, a);
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            foreach (var p in f.Points)
            {
                var u = GoldSrcSpace.ToUnity(p);
                minA = Mathf.Min(minA, Vector3.Dot(u, a)); maxA = Mathf.Max(maxA, Vector3.Dot(u, a));
                minB = Mathf.Min(minB, Vector3.Dot(u, b)); maxB = Mathf.Max(maxB, Vector3.Dot(u, b));
            }
            return Mathf.Min(maxA - minA, maxB - minB);
        }

        /// <summary>
        /// A see-through texture's clear pixels take the average colour of its solid ones
        /// (they were the key colour, usually pure blue, which bled into the edges as a
        /// coloured outline once the texture is filtered and mipmapped).
        /// </summary>
        static byte[] FillTransparent(byte[] rgba)
        {
            var copy = (byte[])rgba.Clone();
            long r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i + 3 < copy.Length; i += 4)
                if (copy[i + 3] > 0) { r += copy[i]; g += copy[i + 1]; b += copy[i + 2]; n++; }
            if (n == 0) return copy;
            byte ar = (byte)(r / n), ag = (byte)(g / n), ab = (byte)(b / n);
            for (int i = 0; i + 3 < copy.Length; i += 4)
                if (copy[i + 3] == 0) { copy[i] = ar; copy[i + 1] = ag; copy[i + 2] = ab; }
            return copy;
        }

        /// <summary>
        /// The UV area each logo face belongs to: the compiler may split one logo
        /// surface into several polygons, so pieces on the same plane whose texture
        /// areas touch are joined and the logo is fitted once across them all.
        /// </summary>
        Rect[] LogoGroups(List<BspFile.Face> faces, float w, float h)
        {
            int n = faces.Count;
            var rects = new Rect[n];
            for (int i = 0; i < n; i++)
            {
                var ti = Bsp.TexInfos[faces[i].TexInfo];
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                foreach (var p in faces[i].Points)
                {
                    var uv = new Vector2((Vec3.Dot(p, ti.S) + ti.SOffset) / w, (Vec3.Dot(p, ti.T) + ti.TOffset) / h);
                    min = Vector2.Min(min, uv); max = Vector2.Max(max, uv);
                }
                rects[i] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (faces[i].Plane != faces[j].Plane || faces[i].BackSide != faces[j].BackSide) continue;
                    var a = rects[i]; var b = rects[j];
                    if (a.xMin <= b.xMax + 0.02f && b.xMin <= a.xMax + 0.02f && a.yMin <= b.yMax + 0.02f && b.yMin <= a.yMax + 0.02f)
                        parent[Root(i)] = Root(j);
                }
            var group = new Dictionary<int, Rect>();
            for (int i = 0; i < n; i++)
            {
                int r = Root(i);
                group[r] = group.TryGetValue(r, out var g)
                    ? Rect.MinMaxRect(Mathf.Min(g.xMin, rects[i].xMin), Mathf.Min(g.yMin, rects[i].yMin), Mathf.Max(g.xMax, rects[i].xMax), Mathf.Max(g.yMax, rects[i].yMax))
                    : rects[i];
            }
            var result = new Rect[n];
            for (int i = 0; i < n; i++) result[i] = group[Root(i)];
            return result;
        }

        /// <summary>Maps one face's UVs (from first on) so its group's area fills 0..1 on both axes.</summary>
        static void StretchUVs(List<Vector2> uvs, int first, Rect area)
        {
            Vector2 size = area.max - area.min;
            if (size.x <= 0 || size.y <= 0) return;
            for (int k = first; k < uvs.Count; k++)
                uvs[k] = new Vector2((uvs[k].x - area.xMin) / size.x, (uvs[k].y - area.yMin) / size.y);
        }

        /// <summary>Rescales one face's UVs (from first on) so its logo area maps to 0..1 around the middle, keeping the aspect.</summary>
        static void FitUVs(List<Vector2> uvs, int first, Rect area)
        {
            Vector2 min = area.min, max = area.max;
            Vector2 size = max - min, mid = (min + max) * 0.5f;
            float s = Math.Max(size.x, size.y);
            if (s <= 0) return;
            for (int k = first; k < uvs.Count; k++)
                uvs[k] = (uvs[k] - mid) / s + new Vector2(0.5f, 0.5f);
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
                t2d.SetPixelData(mip.Transparent ? FillTransparent(mip.Rgba) : mip.Rgba, 0);
                t2d.Apply(true, true);
                mat.SetTexture("_BaseMap", t2d);
                mat.mainTexture = t2d;
                if (mip.Transparent)
                {
                    mat.SetFloat("_AlphaClip", 1);
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cull", (float)CullMode.Off);
                    // Alpha to coverage: cut-out edges (grates, bars) are smoothed by MSAA
                    // rather than popping pixel by pixel as the view moves
                    mat.SetFloat("_AlphaToMask", 1);
                    if (UseWorldMaterials)
                    {
                        // See-through grates and bars in one dark gunmetal: the old textures'
                        // light-grey bar ends showed as specks where bars meet a lintel
                        mat.SetColor("_BaseColor", new Color(0.13f, 0.135f, 0.15f));
                    }
                }
            }
            else
            {
                // Missing texture: a neutral grey so the geometry still reads
                mat.color = new Color(0.55f, 0.55f, 0.55f);
            }
            mat.SetFloat("_Smoothness", 0.15f);
            // Modern material for this texture, if the table and library have one
            if (UseWorldMaterials)
                WorldMaterials.Apply(mat, Bsp.Textures[tex].Name, mat.GetTexture("_BaseMap") as Texture2D, mip?.Rgba,
                    Math.Max(1, Bsp.Textures[tex].Width), Math.Max(1, Bsp.Textures[tex].Height));
            materials[tex] = mat;
            return mat;
        }
    }
}
