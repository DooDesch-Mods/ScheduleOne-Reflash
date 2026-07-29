using Reflash.Wire;
using Sideload.Api;

namespace Reflash.Hijack
{
    /// <summary>
    /// Routes an intercepted vanilla app open to the Sideload app that replaced it.
    ///
    /// The interception itself is a Harmony prefix on each concrete <c>SetOpen(bool)</c> - see
    /// <see cref="SetOpenPatches"/> for why that point and no other. This half only answers "which app, and is it
    /// safe to take over".
    /// </summary>
    internal static class AppHijack
    {
        private static readonly Dictionary<VanillaApp, AppHandle> _handles = new Dictionary<VanillaApp, AppHandle>();
        private static readonly Dictionary<VanillaApp, string> _pendingArg = new Dictionary<VanillaApp, string>();

        /// <summary>
        /// True once the replacements are registered and the host can actually open them. Until then every prefix
        /// lets the vanilla app through - a half-installed takeover that swallows the open and shows nothing is
        /// worse than no takeover at all.
        /// </summary>
        internal static bool Ready { get; private set; }

        internal static void Register(VanillaApp vanilla, AppHandle handle)
        {
            if (handle == null) return;
            _handles[vanilla] = handle;
        }

        /// <summary>
        /// Called once every replacement is registered. Only then do the prefixes start taking over - and only if
        /// the player has not asked for the original screens back.
        /// </summary>
        internal static void Arm(bool takeOver) => Ready = takeOver && _handles.Count > 0;

        /// <summary>
        /// Switch the takeover while the game runs, and remember the choice.
        ///
        /// Safe at any moment because the prefixes ask <see cref="Ready"/> on every press rather than being added
        /// and removed: whatever is on screen stays on screen, and the next app opened is the one this asks for.
        /// </summary>
        internal static void SetTakeOver(bool on)
        {
            Prefs.SetTakeOverVanillaApps(on);
            Arm(on);
        }

        /// <summary>
        /// Take over an open. Returns false when this app has no replacement, which lets the prefix fall through to
        /// vanilla rather than swallowing the press.
        /// </summary>
        internal static bool Open(VanillaApp vanilla)
        {
            if (!Ready || !_handles.TryGetValue(vanilla, out AppHandle handle)) return false;

            handle.Open();
            return true;
        }

        /// <summary>
        /// Open a replacement and hand it an argument its page collects on its first state call - "show this POI",
        /// "open this conversation".
        ///
        /// An argument rather than an event because the page does not exist yet at this point: a Sideload app builds
        /// its document on first open, so an Emit fired here would reach nobody. This is what makes the vanilla
        /// cross-opens work - Quest's "show on map" and the contacts detail panel's, both of which call
        /// MapApp.SetOpen(true) directly and expect the map to be looking at something specific.
        /// </summary>
        internal static bool Open(VanillaApp vanilla, string argument)
        {
            _pendingArg[vanilla] = argument ?? "";
            return Open(vanilla);
        }

        /// <summary>
        /// The argument this app was opened with, cleared as it is read. Empty when it was opened normally.
        /// </summary>
        internal static string TakePendingArg(VanillaApp vanilla)
        {
            if (!_pendingArg.TryGetValue(vanilla, out string arg)) return "";

            _pendingArg.Remove(vanilla);
            return arg ?? "";
        }
    }
}
