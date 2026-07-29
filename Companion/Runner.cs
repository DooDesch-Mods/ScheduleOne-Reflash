namespace Reflash.Companion
{
    /// <summary>
    /// Owns the companion: the seam into Sideload, the server, and the on/off switch behind the Connect app.
    ///
    /// It is a switch rather than a start-up decision because of what the mod is for. The in-game phone stays
    /// vanilla; the companion is the whole feature, and asking someone to close the game, find MelonPreferences.cfg
    /// and start over is not a way to offer it. Pressing the button in the Connect app is - and pressing it also
    /// writes the preference, so the next session starts the way this one ended.
    ///
    /// Everything here runs on the Unity main thread. The server keeps its own threads and marshals back.
    /// </summary>
    internal sealed class Runner
    {
        private readonly Bundles _bundles = new Bundles();
        private readonly CompanionServer _server;

        private bool _bound;
        private bool _mirroring;

        internal Runner() => _server = new CompanionServer(_bundles);

        internal CompanionServer Server => _server;

        internal bool Running => _server.Running;

        /// <summary>
        /// Why the last attempt to turn it on failed, for the Connect app to show. Empty when nothing is wrong -
        /// which is not the same as running, so the page can tell "off" from "would not start".
        /// </summary>
        internal string Problem { get; private set; } = "";

        /// <summary>Follow the preference at start-up. Silent when it is off: that is the default, not a fault.</summary>
        internal void FollowPreference()
        {
            if (Prefs.CompanionEnabled) TurnOn();
            else Core.Log.Msg("[Reflash] the phone companion is off. Turn it on in the Connect app on the in-game phone.");
        }

        /// <summary>
        /// Start serving, and remember it. Returns false with <see cref="Problem"/> set - a port already in use is
        /// the ordinary failure and the player is the only one who can do anything about it.
        /// </summary>
        internal bool TurnOn()
        {
            Problem = "";

            if (_server.Running) return true;

            if (!_bound) _bound = _bundles.Bind();
            if (!_bound)
            {
                Problem = "Sideload is too old for the companion - it needs 1.1.0 or newer.";
                return false;
            }

            if (!_server.Start(Prefs.CompanionPort))
            {
                Problem = "Port " + Prefs.CompanionPort + " is already taken. Set CompanionPort in "
                        + "MelonPreferences.cfg to a free one.";
                return false;
            }

            if (!_mirroring)
            {
                _bundles.MirrorTo(_server.Devices);
                _mirroring = true;
            }

            Prefs.SetCompanionEnabled(true);
            return true;
        }

        /// <summary>
        /// Stop serving, and remember that too. The taps come off as well: with nobody listening they would be a
        /// string built per event for no reader.
        /// </summary>
        internal void TurnOff()
        {
            Problem = "";

            if (_mirroring)
            {
                _bundles.StopMirroring();
                _mirroring = false;
            }

            _server.Stop();
            Prefs.SetCompanionEnabled(false);

            Core.Log.Msg("[Reflash] the phone companion is off.");
        }

        internal void Pump() => _server.Pump();

        /// <summary>Quitting. Unlike <see cref="TurnOff"/> this must not touch the preference - the player did not
        /// ask for it to be off, the game simply ended.</summary>
        internal void Shutdown()
        {
            if (_mirroring)
            {
                _bundles.StopMirroring();
                _mirroring = false;
            }

            _server.Stop();
        }
    }
}
