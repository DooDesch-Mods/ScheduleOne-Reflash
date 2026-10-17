namespace Reflash.Companion
{
    /// <summary>
    /// Which companion session the call now running came from, or null when the in-game phone made it.
    ///
    /// An app that opens another one reads this to open it where the press was. "Show on map" pressed on the phone
    /// in the player's hand opens the map there, not on the phone in the game.
    ///
    /// A plain static is enough: it is set only around a batch of companion calls, on the main thread, and those
    /// calls run one after another.
    /// </summary>
    internal static class CallOrigin
    {
        internal static Sessions.Session Current { get; private set; }

        internal static T Run<T>(Sessions.Session session, Func<T> body)
        {
            Sessions.Session before = Current;
            Current = session;
            try { return body(); }
            finally { Current = before; }
        }
    }
}
