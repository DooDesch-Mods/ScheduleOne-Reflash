using System.Net.Sockets;
using System.Text;

namespace Reflash.Companion
{
    /// <summary>
    /// One HTTP/1.1 request, parsed by hand off a raw socket.
    ///
    /// Hand-rolled because the server has to be a raw TcpListener, and that is not a style choice: on Windows an
    /// HttpListener may only bind loopback prefixes without administrator rights - binding a LAN address or the
    /// wildcard throws unless someone makes a one-time elevated netsh reservation. A TcpListener binds 0.0.0.0
    /// with no privileges at all, and a phone on the same network has to be able to reach this.
    ///
    /// Shaped after Snitch's LanServer, which has been carrying the same job in this runtime for a while.
    /// </summary>
    internal sealed class HttpRequest
    {
        private const int MaxBytes = 64 * 1024;

        internal string Method = "";
        internal string Path = "";
        internal string Query = "";
        internal string Body = "";
        internal string Host = "";
        internal string Origin = "";
        internal string Cookie = "";
        internal string Accept = "";
        internal bool HasClientHeader;   // X-Reflash: the header a cross-origin form post cannot set

        internal static HttpRequest Read(NetworkStream stream)
        {
            var ms = new MemoryStream();
            var buf = new byte[4096];
            int headerEnd = -1;

            while (headerEnd < 0 && ms.Length < MaxBytes)
            {
                int n;
                try { n = stream.Read(buf, 0, buf.Length); } catch { return null; }
                if (n <= 0) break;

                ms.Write(buf, 0, n);
                headerEnd = IndexOfDoubleCrlf(ms.GetBuffer(), (int)ms.Length);
            }

            if (headerEnd < 0) return null;

            var req = new HttpRequest();
            string header = Encoding.ASCII.GetString(ms.GetBuffer(), 0, headerEnd);
            string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return null;

            string[] start = lines[0].Split(' ');
            if (start.Length < 2) return null;

            req.Method = start[0].ToUpperInvariant();
            string target = start[1];

            int q = target.IndexOf('?');
            if (q >= 0) { req.Path = target.Substring(0, q); req.Query = target.Substring(q + 1); }
            else req.Path = target;

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;

                string key = lines[i].Substring(0, colon).Trim().ToLowerInvariant();
                string value = lines[i].Substring(colon + 1).Trim();

                switch (key)
                {
                    case "content-length": int.TryParse(value, out contentLength); break;
                    case "host": req.Host = value; break;
                    case "origin": req.Origin = value; break;
                    case "cookie": req.Cookie = value; break;
                    case "accept": req.Accept = value; break;
                    case "x-reflash": req.HasClientHeader = true; break;
                }
            }

            if (contentLength > 0)
            {
                int bodyStart = headerEnd + 4;
                int have = (int)ms.Length - bodyStart;

                while (have < contentLength && ms.Length < MaxBytes)
                {
                    int n;
                    try { n = stream.Read(buf, 0, buf.Length); } catch { break; }
                    if (n <= 0) break;

                    ms.Write(buf, 0, n);
                    have += n;
                }

                int take = Math.Min(contentLength, (int)ms.Length - bodyStart);
                if (take > 0) req.Body = Encoding.UTF8.GetString(ms.GetBuffer(), bodyStart, take);
            }

            return req;
        }

        /// <summary>The value of one cookie, or empty. The session id travels this way rather than in the query so
        /// that images, stylesheets and the iframe are authenticated too, without rewriting every URL.</summary>
        internal string CookieValue(string name)
        {
            if (string.IsNullOrEmpty(Cookie)) return "";

            foreach (string part in Cookie.Split(';'))
            {
                string p = part.Trim();
                if (p.StartsWith(name + "=", StringComparison.Ordinal)) return p.Substring(name.Length + 1);
            }

            return "";
        }

        /// <summary>A query parameter, url-decoded.</summary>
        internal string QueryValue(string name)
        {
            if (string.IsNullOrEmpty(Query)) return "";

            foreach (string part in Query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (part.Substring(0, eq) != name) continue;

                try { return Uri.UnescapeDataString(part.Substring(eq + 1)); }
                catch { return ""; }
            }

            return "";
        }

        private static int IndexOfDoubleCrlf(byte[] b, int len)
        {
            for (int i = 0; i + 3 < len; i++)
                if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;

            return -1;
        }
    }
}
