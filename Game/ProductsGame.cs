using Reflash.Wire;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Product;
using UnityEngine;

namespace Reflash.Game
{
    /// <summary>
    /// The cleanest of the seven to adapt: <c>ProductManager</c> keeps its lists as plain statics with no UI in
    /// them, and every mutation is a ServerRpc that takes an id.
    ///
    /// All three writes are <c>RequireOwnership = false</c>, so a client may call them and the server rebroadcasts
    /// - the same thing the vanilla screen does. Nothing here checks for a host.
    /// </summary>
    internal sealed class ProductsGame : IProductsSource, INeedsAppHandle
    {
        private readonly SpriteFeed _icons = new SpriteFeed("icon-");

        public void UseHandle(Sideload.Api.AppHandle handle) => _icons.UseHandle(handle);

        /// <summary>The discovered products' own icons, a few per tick - the same picture the vanilla grid shows.</summary>
        public void WarmIcons()
        {
            Il2CppSystem.Collections.Generic.List<ProductDefinition> discovered = ProductManager.DiscoveredProducts;
            if (discovered == null) return;

            _icons.Warm(IconsOf(discovered));
        }

        private static IEnumerable<KeyValuePair<string, Sprite>> IconsOf(
            Il2CppSystem.Collections.Generic.List<ProductDefinition> products)
        {
            for (int i = 0; i < products.Count; i++)
            {
                ProductDefinition p = products[i];
                if (p == null) continue;

                yield return new KeyValuePair<string, Sprite>(p.ID, p.Icon);
            }
        }

        public List<ProductView> Products()
        {
            var views = new List<ProductView>();

            Il2CppSystem.Collections.Generic.List<ProductDefinition> discovered = ProductManager.DiscoveredProducts;
            if (discovered == null) return views;

            for (int i = 0; i < discovered.Count; i++)
            {
                ProductDefinition p = discovered[i];
                if (p == null) continue;

                views.Add(new ProductView
                {
                    Id = p.ID,
                    Name = Text.Clean(p.Name),
                    Listed = IsListed(p),
                    Favourite = IsFavourite(p),
                    Price = (int)Math.Round(PriceOf(p)),
                    MarketValue = (int)Math.Round(p.MarketValue),
                    Quality = Text.Clean(p.DrugType.ToString()),
                    HasIcon = _icons.Has(p.ID),
                });
            }

            return views;
        }

        public ProductDetailView Product(string productId)
        {
            ProductDefinition p = Find(productId);
            if (p == null) return null;

            var view = new ProductDetailView
            {
                Id = p.ID,
                Name = Text.Clean(p.Name),
                Description = Text.Clean(p.Description),
                Listed = IsListed(p),
                Favourite = IsFavourite(p),
                Price = (int)Math.Round(PriceOf(p)),
                MarketValue = (int)Math.Round(p.MarketValue),
                Addictiveness = (int)Math.Round(p.GetAddictiveness() * 100f),
            };

            // `var`, because the element type is declared on a base definition in another assembly and naming it
            // here would be a second place to keep right for no benefit.
            var props = p.Properties;
            if (props != null)
                for (int i = 0; i < props.Count; i++)
                    if (props[i] != null)
                        view.Properties.Add(new LabelView
                        {
                            Text = Text.Clean(props[i].Name),
                            Colour = Colours.Hex(props[i].LabelColor),
                        });

            // How this product is made. Vanilla draws each recipe as three icons - base, mixer, result - and the
            // pair is not in a fixed order: whichever ingredient IS a product is the base and the other is the
            // mixer, which is exactly the test ProductAppDetailPanel makes.
            var recipes = p.Recipes;
            if (recipes != null)
                for (int i = 0; i < recipes.Count; i++)
                {
                    var recipe = recipes[i];
                    if (recipe?.Ingredients == null || recipe.Ingredients.Count < 2) continue;

                    var first = recipe.Ingredients[0]?.Item;
                    var second = recipe.Ingredients[1]?.Item;
                    if (first == null || second == null) continue;

                    bool firstIsProduct = first.TryCast<ProductDefinition>() != null;
                    var baseItem = firstIsProduct ? first : second;
                    var mixer = firstIsProduct ? second : first;

                    view.Recipe.Add(Text.Clean(baseItem.Name) + "  +  " + Text.Clean(mixer.Name));
                }

            return view;
        }

        public string SetListed(string productId, bool listed)
        {
            ProductDefinition p = Find(productId);
            if (p == null) return Reply.NotFound;
            if (!NetworkSingleton<ProductManager>.InstanceExists) return Reply.NoGame;

            NetworkSingleton<ProductManager>.Instance.SetProductListed(p.ID, listed);
            return Reply.Ok;
        }

        public string SetFavourite(string productId, bool favourite)
        {
            ProductDefinition p = Find(productId);
            if (p == null) return Reply.NotFound;
            if (!NetworkSingleton<ProductManager>.InstanceExists) return Reply.NoGame;

            NetworkSingleton<ProductManager>.Instance.SetProductFavourited(p.ID, favourite);
            return Reply.Ok;
        }

        public string SetPrice(string productId, float price)
        {
            ProductDefinition p = Find(productId);
            if (p == null) return Reply.NotFound;
            if (!NetworkSingleton<ProductManager>.InstanceExists) return Reply.NoGame;

            // The game clamps to its own range on the far side, so this passes the number through rather than
            // guessing at limits that would then disagree with the shop.
            NetworkSingleton<ProductManager>.Instance.SendPrice(p.ID, price);
            return Reply.Ok;
        }

        public int Revision
        {
            get
            {
                unchecked
                {
                    int hash = 17;

                    // Bumped once when the icons have all arrived, not once per picture - see SpriteFeed.
                    hash = hash * 31 + _icons.Settled;

                    hash = hash * 31 + (ProductManager.DiscoveredProducts?.Count ?? 0);
                    hash = hash * 31 + (ProductManager.ListedProducts?.Count ?? 0);
                    hash = hash * 31 + (ProductManager.FavouritedProducts?.Count ?? 0);

                    // Prices are the other thing that changes without the lists moving, and a co-op partner can
                    // change one at any time.
                    Il2CppSystem.Collections.Generic.List<ProductDefinition> discovered = ProductManager.DiscoveredProducts;
                    if (discovered != null && NetworkSingleton<ProductManager>.InstanceExists)
                    {
                        ProductManager mgr = NetworkSingleton<ProductManager>.Instance;
                        for (int i = 0; i < discovered.Count; i++)
                            if (discovered[i] != null) hash = hash * 31 + (int)mgr.GetPrice(discovered[i]);
                    }

                    return hash;
                }
            }
        }

        private static float PriceOf(ProductDefinition p) =>
            NetworkSingleton<ProductManager>.InstanceExists
                ? NetworkSingleton<ProductManager>.Instance.GetPrice(p)
                : p.MarketValue;

        private static bool IsListed(ProductDefinition p) => Contains(ProductManager.ListedProducts, p);

        private static bool IsFavourite(ProductDefinition p) => Contains(ProductManager.FavouritedProducts, p);

        private static bool Contains(Il2CppSystem.Collections.Generic.List<ProductDefinition> list, ProductDefinition p)
        {
            if (list == null || p == null) return false;

            // By id rather than by reference: the lists are rebuilt from a save, and two definitions for the same
            // product are not guaranteed to be the same object.
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].ID == p.ID) return true;

            return false;
        }

        private static ProductDefinition Find(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;

            Il2CppSystem.Collections.Generic.List<ProductDefinition> discovered = ProductManager.DiscoveredProducts;
            if (discovered == null) return null;

            for (int i = 0; i < discovered.Count; i++)
                if (discovered[i] != null && discovered[i].ID == productId) return discovered[i];

            return null;
        }
    }
}
