using Reflash.Wire;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Relations;
using Sideload.Api;
using UnityEngine;

namespace Reflash.Game
{
    /// <summary>
    /// The relationship graph, read off vanilla's own circles.
    ///
    /// Vanilla's layout is AUTHORED - every circle sits at a hand-placed anchoredPosition in the prefab, and the
    /// lines are instantiated between them at start-up. A spring layout computed here would be a different picture
    /// of the same data, which is exactly what a replacement must not be: people learn where a face sits.
    ///
    /// So the positions come from the live <c>RelationCircle</c> components. That is the same choice the map image
    /// and the home screen icons make, and for the same reason - the game owns these values, and a table copied out
    /// of a decompile goes stale the first time the game moves someone.
    ///
    /// Everything is converted to css pixels here rather than in the page: the factor is the renderer's, and a page
    /// that knew it would be a page that breaks when the canvas changes.
    /// </summary>
    internal sealed class ContactsGraph
    {
        /// <summary>Canvas units per css pixel. The renderer's scale, and the only place this file states it.</summary>
        private const double CanvasToCss = 1.6375;

        /// <summary>Circle 100x100 canvas units, so the page can place a node by its centre.</summary>
        private const double NodeSize = 100.0 / CanvasToCss;

        private readonly SpriteFeed _faces = new SpriteFeed("face-");

        internal void UseHandle(AppHandle handle) => _faces.UseHandle(handle);

        /// <summary>Bumped once when a run of mugshots finishes. Part of the app's revision - see SpriteFeed.</summary>
        internal int PublishedFaces => _faces.Settled;

        internal GraphView Graph(string regionId)
        {
            var view = new GraphView();
            if (!Enum.TryParse(regionId, out EMapRegion region)) return view;

            // Re-scanned rather than reused: the phone builds its relation circles lazily, so a scan taken on an
            // early tick finds a fraction of them. This runs when a region is opened, which is rare enough to pay
            // for a fresh look every time.
            var circles = Circles(fresh: true);
            if (circles == null) return view;

            // Position by npc id, so an edge can be placed without searching the scene again per connection.
            var placed = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            for (int i = 0; i < circles.Length; i++)
            {
                RelationCircle circle = circles[i];
                if (circle == null || circle.AssignedNPC == null || circle.Rect == null) continue;

                NPC npc = circle.AssignedNPC;
                if (npc.Region != region) continue;

                Vector2 at = circle.Rect.anchoredPosition;
                placed[npc.ID] = at;

                double x = at.x / CanvasToCss;

                // Canvas y grows upwards, css y downwards.
                double y = -at.y / CanvasToCss;

                view.Nodes.Add(NodeFor(npc, x, y));

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            if (view.Nodes.Count == 0) return view;

            AddEdges(view, circles, region, placed);

            // Half a node of air on each side, so a circle at the edge is not clipped in half.
            view.MinX = minX - NodeSize;
            view.MinY = minY - NodeSize;
            view.Width = (maxX - minX) + NodeSize * 2;
            view.Height = (maxY - minY) + NodeSize * 2;

            // The page places everything relative to the world's own origin, so shift now rather than asking it to.
            foreach (NodeView node in view.Nodes) { node.X -= view.MinX; node.Y -= view.MinY; }
            foreach (EdgeView edge in view.Edges) { edge.X -= view.MinX; edge.Y -= view.MinY; }

            return view;
        }

        /// <summary>
        /// One line per connected pair, positioned and rotated exactly as vanilla instantiates them: centred
        /// between the two circles, as long as the distance between them, rotated to point from one to the other.
        /// </summary>
        private static void AddEdges(GraphView view, Il2CppArrayBase<RelationCircle> circles, EMapRegion region,
                                     Dictionary<string, Vector2> placed)
        {
            var drawn = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < circles.Length; i++)
            {
                RelationCircle circle = circles[i];
                if (circle == null || circle.AssignedNPC == null) continue;

                NPC npc = circle.AssignedNPC;
                if (npc.Region != region || npc.RelationData == null) continue;

                var connections = npc.RelationData.Connections;
                if (connections == null) continue;

                for (int c = 0; c < connections.Count; c++)
                {
                    NPC other = connections[c];
                    if (other == null || other.Region != region) continue;

                    if (!placed.TryGetValue(npc.ID, out Vector2 a)) continue;
                    if (!placed.TryGetValue(other.ID, out Vector2 b)) continue;

                    // A connection is listed on both ends; drawing it twice would double every line's opacity.
                    string key = string.CompareOrdinal(npc.ID, other.ID) < 0
                        ? npc.ID + "|" + other.ID
                        : other.ID + "|" + npc.ID;

                    if (!drawn.Add(key)) continue;

                    Vector2 delta = b - a;

                    view.Edges.Add(new EdgeView
                    {
                        X = (a.x + b.x) / 2.0 / CanvasToCss,
                        Y = -(a.y + b.y) / 2.0 / CanvasToCss,
                        Length = delta.magnitude / CanvasToCss,

                        // Vanilla's own formula, and it is measured from vertical rather than from horizontal - the
                        // line prefab is a tall thin bar, not a wide one.
                        AngleDeg = Math.Atan2(delta.x, delta.y) * (180.0 / Math.PI),
                    });
                }
            }
        }

        private NodeView NodeFor(NPC npc, double x, double y)
        {
            var node = new NodeView
            {
                Id = npc.ID,
                Name = Text.Clean(npc.fullName),
                X = x,
                Y = y,
                Unlocked = npc.RelationData != null && npc.RelationData.Unlocked,
                Relationship = npc.RelationData != null ? npc.RelationData.RelationDelta : 0,
                HasFace = _faces.Has(npc.ID),
            };

            var customer = npc.GetComponent<Customer>();
            if (customer != null) node.Addiction = customer.CurrentAddiction;

            node.Supplier = npc.GetComponent<Supplier>() != null;
            node.Dealer = npc.GetComponent<Il2CppScheduleOne.Economy.Dealer>() != null;
            node.Kind = node.Dealer ? "dealer" : node.Supplier ? "supplier" : customer != null ? "customer" : "";

            // Vanilla blacks the headshot out for someone whose face you have no business knowing yet - an
            // unrecommended dealer, any locked supplier, a customer you have not been introduced to.
            node.Hidden = !node.Unlocked && Blacked(npc, node);

            node.RelationshipLabel = CategoryOf(node.Relationship);
            node.RelationshipColour = CategoryColour(node.RelationshipLabel);

            return node;
        }

        private static bool Blacked(NPC npc, NodeView node)
        {
            try
            {
                if (node.Dealer)
                {
                    var dealer = npc.GetComponent<Il2CppScheduleOne.Economy.Dealer>();
                    return dealer != null && !dealer.HasBeenRecommended;
                }

                if (node.Supplier) return true;

                return npc.GetComponent<Customer>() != null && !npc.RelationData.IsMutuallyKnown();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Hand the region's people to the face publisher, which takes a few per tick.
        /// </summary>
        internal void WarmFaces(string regionId)
        {
            if (!Enum.TryParse(regionId, out EMapRegion region)) return;

            var circles = Circles();
            if (circles == null) return;

            _faces.Warm(InRegion(circles, region));
        }

        private static IEnumerable<KeyValuePair<string, Sprite>> InRegion(Il2CppArrayBase<RelationCircle> circles,
                                                                         EMapRegion region)
        {
            for (int i = 0; i < circles.Length; i++)
            {
                RelationCircle circle = circles[i];
                if (circle == null || circle.AssignedNPC == null) continue;
                if (circle.AssignedNPC.Region != region) continue;

                yield return new KeyValuePair<string, Sprite>(circle.AssignedNPC.ID, circle.AssignedNPC.MugshotSprite);
            }
        }

        private Il2CppArrayBase<RelationCircle> _circles;

        /// <summary>
        /// Every relation circle in the scene, including the inactive ones - a region that is not currently
        /// selected has its container switched off, and its circles are still where they belong.
        ///
        /// Cached, because the pulse asks four times a second and a scene-wide search for a component is not a
        /// four-times-a-second cost. A destroyed first entry means the scene changed under us, and the search runs
        /// again. Anything that must not miss a late arrival asks for a fresh scan.
        /// </summary>
        private Il2CppArrayBase<RelationCircle> Circles(bool fresh = false)
        {
            if (!fresh && _circles != null && _circles.Length > 0 && _circles[0] != null) return _circles;

            try
            {
                _circles = UnityEngine.Object.FindObjectsOfType<RelationCircle>(true);
            }
            catch
            {
                _circles = null;
            }

            return _circles;
        }

        /// <summary>
        /// The bands and colours are vanilla's, from RelationshipCategory - the same five names and the same five
        /// colours the contacts panel prints. Asked of the game rather than copied would be better still, but the
        /// enum's colours are static fields on a UI class and this is the one place they are needed.
        /// </summary>
        internal static string CategoryOf(double delta)
        {
            if (delta >= 4f) return "Loyal";
            if (delta >= 3f) return "Friendly";
            if (delta >= 2f) return "Neutral";
            if (delta >= 1f) return "Unfriendly";
            return "Hostile";
        }

        internal static string CategoryColour(string category) => category switch
        {
            "Loyal" => "#3fd33f",
            "Friendly" => "#3db5f3",
            "Neutral" => "#d0d0d0",
            "Unfriendly" => "#e38837",
            _ => "#ad3f3f",
        };
    }
}
