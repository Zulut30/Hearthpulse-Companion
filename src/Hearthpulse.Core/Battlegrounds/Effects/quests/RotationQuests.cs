namespace Hearthpulse.Core.Battlegrounds.Effects.Quests;

// Quests out of today's pool (Hearthpulse API in_pool, 2026-11-19) whose task can progress in a fight, for when quests rotate
// back (TASK-2026-11-19 E6a). Text from HearthstoneJSON enUS latest (Last-Modified 2026-11-03); the task's number is the
// entity's QUEST_PROGRESS_TOTAL, the same assumptions as the quests in Quests.cs. No kept log has either quest.

/// <summary>
/// Exhume the Bones (BG24_Quest_320): "Quest: Trigger {N} friendly Deathrattles." Each friendly deathrattle trigger counts,
/// extra triggers (Titus Rivendare, Turbulent Tombs) included.
/// </summary>
[CardEffect("BG24_Quest_320")]
internal sealed class ExhumeTheBones : QuestEffect
{
    public override void OnFriendlyDeathrattle(Fight fight, Unit self, Unit source) => Progress(fight, self);
}

/// <summary>
/// Witness Protection (BG24_Quest_318): "Quest: Have a friendly Taunt minion attacked {N} times." Each attack declared on a
/// friendly minion that has Taunt at that moment counts.
/// </summary>
[CardEffect("BG24_Quest_318")]
internal sealed class WitnessProtection : QuestEffect
{
    public override void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker)
    {
        if (defender.Taunt)
        {
            Progress(fight, self);
        }
    }
}
