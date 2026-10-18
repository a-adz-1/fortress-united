using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FortressUnited.Core.Movement;

namespace FortressUnited.Core.Maps
{
    /// <summary>
    /// Reads GoldSrc (Half-Life / TFC) compiled maps: BSP version 30.
    /// Gives the entities, every face as a polygon with its texture and
    /// texture axes, the texture list (embedded or from WADs), and the brush
    /// models (model 0 is the world; "*1", "*2"... are doors, triggers, water
    /// and other brush entities). Engine-free: the Unity importer builds
    /// meshes from this.
    /// </summary>
    public sealed class BspFile
    {
        public const int Version = 30;

        enum Lump
        {
            Entities, Planes, Textures, Vertices, Visibility, Nodes, TexInfo, Faces,
            Lighting, ClipNodes, Leaves, MarkSurfaces, Edges, SurfEdges, Models,
        }
        const int LumpCount = 15;

        public struct Plane
        {
            public Vec3 Normal;
            public float Dist;
            public int Type;
        }

        public sealed class Texture
        {
            public string Name;
            public int Width, Height;
            /// <summary>True if the pixels are inside the BSP (not from a WAD).</summary>
            public bool Embedded;
        }

        public struct TexInfo
        {
            /// <summary>u = dot(pos, S) + SOffset, v = dot(pos, T) + TOffset (in texels).</summary>
            public Vec3 S, T;
            public float SOffset, TOffset;
            public int Texture;
            public int Flags;
        }

        public sealed class Face
        {
            public int Plane;
            public bool BackSide;
            public int TexInfo;
            public int LightOffset;
            public byte[] Styles = new byte[4];
            /// <summary>Polygon vertices in order.</summary>
            public Vec3[] Points;
            /// <summary>Brush model this face belongs to (0 = world).</summary>
            public int Model;
        }

        public struct Leaf
        {
            public int Contents;
            public short[] Mins, Maxs;
        }

        public sealed class Model
        {
            public Vec3 Mins, Maxs, Origin;
            public int FirstFace, NumFaces;
        }

        public string EntityText;
        public List<MapEntity> Entities;
        public Plane[] Planes;
        public Texture[] Textures;
        public Vec3[] Vertices;
        public TexInfo[] TexInfos;
        public Face[] Faces;
        public Leaf[] Leaves;
        public Model[] Models;

        public static BspFile Load(string path) => Load(File.ReadAllBytes(path));

        public static BspFile Load(byte[] data)
        {
            var r = new Reader(data);
            int version = r.Int(0);
            if (version != Version)
                throw new InvalidDataException($"not a GoldSrc BSP (version {version}, expected {Version})");

            var ofs = new int[LumpCount];
            var len = new int[LumpCount];
            for (int i = 0; i < LumpCount; i++)
            {
                ofs[i] = r.Int(4 + i * 8);
                len[i] = r.Int(8 + i * 8);
                if (ofs[i] < 0 || len[i] < 0 || ofs[i] + (long)len[i] > data.Length)
                    throw new InvalidDataException($"lump {(Lump)i} out of range");
            }

            var bsp = new BspFile();

            // Entities: text, NUL terminated
            int entLen = len[(int)Lump.Entities];
            int end = Array.IndexOf<byte>(data, 0, ofs[(int)Lump.Entities], entLen);
            if (end < 0) end = ofs[(int)Lump.Entities] + entLen;
            bsp.EntityText = Latin1.GetString(data, ofs[(int)Lump.Entities], end - ofs[(int)Lump.Entities]);
            bsp.Entities = MapEntity.ParseList(bsp.EntityText);

            // Planes (20 bytes)
            int o = ofs[(int)Lump.Planes];
            bsp.Planes = new Plane[len[(int)Lump.Planes] / 20];
            for (int i = 0; i < bsp.Planes.Length; i++, o += 20)
                bsp.Planes[i] = new Plane { Normal = r.Vec(o), Dist = r.Float(o + 12), Type = r.Int(o + 16) };

            // Textures: count, offsets, then miptex headers (name[16], width, height, 4 mip offsets)
            o = ofs[(int)Lump.Textures];
            if (len[(int)Lump.Textures] >= 4)
            {
                int count = r.Int(o);
                bsp.Textures = new Texture[count];
                for (int i = 0; i < count; i++)
                {
                    int mo = r.Int(o + 4 + i * 4);
                    if (mo < 0)
                    {
                        bsp.Textures[i] = new Texture { Name = "" };
                        continue;
                    }
                    int t = o + mo;
                    bsp.Textures[i] = new Texture
                    {
                        Name = r.Name(t, 16),
                        Width = r.Int(t + 16),
                        Height = r.Int(t + 20),
                        Embedded = r.Int(t + 24) != 0,
                    };
                }
            }
            else
            {
                bsp.Textures = new Texture[0];
            }

            // Vertices (12 bytes)
            o = ofs[(int)Lump.Vertices];
            bsp.Vertices = new Vec3[len[(int)Lump.Vertices] / 12];
            for (int i = 0; i < bsp.Vertices.Length; i++, o += 12)
                bsp.Vertices[i] = r.Vec(o);

            // Texinfo (40 bytes)
            o = ofs[(int)Lump.TexInfo];
            bsp.TexInfos = new TexInfo[len[(int)Lump.TexInfo] / 40];
            for (int i = 0; i < bsp.TexInfos.Length; i++, o += 40)
                bsp.TexInfos[i] = new TexInfo
                {
                    S = r.Vec(o), SOffset = r.Float(o + 12),
                    T = r.Vec(o + 16), TOffset = r.Float(o + 28),
                    Texture = r.Int(o + 32), Flags = r.Int(o + 36),
                };

            // Edges (2 x ushort) and surfedges (int)
            o = ofs[(int)Lump.Edges];
            int edgeCount = len[(int)Lump.Edges] / 4;
            var edges = new int[edgeCount, 2];
            for (int i = 0; i < edgeCount; i++, o += 4)
            {
                edges[i, 0] = r.UShort(o);
                edges[i, 1] = r.UShort(o + 2);
            }
            o = ofs[(int)Lump.SurfEdges];
            var surfEdges = new int[len[(int)Lump.SurfEdges] / 4];
            for (int i = 0; i < surfEdges.Length; i++, o += 4)
                surfEdges[i] = r.Int(o);

            // Faces (20 bytes): rebuild polygons from the edge loop
            o = ofs[(int)Lump.Faces];
            bsp.Faces = new Face[len[(int)Lump.Faces] / 20];
            for (int i = 0; i < bsp.Faces.Length; i++, o += 20)
            {
                var f = new Face
                {
                    Plane = r.UShort(o),
                    BackSide = r.Short(o + 2) != 0,
                    TexInfo = r.Short(o + 10),
                    LightOffset = r.Int(o + 16),
                };
                int firstEdge = r.Int(o + 4);
                int numEdges = r.Short(o + 8);
                for (int k = 0; k < 4; k++)
                    f.Styles[k] = data[o + 12 + k];
                f.Points = new Vec3[numEdges];
                for (int k = 0; k < numEdges; k++)
                {
                    int se = surfEdges[firstEdge + k];
                    int v = se >= 0 ? edges[se, 0] : edges[-se, 1];
                    f.Points[k] = bsp.Vertices[v];
                }
                bsp.Faces[i] = f;
            }

            // Leaves (28 bytes): contents for water/slime/lava/sky volumes
            o = ofs[(int)Lump.Leaves];
            bsp.Leaves = new Leaf[len[(int)Lump.Leaves] / 28];
            for (int i = 0; i < bsp.Leaves.Length; i++, o += 28)
                bsp.Leaves[i] = new Leaf
                {
                    Contents = r.Int(o),
                    Mins = new[] { r.Short(o + 8), r.Short(o + 10), r.Short(o + 12) },
                    Maxs = new[] { r.Short(o + 14), r.Short(o + 16), r.Short(o + 18) },
                };

            // Models (64 bytes)
            o = ofs[(int)Lump.Models];
            bsp.Models = new Model[len[(int)Lump.Models] / 64];
            for (int i = 0; i < bsp.Models.Length; i++, o += 64)
            {
                var m = new Model
                {
                    Mins = r.Vec(o), Maxs = r.Vec(o + 12), Origin = r.Vec(o + 24),
                    FirstFace = r.Int(o + 56), NumFaces = r.Int(o + 60),
                };
                bsp.Models[i] = m;
                for (int k = 0; k < m.NumFaces; k++)
                {
                    int fi = m.FirstFace + k;
                    if (fi >= 0 && fi < bsp.Faces.Length)
                        bsp.Faces[fi].Model = i;
                }
            }

            return bsp;
        }

        /// <summary>Texture name of a face, or "" if it has none.</summary>
        public string FaceTexture(Face f)
        {
            if (f.TexInfo < 0 || f.TexInfo >= TexInfos.Length)
                return "";
            int t = TexInfos[f.TexInfo].Texture;
            return t >= 0 && t < Textures.Length ? Textures[t].Name : "";
        }

        /// <summary>Area of a face polygon (map units squared).</summary>
        public static float Area(Vec3[] points)
        {
            if (points.Length < 3)
                return 0;
            Vec3 sum = Vec3.Zero;
            for (int i = 1; i + 1 < points.Length; i++)
                sum = sum + Vec3.Cross(points[i] - points[0], points[i + 1] - points[0]);
            return sum.Length() * 0.5f;
        }

        /// <summary>WAD files the map uses, from worldspawn's "wad" key (file names only).</summary>
        public List<string> WadFiles()
        {
            var list = new List<string>();
            var world = Entities.Count > 0 ? Entities[0] : null;
            if (world == null || !world.Properties.TryGetValue("wad", out string wads))
                return list;
            foreach (var part in wads.Split(';'))
            {
                string p = part.Trim().Replace('\\', '/');
                if (p.Length == 0) continue;
                int slash = p.LastIndexOf('/');
                list.Add(slash >= 0 ? p.Substring(slash + 1) : p);
            }
            return list;
        }

        /// <summary>Little-endian reads with bounds checks.</summary>
        sealed class Reader
        {
            readonly byte[] d;
            public Reader(byte[] data) { d = data; }
            void Check(int o, int n)
            {
                if (o < 0 || o + n > d.Length)
                    throw new InvalidDataException("BSP truncated");
            }
            public int Int(int o) { Check(o, 4); return BitConverter.ToInt32(d, o); }
            public short Short(int o) { Check(o, 2); return BitConverter.ToInt16(d, o); }
            public int UShort(int o) { Check(o, 2); return BitConverter.ToUInt16(d, o); }
            public float Float(int o) { Check(o, 4); return BitConverter.ToSingle(d, o); }
            public Vec3 Vec(int o) => new Vec3(Float(o), Float(o + 4), Float(o + 8));
            public string Name(int o, int max)
            {
                Check(o, max);
                int n = 0;
                while (n < max && d[o + n] != 0) n++;
                return Latin1.GetString(d, o, n);
            }
        }
    }
}
