using MelonLoader;

namespace Reflash
{
    /// <summary>
    /// Settings, in MelonPreferences.cfg.
    ///
    /// Two of the three defaults say "change nothing until asked". The vanilla screens stay in charge of the in-game
    /// phone, and the companion opens no port until someone presses the button in the Connect app - a mod that is
    /// merely installed should not be reachable over the network.
    /// </summary>
    internal static class Prefs
    {
        private const string Category = "Reflash_01_Main";

        private static MelonPreferences_Category _category;
        private static MelonPreferences_Entry<bool> _companion;
        private static MelonPreferences_Entry<int> _port;
        private static MelonPreferences_Entry<bool> _takeOver;

        internal static void Load()
        {
            _category = MelonPreferences.CreateCategory(Category, "Reflash");

            _takeOver = _category.CreateEntry(
                "ReplaceVanillaApps", false,
                description: "Let the phone icons open the Reflash apps instead of the original screens. Off: the "
                           + "in-game phone is untouched and Reflash only adds the Connect app, which hands the "
                           + "same seven screens to your real phone. The replacements are the same code either "
                           + "way - this only decides which screen the icons on the in-game phone open.");

            _companion = _category.CreateEntry(
                "Companion", false,
                description: "Serve the phone to a real phone on your local network. You do not need to edit this: "
                           + "the Connect app on the in-game phone turns it on, and turning it on there sets this "
                           + "so it comes back next time. Off by default because it opens a port, and anything "
                           + "that can reach that port can try to pair. A device still needs the code on screen.");

            _port = _category.CreateEntry(
                "CompanionPort", 6180,
                description: "TCP port for the companion server. Change it only if something else already uses "
                           + "this one - the port is part of the address the QR code carries.");
        }

        internal static bool TakeOverVanillaApps => _takeOver?.Value ?? false;

        internal static bool CompanionEnabled => _companion?.Value ?? false;

        /// <summary>Set from the Connect app, where the takeover is a switch rather than a restart.</summary>
        internal static void SetTakeOverVanillaApps(bool on)
        {
            if (_takeOver == null || _takeOver.Value == on) return;

            _takeOver.Value = on;
            Save();
        }

        /// <summary>
        /// Remember what the Connect app was told to do. Written straight through rather than at shutdown: a game
        /// that is killed rather than quit is the normal way this one ends, and a switch the player pressed should
        /// survive that.
        /// </summary>
        internal static void SetCompanionEnabled(bool on)
        {
            if (_companion == null || _companion.Value == on) return;

            _companion.Value = on;
            Save();
        }

        private static void Save()
        {
            try { _category?.SaveToFile(false); }
            catch (Exception e) { Core.Log.Warning($"could not save settings: {e.Message}"); }
        }

        /// <summary>Clamped, because a nonsense port would fail to bind and read as "the mod is broken".</summary>
        internal static int CompanionPort
        {
            get
            {
                int port = _port?.Value ?? 6180;
                return port < 1024 || port > 65535 ? 6180 : port;
            }
        }
    }
}
