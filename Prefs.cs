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
        private static MelonPreferences_Entry<bool> _relay;
        private static MelonPreferences_Entry<string> _relayUrl;

        internal static void Load()
        {
            _category = MelonPreferences.CreateCategory(Category, "Reflash");

            _takeOver = _category.CreateEntry(
                "ReplaceVanillaApps", false,
                description: "Phone icons open the Reflash apps instead of the original screens. Off: only the "
                           + "Connect app is added.");

            _companion = _category.CreateEntry(
                "Companion", false,
                description: "Serves the apps to a real phone on your network. Set by the Connect app. Opens a port; "
                           + "pairing needs the code on screen.");

            _port = _category.CreateEntry(
                "CompanionPort", 6180,
                description: "Port for the phone server. Change it only if another program uses it.");

            _relay = _category.CreateEntry(
                "Relay", true,
                description: "A phone on another network connects through relay.doodesch.de, end-to-end encrypted. "
                           + "Set by the Connect app. Off: same network only.");

            // Hidden: only for running the relay locally while working on it.
            _relayUrl = _category.CreateEntry(
                "RelayUrl", DefaultRelayUrl,
                description: "Relay address.", is_hidden: true);
        }

        private const string DefaultRelayUrl = "wss://relay.doodesch.de/";

        internal static bool RelayEnabled => _relay?.Value ?? true;

        internal static string RelayUrl
        {
            get
            {
                string url = _relayUrl?.Value ?? "";
                return url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
                       || url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
                    ? url
                    : DefaultRelayUrl;
            }
        }

        internal static void SetRelayEnabled(bool on)
        {
            if (_relay == null || _relay.Value == on) return;

            _relay.Value = on;
            Save();
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
