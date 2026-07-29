using Reflash.Wire;

namespace Reflash.Screens
{
    /// <summary>
    /// Messages. The app with the most going on: a thread list, a conversation, offered replies, the canned lines
    /// the player can open with, and an unread badge.
    ///
    /// Every command carries the revision the page was looking at. That is not defensive padding - it is the one
    /// race this app genuinely has. Replies are identified by position, and a message arriving between the render
    /// and the tap renumbers them, so answering index 1 would send a different reply than the one that was read.
    /// On a companion phone, where the render is a network round trip older, it stops being rare.
    /// </summary>
    internal sealed class MessagesApp : IAppPort, INeedsAppHandle, IWarmUp
    {
        /// <summary>Handed on to the source, which publishes the contact mugshots the rows show.</summary>
        public void UseHandle(Sideload.Api.AppHandle handle)
        {
            if (_game is INeedsAppHandle needs) needs.UseHandle(handle);
        }

        /// <summary>
        /// A few mugshots per tick - reading them all back at once is a stutter; this is not - and a check that the
        /// game's own counter-offer and order windows stay switched off, since it opens them itself and they sit
        /// outside the container this mod turns off.
        /// </summary>
        public void WarmUp()
        {
            _game.WarmFaces();
            _game.HideVanillaSheets();
        }

        private readonly IMessagesSource _game;

        internal MessagesApp(IMessagesSource game) => _game = game;

        public string Id => "reflash-messages";

        public VanillaApp Replaces => VanillaApp.Messages;

        public int Revision => _game.Revision;

        public int Badge => _game.Unread;

        public string State(string section)
        {
            // "thread:<npcId>" is one conversation, anything else is the list. Split on the first colon only: an
            // NPC id is opaque and could contain one.
            if (section != null && section.StartsWith("thread:", StringComparison.Ordinal))
                return ThreadJson(section.Substring(7));

            return ListJson();
        }

        private string ListJson()
        {
            var root = Json.Object().Add("rev", Revision).Add("unread", _game.Unread);

            var list = Json.Array();
            foreach (ThreadView t in _game.Threads())
            {
                var cats = Json.Array();
                foreach (string c in t.Categories) cats.Item(c);

                list.Item(Json.Object()
                    .Add("id", t.Id)
                    .Add("name", t.Name)
                    .Add("known", t.Known)
                    .Add("unread", t.Unread)
                    .Add("preview", Text.Ellipsis(t.Preview, 60))
                    .Add("face", t.HasFace)
                    .Add("offer", t.OfferLeft)
                    .Add("canHide", t.CanHide)
                    .Add("cats", cats));
            }

            return root.Add("threads", list).Close();
        }

        private string ThreadJson(string npcId)
        {
            ThreadDetailView t = _game.Thread(npcId);
            if (t == null) return Json.Object().Add("rev", Revision).AddNull("thread").Close();

            var messages = Json.Array();
            foreach (MessageView m in t.Messages)
                messages.Item(Json.Object()
                    .Add("from", m.From)
                    .Add("text", m.Text)
                    .Add("end", m.EndOfGroup));

            var replies = Json.Array();
            foreach (string r in t.Replies) replies.Item(r);

            var sendables = Json.Array();
            foreach (SendableView s in t.Sendables)
                sendables.Item(Json.Object()
                    .Add("text", s.Text)
                    .Add("valid", s.Valid)
                    .Add("reason", s.Reason));

            var root = Json.Object().Add("rev", Revision);

            // The two sheets the game opens on top of a thread. Absent almost always, so they are written as null
            // rather than as an empty object - the page tells "no sheet" from "an empty one" by identity.
            if (t.Counter == null) root.AddNull("counter"); else root.Add("counter", CounterJson(t.Counter));
            if (t.Order == null) root.AddNull("order"); else root.Add("order", OrderJson(t.Order));
            if (t.Deal == null) root.AddNull("deal"); else root.Add("deal", DealJson(t.Deal));

            return root
                .Add("thread", Json.Object()
                    .Add("id", t.Id)
                    .Add("name", t.Name)
                    .Add("known", t.Known)
                    .Add("avatar", t.Avatar)
                    .Add("face", t.HasFace)
                    .Add("rel", t.Relationship)
                    .Add("standardsColour", t.StandardsColour)
                    .Add("debt", t.Debt)
                    .Add("replyToken", t.ReplyToken)
                    .Add("sendToken", t.SendToken)
                    .Add("infoLabel", t.InfoLabel)
                    .Add("infoValue", t.InfoValue)
                    .Add("messages", messages)
                    .Add("replies", replies)
                    .Add("sendables", sendables))
                .Close();
        }

        private static Json CounterJson(CounterOfferView c)
        {
            var products = Json.Array();
            foreach (PickView p in c.Products)
                products.Item(Json.Object().Add("id", p.Id).Add("name", p.Name).Add("icon", p.HasIcon));

            return Json.Object()
                .Add("productId", c.ProductId)
                .Add("productName", c.ProductName)
                .Add("quantity", c.Quantity)
                .Add("price", c.Price)
                .Add("fair", c.FairPrice)
                .Add("maxQuantity", c.MaxQuantity)
                .Add("products", products);
        }

        private static Json OrderJson(OrderSheetView o)
        {
            var items = Json.Array();
            foreach (OrderItemView i in o.Items)
                items.Item(Json.Object()
                    .Add("id", i.Id)
                    .Add("name", i.Name)
                    .Add("price", i.Price)
                    .Add("icon", i.HasIcon)
                    .Add("locked", i.Locked)
                    .Add("lockText", i.LockText));

            return Json.Object()
                .Add("title", o.Title)
                .Add("subtitle", o.Subtitle)
                .Add("limit", o.OrderLimit)
                .Add("debt", o.Debt)
                .Add("itemLimit", o.ItemLimit)
                .Add("items", items);
        }

        private static Json DealJson(DealWindowView d)
        {
            var windows = Json.Array();
            foreach (DealSlotView w in d.Windows)
                windows.Item(Json.Object().Add("name", w.Name).Add("span", w.Span).Add("open", w.Choosable));

            return Json.Object().Add("now", d.Now).Add("minutes", d.Minutes).Add("windows", windows);
        }

        /// <summary>Trailing id/amount pairs, as the order sheet sends its whole basket in one command.</summary>
        private static List<KeyValuePair<string, int>> Pairs(Cmd cmd)
        {
            var pairs = new List<KeyValuePair<string, int>>();

            for (int i = 0; i + 1 < cmd.Count; i += 2)
            {
                string id = cmd.Str(i);
                if (string.IsNullOrEmpty(id) || !cmd.Int(i + 1, out int amount)) break;

                if (amount > 0) pairs.Add(new KeyValuePair<string, int>(id, amount));
            }

            return pairs;
        }

        public string Act(Cmd cmd)
        {
            // The sheet commands act on whatever the game currently has open, which is one thing at a time - so they
            // carry no contact and are answered before the id is demanded.
            switch (cmd.Op)
            {
                case "counter":
                    if (!cmd.Int(1, out int counterQuantity) || !cmd.Int(2, out int counterPrice)) return Reply.BadArgs;
                    return _game.SendCounterOffer(cmd.Str(0), counterQuantity, counterPrice);

                case "order":
                    return _game.PlaceOrder(Pairs(cmd));

                case "deal":
                    return cmd.Int(0, out int window) ? _game.ChooseDealWindow(window) : Reply.BadArgs;

                case "sheet-close":
                    return _game.CloseSheet();
            }

            string npcId = cmd.Str(0);
            if (string.IsNullOrEmpty(npcId)) return Reply.BadArgs;

            switch (cmd.Op)
            {
                case "read":
                    return _game.MarkRead(npcId);

                case "hide":
                    return cmd.Flag(1, out bool hidden) ? _game.SetHidden(npcId, hidden) : Reply.BadArgs;

                case "reply":
                    // index, then the revision the page rendered from.
                    if (!cmd.Int(1, out int replyIndex) || !cmd.Int(2, out int seenReply)) return Reply.BadArgs;
                    return _game.ChooseReply(npcId, replyIndex, seenReply);

                case "send":
                    if (!cmd.Int(1, out int sendIndex) || !cmd.Int(2, out int seenSend)) return Reply.BadArgs;
                    return _game.SendCanned(npcId, sendIndex, seenSend);

                default:
                    return Reply.BadArgs;
            }
        }
    }
}
