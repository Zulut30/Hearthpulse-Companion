using Hearthpulse.Core.Battlegrounds.Effects.Deaths;

namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

/// <summary>
/// Timewarped Collector (BG34_Giant_680): "Also damages adjacent minions. Rally: If you control 4 Golden minions, gain
/// Divine Shield." Golden: same text.
/// </summary>
/// <remarks>
/// Cleave as Blade Collector: during its combat damage the target's neighbours take its Attack too and do not hit back.
/// The Rally runs when it attacks, before damage, so the Divine Shield already protects it in that attack; the four
/// Golden minions are the living friendly ones, itself included.
/// </remarks>
[CardEffect("BG34_Giant_680")]
internal sealed class TimewarpedCollector : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        var golden = 0;
        var side = self.Side;
        for (var i = 0; i < side.Count; i++)
        {
            if (side.Minions[i].IsAlive && side.Minions[i].Golden)
            {
                golden++;
            }
        }

        if (golden >= 4)
        {
            fight.GiveKeywords(self, Keyword.DivineShield);
        }
    }

    public override void OnAttackDamage(Fight fight, Unit self, Unit target)
    {
        var (left, right) = fight.Neighbours(target);
        if (left is not null) fight.DealDamage(self, left, self.Attack);
        if (right is not null) fight.DealDamage(self, right, self.Attack);
    }
}

/// <summary>
/// Timewarped Nest Swarmer (BG34_Giant_687): "Battlecry, Deathrattle, and Rally: Your Beetles have +2/+2 this game.
/// Summon a 2/2 Beetle." Golden: "+4/+4 … Summon two 2/2 Beetles."
/// </summary>
/// <remarks>
/// In text order, as Turquoise Skitterer: the bonus first (living Beetles, and Beetles summoned later this fight, the
/// ones this summons included), then the summon. The Beetle's base is <see cref="Beetles.Stats"/>: the summoner's
/// TAG_SCRIPT_DATA_NUM_1/2 when the log sends them (the player's current Beetle, as for the other Beetle summoners),
/// else a captured Beetle summoner's, else 2/2. Rally: to its right, before damage; Deathrattle: in its slot; a Battlecry
/// another effect triggers in combat (<see cref="Fight.TriggerBattlecry"/>): to its right.
/// </remarks>
[CardEffect("BG34_Giant_687")]
internal sealed class TimewarpedNestSwarmer : CombatEffect
{
    private static readonly Func<Unit, bool> IsBeetle = Beetles.IsBeetle;

    public override void OnAttack(Fight fight, Unit self, Unit target) => Act(fight, self);

    public override void Deathrattle(Fight fight, Unit self) => Act(fight, self);

    public override void Battlecry(Fight fight, Unit self) => Act(fight, self);

    private static void Act(Fight fight, Unit self)
    {
        var side = self.Side;
        var bonus = self.Pick(2, 4);
        for (var i = 0; i < side.Count; i++)
        {
            if (side.Minions[i].IsAlive && Beetles.IsBeetle(side.Minions[i]))
            {
                fight.Buff(side.Minions[i], bonus, bonus);
            }
        }

        side.AddSummonBuff(IsBeetle, bonus, bonus);
        var (attack, health) = Beetles.Stats(self);
        TimewarpedKit.Summon(fight, self, Tokens.Make(Tokens.BeetleId, attack, health, Tribe.Beast), self.Pick(1, 2));
    }
}

/// <summary>
/// Timewarped Pagle (BG34_Giant_208): "Once per combat, when this attacks and kills a minion, get a Triple Reward."
/// Golden: 2 Triple Rewards. The reward is a card for the Tavern: nothing changes in the fight.
/// </summary>
[CardEffect("BG34_Giant_208")]
internal sealed class TimewarpedPagle : CombatEffect
{
}
