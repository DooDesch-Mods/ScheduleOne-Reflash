using Reflash.Wire;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI.Phone.Delivery;

namespace Reflash.Game
{
    /// <summary>
    /// Deliveries, driven through the vanilla screen rather than around it.
    ///
    /// This is the one adapter that talks to the hidden vanilla UI on purpose. <c>DeliveryShop.SubmitOrder</c> is a
    /// single transaction over five subsystems - the fee from DeliveryConfiguration, a VariableDatabase
    /// notification per line, the DeliveryInstance and its id, the server call, the receipt, and the money - and
    /// every one of those is a rule that can change in a balance patch. Reimplementing it would work today and
    /// diverge silently later, so instead the quantities are filled into the game's own listing entries and its own
    /// submit is pressed.
    ///
    /// The vanilla panel stays inactive throughout; nothing here activates it. Its objects exist because the mod
    /// suppresses SetOpen rather than destroying anything, which is exactly what makes this possible.
    /// </summary>
    internal sealed class DeliveryGame : IDeliverySource, INeedsAppHandle
    {
        /// <summary>The shopkeepers' pictures, published as s1://shop-&lt;id&gt; a few per tick.</summary>
        private readonly SpriteFeed _icons = new SpriteFeed("shop-");

        public void UseHandle(Sideload.Api.AppHandle handle) => _icons.UseHandle(handle);

        /// <summary>Runs every tick from the pulse - the pictures are one readback each and go out gradually.</summary>
        public void WarmIcons() => _icons.Warm(Faces());

        private static IEnumerable<KeyValuePair<string, UnityEngine.Sprite>> Faces()
        {
            foreach (DeliveryApp.DeliveryShopElement element in Cards())
            {
                UnityEngine.UI.Image picture = Portrait(element);
                if (picture != null && picture.sprite != null)
                    yield return new KeyValuePair<string, UnityEngine.Sprite>(IconKey(element.Shop), picture.sprite);

                // The goods too, under their item id. One feed for both because a shop's picture and a jar's are
                // the same kind of thing to publish, and the ids cannot collide.
                var entries = element.Shop.listingEntries;
                if (entries == null) continue;

                for (int i = 0; i < entries.Count; i++)
                {
                    ListingEntry entry = entries[i];
                    if (entry?.MatchingListing?.Item == null) continue;

                    yield return new KeyValuePair<string, UnityEngine.Sprite>(
                        entry.MatchingListing.Item.ID, entry.MatchingListing.Item.Icon);
                }
            }
        }

        /// <summary>Whether the player's rank is high enough to buy this at all - vanilla's own gate.</summary>
        private static bool Unlocked(Il2CppScheduleOne.ItemFramework.ItemDefinition item)
        {
            try
            {
                var storable = item.TryCast<Il2CppScheduleOne.ItemFramework.StorableItemDefinition>();
                return storable == null || storable.IsUnlocked;
            }
            catch
            {
                return true;
            }
        }

        public int Balance =>
            NetworkSingleton<MoneyManager>.InstanceExists
                ? (int)Math.Round(NetworkSingleton<MoneyManager>.Instance.sync___get_value_onlineBalance())
                : 0;

        public List<ShopView> Shops()
        {
            var views = new List<ShopView>();

            foreach (DeliveryApp.DeliveryShopElement element in Cards())
            {
                DeliveryShop shop = element.Shop;

                var view = new ShopView
                {
                    Id = ShopId(shop),
                    Name = Label(element, 0, shop.MatchingShopInterfaceName),
                    Description = Label(element, 1, ""),
                    Colour = Hex(shop.ShopColor),
                    IconKey = IconKey(shop),
                    HasIcon = _icons.Has(IconKey(shop)),
                };

                Summarise(shop, view);

                var entries = shop.listingEntries;
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        ListingEntry entry = entries[i];
                        if (entry?.MatchingListing?.Item == null) continue;

                        int price = (int)Math.Round(entry.MatchingListing.Price);
                        var item = entry.MatchingListing.Item;

                        view.Listings.Add(new ListingView
                        {
                            Id = item.ID,
                            Name = Text.Clean(item.Name),
                            Price = price,
                            Quantity = entry.SelectedQuantity,
                            Affordable = price <= Balance,
                            HasIcon = _icons.Has(item.ID),
                            Locked = !Unlocked(item),
                        });
                    }
                }

                views.Add(view);
            }

            return views;
        }

        public List<DeliveryView> Active()
        {
            var views = new List<DeliveryView>();
            if (!NetworkSingleton<DeliveryManager>.InstanceExists) return views;

            var deliveries = NetworkSingleton<DeliveryManager>.Instance.Deliveries;
            if (deliveries == null) return views;

            for (int i = 0; i < deliveries.Count; i++)
            {
                DeliveryInstance d = deliveries[i];
                if (d == null) continue;

                var view = new DeliveryView
                {
                    Id = d.DeliveryID,
                    Shop = Text.Clean(d.StoreName),
                    Destination = Text.Clean(d.DestinationCode),
                    Status = Text.Clean(d.Status.ToString()),
                    Eta = d.TimeUntilArrival > 0 ? d.TimeUntilArrival + " min" : "",
                };

                AddItems(view, d.Items);
                views.Add(view);
            }

            return views;
        }

        public List<DeliveryView> History()
        {
            var views = new List<DeliveryView>();
            if (!NetworkSingleton<DeliveryManager>.InstanceExists) return views;

            var receipts = NetworkSingleton<DeliveryManager>.Instance.DisplayedDeliveryHistory;
            if (receipts == null) return views;

            for (int i = 0; i < receipts.Count; i++)
            {
                DeliveryReceipt r = receipts[i];
                if (r == null) continue;

                DeliveryShop shop = ShopNamed(r.StoreName);

                var view = new DeliveryView
                {
                    Id = r.DeliveryID,
                    Shop = Text.Clean(r.StoreName),
                    Destination = PropertyNamed(r.DestinationCode),
                    Dock = "Loading Dock " + (r.LoadingDockIndex + 1),
                    Status = "Delivered",
                    ShopId = shop == null ? "" : ShopId(shop),
                    Total = TotalOf(shop, r.Items),
                };

                AddItems(view, r.Items, shop);
                views.Add(view);
            }

            return views;
        }

        /// <summary>
        /// A destination code as the player reads it - "storageunit" becomes "Storage Unit". Vanilla asks the
        /// PropertyManager for exactly this and shows PropertyName; the raw code is an internal id and looks like
        /// one on the card.
        /// </summary>
        private static string PropertyNamed(string code)
        {
            string raw = Text.Clean(code);
            if (raw.Length == 0) return "";

            try
            {
                if (Singleton<Il2CppScheduleOne.Property.PropertyManager>.InstanceExists)
                {
                    var property = Singleton<Il2CppScheduleOne.Property.PropertyManager>.Instance.GetProperty(raw);
                    if (property != null)
                    {
                        string name = Text.Clean(property.PropertyName);
                        if (name.Length > 0) return name;
                    }
                }
            }
            catch { /* an unknown code is not worth a log line per card */ }

            return raw;
        }

        /// <summary>The lines of an order: an item id and how many of it.</summary>
        private static void AddItems(DeliveryView view,
                                     Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.DevUtilities.StringIntPair> items,
                                     DeliveryShop shop = null)
        {
            if (items == null) return;

            var entries = shop?.listingEntries;

            for (int i = 0; i < items.Length; i++)
            {
                var pair = items[i];
                if (pair == null) continue;

                // A receipt keeps the item's ID, not its name. Vanilla writes "25x Green Crack Seed"; the raw
                // "greencrackseed x25" is an internal id shown to a player. The shop's own listing knows the name.
                string name = Text.Clean(pair.String);
                if (entries != null)
                {
                    ListingEntry entry = FindEntry(entries, pair.String);
                    var item = entry?.MatchingListing?.Item;
                    if (item != null) name = Text.Clean(item.Name);
                }

                view.Items.Add(pair.Int + "x " + name);
            }
        }

        public string Order(string shopId, IReadOnlyList<KeyValuePair<string, int>> quantities)
        {
            DeliveryShop shop = FindShop(shopId);
            if (shop == null) return Reply.NotFound;

            var entries = shop.listingEntries;
            if (entries == null) return Reply.NotFound;

            // Clear first. A previous order - or a quantity the vanilla screen was left holding - would otherwise
            // ride along with this one.
            for (int i = 0; i < entries.Count; i++) entries[i]?.SetQuantity(0, false);

            int filled = 0;
            for (int q = 0; q < quantities.Count; q++)
            {
                ListingEntry entry = FindEntry(entries, quantities[q].Key);
                if (entry == null) continue;

                entry.SetQuantity(quantities[q].Value, false);
                filled++;
            }

            if (filled == 0) return Reply.NotFound;

            // The game's own gate, so a refusal reads the same as it would on the vanilla screen - no money, no
            // dock, nothing selected.
            if (!shop.CanOrder(out string reason))
            {
                for (int i = 0; i < entries.Count; i++) entries[i]?.SetQuantity(0, false);
                Core.Log.Msg($"[Reflash] delivery refused by the game: {reason}");
                return Reply.Refused;
            }

            shop.SubmitOrder("");
            return Reply.Ok;
        }

        /// <summary>
        /// Put quantities into the game's order panel and leave them there.
        ///
        /// Nothing is submitted. The point is the side effect: the panel recalculates its fee, its total and its
        /// delivery time, and those are then read back in <see cref="Summarise"/>. It is the same trick the whole
        /// adapter is built on - drive the screen the game already has rather than reimplement what it knows.
        /// </summary>
        public string Fill(string shopId, IReadOnlyList<KeyValuePair<string, int>> quantities)
        {
            DeliveryShop shop = FindShop(shopId);
            var entries = shop?.listingEntries;
            if (entries == null) return Reply.NotFound;

            // notify: true, unlike the order path. The notification is the whole point here - it is what makes the
            // shop recalculate its fee, its total and its delivery time, and those labels are what gets read back.
            // With it suppressed the panel answered for the basket BEFORE this one.
            for (int i = 0; i < entries.Count; i++) entries[i]?.SetQuantity(0, true);

            if (quantities != null)
                for (int q = 0; q < quantities.Count; q++)
                {
                    ListingEntry entry = FindEntry(entries, quantities[q].Key);
                    entry?.SetQuantity(quantities[q].Value, true);
                }

            return Reply.Ok;
        }

        /// <summary>What the game's order panel says right now, in its own words and its own money format.</summary>
        private static void Summarise(DeliveryShop shop, ShopView view)
        {
            // The dropdowns are filled when the vanilla screen opens the shop, and this mod never opens it - so
            // without this they are empty and there is nowhere to send anything.
            try
            {
                if (shop.DestinationDropdown != null && shop.DestinationDropdown.options.Count == 0)
                    shop.RefreshDestinationUI();

                if (shop.LoadingDockDropdown != null && shop.LoadingDockDropdown.options.Count == 0)
                    shop.RefreshLoadingDockUI();
            }
            catch { }

            Options(shop.DestinationDropdown, view.Destinations);
            Options(shop.LoadingDockDropdown, view.Docks);
            view.DestinationIndex = shop.DestinationDropdown == null ? 0 : shop.DestinationDropdown.value;
            view.DockIndex = shop.LoadingDockDropdown == null ? 0 : shop.LoadingDockDropdown.value;

            view.ItemTotal = Read(shop.ItemTotalLabel);
            view.Fee = Read(shop.DeliveryFeeLabel);
            view.OrderTotal = Read(shop.OrderTotalLabel);
            view.Time = Read(shop.DeliveryTimeLabel);

            try
            {
                view.CanOrder = shop.CanOrder(out string reason);
                view.Note = view.CanOrder ? "" : Text.Clean(reason);
            }
            catch
            {
                view.CanOrder = false;
            }
        }

        private static void Options(UnityEngine.UI.Dropdown drop, List<string> into)
        {
            try
            {
                if (drop == null) return;

                for (int i = 0; i < drop.options.Count; i++)
                    into.Add(Text.Clean(drop.options[i].text));
            }
            catch { }
        }

        /// <summary>
        /// Choose a destination or a loading dock. Written to the game's own dropdown so its onValueChanged runs -
        /// that is what actually books the property and refills the dock list.
        /// </summary>
        public string Choose(string shopId, bool dock, int index)
        {
            DeliveryShop shop = FindShop(shopId);
            if (shop == null) return Reply.NotFound;

            UnityEngine.UI.Dropdown drop = dock ? shop.LoadingDockDropdown : shop.DestinationDropdown;
            if (drop == null) return Reply.NotFound;

            if (index < 0 || index >= drop.options.Count) return Reply.BadArgs;

            drop.value = index;
            return Reply.Ok;
        }

        private static string Read(UnityEngine.UI.Text label)
        {
            try { return label == null ? "" : Text.Clean(label.text); }
            catch { return ""; }
        }

        public int ActiveCount
        {
            get
            {
                if (!NetworkSingleton<DeliveryManager>.InstanceExists) return 0;

                var deliveries = NetworkSingleton<DeliveryManager>.Instance.Deliveries;
                return deliveries?.Count ?? 0;
            }
        }

        public int Revision
        {
            get
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + Balance;

                    if (!NetworkSingleton<DeliveryManager>.InstanceExists) return hash;

                    DeliveryManager mgr = NetworkSingleton<DeliveryManager>.Instance;
                    var deliveries = mgr.Deliveries;
                    hash = hash * 31 + (deliveries?.Count ?? 0);

                    if (deliveries != null)
                    {
                        for (int i = 0; i < deliveries.Count; i++)
                        {
                            DeliveryInstance d = deliveries[i];
                            if (d == null) continue;

                            hash = hash * 31 + (int)d.Status;
                            hash = hash * 31 + d.TimeUntilArrival;
                        }
                    }

                    var history = mgr.DisplayedDeliveryHistory;
                    hash = hash * 31 + (history?.Count ?? 0);
                    return hash;
                }
            }
        }

        /// <summary>
        /// The vanilla shop panels. Reached through the hidden DeliveryApp rather than by searching the scene, so
        /// this finds exactly the ones the game itself would order from.
        /// </summary>
        /// <summary>
        /// The cards the vanilla screen would show, and only those.
        ///
        /// Every DeliveryShop in the prefab has a card, but the game switches the button off for the ones that are
        /// not available yet - <c>SetActive(Shop.AvailableByDefault)</c> in DeliveryApp.Start, and more as they are
        /// unlocked. Listing them all instead put nine shops on a screen vanilla shows four on.
        ///
        /// activeSelf rather than activeInHierarchy: the whole app container is switched off by this mod, so
        /// nothing under it is active in the hierarchy and the question would always answer no.
        /// </summary>
        private static List<DeliveryApp.DeliveryShopElement> Cards()
        {
            var cards = new List<DeliveryApp.DeliveryShopElement>();

            var app = PlayerSingleton<DeliveryApp>.Instance;
            var elements = app == null ? null : app._shopElements;
            if (elements == null) return cards;

            for (int i = 0; i < elements.Count; i++)
            {
                DeliveryApp.DeliveryShopElement element = elements[i];
                if (element?.Shop == null || element.Button == null) continue;
                if (element.Shop.MatchingShop == null) continue;
                if (!element.Button.gameObject.activeSelf) continue;

                cards.Add(element);
            }

            return cards;
        }

        private static List<DeliveryShop> VanillaShops()
        {
            var shops = new List<DeliveryShop>();
            foreach (DeliveryApp.DeliveryShopElement element in Cards()) shops.Add(element.Shop);
            return shops;
        }

        /// <summary>
        /// A line of text off the vanilla card. The name and the description are authored into the button rather
        /// than held in a field, so this is where they actually are - and reading them beats printing the internal
        /// interface name, which is what the player saw before.
        /// </summary>
        private static string Label(DeliveryApp.DeliveryShopElement element, int index, string fallback)
        {
            // Both text components, because this screen mixes them: the game is old enough to have uGUI Text in
            // places and TextMeshPro in others, and asking for only one of them found nothing at all.
            try
            {
                var modern = element.Button.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (modern != null && index < modern.Length && modern[index] != null)
                {
                    string text = Text.Clean(modern[index].text);
                    if (text.Length > 0) return text;
                }
            }
            catch { }

            try
            {
                var legacy = element.Button.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                if (legacy != null && index < legacy.Length && legacy[index] != null)
                {
                    string text = Text.Clean(legacy[index].text);
                    if (text.Length > 0) return text;
                }
            }
            catch { }

            return Text.Clean(fallback);
        }

        /// <summary>
        /// The shopkeeper's round picture on the card, or null where the card has none.
        ///
        /// The card is <c>&lt;Shop&gt;Button &gt; Icon (a circular mask) &gt; Image</c>, and the portrait is that
        /// Image's sprite - "Dan_Mugshot", "Steve_Mugshot". Nothing assigns it at runtime; it is authored into the
        /// scene, which is why it has to be read off the live object by path rather than asked of the shop.
        ///
        /// The <c>Icon</c> node's own active flag is the whole answer to "which cards have a picture". Both
        /// Gas-Marts carry an Image holding a leftover Steve_Mugshot with the mask switched OFF, and reading the
        /// sprite without checking that flag is exactly how four shops ended up showing two copies of the same
        /// stranger. Vanilla shows a portrait on the two hardware shops and none on the Gas-Marts.
        /// </summary>
        private static UnityEngine.UI.Image Portrait(DeliveryApp.DeliveryShopElement element)
        {
            if (element == null || element.Button == null) return null;

            UnityEngine.Transform icon = element.Button.transform.Find("Icon");
            if (icon == null || !icon.gameObject.activeSelf) return null;

            UnityEngine.Transform image = icon.Find("Image");
            if (image == null) return null;

            var picture = image.GetComponent<UnityEngine.UI.Image>();
            return picture != null && picture.sprite != null ? picture : null;
        }

        private static string Hex(UnityEngine.Color c) => Colours.Hex(c);

        /// <summary>The shop's own name is its id - it is what the game keys deliveries by, and it is stable.</summary>
        private static string ShopId(DeliveryShop shop) => shop.MatchingShopInterfaceName ?? "";

        /// <summary>
        /// The same shop, as a name a URL can carry.
        ///
        /// The id is the shop's display name - "Dan's Hardware" - which is fine as an identifier and useless as the
        /// tail of a picture's address: the apostrophe and the spaces meant every card asked for a picture the
        /// server could not find, and what showed on all four was the browser's broken-image mark.
        /// </summary>
        private static string IconKey(DeliveryShop shop)
        {
            string id = ShopId(shop);
            var safe = new System.Text.StringBuilder(id.Length);

            foreach (char c in id)
                safe.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');

            return safe.ToString();
        }

        /// <summary>
        /// The shop a receipt names. A receipt keeps the store's NAME, and everything else here is keyed by the
        /// interface name, so the two are matched loosely - the pair agree on ordinary shops and a mismatch only
        /// costs the reorder button, never a wrong order.
        /// </summary>
        private static DeliveryShop ShopNamed(string storeName)
        {
            string wanted = Text.Clean(storeName);
            if (wanted.Length == 0) return null;

            foreach (DeliveryShop shop in VanillaShops())
            {
                string id = ShopId(shop);
                if (string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase)) return shop;
            }

            foreach (DeliveryShop shop in VanillaShops())
            {
                string id = ShopId(shop);
                if (id.Length > 0 && wanted.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) return shop;
            }

            return null;
        }

        /// <summary>
        /// What this order would cost TODAY. The receipt keeps quantities and item ids, not money, so the sum is
        /// worked out from the shop's current listing prices - which is also the honest number for a button that
        /// places the order again.
        /// </summary>
        private static int TotalOf(DeliveryShop shop,
                                   Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.DevUtilities.StringIntPair> items)
        {
            if (shop == null || items == null) return 0;

            var entries = shop.listingEntries;
            if (entries == null) return 0;

            int total = 0;
            for (int i = 0; i < items.Length; i++)
            {
                var pair = items[i];
                if (pair == null) continue;

                ListingEntry entry = FindEntry(entries, pair.String);
                if (entry?.MatchingListing == null) continue;

                total += (int)Math.Round(entry.MatchingListing.Price) * pair.Int;
            }

            return total;
        }

        /// <summary>
        /// Place a past order again, exactly as vanilla's Reorder button does: the same shop, the same items and
        /// quantities, through the same gate as any other order.
        /// </summary>
        public string Reorder(string deliveryId)
        {
            if (!NetworkSingleton<DeliveryManager>.InstanceExists) return Reply.NotFound;

            var receipts = NetworkSingleton<DeliveryManager>.Instance.DisplayedDeliveryHistory;
            if (receipts == null) return Reply.NotFound;

            for (int i = 0; i < receipts.Count; i++)
            {
                DeliveryReceipt r = receipts[i];
                if (r == null || r.DeliveryID != deliveryId) continue;

                DeliveryShop shop = ShopNamed(r.StoreName);
                if (shop == null || r.Items == null) return Reply.NotFound;

                var quantities = new List<KeyValuePair<string, int>>();
                for (int k = 0; k < r.Items.Length; k++)
                {
                    var pair = r.Items[k];
                    if (pair != null) quantities.Add(new KeyValuePair<string, int>(pair.String, pair.Int));
                }

                return Order(ShopId(shop), quantities);
            }

            return Reply.NotFound;
        }

        private static DeliveryShop FindShop(string shopId)
        {
            foreach (DeliveryShop shop in VanillaShops())
                if (ShopId(shop) == shopId) return shop;

            return null;
        }

        private static ListingEntry FindEntry(Il2CppSystem.Collections.Generic.List<ListingEntry> entries, string itemId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ListingEntry entry = entries[i];
                if (entry?.MatchingListing?.Item != null && entry.MatchingListing.Item.ID == itemId) return entry;
            }

            return null;
        }
    }
}
