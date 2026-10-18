namespace FortressUnited.Core.Maps
{
    /// <summary>
    /// Latin-1 text (one byte per character), as used by GoldSrc map and
    /// entity text. Unity's .NET Standard 2.1 has no Encoding.Latin1.
    /// </summary>
    static class Latin1
    {
        public static string GetString(byte[] data, int offset, int count)
        {
            var chars = new char[count];
            for (int i = 0; i < count; i++)
                chars[i] = (char)data[offset + i];
            return new string(chars);
        }

        public static string GetString(byte[] data) => GetString(data, 0, data.Length);
    }
}
