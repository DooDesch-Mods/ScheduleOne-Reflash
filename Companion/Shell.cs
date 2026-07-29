using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace Reflash.Companion
{
    /// <summary>
    /// The page a phone actually loads, and the three lines injected into every app bundle so the SAME files run
    /// in a browser.
    ///
    /// The injection is the whole trick. An app's index.html is served untouched on disk but rewritten on the way
    /// out with a base href, a compatibility stylesheet and the bridge shim. Editing the file instead would break
    /// the in-game renderer, which would treat a script tag pointing at /sc/ as a missing bundle file.
    /// </summary>
    internal static class Shell
    {
        /// <summary>
        /// The outer page. Deliberately tiny and self-contained: it exists to take the pairing token out of the
        /// URL fragment, trade it for a session, and then show the app grid in an iframe.
        /// </summary>
        internal static string Page() => Asset("shell.html");

        internal static void ServeAsset(NetworkStream stream, string name)
        {
            string text = Asset(name);
            if (text == null) { Http.Text(stream, 404, "not found"); return; }

            Http.Bytes(stream, 200, Mime.For(name), Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Put the three lines into an app's head.
        ///
        /// <c>&lt;base&gt;</c> makes every relative asset in the bundle resolve under this app, so an icon
        /// referenced as <c>glyph.png</c> lands on <c>/app/&lt;id&gt;/glyph.png</c> with no rewriting anywhere.
        /// compat.css restates the renderer's own defaults, without which the browser lays every box out
        /// differently. s1-bridge.js is the shim that makes s1.call work over a network.
        /// </summary>
        internal static byte[] InjectInto(byte[] html, string appId)
        {
            string text = Encoding.UTF8.GetString(html);

            // The colour-scheme declaration has to be a meta tag in the markup, not a rule in compat.css. Android
            // Chrome decides whether to force-darken a document before the stylesheets arrive, and a page it
            // decides to darken has its IMAGES dimmed - which turned every app icon grey and, worse, took the QR
            // code's white quiet zone down to about #c0c0c0, where a camera struggles to read it.
            string inject =
                "<meta name=\"color-scheme\" content=\"dark\">\n" +
                "<base href=\"/app/" + appId + "/\">\n" +
                "<link rel=\"stylesheet\" href=\"/sc/font.css\">\n" +
                "<link rel=\"stylesheet\" href=\"/sc/compat.css\">\n" +
                "<script>window.__reflashApp=" + JsonRead.Quote(appId) + ";</script>\n" +
                "<script src=\"/sc/s1-bridge.js\"></script>\n";

            // `s1://` is the renderer's own scheme for a picture the mod supplied at runtime. The bridge rewrites it
            // wherever script touches it, but a src written straight into the markup never goes through script - the
            // browser has already tried to fetch it and failed by the time any code runs. Rewriting it here is what
            // stops the map asking for a scheme no browser has ever heard of.
            text = text.Replace("\"s1://", "\"/img/" + appId + "/")
                       .Replace("'s1://", "'/img/" + appId + "/");

            // The bundles here have no <head> - they are fragments, which is what the in-game renderer wants - so
            // the injection goes at the very front. A browser builds the head implicitly around it.
            int head = text.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
            if (head >= 0) return Encoding.UTF8.GetBytes(text.Insert(head + 6, "\n" + inject));

            return Encoding.UTF8.GetBytes(inject + text);
        }

        /// <summary>
        /// A shell file, embedded in this assembly. Kept as resources rather than written to disk so there is
        /// nothing to install, and nothing a player can accidentally break by editing.
        /// </summary>
        private static string Asset(string name)
        {
            string resource = "Reflash.Assets.shell." + name.Replace('/', '.');

            using Stream stream = typeof(Shell).Assembly.GetManifestResourceStream(resource);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
