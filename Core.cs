using MelonLoader;
using Reflash.Companion;
using Reflash.Hijack;
using Reflash.Screens;
using Reflash.Wire;
using Sideload.Api;
using UnityEngine;

[assembly: MelonInfo(typeof(Reflash.Core), "Reflash", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Reflash")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonOptionalDependencies("Sideload")]

namespace Reflash
{
    /// <summary>
    /// Rebuilds the seven vanilla phone apps as HTML and serves them to a real phone on the local network.
    ///
    /// By default that second screen is the only place they appear: the in-game phone keeps its original apps, and
    /// Reflash adds one icon, Connect, which pairs a phone. The replacements can take the in-game icons over as
    /// well - <c>ReplaceVanillaApps</c> - and that is how they will arrive there, one screen at a time, once each
    /// has been through enough real hands.
    ///
    /// Nothing here reimplements a game rule: every read comes from the same managers the vanilla screens read, and
    /// every write goes through the same ServerRpc the vanilla screens call - all of them
    /// <c>RequireOwnership = false</c>, so a client may act and the server rebroadcasts, exactly as before.
    ///
    /// Wiring only. The apps live under Screens/ (wire protocol, no engine reference) and Game/ (the managers), and
    /// the companion under Companion/.
    /// </summary>
    public sealed class Core : MelonMod
    {
        internal static MelonLogger.Instance Log;

        private readonly Pulse _pulse = new Pulse();
        private Runner _companion;

        /// <summary>Reached only by the debug tooling, which has no phone camera to scan the Connect app's QR.</summary>
        private static Runner _running;

        /// <summary>
        /// Whether a phone is paired and therefore watching.
        ///
        /// The pulse needs this because "on screen" is not the same question any more: an app can be open on a
        /// companion device while the in-game phone shows something else entirely, and an app told nothing is an app
        /// whose page never re-reads. That is what left the contacts graph full of empty circles - the mugshots had
        /// arrived, and the only device looking at them was never told.
        /// </summary>
        internal static bool CompanionWatching =>
            _running != null && _running.Running && _running.Server.Devices.Count > 0;

        internal static string CompanionPairingUrl() =>
            _running == null || !_running.Running
                ? ""
                : "http://" + _running.Server.LanIp + ":" + _running.Server.Port + "/#t=" +
                  _running.Server.Devices.CurrentToken(Companion.Clock.Now);

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Prefs.Load();

            // Refuse rather than half-work. Without iconless apps that can be opened from code, the takeover would
            // hide nothing and open nothing, and the player would be left with seven apps they cannot reach. A
            // version number in the log is fixable; an unreachable phone is not.
            if (!AppHandle.CanOpenProgrammatically)
            {
                Log.Error(Apps.Available
                    ? "Sideload is too old - Reflash needs 1.1.0 or newer for iconless apps. The vanilla phone is untouched."
                    : "Sideload is not installed - Reflash does nothing without it. The vanilla phone is untouched.");
                return;
            }

            RegisterReplacements();
            AppHijack.Arm(Prefs.TakeOverVanillaApps);

            if (!Prefs.TakeOverVanillaApps)
                Log.Msg("the in-game phone keeps its own apps. The replacements are registered anyway - " +
                        "that is what the companion serves. Set ReplaceVanillaApps to try them in the game.");

            // MelonLoader hands every mod its own Harmony instance and unpatches it on unload, which is one less
            // lifetime to get wrong than creating one here. Applied even with the takeover off: the prefixes ask
            // AppHijack.Ready every time, so they cost a branch and can be switched on without a restart.
            SetOpenPatches.ApplyAll(HarmonyInstance);

            StartCompanion();
        }

        private void RegisterReplacements()
        {
            foreach (IAppPort port in AppRoster.All())
            {
                AppHandle handle = Apps
                    .Register(port.Id, "Reflash.Assets." + port.Id, AppRoster.TitleOf(port.Replaces))
                    .NoIcon()
                    .Orientation(AppRoster.OrientationsOf(port.Replaces));

                Wire(port, handle);
                AppHijack.Register(port.Replaces, handle);
            }
        }

        /// <summary>
        /// Connect, and the companion behind it. The app is registered whether or not the server is running: with
        /// it off the app is the switch that turns it on, which is the only reason a player would go looking for
        /// this screen in the first place.
        ///
        /// This one keeps its icon. It is the mod's single mark on the in-game phone.
        /// </summary>
        private void StartCompanion()
        {
            AppHandle connect = Apps
                .Register("reflash-connect", "Reflash.Assets.reflash-connect", "Connect", "Connect")
                .Orientation("landscape", "portrait");

            // Connect replaces nothing, so no prefix routes to it - but it still has to close when a vanilla app
            // opens, or it sits on top of one.
            AppHijack.Watch(connect);

            _companion = new Runner();
            _running = _companion;

            Wire(new ConnectApp(_companion, connect), connect);

            _companion.FollowPreference();
        }

        private void Wire(IAppPort port, AppHandle handle)
        {
            // Only the map needs its own handle, to publish the extracted map picture as s1://map.
            if (port is INeedsAppHandle needs) needs.UseHandle(handle);

            handle.OnCall(port.Id + ".state", port.State)
                  .OnCall(port.Id + ".act", raw => port.Act(Cmd.Parse(raw)));

            _pulse.Add(port, handle);
        }

        public override void OnUpdate()
        {
            _pulse.Tick(Time.unscaledTime);

            // Guarded on its own. Pump hands work from connection threads to this one, and a mod that can throw out
            // of OnUpdate is a mod that can take a frame - or the session - with it.
            try { _companion?.Pump(); }
            catch (Exception e) { Log.Error($"companion pump failed: {e.Message}"); }

#if DEBUG
            Dev.Poke.Tick(Time.unscaledTime);
#endif
        }

        public override void OnApplicationQuit()
        {
            // Logged so a session that ends can be told apart from one that is ENDED. Four times this game vanished
            // with nothing in any log, and without this line there is no way to know whether it quit or was killed.
            Log.Msg("the game is quitting - shutting the companion down.");

            _companion?.Shutdown();
        }
    }
}
