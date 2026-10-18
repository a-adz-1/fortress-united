using System.Collections.Generic;
using System.Text;

namespace FortressUnited.Core.Maps
{
    /// <summary>
    /// One map entity: its keyvalues (classname, origin, targetname,
    /// goal_no...) and, for .map sources, its brushes. The same text format is
    /// used inside compiled BSPs and in .map files.
    /// </summary>
    public sealed class MapEntity
    {
        /// <summary>Keyvalues in file order (keys are unique; a later duplicate wins).</summary>
        public readonly Dictionary<string, string> Properties = new Dictionary<string, string>();
        public readonly List<string> KeyOrder = new List<string>();
        public readonly List<MapBrush> Brushes = new List<MapBrush>();

        public string ClassName => Get("classname");

        public string Get(string key, string fallback = "") =>
            Properties.TryGetValue(key, out var v) ? v : fallback;

        public void Set(string key, string value)
        {
            if (!Properties.ContainsKey(key))
                KeyOrder.Add(key);
            Properties[key] = value;
        }

        /// <summary>Parses entity text: { "key" "value" ... } blocks without brushes.</summary>
        public static List<MapEntity> ParseList(string text)
        {
            var list = new List<MapEntity>();
            var t = new Tokenizer(text);
            while (t.Next(out string tok))
            {
                if (tok != "{")
                    throw new System.FormatException($"expected '{{' at line {t.Line}, got '{tok}'");
                var e = new MapEntity();
                while (t.Next(out tok) && tok != "}")
                {
                    if (!t.Next(out string value))
                        throw new System.FormatException($"missing value for '{tok}' at line {t.Line}");
                    e.Set(tok, value);
                }
                list.Add(e);
            }
            return list;
        }

        /// <summary>Writes entity text in the BSP/.map format (keyvalues only).</summary>
        public static string Write(IEnumerable<MapEntity> entities)
        {
            var sb = new StringBuilder();
            foreach (var e in entities)
            {
                sb.Append("{\n");
                foreach (var k in e.KeyOrder)
                    sb.Append('"').Append(k).Append("\" \"").Append(e.Properties[k]).Append("\"\n");
                sb.Append("}\n");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Tokens of map/entity text: quoted strings, braces, parentheses,
    /// brackets and bare words; // comments are skipped.
    /// </summary>
    sealed class Tokenizer
    {
        readonly string s;
        int i;
        public int Line = 1;

        public Tokenizer(string text) { s = text; }

        public bool Next(out string token)
        {
            token = null;
            while (true)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                {
                    if (s[i] == '\n') Line++;
                    i++;
                }
                if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                break;
            }
            if (i >= s.Length)
                return false;

            char c = s[i];
            if (c == '"')
            {
                int start = ++i;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\n') Line++;
                    i++;
                }
                token = s.Substring(start, i - start);
                if (i < s.Length) i++; // closing quote
                return true;
            }
            if (c == '{' || c == '}' || c == '(' || c == ')' || c == '[' || c == ']')
            {
                i++;
                token = c.ToString();
                return true;
            }
            int b = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '"' && s[i] != '{' && s[i] != '}'
                   && s[i] != '(' && s[i] != ')' && s[i] != '[' && s[i] != ']')
                i++;
            token = s.Substring(b, i - b);
            return true;
        }
    }
}
