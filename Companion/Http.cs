using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Reflash.Companion
{
    /// <summary>Writing HTTP/1.1 responses onto a raw socket. Every response closes the connection - there is no
    /// keep-alive here, and a phone making a handful of requests per interaction does not need one.</summary>
    internal static class Http
    {
        internal static void Text(NetworkStream stream, int status, string message) =>
            Bytes(stream, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(message));

        internal static void Json(NetworkStream stream, int status, string json) =>
            Bytes(stream, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));

        internal static void Html(NetworkStream stream, string html) =>
            Bytes(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

        internal static void Bytes(NetworkStream stream, int status, string contentType, byte[] body, string extraHeader = null)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
            head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");

            // Default to no-store, but let a caller that knows better replace it rather than contradict it - two
            // Cache-Control headers on one response is a guess about which the browser reads first.
            bool ownCache = extraHeader != null
                            && extraHeader.StartsWith("Cache-Control:", StringComparison.OrdinalIgnoreCase);

            if (!ownCache) head.Append("Cache-Control: no-store\r\n");
            if (extraHeader != null) head.Append(extraHeader).Append("\r\n");
            head.Append("Connection: close\r\n\r\n");

            try
            {
                byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
                stream.Write(headBytes, 0, headBytes.Length);
                if (body != null && body.Length > 0) stream.Write(body, 0, body.Length);
                stream.Flush();
            }
            catch
            {
                // The device hung up mid-response. Nothing useful to do and nothing worth logging - it happens
                // every time a page navigates away.
            }
        }

        /// <summary>Write straight onto the socket. For the event stream, which has no content length and stays
        /// open - the one response that is not a single blob.</summary>
        internal static void Raw(NetworkStream stream, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// Percent-decoding for one path segment. Hand-rolled rather than Uri.UnescapeDataString because the
        /// segments here are ASCII labels and the framework method drags in globalization tables under IL2CPP.
        /// </summary>
        internal static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('%') < 0) return value ?? "";

            var sb = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '%' && i + 2 < value.Length
                    && Hex(value[i + 1], out int hi) && Hex(value[i + 2], out int lo))
                {
                    sb.Append((char)((hi << 4) | lo));
                    i += 2;
                    continue;
                }

                sb.Append(value[i] == '+' ? ' ' : value[i]);
            }

            return sb.ToString();
        }

        private static bool Hex(char c, out int value)
        {
            if (c >= '0' && c <= '9') { value = c - '0'; return true; }
            if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
            if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }

            value = 0;
            return false;
        }

        private static string Reason(int status) => status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            503 => "Service Unavailable",
            _ => "OK",
        };
    }

    internal static class Mime
    {
        internal static string For(string path)
        {
            int dot = path.LastIndexOf('.');
            string ext = dot < 0 ? "" : path.Substring(dot).ToLowerInvariant();

            return ext switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                ".woff2" => "font/woff2",
                _ => "application/octet-stream",
            };
        }
    }

    /// <summary>
    /// Wall-clock seconds, from a monotonic source. Not Time.unscaledTime, because the companion's threads are not
    /// the main thread and Unity's clock may only be read there.
    /// </summary>
    internal static class Clock
    {
        private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();

        internal static double Now => Watch.Elapsed.TotalSeconds;
    }

    internal static class NetworkInfo
    {
        /// <summary>
        /// The address a phone on the same network should aim at.
        ///
        /// Found by opening a UDP socket towards a public address and asking which local endpoint the OS chose -
        /// nothing is ever sent. That picks the interface with the default route, which is the one that actually
        /// carries traffic, instead of guessing among WSL adapters, VPN tunnels and disconnected NICs.
        /// </summary>
        internal static string DetectLanIp()
        {
            try
            {
                using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                probe.Connect("8.8.8.8", 65530);

                if (probe.LocalEndPoint is IPEndPoint local && !IPAddress.IsLoopback(local.Address))
                    return local.Address.ToString();
            }
            catch
            {
                // No route out at all - fall through to enumerating.
            }

            // A private address on any interface that is up. Preferred over whatever DNS says, because a machine
            // name can resolve to something a phone cannot reach.
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IsPrivate(addr.Address)) return addr.Address.ToString();
                    }
                }
            }
            catch
            {
                // Enumeration is not available. Loopback at least lets the desktop browser work.
            }

            return "127.0.0.1";
        }

        private static bool IsPrivate(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
        }
    }
}
