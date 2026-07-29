namespace Reflash.Wire
{
    /// <summary>
    /// What each app needs from the game, one interface per app.
    ///
    /// This is the seam that keeps every IL2CPP reference in Game/. An app asks for views and issues intents; it
    /// never learns that a conversation is also a UI object, that a quest instantiates its own journal row, or that
    /// a price goes through a ServerRpc.
    ///
    /// Every mutating method returns a <see cref="Reply"/> code rather than throwing, because "the dealer already
    /// has ten customers" is an answer the page shows, not an error anyone should catch.
    /// </summary>
    internal interface IJournalSource
    {
        List<QuestView> ActiveQuests();

        RankView Rank();

        /// <summary>Point the map at a quest step. The vanilla journal does this too, which is why the map has to
        /// accept being opened at a position rather than only from its own icon.</summary>
        string ShowStepOnMap(string questId, int stepIndex);

        /// <summary>Changes whenever a quest, a step or the rank changes.</summary>
        int Revision { get; }
    }

    internal interface IMessagesSource
    {
        List<ThreadView> Threads();

        /// <summary>Publish a few contact mugshots per call. Spread over ticks - see Faces.</summary>
        void WarmFaces();

        ThreadDetailView Thread(string npcId);

        string MarkRead(string npcId);

        string SetHidden(string npcId, bool hidden);

        /// <summary>Choose one of the offered replies. The index is checked against the revision the page was
        /// looking at, because a message arriving in between renumbers them.</summary>
        string ChooseReply(string npcId, int index, int seenRevision);

        string SendCanned(string npcId, int index, int seenRevision);

        /// <summary>
        /// Send the counter-offer the game currently has open, with these values. Quantity and price are clamped by
        /// the game itself, which is why they are passed rather than validated here.
        /// </summary>
        string SendCounterOffer(string productId, int quantity, int price);

        /// <summary>Place the dead-drop order the game currently has open. Ids are item ids, paired with amounts.</summary>
        string PlaceOrder(IReadOnlyList<KeyValuePair<string, int>> quantities);

        /// <summary>Pick a delivery window for the offer just accepted, by its position in the game's own four.</summary>
        string ChooseDealWindow(int index);

        /// <summary>Shut whichever sheet is open without acting on it - the sheet's own cross.</summary>
        string CloseSheet();

        /// <summary>
        /// Runs every tick. Keeps the game's own counter-offer and order windows from drawing over this app: the
        /// game opens them itself when a reply is chosen, and they live outside the app container this mod leaves
        /// switched off.
        /// </summary>
        void HideVanillaSheets();

        int Revision { get; }

        int Unread { get; }
    }

    internal interface IProductsSource
    {
        List<ProductView> Products();

        /// <summary>Publish a few product icons per call. Spread over ticks - see SpriteFeed.</summary>
        void WarmIcons();

        ProductDetailView Product(string productId);

        string SetListed(string productId, bool listed);

        string SetFavourite(string productId, bool favourite);

        string SetPrice(string productId, float price);

        int Revision { get; }
    }

    internal interface IDeliverySource
    {
        List<ShopView> Shops();

        /// <summary>Publish a few shopkeeper pictures per call. Spread over ticks - see SpriteFeed.</summary>
        void WarmIcons();

        List<DeliveryView> Active();

        List<DeliveryView> History();

        int Balance { get; }

        /// <summary>Quantities for one shop, as listingId/quantity pairs, then submit. The whole order at once
        /// because vanilla's SubmitOrder is one transaction - fee, money, delivery and receipt together.</summary>
        string Order(string shopId, IReadOnlyList<KeyValuePair<string, int>> quantities);

        /// <summary>
        /// Put the quantities into the game's own order panel WITHOUT submitting, so the fee, the total and the
        /// delivery time can be read back off it. That is how the page shows those numbers without owning the
        /// formulas behind them.
        /// </summary>
        string Fill(string shopId, IReadOnlyList<KeyValuePair<string, int>> quantities);

        /// <summary>Pick a destination or a loading dock, by its position in the game's own dropdown.</summary>
        string Choose(string shopId, bool dock, int index);

        int Revision { get; }

        int ActiveCount { get; }
    }

    internal interface IContactsSource
    {
        List<RegionView> Regions();

        /// <summary>
        /// One region's relationship graph, positions and all. This is what the vanilla app draws, so it is what
        /// this app draws.
        /// </summary>
        GraphView Graph(string regionId);

        /// <summary>
        /// Publish a few mugshots per call, for the region the page is looking at.
        ///
        /// Spread over ticks on purpose: forty-five texture readbacks in one frame is a visible stutter, and the
        /// faces arriving over the next second is not something anyone notices.
        /// </summary>
        void WarmFaces(string regionId);

        ContactDetailView Contact(string npcId);

        string ShowOnMap(string npcId);

        int Revision { get; }
    }

    internal interface IDealersSource
    {
        List<DealerView> Dealers();

        DealerDetailView Dealer(string dealerId);

        /// <summary>Customers that could be assigned to this dealer - already filtered to the ones the game would
        /// accept, so the page never offers a choice that is refused.</summary>
        List<ContactView> AssignableTo(string dealerId);

        string AddCustomer(string dealerId, string npcId);

        string RemoveCustomer(string dealerId, string npcId);

        string SetCut(string dealerId, int percent);

        int Revision { get; }
    }

    internal interface IMapSource
    {
        List<PoiView> Pois();

        List<RegionView> Regions();

        /// <summary>Where the player is, in the same 0..1 map space as a pin.</summary>
        PoiView Player();

        /// <summary>
        /// Makes the map image available to the page as <c>s1://map</c>, once. EXPENSIVE - a full texture readback
        /// and a PNG encode - so it is called from the pulse and never from inside a page's call.
        /// </summary>
        void EnsureImage();

        /// <summary>Publishes the mugshots the customer markers draw, a few per tick. Same reason as EnsureImage:
        /// a texture readback has no business inside a page's call.</summary>
        void WarmFaces();

        /// <summary>
        /// Whether the picture made it. False is a real possibility - the sprite may not read back - and the page
        /// then draws regions as plain boxes rather than showing nothing.
        /// </summary>
        bool ImageReady { get; }

        int Revision { get; }
    }
}
