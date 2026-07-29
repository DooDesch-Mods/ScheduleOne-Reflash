using UnityEngine;

namespace Reflash.Game
{
    /// <summary>
    /// The game's colours, written the way a stylesheet writes them.
    ///
    /// Every colour these apps show is the game's own - a shop's card colour, an effect's label colour, the tint on a
    /// quality star. Picking them by eye out of a screenshot would look right until the day a patch retunes one, so
    /// they are read from the same fields the vanilla screens read and converted here.
    /// </summary>
    internal static class Colours
    {
        /// <summary>An opaque colour as <c>#rrggbb</c>. Alpha is dropped: nothing in these pages varies it, and a
        /// four-byte form would only invite a stylesheet to depend on one.</summary>
        internal static string Hex(Color c) =>
            "#" + Byte(c.r) + Byte(c.g) + Byte(c.b);

        internal static string Hex(Color32 c) =>
            "#" + c.r.ToString("x2") + c.g.ToString("x2") + c.b.ToString("x2");

        private static string Byte(float channel) =>
            Mathf.Clamp(Mathf.RoundToInt(channel * 255f), 0, 255).ToString("x2");

        /// <summary>
        /// The colour a customer's standards are written in, and the tint on the star beside them.
        ///
        /// Vanilla does not colour the standard itself - it maps the standard to the item quality it corresponds to
        /// (<c>StandardsMethod.GetCorrespondingQuality</c>) and takes that quality's colour
        /// (<c>ItemQuality.GetColor</c>). Two screens show this - the messages header and the contacts panel - and
        /// they used to answer differently, which is why the mapping lives here rather than in either of them.
        /// </summary>
        internal static string OfStandards(string standards) => standards switch
        {
            "VeryHigh" => "#ffc832",   // Heavenly
            "High" => "#e14bff",       // Premium
            "Moderate" => "#64beff",   // Standard
            "Low" => "#509132",        // Poor
            _ => "#7d3232",            // Trash
        };

        /// <summary>How vanilla spells a standard where a player reads it - "Very Low", not "VeryLow".</summary>
        internal static string StandardsName(string standards) => standards switch
        {
            "VeryLow" => "Very Low",
            "Low" => "Low",
            "Moderate" => "Moderate",
            "High" => "High",
            "VeryHigh" => "Very High",
            _ => "Standard",
        };

        /// <summary>
        /// The five bands of the relationship scale, darkest to friendliest, as
        /// <c>RelationshipCategory</c> declares them. The bar is not a ramp between two colours - it is these five
        /// blocks with a marker sliding over them.
        /// </summary>
        internal static readonly string[] Relationship =
        {
            "#ad3f3f",   // Hostile
            "#e38837",   // Unfriendly
            "#d0d0d0",   // Neutral
            "#3db5f3",   // Friendly
            "#3fd33f",   // Loyal
        };
    }
}
