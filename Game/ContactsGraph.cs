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

        /// <summary>
        /// Bumped when faces the page has not drawn yet have arrived in the region it shows, and have stopped
        /// arriving. Part of the app's revision.
        ///
        /// Counted against what the page last READ, not against everything published: every face is warmed in the
        /// background once the app has been opened, and a revision that moved for a face in another region would
        /// rebuild the page on screen for nothing. And counted only once the arrivals pause, because a rebuild per
        /// face is a rebuild per tick - see SpriteFeed.
        /// </summary>
        internal int PublishedFaces { get; private set; }

        /// <summary>The people in the region the page last read, and how many of them had a face then.</summary>
        private readonly List<string> _readIds = new List<string>();
        private int _facesRead;

        /// <summary>The same count on the previous tick, to tell "still arriving" from "settled".</summary>
        private int _facesSeen;

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

            _readIds.Clear();
            int faces = 0;
            foreach (NodeView node in view.Nodes)
            {
                _readIds.Add(node.Id);
                if (node.HasFace) faces++;
            }
            _facesRead = faces;
            _facesSeen = faces;

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
                Name = Text.Clean(npc.FullName),
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
        /// Hand people to the face publisher, which takes a few per tick: the region on screen first, then everyone
        /// else, so a region opened later already has its faces and is built once instead of twice.
        /// </summary>
        internal void WarmFaces(string regionId)
        {
            if (!Enum.TryParse(regionId, out EMapRegion region)) return;

            var circles = Circles();
            if (circles == null) return;

            _faces.Warm(RegionFirst(People(circles), region));

            int ready = 0;
            for (int i = 0; i < _readIds.Count; i++)
                if (_faces.Has(_readIds[i])) ready++;

            if (ready != _facesSeen) { _facesSeen = ready; return; }
            if (ready == _facesRead) return;

            _facesRead = ready;
            PublishedFaces++;
        }

        private IEnumerable<KeyValuePair<string, Sprite>> RegionFirst(List<(string Id, NPC Npc, EMapRegion Region)> people,
                                                                     EMapRegion region)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < people.Count; i++)
                {
                    var person = people[i];
                    if ((person.Region == region) != (pass == 0)) continue;

                    // Skipped before the sprite is read: once everyone is done, a tick costs a walk over strings.
                    if (_faces.Tried(person.Id) || person.Npc == null) continue;

                    yield return new KeyValuePair<string, Sprite>(person.Id, person.Npc.MugshotSprite);
                }
            }
        }

        /// <summary>
        /// Who sits in which circle, read off the circles once per scan. Reading the id and the region of 285 NPCs
        /// through the interop layer four times a second is a string per NPC per tick for the collector to sweep up.
        /// </summary>
        private List<(string Id, NPC Npc, EMapRegion Region)> People(Il2CppArrayBase<RelationCircle> circles)
        {
            if (ReferenceEquals(circles, _peopleFrom)) return _people;

            _people.Clear();
            for (int i = 0; i < circles.Length; i++)
            {
                RelationCircle circle = circles[i];
                NPC npc = circle != null ? circle.AssignedNPC : null;
                if (npc == null || string.IsNullOrEmpty(npc.ID)) continue;

                _people.Add((npc.ID, npc, npc.Region));
            }

            _peopleFrom = circles;
            return _people;
        }

        private readonly List<(string Id, NPC Npc, EMapRegion Region)> _people = new List<(string, NPC, EMapRegion)>();
        private Il2CppArrayBase<RelationCircle> _peopleFrom;

        private Il2CppArrayBase<RelationCircle> _circles;
        private bool _searchedScene;

        /// <summary>
        /// Every relation circle, including the inactive ones - a region that is not currently selected has its
        /// container switched off, and its circles are still where they belong.
        ///
        /// Taken from under vanilla's own <c>ContactsApp.CirclesContainer</c>, which is where its Start collects them
        /// too. A scene-wide <c>FindObjectsOfType</c> found the same circles and cost about 150 ms, inside the click
        /// that switches a region.
        ///
        /// Cached, because the pulse asks four times a second. A destroyed first entry means the scene changed under
        /// us, and the search runs again. Anything that must not miss a late arrival asks for a fresh scan.
        /// </summary>
        private Il2CppArrayBase<RelationCircle> Circles(bool fresh = false)
        {
            if (!fresh && _circles != null && _circles.Length > 0 && _circles[0] != null) return _circles;

            try
            {
#if SNITCH
                using (Snitch.Api.Profiler.Sample("Reflash.contacts.Scan"))
#endif
                {
                    var app = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.UI.Phone.ContactsApp.ContactsApp>.Instance;
                    RectTransform container = app != null ? app.CirclesContainer : null;

                    if (container != null)
                    {
                        _circles = container.GetComponentsInChildren<RelationCircle>(true);
                    }
                    else
                    {
                        if (!_searchedScene)
                            Core.Log.Warning("Contacts: no vanilla contacts app to read the circles from yet - searching the scene.");
                        _searchedScene = true;
                        _circles = UnityEngine.Object.FindObjectsOfType<RelationCircle>(true);
                    }
                }
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Contacts: searching the relation circles failed ({e.Message}) - no graph this time.");
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
