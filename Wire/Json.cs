using System.Globalization;
using System.Text;

namespace Reflash.Wire
{
    /// <summary>
    /// Just enough JSON to answer a state call. Strings are the only thing that crosses the bridge, so this side
    /// needs a writer; it does not need a parser, because the page has <c>JSON.parse</c> and sends commands back as
    /// flat separated fields rather than as JSON.
    ///
    /// Grown from WhatsDab's version with the pieces seven apps need: doubles, nulls, and arrays as values.
    /// Deliberately not System.Text.Json - reflection-based serialisation under this IL2CPP runtime is an untested
    /// risk for no gain, since every shape here is written by hand exactly once.
    /// </summary>
    internal sealed class Json
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private bool _empty = true;
        private string _closed;

        private Json(char open) => _sb.Append(open);

        internal static Json Object() => new Json('{');

        internal static Json Array() => new Json('[');

        internal Json Add(string key, string value)
        {
            Separate();
            Quote(key);
            _sb.Append(':');
            Quote(value);
            return this;
        }

        internal Json Add(string key, long value)
        {
            Separate();
            Quote(key);
            _sb.Append(':').Append(value);
            return this;
        }

        internal Json Add(string key, bool value)
        {
            Separate();
            Quote(key);
            _sb.Append(':').Append(value ? "true" : "false");
            return this;
        }

        /// <summary>
        /// A number with a fraction. Written with the invariant culture on purpose: the mod runtime has invariant
        /// globalization, and a comma decimal separator would produce something the page cannot parse.
        /// Non-finite values become null - JSON has no NaN, and emitting one silently breaks the whole document.
        /// </summary>
        internal Json Add(string key, double value)
        {
            Separate();
            Quote(key);
            _sb.Append(':');

            if (double.IsNaN(value) || double.IsInfinity(value)) _sb.Append("null");
            else _sb.Append(value.ToString("0.####", CultureInfo.InvariantCulture));

            return this;
        }

        /// <summary>Nest a finished object or array under a key.</summary>
        internal Json Add(string key, Json value)
        {
            Separate();
            Quote(key);
            _sb.Append(':').Append(value.Close());
            return this;
        }

        /// <summary>An explicit null, for "this field exists and has no value" - which a page reads differently
        /// from an empty string.</summary>
        internal Json AddNull(string key)
        {
            Separate();
            Quote(key);
            _sb.Append(":null");
            return this;
        }

        /// <summary>Append a nested object or array to an array.</summary>
        internal Json Item(Json value)
        {
            Separate();
            _sb.Append(value.Close());
            return this;
        }

        /// <summary>Append a string to an array.</summary>
        internal Json Item(string value)
        {
            Separate();
            Quote(value);
            return this;
        }

        /// <summary>Append a number to an array.</summary>
        internal Json Item(long value)
        {
            Separate();
            _sb.Append(value);
            return this;
        }

        /// <summary>
        /// Finish the document and hand back the text. Idempotent: nesting a builder calls this, and so does
        /// ToString(), so a value that is closed twice must not grow a second bracket - a corruption that only shows
        /// up as a parse error on the far side of the bridge.
        /// </summary>
        internal string Close()
        {
            if (_closed != null) return _closed;

            _sb.Append(_sb[0] == '{' ? '}' : ']');
            return _closed = _sb.ToString();
        }

        public override string ToString() => Close();

        private void Separate()
        {
            // Writing into a finished document would silently produce something that is no longer JSON.
            if (_closed != null)
                throw new InvalidOperationException("this JSON value is already closed and cannot be added to");

            if (!_empty) _sb.Append(',');
            _empty = false;
        }

        private void Quote(string value)
        {
            _sb.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < ' ') _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
