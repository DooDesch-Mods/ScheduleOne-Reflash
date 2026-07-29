using Reflash.Wire;

namespace Reflash.Screens
{
    /// <summary>
    /// Map. The hardest of the seven to replace honestly, and the one where the result is a redesign rather than a
    /// reproduction.
    ///
    /// The renderer has no canvas, no background image, no drag and no wheel. What it does have is an
    /// absolutely-positioned image, elements placed in CSS pixels, a `transform` that no longer costs a rebuild,
    /// and - since 1.1.0 - the coordinates of a click inside an element.
    ///
    /// So: the map is an image with pins placed on top, pan is "click where you want the middle to be", and zoom is
    /// three steps. That is not vanilla's mouse-drag, and it is not pretending to be. The search and region list
    /// carry the weight instead, which for finding a specific place is better than dragging ever was.
    /// </summary>
    internal sealed class MapApp : IAppPort, INeedsAppHandle, IWarmUp
    {
        private readonly IMapSource _game;

        internal MapApp(IMapSource game) => _game = game;

        /// <summary>
        /// Handed on to the source, which is the half that extracts the picture and so the half that has to publish
        /// it. Core only ever sees ports, so the port is where the handle arrives.
        /// </summary>
        public void UseHandle(Sideload.Api.AppHandle handle)
        {
            if (_game is INeedsAppHandle needs) needs.UseHandle(handle);
        }

        public string Id => "reflash-map";

        public VanillaApp Replaces => VanillaApp.Map;

        public int Revision => _game.Revision;

        public int Badge => 0;

        /// <summary>
        /// Extract the map picture, once, on a tick. Doing it on first use put a texture readback inside the page's
        /// first script call and Jint killed the handler before it had drawn anything.
        /// </summary>
        public void WarmUp()
        {
            _game.EnsureImage();
            _game.WarmFaces();
        }

        public string State(string section)
        {
            bool hasImage = _game.ImageReady;

            var pins = Json.Array();
            foreach (PoiView p in _game.Pois())
                pins.Item(Json.Object()
                    .Add("id", p.Id)
                    .Add("label", p.Label)
                    .Add("kind", p.Kind)
                    .Add("region", p.Region)
                    .Add("x", p.X)
                    .Add("y", p.Y)
                    .Add("radius", p.Radius)
                    .Add("face", p.HasFace));

            var regions = Json.Array();
            foreach (RegionView r in _game.Regions())
                regions.Item(Json.Object()
                    .Add("id", r.Id)
                    .Add("name", r.Name)
                    .Add("unlocked", r.Unlocked));

            var root = Json.Object()
                .Add("rev", Revision)
                .Add("image", hasImage)
                .Add("pins", pins)
                .Add("regions", regions);

            PoiView player = _game.Player();
            if (player != null) root.Add("player", Json.Object().Add("x", player.X).Add("y", player.Y));
            else root.AddNull("player");

            // What the app was opened WITH, if anything. Quest's "show on map" and the contacts detail panel both
            // open this app expecting it to be looking at something specific, and the page collects that here
            // rather than through an event - at the moment they ask, this page does not exist yet to be told.
            root.Add("focus", Hijack.AppHijack.TakePendingArg(VanillaApp.Map));

            return root.Close();
        }

        /// <summary>Nothing to do. The map changes the world's view of itself, never the world.</summary>
        public string Act(Cmd cmd) => Reply.BadArgs;
    }
}
