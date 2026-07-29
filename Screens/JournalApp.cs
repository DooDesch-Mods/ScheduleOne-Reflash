using Reflash.Wire;

namespace Reflash.Screens
{
    /// <summary>
    /// Tasks. The smallest of the seven and the only one that writes nothing, which is why it was built first: it
    /// exercises the whole takeover chain - icon, number key, the J shortcut, the back key, the pulse - with no
    /// mutation to get wrong.
    ///
    /// The one thing it does beyond reading is hand the map a position, because the vanilla journal does too.
    /// </summary>
    internal sealed class JournalApp : IAppPort
    {
        private readonly IJournalSource _game;

        internal JournalApp(IJournalSource game) => _game = game;

        public string Id => "reflash-journal";

        public VanillaApp Replaces => VanillaApp.Journal;

        public int Revision => _game.Revision;

        /// <summary>No badge. Vanilla's journal has none either, and a count of open tasks would nag rather than
        /// inform - the number is rarely zero and never urgent.</summary>
        public int Badge => 0;

        public string State(string section)
        {
            List<QuestView> quests = _game.ActiveQuests();
            RankView rank = _game.Rank();

            var root = Json.Object();
            root.Add("rev", Revision);

            var list = Json.Array();
            foreach (QuestView q in quests)
            {
                var steps = Json.Array();
                foreach (QuestStepView s in q.Steps)
                    steps.Item(Json.Object()
                        .Add("title", s.Title)
                        .Add("state", s.State)
                        .Add("poi", s.HasPoi));

                list.Item(Json.Object()
                    .Add("id", q.Id)
                    .Add("title", q.Title)
                    .Add("subtitle", q.Subtitle)
                    .Add("description", q.Description)
                    .Add("tracked", q.Tracked)
                    .Add("expires", q.ExpiresIn)
                    .Add("critical", q.Critical)
                    .Add("steps", steps));
            }
            root.Add("quests", list);

            root.Add("rank", Json.Object()
                .Add("name", rank.Name)
                .Add("tier", rank.Tier)
                .Add("xp", rank.Xp)
                .Add("next", rank.XpForNext));

            return root.Close();
        }

        public string Act(Cmd cmd)
        {
            switch (cmd.Op)
            {
                case "map":
                    // Show a step on the map. Two fields, both required - a missing index is a refusal rather than
                    // step zero, because pointing the map at the wrong place looks like a bug in the map.
                    if (!cmd.Int(1, out int step)) return Reply.BadArgs;
                    string questId = cmd.Str(0);
                    return string.IsNullOrEmpty(questId) ? Reply.BadArgs : _game.ShowStepOnMap(questId, step);

                default:
                    return Reply.BadArgs;
            }
        }
    }
}
