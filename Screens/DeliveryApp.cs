using Reflash.Wire;

namespace Reflash.Screens
{
    /// <summary>
    /// Deliveries. Three tabs, like vanilla: what can be ordered, what is on its way, and what has arrived.
    ///
    /// Ordering does NOT rebuild the transaction. Vanilla's SubmitOrder does five things together - the delivery
    /// fee from configuration, a VariableDatabase notification per line, the DeliveryInstance and its id, the
    /// server call, the receipt, and the money - and any of those formulas can change in a balance patch. So the
    /// page fills in quantities and presses the game's own button; this app never learns what a delivery costs.
    /// </summary>
    internal sealed class DeliveryApp : IAppPort, INeedsAppHandle, IWarmUp
    {
        private readonly IDeliverySource _game;

        internal DeliveryApp(IDeliverySource game) => _game = game;

        /// <summary>Handed on to the source, which publishes the shopkeepers' pictures the cards show.</summary>
        public void UseHandle(Sideload.Api.AppHandle handle)
        {
            if (_game is INeedsAppHandle needs) needs.UseHandle(handle);
        }

        /// <summary>A few pictures per tick, off the pulse rather than out of a page's call.</summary>
        public void WarmUp() => _game.WarmIcons();

        public string Id => "reflash-delivery";

        public VanillaApp Replaces => VanillaApp.Delivery;

        public int Revision => _game.Revision;

        /// <summary>Deliveries in transit, which vanilla shows as a count on the tab. Worth a badge: it is the one
        /// thing here you are waiting for.</summary>
        public int Badge => _game.ActiveCount;

        public string State(string section)
        {
            switch (section)
            {
                case "active": return DeliveriesJson("active", _game.Active());
                case "history": return DeliveriesJson("history", _game.History());
                default: return ShopsJson();
            }
        }

        private string ShopsJson()
        {
            var shops = Json.Array();
            foreach (ShopView s in _game.Shops())
            {
                var listings = Json.Array();
                foreach (ListingView l in s.Listings)
                    listings.Item(Json.Object()
                        .Add("id", l.Id)
                        .Add("name", l.Name)
                        .Add("price", l.Price)
                        .Add("qty", l.Quantity)
                        .Add("afford", l.Affordable)
                        .Add("icon", l.HasIcon)
                        .Add("locked", l.Locked));

                shops.Item(Json.Object()
                    .Add("id", s.Id)
                    .Add("name", s.Name)
                    .Add("desc", s.Description)
                    .Add("colour", s.Colour)
                    .Add("icon", s.HasIcon)
                    .Add("iconKey", s.IconKey)
                    .Add("itemTotal", s.ItemTotal)
                    .Add("fee", s.Fee)
                    .Add("orderTotal", s.OrderTotal)
                    .Add("time", s.Time)
                    .Add("note", s.Note)
                    .Add("canOrder", s.CanOrder)
                    .Add("destinations", Strings(s.Destinations))
                    .Add("destination", s.DestinationIndex)
                    .Add("docks", Strings(s.Docks))
                    .Add("dock", s.DockIndex)
                    .Add("listings", listings));
            }

            return Json.Object()
                .Add("rev", Revision)
                .Add("balance", _game.Balance)
                .Add("active", _game.ActiveCount)
                .Add("shops", shops)
                .Close();
        }

        private string DeliveriesJson(string which, List<DeliveryView> deliveries)
        {
            var list = Json.Array();
            foreach (DeliveryView d in deliveries)
            {
                var items = Json.Array();
                foreach (string i in d.Items) items.Item(i);

                list.Item(Json.Object()
                    .Add("id", d.Id)
                    .Add("shop", d.Shop)
                    .Add("destination", d.Destination)
                    .Add("status", d.Status)
                    .Add("eta", d.Eta)
                    .Add("items", items));
            }

            return Json.Object().Add("rev", Revision).Add("which", which).Add("deliveries", list).Close();
        }

        private static Json Strings(List<string> values)
        {
            var list = Json.Array();
            foreach (string v in values) list.Item(v);
            return list;
        }

        public string Act(Cmd cmd)
        {
            switch (cmd.Op)
            {
                case "order":
                {
                    // shopId, then listingId/quantity pairs. One command for the whole basket, because vanilla's
                    // submit is one transaction - sending lines one at a time would mean one delivery per item.
                    string shopId = cmd.Str(0);
                    if (string.IsNullOrEmpty(shopId)) return Reply.BadArgs;

                    var quantities = new List<KeyValuePair<string, int>>();
                    for (int i = 1; i + 1 < cmd.Count + 1; i += 2)
                    {
                        string listingId = cmd.Str(i);
                        if (listingId == null) break;
                        if (!cmd.Int(i + 1, out int qty)) return Reply.BadArgs;

                        if (qty > 0) quantities.Add(new KeyValuePair<string, int>(listingId, qty));
                    }

                    if (quantities.Count == 0) return Reply.BadArgs;
                    return _game.Order(shopId, quantities);
                }

                case "fill":
                {
                    // The same basket, put into the game's panel without submitting, so the next state read carries
                    // the fee and the total the game worked out.
                    string shopId = cmd.Str(0);
                    if (string.IsNullOrEmpty(shopId)) return Reply.BadArgs;

                    var quantities = new List<KeyValuePair<string, int>>();
                    for (int i = 1; i + 1 < cmd.Count + 1; i += 2)
                    {
                        string listingId = cmd.Str(i);
                        if (listingId == null) break;
                        if (!cmd.Int(i + 1, out int qty)) return Reply.BadArgs;

                        if (qty > 0) quantities.Add(new KeyValuePair<string, int>(listingId, qty));
                    }

                    return _game.Fill(shopId, quantities);
                }

                case "dest":
                case "dock":
                {
                    string shopId = cmd.Str(0);
                    if (string.IsNullOrEmpty(shopId) || !cmd.Int(1, out int index)) return Reply.BadArgs;

                    return _game.Choose(shopId, cmd.Op == "dock", index);
                }

                default:
                    return Reply.BadArgs;
            }
        }
    }
}
