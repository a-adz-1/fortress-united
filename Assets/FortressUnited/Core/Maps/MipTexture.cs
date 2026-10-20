using System;
using System.Collections.Generic;
using System.IO;

namespace FortressUnited.Core.Maps
{
    /// <summary>
    /// A decoded GoldSrc texture: 8-bit palette image turned into RGBA32.
    /// Rows are top to bottom as stored; with GoldSrc texture coordinates
    /// (v grows downwards) the rows can be uploaded to Unity as-is.
    /// </summary>
    public sealed class MipTexture
    {
        public string Name;
        public int Width, Height;
        /// <summary>RGBA, 4 bytes per pixel, Width * Height pixels.</summary>
        public byte[] Rgba;
        /// <summary>True for "{" textures, where palette index 255 is see-through.</summary>
        public bool Transparent;

        /// <summary>
        /// Decodes a miptex structure (as stored in BSPs and WADs) starting at
        /// offset. Returns null if it has no pixel data (an external texture).
        /// </summary>
        public static MipTexture Decode(byte[] data, int offset, int length)
        {
            if (length < 40 || offset < 0 || offset + 40 > data.Length)
                return null;
            string name = ReadName(data, offset, 16);
            int width = BitConverter.ToInt32(data, offset + 16);
            int height = BitConverter.ToInt32(data, offset + 20);
            int mip0 = BitConverter.ToInt32(data, offset + 24);
            int mip3 = BitConverter.ToInt32(data, offset + 36);
            if (mip0 == 0 || width <= 0 || height <= 0 || width > 4096 || height > 4096)
                return null;

            int pixels = width * height;
            // Palette: after the smallest mip level, a 2-byte count then 256 RGB triples
            int palette = offset + mip3 + (width / 8) * (height / 8) + 2;
            if (offset + mip0 + pixels > data.Length || palette + 768 > data.Length)
                return null;

            bool transparent = name.StartsWith("{");
            var rgba = new byte[pixels * 4];
            int src = offset + mip0;
            for (int i = 0; i < pixels; i++)
            {
                int idx = data[src + i];
                int p = palette + idx * 3;
                rgba[i * 4] = data[p];
                rgba[i * 4 + 1] = data[p + 1];
                rgba[i * 4 + 2] = data[p + 2];
                rgba[i * 4 + 3] = transparent && idx == 255 ? (byte)0 : (byte)255;
            }
            return new MipTexture { Name = name, Width = width, Height = height, Rgba = rgba, Transparent = transparent };
        }

        internal static string ReadName(byte[] d, int o, int max)
        {
            int n = 0;
            while (n < max && o + n < d.Length && d[o + n] != 0) n++;
            return Latin1.GetString(d, o, n);
        }
    }

    /// <summary>
    /// Finds a map's textures the way the engine does: embedded ones first,
    /// then the WADs in the order the map lists them (worldspawn "wad" key),
    /// then any other WAD found in the search folders.
    /// </summary>
    public sealed class TextureSource
    {
        readonly BspFile bsp;
        readonly List<WadFile> wads = new List<WadFile>();

        public TextureSource(BspFile bsp, IEnumerable<string> searchDirs)
        {
            this.bsp = bsp;
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                // Case-insensitive: TFC ships "tfc.WAD", and Linux file names are case-sensitive
                foreach (var f in Directory.GetFiles(dir))
                {
                    if (!f.EndsWith(".wad", StringComparison.OrdinalIgnoreCase)) continue;
                    string n = System.IO.Path.GetFileName(f);
                    if (!byName.ContainsKey(n)) byName[n] = f;
                }
            }
            var order = new List<string>();
            foreach (var w in bsp.WadFiles())
                if (byName.TryGetValue(w, out var f) && !order.Contains(f)) order.Add(f);
            foreach (var f in byName.Values)
                if (!order.Contains(f)) order.Add(f);
            foreach (var f in order)
            {
                try { wads.Add(WadFile.Load(f)); }
                catch (Exception) { /* not a WAD3: skip */ }
            }
        }

        /// <summary>The texture's pixels, or null if it can't be found.</summary>
        public MipTexture Get(int textureIndex)
        {
            var t = bsp.DecodeTexture(textureIndex);
            if (t != null) return t;
            string name = bsp.Textures[textureIndex].Name;
            foreach (var w in wads)
            {
                var m = w.Decode(name);
                if (m != null) return m;
            }
            return null;
        }
    }

    /// <summary>
    /// A WAD3 texture package (Half-Life's .wad files). Textures are looked
    /// up by name, case-insensitively, and decoded on demand.
    /// </summary>
    public sealed class WadFile
    {
        readonly byte[] data;
        readonly Dictionary<string, (int Offset, int Size)> lumps =
            new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

        public string Path { get; private set; }
        public IEnumerable<string> Names => lumps.Keys;

        public static WadFile Load(string path)
        {
            var w = new WadFile(File.ReadAllBytes(path)) { Path = path };
            return w;
        }

        public WadFile(byte[] bytes)
        {
            data = bytes;
            if (data.Length < 12 || data[0] != 'W' || data[1] != 'A' || data[2] != 'D' || data[3] != '3')
                throw new InvalidDataException("not a WAD3 file");
            int count = BitConverter.ToInt32(data, 4);
            int table = BitConverter.ToInt32(data, 8);
            for (int i = 0; i < count; i++)
            {
                int e = table + i * 32;
                if (e + 32 > data.Length)
                    break;
                int pos = BitConverter.ToInt32(data, e);
                int size = BitConverter.ToInt32(data, e + 4);
                byte type = data[e + 12];
                byte compression = data[e + 13];
                if (type != 0x43 || compression != 0) // miptex, uncompressed
                    continue;
                string name = MipTexture.ReadName(data, e + 16, 16);
                lumps[name] = (pos, size);
            }
        }

        public bool Contains(string name) => lumps.ContainsKey(name);

        public MipTexture Decode(string name) =>
            lumps.TryGetValue(name, out var l) ? MipTexture.Decode(data, l.Offset, l.Size) : null;
    }
}
