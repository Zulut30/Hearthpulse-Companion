using Hearthpulse.Core.Battlegrounds.Effects.Player;

namespace Hearthpulse.Core.Battlegrounds.Effects.Rewards;

// Quest rewards out of today's pool (Hearthpulse API in_pool, 2026-11-19) whose text acts in combat, for when quests
// rotate back (TASK-2026-11-19 E6a). English text from HearthstoneJSON enUS latest (Last-Modified 2026-11-03). No kept
// log has one of them (D:\Hearthstone\Logs, 2026-11-19). As the other rewards they arrive as extras of their side (both
// sides: a reward in play is public). A number the text shows is read from the card's captured TAG_SCRIPT_DATA_NUM_1/2
// when present (our own cards, GrowingValue), the text's number otherwise. "Your minions have +X": the captured minions
// already show the aura; minions summoned in combat get it too (as Feral Talisman).

/// <summary>Boom Squad (BG27_Reward_502): "Avenge (3): Deal 10 damage to the highest-Health enemy minion."</summary>
/// <remarks>
/// As De-volition-ist: the living enemy minion with the highest current Health, ties at random. X and the progress come from
/// SCORE_VALUE_1/2 (<see cref="PlayerAvengeEffect"/>); the 10 is the text's (an Avenge card's NUM_1 can hold its X).
/// </remarks>
[CardEffect("BG27_Reward_502")]
internal sealed class BoomSquad : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => 3;

    protected override void Avenge(Fight fight, Unit self)
    {
        if (fight.Best(fight.Alive(self.Enemy), u => u.Health, self) is { } victim)
        {
            fight.DealDamage(self, victim, 10);
        }
    }
}

/// <summary>Ritual Dagger (BG24_Reward_113): "After a friendly Deathrattle minion dies, give it +5/+5 permanently."</summary>
/// <remarks>
/// The dead minion gains the stats after its deathrattles resolved; a Reborn copy comes back as a fresh card, so in the fight
/// the gain shows only to effects that read a dead minion's stats or count stat gains.
/// </remarks>
[CardEffect("BG24_Reward_113")]
internal sealed class RitualDagger : CombatEffect
{
    private const string Id = "BG24_Reward_113";

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (dead.HasDeathrattle)
        {
            var (attack, health) = GrowingValue.Current(self.Side.Info, Id, 5, 5);
            fight.Buff(dead, attack, health);
        }
    }
}

/// <summary>Stable Amalgamation (BG28_Reward_518): "Avenge (7): When you have space, summon a 50/50 Amalgam."</summary>
/// <remarks>
/// The Amalgam is the Totally Normal Horse (BG28_Reward_518t, 50/50, all minion types). Each Avenge owes one (in
/// <see cref="Unit.Counter"/>); owed ones are summoned at the right end at once when there is space, else after a later
/// death phase that leaves space (as Boon of Beetles).
/// </remarks>
[CardEffect("BG28_Reward_518")]
internal sealed class StableAmalgamation : PlayerAvengeEffect
{
    private const string Horse = "BG28_Reward_518t";

    protected override int PrintedThreshold(Unit self) => 7;

    public override void OnFightStart(Fight fight, Unit self)
    {
        base.OnFightStart(fight, self);
        self.Counter = 0;
    }

    protected override void Avenge(Fight fight, Unit self)
    {
        self.Counter++;
        SummonWhileSpace(fight, self);
    }

    public override void AfterDeathPhase(Fight fight, Unit self) => SummonWhileSpace(fight, self);

    private static void SummonWhileSpace(Fight fight, Unit self)
    {
        var side = self.Side;
        while (self.Counter > 0 && !side.IsFull)
        {
            if (fight.Summon(side, Horse, 50, 50, tribes: Tribe.All) is null)
            {
                return;
            }

            self.Counter--;
        }
    }
}

/// <summary>The Smoking Gun (BG24_Reward_125): "Your minions have +4 Attack."</summary>
[CardEffect("BG24_Reward_125")]
internal sealed class TheSmokingGun : CombatEffect
{
    private const string Id = "BG24_Reward_125";

    public override void OnFightStart(Fight fight, Unit self) =>
        self.Side.AddSummonBuff(Tribe.All, GrowingValue.Current(self.Side.Info, Id, 4, 0).Attack, 0);
}

/// <summary>
/// Tumbling Disaster (BG28_Reward_505): "Whenever you summon a minion in combat, give it +4/+4. Avenge (4): Improve this
/// permanently."
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The current "+X/+Y" is the card's captured NUM_1/2 (it grows over the game). The opponent's is hidden from the
/// player: the printed +4/+4 stands in, listed as an estimate (Tracking/SimEstimates).</item>
/// <item>Assumption (no log, the text names no step): each Avenge improves it by +1/+1 for the rest of the fight.</item>
/// <item>Every minion summoned in the fight gets it, reborn copies included. X and the progress of Avenge come from
/// SCORE_VALUE_1/2 (<see cref="PlayerAvengeEffect"/>).</item>
/// </list>
/// </remarks>
[CardEffect("BG28_Reward_505")]
internal sealed class TumblingDisaster : PlayerAvengeEffect
{
    public const string Id = "BG28_Reward_505";

    protected override int PrintedThreshold(Unit self) => 4;

    public override bool HasInputs(SimSide side, string cardId) => GrowingValue.Known(side, cardId);

    public override void OnFightStart(Fight fight, Unit self)
    {
        base.OnFightStart(fight, self);
        (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, Id, 4, 4);
    }

    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned) => fight.Buff(summoned, self.Counter, self.Counter2);

    protected override void Avenge(Fight fight, Unit self)
    {
        self.Counter++;
        self.Counter2++;
    }
}

/// <summary>Volatile Venom (BG24_Reward_364): "Your minions have +7/+7. After they attack, they die. Horribly."</summary>
/// <remarks>
/// Every friendly minion that attacks is destroyed after the attack (before the death phase that follows it), whether it
/// survived the exchange or not; minions summoned in the fight get the +7/+7 too.
/// </remarks>
[CardEffect("BG24_Reward_364")]
internal sealed class VolatileVenom : CombatEffect
{
    private const string Id = "BG24_Reward_364";

    public override void OnFightStart(Fight fight, Unit self)
    {
        var (attack, health) = GrowingValue.Current(self.Side.Info, Id, 7, 7);
        self.Side.AddSummonBuff(Tribe.All, attack, health);
    }

    public override void AfterFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target, AttackOutcome outcome)
    {
        if (attacker.IsOnBoard)
        {
            fight.Destroy(attacker);
        }
    }
}
