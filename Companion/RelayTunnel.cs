// Host side of the DooDesch relay tunnel. Canonical copy: github.com/DooDesch/relay, host/RelayTunnel.cs.
// A mod copies this file as it is and only changes the namespace if it has to.
//
// What it does: a phone opens https://relay.doodesch.de/<app>/#t=<token>&lan=<ip:port> (PageUrl). That page
// (public/app/loader.js) loads the mod's own web UI through the relay, and every HTTP request of that UI arrives
// here as Handlers.Handle. The mod answers it with the same code that serves its LAN page.
//
// Rooms:
//   - one pairing room per pairing token. Its code and key derive from the token, so only someone who saw the QR
//     can reach it. A phone sends "pair"; Handlers.Pair decides.
//   - one session room per paired phone, with its own random code and secret. One room per phone, because the
//     relay sends every host frame to every client in a room.
//
// Wire: AES-GCM, key = SHA-256("doodesch-relay:v1:" + secret), frame {"d": base64(iv[12] || ct || tag[16])}. The
// secret is the token in the pairing room and the session secret afterwards. Inner frames are listed in loader.js.
//
// Threading: everything runs on pool threads. Handlers.Pair, Handle and Pull are called from there and must not
// touch the game directly.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DooDesch.Relay
{
    internal sealed class RelayTunnel
    {
        internal sealed class Request
        {
            internal string Session = "";
            internal string Method = "GET";
            internal string Path = "/";
            internal string Query = "";
            /// <summary>Where the game's "/" lives on the phone, for example "/reflash/~". Links in served pages go
            /// under this prefix.</summary>
            internal string Root = "";
            internal Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal byte[] Body = Array.Empty<byte>();
        }

        internal sealed class Response
        {
            internal int Status = 200;
            internal string ContentType = "application/octet-stream";
            internal string Cache = "no-store";
            internal byte[] Body = Array.Empty<byte>();
        }

        internal sealed class PairResult
        {
            /// <summary>The mod's session id for this phone, or null when refused.</summary>
            internal string Session;
            /// <summary>Shown on the phone when refused.</summary>
            internal string Refusal;
        }

        internal sealed class Handlers
        {
            /// <summary>(token, device) -> session or refusal. The token is the one this pairing room was opened
            /// for; the mod checks it like a LAN pairing.</summary>
            internal Func<string, string, PairResult> Pair;
            internal Func<Request, Response> Handle;
            /// <summary>(session, drain) -> events to push, or null when the session is gone. drain=false only asks
            /// whether the session still exists and must not count as activity.</summary>
            internal Func<string, bool, IReadOnlyList<string>> Pull;
            internal Action<string> Info;
            internal Action<string> Warn;
            internal Action<string> Error;
        }

        private const int ChunkBytes = 200 * 1024;
        private const int MaxFrameBytes = 1024 * 1024;
        private const int ReconnectMs = 3000;
        private const int PullMs = 100;
        private const int IdleCheckMs = 5000;

        private readonly string _app;
        private readonly string _relay;
        private readonly Handlers _h;
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<string, Room> _sessions = new ConcurrentDictionary<string, Room>(StringComparer.Ordinal);
        private Room _pairing;
        private volatile bool _stopped;

        private sealed class Room
        {
            internal string Code;
            internal byte[] Key;
            internal string Token;   // pairing room only
            internal string Session; // session room only
            internal readonly CancellationTokenSource Cts = new CancellationTokenSource();
            internal readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
            internal volatile ClientWebSocket Ws;
            internal volatile int Clients;
            internal int Failures;
            internal string Label => Session != null ? "session room" : "pairing room";
        }

        /// <param name="app">Namespace on the relay and the page path, [a-z0-9-].</param>
        /// <param name="relayUrl">"wss://relay.doodesch.de/" in production, "ws://127.0.0.1:8080/" for a local relay.</param>
        internal RelayTunnel(string app, string relayUrl, Handlers handlers)
        {
            _app = app;
            _relay = relayUrl.EndsWith("/", StringComparison.Ordinal) ? relayUrl : relayUrl + "/";
            _h = handlers;
            _h.Info?.Invoke("relay tunnel on (" + _relay + _app + "/)");
        }

        /// <summary>Where the game's "/" lives on the phone.</summary>
        internal string Root => "/" + _app + "/~";

        /// <summary>Phones connected through a session room right now.</summary>
        internal int Phones
        {
            get
            {
                int n = 0;
                foreach (Room r in _sessions.Values) if (r.Clients > 0) n++;
                return n;
            }
        }

        /// <summary>The URL for the QR code. lan is "ip:port" of the mod's own server, or empty.</summary>
        internal string PageUrl(string token, string lan)
        {
            string page = _relay.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) ? "https://" + _relay.Substring(6)
                        : _relay.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ? "http://" + _relay.Substring(5)
                        : _relay;
            string url = page + _app + "/#t=" + Uri.EscapeDataString(token ?? "");
            if (!string.IsNullOrEmpty(lan)) url += "&lan=" + lan;
            return url;
        }

        /// <summary>Follow the mod's current pairing token. Empty closes the pairing room. Cheap when unchanged, so it
        /// may be called every frame.</summary>
        internal void SetPairingToken(string token)
        {
            if (_stopped) return;
            token ??= "";
            Room old = null;
            Room fresh = null;
            lock (_lock)
            {
                if ((_pairing?.Token ?? "") == token) return;
                old = _pairing;
                if (token.Length > 0)
                    fresh = new Room { Token = token, Code = RoomFor(token), Key = KeyFor(token) };
                _pairing = fresh;
            }
            // The old room lives on for a moment: a token is replaced the instant a phone pairs with it, and the
            // "paired" answer still has to go out through that room.
            if (old != null) _ = Task.Delay(3000).ContinueWith(_ => Close(old), TaskScheduler.Default);
            if (fresh != null) Start(fresh);
        }

        /// <summary>Close every room. Phones in a session get "bye" first, best effort.</summary>
        internal void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            Room pairing;
            lock (_lock) { pairing = _pairing; _pairing = null; }
            if (pairing != null) Close(pairing);
            foreach (Room r in _sessions.Values) _ = Goodbye(r);
            _h.Info?.Invoke("relay tunnel off");
        }

        // ----- rooms -----

        private void Start(Room room) => _ = Task.Run(() => Run(room));

        private void Close(Room room)
        {
            try { room.Cts.Cancel(); }
            catch (ObjectDisposedException) { /* already closed */ }
            if (room.Session != null) _sessions.TryRemove(room.Session, out _);
        }

        private async Task Goodbye(Room room)
        {
            try { await Send(room, "{\"k\":\"bye\"}"); }
            catch (Exception e) { _h.Warn?.Invoke("relay: could not say bye to a phone: " + e.Message); }
            Close(room);
        }

        private async Task Run(Room room)
        {
            CancellationToken ct = room.Cts.Token;
            while (!ct.IsCancellationRequested && !_stopped)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    var uri = new Uri(_relay + "?app=" + Uri.EscapeDataString(_app) + "&role=host&code=" + Uri.EscapeDataString(room.Code));
                    await ws.ConnectAsync(uri, ct);
                    room.Ws = ws;
                    room.Clients = 0;
                    if (room.Failures > 0) _h.Info?.Invoke("relay: " + room.Label + " connected again after " + room.Failures + " failed tries");
                    room.Failures = 0;

                    Task receive = Receive(room, ws, ct);
                    Task pull = room.Session != null ? PullLoop(room, ct) : Task.Delay(Timeout.Infinite, ct);
                    Task done = await Task.WhenAny(receive, pull);
                    room.Ws = null;
                    if (ws.State == WebSocketState.Open)
                    {
                        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                        catch (Exception e) { _h.Warn?.Invoke("relay: close failed: " + e.Message); }
                    }
                    if (done.IsFaulted) throw done.Exception.GetBaseException();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    room.Ws = null;
                    room.Failures++;
                    // The first failure and then every 20th, so a relay that is down for an hour does not fill the log.
                    if (room.Failures == 1 || room.Failures % 20 == 0)
                        _h.Warn?.Invoke("relay: " + room.Label + " lost (" + room.Failures + "x): " + e.Message.TrimEnd('.') + ". Retrying every 3 s.");
                }

                if (ct.IsCancellationRequested || _stopped) break;
                try { await Task.Delay(ReconnectMs, ct); }
                catch (OperationCanceledException) { break; }
            }
            room.Ws = null;
        }

        private async Task Receive(Room room, ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[64 * 1024];
            var msg = new System.IO.MemoryStream();
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                msg.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        _h.Warn?.Invoke("relay: " + room.Label + " closed by the relay (" + r.CloseStatus + " " + r.CloseStatusDescription + ")");
                        return;
                    }
                    msg.Write(buf, 0, r.Count);
                    if (msg.Length > MaxFrameBytes) throw new InvalidOperationException("frame over " + MaxFrameBytes + " bytes");
                } while (!r.EndOfMessage);

                if (r.MessageType != WebSocketMessageType.Text) continue;
                string text = Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length);
                try { OnFrame(room, text); }
                catch (Exception e) { _h.Warn?.Invoke("relay: dropped a frame in the " + room.Label + ": " + e.Message); }
            }
        }

        private void OnFrame(Room room, string text)
        {
            if (!(MiniJson.Parse(text) is Dictionary<string, object> outer)) throw new FormatException("not an object");

            if (outer.TryGetValue("__relay", out object ctl))
            {
                string kind = ctl as string;
                if (kind == "join" || kind == "leave")
                {
                    int before = room.Clients;
                    room.Clients = (int)MiniJson.Num(outer, "n");
                    if (room.Session != null && before == 0 && room.Clients > 0) _h.Info?.Invoke("relay: a phone is connected");
                    if (room.Session != null && before > 0 && room.Clients == 0) _h.Info?.Invoke("relay: a phone disconnected");
                }
                return;
            }

            string d = MiniJson.Str(outer, "d");
            if (d.Length == 0) throw new FormatException("no payload");
            string json = Decrypt(room.Key, d);
            if (!(MiniJson.Parse(json) is Dictionary<string, object> inner)) throw new FormatException("payload not an object");

            string k = MiniJson.Str(inner, "k");
            if (k == "pair" && room.Token != null) _ = Task.Run(() => OnPair(room, inner));
            else if (k == "req" && room.Session != null) _ = Task.Run(() => OnRequest(room, inner));
            else _h.Warn?.Invoke("relay: unexpected '" + k + "' in the " + room.Label);
        }

        private async Task OnPair(Room pairing, Dictionary<string, object> m)
        {
            string nonce = MiniJson.Str(m, "n");
            string device = MiniJson.Str(m, "d");
            if (device.Length > 60) device = device.Substring(0, 60);

            PairResult result;
            try { result = _h.Pair(pairing.Token, device); }
            catch (Exception e)
            {
                _h.Error?.Invoke("relay: pairing failed: " + e.Message);
                result = new PairResult { Refusal = "Something went wrong in the game." };
            }

            if (result?.Session == null)
            {
                string why = result?.Refusal ?? "Refused.";
                _h.Warn?.Invoke("relay: pairing refused: " + why);
                await SendSafe(pairing, "{\"k\":\"refused\",\"n\":" + MiniJson.Quote(nonce) + ",\"why\":" + MiniJson.Quote(why) + "}");
                return;
            }

            string secret = RandomId(24);
            var room = new Room { Session = result.Session, Code = RandomId(16), Key = KeyFor(secret) };
            _sessions[result.Session] = room;
            Start(room);
            await SendSafe(pairing, "{\"k\":\"paired\",\"n\":" + MiniJson.Quote(nonce) + ",\"room\":" + MiniJson.Quote(room.Code)
                                    + ",\"secret\":" + MiniJson.Quote(secret) + "}");
            _h.Info?.Invoke("relay: a phone paired");
        }

        private async Task OnRequest(Room room, Dictionary<string, object> m)
        {
            long id = (long)MiniJson.Num(m, "i");
            var req = new Request { Session = room.Session, Method = MiniJson.Str(m, "m").ToUpperInvariant(), Root = Root };
            string p = MiniJson.Str(m, "p");
            int q = p.IndexOf('?');
            req.Path = q >= 0 ? p.Substring(0, q) : p;
            req.Query = q >= 0 ? p.Substring(q + 1) : "";
            if (req.Path.Length == 0 || req.Path[0] != '/') req.Path = "/" + req.Path;
            if (m.TryGetValue("h", out object h) && h is Dictionary<string, object> headers)
                foreach (KeyValuePair<string, object> kv in headers)
                    if (kv.Value is string v) req.Headers[kv.Key] = v;
            string b = MiniJson.Str(m, "b");
            if (b.Length > 0) req.Body = Convert.FromBase64String(b);

            Response res;
            try { res = _h.Handle(req) ?? new Response { Status = 500, ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("no answer") }; }
            catch (Exception e)
            {
                _h.Error?.Invoke("relay: " + req.Method + " " + req.Path + " failed: " + e.Message);
                res = new Response { Status = 500, ContentType = "text/plain; charset=utf-8", Body = Encoding.UTF8.GetBytes("The game failed to answer.") };
            }

            byte[] body = res.Body ?? Array.Empty<byte>();
            int n = Math.Max(1, (body.Length + ChunkBytes - 1) / ChunkBytes);
            string head = "{\"k\":\"res\",\"i\":" + id.ToString(CultureInfo.InvariantCulture)
                        + ",\"s\":" + res.Status.ToString(CultureInfo.InvariantCulture)
                        + ",\"t\":" + MiniJson.Quote(res.ContentType ?? "")
                        + ",\"c\":" + MiniJson.Quote(res.Cache ?? "")
                        + ",\"n\":" + n.ToString(CultureInfo.InvariantCulture);
            for (int of = 0; of < n; of++)
            {
                int start = of * ChunkBytes;
                int len = Math.Min(ChunkBytes, body.Length - start);
                string chunk = len > 0 ? Convert.ToBase64String(body, start, len) : "";
                if (!await SendSafe(room, head + ",\"of\":" + of.ToString(CultureInfo.InvariantCulture) + ",\"b\":\"" + chunk + "\"}")) return;
            }
        }

        /// <summary>Pushes the session's events while a phone is there. Without a phone it only checks every few
        /// seconds whether the session still exists, so an idle session can expire in the mod as it would on LAN.</summary>
        private async Task PullLoop(Room room, CancellationToken ct)
        {
            int idle = 0;
            while (!ct.IsCancellationRequested && room.Ws != null && room.Ws.State == WebSocketState.Open)
            {
                bool phone = room.Clients > 0;
                if (phone || (idle += PullMs) >= IdleCheckMs)
                {
                    idle = 0;
                    IReadOnlyList<string> events;
                    try { events = _h.Pull(room.Session, phone); }
                    catch (Exception e)
                    {
                        _h.Error?.Invoke("relay: reading events failed: " + e.Message);
                        events = Array.Empty<string>();
                    }

                    if (events == null)
                    {
                        _h.Info?.Invoke("relay: session ended, closing its room");
                        await Goodbye(room);
                        return;
                    }
                    foreach (string ev in events)
                        if (!await SendSafe(room, "{\"k\":\"ev\",\"d\":" + MiniJson.Quote(ev) + "}")) break;
                }
                await Task.Delay(PullMs, ct);
            }
        }

        // ----- sending -----

        private async Task<bool> SendSafe(Room room, string json)
        {
            try
            {
                await Send(room, json);
                return true;
            }
            catch (Exception e)
            {
                _h.Warn?.Invoke("relay: send failed in the " + room.Label + ": " + e.Message);
                return false;
            }
        }

        private async Task Send(Room room, string json)
        {
            ClientWebSocket ws = room.Ws;
            if (ws == null || ws.State != WebSocketState.Open) throw new InvalidOperationException("not connected");
            byte[] frame = Encoding.UTF8.GetBytes("{\"d\":\"" + Encrypt(room.Key, json) + "\"}");
            await room.SendLock.WaitAsync();
            try { await ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Text, true, CancellationToken.None); }
            finally { room.SendLock.Release(); }
        }

        // ----- crypto -----

        private static byte[] KeyFor(string secret)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes("doodesch-relay:v1:" + secret));
        }

        private static string RoomFor(string token)
        {
            using var sha = SHA256.Create();
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("doodesch-relay:room:v1:" + token));
            return Base64Url(h).Substring(0, 22);
        }

        private static string Encrypt(byte[] key, string json)
        {
            byte[] iv = new byte[12];
            RandomNumberGenerator.Fill(iv);
            byte[] pt = Encoding.UTF8.GetBytes(json);
            byte[] wire = new byte[12 + pt.Length + 16];
            byte[] ct = new byte[pt.Length];
            byte[] tag = new byte[16];
            using (var gcm = new AesGcm(key)) gcm.Encrypt(iv, pt, ct, tag);
            Buffer.BlockCopy(iv, 0, wire, 0, 12);
            Buffer.BlockCopy(ct, 0, wire, 12, ct.Length);
            Buffer.BlockCopy(tag, 0, wire, 12 + ct.Length, 16);
            return Convert.ToBase64String(wire);
        }

        private static string Decrypt(byte[] key, string b64)
        {
            byte[] wire = Convert.FromBase64String(b64);
            if (wire.Length < 28) throw new FormatException("frame too short");
            int len = wire.Length - 28;
            byte[] iv = new byte[12];
            byte[] ct = new byte[len];
            byte[] tag = new byte[16];
            Buffer.BlockCopy(wire, 0, iv, 0, 12);
            Buffer.BlockCopy(wire, 12, ct, 0, len);
            Buffer.BlockCopy(wire, 12 + len, tag, 0, 16);
            byte[] pt = new byte[len];
            using (var gcm = new AesGcm(key)) gcm.Decrypt(iv, ct, tag, pt);
            return Encoding.UTF8.GetString(pt);
        }

        private static string RandomId(int bytes)
        {
            var buf = new byte[bytes];
            RandomNumberGenerator.Fill(buf);
            return Base64Url(buf);
        }

        private static string Base64Url(byte[] b) => Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        /// <summary>
        /// Just enough JSON for the tunnel's own frames: objects, arrays, strings, numbers, true/false/null. Not
        /// System.Text.Json, because its reflection paths are untested under IL2CPP and this file must not need a
        /// package.
        /// </summary>
        internal static class MiniJson
        {
            internal static object Parse(string s)
            {
                int i = 0;
                object v = Value(s, ref i);
                Ws(s, ref i);
                if (i != s.Length) throw new FormatException("trailing text at " + i);
                return v;
            }

            internal static string Str(Dictionary<string, object> o, string key) =>
                o.TryGetValue(key, out object v) && v is string s ? s : "";

            internal static double Num(Dictionary<string, object> o, string key) =>
                o.TryGetValue(key, out object v) && v is double d ? d : 0;

            internal static string Quote(string s)
            {
                var sb = new StringBuilder(s.Length + 2).Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20 || c == '\u2028' || c == '\u2029') sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
                return sb.Append('"').ToString();
            }

            private static void Ws(string s, ref int i)
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
            }

            private static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                if (i >= s.Length) throw new FormatException("unexpected end");
                char c = s[i];
                if (c == '{') return Obj(s, ref i);
                if (c == '[') return Arr(s, ref i);
                if (c == '"') return String(s, ref i);
                if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == start) throw new FormatException("unexpected '" + c + "' at " + i);
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private static Dictionary<string, object> Obj(string s, ref int i)
            {
                var o = new Dictionary<string, object>(StringComparer.Ordinal);
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    string key = String(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':' at " + i);
                    i++;
                    o[key] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new FormatException("expected ',' or '}' at " + i);
                }
            }

            private static List<object> Arr(string s, ref int i)
            {
                var a = new List<object>();
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException("expected ',' or ']' at " + i);
                }
            }

            private static string String(string s, ref int i)
            {
                if (i >= s.Length || s[i] != '"') throw new FormatException("expected a string at " + i);
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw new FormatException("bad escape \\" + e);
                    }
                }
                throw new FormatException("unterminated string");
            }
        }
    }
}
