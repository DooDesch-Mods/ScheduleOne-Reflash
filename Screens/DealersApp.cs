using Reflash.Wire;

namespace Reflash.Screens
{
    /// <summary>
    /// Dealers. Master/detail: pick a dealer, see their cash, cut, inventory and customers, and move customers on
    /// and off them.
    ///
    /// Only the SELECTED dealer's detail is ever built. Ten dealers with ten customers and a full inventory each
    /// would be several hundred boxes on one screen, and the render budget is about two hundred - so the
    /// virtualisation here is the selection itself rather than anything clever about scrolling.
    /// </summary>
    internal sealed class DealersApp : IAppPort
    {
        private readonly IDealersSource _game;

        internal DealersApp(IDealersSource game) => _game = game;

        public string Id => "reflash-dealers";

        public VanillaApp Replaces => VanillaApp.DealerManagement;

        public int Revision => _game.Revision;

        public int Badge => 0;

        public string State(string section)
        {
            if (section != null && section.StartsWith("dealer:", StringComparison.Ordinal))
                return DetailJson(section.Substring(7));

            if (section != null && section.StartsWith("assignable:", StringComparison.Ordinal))
                return AssignableJson(section.Substring(11));

            var list = Json.Array();
            foreach (DealerView d in _game.Dealers()) list.Item(DealerJson(d));

            return Json.Object().Add("rev", Revision).Add("dealers", list).Close();
        }

        private static Json DealerJson(DealerView d) => Json.Object()
            .Add("id", d.Id)
            .Add("name", d.Name)
            .Add("region", d.Region)
            .Add("home", d.Home)
            .Add("cash", d.Cash)
            .Add("cut", d.CutPercent)
            .Add("customers", d.CustomerCount)
            .Add("limit", d.CustomerLimit)
            .Add("avatar", d.Avatar);

        private string DetailJson(string dealerId)
        {
            DealerDetailView d = _game.Dealer(dealerId);
            if (d == null || d.Dealer == null) return Json.Object().Add("rev", Revision).AddNull("dealer").Close();

            var inventory = Json.Array();
            foreach (SlotView s in d.Inventory)
                inventory.Item(Json.Object().Add("name", s.Name).Add("qty", s.Quantity));

            var customers = Json.Array();
            foreach (ContactView c in d.Customers)
                customers.Item(Json.Object()
                    .Add("id", c.Id)
                    .Add("name", c.Name)
                    .Add("region", c.Region)
                    .Add("rel", c.Relationship)
                    .Add("relLabel", c.RelationshipLabel));

            return Json.Object()
                .Add("rev", Revision)
                .Add("dealer", DealerJson(d.Dealer))
                .Add("inventory", inventory)
                .Add("customers", customers)
                .Close();
        }

        private string AssignableJson(string dealerId)
        {
            var list = Json.Array();
            foreach (ContactView c in _game.AssignableTo(dealerId))
                list.Item(Json.Object()
                    .Add("id", c.Id)
                    .Add("name", c.Name)
                    .Add("region", c.Region));

            return Json.Object().Add("rev", Revision).Add("candidates", list).Close();
        }

        public string Act(Cmd cmd)
        {
            string dealerId = cmd.Str(0);
            if (string.IsNullOrEmpty(dealerId)) return Reply.BadArgs;

            switch (cmd.Op)
            {
                case "add":
                {
                    string npcId = cmd.Str(1);
                    return string.IsNullOrEmpty(npcId) ? Reply.BadArgs : _game.AddCustomer(dealerId, npcId);
                }

                case "remove":
                {
                    string npcId = cmd.Str(1);
                    return string.IsNullOrEmpty(npcId) ? Reply.BadArgs : _game.RemoveCustomer(dealerId, npcId);
                }

                case "cut":
                    return cmd.Int(1, out int percent) ? _game.SetCut(dealerId, percent) : Reply.BadArgs;

                default:
                    return Reply.BadArgs;
            }
        }
    }
}
