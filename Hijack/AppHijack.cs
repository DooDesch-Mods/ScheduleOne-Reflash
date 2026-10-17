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

        /// <summary>Every app this mod registered, replacements and Connect alike, for the one job that has to
        /// cover all of them: getting out of vanilla's way.</summary>
        private static readonly List<AppHandle> _ours = new List<AppHandle>();

        internal static void Register(VanillaApp vanilla, AppHandle handle)
        {
            if (handle == null) return;

            _handles[vanilla] = handle;
            Watch(handle);
        }

        /// <summary>An app of ours that replaces nothing - Connect. It still has to stand aside.</summary>
        internal static void Watch(AppHandle handle)
        {
            if (handle != null && !_ours.Contains(handle)) _ours.Add(handle);
        }

        /// <summary>
        /// Close whatever of ours is open, because a vanilla app is about to take the screen.
        ///
        /// Both hosts activate their own container and neither knows about the other, so without this the vanilla
        /// panel opens BEHIND ours and the press reads as ignored.
        /// </summary>
        internal static void StandAside()
        {
            for (int i = 0; i < _ours.Count; i++)
            {
                AppHandle handle = _ours[i];
                if (handle != null && handle.IsOpen) handle.Close();
            }
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
        /// Open a replacement and hand it an argument its page collects on its next state call - "show this POI",
        /// "open this conversation".
        ///
        /// An argument a page collects, because the page may or may not exist yet. Sideload builds every page while
        /// the save loads, so usually it does - and then it has read its state long ago and has no reason to read it
        /// again, which is what the <c>.changed</c> sent after the open is for. A page that does not exist yet
        /// collects the argument on its first read instead. This is what makes the vanilla cross-opens work -
        /// Quest's "show on map" and the contacts detail panel's, both of which call MapApp.SetOpen(true) directly
        /// and expect the map to be looking at something specific.
        /// </summary>
        internal static bool Open(VanillaApp vanilla, string argument)
        {
            Companion.Sessions.Session from = Companion.CallOrigin.Current;
            if (from != null) return OpenOnCompanion(from, vanilla, argument);

            _pendingArg[vanilla] = argument ?? "";
            if (!Open(vanilla)) return false;

            if (_handles.TryGetValue(vanilla, out AppHandle handle))
            {
                handle.Emit(handle.Id + ".changed", "open");
                Core.Log?.Msg($"Opened {handle.Id} with '{argument}'.");
            }

            return true;
        }

        /// <summary>
        /// The same open, asked for from a companion phone: it happens on that phone, through its event stream, and
        /// the game's phone stays as it is. The takeover setting does not apply - it is about the game's phone, and
        /// the companion only has our apps.
        /// </summary>
        private static bool OpenOnCompanion(Companion.Sessions.Session from, VanillaApp vanilla, string argument)
        {
            if (!_handles.TryGetValue(vanilla, out AppHandle handle))
            {
                Core.Log?.Warning($"No app replaces {vanilla}, so the companion cannot open it.");
                return false;
            }

            from.PendingArg[handle.Id] = argument ?? "";
            from.Outbox.Enqueue("{\"k\":\"open\",\"app\":" + Companion.JsonRead.Quote(handle.Id) + "}");
            Core.Log?.Msg($"Opened {handle.Id} on the companion ({from.Device}) with '{argument}'.");
            return true;
        }

        /// <summary>
        /// The argument this app was opened with, cleared as it is read. Empty when it was opened normally. A call
        /// from a companion phone reads that phone's own, so the two phones never take each other's.
        /// </summary>
        internal static string TakePendingArg(VanillaApp vanilla)
        {
            Companion.Sessions.Session from = Companion.CallOrigin.Current;
            if (from != null)
            {
                if (!_handles.TryGetValue(vanilla, out AppHandle handle)
                    || !from.PendingArg.TryGetValue(handle.Id, out string sent)) return "";

                from.PendingArg.Remove(handle.Id);
                return sent ?? "";
            }

            if (!_pendingArg.TryGetValue(vanilla, out string arg)) return "";

            _pendingArg.Remove(vanilla);
            return arg ?? "";
        }
    }
}
