using Sideload.Api;
using UnityEngine;

namespace Reflash.Game
{
    /// <summary>
    /// Publishes the game's own sprites to one app, a few per tick, as <c>s1://&lt;prefix&gt;&lt;id&gt;</c>.
    ///
    /// Every screen that shows a person or a product shows its picture in vanilla, and those pictures are sprites
    /// the game already has - so this is a texture readback rather than anything invented. The readback is the whole
    /// reason this is a class and not two lines: a region of contacts is forty-five of them, and doing forty-five in
    /// one frame is a stutter you can see. They go out a few per tick instead, and the app's revision counts them so
    /// a blank circle fills in a moment later.
    ///
    /// One instance per app, because a runtime image belongs to the app handle it was published on.
    /// </summary>
    internal sealed class SpriteFeed
    {
        /// <summary>How many a single tick may cost. Four readbacks is well under a millisecond of frame time.</summary>
        private const int PerTick = 4;

        private readonly string _prefix;

        /// <summary>Everything already tried, whether or not a picture came of it.</summary>
        private readonly HashSet<string> _tried = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// How many times each id has come back with nothing.
        ///
        /// The game fills an NPC's mugshot in some time after the save loads, so an early read is a "not yet" and
        /// deserves another go - but a texture that will never read back deserves a limit, or it is retried four
        /// times a second for as long as the app is open.
        /// </summary>
        private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>About fifteen seconds of ticks, which is far longer than the game takes.</summary>
        private const int MaxAttempts = 24;

        /// <summary>Everything that actually HAS a picture. What the page is told about.</summary>
        private readonly HashSet<string> _ok = new HashSet<string>(StringComparer.Ordinal);

        private AppHandle _handle;
        private bool _warned;

        internal SpriteFeed(string prefix) => _prefix = prefix;

        internal void UseHandle(AppHandle handle) => _handle = handle;

        /// <summary>
        /// Bumped ONCE when a run of publishing finishes, not once per picture.
        ///
        /// This belongs in the app's revision so a page learns its blank circles have filled in. Putting the raw
        /// count there instead was measurably wrong: a revision change rebuilds the whole page at about half a
        /// millisecond per box, and handing out four pictures per tick meant six rebuilds of a seventy-box list -
        /// a quarter of a second of stutter that read as "the app takes forever to open".
        /// </summary>
        internal int Settled => _settled;

        private int _settled;
        private bool _unannounced;

        /// <summary>
        /// Whether there is a picture for this id - not merely whether one was attempted. The distinction matters:
        /// a page told "yes" puts an img there, and an img with nothing behind it paints nothing at all.
        /// </summary>
        internal bool Has(string id) => id != null && _ok.Contains(id);

        /// <summary>
        /// Publish up to <see cref="PerTick"/> of the given pictures. Safe to call every tick with the same list -
        /// anything already tried is skipped, and an entry whose sprite is not ready yet comes round again.
        /// </summary>
        internal void Warm(IEnumerable<KeyValuePair<string, Sprite>> pictures)
        {
            if (_handle == null || pictures == null) return;

            int done = 0;          // work attempted this tick, which is what the per-tick budget counts
            int published = 0;     // pictures that actually reached the page

            foreach (KeyValuePair<string, Sprite> picture in pictures)
            {
                if (done >= PerTick) break;

                string id = picture.Key;
                if (string.IsNullOrEmpty(id) || _tried.Contains(id)) continue;

                // The game builds mugshots after the save loads, so "no sprite yet" is a not-yet rather than a
                // never - skip without recording and this one comes round again on the next tick. Recording it
                // here instead would leave a permanently blank circle.
                Sprite sprite = picture.Value;
                if (sprite == null) continue;

                done++;

                byte[] png = TextureIO.EncodeSpritePng(sprite);
                if (png != null && png.Length > 0)
                {
                    _tried.Add(id);
                    _attempts.Remove(id);
                    _ok.Add(id);
                    _handle.Image(_prefix + id, png);
                    published++;
                    continue;
                }

                // Nothing came back. That is usually a picture the game has not drawn yet - a mugshot is rendered
                // some time after the save loads - so it is worth asking again rather than settling for the black
                // square an early read produces. Only after a good many tries is it recorded as hopeless.
                _attempts.TryGetValue(id, out int failures);
                _attempts[id] = ++failures;

                if (failures < MaxAttempts) continue;

                _tried.Add(id);
                if (_warned) continue;

                _warned = true;
                Core.Log.Warning($"a picture would not read back ({_prefix}{id}, sprite='{sprite.name}', " +
                                 $"texture={(sprite.texture == null ? "none" : sprite.texture.width + "x" + sprite.texture.height)}) " +
                                 "- those rows fall back to text.");
            }

            // A pass that published something means there may be more; a pass that published nothing means the set
            // is complete for now. Telling the page only at that point costs it one rebuild instead of one per
            // batch. Counted on PUBLISHES rather than on work done, or a picture the game has not drawn yet would
            // keep the pass looking busy for ever and the page would never be told about the ones that did arrive.
            if (published > 0) { _unannounced = true; return; }

            if (_unannounced) { _unannounced = false; _settled++; }
        }
    }
}
