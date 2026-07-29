using Reflash.Wire;

namespace Reflash.Screens
{
    /// <summary>
    /// Products. A list of what has been discovered, and per product: list it for sale, favourite it, set its price.
    ///
    /// The price field is the interesting part. Sideload dispatches no `blur` and no `change`, so there is no
    /// "left the field, take the value" moment - a page either commits on every keystroke or offers something
    /// explicit. This one takes Enter, which IS delivered, plus step buttons, because typing on a phone in a game
    /// is worse than pressing minus twice.
    /// </summary>
    internal sealed class ProductsApp : IAppPort, INeedsAppHandle, IWarmUp
    {
        /// <summary>Handed on to the source, which publishes the product icons the grid shows.</summary>
        public void UseHandle(Sideload.Api.AppHandle handle)
        {
            if (_game is INeedsAppHandle needs) needs.UseHandle(handle);
        }

        /// <summary>A few icons per tick. Reading them all back at once is a stutter; this is not.</summary>
        public void WarmUp() => _game.WarmIcons();

        private readonly IProductsSource _game;

        internal ProductsApp(IProductsSource game) => _game = game;

        public string Id => "reflash-products";

        public VanillaApp Replaces => VanillaApp.ProductManager;

        public int Revision => _game.Revision;

        public int Badge => 0;

        public string State(string section)
        {
            if (section != null && section.StartsWith("item:", StringComparison.Ordinal))
                return DetailJson(section.Substring(5));

            var list = Json.Array();
            foreach (ProductView p in _game.Products())
                list.Item(Json.Object()
                    .Add("id", p.Id)
                    .Add("name", p.Name)
                    .Add("listed", p.Listed)
                    .Add("fav", p.Favourite)
                    .Add("price", p.Price)
                    .Add("value", p.MarketValue)
                    .Add("quality", p.Quality)
                    .Add("icon", p.HasIcon));

            return Json.Object().Add("rev", Revision).Add("products", list).Close();
        }

        private string DetailJson(string productId)
        {
            ProductDetailView d = _game.Product(productId);
            if (d == null) return Json.Object().Add("rev", Revision).AddNull("product").Close();

            var props = Json.Array();
            foreach (LabelView p in d.Properties)
                props.Item(Json.Object().Add("text", p.Text).Add("colour", p.Colour));

            var recipe = Json.Array();
            foreach (string r in d.Recipe) recipe.Item(r);

            return Json.Object()
                .Add("rev", Revision)
                .Add("product", Json.Object()
                    .Add("id", d.Id)
                    .Add("name", d.Name)
                    .Add("description", d.Description)
                    .Add("listed", d.Listed)
                    .Add("fav", d.Favourite)
                    .Add("price", d.Price)
                    .Add("value", d.MarketValue)
                    .Add("addictiveness", d.Addictiveness)
                    .Add("properties", props)
                    .Add("recipe", recipe))
                .Close();
        }

        public string Act(Cmd cmd)
        {
            string id = cmd.Str(0);
            if (string.IsNullOrEmpty(id)) return Reply.BadArgs;

            switch (cmd.Op)
            {
                case "list":
                    return cmd.Flag(1, out bool listed) ? _game.SetListed(id, listed) : Reply.BadArgs;

                case "fav":
                    return cmd.Flag(1, out bool fav) ? _game.SetFavourite(id, fav) : Reply.BadArgs;

                case "price":
                    // The game clamps the value itself, so this only refuses what is not a number at all - a page
                    // that sends nonsense should hear so rather than silently set zero.
                    return cmd.Num(1, out float price) ? _game.SetPrice(id, price) : Reply.BadArgs;

                default:
                    return Reply.BadArgs;
            }
        }
    }
}
