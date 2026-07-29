using Reflash.Wire;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.Messages;

namespace Reflash.Game
{
    /// <summary>
    /// Reads and drives conversations through the game's own objects.
    ///
    /// Two rules were learned from the vanilla source and are load-bearing:
    ///
    ///   * Replies go through <c>MSGConversation.ResponseChosen</c>, NOT straight to
    ///     <c>MessagingManager.SendResponse</c>. ResponseChosen is what honours a response's
    ///     <c>disableDefaultResponseBehaviour</c> and runs its callback - and quest responses are exactly the ones
    ///     that use both. Calling the manager directly sends the text and silently drops the quest step.
    ///   * <c>MSGConversation.SetOpen</c> is never called. It drives the vanilla dialogue page, which this mod has
    ///     deliberately left inactive. Only <c>SetRead</c> is used, and it keeps the vanilla unread bookkeeping and
    ///     badge correct for free.
    /// </summary>
    internal sealed class MessagesGame : IMessagesSource, INeedsAppHandle
    {
        private readonly SpriteFeed _faces = new SpriteFeed("face-");

        /// <summary>Product and item icons, for the counter-offer picker and the dead-drop sheet.</summary>
        private readonly SpriteFeed _icons = new SpriteFeed("item-");

        public void UseHandle(Sideload.Api.AppHandle handle)
        {
            _faces.UseHandle(handle);
            _icons.UseHandle(handle);
        }

        /// <summary>The senders of the conversations on screen, a few faces per tick.</summary>
        public void WarmFaces()
        {
            Il2CppSystem.Collections.Generic.List<MSGConversation> active = MessagesApp.ActiveConversations;
            if (active != null) _icons.Warm(Icons());

            if (active == null) return;

            _faces.Warm(Senders(active));
        }

        /// <summary>
        /// The pictures the two sheets need: every product the player has discovered, plus whatever a supplier is
        /// currently offering. Both sets are small and only grow, so warming them alongside the mugshots costs
        /// nothing once they have arrived.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, UnityEngine.Sprite>> Icons()
        {
            var discovered = ProductManager.DiscoveredProducts;
            if (discovered != null)
                for (int i = 0; i < discovered.Count; i++)
                {
                    ProductDefinition p = discovered[i];
                    if (p != null) yield return new KeyValuePair<string, UnityEngine.Sprite>(p.ID, p.Icon);
                }

            PhoneShopInterface shop = Sheets.Shop;
            if (shop == null || !shop.IsOpen || shop._items == null) yield break;

            for (int i = 0; i < shop._items.Count; i++)
            {
                PhoneShopInterface.Listing listing = shop._items[i];
                if (listing != null && listing.Item != null)
                    yield return new KeyValuePair<string, UnityEngine.Sprite>(listing.Item.ID, listing.Item.Icon);
            }
        }

        private static IEnumerable<KeyValuePair<string, UnityEngine.Sprite>> Senders(
            Il2CppSystem.Collections.Generic.List<MSGConversation> active)
        {
            for (int i = 0; i < active.Count; i++)
            {
                MSGConversation c = active[i];

                // An unknown sender has no face to show in vanilla either - the row keeps its blank circle.
                if (c == null || !c.EntryVisible || c.sender == null || !c.IsSenderKnown) continue;

                yield return new KeyValuePair<string, UnityEngine.Sprite>(c.sender.ID, c.sender.MugshotSprite);
            }
        }

        public List<ThreadView> Threads()
        {
            var views = new List<ThreadView>();

            Il2CppSystem.Collections.Generic.List<MSGConversation> active = MessagesApp.ActiveConversations;
            if (active == null) return views;

            for (int i = 0; i < active.Count; i++)
            {
                MSGConversation c = active[i];
                if (c == null || !c.EntryVisible || c.sender == null) continue;

                var view = new ThreadView
                {
                    Id = c.sender.ID,
                    Name = c.IsSenderKnown ? Text.Clean(c.contactName) : "Unknown",
                    Known = c.IsSenderKnown,
                    Unread = !c.Read,
                    Preview = LastText(c),
                    HasFace = _faces.Has(c.sender.ID),
                    OfferLeft = OfferLeft(c.sender),

                    // Vanilla offers the cross on a customer's row and nowhere else - a supplier or a quest
                    // contact is not something you get to dismiss.
                    CanHide = c.sender.GetComponent<Customer>() != null,
                };

                Il2CppSystem.Collections.Generic.List<EConversationCategory> cats = c.Categories;
                if (cats != null)
                    for (int k = 0; k < cats.Count; k++) view.Categories.Add(cats[k].ToString());

                views.Add(view);
            }

            return views;
        }

        /// <summary>
        /// How much of an open offer's time is left, 0..1, or -1 when there is no offer.
        ///
        /// Vanilla's own arithmetic: an offer lasts 600 in-game minutes from when it was made, and the bar under
        /// the row counts that down. Recomputing it here rather than reading the slider off the vanilla UI, because
        /// that UI is the thing being replaced.
        /// </summary>
        private static double OfferLeft(NPC npc)
        {
            var customer = npc.GetComponent<Customer>();
            if (customer == null || customer.OfferedContractInfo == null) return -1;

            try
            {
                if (!NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.InstanceExists) return -1;

                int made = customer.OfferedContractTime.GetMinSum();
                int now = NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance.GetTotalMinSum();

                return Math.Clamp(1.0 - (now - made) / 600.0, 0.0, 1.0);
            }
            catch
            {
                return -1;
            }
        }

        public ThreadDetailView Thread(string npcId)
        {
            MSGConversation c = Find(npcId);
            if (c == null) return null;

            var view = new ThreadDetailView
            {
                Id = npcId,
                Name = c.IsSenderKnown ? Text.Clean(c.contactName) : "Unknown",
                Known = c.IsSenderKnown,
                HasFace = c.sender != null && _faces.Has(c.sender.ID),
            };

            Il2CppSystem.Collections.Generic.List<Message> history = c.messageHistory;
            if (history != null)
            {
                for (int i = 0; i < history.Count; i++)
                {
                    Message m = history[i];
                    if (m == null) continue;

                    view.Messages.Add(new MessageView
                    {
                        // No timestamp: the game's Message carries none, and inventing one would be a prettier lie
                        // than showing nothing.
                        From = m.sender == Message.ESenderType.Player ? "me" : "them",
                        Text = Text.Clean(m.text),
                        EndOfGroup = m.endOfGroup,
                    });
                }
            }

            Il2CppSystem.Collections.Generic.List<Response> responses = c.currentResponses;
            if (responses != null)
                for (int i = 0; i < responses.Count; i++)
                    if (responses[i] != null) view.Replies.Add(Text.Clean(responses[i].text));

            // Only the lines the game would show. ShouldShow is the "does this belong here at all" gate; IsValid is
            // the "can it be used right now" one, and the vanilla UI greys out rather than hides the second kind -
            // so the reason travels with it.
            Il2CppSystem.Collections.Generic.List<SendableMessage> sendables = c.Sendables;
            if (sendables != null)
            {
                for (int i = 0; i < sendables.Count; i++)
                {
                    SendableMessage s = sendables[i];
                    if (s == null || !s.ShouldShow()) continue;

                    bool valid;
                    string reason = "";
                    try { valid = s.IsValid(out reason); }
                    catch { valid = false; }

                    view.Sendables.Add(new SendableView
                    {
                        Text = Text.Clean(s.Text),
                        Valid = valid,
                        Reason = Text.Clean(reason),
                    });
                }
            }

            view.ReplyToken = ReplyToken(c);
            view.SendToken = SendToken(c);

            // Only for the conversation the sheet was opened from. There is one of each in the whole game, and a
            // sheet belonging to someone else would otherwise appear over whichever thread happened to be on screen.
            if (Sheets.CounterReady && Sheets.Counter.conversation == c) view.Counter = Counter();
            if (Sheets.ShopReady && Sheets.Shop.conversation == c) view.Order = Order();

            // The deal picker keeps no conversation of its own, so it belongs to whichever thread is open - which is
            // the one the player just said yes in.
            if (Sheets.DealReady) view.Deal = Deal();

            FillRelationshipInfo(c, view);
            return view;
        }

        /// <summary>
        /// The line the vanilla thread header shows under the name: a customer's standards, a supplier's debt.
        /// Reproduced rather than invented, because it is the one number that decides whether an offer is worth
        /// making.
        /// </summary>
        private static void FillRelationshipInfo(MSGConversation c, ThreadDetailView view)
        {
            NPC npc = c.sender;
            if (npc == null || !c.IsSenderKnown) return;

            // The relationship bar rides the header for anyone the player actually knows, customer or not.
            if (npc.RelationData != null)
                view.Relationship = Math.Clamp(npc.RelationData.RelationDelta / 5.0, 0.0, 1.0);

            var customer = npc.GetComponent<Customer>();
            if (customer != null && customer.CustomerData != null)
            {
                view.InfoLabel = "Standards";
                view.InfoValue = Text.Clean(customer.CustomerData.Standards.ToString());
                view.StandardsColour = StandardsColour(customer.CustomerData.Standards.ToString());
                return;
            }

            // A supplier shows their debt instead, and shows it at zero too - "you owe nothing" is the answer the
            // player is looking for as often as the other one.
            var supplier = npc.GetComponent<Supplier>();
            if (supplier != null)
            {
                view.InfoLabel = "Debt";
                view.Debt = (int)Math.Round(supplier.Debt);
                view.InfoValue = Money.Format(supplier.Debt);
            }
        }

        /// <summary>
        /// A number that changes when the offered ANSWERS change, and at no other time.
        ///
        /// This is what a reply is checked against. It has to be stable while the player reads the buttons and it
        /// has to move the moment the buttons are renumbered - the app's own revision does neither.
        /// </summary>
        private static int ReplyToken(MSGConversation c)
        {
            unchecked
            {
                int hash = 19;

                var responses = c.currentResponses;
                if (responses == null) return hash;

                hash = hash * 31 + responses.Count;
                for (int i = 0; i < responses.Count; i++)
                    if (responses[i] != null) hash = hash * 31 + (responses[i].text ?? "").GetHashCode();

                return hash;
            }
        }

        /// <summary>The same for the lines a player can send unprompted - see ReplyToken.</summary>
        private static int SendToken(MSGConversation c)
        {
            unchecked
            {
                int hash = 23;

                var sendables = c.Sendables;
                if (sendables == null) return hash;

                for (int i = 0; i < sendables.Count; i++)
                {
                    SendableMessage s = sendables[i];
                    if (s == null || !s.ShouldShow()) continue;

                    hash = hash * 31 + (s.Text ?? "").GetHashCode();
                }

                return hash;
            }
        }

        /// <summary>
        /// The colour vanilla tints the standards star with.
        ///
        /// Not a palette of this mod's choosing: standards map onto a product quality (StandardsMethod) and the star
        /// takes that quality's colour (ItemQuality). Inventing a red-to-green ramp instead put a friendly green on
        /// the customers hardest to please.
        /// </summary>
        private static string StandardsColour(string standards) => standards switch
        {
            "VeryHigh" => "#ffc832",   // Heavenly
            "High" => "#e14bff",       // Premium
            "Moderate" => "#64beff",   // Standard
            "Low" => "#509132",        // Poor
            _ => "#7d3232",            // Trash
        };

        public string MarkRead(string npcId)
        {
            MSGConversation c = Find(npcId);
            if (c == null) return Reply.NotFound;

            // Local only, and it maintains MessagesApp's unread list plus the vanilla icon badge - which is why the
            // badge stays right without this mod tracking it.
            c.SetRead(true);
            return Reply.Ok;
        }

        public string SetHidden(string npcId, bool hidden)
        {
            MSGConversation c = Find(npcId);
            if (c == null) return Reply.NotFound;

            // The game refuses to hide some conversations. Asking first means the page can say "refused" rather
            // than appearing to work and leaving the row where it was.
            if (hidden && (c.sender == null || !c.sender.ConversationCanBeHidden)) return Reply.Refused;

            c.SetEntryVisibility(!hidden);
            return Reply.Ok;
        }

        public string ChooseReply(string npcId, int index, int seenRevision)
        {
            MSGConversation c = Find(npcId);
            if (c == null) return Reply.NotFound;

            // Checked against the ANSWERS, not against the app.
            //
            // The whole-app revision was the wrong yardstick and made every reply fail: it moves whenever anything
            // moves - a mugshot arriving, a message elsewhere - so by the time a player pressed a button it had
            // almost always changed and the press came back "stale". What this guards against is narrower and real:
            // a message arriving in between renumbers the buttons, and pressing the second one would then agree to
            // something else. That is exactly what a token over the answers detects, and nothing else.
            if (seenRevision != ReplyToken(c)) return Reply.Stale;

            Il2CppSystem.Collections.Generic.List<Response> responses = c.currentResponses;
            if (responses == null || index < 0 || index >= responses.Count) return Reply.NotFound;

            Response chosen = responses[index];
            if (chosen == null) return Reply.NotFound;

            // network: true unless the response opts out of the default behaviour - the same condition the vanilla
            // reply buttons pass, which is what keeps quest callbacks working.
            c.ResponseChosen(chosen, !chosen.disableDefaultResponseBehaviour);
            return Reply.Ok;
        }

        public string SendCanned(string npcId, int index, int seenRevision)
        {
            MSGConversation c = Find(npcId);
            if (c == null) return Reply.NotFound;

            if (seenRevision != SendToken(c)) return Reply.Stale;

            // The page is shown only the sendables that pass ShouldShow, so its index counts those - resolve it the
            // same way rather than against the raw list.
            Il2CppSystem.Collections.Generic.List<SendableMessage> sendables = c.Sendables;
            if (sendables == null) return Reply.NotFound;

            int shown = -1;
            for (int i = 0; i < sendables.Count; i++)
            {
                SendableMessage s = sendables[i];
                if (s == null || !s.ShouldShow()) continue;
                if (++shown != index) continue;

                if (!s.IsValid(out _)) return Reply.Refused;

                c.SendPlayerMessage(i, -1, true);
                return Reply.Ok;
            }

            return Reply.NotFound;
        }

        // ---- the two sheets the game opens on top of the thread -----------------------------------------------

        /// <summary>
        /// Switch the vanilla counter-offer, order and deal windows off - but ONLY while this mod is the one drawing
        /// the phone.
        ///
        /// The gate is the whole point. With ReplaceVanillaApps off the player is using the original screens, and
        /// hiding their windows meant accepting an offer in vanilla opened a deal picker that was immediately taken
        /// away again: the reply seemed to do nothing at all. A mod that has been told to stand aside has to stand
        /// aside completely.
        /// </summary>
        public void HideVanillaSheets()
        {
            if (Hijack.AppHijack.Ready) Sheets.KeepHidden();
        }

        /// <summary>
        /// What the counter-offer window is currently holding, or null when there is none.
        ///
        /// Read out of the window rather than recomputed from the contract: the window is what the send will act on,
        /// and the two agreeing is not something worth hoping for.
        /// </summary>
        private CounterOfferView Counter()
        {
            if (!Sheets.CounterReady) return null;

            CounterofferInterface c = Sheets.Counter;
            ProductDefinition product = c.selectedProduct;

            var view = new CounterOfferView
            {
                ProductId = product.ID,
                ProductName = Text.Clean(product.Name),
                Quantity = c.quantity,
                Price = (int)Math.Round(c.price),
                FairPrice = (int)Math.Round(product.MarketValue * c.quantity),
                MaxQuantity = c.MaxQuantity,
            };

            var discovered = ProductManager.DiscoveredProducts;
            if (discovered == null) return view;

            // Same set and same order as the vanilla picker: by drug type first, by name within it.
            var sorted = new List<ProductDefinition>();
            for (int i = 0; i < discovered.Count; i++)
                if (discovered[i] != null) sorted.Add(discovered[i]);

            sorted.Sort((a, b) =>
            {
                int type = ((int)a.DrugType).CompareTo((int)b.DrugType);
                return type != 0 ? type : string.CompareOrdinal(a.Name, b.Name);
            });

            foreach (ProductDefinition p in sorted)
                view.Products.Add(new PickView
                {
                    Id = p.ID,
                    Name = Text.Clean(p.Name),
                    HasIcon = _icons.Has(p.ID),
                });

            return view;
        }

        /// <summary>The order sheet's contents, or null when no supplier has one open.</summary>
        private OrderSheetView Order()
        {
            if (!Sheets.ShopReady) return null;

            PhoneShopInterface s = Sheets.Shop;

            var view = new OrderSheetView
            {
                Title = s.TitleLabel == null ? "" : Text.Clean(s.TitleLabel.text),
                Subtitle = s.SubtitleLabel == null ? "" : Text.Clean(s.SubtitleLabel.text),
                OrderLimit = (int)Math.Round(s.orderLimit),
                Debt = SupplierDebt(s),
            };

            var items = s._items;
            if (items == null) return view;

            for (int i = 0; i < items.Count; i++)
            {
                PhoneShopInterface.Listing listing = items[i];
                if (listing == null || listing.Item == null) continue;

                StorableItemDefinition item = listing.Item;
                bool unlocked = Unlocked(item);

                view.Items.Add(new OrderItemView
                {
                    Id = item.ID,
                    Name = Text.Clean(item.Name),
                    Price = (int)Math.Round(listing.Price),
                    HasIcon = _icons.Has(item.ID),
                    Locked = !unlocked,
                    LockText = unlocked ? "" : "Unlocks at " + item.RequiredRank.ToString(),
                });
            }

            return view;
        }

        private static bool Unlocked(StorableItemDefinition item)
        {
            try { return item.IsUnlocked; }
            catch { return true; }
        }

        /// <summary>What the sheet's own supplier is owed. The window shows it, so it comes from the same place.</summary>
        private static int SupplierDebt(PhoneShopInterface s)
        {
            MSGConversation c = s.conversation;
            if (c == null || c.sender == null) return 0;

            var supplier = c.sender.GetComponent<Supplier>();
            return supplier == null ? 0 : (int)Math.Round(supplier.Debt);
        }

        /// <summary>
        /// The four delivery windows and which of them can still be chosen.
        ///
        /// Vanilla's own rule, not a copy of it: a window is open while more than two hours of it remain, counted in
        /// game minutes and wrapping past midnight. Reading its buttons' own interactable flag would be simpler and
        /// wrong - they are only refreshed while the vanilla window is on screen, and this mod keeps it off.
        /// </summary>
        private static DealWindowView Deal()
        {
            var view = new DealWindowView();
            if (!NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.InstanceExists) return view;

            var time = NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance;
            int today = time.DailyMinSum;

            view.Now = Text.Clean(Il2CppScheduleOne.GameTime.TimeManager.Get12HourTime(time.CurrentTime));
            view.Minutes = today;

            foreach (Il2CppScheduleOne.Economy.EDealWindow window in new[]
                     {
                         Il2CppScheduleOne.Economy.EDealWindow.Morning,
                         Il2CppScheduleOne.Economy.EDealWindow.Afternoon,
                         Il2CppScheduleOne.Economy.EDealWindow.Night,
                         Il2CppScheduleOne.Economy.EDealWindow.LateNight,
                     })
            {
                Il2CppScheduleOne.Economy.DealWindowInfo info =
                    Il2CppScheduleOne.Economy.DealWindowInfo.GetWindowInfo(window);

                int ends = Il2CppScheduleOne.GameTime.TimeManager.GetMinSumFrom24HourTime(info.EndTime);
                if (today > ends) ends += 1440;

                view.Windows.Add(new DealSlotView
                {
                    Name = Spaced(window.ToString()),
                    Span = Oclock(info.StartTime) + "-" + Oclock(info.EndTime),
                    Choosable = ends - today > 120,
                });
            }

            return view;
        }

        /// <summary>
        /// A window boundary the way the dial labels it: "6AM", "12PM". Not the game's Get12HourTime, which spells
        /// out "6:00 AM" - the four labels on the vanilla wheel are the short form, and the quadrants have no room
        /// for the long one.
        /// </summary>
        private static string Oclock(int time24)
        {
            int hour = time24 / 100 % 24;
            string half = hour < 12 ? "AM" : "PM";

            int shown = hour % 12;
            if (shown == 0) shown = 12;

            return shown + half;
        }

        /// <summary>"LateNight" is one word in the enum and two on the screen.</summary>
        private static string Spaced(string name)
        {
            var text = new System.Text.StringBuilder(name.Length + 2);

            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i])) text.Append(' ');
                text.Append(name[i]);
            }

            return text.ToString();
        }

        /// <summary>Choose a delivery window, by its position in the game's own list of four.</summary>
        public string ChooseDealWindow(int index)
        {
            if (!Sheets.DealReady) return Reply.NotFound;
            if (index < 0 || index > 3) return Reply.BadArgs;

            var window = (Il2CppScheduleOne.Economy.EDealWindow)index;

            // Straight to the game's own handler, which runs the callback that files the contract and then shuts the
            // window. Pressing the button object instead would need it on screen, and it deliberately is not.
            if (Sheets.Deal.callback == null)
                Core.Log.Warning("[Reflash] the deal window has no callback - the contract will not be filed.");

            Sheets.Deal.ButtonClicked(window);
            return Reply.Ok;
        }

        public string SendCounterOffer(string productId, int quantity, int price)
        {
            if (!Sheets.CounterReady) return Reply.NotFound;

            CounterofferInterface c = Sheets.Counter;

            ProductDefinition product = Pick(productId);
            if (product == null) return Reply.NotFound;

            c.selectedProduct = product;
            c.quantity = Math.Clamp(quantity, 1, c.MaxQuantity);
            c.price = Math.Clamp(price, 1, 9999);

            // Send re-reads the field rather than trusting its own number, so the field has to carry it. Invariant
            // because the parse on the other side is invariant too - the mod runtime has no culture data.
            if (c.PriceInput != null)
                c.PriceInput.text = c.price.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            c.Send();
            return Reply.Ok;
        }

        /// <summary>The discovered product with this id. Only discovered ones: the vanilla picker offers no others.</summary>
        private static ProductDefinition Pick(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;

            var discovered = ProductManager.DiscoveredProducts;
            if (discovered == null) return null;

            for (int i = 0; i < discovered.Count; i++)
                if (discovered[i] != null && discovered[i].ID == productId) return discovered[i];

            return null;
        }

        public string PlaceOrder(IReadOnlyList<KeyValuePair<string, int>> quantities)
        {
            if (!Sheets.ShopReady) return Reply.NotFound;

            PhoneShopInterface s = Sheets.Shop;
            var items = s._items;
            if (items == null || s._cart == null) return Reply.NotFound;

            s._cart.Clear();

            float total = 0f;
            int count = 0;

            for (int i = 0; i < items.Count; i++)
            {
                PhoneShopInterface.Listing listing = items[i];
                if (listing == null || listing.Item == null || !Unlocked(listing.Item)) continue;

                int wanted = Math.Clamp(Amount(quantities, listing.Item.ID), 0, 99);
                if (wanted <= 0) continue;

                s._cart.Add(new PhoneShopInterface.CartEntry(listing, wanted));
                total += listing.Price * wanted;
                count += wanted;
            }

            // The same three conditions vanilla's confirm button greys itself out on. Checked here as well because
            // the button is not what is being pressed any more.
            if (count == 0 || total > s.orderLimit || count > 10)
            {
                s._cart.Clear();
                return Reply.Refused;
            }

            s.ConfirmOrderPressed();
            return Reply.Ok;
        }

        private static int Amount(IReadOnlyList<KeyValuePair<string, int>> quantities, string id)
        {
            if (quantities == null) return 0;

            for (int i = 0; i < quantities.Count; i++)
                if (string.Equals(quantities[i].Key, id, StringComparison.Ordinal)) return quantities[i].Value;

            return 0;
        }

        public string CloseSheet()
        {
            bool closed = false;

            if (Sheets.Counter != null && Sheets.Counter.IsOpen) { Sheets.Counter.Close(); closed = true; }
            if (Sheets.Shop != null && Sheets.Shop.IsOpen) { Sheets.Shop.Close(); closed = true; }
            if (Sheets.Deal != null && Sheets.Deal.IsOpen) { Sheets.Deal.SetIsOpen(false); closed = true; }

            return closed ? Reply.Ok : Reply.NotFound;
        }

        public int Unread
        {
            get
            {
                Il2CppSystem.Collections.Generic.List<MSGConversation> active = MessagesApp.ActiveConversations;
                if (active == null) return 0;

                int n = 0;
                for (int i = 0; i < active.Count; i++)
                    if (active[i] != null && active[i].EntryVisible && !active[i].Read) n++;

                return n;
            }
        }

        /// <summary>
        /// Changes when a conversation is added, a message arrives, a thread is read, or the offered replies
        /// change. The reply count is in here deliberately: it is what the stale check protects.
        /// </summary>
        public int Revision
        {
            get
            {
                unchecked
                {
                    int hash = 17;

                    // Bumped once when the pictures have all arrived, not once per picture - a revision change
                    // rebuilds the page, and one rebuild per batch is a visible stutter.
                    hash = hash * 31 + _faces.Settled;
                    hash = hash * 31 + _icons.Settled;

                    // A sheet opening is the game's doing, not this app's - the page finds out about it here.
                    hash = hash * 31 + (Sheets.CounterReady ? 1 : 0);
                    hash = hash * 31 + (Sheets.ShopReady ? 1 : 0);
                    hash = hash * 31 + (Sheets.DealReady ? 1 : 0);

                    Il2CppSystem.Collections.Generic.List<MSGConversation> active = MessagesApp.ActiveConversations;
                    hash = hash * 31 + (active?.Count ?? 0);

                    if (active == null) return hash;

                    for (int i = 0; i < active.Count; i++)
                    {
                        MSGConversation c = active[i];
                        if (c == null) continue;

                        hash = hash * 31 + (c.messageHistory?.Count ?? 0);
                        hash = hash * 31 + (c.currentResponses?.Count ?? 0);
                        hash = hash * 31 + (c.Read ? 1 : 0);
                        hash = hash * 31 + (c.EntryVisible ? 1 : 0);
                    }

                    return hash;
                }
            }
        }

        private static string LastText(MSGConversation c)
        {
            Il2CppSystem.Collections.Generic.List<Message> history = c.messageHistory;
            if (history == null || history.Count == 0) return "";

            Message last = history[history.Count - 1];
            return last == null ? "" : Text.Clean(last.text);
        }

        private static MSGConversation Find(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;

            Il2CppSystem.Collections.Generic.List<MSGConversation> active = MessagesApp.ActiveConversations;
            if (active == null) return null;

            for (int i = 0; i < active.Count; i++)
            {
                MSGConversation c = active[i];
                if (c != null && c.sender != null && c.sender.ID == npcId) return c;
            }

            return null;
        }
    }
}
