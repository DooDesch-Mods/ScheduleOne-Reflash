namespace Reflash.Wire
{
    /// <summary>
    /// One command from a page: an operation name followed by fields, separated by U+001F (unit separator).
    ///
    /// Not JSON, because nothing coming back needs structure - every command is a verb and a few flat values, and a
    /// parser would be code to maintain for no expressive gain. Unit separator rather than newline or tab because a
    /// message body may legitimately contain both, and the one thing that must never happen is a player's text
    /// splitting a command in half.
    ///
    /// Strict on purpose. Every web bundle is replaceable by the player in the Mods folder, so a handler is a trust
    /// boundary: a command with the wrong number of fields is refused rather than padded, and a field that should be
    /// a number and is not is refused rather than defaulted. The page gets an error code back and can say so.
    /// </summary>
    internal readonly struct Cmd
    {
        /// <summary>U+001F. Written as an escape rather than literally: the raw character is invisible in an
        /// editor and would not survive a copy-paste or a well-meaning whitespace cleanup.</summary>
        internal const char Separator = '\u001F';

        private readonly string[] _fields;

        private Cmd(string op, string[] fields)
        {
            Op = op;
            _fields = fields;
        }

        /// <summary>The verb. Empty when the command was malformed.</summary>
        internal string Op { get; }

        /// <summary>How many fields followed the verb.</summary>
        internal int Count => _fields?.Length ?? 0;

        internal bool IsEmpty => string.IsNullOrEmpty(Op);

        internal static Cmd Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return default;

            string[] parts = raw.Split(Separator);
            string op = parts[0].Trim();
            if (op.Length == 0) return default;

            var fields = new string[parts.Length - 1];
            Array.Copy(parts, 1, fields, 0, fields.Length);
            return new Cmd(op, fields);
        }

        /// <summary>A field by position, or null when the command did not carry one. Never throws - a short command
        /// is a refusal, not an exception.</summary>
        internal string Str(int index) => _fields != null && index >= 0 && index < _fields.Length ? _fields[index] : null;

        /// <summary>A field as an integer. False when it is missing or not a number, so the caller answers
        /// <c>err:bad-args</c> rather than acting on a zero nobody sent.</summary>
        internal bool Int(int index, out int value)
        {
            value = 0;
            string s = Str(index);
            return s != null && int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer,
                                             System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>A field as a number with a fraction - a price. Invariant culture, because that is what the page
        /// writes and the mod runtime has invariant globalization anyway.</summary>
        internal bool Num(int index, out float value)
        {
            value = 0f;
            string s = Str(index);
            return s != null && float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                                               System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>A field as a flag. "1" and "true" are true, "0" and "false" are false, anything else is a
        /// refusal - a bad flag should not silently mean "off".</summary>
        internal bool Flag(int index, out bool value)
        {
            value = false;
            string s = Str(index)?.Trim();
            if (s == null) return false;

            if (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
            if (s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
            return false;
        }
    }

    /// <summary>
    /// What a command handler answers. The page maps a code onto words it owns, so every string a player reads
    /// lives in the bundle and can be reworded - or translated - without rebuilding the mod.
    /// </summary>
    internal static class Reply
    {
        internal const string Ok = "ok";

        /// <summary>The command named something that does not exist, or the wrong number of fields.</summary>
        internal const string BadArgs = "err:bad-args";

        /// <summary>The thing it names is gone - a conversation, a dealer, a product.</summary>
        internal const string NotFound = "err:not-found";

        /// <summary>
        /// The page acted on a view of the world that has since moved on. The one error that matters in co-op and on
        /// a companion device: a message arriving while the player is reading the reply buttons renumbers them, and
        /// answering the old number would send the wrong reply.
        /// </summary>
        internal const string Stale = "err:stale";

        /// <summary>The action is understood but not allowed right now - a dealer already at ten customers.</summary>
        internal const string Refused = "err:refused";

        /// <summary>Nothing was reachable to act on: no player, no save loaded, a manager missing.</summary>
        internal const string NoGame = "err:no-game";
    }
}
