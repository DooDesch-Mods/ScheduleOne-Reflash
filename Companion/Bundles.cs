using System.Reflection;

namespace Reflash.Companion
{
    /// <summary>
    /// Reaches Sideload's companion seam - the app list, bundle files, framework assets, runtime images, a way to
    /// run a handler, and taps on everything the host pushes at pages.
    ///
    /// Bound by reflection, exactly the way Sideload.Api binds the rest of the host: locate a public static class
    /// by name, read its static delegate fields, cast them. Both assemblies only ever share BCL types, so Reflash
    /// still references no Sideload assembly and still degrades to "the companion is unavailable" rather than
    /// failing to load when Sideload is older than this.
    ///
    /// Everything here MUST be called on the Unity main thread. The server queues onto it; nothing on a connection
    /// thread calls these directly.
    /// </summary>
    internal sealed class Bundles
    {
        private const string BridgeTypeName = "Sideload.Bridge.SideloadBridge";

        private Func<string> _listApps;
        private Func<string, string, byte[]> _readBundleFile;
        private Func<string, byte[]> _readFrameworkAsset;
        private Func<string, string, byte[]> _readRuntimeImage;
        private Func<string, string, string, string> _invoke;
        private Action<Action<string, string, string>, Action<string, int>, Action<string, string, string>> _setTaps;

        internal bool Available { get; private set; }

        /// <summary>
        /// Bind, or report why not. Called once at start-up: if the installed Sideload predates the companion seam
        /// the server simply does not start, and the in-game phone is unaffected.
        /// </summary>
        internal bool Bind()
        {
            try
            {
                Type bridge = FindBridge();
                if (bridge == null) { Core.Log.Warning("[Reflash] Sideload is not loaded - no companion."); return false; }

                _listApps = Get<Func<string>>(bridge, "ListAppsJson");
                _readBundleFile = Get<Func<string, string, byte[]>>(bridge, "ReadBundleFile");
                _readFrameworkAsset = Get<Func<string, byte[]>>(bridge, "ReadFrameworkAsset");
                _readRuntimeImage = Get<Func<string, string, byte[]>>(bridge, "ReadRuntimeImage");
                _invoke = Get<Func<string, string, string, string>>(bridge, "Invoke");
                _setTaps = Get<Action<Action<string, string, string>, Action<string, int>, Action<string, string, string>>>(bridge, "SetCompanionTaps");

                Available = _listApps != null && _readBundleFile != null && _invoke != null && _setTaps != null;

                if (!Available)
                    Core.Log.Warning("[Reflash] this Sideload has no companion seam - it needs 1.1.0 or newer. " +
                                     "The in-game phone is unaffected.");

                return Available;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[Reflash] binding the companion seam failed: {e.Message}");
                return false;
            }
        }

        internal string ManifestJson() => _listApps?.Invoke() ?? "[]";

        internal byte[] Read(string appId, string path) => _readBundleFile?.Invoke(appId, path);

        internal byte[] FrameworkAsset(string path) => _readFrameworkAsset?.Invoke(path);

        internal byte[] RuntimeImage(string appId, string name) => _readRuntimeImage?.Invoke(appId, name);

        internal string Invoke(string appId, string name, string argument) => _invoke?.Invoke(appId, name, argument) ?? "";

        /// <summary>
        /// Start mirroring host events to connected devices. The callbacks fire on the main thread inside whatever
        /// caused them, so they only enqueue - doing work here would put companion latency inside a game frame.
        /// </summary>
        internal void MirrorTo(Sessions sessions)
        {
            _setTaps?.Invoke(
                (app, name, payload) => sessions.Broadcast(
                    "{\"k\":\"emit\",\"app\":" + JsonRead.Quote(app) + ",\"n\":" + JsonRead.Quote(name) + ",\"p\":" + JsonRead.Quote(payload) + "}"),

                (app, count) => sessions.Broadcast(
                    "{\"k\":\"badge\",\"app\":" + JsonRead.Quote(app) + ",\"c\":" + count + "}"),

                (app, title, subtitle) => sessions.Broadcast(
                    "{\"k\":\"notify\",\"app\":" + JsonRead.Quote(app) + ",\"t\":" + JsonRead.Quote(title) + ",\"s\":" + JsonRead.Quote(subtitle) + "}"));
        }

        internal void StopMirroring() => _setTaps?.Invoke(null, null, null);

        private static T Get<T>(Type type, string field) where T : class =>
            type.GetField(field, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as T;

        private static Type FindBridge()
        {
            Type direct = Type.GetType(BridgeTypeName + ", Sideload", false);
            if (direct != null) return direct;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(BridgeTypeName, false);
                    if (t != null) return t;
                }
                catch { /* an assembly that will not report its types */ }
            }

            return null;
        }
    }
}
