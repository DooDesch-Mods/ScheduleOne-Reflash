using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Reflash.Companion
{
    /// <summary>
    /// Serves the phone to a phone.
    ///
    /// A raw TcpListener on 0.0.0.0, because that is the only way to be reachable from another device without
    /// administrator rights - see HttpRequest for why HttpListener cannot do it. The consequence is that the page
    /// is served over plain HTTP from the game itself, same-origin, which is also what keeps the browser's private
    /// network rules out of the way: a page loaded from an https origin may not reach a LAN address at all.
    ///
    /// Downstream events go over SSE rather than a WebSocket. The upgrade handshake and frame codec would be a
    /// couple of hundred lines against roughly thirty for "write the response headers and never send
    /// Content-Length", and nothing here needs full duplex on one connection - a tap on a phone is a POST.
    ///
    /// Threading: accept and per-connection work happen on pool threads; anything touching the game is queued and
    /// runs on the Unity main thread. Nothing in here calls into the game directly.
    /// </summary>
    internal sealed class CompanionServer
    {
        private const int MaxStreams = 4;
        private const int SocketTimeoutMs = 5000;
        private const int MainThreadTimeoutMs = 2000;

        /// <summary>
        /// A ceiling on connections being served at once. Without it, anything on the network can park a thread per
        /// socket by trickling one byte of a request every few seconds - the per-read timeout keeps resetting, so
        /// nothing ever times out and the pool drains. Unpaired clients cannot read anything, but they can still
        /// take up room.
        /// </summary>
        private const int MaxConnections = 24;

        private readonly Sessions _sessions = new Sessions();
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly Bundles _bundles;

        private TcpListener _listener;
        private Thread _accept;
        private volatile bool _running;
        private int _streams;
        private int _connections;

        internal CompanionServer(Bundles bundles) => _bundles = bundles;

        internal bool Running => _running;

        internal int Port { get; private set; }

        internal string LanIp { get; private set; } = "";

        internal Sessions Devices => _sessions;

        internal bool Start(int port)
        {
            if (_running) return true;

            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();

                Port = port;
                LanIp = NetworkInfo.DetectLanIp();
                _running = true;

                _accept = new Thread(AcceptLoop) { IsBackground = true, Name = "Reflash-Companion" };
                _accept.Start();

                Core.Log.Msg($"[Reflash] companion server on http://{LanIp}:{port}");
                Core.Log.Msg("[Reflash] if a phone cannot reach it, Windows Firewall is the usual reason - allow " +
                             $"TCP {port} on the private network.");
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Error($"[Reflash] the companion server could not start on port {port}: {e.Message}");
                _running = false;
                return false;
            }
        }

        internal void Stop()
        {
            _running = false;
            _sessions.DropAll();

            try { _listener?.Stop(); } catch { /* already down */ }
            _listener = null;
        }

        /// <summary>Drains work queued by connection threads. Called from OnUpdate, so everything it runs is on the
        /// Unity main thread and may touch the game.</summary>
        internal void Pump()
        {
            int budget = 16;
            while (budget-- > 0 && _mainThread.TryDequeue(out Action work))
            {
                try { work(); }
                catch (Exception e) { Core.Log.Error($"[Reflash] companion work failed: {e.Message}"); }
            }
        }

        /// <summary>
        /// Queue something for the main thread and wait for it. Used by request handlers, which cannot touch the
        /// game themselves but need its answer to write a response.
        ///
        /// Three things have to be right here, and getting any of them wrong is worse than the latency they save.
        ///
        /// The wait is bounded, so a request cannot hang a connection thread forever when the game is paused or
        /// loading. But a timed-out request must then ABANDON its work rather than let it run late: a command that
        /// executes two seconds after the page gave up is a player action nobody asked for, and pressing the button
        /// again would do it twice. The job checks whether it was abandoned before touching the game.
        ///
        /// And the wait handle is only disposed by whoever gets there last. Disposing it on timeout while the job
        /// still holds a reference is an ObjectDisposedException on the main thread - inside a game frame.
        /// </summary>
        private T OnMainThread<T>(Func<T> work, T fallback)
        {
            var job = new Job<T> { Work = work, Result = fallback };

            _mainThread.Enqueue(job.Run);

            bool finished = job.Done.Wait(MainThreadTimeoutMs);
            if (!finished) job.Abandon();

            T result = finished ? job.Result : fallback;
            job.Release();
            return result;
        }

        /// <summary>
        /// One piece of work handed to the main thread, with a lifetime both sides agree on. Two owners - the
        /// waiting connection thread and the main thread - and the wait handle goes away when the second of them
        /// lets go.
        /// </summary>
        private sealed class Job<T>
        {
            internal Func<T> Work;
            internal T Result;
            internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);

            private int _abandoned;
            private int _owners = 2;

            internal void Run()
            {
                // Nobody is waiting for this any more. Do NOT touch the game: the request it belonged to already
                // answered, and running now would be an action out of nowhere.
                if (Volatile.Read(ref _abandoned) == 0)
                {
                    try { Result = Work(); }
                    catch (Exception e) { Core.Log.Error($"[Reflash] companion call failed: {e.Message}"); }
                }

                try { Done.Set(); }
                catch (ObjectDisposedException) { /* the waiter has already let go */ }

                Release();
            }

            internal void Abandon() => Interlocked.Exchange(ref _abandoned, 1);

            internal void Release()
            {
                if (Interlocked.Decrement(ref _owners) == 0) Done.Dispose();
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try { client = _listener.AcceptTcpClient(); }
                catch { if (_running) continue; else break; }

                TcpClient captured = client;
                ThreadPool.QueueUserWorkItem(_ => Serve(captured));
            }
        }

        private void Serve(TcpClient client)
        {
            if (Interlocked.Increment(ref _connections) > MaxConnections)
            {
                Interlocked.Decrement(ref _connections);
                try { client.Close(); } catch { /* gone */ }
                return;
            }

            try
            {
                client.ReceiveTimeout = SocketTimeoutMs;
                client.SendTimeout = SocketTimeoutMs;

                using NetworkStream stream = client.GetStream();
                HttpRequest req = HttpRequest.Read(stream);
                if (req == null) return;

                string remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
                Route(stream, req, remote);
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[Reflash] a companion connection failed: {e.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _connections);
                try { client.Close(); } catch { /* gone */ }
            }
        }

        private void Route(NetworkStream stream, HttpRequest req, string remote)
        {
            // Rebinding guard: only the addresses this server is actually reachable at are accepted as a Host, so a
            // hostile page cannot point a name it controls at this port and read the answers.
            if (!HostAllowed(req.Host)) { Http.Text(stream, 400, "bad host"); return; }

            // No CORS headers anywhere, deliberately - anything cross-origin has no business here, and SameSite
            // plus the custom header is what keeps a form post from another page out.
            if (req.Origin.Length > 0 && !OriginAllowed(req.Origin)) { Http.Text(stream, 403, "bad origin"); return; }

            switch (req.Path)
            {
                case "/health":
                    // The one route an unpaired device may read, and its whole job is to say "something is here".
                    // Not even the number of connected devices - that is a fact about the household, and nothing
                    // that has not paired has a reason to learn it.
                    Http.Json(stream, 200, "{\"ok\":true,\"name\":\"Reflash\"}");
                    return;

                case "/":
                    Http.Html(stream, Shell.Page());
                    return;

                case "/api/pair":
                    Pair(stream, req, remote);
                    return;
            }

            if (req.Path.StartsWith("/sc/", StringComparison.Ordinal))
            {
                // The shell's own files carry no secrets and are needed before pairing.
                Shell.ServeAsset(stream, req.Path.Substring(4));
                return;
            }

            Sessions.Session session = _sessions.Find(req.CookieValue("rsid"), Clock.Now);
            if (session == null) { Http.Text(stream, 401, "not paired"); return; }

            if (req.Path == "/events") { ServeEvents(stream, session); return; }

            if (req.Path == "/api/manifest") { Http.Json(stream, 200, OnMainThread(Manifest, "[]")); return; }

            if (req.Path == "/api/calls") { ServeCalls(stream, req); return; }

            if (req.Path.StartsWith("/app/", StringComparison.Ordinal)) { ServeBundle(stream, req.Path.Substring(5)); return; }

            if (req.Path.StartsWith("/img/", StringComparison.Ordinal)) { ServeImage(stream, req.Path.Substring(5)); return; }

            if (req.Path.StartsWith("/icon/", StringComparison.Ordinal)) { ServeIcon(stream, req.Path.Substring(6)); return; }

            if (req.Path == "/wallpaper")
            {
                byte[] png = OnMainThread(Wallpaper.Png, null);
                if (png == null) Http.Text(stream, 404, "not found");
                else Http.Bytes(stream, 200, "image/png", png, "Cache-Control: private, max-age=3600");
                return;
            }

            if (req.Path == "/fw/s1.css")
            {
                byte[] css = OnMainThread(() => _bundles.FrameworkAsset("s1.css"), null);
                if (css == null) Http.Text(stream, 404, "not found");
                else Http.Bytes(stream, 200, "text/css; charset=utf-8", css);
                return;
            }

            Http.Text(stream, 404, "not found");
        }

        private void Pair(NetworkStream stream, HttpRequest req, string remote)
        {
            if (req.Method != "POST" || !req.HasClientHeader) { Http.Text(stream, 400, "bad request"); return; }

            string token = JsonRead.Field(req.Body, "t");
            string device = JsonRead.Field(req.Body, "device");

            Sessions.Session session = _sessions.Pair(token, device, remote, Clock.Now, out Sessions.Refusal why);
            if (session == null) { Http.Text(stream, 403, Explain(why)); return; }

            Core.Log.Msg($"[Reflash] a device paired from {remote}.");

            // HttpOnly so no script can read it, SameSite=Strict so no other site can cause a request that carries
            // it. No Secure flag - there is no https here and claiming otherwise would just break the cookie.
            Http.Bytes(stream, 200, "application/json",
                       Encoding.UTF8.GetBytes("{\"ok\":true}"),
                       "Set-Cookie: rsid=" + session.Id + "; HttpOnly; SameSite=Strict; Path=/; Max-Age=43200");
        }

        /// <summary>
        /// What to tell the phone. Four different problems used to arrive as one sentence about the code, so a
        /// player whose real problem was anything else kept showing new codes that were never going to work.
        /// </summary>
        private static string Explain(Sessions.Refusal why) => why switch
        {
            Sessions.Refusal.LockedOut =>
                "Too many failed attempts from this device. Wait a minute, then scan again.",
            Sessions.Refusal.Expired =>
                "That code has expired. Show a new one in the game.",
            Sessions.Refusal.TooManyDevices =>
                "Four devices are already connected. Disconnect one in the game first.",
            _ => "That code does not match the one on screen.",
        };

        /// <summary>
        /// The event stream. One parked thread per device, capped - the alternative is polling, which would show
        /// up as a delay on every notification.
        /// </summary>
        private void ServeEvents(NetworkStream stream, Sessions.Session session)
        {
            if (Interlocked.Increment(ref _streams) > MaxStreams)
            {
                Interlocked.Decrement(ref _streams);
                Http.Text(stream, 503, "too many streams");
                return;
            }

            try
            {
                // X-Accel-Buffering because a proxy or an antivirus web shield that buffers would hold every event
                // until the connection closed, which looks exactly like the server being dead.
                Http.Raw(stream,
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/event-stream\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "X-Accel-Buffering: no\r\n" +
                    "Connection: keep-alive\r\n\r\n" +
                    "retry: 2000\n\n");

                double lastBeat = Clock.Now;

                // Ends when the session goes away, not only when the socket does. Disconnecting a device has to
                // actually free its slot - otherwise a kicked phone keeps one of the four streams until it happens
                // to notice, and keeps draining events meant for whoever replaced it.
                while (_running && _sessions.Find(session.Id, Clock.Now) != null)
                {
                    while (session.Outbox.TryDequeue(out string envelope))
                        Http.Raw(stream, "data: " + envelope + "\n\n");

                    // A heartbeat every fifteen seconds. It keeps intermediaries from calling the connection idle,
                    // and it is how a dead socket is noticed at all - the write throws.
                    if (Clock.Now - lastBeat > 15)
                    {
                        Http.Raw(stream, ":hb\n\n");
                        lastBeat = Clock.Now;
                    }

                    Thread.Sleep(100);
                }
            }
            catch
            {
                // The device went away. Ordinary.
            }
            finally
            {
                Interlocked.Decrement(ref _streams);
            }
        }

        /// <summary>
        /// A batch of s1.call equivalents. Batched on purpose: each one has to reach the Unity main thread, and
        /// marshalling forty times means forty frames of latency where one will do.
        /// </summary>
        private void ServeCalls(NetworkStream stream, HttpRequest req)
        {
            if (req.Method != "POST" || !req.HasClientHeader) { Http.Text(stream, 400, "bad request"); return; }

            List<JsonRead.Call> calls = JsonRead.ParseCalls(req.Body);
            if (calls.Count == 0) { Http.Json(stream, 200, "[]"); return; }
            if (calls.Count > 64) { Http.Text(stream, 400, "too many calls"); return; }

            string answer = OnMainThread(() =>
            {
                var sb = new StringBuilder("[");
                for (int i = 0; i < calls.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    string value = _bundles.Invoke(calls[i].App, calls[i].Name, calls[i].Arg);
                    sb.Append("{\"id\":").Append(calls[i].Id).Append(",\"r\":").Append(JsonRead.Quote(value)).Append('}');
                }
                return sb.Append(']').ToString();
            }, "[]");

            Http.Json(stream, 200, answer);
        }

        private void ServeBundle(NetworkStream stream, string rest)
        {
            int slash = rest.IndexOf('/');
            if (slash <= 0) { Http.Text(stream, 404, "not found"); return; }

            string appId = rest.Substring(0, slash);
            string path = rest.Substring(slash + 1);

            if (!SafePath(path)) { Http.Text(stream, 400, "bad path"); return; }

            byte[] bytes = OnMainThread(() => _bundles.Read(appId, path), null);
            if (bytes == null) { Http.Text(stream, 404, "not found"); return; }

            // index.html is the one file that is rewritten on the way out: three lines go into its head so the same
            // bundle that runs in the game runs here. The file on disk is untouched - a real <script> tag pointing
            // at /sc/ would be a missing bundle file to the in-game renderer.
            if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
                bytes = Shell.InjectInto(bytes, appId);

            // no-store, and it is not caution. A bundle file may be replaced under the app - by a mod update, or by
            // a folder under Mods/<appId>/ while someone is working on it - and a browser that kept the old app.js
            // shows the old app with no way to tell. Hunting a fix that was already deployed cost real time here.
            Http.Bytes(stream, 200, Mime.For(path), bytes, "Cache-Control: no-store");
        }

        private void ServeImage(NetworkStream stream, string rest)
        {
            int slash = rest.IndexOf('/');
            if (slash <= 0) { Http.Text(stream, 404, "not found"); return; }

            string appId = rest.Substring(0, slash);
            string name = rest.Substring(slash + 1);

            byte[] png = OnMainThread(() => _bundles.RuntimeImage(appId, name), null);
            if (png == null) { Http.Text(stream, 404, "not found"); return; }

            Http.Bytes(stream, 200, "image/png", png);
        }

        /// <summary>
        /// The app list, in the order the phone's own home screen has them.
        ///
        /// Sideload hands them over in registration order, which is an accident of which mod loaded first - so the
        /// second screen would put the icons somewhere else than the phone does, and the whole point of the
        /// companion is that it is the same phone. Main thread only: reading the order reads the scene.
        /// </summary>
        private string Manifest()
        {
            string json = _bundles.ManifestJson();

            var apps = JsonRead.Objects(json);
            if (apps.Count < 2) return json;

            // Sorted by where the icon sits, with anything the home screen does not carry left in its original
            // order at the end.
            apps.Sort((a, b) =>
            {
                int at = Icons.PositionOf(JsonRead.Field(a, "iconLabel"));
                int bt = Icons.PositionOf(JsonRead.Field(b, "iconLabel"));
                return at.CompareTo(bt);
            });

            return "[" + string.Join(",", apps) + "]";
        }

        /// <summary>
        /// A home screen icon, addressed by the label under it - "/icon/Messages". The label rather than an app id
        /// because it is the only thing the phone's icon and the companion's app list agree on.
        /// </summary>
        private void ServeIcon(NetworkStream stream, string label)
        {
            label = Http.Unescape(label);
            if (label.Length == 0) { Http.Text(stream, 404, "not found"); return; }

            byte[] png = OnMainThread(() => Icons.For(label), null);
            if (png == null) { Http.Text(stream, 404, "not found"); return; }

            // The artwork does not change while the game runs, and a phone re-reading eight PNGs on every return to
            // the home screen is eight readbacks nobody asked for.
            Http.Bytes(stream, 200, "image/png", png, "Cache-Control: private, max-age=3600");
        }

        /// <summary>
        /// Only the addresses this server is actually reachable at, and only on its own port. The port matters:
        /// without checking it, "192.168.1.10:anything" passes, and so does a name whose suffix merely starts the
        /// right way.
        /// </summary>
        private bool HostAllowed(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;

            int colon = host.LastIndexOf(':');
            string name = colon > 0 ? host.Substring(0, colon) : host;
            string port = colon > 0 ? host.Substring(colon + 1) : "";

            // A Host without a port only makes sense on 80, which this never is.
            if (port.Length == 0 || !int.TryParse(port, out int p) || p != Port) return false;

            return NameAllowed(name);
        }

        /// <summary>Exact match, not a prefix - "http://127.0.0.1:6180.evil.test" starts with the allowed text.</summary>
        private bool OriginAllowed(string origin)
        {
            const string scheme = "http://";
            if (!origin.StartsWith(scheme, StringComparison.Ordinal)) return false;

            string rest = origin.Substring(scheme.Length);
            int colon = rest.LastIndexOf(':');
            if (colon <= 0) return false;

            string name = rest.Substring(0, colon);
            return int.TryParse(rest.Substring(colon + 1), out int p) && p == Port && NameAllowed(name);
        }

        private bool NameAllowed(string name) =>
            name == LanIp || name == "127.0.0.1" || name == "localhost";

        /// <summary>
        /// A bundle path never reaches the filesystem directly - AppBundle resolves it against a fixed root - but a
        /// request that means nothing is refused here rather than quietly missing.
        /// </summary>
        private static bool SafePath(string path) =>
            path.Length > 0
            && path.IndexOf("..", StringComparison.Ordinal) < 0
            && path.IndexOf('\\') < 0
            && !path.StartsWith("/", StringComparison.Ordinal);
    }
}
