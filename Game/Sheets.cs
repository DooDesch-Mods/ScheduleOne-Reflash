using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.Messages;

namespace Reflash.Game
{
    /// <summary>
    /// The three windows the messages app opens on top of itself: the counter-offer, a supplier's order sheet, and
    /// the deal-window picker that accepting an offer brings up.
    ///
    /// Neither is opened by this mod. Choosing the "[Counter-offer]" reply, or sending "I need to order a dead drop",
    /// runs game code that opens the window and hands it a callback closed over the customer or supplier that asked -
    /// <c>Customer.SendCounteroffer</c> and <c>Supplier.DeaddropConfirmed</c>, both non-public. That callback is the
    /// whole transaction: it writes the message, changes the debt, schedules the drop. It cannot be rebuilt from
    /// outside, so the window is driven rather than replaced - opened by the game, read out and confirmed from here.
    ///
    /// The catch is that these two live OUTSIDE the app container, so switching that container off - which is how
    /// this mod keeps the vanilla apps from drawing - does not reach them. Left alone they appear on top of the HTML
    /// page, in the middle of the phone, with the real thing and the replacement both on screen. Hence
    /// <see cref="KeepHidden"/>, which runs every tick.
    /// </summary>
    internal static class Sheets
    {
        internal static CounterofferInterface Counter => App?.CounterofferInterface;

        internal static PhoneShopInterface Shop => App?.PhoneShopInterface;

        internal static DealWindowSelector Deal => App?.DealWindowSelector;

        private static MessagesApp App
        {
            get
            {
                try { return PlayerSingleton<MessagesApp>.Instance; }
                catch { return null; }
            }
        }

        /// <summary>
        /// Whether the counter-offer window is genuinely usable, rather than merely flagged open.
        ///
        /// <c>Open</c> sets IsOpen on its first line and stores the callback most of the way down, and the last thing
        /// it does is start a coroutine - which throws when the window's own object happens to be inactive. Checking
        /// the callback rather than the flag is what tells the two apart: without it there is nothing to send to.
        /// </summary>
        internal static bool CounterReady
        {
            get
            {
                CounterofferInterface c = Counter;
                return c != null && c.IsOpen && c.orderConfirmedCallback != null && c.selectedProduct != null;
            }
        }

        internal static bool ShopReady
        {
            get
            {
                PhoneShopInterface s = Shop;
                return s != null && s.IsOpen && s.orderConfirmedCallback != null;
            }
        }

        /// <summary>
        /// Whether the deal-window picker is up.
        ///
        /// Only IsOpen, unlike the other two. <c>SetIsOpen</c> stores its callback on its LAST line, after starting
        /// a coroutine - and starting one throws when the object is inactive, which is how this mod keeps the window
        /// off screen. So the callback may be missing on a window that is genuinely open, and demanding it meant the
        /// picker never appeared at all.
        /// </summary>
        internal static bool DealReady
        {
            get
            {
                DealWindowSelector d = Deal;
                return d != null && d.IsOpen;
            }
        }

        /// <summary>
        /// Switch the vanilla windows off while leaving them open.
        ///
        /// Deliberately not <c>Close()</c>: closing drops the callback and the cart, which is the state being read.
        /// Only the container goes, which is what a hidden window is - and it is what Close would have done anyway.
        /// </summary>
        internal static void KeepHidden()
        {
            Hide(Counter?.Container);
            Hide(Shop?.Container);
            Hide(Deal?.Container);
        }

        private static void Hide(UnityEngine.GameObject container)
        {
            if (container != null && container.activeSelf) container.SetActive(false);
        }
    }
}
