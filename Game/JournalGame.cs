using Reflash.Hijack;
using Reflash.Wire;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Quests;

namespace Reflash.Game
{
    /// <summary>
    /// Reads the quest log the way the vanilla journal does.
    ///
    /// The list to read is <c>Quest.Quests</c>, not <c>Quest.ActiveQuests</c>. The second one is a leftover: nothing
    /// in the game ever adds to it, only removes and clears - so the journal came up empty on a save with a running
    /// quest. What decides whether a quest is listed is <c>Quest.ShouldShowJournalEntry</c>, and that is the rule
    /// mirrored in <see cref="ShowsInJournal"/>.
    ///
    /// Nothing here writes. The one action the journal offers - show a step on the map - is a UI navigation, not a
    /// game change, and it goes through the same cross-open the vanilla journal uses.
    /// </summary>
    internal sealed class JournalGame : IJournalSource
    {
        public List<QuestView> ActiveQuests()
        {
            var views = new List<QuestView>();

            Il2CppSystem.Collections.Generic.List<Quest> active = Quest.Quests;
            if (active == null) return views;

            for (int i = 0; i < active.Count; i++)
            {
                Quest q = active[i];
                if (q == null || !ShowsInJournal(q)) continue;

                var view = new QuestView
                {
                    Id = QuestId(q, i),
                    Title = Text.Clean(q.Title),
                    Subtitle = Text.Clean(q.Subtitle),
                    Description = Text.Clean(q.Description),
                    Tracked = q.IsTracked,
                };

                if (q.Expires)
                {
                    view.ExpiresIn = Text.Clean(q.GetExpiryText());

                    // The same threshold vanilla colours red - two hours, expressed in minutes as the game counts.
                    try { view.Critical = q.GetMinsUntilExpiry() <= 120; } catch { view.Critical = false; }
                }

                Il2CppSystem.Collections.Generic.List<QuestEntry> entries = q.Entries;
                if (entries != null)
                {
                    for (int e = 0; e < entries.Count; e++)
                    {
                        QuestEntry entry = entries[e];
                        if (entry == null || entry.State == EQuestState.Inactive) continue;

                        view.Steps.Add(new QuestStepView
                        {
                            Title = Text.Clean(entry.Title),
                            State = StateName(entry.State),
                            HasPoi = entry.PoI != null,
                        });
                    }
                }

                views.Add(view);
            }

            return views;
        }

        public RankView Rank()
        {
            var view = new RankView();
            if (!NetworkSingleton<LevelManager>.InstanceExists) return view;

            LevelManager level = NetworkSingleton<LevelManager>.Instance;
            view.Name = Text.Clean(level.Rank.ToString());
            view.Tier = level.Tier;
            view.Xp = level.XP;
            view.XpForNext = (int)level.XPToNextTier;
            return view;
        }

        public string ShowStepOnMap(string questId, int stepIndex)
        {
            Quest quest = FindQuest(questId);
            if (quest == null) return Reply.NotFound;

            Il2CppSystem.Collections.Generic.List<QuestEntry> entries = quest.Entries;
            if (entries == null) return Reply.NotFound;

            // The index counts the steps the page was shown, which skips inactive ones - so it is resolved the same
            // way rather than used against the raw list.
            int shown = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                QuestEntry entry = entries[i];
                if (entry == null || entry.State == EQuestState.Inactive) continue;

                if (++shown != stepIndex) continue;
                if (entry.PoI == null) return Reply.Refused;

                // Handed over as a position rather than a POI id: the map then has nothing to resolve, and the same
                // argument works for the contacts screen, where there is no POI to name at all.
                string focus = MapSpace.FocusArg(entry.PoI.transform.position);
                if (focus.Length == 0) return Reply.Refused;

                return AppHijack.Open(VanillaApp.Map, focus) ? Reply.Ok : Reply.Refused;
            }

            return Reply.NotFound;
        }

        /// <summary>
        /// Changes when a quest is added or removed, when any step changes state, or when the rank moves. Cheap on
        /// purpose - the pulse reads it four times a second for every app.
        /// </summary>
        public int Revision
        {
            get
            {
                unchecked
                {
                    int hash = 17;

                    Il2CppSystem.Collections.Generic.List<Quest> active = Quest.Quests;
                    hash = hash * 31 + (active?.Count ?? 0);

                    if (active != null)
                    {
                        for (int i = 0; i < active.Count; i++)
                        {
                            Quest q = active[i];
                            if (q == null) continue;

                            hash = hash * 31 + (int)q.State;
                            hash = hash * 31 + (q.IsTracked ? 1 : 0);

                            Il2CppSystem.Collections.Generic.List<QuestEntry> entries = q.Entries;
                            if (entries == null) continue;

                            for (int e = 0; e < entries.Count; e++)
                                if (entries[e] != null) hash = hash * 31 + (int)entries[e].State;
                        }
                    }

                    if (NetworkSingleton<LevelManager>.InstanceExists)
                    {
                        LevelManager level = NetworkSingleton<LevelManager>.Instance;
                        hash = hash * 31 + level.TotalXP;
                    }

                    return hash;
                }
            }
        }

        /// <summary>
        /// Whether the journal lists this quest, which is <c>Quest.ShouldShowJournalEntry</c> restated.
        ///
        /// It cannot simply be called: it is protected, and the override that matters is on <c>Contract</c> - a deal
        /// a dealer is handling has no journal entry, because the player is not the one doing it.
        /// </summary>
        private static bool ShowsInJournal(Quest quest)
        {
            if (quest.State != EQuestState.Active) return false;

            var contract = quest.TryCast<Contract>();
            return contract == null || contract.Dealer == null;
        }

        /// <summary>
        /// A stable id for a quest. The GUID is the real one, but a quest whose GUID has not been assigned yet would
        /// collide with every other such quest, so the list position is the fallback - stable enough for the moment
        /// between a page rendering and the player pressing something on it.
        /// </summary>
        private static string QuestId(Quest quest, int index)
        {
            try
            {
                string guid = quest.GUID.ToString();
                if (!string.IsNullOrEmpty(guid) && guid != System.Guid.Empty.ToString()) return guid;
            }
            catch { /* not registered yet */ }

            return "#" + index;
        }

        private static Quest FindQuest(string id)
        {
            Il2CppSystem.Collections.Generic.List<Quest> active = Quest.Quests;
            if (active == null || string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && QuestId(active[i], i) == id) return active[i];

            return null;
        }

        private static string StateName(EQuestState state) => state switch
        {
            EQuestState.Completed => "completed",
            EQuestState.Failed => "failed",
            EQuestState.Expired => "failed",
            EQuestState.Cancelled => "failed",
            _ => "active",
        };
    }
}
