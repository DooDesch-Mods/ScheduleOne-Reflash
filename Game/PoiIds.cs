using Il2CppScheduleOne.Map;
using UnityEngine;

namespace Reflash.Game
{
    /// <summary>
    /// Stable-enough names for map points.
    ///
    /// A POI has no id of its own - the vanilla map never needs one, because it holds the object itself. A page
    /// holds a string, so one has to be made up, and it has to survive the round trip from "the map listed this pin"
    /// to "the player pressed it" a moment later.
    ///
    /// The GameObject's instance id does exactly that and no more: unique while the object lives, meaningless after
    /// a reload. That is the right lifetime here - a page always re-reads the pin list before it can show a pin, so
    /// an id from a previous session is never one it can press.
    /// </summary>
    internal static class PoiIds
    {
        internal static string Of(POI poi) =>
            poi == null ? "" : poi.gameObject.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The POI an id names, or null when it is gone - which is an ordinary outcome, not an error.</summary>
        internal static POI Find(string id)
        {
            if (string.IsNullOrEmpty(id) ||
                !int.TryParse(id, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out int instanceId))
                return null;

            var all = UnityEngine.Object.FindObjectsOfType<POI>();
            if (all == null) return null;

            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].gameObject.GetInstanceID() == instanceId) return all[i];

            return null;
        }
    }
}
