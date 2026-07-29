using Reflash.Game;
using Reflash.Wire;

namespace Reflash.Screens
{
    /// <summary>
    /// The seven replacements, and the two facts about each that only matter at registration time.
    ///
    /// Separate from Core so that adding an app is one line here rather than an edit to the mod entry point, and so
    /// the roster can be enumerated by the test suite without a game.
    /// </summary>
    internal static class AppRoster
    {
        internal static IEnumerable<IAppPort> All()
        {
            yield return new JournalApp(new JournalGame());
            yield return new MessagesApp(new MessagesGame());
            yield return new DeliveryApp(new DeliveryGame());
            yield return new ProductsApp(new ProductsGame());
            yield return new DealersApp(new DealersGame());
            yield return new ContactsApp(new ContactsGame());
            yield return new MapApp(new MapGame());
        }

        /// <summary>The title Sideload shows. Matches the vanilla app it replaces, because the player already knows
        /// that name from the icon they pressed.</summary>
        internal static string TitleOf(VanillaApp app) => app switch
        {
            VanillaApp.Messages => "Messages",
            VanillaApp.Map => "Map",
            VanillaApp.Delivery => "Deliveries",
            VanillaApp.ProductManager => "Products",
            VanillaApp.Contacts => "Contacts",
            VanillaApp.DealerManagement => "Dealers",
            VanillaApp.Journal => "Journal",
            _ => "App",
        };

        /// <summary>
        /// Which way round the phone holds each app, first one wins on open.
        ///
        /// Every app declares both, which the vanilla ones cannot do - one stylesheet with
        /// <c>@media (orientation: ...)</c> covers it and the player gets a rotate key for free. The first entry
        /// matches how vanilla holds that app, so nothing changes for someone who never rotates.
        /// </summary>
        internal static string[] OrientationsOf(VanillaApp app) => app switch
        {
            // Upright, as their vanilla panels are: the dumps have both of these at 655x1201 canvas units, where
            // everything else is 1201x655.
            VanillaApp.Messages => new[] { "portrait", "landscape" },
            VanillaApp.DealerManagement => new[] { "portrait", "landscape" },

            // And these wide - the map because it is a map, the rest because they are tables.
            _ => new[] { "landscape", "portrait" },
        };
    }
}
