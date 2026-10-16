using Reflash.Wire;
using Il2CppScheduleOne.Economy;
using UnityEngine;

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
    internal sealed class DealersGame : IDealersSource, INeedsAppHandle
    {
        private readonly SpriteFeed _faces = new SpriteFeed("face-");
        private readonly SpriteFeed _icons = new SpriteFeed("item-");

        public void UseHandle(Sideload.Api.AppHandle handle)
        {
            _faces.UseHandle(handle);
            _icons.UseHandle(handle);
        }

        /// <summary>
        /// The dealers' faces, their customers' faces, everyone a dealer could be given, and the icons of what the
        /// dealers carry - the pictures the vanilla screen shows, a few per tick.
        /// </summary>
        public void WarmIcons()
        {
            _faces.Warm(Faces());
            _icons.Warm(ItemIcons());
        }

        private static IEnumerable<KeyValuePair<string, Sprite>> Faces()
        {
            foreach (GameDealer d in Recruited())
            {
                yield return new KeyValuePair<string, Sprite>(d.ID, d.MugshotSprite);

                Il2CppSystem.Collections.Generic.List<Customer> customers = d.AssignedCustomers;
                if (customers == null) continue;

                for (int i = 0; i < customers.Count; i++)
                {
                    Customer c = customers[i];
                    if (c == null || c.NPC == null) continue;

                    yield return new KeyValuePair<string, Sprite>(c.NPC.ID, c.NPC.MugshotSprite);
                }
            }

            Il2CppSystem.Collections.Generic.List<Customer> unlocked = Customer.UnlockedCustomers;
            if (unlocked == null) yield break;

            for (int i = 0; i < unlocked.Count; i++)
            {
                Customer c = unlocked[i];
                if (c == null || c.NPC == null || c.AssignedDealer != null) continue;

                yield return new KeyValuePair<string, Sprite>(c.NPC.ID, c.NPC.MugshotSprite);
            }
        }

        private static IEnumerable<KeyValuePair<string, Sprite>> ItemIcons()
        {
            foreach (GameDealer d in Recruited())
            {
                var slots = d.GetAllSlots();
                if (slots == null) continue;

                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.ItemInstance == null) continue;

                    var item = Il2CppScheduleOne.Registry.GetItem(slot.ItemInstance.ID);
                    if (item == null) continue;

                    yield return new KeyValuePair<string, Sprite>(item.ID, item.Icon);
                }
            }
        }

        /// <summary>
        /// The recruited dealers in vanilla's order: sorted by region, which is the order of its dropdown.
        /// </summary>
        private static List<GameDealer> Recruited()
        {
            var list = new List<GameDealer>();

            Il2CppSystem.Collections.Generic.List<GameDealer> all = GameDealer.AllPlayerDealers;
            if (all == null) return list;

            for (int i = 0; i < all.Count; i++)
            {
                GameDealer d = all[i];
                if (d == null || !d.IsRecruited) continue;

                list.Add(d);
            }

            // Stable, so dealers of one region keep the order the game recruited them in, as OrderBy does there.
            return list.Select((d, i) => (d, i)).OrderBy(p => (int)p.d.Region).ThenBy(p => p.i).Select(p => p.d).ToList();
        }

        public List<DealerView> Dealers()
        {
            var views = new List<DealerView>();
            foreach (GameDealer d in Recruited()) views.Add(ViewOf(d));
            return views;
        }

        private DealerView ViewOf(GameDealer d) => new DealerView
        {
            Id = d.ID,
            Name = Text.Clean(d.FullName),
            Region = Text.Clean(d.Region.ToString()),
            Home = Text.Clean(d.DealerData.HomeName),
            Cash = (int)Math.Round(d.Cash),
            CutPercent = (int)Math.Round(d.DealerData.SalesCutPercentage * 100f),
            CustomerCount = d.AssignedCustomers?.Count ?? 0,
            CustomerLimit = GameDealer.MAX_CUSTOMERS,
            HasFace = _faces.Has(d.ID),
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

                    ContactView contact = ContactOf(c.NPC);
                    contact.HasFace = _faces.Has(c.NPC.ID);
                    contact.StandardsColour = Colours.OfStandards(c.CustomerData.Standards.ToString());
                    view.Customers.Add(contact);
                }
            }

            FillInventory(d, view);
            return view;
        }

        /// <summary>
        /// What the dealer is carrying, summed the way vanilla sums it: one line per item and quality, a product
        /// counted in units (a jar of twenty is twenty), empty slots left out.
        /// </summary>
        private void FillInventory(GameDealer d, DealerDetailView view)
        {
            try
            {
                var slots = d.GetAllSlots();
                if (slots == null) return;

                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.ItemInstance == null || slot.Quantity == 0) continue;

                    int quantity = slot.Quantity;
                    int quality = -1;

                    var product = slot.ItemInstance.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>();
                    if (product != null)
                    {
                        quantity *= product.Amount;
                        quality = (int)product.Quality;
                    }

                    string id = slot.ItemInstance.ID;
                    SlotView same = view.Inventory.Find(s => s.Id == id && s.Quality == quality);
                    if (same != null) { same.Quantity += quantity; continue; }

                    var item = Il2CppScheduleOne.Registry.GetItem(id);
                    view.Inventory.Add(new SlotView
                    {
                        Id = id,
                        Name = Text.Clean(item != null ? item.Name : slot.ItemInstance.Name),
                        Quantity = quantity,
                        Quality = quality,
                        HasIcon = _icons.Has(id),
                    });
                }
            }
            catch (Exception e)
            {
                Core.Log.Warning($"reading dealer inventory failed: {e.Message}");
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

                ContactView contact = ContactOf(c.NPC);
                contact.HasFace = _faces.Has(c.NPC.ID);
                contact.StandardsColour = Colours.OfStandards(c.CustomerData.Standards.ToString());
                candidates.Add(contact);
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

            d.DealerData.SalesCutPercentage = percent / 100f;
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
                        hash = hash * 31 + (int)(d.DealerData.SalesCutPercentage * 100f);
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
                Name = Text.Clean(npc.FullName),
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
