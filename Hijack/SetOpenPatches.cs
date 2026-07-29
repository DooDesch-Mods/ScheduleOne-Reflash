using System.Reflection;
using HarmonyLib;
using Reflash.Wire;

namespace Reflash.Hijack
{
    /// <summary>
    /// Takes over the seven vanilla phone apps at the one point every way of opening them passes through:
    /// <c>App.SetOpen(bool)</c>.
    ///
    /// Why there and nowhere else. The icon click reaches it through <c>App.ShortcutClicked</c>; the number keys 1-9
    /// reach it through <c>HomeScreen.Update</c> invoking the same button; the hardware shortcuts in
    /// <c>GameplayMenu</c> call <c>MapApp/JournalApp/MessagesApp.SetOpen(true)</c> DIRECTLY, bypassing icons
    /// entirely; and so do the two cross-opens - <c>Quest.cs</c>'s "show on map" and the contacts detail panel's.
    /// Hiding icons would have missed the last five.
    ///
    /// The generic base <c>App&lt;T&gt;.SetOpen</c> is an awkward Harmony target under IL2CPP, but every one of the
    /// seven carries its own concrete override, so there are seven ordinary, non-generic targets.
    ///
    /// Verified in-game before this was built (Workspace/docs/Reflash/PHASE0-GATE-RESULTS.md): with the open
    /// swallowed, an incoming NPC message still runs MSGConversation.CreateUI - which dereferences
    /// PlayerSingleton&lt;MessagesApp&gt;.Instance unguarded - without throwing, the conversation is created, the
    /// history grows and the unread flag is set. Vanilla stays healthy because it is already used to this state:
    /// App.Start calls SetOpen(false) itself, so the container is inactive most of the time anyway.
    /// </summary>
    internal static class SetOpenPatches
    {
        /// <summary>
        /// Apply all seven, each on its own. A failed patch means THAT vanilla app keeps working normally while the
        /// other six are replaced - which is the right failure for a game update that renames one method, and the
        /// reason this does not use PatchAll.
        /// </summary>
        internal static void ApplyAll(HarmonyLib.Harmony harmony)
        {
            int ok = 0;

            ok += Apply(harmony, typeof(Il2CppScheduleOne.UI.Phone.Messages.MessagesApp), nameof(Prefix_Messages)) ? 1 : 0;
            ok += Apply(harmony, typeof(Il2CppScheduleOne.UI.Phone.Map.MapApp), nameof(Prefix_Map)) ? 1 : 0;
            ok += Apply(harmony, typeof(Il2CppScheduleOne.UI.Phone.Delivery.DeliveryApp), nameof(Prefix_Delivery)) ? 1 : 0;
            ok += Apply(harmony, typeof(Il2CppScheduleOne.UI.Phone.ProductManagerApp.ProductManagerApp), nameof(Prefix_Products)) ? 1 : 0;
            ok += Apply(harmony, typeof(Il2CppScheduleOne.UI.Phone.ContactsApp.ContactsApp), nameof(Prefix_Contacts)) ? 1 : 0;
            ok += Apply(harmony, typeof(Il2CppScheduleOne.UI.Phone.Messages.DealerManagementApp), nameof(Prefix_Dealers)) ? 1 : 0;
            ok += Apply(harmony, typeof(Il2CppScheduleOne.UI.Phone.JournalApp), nameof(Prefix_Journal)) ? 1 : 0;

            // "hooked", not "took over": with the takeover off these prefixes fall straight through, and a startup
            // line claiming seven apps were replaced when none were is the kind of thing a bug report is built on.
            Core.Log.Msg($"[Reflash] hooked {ok} of 7 phone apps.");
            if (ok < 7) Core.Log.Warning("[Reflash] the rest keep their vanilla screens whatever the setting says - " +
                                         "see the errors above.");
        }

        private static bool Apply(HarmonyLib.Harmony harmony, Type appType, string prefixName)
        {
            try
            {
                MethodInfo target = AccessTools.Method(appType, "SetOpen", new[] { typeof(bool) });
                if (target == null)
                {
                    Core.Log.Error($"[Reflash] {appType.Name}.SetOpen(bool) not found - that app keeps its vanilla screen.");
                    return false;
                }

                var prefix = new HarmonyMethod(AccessTools.Method(typeof(SetOpenPatches), prefixName));
                harmony.Patch(target, prefix);
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Error($"[Reflash] patching {appType.Name}.SetOpen failed ({e.Message}) - that app keeps its vanilla screen.");
                return false;
            }
        }

        /// <summary>
        /// The decision, shared by all seven.
        ///
        /// <c>open == false</c> MUST reach vanilla. App.Start calls SetOpen(false) to initialise the panel, the
        /// phone's closeApps event calls it, and Quest closes the journal through it before opening the map -
        /// swallowing those would leave the vanilla side in a state it never expects.
        /// </summary>
        private static bool Intercept(bool open, VanillaApp which)
        {
            if (!open) return true;

            return !AppHijack.Open(which);
        }

        // Seven one-line prefixes rather than one shared generic: a Harmony prefix is matched by signature against
        // the method it patches, and each of these is attached to a different type.
        private static bool Prefix_Messages(bool open) => Intercept(open, VanillaApp.Messages);
        private static bool Prefix_Map(bool open) => Intercept(open, VanillaApp.Map);
        private static bool Prefix_Delivery(bool open) => Intercept(open, VanillaApp.Delivery);
        private static bool Prefix_Products(bool open) => Intercept(open, VanillaApp.ProductManager);
        private static bool Prefix_Contacts(bool open) => Intercept(open, VanillaApp.Contacts);
        private static bool Prefix_Dealers(bool open) => Intercept(open, VanillaApp.DealerManagement);
        private static bool Prefix_Journal(bool open) => Intercept(open, VanillaApp.Journal);
    }
}
