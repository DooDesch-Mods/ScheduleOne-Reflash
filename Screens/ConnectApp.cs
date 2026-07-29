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
        }

        private CompanionServer Server => _companion.Server;

        public string Id => "reflash-connect";

        /// <summary>Nothing. It replaces no vanilla app, so no prefix ever routes to it - it is opened by its own
        /// icon like any ordinary Sideload app.</summary>
        public VanillaApp Replaces => (VanillaApp)(-1);

        public int Badge => 0;

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
                .Add("takeover", AppHijack.Ready);

            if (!_companion.Running) return root.Add("url", "").Add("qr", false).Close();

            string token = Server.Devices.CurrentToken(Clock.Now);
            string url = "http://" + Server.LanIp + ":" + Server.Port + "/#t=" + token;

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

                case "new":
                    // Showing a fresh code is how a second device is paired - one token, one phone, by design.
                    Server.Devices.NewToken(Clock.Now);
                    return Reply.Ok;

                case "kick":
                    Server.Devices.DropAll();
                    return Reply.Ok;

                default:
                    return Reply.BadArgs;
            }
        }
    }
}
