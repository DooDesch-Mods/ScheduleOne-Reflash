using Reflash.Wire;
using Sideload.Api;

namespace Reflash
{
    /// <summary>
    /// Tells pages when to read again, and keeps the icon badges right.
    ///
    /// Pull, not push. Each app reports a revision number that changes when anything it shows has changed; when it
    /// differs from what was last pushed, the app gets one <c>&lt;app&gt;.changed</c> event carrying that number and
    /// fetches what it needs itself. An idle app therefore costs one integer comparison per tick instead of a
    /// serialisation, and a burst of changes between two ticks collapses into a single event.
    ///
    /// Everything emits from HERE, never from inside a state or command handler: those run inside the page's own
    /// script engine, and emitting from within one re-enters the engine that is currently executing it.
    /// </summary>
    internal sealed class Pulse
    {
        /// <summary>
        /// Four times a second. Fast enough that a reply arriving feels immediate, slow enough that seven revision
        /// checks per tick are free. The badge is what has to keep up when nothing is open, and nobody notices a
        /// quarter second on a badge.
        /// </summary>
        private const float IntervalSeconds = 0.25f;

        private readonly List<Entry> _entries = new List<Entry>();
        private float _next;

        private sealed class Entry
        {
            internal IAppPort Port;
            internal AppHandle Handle;
            internal int PushedRevision = int.MinValue;
            internal int PushedBadge = -1;
        }

        internal void Add(IAppPort port, AppHandle handle)
        {
            if (port == null || handle == null) return;
            _entries.Add(new Entry { Port = port, Handle = handle });
        }

        /// <summary>Driven from OnUpdate. Never throws outward - one app misbehaving must not stop the other six.</summary>
        internal void Tick(float unscaledTime)
        {
            if (unscaledTime < _next) return;
            _next = unscaledTime + IntervalSeconds;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                try { TickOne(e); }
                catch (Exception ex) { Core.Log.Error($"[Reflash] pulse for '{e.Port.Id}' failed: {ex.Message}"); }
            }
        }

        private static void TickOne(Entry e)
        {
            // Expensive one-off work belongs on a tick, never inside a page's call - see IWarmUp.
            if (e.Port is IWarmUp warm) warm.WarmUp();

            int badge = e.Port.Badge;
            if (badge != e.PushedBadge)
            {
                e.PushedBadge = badge;
                e.Handle.Badge(badge);
            }

            int revision = e.Port.Revision;
            if (revision == e.PushedRevision) return;

            e.PushedRevision = revision;

            // A page that nobody is looking at is not told: it will read fresh state when it is opened, so an event
            // now would be work nobody sees, and the revision is already recorded so nothing is lost.
            //
            // "Nobody" has to include the phone in someone's hand, though. A companion device can have an app open
            // while the in-game screen shows something else, and IsOnScreen knows nothing about it - which is how
            // the contacts graph sat there with empty circles long after every mugshot had been published.
            if (e.Handle.IsOnScreen || Core.CompanionWatching)
                e.Handle.Emit(e.Port.Id + ".changed", revision.ToString());
        }
    }
}
