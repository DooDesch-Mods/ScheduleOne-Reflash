using Reflash.Game;
using Il2CppScheduleOne.DevUtilities;
using UnityEngine;
using UnityEngine.UI;

namespace Reflash.Companion
{
    /// <summary>
    /// The home screen's app icons, as PNGs a browser can show.
    ///
    /// The companion is meant to look like the phone, and the phone's home screen is mostly artwork. Drawing a
    /// coloured square with a letter in it was the honest placeholder; this is the real thing, taken from the same
    /// Image components the player is looking at.
    ///
    /// Read off the live home screen rather than loaded by name, for the same reason the map picture is: which
    /// sprite an icon carries is a prefab decision, other mods add their own icons here, and a name table would go
    /// stale the first time either changed.
    ///
    /// Keyed by the icon's LABEL, because that is the only thing the icon and the companion's app list share - the
    /// GameObjects are all called "AppIcon(Clone)".
    /// </summary>
    internal static class Icons
    {
        private static readonly Dictionary<string, byte[]> Cache =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where each label sat on the home screen, so a second screen can show the same order.</summary>
        private static readonly Dictionary<string, int> Order =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static bool _scanned;

        /// <summary>
        /// The icon for a label, or null. Main thread only - it reads the scene and may encode a texture.
        /// </summary>
        internal static byte[] For(string label)
        {
            if (!_scanned) Scan();

            return !string.IsNullOrEmpty(label) && Cache.TryGetValue(label, out byte[] png) ? png : null;
        }

        /// <summary>
        /// Where this label sits on the phone's home screen, or a large number for one that is not there.
        ///
        /// The companion's app list arrives in registration order, which is an accident of which mod loaded first.
        /// The phone's own order is the one the player knows, so it is the one the second screen uses.
        /// </summary>
        internal static int PositionOf(string label)
        {
            if (!_scanned) Scan();

            return !string.IsNullOrEmpty(label) && Order.TryGetValue(label, out int at) ? at : int.MaxValue;
        }

        /// <summary>
        /// Walk the home screen once and keep what it finds. Once per session: the icons do not change, and each
        /// one costs a full texture readback.
        /// </summary>
        private static void Scan()
        {
            _scanned = true;

            try
            {
                var home = PlayerSingleton<Il2CppScheduleOne.UI.Phone.HomeScreen>.Instance;
                if (home == null) { _scanned = false; return; }   // the phone is not built yet - try again later

                Walk(home.transform, 0);
                Core.Log.Msg($"{Cache.Count} of {Order.Count} phone icons available to the companion.");

                // An icon that produced nothing may simply have been asked for too early - the phone builds itself
                // over several frames. Leaving the scan open lets a later request pick up what was missing, which
                // costs one walk of a hierarchy of thirty objects and is the difference between the second screen
                // showing artwork and showing initials.
                if (Cache.Count < Order.Count) _scanned = false;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"reading the phone icons failed: {e.Message}");
            }
        }

        private const int MaxDepth = 8;

        private static void Walk(Transform node, int depth)
        {
            if (node == null || depth > MaxDepth) return;

            // A button with a label under it is an app icon, whatever the GameObject happens to be called. That is
            // what makes this work for the icons other mods add as well as for the seven vanilla ones.
            if (node.GetComponent<Button>() != null)
            {
                string label = LabelUnder(node);
                if (label.Length > 0)
                {
                    if (!Order.ContainsKey(label)) Order[label] = Order.Count;

                    // Its POSITION is settled the first time; its PICTURE is retried until there is one, because a
                    // texture asked for before the phone finished building itself comes back empty.
                    if (!Cache.ContainsKey(label))
                    {
                        byte[] png = Artwork(node);
                        if (png != null) Cache[label] = png;
                    }
                }
            }

            for (int i = 0; i < node.childCount; i++) Walk(node.GetChild(i), depth + 1);
        }

        private static string LabelUnder(Transform icon)
        {
            for (int i = 0; i < icon.childCount; i++)
            {
                Transform child = icon.GetChild(i);

                var legacy = child.GetComponent<UnityEngine.UI.Text>();
                if (legacy != null && !string.IsNullOrEmpty(legacy.text)) return legacy.text.Trim();

                var tmp = child.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
                if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text.Trim();
            }

            return "";
        }

        /// <summary>
        /// The picture inside the icon's frame.
        ///
        /// Deepest sprite wins: the icon is a white rounded frame with a mask and the artwork inside it, so the
        /// outermost Image is the frame - which every icon shares and none of them is.
        /// </summary>
        private static byte[] Artwork(Transform icon)
        {
            Sprite best = null;
            int bestDepth = -1;

            FindSprite(icon, 0, ref best, ref bestDepth);
            if (best == null || best.texture == null) return null;

            byte[] png = TextureIO.EncodeSpritePng(best);
            if (png != null) return png;

            // Worth naming rather than swallowing: an icon that will not read back is the difference between the
            // companion looking like the phone and looking like a list of coloured letters, and which sprite it was
            // is the only thing that says why.
            Core.Log.Warning($"the icon '{best.name}' would not read back " +
                             $"(texture {best.texture.width}x{best.texture.height}, readable={best.texture.isReadable}, " +
                             $"format={best.texture.format}, rect={best.textureRect}) - that tile falls back to a letter.");
            return null;
        }

        private static void FindSprite(Transform node, int depth, ref Sprite best, ref int bestDepth)
        {
            var image = node.GetComponent<Image>();
            if (image != null && image.sprite != null && depth > bestDepth)
            {
                // The rounded frame and its mask are the same shared sprite on every icon, so they are never the
                // artwork - taking them would give all eight tiles the same picture.
                string name = image.sprite.name ?? "";
                if (name.IndexOf("Rectangle_Rounded", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    best = image.sprite;
                    bestDepth = depth;
                }
            }

            for (int i = 0; i < node.childCount; i++) FindSprite(node.GetChild(i), depth + 1, ref best, ref bestDepth);
        }
    }
}
