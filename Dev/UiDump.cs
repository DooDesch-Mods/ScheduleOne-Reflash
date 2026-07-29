#if DEBUG
using System.Globalization;
using System.Text;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Reflash.Dev
{
    /// <summary>
    /// Writes the real numbers behind a vanilla screen: sizes, colours, font sizes, sprite names, the actual text.
    ///
    /// Rebuilding a screen from screenshots means guessing at every colour and every gap. This reads them instead,
    /// so "1:1" is something that can be checked rather than eyeballed - and a value that disagrees with the dump
    /// is simply wrong.
    ///
    /// Debug-only, and it writes to a file rather than the log: a whole app is a few hundred lines and the log is
    /// shared with every other mod.
    /// </summary>
    internal static class UiDump
    {
        /// <summary>
        /// Dump the app the phone currently has open. Deep enough to reach the leaves that carry the colours, but
        /// bounded - a runaway hierarchy would produce a file nobody reads.
        /// </summary>
        internal static void Active(string label)
        {
            GameObject app = Il2CppScheduleOne.UI.Phone.Phone.ActiveApp;
            if (app == null) { Core.Log.Warning("[Reflash/dump] no app open."); return; }

            Dump(app.transform, label.Length > 0 ? label : app.name);
        }

        /// <summary>Dump any subtree by the name of its root, for the parts that are not an app - the home screen.</summary>
        /// <summary>
        /// Whether a switched-off branch is written too.
        ///
        /// Normally it is not: an inactive branch is not on screen and is not what is being reproduced. But a vanilla
        /// screen this mod has taken over is never activated at all, so the only way to read the card the vanilla app
        /// would have drawn is to walk it switched off.
        /// </summary>
        private static bool _includeHidden;

        internal static void ByName(string name)
        {
            GameObject found = GameObject.Find(name);
            if (found == null)
            {
                var all = UnityEngine.Object.FindObjectsOfType<RectTransform>();
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    if (all[i] == null || all[i].name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found = all[i].gameObject;
                    break;
                }
            }

            if (found == null) { Core.Log.Warning($"[Reflash/dump] nothing named '{name}'."); return; }

            _includeHidden = true;
            try { Dump(found.transform, name); }
            finally { _includeHidden = false; }
        }

        private static void Dump(Transform root, string label)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(label).Append('\n');
            sb.Append("# every length in canvas units, colours as #rrggbbaa\n\n");

            Walk(root, sb, 0);

            try
            {
                string dir = Path.Combine(Environment.CurrentDirectory, "Mods", "reflash-dumps");
                Directory.CreateDirectory(dir);

                string file = Path.Combine(dir, Sanitise(label) + ".txt");
                File.WriteAllText(file, sb.ToString());

                Core.Log.Msg($"[Reflash/dump] wrote {file}");
            }
            catch (Exception e)
            {
                Core.Log.Error($"[Reflash/dump] could not write: {e.Message}");
            }
        }

        private const int MaxDepth = 14;

        private static void Walk(Transform node, StringBuilder sb, int depth)
        {
            if (node == null || depth > MaxDepth) return;

            // An inactive branch is not on screen and is not what is being reproduced - unless the caller is reading
            // a screen this mod has switched off, where hidden is the only state it is ever in.
            bool hidden = !node.gameObject.activeSelf;
            if (hidden && !_includeHidden) return;

            sb.Append(' ', depth * 2).Append(node.name);
            if (hidden) sb.Append(" (off)");

            var rect = node.GetComponent<RectTransform>();
            if (rect != null)
            {
                Rect r = rect.rect;
                sb.Append("  [").Append(F(r.width)).Append('x').Append(F(r.height)).Append(']');

                if (rect.anchoredPosition != Vector2.zero)
                    sb.Append(" at ").Append(F(rect.anchoredPosition.x)).Append(',').Append(F(rect.anchoredPosition.y));
            }

            Describe(node, sb);
            sb.Append('\n');

            for (int i = 0; i < node.childCount; i++) Walk(node.GetChild(i), sb, depth + 1);
        }

        /// <summary>The parts worth copying: what colour it is, what it says, how big the text is.</summary>
        private static void Describe(Transform node, StringBuilder sb)
        {
            var image = node.GetComponent<Image>();
            if (image != null)
            {
                sb.Append("  img=").Append(Hex(image.color));
                if (image.sprite != null) sb.Append(" sprite=").Append(image.sprite.name);
                if (image.type == Image.Type.Sliced) sb.Append(" sliced");
            }

            var raw = node.GetComponent<RawImage>();
            if (raw != null) sb.Append("  raw=").Append(Hex(raw.color));

            var legacy = node.GetComponent<UnityEngine.UI.Text>();
            if (legacy != null)
            {
                sb.Append("  text=").Append(Quote(legacy.text))
                  .Append(" size=").Append(legacy.fontSize)
                  .Append(" colour=").Append(Hex(legacy.color))
                  .Append(" align=").Append(legacy.alignment);
            }

            var tmp = node.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                sb.Append("  tmp=").Append(Quote(tmp.text))
                  .Append(" size=").Append(F(tmp.fontSize))
                  .Append(" colour=").Append(Hex(tmp.color))
                  .Append(" align=").Append(tmp.alignment);

                if (tmp.font != null) sb.Append(" font=").Append(tmp.font.name);
            }

            if (node.GetComponent<Button>() != null) sb.Append("  [button]");
            if (node.GetComponent<ScrollRect>() != null) sb.Append("  [scroll]");
            if (node.GetComponent<Mask>() != null || node.GetComponent<RectMask2D>() != null) sb.Append("  [mask]");

            var layout = node.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (layout != null)
                sb.Append("  layout=").Append(layout.GetType().Name.Replace("LayoutGroup", ""))
                  .Append(" spacing=").Append(F(layout.spacing))
                  .Append(" pad=").Append(layout.padding.left).Append(',').Append(layout.padding.top)
                  .Append(',').Append(layout.padding.right).Append(',').Append(layout.padding.bottom);

            var grid = node.GetComponent<GridLayoutGroup>();
            if (grid != null)
                sb.Append("  grid cell=").Append(F(grid.cellSize.x)).Append('x').Append(F(grid.cellSize.y))
                  .Append(" spacing=").Append(F(grid.spacing.x)).Append(',').Append(F(grid.spacing.y));
        }

        private static string Hex(Color c) =>
            "#" + ToByte(c.r) + ToByte(c.g) + ToByte(c.b) + ToByte(c.a);

        private static string ToByte(float v) =>
            Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255).ToString("x2", CultureInfo.InvariantCulture);

        private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>Text on one line, shortened - a description is not a transcript.</summary>
        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";

            string flat = s.Replace("\r", "").Replace("\n", " | ");
            if (flat.Length > 60) flat = flat.Substring(0, 57) + "...";

            return "\"" + flat + "\"";
        }

        private static string Sanitise(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            return sb.Length == 0 ? "dump" : sb.ToString();
        }
    }
}
#endif
