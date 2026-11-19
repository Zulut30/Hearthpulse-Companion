namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Eternal Portrait (BG36_MagicItem_216): "Get a Golden Eternal Knight. After a friendly Undead dies, your Eternal Knights
/// have +4/+2 this game."
/// <para>
/// In combat, after each friendly Undead death (its deathrattles and Reborn resolved) every living friendly Eternal Knight
/// gets +4/+2, and an Eternal Knight summoned later this fight (a reborn copy, Eternal Summoner...) arrives with +4/+2 for
/// each such death so far. Bonuses from before the fight are in the captured stats; a summoned Knight's share of them is
/// what <see cref="Ongoing.EternalKnight"/> infers from the captured Knights (a normal Knight gains +4/+2 per event either
/// way). Assumption: +4/+2 for golden Knights too, as the text says (not +8/+4 like a dying Eternal Knight gives them).
/// The +4/+2 is the text's (HearthstoneJSON latest): the trinket's script data is not read, it may hold the bonus so far
/// "this game" rather than the bonus per death, and no log has the trinket.
/// </para>
/// </summary>
[CardEffect("BG36_MagicItem_216")]
internal sealed class EternalPortrait : CombatEffect
{
    private const int Attack = 4;
    private const int Health = 2;

    // Counter: friendly Undead deaths this fight.
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!dead.Is(Tribe.Undead))
        {
            return;
        }

        self.Counter++;
        var board = self.Side.Minions;
        for (var i = 0; i < board.Count; i++)
        {
            if (board[i] is { IsAlive: true } unit && unit.CardId == Tokens.EternalKnightId)
            {
                fight.Buff(unit, Attack, Health);
            }
        }
    }

    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (self.Counter > 0 && summoned.CardId == Tokens.EternalKnightId)
        {
            fight.Buff(summoned, Attack * self.Counter, Health * self.Counter);
        }
    }
}
