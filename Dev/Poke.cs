#if DEBUG
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Phone;
using UnityEngine;

namespace Reflash.Dev
{
    /// <summary>
    /// Drives the phone from the dev console, for taking reference shots and for reproducing a screen without
    /// clicking to it by hand: <c>reflash phone</c>, <c>reflash click messages</c>, <c>reflash tap Entry#2</c>.
    ///
    /// Debug builds only - it is a development tool, not a feature. A console command rather than a hotkey because
    /// whoever is using it has no keyboard: the bridge can submit a console line, it cannot press a key.
    ///
    /// Every route goes through a real Button.onClick, so what gets photographed is what a player would see.
    /// </summary>
    internal static class Poke
    {
        private const string Word = "reflash";

        private static int _lastFrame = -1;
        private static string _lastLine = "";

        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim());
        }

        internal static bool TryHandle(Il2CppSystem.Collections.Generic.List<string> args)
        {
            if (args == null || args.Count == 0) return false;
            var parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++) parts[i] = args[i];
            return Dispatch(string.Join(" ", parts));
        }

        /// <summary>True when the line was ours and the game must not see it.</summary>
        private static bool Dispatch(string line)
        {
            if (!line.StartsWith(Word, StringComparison.OrdinalIgnoreCase)) return false;
            if (line.Length > Word.Length && line[Word.Length] != ' ') return false;

            // The string overload calls the list overload, so one submission can arrive twice in the same frame.
            int frame = Time.frameCount;
            if (frame == _lastFrame && line == _lastLine) return true;
            _lastFrame = frame;
            _lastLine = line;

            string cmd = line.Substring(Word.Length).Trim().ToLowerInvariant();
            if (cmd.Length == 0) cmd = "help";

            Core.Log.Msg($"[Reflash/dev] {cmd}");
            try { Run(cmd); }
            catch (Exception e) { Core.Log.Error($"[Reflash/dev] '{cmd}' threw: {e}"); }
            return true;
        }

        private static void Run(string cmd)
        {
            if (cmd.StartsWith("click ")) { ClickIcon(cmd.Substring(6).Trim()); return; }
            if (cmd.StartsWith("tap ")) { TapNamed(cmd.Substring(4).Trim()); return; }
            if (cmd.StartsWith("dump ")) { UiDump.Active(cmd.Substring(5).Trim()); return; }
            if (cmd.StartsWith("dumpname ")) { UiDump.ByName(cmd.Substring(9).Trim()); return; }
            if (cmd.StartsWith("press ")) { PressAnywhere(cmd.Substring(6).Trim()); return; }
            if (cmd.StartsWith("takeover ")) { TakeOver(cmd.Substring(9).Trim()); return; }
            if (cmd == "fonts") { Fonts(); return; }
            if (cmd.StartsWith("scroll ")) { Scroll(cmd.Substring(7).Trim()); return; }
            if (cmd.StartsWith("sprite ")) { SpriteProbe(cmd.Substring(7).Trim()); return; }
            if (cmd.StartsWith("export ")) { ExportSprite(cmd.Substring(7).Trim()); return; }

            switch (cmd)
            {
                case "phone": OpenPhone(); break;
                case "close": ClosePhone(); break;
                case "home": HomeScreen(); break;
                case "icons": ListIcons(); break;
                case "buttons": ListButtons(); break;
                case "pair": Pair(); break;
                case "counter": CounterProbe(); break;
                case "faces": FacesProbe(); break;
                case "help": Help(); break;
                default: Core.Log.Warning($"[Reflash/dev] unknown: {cmd}"); break;
            }
        }

        /// <summary>
        /// Name every reference <c>CounterofferInterface.Open</c> dereferences, so a throw inside it points at one.
        /// </summary>
        private static void CounterProbe()
        {
            CounterofferInterface c = Game.Sheets.Counter;
            if (c == null) { Core.Log.Warning("[Reflash/dev] counter: no CounterofferInterface."); return; }

            string N(UnityEngine.Object o) => o == null ? "NULL" : "ok";
            Core.Log.Msg($"[Reflash/dev] counter: open={c.IsOpen} active={c.gameObject.activeInHierarchy} "
                       + $"Container={N(c.Container)} ProductIcon={N(c.ProductIcon)} ProductLabel={N(c.ProductLabel)} "
                       + $"FairPriceLabel={N(c.FairPriceLabel)} ProductSelector={N(c.ProductSelector)} "
                       + $"ProductAmountInput={N(c.ProductAmountInput)} PriceSelector={N(c.PriceSelector)} "
                       + $"uiScreen={N(c.uiScreen)} uiPanel={N(c.uiPanel)} TitleLabel={N(c.TitleLabel)} "
                       + $"UIScreenManager={Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UIScreenManager>.InstanceExists} "
                       + $"conversation={(c.conversation == null ? "NULL" : "ok")} product={N(c.selectedProduct)} "
                       + $"callback={(c.orderConfirmedCallback == null ? "NULL" : "ok")}");
        }

        /// <summary>
        /// One line per conversation on the messages list: whether the game has a picture for its sender, and where
        /// each face feed stands with it.
        /// </summary>
        private static void FacesProbe()
        {
            var active = Il2CppScheduleOne.UI.Phone.Messages.MessagesApp.ActiveConversations;
            if (active == null) { Core.Log.Warning("[Reflash/dev] faces: no active conversations list."); return; }

            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c == null) { Core.Log.Msg($"[Reflash/dev] faces #{i}: null"); continue; }

                var who = c._sender;
                string id = who == null ? null : who._npcId;
                Sprite icon = who == null ? null : who.Icon;
                string tex = icon == null ? "none" : (icon.texture == null ? "no-texture" : icon.texture.width + "x" + icon.texture.height);

                var feeds = new List<string>();
                foreach (Game.SpriteFeed f in Game.SpriteFeed.All)
                    if (f.Prefix == "face-") feeds.Add(f.Explain(id));

                Core.Log.Msg($"[Reflash/dev] faces #{i}: {c.ContactName} id={id ?? "none"} known={c.IsSenderKnown} "
                           + $"visible={c.EntryVisible} icon={tex} feeds=[{string.Join(",", feeds)}]");
            }
        }

        /// <summary>
        /// Move every scroll list of the open app to a position, 0 the top and 1 the bottom - the same thing a wheel
        /// or a drag does, so the clips a scroll rewrites can be photographed.
        /// </summary>
        private static void Scroll(string arg)
        {
            if (!float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float to))
            {
                Core.Log.Warning($"[Reflash/dev] scroll: '{arg}' is not a number between 0 and 1.");
                return;
            }

            int moved = 0;
            foreach (UnityEngine.UI.ScrollRect s in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.ScrollRect>())
            {
                if (s == null || !s.isActiveAndEnabled || s.content == null || s.gameObject.name != "scroll-viewport") continue;
                s.verticalNormalizedPosition = 1f - Mathf.Clamp01(to);
                moved++;
            }

            Core.Log.Msg($"[Reflash/dev] scroll: moved {moved} list(s) to {to:0.##}.");
        }

        private static void Help()
        {
            Core.Log.Msg("[Reflash/dev] reflash phone | close | home | icons | buttons | pair | click <icon|index> | "
                       + "tap <path>[#n] | counter | faces | scroll <0..1> | press <name> | takeover on|off | fonts | dump <label> | dumpname <name> | "
                       + "sprite <npc id | product id> | export <sprite name>");
        }

        /// <summary>
        /// Write one of the game's sprites, found by its asset name, to reflash-dumps as a PNG. This is where the
        /// pages' glyphs come from: the marker icons are the game's own pictures, not redrawn ones.
        /// </summary>
        private static void ExportSprite(string name)
        {
            Sprite found = null;
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase)) { found = s; break; }

            if (found == null) { Core.Log.Warning($"[Reflash/dev] no loaded sprite named '{name}'."); return; }

            byte[] png = Game.TextureIO.EncodeSpritePng(found);
            if (png == null || png.Length == 0) { Core.Log.Warning($"[Reflash/dev] '{found.name}' did not read back."); return; }

            string dir = System.IO.Path.Combine(Environment.CurrentDirectory, "Mods", "reflash-dumps");
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, found.name + ".png");
            System.IO.File.WriteAllBytes(file, png);
            Core.Log.Msg($"[Reflash/dev] exported '{found.name}' {found.rect.width}x{found.rect.height} to {file}");
        }

        /// <summary>
        /// What the mod hands a page for one NPC's mugshot or one product's icon: the texture's flags, the sprite's
        /// full rect against the region its pixels are read from, and the pixel in its middle as the published PNG
        /// carries it. Set against the same picture in a vanilla screenshot, this says whether a face that looks too
        /// dark or an icon that looks too big was read wrong or drawn wrong.
        /// </summary>
        private static void SpriteProbe(string id)
        {
            Sprite sprite = null;

            Il2CppScheduleOne.NPCs.NPC npc = Il2CppScheduleOne.NPCs.NPCManager.GetNPC(id);
            if (npc != null) sprite = npc.MugshotSprite;
            else
            {
                var item = Il2CppScheduleOne.Registry.GetItem(id);
                if (item == null) { Core.Log.Warning($"[Reflash/dev] no NPC or item '{id}'."); return; }
                sprite = item.Icon;
            }

            if (sprite == null || sprite.texture == null) { Core.Log.Warning($"[Reflash/dev] '{id}' has no picture."); return; }

            Texture2D t = sprite.texture;
            Core.Log.Msg($"[Reflash/dev] sprite '{sprite.name}' tex '{t.name}' {t.width}x{t.height} format={t.format} "
                       + $"graphics={t.graphicsFormat} readable={t.isReadable} rect={sprite.rect} "
                       + $"textureRect={sprite.textureRect} offset={sprite.textureRectOffset} packed={sprite.packed}");

            Color32[] px = Game.TextureIO.SpritePixels(sprite, out int w, out int h);
            if (px == null) { Core.Log.Warning("[Reflash/dev] pixels unreadable."); return; }

            for (int row = 0; row < 5; row++)
            {
                int y = h * (row + 3) / 10;
                Color32 c = px[y * w + w / 2];
                Core.Log.Msg($"[Reflash/dev] pixel x={w / 2} y={y} (from top {h - 1 - y}) = {c.r},{c.g},{c.b},{c.a}");
            }
        }

        /// <summary>
        /// Flip which screen the phone icons open, the same live switch the Connect app has. Saved like it, so a
        /// test that turns it on should turn it off again.
        /// </summary>
        private static void Fonts()
        {
            foreach (var f in UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppTMPro.TMP_FontAsset>())
                if (f != null) Core.Log.Msg($"[Reflash/dev] tmp font '{f.name}'");
            foreach (var f in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Font>())
                if (f != null) Core.Log.Msg($"[Reflash/dev] legacy font '{f.name}'");
        }

        private static void TakeOver(string arg)
        {
            bool on = arg == "on" || arg == "1" || arg == "true";
            Hijack.AppHijack.SetTakeOver(on);
            Core.Log.Msg($"[Reflash/dev] takeover {(on ? "on" : "off")}, ready={Hijack.AppHijack.Ready}");
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

            string relay = Core.CompanionRelayUrl();
            Core.Log.Msg(relay.Length == 0 ? "[Reflash/dev] the relay is off." : "[Reflash/dev] relay pair at " + relay);
        }

        private static void ClosePhone()
        {
            // The overlay too, mirroring OpenPhone. Closing only the phone left the handset raised under an open
            // overlay with its app canvas switched off a quarter second later - a blank phone.
            if (Singleton<Il2CppScheduleOne.UI.GameplayMenu>.InstanceExists && Singleton<Il2CppScheduleOne.UI.GameplayMenu>.Instance.IsOpen)
                Singleton<Il2CppScheduleOne.UI.GameplayMenu>.Instance.Close();

            var phone = PlayerSingleton<Phone>.Instance;
            if (phone != null && phone.IsOpen) phone.SetIsOpen(false);
            Core.Log.Msg($"[Reflash/dev] phone open={phone?.IsOpen}");
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

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new[] { typeof(string) })]
    internal static class Poke_SubmitCommand_String
    {
        private static bool Prefix(string args)
        {
            try { return !Poke.TryHandle(args); }
            catch (Exception e) { Core.Log.Error($"[Reflash/dev] console hook (string) threw: {e.Message}"); return true; }
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand),
                  new[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Poke_SubmitCommand_List
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
        {
            try { return !Poke.TryHandle(args); }
            catch (Exception e) { Core.Log.Error($"[Reflash/dev] console hook (list) threw: {e.Message}"); return true; }
        }
    }
}
#endif
