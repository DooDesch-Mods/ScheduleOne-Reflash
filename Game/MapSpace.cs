using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Map;
using UnityEngine;

namespace Reflash.Game
{
    /// <summary>
    /// World position to map position, normalised to 0..1 across the map picture.
    ///
    /// Shared, because three apps need it: the map places its pins with it, and both the journal and the contacts
    /// screen use it to say WHERE the map should look when they hand off. Cross-opening by position rather than by
    /// a POI reference is deliberate - an NPC carries no POI of its own, and a position is something every caller
    /// already has.
    ///
    /// The arithmetic is the game's own: MapPositionUtility measures from the map's centre and scales so the edge
    /// lands at half of MapDimensions, giving roughly -1024..+1024. Y is flipped because the map counts upward and
    /// a page counts downward.
    /// </summary>
    internal static class MapSpace
    {
        internal static bool TryNormalise(Vector3 world, out double x, out double y)
        {
            x = 0;
            y = 0;

            if (!Singleton<MapPositionUtility>.InstanceExists) return false;

            MapPositionUtility util = Singleton<MapPositionUtility>.Instance;
            float size = util.MapDimensions;
            if (size <= 0f) return false;

            Vector2 pos = util.GetMapPosition(world);
            x = 0.5 + pos.x / size;
            y = 0.5 - pos.y / size;

            // A little slack past the edge rather than an exact 0..1 test: a point just outside the picture is
            // still worth showing at the border, and rejecting it would silently drop pins near the map edge.
            return x >= -0.05 && x <= 1.05 && y >= -0.05 && y <= 1.05;
        }

        /// <summary>
        /// The argument an app hands the map when it opens it at a place: <c>at=&lt;x&gt;,&lt;y&gt;</c> with both
        /// normalised. Empty when the position is not on the map, which the caller should treat as "cannot show".
        /// </summary>
        internal static string FocusArg(Vector3 world)
        {
            if (!TryNormalise(world, out double x, out double y)) return "";

            return "at=" + x.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                 + "," + y.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
