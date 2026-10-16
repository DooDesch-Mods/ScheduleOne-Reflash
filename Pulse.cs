using Reflash.Wire;
using Sideload.Api;
#if SNITCH
using Snitch.Api;
#endif

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

            /// <summary>The revision moved while nobody was looking, and the page has not been told yet.</summary>
            internal bool Missed;
#if SNITCH
            // Section names built once: a string per tick per app would be garbage four times a second.
            internal string WarmSection, ReadSection, EmitSection;
#endif
        }

        internal void Add(IAppPort port, AppHandle handle)
        {
            if (port == null || handle == null) return;

            var entry = new Entry { Port = port, Handle = handle };
#if SNITCH
            string app = port.Id.StartsWith("reflash-", StringComparison.Ordinal) ? port.Id.Substring(8) : port.Id;
            entry.WarmSection = "Reflash." + app + ".WarmUp";
            entry.ReadSection = "Reflash." + app + ".Revision";
            entry.EmitSection = "Reflash." + app + ".Emit";
#endif
            _entries.Add(entry);
        }

        /// <summary>
        /// The page just read its state, so it is current: nothing it missed while hidden is owed to it any more.
        /// Runs inside the page's own call, so it only records - an emit from here would re-enter the engine.
        /// </summary>
        internal void Heard(IAppPort port)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (!ReferenceEquals(e.Port, port)) continue;

                e.Missed = false;
                try { e.PushedRevision = port.Revision; }
                catch (Exception ex) { Core.Log.Warning($"'{port.Id}' revision unreadable while its page read state: {ex.Message}"); }
                return;
            }
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
                catch (Exception ex) { Core.Log.Error($"pulse for '{e.Port.Id}' failed: {ex.Message}"); }
            }
        }

        private static void TickOne(Entry e)
        {
            // Expensive one-off work belongs on a tick, never inside a page's call - see IWarmUp.
            if (e.Port is IWarmUp warm)
            {
#if SNITCH
                using (Profiler.Sample(e.WarmSection))
#endif
                warm.WarmUp();
            }

            int badge, revision;
#if SNITCH
            using (Profiler.Sample(e.ReadSection))
#endif
            {
                badge = e.Port.Badge;
                revision = e.Port.Revision;
            }

            if (badge != e.PushedBadge)
            {
                e.PushedBadge = badge;
                e.Handle.Badge(badge);
            }

            // A page that nobody is looking at is not told now: an event would rebuild a page nobody sees. It is told
            // the moment it is on screen instead, and only if something did change meanwhile. Waiting for it to
            // read fresh state on its own is not enough - Sideload builds every page while the save is loading, so
            // a page is already built when the player first opens it and reads nothing more. That is how the
            // messages list kept its letters in place of faces that had long since arrived.
            //
            // "Nobody" has to include the phone in someone's hand, though. A companion device can have an app open
            // while the in-game screen shows something else, and IsOnScreen knows nothing about it - which is how
            // the contacts graph sat there with empty circles long after every mugshot had been published.
            bool watched = e.Handle.IsOnScreen || Core.CompanionWatching;

            if (revision != e.PushedRevision)
            {
                // The very first reading is a baseline rather than a change: whatever page exists by now was built
                // from this state or from a later one.
                bool first = e.PushedRevision == int.MinValue;
                e.PushedRevision = revision;
                if (!watched) { e.Missed |= !first; return; }
            }
            else if (!(watched && e.Missed)) return;

            e.Missed = false;
#if SNITCH
            using (Profiler.Sample(e.EmitSection))
#endif
            e.Handle.Emit(e.Port.Id + ".changed", revision.ToString());
        }
    }
}
