using Reflash.Wire;
using Il2CppScheduleOne.Economy;

// The IDealersSource method Dealer(string) hides the type of the same name, so the type gets an alias rather
// than every use getting a namespace.
using GameDealer = Il2CppScheduleOne.Economy.Dealer;

namespace Reflash.Game
{
    /// <summary>
    /// Recruited dealers and their customer assignments.
    ///
    /// Both writes are the game's own ServerRpcs - <c>AddCustomer_Server</c> and <c>SendRemoveCustomer</c> - which
    /// take an NPC id and do not require ownership. The ten-customer cap is checked here as well as there, not to
    /// be clever but so the page can say "full" instead of watching a press do nothing.
    /// </summary>
    internal sealed class DealersGame : IDealersSource
    {
        public List<DealerView> Dealers()
        {
            var views = new List<DealerView>();

            Il2CppSystem.Collections.Generic.List<GameDealer> all = GameDealer.AllPlayerDealers;
            if (all == null) return views;

            for (int i = 0; i < all.Count; i++)
            {
                GameDealer d = all[i];
                if (d == null || !d.IsRecruited) continue;

                views.Add(ViewOf(d));
            }

            return views;
        }

        private static DealerView ViewOf(GameDealer d) => new DealerView
        {
            Id = d.ID,
            Name = Text.Clean(d.fullName),
            Region = Text.Clean(d.Region.ToString()),
            Home = Text.Clean(d.HomeName),
            Cash = (int)Math.Round(d.Cash),
            CutPercent = (int)Math.Round(d.Cut * 100f),
            CustomerCount = d.AssignedCustomers?.Count ?? 0,
            CustomerLimit = GameDealer.MAX_CUSTOMERS,
        };

        public DealerDetailView Dealer(string dealerId)
        {
            GameDealer d = Find(dealerId);
            if (d == null) return null;

            var view = new DealerDetailView { Dealer = ViewOf(d) };

            Il2CppSystem.Collections.Generic.List<Customer> customers = d.AssignedCustomers;
            if (customers != null)
            {
                for (int i = 0; i < customers.Count; i++)
                {
                    Customer c = customers[i];
                    if (c == null || c.NPC == null) continue;

                    view.Customers.Add(ContactOf(c.NPC));
                }
            }

            FillInventory(d, view);
            return view;
        }

        /// <summary>
        /// What the dealer is carrying. Empty slots are skipped rather than shown as blanks - a dealer with two
        /// items and eight empty slots is eight rows of nothing on a screen with a two-hundred-box budget.
        /// </summary>
        private static void FillInventory(GameDealer d, DealerDetailView view)
        {
            try
            {
                var slots = d.GetAllSlots();
                if (slots == null) return;

                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.ItemInstance == null) continue;

                    view.Inventory.Add(new SlotView
                    {
                        Name = Text.Clean(slot.ItemInstance.Name),
                        Quantity = slot.Quantity,
                    });
                }
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[Reflash] reading dealer inventory failed: {e.Message}");
            }
        }

        public List<ContactView> AssignableTo(string dealerId)
        {
            var candidates = new List<ContactView>();

            GameDealer d = Find(dealerId);
            if (d == null) return candidates;

            // Every customer the player has that is not already on a dealer. Offering one that is taken would be a
            // choice the game refuses, and a picker whose entries can fail is worse than a shorter picker.
            Il2CppSystem.Collections.Generic.List<Customer> all = Customer.UnlockedCustomers;
            if (all == null) return candidates;

            for (int i = 0; i < all.Count; i++)
            {
                Customer c = all[i];
                if (c == null || c.NPC == null) continue;
                if (c.AssignedDealer != null) continue;

                candidates.Add(ContactOf(c.NPC));
            }

            return candidates;
        }

        public string AddCustomer(string dealerId, string npcId)
        {
            GameDealer d = Find(dealerId);
            if (d == null) return Reply.NotFound;

            // Checked here so the page can say "full" rather than watching the press do nothing.
            if ((d.AssignedCustomers?.Count ?? 0) >= GameDealer.MAX_CUSTOMERS) return Reply.Refused;

            d.AddCustomer_Server(npcId);
            return Reply.Ok;
        }

        public string RemoveCustomer(string dealerId, string npcId)
        {
            GameDealer d = Find(dealerId);
            if (d == null) return Reply.NotFound;

            d.SendRemoveCustomer(npcId);
            return Reply.Ok;
        }

        /// <summary>
        /// The dealer's share. Vanilla exposes this as a plain field on a NetworkBehaviour rather than through an
        /// RPC, so it is set directly - the same thing the vanilla screen's slider does.
        /// </summary>
        public string SetCut(string dealerId, int percent)
        {
            GameDealer d = Find(dealerId);
            if (d == null) return Reply.NotFound;
            if (percent < 0 || percent > 100) return Reply.BadArgs;

            d.Cut = percent / 100f;
            return Reply.Ok;
        }

        public int Revision
        {
            get
            {
                unchecked
                {
                    int hash = 17;

                    Il2CppSystem.Collections.Generic.List<GameDealer> all = GameDealer.AllPlayerDealers;
                    hash = hash * 31 + (all?.Count ?? 0);

                    if (all == null) return hash;

                    for (int i = 0; i < all.Count; i++)
                    {
                        GameDealer d = all[i];
                        if (d == null || !d.IsRecruited) continue;

                        hash = hash * 31 + (int)d.Cash;
                        hash = hash * 31 + (int)(d.Cut * 100f);
                        hash = hash * 31 + (d.AssignedCustomers?.Count ?? 0);
                    }

                    return hash;
                }
            }
        }

        internal static ContactView ContactOf(Il2CppScheduleOne.NPCs.NPC npc)
        {
            var view = new ContactView
            {
                Id = npc.ID,
                Name = Text.Clean(npc.fullName),
                Region = Text.Clean(npc.Region.ToString()),
            };

            if (npc.RelationData != null)
            {
                view.Relationship = npc.RelationData.NormalizedRelationDelta;
                view.Unlocked = npc.RelationData.Unlocked;
                view.RelationshipLabel = RelationLabel(npc.RelationData.NormalizedRelationDelta);
            }

            return view;
        }

        /// <summary>
        /// A word for a number - the game's own word.
        ///
        /// These are <c>RelationshipCategory</c>'s five: Hostile, Unfriendly, Neutral, Friendly, Loyal, cut at whole
        /// points of the raw 0..5 delta. An earlier version invented its own scale ("Best friend", "Close") and the
        /// contacts panel then printed a word the game has never used next to a bar that was the game's.
        /// </summary>
        private static string RelationLabel(float normalized) => ContactsGraph.CategoryOf(normalized * 5f);

        private static GameDealer Find(string dealerId)
        {
            if (string.IsNullOrEmpty(dealerId)) return null;

            Il2CppSystem.Collections.Generic.List<GameDealer> all = GameDealer.AllPlayerDealers;
            if (all == null) return null;

            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ID == dealerId) return all[i];

            return null;
        }
    }
}
