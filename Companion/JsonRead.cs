using System.Text;

namespace Reflash.Companion
{
    /// <summary>
    /// The little bit of JSON the companion has to READ.
    ///
    /// Everything the game sends out is already JSON built by the apps; what comes back is two shapes only - a
    /// pairing token and a batch of calls. So this is a small hand-written reader for exactly those, not a parser.
    /// A general one would be more code, more surface, and one more thing to be wrong about a payload that arrives
    /// from a device on the network.
    /// </summary>
    internal static class JsonRead
    {
        internal struct Call
        {
            internal int Id;
            internal string App;
            internal string Name;
            internal string Arg;
        }

        /// <summary>A top-level string field. Returns empty rather than throwing - a malformed body is a refusal,
        /// not an exception.</summary>
        internal static string Field(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return "";

            string key = "\"" + name + "\"";
            int at = json.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return "";

            int colon = json.IndexOf(':', at + key.Length);
            if (colon < 0) return "";

            return ReadString(json, colon + 1, out _);
        }

        /// <summary>
        /// A batch of calls: <c>[{"id":1,"app":"reflash-map","n":"reflash-map.state","a":""}, ...]</c>.
        /// Anything that does not fit that shape is skipped rather than failing the whole batch - one bad entry
        /// should not cost the other thirty-nine.
        /// </summary>
        internal static List<Call> ParseCalls(string json)
        {
            var calls = new List<Call>();
            if (string.IsNullOrEmpty(json)) return calls;

            int i = 0;
            while (i < json.Length)
            {
                int open = json.IndexOf('{', i);
                if (open < 0) break;

                int close = json.IndexOf('}', open);
                if (close < 0) break;

                string item = json.Substring(open, close - open + 1);
                i = close + 1;

                var call = new Call
                {
                    App = Field(item, "app"),
                    Name = Field(item, "n"),
                    Arg = Field(item, "a"),
                };

                if (call.App.Length == 0 || call.Name.Length == 0) continue;

                call.Id = ReadInt(item, "id");
                calls.Add(call);
            }

            return calls;
        }

        /// <summary>
        /// The top-level objects of a flat array, each as its own JSON text.
        ///
        /// Enough for the app manifest, which is one level deep by construction - no nested braces, no strings
        /// carrying one. Handed back as text rather than parsed because the only thing done with them is reordering
        /// and re-joining, and a parse-then-rebuild would be a second chance to lose a field.
        /// </summary>
        internal static List<string> Objects(string json)
        {
            var items = new List<string>();
            if (string.IsNullOrEmpty(json)) return items;

            int i = 0;
            while (i < json.Length)
            {
                int open = json.IndexOf('{', i);
                if (open < 0) break;

                int close = json.IndexOf('}', open);
                if (close < 0) break;

                items.Add(json.Substring(open, close - open + 1));
                i = close + 1;
            }

            return items;
        }

        /// <summary>A JSON string literal, escaped. The only writer here - everything else the companion sends is
        /// already-built JSON from the apps.</summary>
        internal static string Quote(string value)
        {
            if (value == null) return "\"\"";

            var sb = new StringBuilder(value.Length + 8).Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static int ReadInt(string json, string name)
        {
            string key = "\"" + name + "\"";
            int at = json.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return 0;

            int colon = json.IndexOf(':', at + key.Length);
            if (colon < 0) return 0;

            int p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t')) p++;

            int start = p;
            while (p < json.Length && (char.IsDigit(json[p]) || json[p] == '-')) p++;

            return int.TryParse(json.Substring(start, p - start), out int value) ? value : 0;
        }

        /// <summary>Reads a quoted string starting at or after <paramref name="from"/>, honouring escapes.</summary>
        private static string ReadString(string json, int from, out int end)
        {
            end = from;

            int p = from;
            while (p < json.Length && json[p] != '"') p++;
            if (p >= json.Length) return "";

            p++;
            var sb = new StringBuilder();

            while (p < json.Length)
            {
                char c = json[p];

                if (c == '\\' && p + 1 < json.Length)
                {
                    char next = json[++p];
                    switch (next)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (p + 4 < json.Length
                                && int.TryParse(json.Substring(p + 1, 4),
                                                System.Globalization.NumberStyles.HexNumber,
                                                System.Globalization.CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                p += 4;
                            }
                            break;
                        default: sb.Append(next); break;
                    }
                    p++;
                    continue;
                }

                if (c == '"') { end = p + 1; return sb.ToString(); }

                sb.Append(c);
                p++;
            }

            end = p;
            return sb.ToString();
        }
    }
}
