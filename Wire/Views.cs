namespace Reflash.Wire
{
    /// <summary>
    /// The shapes each app reads from the game, as plain data with no engine type in them.
    ///
    /// They exist so an app's view logic can be written and tested without a game: the Game/ half fills these in
    /// from the real managers, a test fills them in by hand, and the app cannot tell the difference. That also keeps
    /// the awkward parts of the game's model - a conversation that is also its own UI, a quest that instantiates its
    /// own journal row - on the far side of one boundary.
    ///
    /// Only what a page actually draws. A field nobody shows is a field that has to be kept correct for nothing.
    /// </summary>
    /// <summary>
    /// A line of text that carries its own colour, because the game does.
    ///
    /// An effect is not merely named in vanilla - it is written in the colour that effect owns, and the same effect
    /// appears in the product screen and in a customer's preferences in the same colour. Sending the name alone left
    /// both screens guessing, and both guessed the same wrong green.
    /// </summary>
    internal sealed class LabelView
    {
        internal string Text = "";

        /// <summary>CSS <c>#rrggbb</c>, or empty when the game does not colour this one.</summary>
        internal string Colour = "";
    }

    internal sealed class QuestView
    {
        internal string Id;
        internal string Title;
        internal string Subtitle;
        internal string Description;
        internal bool Tracked;

        /// <summary>Empty when the quest does not expire. Already formatted - "2 days", "4 hours" - because how long
        /// is left is a game-time calculation and the page has no clock.</summary>
        internal string ExpiresIn = "";

        /// <summary>True when the remaining time is short enough that vanilla colours it red.</summary>
        internal bool Critical;

        internal List<QuestStepView> Steps = new List<QuestStepView>();
    }

    internal sealed class QuestStepView
    {
        internal string Title;

        /// <summary>"active", "completed" or "failed" - the three the journal distinguishes.</summary>
        internal string State;

        /// <summary>Whether this step has somewhere on the map to point at, which is what enables "show on map".</summary>
        internal bool HasPoi;
    }

    internal sealed class RankView
    {
        internal string Name = "";
        internal int Tier;
        internal int Xp;
        internal int XpForNext;
    }

    internal sealed class ThreadView
    {
        internal string Id;
        internal string Name;
        internal bool Known;
        internal bool Unread;
        internal string Preview = "";
        internal List<string> Categories = new List<string>();

        /// <summary>Whether a mugshot has been published for this contact yet - they arrive a few per tick.</summary>
        internal bool HasFace;

        /// <summary>
        /// How much of an open offer's time is left, 0..1, or -1 when there is no offer.
        ///
        /// Vanilla draws this as a thin bar under the row, colouring it from red to green - and it is the only
        /// thing on that screen telling you an offer is about to lapse.
        /// </summary>
        internal double OfferLeft = -1;

        /// <summary>Whether vanilla offers the little cross that hides the row.</summary>
        internal bool CanHide;
    }

    internal sealed class MessageView
    {
        /// <summary>"me" or "them". No timestamp: the game's Message carries none, so inventing one would be a
        /// prettier lie than showing nothing.</summary>
        internal string From;
        internal string Text;
        internal bool EndOfGroup;
    }

    internal sealed class ThreadDetailView
    {
        internal string Id;
        internal string Name;
        internal bool Known;
        internal string Avatar = "";
        internal string InfoLabel = "";
        internal string InfoValue = "";

        /// <summary>Whether a mugshot has been published for this contact yet.</summary>
        internal bool HasFace;

        /// <summary>0..1 across the relationship scale, or -1 when this contact has no relationship to show.</summary>
        internal double Relationship = -1;

        /// <summary>The colour vanilla tints the standards star with - the quality band the customer expects.</summary>
        internal string StandardsColour = "";

        /// <summary>A supplier's outstanding debt, or -1 for anyone who is not a supplier.</summary>
        internal int Debt = -1;
        internal List<MessageView> Messages = new List<MessageView>();
        internal List<string> Replies = new List<string>();
        internal List<SendableView> Sendables = new List<SendableView>();

        /// <summary>
        /// What a reply is checked against - a number over the ANSWERS rather than over the whole app.
        ///
        /// The page sends it back with the press. Using the app's revision for this made every reply fail: it
        /// moves whenever anything anywhere moves, so it had almost always changed by the time a button was
        /// pressed.
        /// </summary>
        internal int ReplyToken;

        /// <summary>The same for the lines a player can send unprompted.</summary>
        internal int SendToken;

        /// <summary>The counter-offer sheet, when the game has one open. Null the rest of the time.</summary>
        internal CounterOfferView Counter;

        /// <summary>The dead-drop order sheet, when the game has one open.</summary>
        internal OrderSheetView Order;

        /// <summary>The window picker that accepting an offer brings up.</summary>
        internal DealWindowView Deal;
    }

    internal sealed class SendableView
    {
        internal string Text;
        internal bool Valid;
        internal string Reason = "";
    }

    /// <summary>
    /// The counter-offer sheet, while one is open.
    ///
    /// Opening it is not this mod's decision: choosing the "[Counter-offer]" reply makes the game open its own
    /// interface and hand it the callback that actually sends the offer. That callback cannot be reconstructed from
    /// outside, so the sheet is driven rather than replaced - this view is what the game currently has in it.
    /// </summary>
    internal sealed class CounterOfferView
    {
        internal string ProductId = "";
        internal string ProductName = "";
        internal int Quantity;
        internal int Price;

        /// <summary>What the goods are worth at the market rate, which is the number vanilla prints under the field.</summary>
        internal int FairPrice;

        internal int MaxQuantity = 50;

        /// <summary>Everything the player has discovered, for the picker. Same set and order as the vanilla one.</summary>
        internal List<PickView> Products = new List<PickView>();
    }

    /// <summary>One entry in a picker: an id, a name and whether its icon has been published yet.</summary>
    internal sealed class PickView
    {
        internal string Id;
        internal string Name = "";
        internal bool HasIcon;
    }

    /// <summary>
    /// The order sheet a supplier opens for a dead drop. Same arrangement as the counter-offer: the game opens it and
    /// keeps the callback, this reads out what it is holding.
    /// </summary>
    internal sealed class OrderSheetView
    {
        internal string Title = "";
        internal string Subtitle = "";

        /// <summary>What may still be spent with this supplier - their limit less what is already owed.</summary>
        internal int OrderLimit;
        internal int Debt;

        /// <summary>Ten, in vanilla, and the sheet says so rather than only refusing at the end.</summary>
        internal int ItemLimit = 10;

        internal List<OrderItemView> Items = new List<OrderItemView>();
    }

    /// <summary>
    /// The four delivery windows, as the game's own picker offers them.
    ///
    /// Saying "Sure thing" to a customer does not close the deal - it asks WHEN. Vanilla opens a clock with four
    /// windows on it and keeps the callback that files the contract, which is the same arrangement as the
    /// counter-offer: the window is driven, not replaced.
    /// </summary>
    internal sealed class DealWindowView
    {
        /// <summary>The time it is now, in the game's own wording.</summary>
        internal string Now = "";

        /// <summary>The same time as minutes since midnight, which is what the dial's marker is placed from. The
        /// wording alone would have to be parsed back into a number, and a page should not be re-deriving a clock.
        /// </summary>
        internal int Minutes;

        internal List<DealSlotView> Windows = new List<DealSlotView>();
    }

    internal sealed class DealSlotView
    {
        internal string Name = "";

        /// <summary>"10 AM - 8 PM", written by the game's own clock rather than formatted here.</summary>
        internal string Span = "";

        /// <summary>Vanilla greys out a window with less than two hours left in it rather than hiding it.</summary>
        internal bool Choosable;
    }

    internal sealed class OrderItemView
    {
        internal string Id;
        internal string Name = "";
        internal int Price;
        internal bool HasIcon;

        /// <summary>Set when the player's rank is too low. Vanilla covers the row rather than hiding it.</summary>
        internal bool Locked;
        internal string LockText = "";
    }

    internal sealed class ProductView
    {
        internal string Id;
        internal string Name;
        internal bool Listed;
        internal bool Favourite;
        internal int Price;
        internal int MarketValue;
        internal string Quality = "";

        /// <summary>Whether the product's own icon has been published yet - they arrive a few per tick.</summary>
        internal bool HasIcon;
    }

    internal sealed class ProductDetailView
    {
        internal string Id;
        internal string Name;
        internal string Description = "";
        internal bool Listed;
        internal bool Favourite;
        internal int Price;
        internal int MarketValue;
        internal int Addictiveness;
        internal List<LabelView> Properties = new List<LabelView>();
        internal List<string> Recipe = new List<string>();
    }

    internal sealed class ShopView
    {
        internal string Id;
        internal string Name;

        /// <summary>The line under the name - "General (legal) supplies". Read off the vanilla card, not invented.</summary>
        internal string Description = "";

        /// <summary>The card's colour, which the game authors per shop rather than deriving from the name.</summary>
        internal string Colour = "";

        /// <summary>Whether the shopkeeper's picture has been published yet, and the name it went out under - the
        /// id itself carries apostrophes and spaces and cannot be part of an address.</summary>
        internal bool HasIcon;
        internal string IconKey = "";

        /// <summary>
        /// What the game's own order panel currently reads, for whatever quantities were last pushed into it.
        ///
        /// Read rather than recomputed on purpose: the delivery fee, the total and the time are the game's
        /// arithmetic, spread over a configuration asset and the shop, and a copy of that formula here would agree
        /// today and drift at the next balance patch.
        /// </summary>
        internal string ItemTotal = "";
        internal string Fee = "";
        internal string OrderTotal = "";
        internal string Time = "";

        /// <summary>Why the order cannot be placed, in the game's own words. Empty when it can.</summary>
        internal string Note = "";
        internal bool CanOrder;

        /// <summary>Where it can go and which dock it arrives at, as the game's own dropdowns list them.</summary>
        internal List<string> Destinations = new List<string>();
        internal int DestinationIndex;
        internal List<string> Docks = new List<string>();
        internal int DockIndex;

        internal List<ListingView> Listings = new List<ListingView>();
    }

    internal sealed class ListingView
    {
        internal string Id;
        internal string Name;
        internal int Price;
        internal int Quantity;
        internal bool Affordable;

        /// <summary>Whether the item's picture has been published yet.</summary>
        internal bool HasIcon;

        /// <summary>Set when the player's rank is too low. Vanilla darkens the row and puts a padlock where the
        /// plus would be, rather than hiding it.</summary>
        internal bool Locked;
    }

    internal sealed class DeliveryView
    {
        internal string Id;
        internal string Shop;
        internal string Destination = "";
        internal string Status = "";
        internal string Eta = "";
        internal List<string> Items = new List<string>();
    }

    internal sealed class DealerView
    {
        internal string Id;
        internal string Name;
        internal string Region = "";
        internal string Home = "";
        internal int Cash;
        internal int CutPercent;
        internal int CustomerCount;
        internal int CustomerLimit;
        internal string Avatar = "";
    }

    internal sealed class DealerDetailView
    {
        internal DealerView Dealer;
        internal List<SlotView> Inventory = new List<SlotView>();
        internal List<ContactView> Customers = new List<ContactView>();
    }

    internal sealed class SlotView
    {
        internal string Name = "";
        internal int Quantity;
    }

    internal sealed class ContactView
    {
        internal string Id;
        internal string Name;
        internal string Kind = "";
        internal string Region = "";
        internal string Avatar = "";

        /// <summary>0..1, what the relationship bar fills to.</summary>
        internal double Relationship;
        internal string RelationshipLabel = "";
        internal bool Unlocked;
    }

    internal sealed class ContactDetailView
    {
        internal ContactView Contact;
        internal string Standards = "";
        internal string StandardsColour = "";
        internal int AddictionPercent;
        internal int SpentTotal;
        internal int Debt;
        internal List<LabelView> PreferredProperties = new List<LabelView>();
        internal List<string> TopPurchases = new List<string>();
        internal List<ContactView> Connections = new List<ContactView>();
        internal bool HasPoi;
    }

    internal sealed class RegionView
    {
        internal string Id;
        internal string Name;
        internal bool Unlocked;
        internal int CartelInfluencePercent;
        internal int ContactCount;
    }

    /// <summary>
    /// One region's relationship graph: the same circles and lines the vanilla contacts app draws.
    ///
    /// The positions are not this mod's to invent. Vanilla's layout is authored - every circle sits at a hand-placed
    /// anchoredPosition in the prefab - so a spring layout computed here would put familiar faces in unfamiliar
    /// places and be wrong in a way a player would notice immediately. They are read off the live circles instead,
    /// in css pixels, already divided by the canvas factor.
    /// </summary>
    internal sealed class GraphView
    {
        internal List<NodeView> Nodes = new List<NodeView>();
        internal List<EdgeView> Edges = new List<EdgeView>();

        /// <summary>The box the graph occupies, so the page can size its world without measuring anything.</summary>
        internal double MinX, MinY, Width, Height;
    }

    internal sealed class NodeView
    {
        internal string Id;
        internal string Name = "";
        internal string Kind = "";
        internal double X, Y;

        /// <summary>0..5, as the game stores it. The notch's rotation is this, and so is the category.</summary>
        internal double Relationship;
        internal string RelationshipLabel = "";
        internal string RelationshipColour = "";

        /// <summary>0..1. Vanilla tints the portrait's backing from #3c3c3c to #780f0f across this.</summary>
        internal double Addiction;

        internal bool Unlocked;

        /// <summary>
        /// Vanilla blacks the headshot out for someone you have not properly met - it does NOT remove it. The
        /// picture is always there; only its tint says whether you are supposed to recognise the face.
        /// </summary>
        internal bool Hidden;

        internal bool Supplier;
        internal bool Dealer;

        /// <summary>Whether a mugshot has been published for this one yet - they arrive a few per tick.</summary>
        internal bool HasFace;
    }

    internal sealed class EdgeView
    {
        internal double X, Y, Length, AngleDeg;
    }

    /// <summary>
    /// One map pin. Coordinates are already normalised to 0..1 across the map image, because turning a world
    /// position into a map position is the game's arithmetic (MapPositionUtility) and the page must not guess at it.
    /// </summary>
    internal sealed class PoiView
    {
        internal string Id;
        internal string Label = "";
        internal string Kind = "";

        /// <summary>Which region this point sits in, so the map can jump to a region without a table of
        /// coordinates that a map update would silently invalidate.</summary>
        internal string Region = "";

        internal double X;
        internal double Y;

        /// <summary>
        /// How big an AREA this marker stands for, as a fraction of the map's width. Zero for a point.
        ///
        /// A potential customer is not at a spot - vanilla draws the disc they can be found somewhere inside, and
        /// the disc is the useful part of the marker.
        /// </summary>
        internal double Radius;

        /// <summary>Whether this marker's mugshot has been published yet. They arrive a few per tick, so a "no" is
        /// usually a "not yet".</summary>
        internal bool HasFace;
    }
}
