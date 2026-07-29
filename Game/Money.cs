using System.Globalization;

namespace Reflash.Game
{
    /// <summary>
    /// Money, spelled the way the game spells it.
    ///
    /// Invariant culture throughout, and not by accident: the mod runtime has invariant globalization, so
    /// constructing a specific CultureInfo throws at runtime. It is also the right answer here - the game's own
    /// interface writes dollars with a comma thousands separator regardless of the machine's locale, and a price
    /// that reads differently in the phone than in the shop is worse than one that reads American everywhere.
    /// </summary>
    internal static class Money
    {
        internal static string Format(float amount) => Format((int)Math.Round(amount));

        internal static string Format(int amount) =>
            (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture);
    }
}
