using Reflash.Wire;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;

// IMapSource.Player() hides the type of the same name.
using GamePlayer = Il2CppScheduleOne.PlayerScripts.Player;
using UnityEngine;
using Sideload.Api;

namespace Reflash.Game
{
    /// <summary>
    /// Pins and the map picture.
    ///
    /// Positions come from <c>MapPositionUtility.GetMapPosition</c> - the game's own world-to-map arithmetic - and
    /// are normalised here rather than in the page. The utility measures from the map's centre and scales so the
    /// edge lands at half of <c>MapDimensions</c>, so the range is roughly -1024..+1024 and 0..1 is
    /// <c>0.5 + pos / MapDimensions</c>. Doing that conversion in the page would be the page guessing at the
    /// game's coordinate space.
    /// </summary>
    internal sealed class MapGame : IMapSource, INeedsAppHandle
    {
        private bool _imageTried;
        private bool _imageOk;
        private AppHandle _handle;

        /// <summary>The mugshots the customer markers show, published a few per tick like every other picture.</summary>
        private readonly SpriteFeed _faces = new SpriteFeed("face-");

        /// <summary>
        /// The app's own handle, so the picture is published through the existing registration.
        ///
        /// Registering the app again to reach Image() looked equivalent and was not: Sideload treats a repeat
        /// registration as a replacement, so the second one silently dropped the iconless flag and the declared
        /// orientations - and the picture never reached the page that was already mounted.
        /// </summary>
        public void UseHandle(AppHandle handle)
        {
            _handle = handle;
            _faces.UseHandle(handle);
        }

        /// <summary>
        /// How many pins the page is ever given.
        ///
        /// A real save carries over five hundred points of interest. Handing all of them over meant the page built
        /// five hundred elements inside one script turn, and Jint stops a handler after 250 ms - so the map's script
        /// did not merely run slowly, it was killed mid-render and the app came up dead.
        ///
        /// In practice the filter below leaves far fewer than this; the cap is the backstop, not the plan.
        /// </summary>
        private const int MaxPins = 120;

        /// <summary>
        /// How big the disc around a potential customer or dealer is, as a fraction of the map's width.
        ///
        /// The vanilla prefab draws it as a fixed 140x140 child of a 2048-wide map, so the radius is 70/2048. It is
        /// a constant in MAP units, not in screen ones - it scales with the map, because it marks an area of the
        /// world. Nothing in the game computes it: the marker itself is placed up to ten world units away from the
        /// NPC on a hash of their name, and the disc is the "somewhere around here" that covers that jitter.
        /// </summary>
        private const double AreaRadius = 70.0 / 2048.0;

        public List<PoiView> Pois()
        {
            var views = new List<PoiView>();

            var all = UnityEngine.Object.FindObjectsOfType<POI>();
            if (all == null) return views;

            // Nearest first, so a cut list is the useful half rather than whatever the scene happened to enumerate.
            GamePlayer local = GamePlayer.Local;
            Vector3 from = local != null ? local.transform.position : Vector3.zero;

            var candidates = new List<(float Distance, POI Poi)>();
            for (int i = 0; i < all.Length; i++)
            {
                POI poi = all[i];

                // isActiveAndEnabled, not activeInHierarchy. The COMPONENT being disabled is how the game hides a
                // marker - a potential customer the player has already met, an unowned property - and POI.OnDisable
                // destroys the marker's UI to prove it. Testing only the GameObject showed a hundred and twenty
                // markers where vanilla shows four.
                if (poi == null || !poi.isActiveAndEnabled) continue;

                // The player carries a POI of their own, and Player() already returns it as the marker with the halo
                // and the heading. Left in, it was drawn a second time as an anonymous badge under the player's feet.
                if (local != null && local.PoI != null && local.PoI.GetInstanceID() == poi.GetInstanceID()) continue;

                candidates.Add(((poi.transform.position - from).sqrMagnitude, poi));
            }

            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            for (int i = 0; i < candidates.Count && views.Count < MaxPins; i++)
            {
                POI poi = candidates[i].Poi;
                if (!MapSpace.TryNormalise(poi.transform.position, out double x, out double y)) continue;

                NPC npc = NpcBehind(poi);

                views.Add(new PoiView
                {
                    Id = npc != null ? npc.ID : PoiIds.Of(poi),
                    Label = Text.Ellipsis(Text.Clean(poi.MainText), 40),
                    Kind = KindOf(poi, npc),
                    Region = RegionAt(poi.transform.position),
                    X = x,
                    Y = y,
                    Radius = IsArea(poi, npc) ? AreaRadius : 0,
                    HasFace = npc != null && _faces.Has(npc.ID),
                });
            }

            return views;
        }

        /// <summary>
        /// Publish the mugshots the customer markers need. Called on a tick rather than from a read, because a
        /// texture readback inside the page's first script call is a quarter of its whole budget.
        /// </summary>
        public void WarmFaces()
        {
            var all = UnityEngine.Object.FindObjectsOfType<POI>();
            if (all == null) return;

            var wanted = new List<KeyValuePair<string, Sprite>>();

            for (int i = 0; i < all.Length; i++)
            {
                POI poi = all[i];
                if (poi == null || !poi.isActiveAndEnabled) continue;

                NPC npc = NpcBehind(poi);
                if (npc == null || string.IsNullOrEmpty(npc.ID)) continue;

                wanted.Add(new KeyValuePair<string, Sprite>(npc.ID, npc.MugshotSprite));
            }

            _faces.Warm(wanted);
        }

        /// <summary>The NPC a marker stands for, or null for a place.</summary>
        private static NPC NpcBehind(POI poi)
        {
            var npcPoi = poi.TryCast<NPCPoI>();
            return npcPoi == null ? null : npcPoi.NPC;
        }

        /// <summary>
        /// What kind of thing a marker is, which decides how the page draws it.
        ///
        /// Asked of the OWNER rather than of the marker: a POI carries no type of its own beyond NPCPoI, and which
        /// icon it shows is authored into its prefab where no code can read it. What is readable is who put it there
        /// - a Property, a Business, or an NPC - and that is the same distinction the icons make.
        /// </summary>
        private static string KindOf(POI poi, NPC npc)
        {
            if (npc != null)
            {
                if (npc.GetComponent<Dealer>() != null) return "dealer";
                if (npc.GetComponent<Supplier>() != null) return "supplier";
                return "customer";
            }

            // A supplier's stash is the small red briefcase, and it is the one marker that is neither a person nor a
            // building.
            if (poi.GetComponentInParent<SupplierStash>() != null) return "stash";

            // Business derives from Property, so it is caught by the same test - and vanilla gives both the same
            // blue badge, so there is nothing to tell apart here.
            if (poi.GetComponentInParent<Property>() != null) return "property";

            return "poi";
        }

        /// <summary>
        /// Whether this marker stands for an area rather than a point.
        ///
        /// Only two markers do: the one for a customer the player could recruit and the one for a dealer they could.
        /// A dealer already working for the player gets the same mugshot with no disc around it - they are somewhere
        /// exact. Told apart by which of the NPC's own two marker fields this is, because both are NPCPoI and there
        /// is nothing else to go on.
        /// </summary>
        private static bool IsArea(POI poi, NPC npc)
        {
            if (npc == null) return false;

            var customer = npc.GetComponent<Customer>();
            if (customer != null && Same(customer.potentialCustomerPoI, poi)) return true;

            var dealer = npc.GetComponent<Dealer>();
            return dealer != null && Same(dealer.PotentialDealerPoI, poi);
        }

        /// <summary>
        /// Whether two references are the same object.
        ///
        /// Compared by instance id rather than with ==: under Il2CppInterop every lookup hands back a FRESH managed
        /// wrapper around the same native object, so reference equality is false for things that are plainly the
        /// same - the trap that made a list of contacts fail to deduplicate.
        /// </summary>
        private static bool Same(POI a, POI b) =>
            a != null && b != null && a.GetInstanceID() == b.GetInstanceID();

        public PoiView Player()
        {
            GamePlayer local = GamePlayer.Local;
            if (local == null) return null;

            if (!MapSpace.TryNormalise(local.transform.position, out double x, out double y)) return null;

            return new PoiView { Id = "player", Label = "You", Kind = "player", X = x, Y = y };
        }

        public List<RegionView> Regions()
        {
            var views = new List<RegionView>();
            if (!Singleton<Il2CppScheduleOne.Map.Map>.InstanceExists) return views;

            Il2CppScheduleOne.Map.Map map = Singleton<Il2CppScheduleOne.Map.Map>.Instance;

            foreach (EMapRegion region in Enum.GetValues(typeof(EMapRegion)))
            {
                MapRegionData data = null;
                try { data = map.GetRegionData(region); }
                catch { /* a region the build does not carry */ }

                views.Add(new RegionView
                {
                    Id = region.ToString(),
                    Name = Text.Clean(region.ToString()),
                    Unlocked = data != null && data.IsUnlocked,
                });
            }

            return views;
        }

        /// <summary>
        /// Hand the vanilla map picture to the page as <c>s1://map</c>, once per session.
        ///
        /// The sprite's texture is usually not CPU-readable, so it is blitted through a RenderTexture first rather
        /// than read directly - GetPixels on a non-readable texture throws. Returns false if any of that fails, and
        /// the page then draws regions as plain boxes instead of showing an empty screen.
        /// </summary>
        public bool ImageReady => _imageOk;

        public void EnsureImage()
        {
            if (_imageTried) return;
            _imageTried = true;

            try
            {
                Sprite sprite = FindMapSprite();
                if (sprite == null || sprite.texture == null)
                {
                    // Before the phone has built itself there is no MapApp to read the sprite off, so this is a
                    // "not yet" rather than a "never" - try again on the next tick.
                    _imageTried = false;
                    return;
                }

                byte[] png = TextureIO.EncodePng(sprite.texture);
                if (png == null || png.Length == 0)
                {
                    Core.Log.Warning("the map picture could not be read - falling back to region boxes.");
                    return;
                }

                if (_handle == null)
                {
                    Core.Log.Warning("the map has no app handle - the picture cannot be published.");
                    return;
                }

                _handle.Image("map", png);
                _imageOk = true;
                Core.Log.Msg($"map image ready ({png.Length / 1024} KB).");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"extracting the map image failed ({e.Message}) - falling back to region boxes.");
            }
        }

        /// <summary>
        /// Which region a world position falls in, asked of the game rather than derived from coordinates - the
        /// region boundaries are the game's to know, and a map update would move them.
        /// </summary>
        private static string RegionAt(UnityEngine.Vector3 world)
        {
            try
            {
                return Singleton<Il2CppScheduleOne.Map.Map>.InstanceExists
                    ? Singleton<Il2CppScheduleOne.Map.Map>.Instance.GetRegionFromPosition(world).ToString()
                    : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// The picture the vanilla map app draws. Read off the live app rather than loaded by name, because which
        /// sprite is current depends on whether the tutorial is running.
        /// </summary>
        private static Sprite FindMapSprite()
        {
            var app = PlayerSingleton<Il2CppScheduleOne.UI.Phone.Map.MapApp>.Instance;
            if (app == null) return null;

            return app.MainMapSprite;
        }

        public int Revision
        {
            get
            {
                unchecked
                {
                    // The player moving is the thing that changes most often, quantised so a step does not count as
                    // a change - the pulse runs four times a second and a pin that jitters is worse than one that
                    // updates a moment late.
                    int hash = 17;

                    GamePlayer local = GamePlayer.Local;
                    if (local != null)
                    {
                        Vector3 p = local.transform.position;
                        hash = hash * 31 + (int)(p.x / 2f);
                        hash = hash * 31 + (int)(p.z / 2f);
                    }

                    if (Singleton<Il2CppScheduleOne.Map.Map>.InstanceExists)
                    {
                        var unlocked = Singleton<Il2CppScheduleOne.Map.Map>.Instance.GetUnlockedRegions();
                        hash = hash * 31 + (unlocked?.Count ?? 0);
                    }

                    // So a marker whose face has just arrived gets drawn with it.
                    hash = hash * 31 + _faces.Settled;

                    return hash;
                }
            }
        }
    }
}
