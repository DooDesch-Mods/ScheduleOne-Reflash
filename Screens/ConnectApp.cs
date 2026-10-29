using Reflash.Companion;
using Reflash.Hijack;
using Reflash.Wire;
using Sideload.Api;

namespace Reflash.Screens
{
    /// <summary>
    /// Connect. The one app that is NOT a replacement - it is the new thing, and the only one with its own home
    /// screen icon.
    ///
    /// It shows a QR code on the in-game phone that a real phone scans. That is the right fiction and it also
    /// dogfoods the framework: the screen that hands the phone to another screen is itself one of these pages.
    /// And it is where the companion is switched on, so the whole feature is reachable from inside the game rather
    /// than from a text file.
    ///
    /// The pairing URL is deliberately plain and short - <c>http://ip:port/#t=token</c>, about fifty characters,
    /// well inside what a low-version QR carries at a module size a camera can read across a room.
    /// </summary>
    internal sealed class ConnectApp : IAppPort
    {
        private readonly Runner _companion;
        private readonly AppHandle _handle;

        internal ConnectApp(Runner companion, AppHandle handle)
        {
            _companion = companion;
            _handle = handle;
            Server.DeviceRequested = OnDeviceRequested;
        }

        /// <summary>
        /// A phone asks to be allowed. The page shows it when Connect is open; otherwise a phone notification says
        /// so, and the icon badge stays until it is answered. The notification cannot be pressed, so it names the app.
        /// </summary>
        private void OnDeviceRequested(KnownDevices.Request request)
        {
            if (_handle.IsOnScreen)
            {
                Core.Log.Msg($"'{request.Name}' request shown in the open Connect app.");
                return;
            }

            _handle.Notify(request.Name + " wants to connect", "Allow it in Connect.", 20f);
            Core.Log.Msg($"'{request.Name}' request sent as a phone notification.");
        }

        private CompanionServer Server => _companion.Server;

        public string Id => "reflash-connect";

        /// <summary>Nothing. It replaces no vanilla app, so no prefix ever routes to it - it is opened by its own
        /// icon like any ordinary Sideload app.</summary>
        public VanillaApp Replaces => (VanillaApp)(-1);

        /// <summary>Requests waiting for an answer. The badge is what still points at Connect after the
        /// notification is gone.</summary>
        public int Badge => _companion.Running ? Server.Known.Waiting(Clock.Now).Count : 0;

        /// <summary>
        /// Changes with the number of connected devices and with the pairing token, so the page re-reads when a
        /// phone joins or when a code expires.
        /// </summary>
        public int Revision
        {
            get
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (_companion.Running ? 1 : 0);
                    hash = hash * 31 + Server.Devices.Count;
                    hash = hash * 31 + _companion.Problem.Length;
                    hash = hash * 31 + (AppHijack.Ready ? 1 : 0);
                    hash = hash * 31 + (Server.RelayOn ? 1 : 0);
                    hash = hash * 31 + Server.Known.Revision;
                    hash = hash * 31 + Server.Known.Waiting(Clock.Now).Count;

                    // Quantised to five seconds: the countdown should tick, not the whole page.
                    if (_companion.Running) hash = hash * 31 + (int)(Server.Devices.TokenAgeSeconds(Clock.Now) / 5);

                    return hash;
                }
            }
        }

        public string State(string section)
        {
            var root = Json.Object()
                .Add("rev", Revision)
                .Add("running", _companion.Running)
                .Add("devices", Server.Devices.Count)
                .Add("problem", _companion.Problem)
                .Add("takeover", AppHijack.Ready)
                .Add("relay", Server.RelayOn)
                .Add("known", KnownJson());

            if (!_companion.Running) return root.Add("url", "").Add("qr", false).Add("requests", Json.Array()).Close();

            var requests = Json.Array();
            foreach (KnownDevices.Request r in Server.Known.Waiting(Clock.Now))
                requests.Item(Json.Object().Add("id", r.Id).Add("name", r.Name));
            root.Add("requests", requests);

            string token = Server.Devices.CurrentToken(Clock.Now);

            // With the relay on, the code opens the relay page. It still carries the LAN address and switches to it
            // when the phone is on the same network, so LAN stays the first choice.
            string url = Server.RelayOn
                ? Server.RelayPageUrl(token)
                : "http://" + Server.LanIp + ":" + Server.Port + "/#t=" + token;

            // The picture is handed over as a runtime image rather than drawn - there is no canvas here, and a QR
            // built out of boxes would be several hundred of them.
            byte[] png = QrImage.For(url);
            if (png != null) _handle.Image("qr", png);

            double age = Server.Devices.TokenAgeSeconds(Clock.Now);

            return root
                .Add("url", url)
                .Add("qr", png != null)
                // The URL is also shown as text, because a QR on a stream, behind sharpening or at a distance is
                // often unreadable and typing it has to stay possible.
                .Add("plain", Server.LanIp + ":" + Server.Port)
                .Add("expires", Math.Max(0, (int)(120 - age)))
                .Close();
        }

        /// <summary>Remembered devices for the list in Connect. "on" marks one with a live session.</summary>
        private Json KnownJson()
        {
            var list = Json.Array();
            foreach (KnownDevices.Device d in Server.Known.Devices())
            {
                bool on = _companion.Running && Server.Devices.Peek(d.SessionId, Clock.Now) != null;
                list.Item(Json.Object().Add("id", d.Id).Add("name", d.Name).Add("on", on));
            }
            return list;
        }

        public string Act(Cmd cmd)
        {
            switch (cmd.Op)
            {
                case "on":
                    // Deliberately answers ok even when the server refuses to start. The failure is not the
                    // player's mistake and belongs on screen as a sentence they can act on, not as an error code
                    // the page would have to translate back into one - Problem carries it, and the page re-reads.
                    _companion.TurnOn();
                    return Reply.Ok;

                case "off":
                    _companion.TurnOff();
                    return Reply.Ok;

                case "apps-on":
                case "apps-off":
                    // The staged rollout, in the player's hands: the same screens the phone in their pocket is
                    // showing can take over the icons here, one press, no restart.
                    AppHijack.SetTakeOver(cmd.Op == "apps-on");
                    return Reply.Ok;

                case "relay-on":
                case "relay-off":
                    _companion.SetRelay(cmd.Op == "relay-on");
                    return Reply.Ok;

                case "new":
                    // Showing a fresh code is how a second device is paired - one token, one phone, by design.
                    Server.Devices.NewToken(Clock.Now);
                    return Reply.Ok;

                case "kick":
                    // Also forgets every remembered device: "all" has to mean nobody gets back in without asking.
                    Server.DisconnectAll();
                    return Reply.Ok;

                case "allow":
                case "deny":
                {
                    string id = cmd.Str(0);
                    if (string.IsNullOrEmpty(id)) return Reply.BadArgs;

                    // Answered on the in-game phone only. A paired phone letting in more phones is not the
                    // player saying yes at the game.
                    if (CallOrigin.Current != null)
                    {
                        Core.Log.Warning($"{cmd.Op} for request {id} refused: pressed on a paired phone, not in the game.");
                        return Reply.Refused;
                    }

                    KnownDevices.Request r = cmd.Op == "allow"
                        ? Server.Known.Allow(id, Clock.Now)
                        : Server.Known.Deny(id, Clock.Now);
                    if (r == null)
                    {
                        Core.Log.Warning($"{cmd.Op} for request {id}: the request is gone (expired or already answered).");
                        return Reply.Stale;
                    }

                    Core.Log.Msg(cmd.Op == "allow"
                        ? $"device '{r.Name}' from {r.Remote} allowed in Connect and remembered."
                        : $"device '{r.Name}' from {r.Remote} denied in Connect.");
                    return Reply.Ok;
                }

                case "forget":
                {
                    string id = cmd.Str(0);
                    if (string.IsNullOrEmpty(id)) return Reply.BadArgs;
                    return Server.ForgetDevice(id) ? Reply.Ok : Reply.NotFound;
                }

                default:
                    return Reply.BadArgs;
            }
        }
    }
}
