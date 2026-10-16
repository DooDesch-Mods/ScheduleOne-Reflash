using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Map;
using UnityEngine.SceneManagement;

namespace Reflash.Game
{
    /// <summary>
    /// Every map marker that is switched on, kept current by the markers themselves.
    ///
    /// The map asks for its markers on every pulse tick, four times a second, whether or not it is open: the faces
    /// on the customer markers are published ahead of time. A scene-wide <c>FindObjectsOfType&lt;POI&gt;</c> in a
    /// loaded save cost about 70 ms each time, which was a visible hitch four times a second in every other app.
    ///
    /// So the scene is searched once per scene, and after that <c>POI.OnEnable</c> and <c>POI.OnDisable</c> keep the
    /// list - the same two calls through which the game shows and hides a marker on its own map. Neither is
    /// overridden by <c>NPCPoI</c> or <c>SpecialCustomerGroupPOI</c>, so one pair of patches sees every marker.
    /// </summary>
    internal static class PoiRegistry
    {
        private static readonly Dictionary<int, POI> Markers = new Dictionary<int, POI>();
        private static readonly List<POI> Snapshot = new List<POI>();

        private static bool _hooked;
        private static int _scene = int.MinValue;
        private static int _snapshotVersion = -1;

        /// <summary>Bumped whenever a marker comes or goes, so a reader can keep what it built from the last list.</summary>
        internal static int Version { get; private set; }

        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                MethodInfo enable = AccessTools.Method(typeof(POI), "OnEnable");
                MethodInfo disable = AccessTools.Method(typeof(POI), "OnDisable");
                if (enable == null || disable == null)
                {
                    Core.Log.Warning("POI.OnEnable/OnDisable not found - the map searches the scene for its markers " +
                                     "on every tick instead, which costs frame time.");
                    return;
                }

                harmony.Patch(enable, postfix: new HarmonyMethod(AccessTools.Method(typeof(PoiRegistry), nameof(Enabled))));
                harmony.Patch(disable, prefix: new HarmonyMethod(AccessTools.Method(typeof(PoiRegistry), nameof(Disabled))));
                _hooked = true;
                Core.Log.Msg("map markers are tracked as they come and go.");
            }
            catch (Exception e)
            {
                Core.Log.Error($"tracking map markers failed ({e.Message}) - the map searches the scene on every tick " +
                               "instead, which costs frame time.");
            }
        }

        /// <summary>
        /// The markers that are switched on right now. The list is reused: read it, do not keep it.
        /// </summary>
        internal static List<POI> Live()
        {
            if (!_hooked) return Search();

            int scene = SceneManager.GetActiveScene().handle;
            if (scene != _scene)
            {
                // A new scene, or the first ask in this one. Markers that were enabled before the patches existed
                // are only found by looking, and the ones from a scene that has gone are dropped with it.
                _scene = scene;
                Markers.Clear();
                foreach (POI poi in Search()) Markers[poi.GetInstanceID()] = poi;
                Version++;
            }

            if (_snapshotVersion == Version) return Snapshot;

            Snapshot.Clear();
            foreach (POI poi in Markers.Values)
                if (poi != null) Snapshot.Add(poi);

            _snapshotVersion = Version;
            return Snapshot;
        }

        private static List<POI> Search()
        {
            var found = new List<POI>();
            var all = UnityEngine.Object.FindObjectsOfType<POI>();
            if (all == null) return found;

            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) found.Add(all[i]);

            return found;
        }

        private static void Enabled(POI __instance)
        {
            if (__instance == null) return;

            Markers[__instance.GetInstanceID()] = __instance;
            Version++;
        }

        private static void Disabled(POI __instance)
        {
            if (__instance == null) return;

            if (Markers.Remove(__instance.GetInstanceID())) Version++;
        }
    }
}
