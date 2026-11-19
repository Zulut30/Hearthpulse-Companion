using Hearthpulse.Core.Battlegrounds;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Quests;

/// <summary>
/// Quests whose task progresses in a fight and whose reward acts at once when completed there (TASK-2026-11-19 E5; the
/// "at once" part is an assumption, see Effects/quests/Quests.cs). The local player's quest arrives as an extra with its
/// QUEST_PROGRESS, QUEST_PROGRESS_TOTAL and QUEST_REWARD_DATABASE_ID. Results are worked out by hand in each comment.
/// </summary>
public sealed class QuestTests
{
    private const int TurbulentTombsDbf = 104670;
    private const string Skeleton = "summoned our BG_ICC_026t";

    private static SimSide WithQuest(SimSide side, string questId, int progress, int total, int rewardDbf) => side with
    {
        Extras = [.. side.Extras, questId],
        ExtraTags = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [questId] = new Dictionary<string, int>
            {
                [SimInputs.QuestProgress] = progress,
                [SimInputs.QuestTotal] = total,
                [SimInputs.QuestReward] = rewardDbf,
            },
        },
    };

    /// <summary>Our 0/1 A, then a 0/1 Harmless Bonehead ("Deathrattle: Summon two 1/1 Skeletons"); neither attacks.</summary>
    private static SimSide Board() => Side(Minion(0, 1, cardId: "A"), Minion(0, 1, cardId: "BG28_300", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle));

    private static SimSide Killer() => Side(Minion(10, 100, cardId: "E"));

    [Fact]
    public void A_quest_completed_mid_fight_gives_its_reward_for_the_rest_of_it()
    {
        // Reenact the Murder at 2 of 3 friendly deaths, reward Turbulent Tombs ("Your Deathrattles trigger an extra time").
        // Their 10/100 kills A first (pick 0): the quest completes and the reward gets a card. The Bonehead dies next and
        // its deathrattle triggers twice: four Skeletons.
        var result = Fight(WithQuest(Board(), "BG24_Quest_124", 2, 3, TurbulentTombsDbf), Killer(), false);

        Assert.Equal(4, result.Lines(Skeleton).Count);
        Assert.Contains("we get a random card", result.Log);
    }

    [Fact]
    public void An_unfinished_quest_gives_nothing_yet()
    {
        // At 0 of 3 the Bonehead is the second death: no reward before it, two Skeletons.
        var result = Fight(WithQuest(Board(), "BG24_Quest_124", 0, 3, TurbulentTombsDbf), Killer(), false);
        Assert.Equal(2, result.Lines(Skeleton).Count);

        // A quest without a captured task (an older capture) does nothing.
        var untracked = Board() with { Extras = ["BG24_Quest_124"] };
        Assert.Equal(2, Fight(untracked, Killer(), false).Lines(Skeleton).Count);
    }

    [Fact]
    public void Round_up_the_suspects_counts_enemy_deaths()
    {
        // At 0 of 1 enemy deaths: our 5/5 kills their 1/1 S, the quest completes; their T then kills our 0/1 Bonehead,
        // whose deathrattle now triggers twice.
        var ours = WithQuest(Side(Minion(5, 5, cardId: "K"), Minion(0, 1, cardId: "BG28_300", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle)),
                             "BG27_Quest_802", 0, 1, TurbulentTombsDbf);
        var theirs = Side(Minion(1, 1, cardId: "S"), Minion(20, 100, cardId: "T"));
        var result = Fight(ours, theirs, true, null, 0, 1);

        Assert.Equal(4, result.Lines(Skeleton).Count);
    }

    [Fact]
    public void Crack_the_case_counts_attacks_and_a_reward_without_combat_effect_changes_nothing()
    {
        // At 0 of 1 attacks: our first attack completes it. The reward dbf names no combat reward (Secret Culprit, a Tavern
        // card): no extra trigger, two Skeletons.
        var ours = WithQuest(Side(Minion(1, 5, cardId: "K"), Minion(0, 1, cardId: "BG28_300", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle)),
                             "BG24_Quest_352", 0, 1, 110310);
        var result = Fight(ours, Side(Minion(20, 100, cardId: "T")), true, null, 0, 1);

        Assert.Equal(2, result.Lines(Skeleton).Count);
        Assert.DoesNotContain("we get a random card", result.Log);
    }
}
