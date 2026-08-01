#if DEBUG
using System.IO;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Phone;
using UnityEngine;

namespace Reflash.Dev
{
    /// <summary>
    /// Drives the phone from outside the game, for taking reference shots and for reproducing a screen without
    /// clicking to it by hand.
    ///
    /// Debug builds only - it is a development tool, not a feature, and a released mod has no business watching a
    /// file in the Mods folder.
    ///
    /// Driven by a file rather than a hotkey because whoever is using it has no keyboard: write a word into
    /// Mods/reflash.cmd and it runs on the main thread and deletes the file. A console command would be tidier, but
    /// the game's command dictionary is private and reaching it through IL2CPP is a guess.
    ///
    /// Every route goes through a real Button.onClick, so what gets photographed is what a player would see.
    /// </summary>
    internal static class Poke
    {
        private static string _path;
        private static float _next;

        internal static void Tick(float unscaledTime)
        {
            _path ??= Path.Combine(Environment.CurrentDirectory, "Mods", "reflash.cmd");

            // Twice a second. Checking a file every frame would show up in the render timings being measured.
            if (unscaledTime < _next) return;
            _next = unscaledTime + 0.5f;

            string cmd;
            try
            {
                if (!File.Exists(_path)) return;
                cmd = File.ReadAllText(_path).Trim().ToLowerInvariant();
                File.Delete(_path);
            }
            catch { return; }   // still being written; the next tick gets it

            if (cmd.Length == 0) return;

            Core.Log.Msg($"[Reflash/dev] {cmd}");
            try { Run(cmd); }
            catch (Exception e) { Core.Log.Error($"[Reflash/dev] '{cmd}' threw: {e}"); }
        }

        private static void Run(string cmd)
        {
            if (cmd.StartsWith("click ")) { ClickIcon(cmd.Substring(6).Trim()); return; }
            if (cmd.StartsWith("tap ")) { TapNamed(cmd.Substring(4).Trim()); return; }
            if (cmd.StartsWith("dump ")) { UiDump.Active(cmd.Substring(5).Trim()); return; }
            if (cmd.StartsWith("dumpname ")) { UiDump.ByName(cmd.Substring(9).Trim()); return; }
            if (cmd.StartsWith("press ")) { PressAnywhere(cmd.Substring(6).Trim()); return; }

            switch (cmd)
            {
                case "phone": OpenPhone(); break;
                case "close": ClosePhone(); break;
                case "home": HomeScreen(); break;
                case "icons": ListIcons(); break;
                case "buttons": ListButtons(); break;
                case "pair": Pair(); break;
                default: Core.Log.Warning($"[Reflash/dev] unknown: {cmd}"); break;
            }
        }

        private static void OpenPhone()
        {
            // The overlay is what actually raises the phone model. Phone.SetIsOpen alone only flips a flag and
            // fires events, leaving the handset stowed and every app invisible - which looks exactly like an app
            // that failed to render.
            if (Singleton<Il2CppScheduleOne.UI.GameplayMenu>.InstanceExists)
                Singleton<Il2CppScheduleOne.UI.GameplayMenu>.Instance.Open();

            var phone = PlayerSingleton<Phone>.Instance;
            if (phone == null) { Core.Log.Warning("[Reflash/dev] no Phone."); return; }

            if (!phone.IsOpen) phone.SetIsOpen(true);
            Core.Log.Msg($"[Reflash/dev] phone open={phone.IsOpen}");
        }

        /// <summary>
        /// Put a live pairing URL in the log.
        ///
        /// The same code the Connect app shows as a QR. Scanning a QR off a screenshot is not something a test
        /// harness can do, and reaching the app's own state means the app has to be open - so this is the way a
        /// browser gets paired without a pair of eyes and a phone camera.
        /// </summary>
        private static void Pair()
        {
            string url = Core.CompanionPairingUrl();
            Core.Log.Msg(string.IsNullOrEmpty(url)
                ? "[Reflash/dev] the companion server is not running."
                : "[Reflash/dev] pair at " + url);
        }

        private static void ClosePhone()
        {
            var phone = PlayerSingleton<Phone>.Instance;
            if (phone != null && phone.IsOpen) phone.SetIsOpen(false);
        }

        /// <summary>Back to the grid, so the next shot starts from a known place.</summary>
        private static void HomeScreen()
        {
            var phone = PlayerSingleton<Phone>.Instance;
            if (phone == null) return;

            phone.RequestCloseApp();
            OpenPhone();
        }

        private static RectTransform IconContainer()
        {
            var home = PlayerSingleton<Il2CppScheduleOne.UI.Phone.HomeScreen>.Instance;
            return home == null ? null : home.appIconContainer;
        }

        /// <summary>
        /// Names every icon. Needed because the seven vanilla ones are all called "AppIcon(Clone)" - they cannot be
        /// told apart by name, only by position, which is why <see cref="ClickIcon"/> takes an index too.
        /// </summary>
        private static void ListIcons()
        {
            RectTransform container = IconContainer();
            if (container == null) { Core.Log.Warning("[Reflash/dev] no HomeScreen."); return; }

            Core.Log.Msg($"[Reflash/dev] {container.childCount} icons");
            for (int i = 0; i < container.childCount; i++)
            {
                Transform child = container.GetChild(i);
                string label = LabelOf(child);
                Core.Log.Msg($"[Reflash/dev]   [{i}] {child.name}{(label.Length > 0 ? " - " + label : "")}");
            }
        }

        /// <summary>The caption under an icon, which is how a vanilla app can actually be identified.</summary>
        private static string LabelOf(Transform icon)
        {
            Transform label = icon.Find("Label");
            var text = label != null ? label.GetComponent<UnityEngine.UI.Text>() : null;
            return text != null ? text.text : "";
        }

        internal static void ClickIcon(string which)
        {
            OpenPhone();

            RectTransform container = IconContainer();
            if (container == null) return;

            Transform target = null;

            if (int.TryParse(which, out int index))
            {
                if (index >= 0 && index < container.childCount) target = container.GetChild(index);
            }
            else
            {
                for (int i = 0; i < container.childCount; i++)
                {
                    Transform child = container.GetChild(i);
                    if (child.name.IndexOf(which, StringComparison.OrdinalIgnoreCase) >= 0
                        || LabelOf(child).IndexOf(which, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        target = child;
                        break;
                    }
                }
            }

            if (target == null) { Core.Log.Warning($"[Reflash/dev] no icon '{which}' - run 'icons'."); return; }

            var button = target.GetComponent<UnityEngine.UI.Button>();
            if (button == null) { Core.Log.Warning($"[Reflash/dev] '{target.name}' has no Button."); return; }

            Core.Log.Msg($"[Reflash/dev] clicking '{target.name}' ({LabelOf(target)})");
            button.onClick.Invoke();
        }

        /// <summary>
        /// Every interactive thing on the open app, so a sub-screen can be reached without guessing. Only what is
        /// visible - a disabled or hidden button is not a route anywhere.
        /// </summary>
        private static void ListButtons()
        {
            GameObject active = Phone.ActiveApp;
            if (active == null) { Core.Log.Warning("[Reflash/dev] no app open."); return; }

            var buttons = active.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            Core.Log.Msg($"[Reflash/dev] {(buttons == null ? 0 : buttons.Length)} buttons in '{active.name}'");

            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || !buttons[i].isActiveAndEnabled) continue;
                Core.Log.Msg($"[Reflash/dev]   {PathOf(buttons[i].transform, active.transform)}");
            }
        }

        /// <summary>
        /// Press a button on the open app by a fragment of its path.
        ///
        /// A trailing <c>#n</c> takes the n-th match instead of the first, which a vanilla list needs: every row of
        /// a conversation list is an "Entry(Clone)/Button" and they are told apart by position alone.
        /// </summary>
        private static void TapNamed(string fragment)
        {
            GameObject active = Phone.ActiveApp;
            if (active == null) { Core.Log.Warning("[Reflash/dev] no app open."); return; }

            int wanted = 0;
            int hash = fragment.LastIndexOf('#');
            if (hash > 0 && int.TryParse(fragment.Substring(hash + 1), out int nth))
            {
                wanted = nth;
                fragment = fragment.Substring(0, hash).Trim();
            }

            var buttons = active.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            if (buttons == null) return;

            int seen = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                UnityEngine.UI.Button b = buttons[i];
                if (b == null || !b.isActiveAndEnabled) continue;

                if (PathOf(b.transform, active.transform).IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (seen++ != wanted) continue;

                Core.Log.Msg($"[Reflash/dev] tapping {PathOf(b.transform, active.transform)} (#{wanted}, '{CaptionOf(b.transform)}')");
                b.onClick.Invoke();
                return;
            }

            Core.Log.Warning($"[Reflash/dev] no button #{wanted} matching '{fragment}' - run 'buttons'.");
        }

        /// <summary>
        /// Press any visible button anywhere on screen, found by its own name or its caption.
        ///
        /// Needed for the things that are not phone apps and still block everything: an arrest notice, a
        /// confirmation, a shop screen. Without it a session can be stuck behind a dialog with no way to dismiss it,
        /// because this tool has no keyboard and no mouse.
        /// </summary>
        private static void PressAnywhere(string which)
        {
            var buttons = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>();
            if (buttons == null) { Core.Log.Warning("[Reflash/dev] no buttons on screen."); return; }

            for (int i = 0; i < buttons.Length; i++)
            {
                UnityEngine.UI.Button b = buttons[i];
                if (b == null || !b.isActiveAndEnabled || !b.gameObject.activeInHierarchy) continue;

                if (b.name.IndexOf(which, StringComparison.OrdinalIgnoreCase) < 0
                    && CaptionOf(b.transform).IndexOf(which, StringComparison.OrdinalIgnoreCase) < 0) continue;

                Core.Log.Msg($"[Reflash/dev] pressing '{b.name}' ({CaptionOf(b.transform)})");
                b.onClick.Invoke();
                return;
            }

            Core.Log.Warning($"[Reflash/dev] no visible button matching '{which}'.");
        }

        /// <summary>Whatever text a button carries, legacy or TextMeshPro.</summary>
        private static string CaptionOf(Transform node)
        {
            var legacy = node.GetComponentInChildren<UnityEngine.UI.Text>(false);
            if (legacy != null && !string.IsNullOrEmpty(legacy.text)) return legacy.text;

            var tmp = node.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(false);
            return tmp != null && !string.IsNullOrEmpty(tmp.text) ? tmp.text : "";
        }

        private static string PathOf(Transform node, Transform root)
        {
            string path = node.name;
            Transform p = node.parent;

            while (p != null && p != root)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }

            return path;
        }
    }
}
#endif
