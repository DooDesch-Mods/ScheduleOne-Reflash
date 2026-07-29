using Reflash.Hijack;
using Reflash.Wire;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;

namespace Reflash.Game
{
    /// <summary>
    /// Contacts, read from the NPC registry rather than from the relationship-graph prefabs.
    ///
    /// Vanilla's graph geometry lives only as authored anchoredPositions on RelationCircle components, which is
    /// data this mod would have to read out of a live hierarchy and could not reproduce if that hierarchy changed.
    /// The relationships themselves are in NPCRelationData, which is real data - so that is what is used, and the
    /// layout is this app's own.
    /// </summary>
    internal sealed class ContactsGame : IContactsSource, INeedsAppHandle
    {
        /// <summary>The graph half, kept separate because reading the scene is a different job from reading data.</summary>
        private readonly ContactsGraph _graph = new ContactsGraph();

        public void UseHandle(Sideload.Api.AppHandle handle) => _graph.UseHandle(handle);

        public GraphView Graph(string regionId) => _graph.Graph(regionId);

        public void WarmFaces(string regionId) => _graph.WarmFaces(regionId);

        public List<RegionView> Regions()
        {
            var views = new List<RegionView>();

            bool haveMap = Singleton<Il2CppScheduleOne.Map.Map>.InstanceExists;
            Il2CppScheduleOne.Map.Map map = haveMap ? Singleton<Il2CppScheduleOne.Map.Map>.Instance : null;

            foreach (EMapRegion region in Enum.GetValues(typeof(EMapRegion)))
            {
                MapRegionData data = null;
                if (map != null)
                {
                    try { data = map.GetRegionData(region); }
                    catch { /* a region this build does not carry */ }
                }

                views.Add(new RegionView
                {
                    Id = region.ToString(),
                    Name = Text.Clean(region.ToString()),
                    Unlocked = data != null && data.IsUnlocked,
                    ContactCount = CountIn(region),
                    CartelInfluencePercent = CartelInfluence(region),
                });
            }

            return views;
        }

        public ContactDetailView Contact(string npcId)
        {
            NPC npc = FindNpc(npcId);
            if (npc == null) return null;

            ContactView brief = DealersGame.ContactOf(npc);
            brief.Kind = KindOf(npc);

            // "Can the map show them" is answered by whether their position maps onto it, not by a POI reference -
            // an NPC carries none, and vanilla reaches its POI through the detail panel rather than the NPC.
            var view = new ContactDetailView
            {
                Contact = brief,
                HasPoi = MapSpace.FocusArg(npc.transform.position).Length > 0,
            };

            var customer = npc.GetComponent<Customer>();
            if (customer != null)
            {
                view.AddictionPercent = (int)Math.Round(customer.CurrentAddiction * 100f);

                if (customer.CustomerData != null)
                {
                    string standards = customer.CustomerData.Standards.ToString();
                    view.Standards = Colours.StandardsName(standards);
                    view.StandardsColour = Colours.OfStandards(standards);

                    var preferred = customer.CustomerData.PreferredProperties;
                    if (preferred != null)
                        for (int i = 0; i < preferred.Count; i++)
                            if (preferred[i] != null)
                                view.PreferredProperties.Add(new LabelView
                                {
                                    Text = Text.Clean(preferred[i].Name),
                                    Colour = Colours.Hex(preferred[i].LabelColor),
                                });
                }

                // Vanilla hides both of these until the customer is unlocked, and it is the same gate that hides the
                // relationship bar - so the page gets nothing rather than a zero that reads as "they spent nothing".
                if (npc.RelationData != null && npc.RelationData.Unlocked) FillPurchases(customer, view);
            }

            var supplier = npc.GetComponent<Supplier>();
            if (supplier != null) view.Debt = (int)Math.Round(supplier.Debt);

            FillConnections(npc, view);
            return view;
        }

        /// <summary>
        /// What they bought in the last week and what it came to.
        ///
        /// The window is the game's, not ours: <c>CalculateTopWeeklyPurchases</c> filters the contract receipts to
        /// seven in-game days and sums what was actually paid. Adding up receipts here would be a second definition
        /// of "recently" that drifts the first time the game changes its own.
        ///
        /// Three lines, because that is what vanilla shows.
        /// </summary>
        private static void FillPurchases(Customer customer, ContactDetailView view)
        {
            Il2CppSystem.Collections.Generic.List<StringIntPair> top;
            float spent;

            try { customer.CalculateTopWeeklyPurchases(out top, out spent); }
            catch { return; }

            view.SpentTotal = (int)Math.Round(spent);
            if (top == null) return;

            for (int i = 0; i < top.Count && view.TopPurchases.Count < 3; i++)
            {
                StringIntPair pair = top[i];
                if (pair == null) continue;

                // The receipt carries an item id; the name is the registry's to give.
                var item = Il2CppScheduleOne.Registry.GetItem(pair.String);
                string name = item != null ? Text.Clean(item.Name) : Text.Clean(pair.String);

                view.TopPurchases.Add(pair.Int + "x " + name);
            }
        }

        /// <summary>
        /// Who this contact is connected to. Capped, because the point is "who do they know", not an exhaustive
        /// edge list - and twelve names is already more than fits on a phone screen inside the render budget.
        /// </summary>
        private static void FillConnections(NPC npc, ContactDetailView view)
        {
            var connections = npc.RelationData?.Connections;
            if (connections == null) return;

            for (int i = 0; i < connections.Count && view.Connections.Count < 12; i++)
            {
                NPC other = connections[i];
                if (other == null || other.RelationData == null || !other.RelationData.Unlocked) continue;

                ContactView brief = DealersGame.ContactOf(other);
                brief.Kind = KindOf(other);
                view.Connections.Add(brief);
            }
        }

        public string ShowOnMap(string npcId)
        {
            NPC npc = FindNpc(npcId);
            if (npc == null) return Reply.NotFound;

            string focus = MapSpace.FocusArg(npc.transform.position);
            if (focus.Length == 0) return Reply.Refused;

            return AppHijack.Open(VanillaApp.Map, focus) ? Reply.Ok : Reply.Refused;
        }

        public int Revision
        {
            get
            {
                unchecked
                {
                    int hash = 17;

                    Il2CppSystem.Collections.Generic.List<NPC> all = NPCManager.NPCRegistry;
                    hash = hash * 31 + (all?.Count ?? 0);

                    if (all == null) return hash;

                    // Only the unlocked count and the coarse relationship total. Reading every relationship every
                    // tick for 285 NPCs is exactly the sort of thing the revision must not do.
                    int unlocked = 0, relations = 0;
                    for (int i = 0; i < all.Count; i++)
                    {
                        NPC npc = all[i];
                        if (npc?.RelationData == null || !npc.RelationData.Unlocked) continue;

                        unlocked++;
                        relations += (int)(npc.RelationData.RelationDelta * 2f);
                    }

                    hash = hash * 31 + unlocked;
                    hash = hash * 31 + relations;

                    // The mugshots arrive a few per tick, and each one is something the page has to be told about.
                    hash = hash * 31 + _graph.PublishedFaces;

                    return hash;
                }
            }
        }

        private static int CountIn(EMapRegion region)
        {
            Il2CppSystem.Collections.Generic.List<NPC> all = NPCManager.NPCRegistry;
            if (all == null) return 0;

            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                NPC npc = all[i];
                if (npc != null && npc.Region == region && npc.RelationData != null && npc.RelationData.Unlocked) n++;
            }

            return n;
        }

        private static int CartelInfluence(EMapRegion region)
        {
            try
            {
                if (!NetworkSingleton<Il2CppScheduleOne.Cartel.Cartel>.InstanceExists) return 0;

                var cartel = NetworkSingleton<Il2CppScheduleOne.Cartel.Cartel>.Instance;
                if (cartel.Influence == null) return 0;

                return (int)Math.Round(cartel.Influence.GetInfluence(region) * 100f);
            }
            catch
            {
                // A build without the cartel content, or a save from before it. Zero reads as "none", which is the
                // honest answer either way.
                return 0;
            }
        }

        private static string KindOf(NPC npc)
        {
            if (npc.GetComponent<Dealer>() != null) return "dealer";
            if (npc.GetComponent<Supplier>() != null) return "supplier";
            if (npc.GetComponent<Customer>() != null) return "customer";
            return "";
        }

        private static NPC FindNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;

            Il2CppSystem.Collections.Generic.List<NPC> all = NPCManager.NPCRegistry;
            if (all == null) return null;

            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ID == npcId) return all[i];

            return null;
        }
    }
}
