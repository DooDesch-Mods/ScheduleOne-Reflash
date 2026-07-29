using System.Text;

namespace Reflash.Wire
{
    /// <summary>
    /// Makes game text safe to draw.
    ///
    /// The renderer uses the game's TextMeshPro atlases, which carry Latin-1 and very little else. Anything above
    /// U+00FF draws as an empty box - and that includes the characters text most often picks up by accident: a
    /// typographic ellipsis, curly quotes, an en dash, an arrow. One of those in a contact name turns a list into a
    /// row of tofu, and it reads as "the mod is broken" rather than "the font is missing a glyph".
    ///
    /// So the substitutions are deliberate rather than a blanket strip: the common ones become their ASCII
    /// equivalents, and anything else unrenderable becomes a question mark, which at least reads as "a character was
    /// here".
    /// </summary>
    internal static class Text
    {
        // Codepoints rather than the characters themselves, so this file stays pure ASCII. A file whose correctness
        // depends on its own encoding is one bad checkout away from replacing an ellipsis with a question mark and
        // calling it a fix.
        private const char Ellipsis0 = (char)0x2026;   // horizontal ellipsis
        private const char SingleQuoteOpen = (char)0x2018;
        private const char SingleQuoteClose = (char)0x2019;
        private const char DoubleQuoteOpen = (char)0x201C;
        private const char DoubleQuoteClose = (char)0x201D;
        private const char EnDash = (char)0x2013;
        private const char EmDash = (char)0x2014;
        private const char NonBreakingSpace = (char)0x00A0;
        private const char Bullet = (char)0x2022;
        private const char ArrowRight = (char)0x2192;
        private const char ArrowLeft = (char)0x2190;
        private const char Multiply = (char)0x00D7;
        private const char Check = (char)0x2713;
        private const char HeavyCheck = (char)0x2714;

        /// <summary>The last codepoint the game's fonts carry.</summary>
        private const char LastDrawable = (char)0x00FF;

        /// <summary>
        /// Text as the phone can actually draw it. Also collapses newlines, which a single-line row cannot show and
        /// which the layout would otherwise turn into an unexpectedly tall box.
        /// </summary>
        internal static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            StringBuilder sb = null;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                string replacement = Replace(c);

                if (replacement == null)
                {
                    sb?.Append(c);
                    continue;
                }

                // Only allocate once something actually needs changing - most strings need nothing.
                sb ??= new StringBuilder(value.Length + 8).Append(value, 0, i);
                sb.Append(replacement);
            }

            return sb?.ToString() ?? value;
        }

        /// <summary>
        /// Shorten to a maximum, ending in three full stops rather than U+2026 - which is exactly the character the
        /// atlas does not have. Done here rather than with `text-overflow: ellipsis`, which sets that character in
        /// TextMeshPro and puts an empty box at the end of every truncated row.
        ///
        /// Counts what the reader sees, not what the string holds. Game text carries TextMeshPro markup - a price is
        /// <c>&lt;color=#46CB4F&gt;$160&lt;/color&gt;</c> - and Sideload renders with <c>richText</c> on, so that
        /// markup is the colour vanilla shows and is worth keeping. Cutting by raw length breaks it twice over: the
        /// tags eat the budget, and a cut that lands inside one leaves TextMeshPro a fragment it cannot parse, which
        /// it then prints verbatim. Any tags still open at the cut are closed here.
        /// </summary>
        internal static string Ellipsis(string value, int max)
        {
            string clean = Clean(value);
            if (max <= 3) return clean;

            // Nothing to interpret and short enough - the overwhelmingly common case.
            if (clean.Length <= max && clean.IndexOf('<') < 0) return clean;

            if (Visible(clean) <= max) return clean;

            int budget = max - 3;
            var sb = new StringBuilder(clean.Length + 16);
            var open = new List<string>();
            int shown = 0;

            for (int i = 0; i < clean.Length && shown < budget; )
            {
                if (TryTag(clean, i, out int end, out string name, out bool closing))
                {
                    sb.Append(clean, i, end - i + 1);

                    if (closing) CloseTag(open, name);
                    else if (!IsVoidTag(name)) open.Add(name);

                    i = end + 1;
                    continue;
                }

                sb.Append(clean[i]);
                shown++;
                i++;
            }

            // Trailing spaces before an ellipsis read as a gap, so drop them - but only from the visible tail, never
            // from inside a tag.
            while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;

            sb.Append("...");

            for (int i = open.Count - 1; i >= 0; i--) sb.Append("</").Append(open[i]).Append('>');

            return sb.ToString();
        }

        /// <summary>How many characters a reader actually sees, markup not counted.</summary>
        private static int Visible(string value)
        {
            int count = 0;

            for (int i = 0; i < value.Length; )
            {
                if (TryTag(value, i, out int end, out _, out _)) { i = end + 1; continue; }

                count++;
                i++;
            }

            return count;
        }

        /// <summary>
        /// Whether a markup tag starts here, and where it ends.
        ///
        /// Deliberately strict: a tag has to close within <see cref="MaxTagLength"/> characters and may not contain
        /// another '&lt;'. A lone angle bracket in a message - which players do type - is then text, exactly as
        /// TextMeshPro treats it.
        /// </summary>
        private static bool TryTag(string value, int start, out int end, out string name, out bool closing)
        {
            end = -1;
            name = "";
            closing = false;

            if (value[start] != '<') return false;

            int limit = Math.Min(value.Length, start + MaxTagLength);
            for (int i = start + 1; i < limit; i++)
            {
                char c = value[i];
                if (c == '<') return false;
                if (c != '>') continue;

                end = i;

                int from = start + 1;
                if (from < end && value[from] == '/') { closing = true; from++; }

                int to = from;
                while (to < end && value[to] != '=' && value[to] != ' ') to++;

                name = value.Substring(from, to - from).ToLowerInvariant();
                return true;
            }

            return false;
        }

        /// <summary>Pop the innermost matching tag. An unmatched close is dropped rather than guessed at.</summary>
        private static void CloseTag(List<string> open, string name)
        {
            if (open.Count == 0) return;

            // TextMeshPro accepts a bare "</>" as closing the last thing opened.
            if (name.Length == 0) { open.RemoveAt(open.Count - 1); return; }

            for (int i = open.Count - 1; i >= 0; i--)
            {
                if (open[i] != name) continue;

                open.RemoveRange(i, open.Count - i);
                return;
            }
        }

        /// <summary>Tags that stand alone and must not be closed - closing them prints the close tag.</summary>
        private static bool IsVoidTag(string name) =>
            name == "br" || name == "sprite" || name == "space" || name == "nbsp";

        /// <summary>Longest run that is still considered a tag rather than a stray angle bracket.</summary>
        private const int MaxTagLength = 64;

        /// <summary>What a character should become, or null to keep it as it is.</summary>
        private static string Replace(char c)
        {
            switch (c)
            {
                case '\r': return "";
                case '\n': return " ";
                case '\t': return " ";

                case Ellipsis0: return "...";
                case SingleQuoteOpen:
                case SingleQuoteClose: return "'";
                case DoubleQuoteOpen:
                case DoubleQuoteClose: return "\"";
                case EnDash:
                case EmDash: return "-";
                case NonBreakingSpace: return " ";
                case Bullet: return "*";
                case ArrowRight: return "->";
                case ArrowLeft: return "<-";
                case Multiply: return "x";
                case Check:
                case HeavyCheck: return "+";
            }

            // Control characters have no glyph and can break the wire format.
            if (c < ' ') return "";

            return c > LastDrawable ? "?" : null;
        }
    }
}
