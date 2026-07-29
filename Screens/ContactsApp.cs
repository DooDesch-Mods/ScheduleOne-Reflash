using Reflash.Wire;

namespace Reflash.Screens
{
    /// <summary>
    /// Contacts. A relationship graph in vanilla; a folded list here, for a measured reason.
    ///
    /// A real save carries 285 NPCs. At four boxes a row that is over a thousand boxes for one screen, against a
    /// budget of about two hundred - the whole list at once would freeze the phone for half a second every time
    /// anything changed. So it is grouped by region and only the open group renders, which also happens to be how
    /// you actually look for someone.
    ///
    /// The connections a contact has are shown on the contact, not as a picture of the whole web. That is the part
    /// vanilla does better; twelve names around one person is what fits here honestly.
    /// </summary>
    internal sealed class ContactsApp : IAppPort, INeedsAppHandle, IWarmUp
    {
        private readonly IContactsSource _game;

        /// <summary>The region the page last asked about, so the faces being warmed are the ones on screen.</summary>
        private string _showing = "";

        internal ContactsApp(IContactsSource game) => _game = game;

        /// <summary>Handed on to the source, which publishes the mugshots the graph draws.</summary>
        public void UseHandle(Sideload.Api.AppHandle handle)
        {
            if (_game is INeedsAppHandle needs) needs.UseHandle(handle);
        }

        /// <summary>
        /// A few mugshots per tick for the region on screen. Reading forty-five textures back at once is a visible
        /// stutter; letting the faces arrive over a second is not.
        /// </summary>
        public void WarmUp()
        {
            if (_showing.Length > 0) _game.WarmFaces(_showing);
        }

        public string Id => "reflash-contacts";

        public VanillaApp Replaces => VanillaApp.Contacts;

        public int Revision => _game.Revision;

        public int Badge => 0;

        public string State(string section)
        {
            if (section != null && section.StartsWith("region:", StringComparison.Ordinal))
                return RegionJson(section.Substring(7));

            if (section != null && section.StartsWith("contact:", StringComparison.Ordinal))
                return ContactJson(section.Substring(8));

            var regions = Json.Array();
            foreach (RegionView r in _game.Regions())
                regions.Item(Json.Object()
                    .Add("id", r.Id)
                    .Add("name", r.Name)
                    .Add("unlocked", r.Unlocked)
                    .Add("cartel", r.CartelInfluencePercent)
                    .Add("count", r.ContactCount));

            return Json.Object().Add("rev", Revision).Add("regions", regions).Close();
        }

        private string RegionJson(string regionId)
        {
            // Remembered so the pulse knows whose faces to fetch - the page asking for a region IS the signal that
            // it is looking at one.
            _showing = regionId ?? "";

            GraphView graph = _game.Graph(regionId);

            var nodes = Json.Array();
            foreach (NodeView n in graph.Nodes)
                nodes.Item(Json.Object()
                    .Add("id", n.Id)
                    .Add("name", n.Name)
                    .Add("kind", n.Kind)
                    .Add("x", n.X)
                    .Add("y", n.Y)
                    .Add("rel", n.Relationship)
                    .Add("relLabel", n.RelationshipLabel)
                    .Add("relColour", n.RelationshipColour)
                    .Add("addiction", n.Addiction)
                    .Add("unlocked", n.Unlocked)
                    .Add("hidden", n.Hidden)
                    .Add("supplier", n.Supplier)
                    .Add("face", n.HasFace));

            var edges = Json.Array();
            foreach (EdgeView e in graph.Edges)
                edges.Item(Json.Object()
                    .Add("x", e.X)
                    .Add("y", e.Y)
                    .Add("len", e.Length)
                    .Add("deg", e.AngleDeg));

            return Json.Object()
                .Add("rev", Revision)
                .Add("region", regionId)
                .Add("w", graph.Width)
                .Add("h", graph.Height)
                .Add("nodes", nodes)
                .Add("edges", edges)
                .Close();
        }

        private static Json Brief(ContactView c) => Json.Object()
            .Add("id", c.Id)
            .Add("name", c.Name)
            .Add("kind", c.Kind)
            .Add("region", c.Region)
            .Add("rel", c.Relationship)
            .Add("relLabel", c.RelationshipLabel)
            .Add("unlocked", c.Unlocked);

        private string ContactJson(string npcId)
        {
            ContactDetailView d = _game.Contact(npcId);
            if (d == null || d.Contact == null) return Json.Object().Add("rev", Revision).AddNull("contact").Close();

            var props = Json.Array();
            foreach (LabelView p in d.PreferredProperties)
                props.Item(Json.Object().Add("text", p.Text).Add("colour", p.Colour));

            var purchases = Json.Array();
            foreach (string p in d.TopPurchases) purchases.Item(p);

            var connections = Json.Array();
            foreach (ContactView c in d.Connections) connections.Item(Brief(c));

            return Json.Object()
                .Add("rev", Revision)
                .Add("contact", Brief(d.Contact))
                .Add("standards", d.Standards)
                .Add("standardsColour", d.StandardsColour)
                .Add("addiction", d.AddictionPercent)
                .Add("spent", d.SpentTotal)
                .Add("debt", d.Debt)
                .Add("properties", props)
                .Add("purchases", purchases)
                .Add("connections", connections)
                .Add("poi", d.HasPoi)
                .Close();
        }

        public string Act(Cmd cmd)
        {
            switch (cmd.Op)
            {
                case "map":
                {
                    string npcId = cmd.Str(0);
                    return string.IsNullOrEmpty(npcId) ? Reply.BadArgs : _game.ShowOnMap(npcId);
                }

                default:
                    return Reply.BadArgs;
            }
        }
    }
}
