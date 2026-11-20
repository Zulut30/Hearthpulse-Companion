namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

// Timewarped minions out of today's pool (Hearthpulse API in_pool, 2026-11-19) that come back when the Timewarp Tavern
// rotates (TASK-2026-11-19 E6a). From the English text and golden text (HearthstoneJSON enUS latest, Last-Modified
// 2026-11-03). Their texts carry no {0}/{1}, so the numbers below are the card text; no kept log has one of them
// (D:\Hearthstone\Logs, 2026-11-19), so the orders and picks follow the text and the engine's general rules.
// Timewarped Festergut (BG34_Giant_590, "Summon and get a random Undead Creation") stays unimplemented: the card data has
// no Undead Creation parts to build one from.

/// <summary>
/// Timewarped Arm (BG34_Giant_027): "Whenever a friendly minion is attacked, give it +8 Attack permanently." Golden:
/// "+16 Attack".
/// </summary>
/// <remarks>Before damage (the attacked minion hits back with the new Attack); itself included.</remarks>
[CardEffect("BG34_Giant_027")]
internal sealed class TimewarpedArm : CombatEffect
{
    public override void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker) =>
        fight.Buff(defender, self.Pick(8, 16), 0);
}

/// <summary>
/// Timewarped Bristler (BG34_Giant_104): "Deathrattle: Give this minion's Blood Gems to 2 different friendly Quilboar."
/// Golden: "…, twice."
/// </summary>
/// <remarks>
/// "This minion's Blood Gems" are the stats it gained from Blood Gems (<see cref="Unit.BloodGemAttack"/> /
/// <see cref="Unit.BloodGemHealth"/>: the captured total plus gems played on it this fight). Each of 2 different random
/// living friendly Quilboar gains them, and they count as its own Blood Gems from then on. Golden: the whole effect twice,
/// with fresh picks. A Bristler without gem stats gives nothing.
/// </remarks>
[CardEffect("BG34_Giant_104")]
internal sealed class TimewarpedBristler : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var (attack, health) = (Math.Max(0, self.BloodGemAttack), Math.Max(0, self.BloodGemHealth));
        if (attack == 0 && health == 0)
        {
            return;
        }

        for (var time = 0; time < self.Pick(1, 2); time++)
        {
            foreach (var target in fight.RandomDistinct(fight.Alive(self.Side, Tribe.Quilboar, self), 2, self))
            {
                fight.Buff(target, attack, health);
                target.BloodGemAttack += attack;
                target.BloodGemHealth += health;
            }
        }
    }
}

/// <summary>
/// Timewarped Goldrinn (BG34_Giant_362): "Deathrattle: Your Beasts have +4/+4 this game (wherever they are)." Golden:
/// "+8/+8".
/// </summary>
/// <remarks>
/// As Goldrinn, the Great Wolf: living friendly Beasts get it now, Beasts summoned later this fight when they arrive. The
/// Beasts in the hand are not changed (no effect of the fight reads their stats after this).
/// </remarks>
[CardEffect("BG34_Giant_362")]
internal sealed class TimewarpedGoldrinn : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var bonus = self.Pick(4, 8);
        TimewarpedKit.BuffAll(fight, self.Side, Tribe.Beast, bonus, bonus);
        self.Side.AddSummonBuff(Tribe.Beast, bonus, bonus);
    }
}

/// <summary>
/// Timewarped Grease Bot (BG34_Giant_656): "Divine Shield. After a friendly minion loses Divine Shield, give your minions
/// +3/+3 permanently." Golden: "+6/+6".
/// </summary>
/// <remarks>Every living friendly minion, itself included; also when its own Divine Shield breaks.</remarks>
[CardEffect("BG34_Giant_656")]
internal sealed class TimewarpedGreaseBot : CombatEffect
{
    public override void OnFriendlyShieldLost(Fight fight, Unit self, Unit unit)
    {
        var bonus = self.Pick(3, 6);
        TimewarpedKit.BuffAll(fight, self.Side, Tribe.All, bonus, bonus);
    }
}

/// <summary>
/// Timewarped Guard (BG34_Giant_068): "Divine Shield. Rally: Give a different friendly minion Divine Shield
/// permanently." Golden: "Give 2 different friendly minions…".
/// </summary>
/// <remarks>Before damage, random living friendly minions other than itself (one already shielded can be picked).</remarks>
[CardEffect("BG34_Giant_068")]
internal sealed class TimewarpedGuard : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        foreach (var unit in fight.RandomDistinct(fight.Alive(self.Side, Tribe.All, self), self.Pick(1, 2), self))
        {
            fight.GiveKeywords(unit, Keyword.DivineShield);
        }
    }
}

/// <summary>
/// Timewarped Jelly Belly (BG34_Giant_024): "After a friendly minion is Reborn, give your minions +2/+2 permanently."
/// Golden: "+4/+4".
/// </summary>
/// <remarks>Every living friendly minion, the reborn one and itself included.</remarks>
[CardEffect("BG34_Giant_024")]
internal sealed class TimewarpedJellyBelly : CombatEffect
{
    public override void AfterFriendlyReborn(Fight fight, Unit self, Unit reborn)
    {
        var bonus = self.Pick(2, 4);
        TimewarpedKit.BuffAll(fight, self.Side, Tribe.All, bonus, bonus);
    }
}

/// <summary>
/// Timewarped Karathress (BG34_PreMadeChamp_056): "After you summon a minion in combat, get a copy of Deep Blues."
/// Golden: "get 2 copies".
/// </summary>
/// <remarks>
/// Deep Blues (BG26_502t) is a Tavern spell: in the fight it is a card added to the hand
/// (<see cref="Fight.AddToHand"/>), which "a card is added to your hand" effects see. Every summon counts, reborn copies
/// included, while the Karathress is alive.
/// </remarks>
[CardEffect("BG34_PreMadeChamp_056")]
internal sealed class TimewarpedKarathress : CombatEffect
{
    private const string DeepBlues = "BG26_502t";

    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            fight.AddToHand(self.Side, DeepBlues);
        }
    }
}

/// <summary>
/// Timewarped Leapfrogger (BG34_Giant_031): "Taunt, Reborn. Deathrattle: Give a friendly Beast +1/+1 and this
/// Deathrattle." Golden: "+2/+2".
/// </summary>
/// <remarks>
/// A random living friendly Beast gets the stats and the deathrattle for the rest of the fight
/// (<see cref="Unit.AddEffect"/>); a Beast that already has it gets it again and triggers both. The deathrattle passed on
/// is the one that triggered: a golden Leapfrogger's keeps +2/+2 on whatever carries it, so the normal and golden
/// versions are separate effect objects.
/// </remarks>
internal abstract class TimewarpedLeapfroggerDeathrattle : CombatEffect
{
    protected abstract int Bonus { get; }

    public override void Deathrattle(Fight fight, Unit self)
    {
        if (fight.RandomFriendly(self, Tribe.Beast) is { } beast)
        {
            fight.Buff(beast, Bonus, Bonus);
            beast.AddEffect(this);
        }
    }
}

/// <summary>Timewarped Leapfrogger, normal: +1/+1 (see <see cref="TimewarpedLeapfroggerDeathrattle"/>).</summary>
[CardEffect("BG34_Giant_031", Golden = GoldenFilter.NormalOnly)]
internal sealed class TimewarpedLeapfrogger : TimewarpedLeapfroggerDeathrattle
{
    protected override int Bonus => 1;
}

/// <summary>Timewarped Leapfrogger, golden: +2/+2 (see <see cref="TimewarpedLeapfroggerDeathrattle"/>).</summary>
[CardEffect("BG34_Giant_031", Golden = GoldenFilter.GoldenOnly)]
internal sealed class TimewarpedGoldenLeapfrogger : TimewarpedLeapfroggerDeathrattle
{
    protected override int Bonus => 2;
}

/// <summary>
/// Timewarped Magnanimoose (BG34_Giant_619): "Deathrattle: Summon and get a minion from a random opponent's warband."
/// Golden: "…, twice."
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>"A random opponent" is any player of the lobby; the fight knows only the one it is fought against. The estimate
/// (<see cref="SimInputs.OpponentWarband"/>, listed in the side's estimates by the tracker): this fight's opponent, a
/// random minion of their board as captured at the start of combat (its stats and keywords then).</item>
/// <item>The copy is summoned in the Magnanimoose's slot and a copy goes to the hand. Golden: twice, fresh picks.</item>
/// </list>
/// </remarks>
[CardEffect("BG34_Giant_619")]
internal sealed class TimewarpedMagnanimoose : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var warband = self.Enemy.Info.Minions;
        if (warband.Count == 0)
        {
            return;
        }

        var anchor = self;
        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            var copy = Copy(warband[fight.Choose(ChoiceKind.RandomSummon, self, warband)]);
            if (fight.SummonAt(anchor.IsOnBoard && anchor != self ? anchor : self, copy) is { } summoned)
            {
                anchor = summoned;
            }

            fight.AddToHand(self.Side, copy.CardId, copy);
        }
    }

    private static SimMinion Copy(SimMinion minion) =>
        minion with
        {
            EntityId = 0,
            HasUnsimulatedEffect = false,
            Health = minion.Health > 0 ? minion.Health : Math.Max(1, minion.MaxHealth),
        };
}

/// <summary>
/// Timewarped Spirit of Air (BG34_Giant_592): "Deathrattle: Give a random friendly minion Windfury, Divine Shield, and
/// Taunt." Golden: "Give 2 random friendly minions…".
/// </summary>
[CardEffect("BG34_Giant_592")]
internal sealed class TimewarpedSpiritOfAir : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        foreach (var unit in fight.RandomDistinct(fight.Alive(self.Side, Tribe.All, self), self.Pick(1, 2), self))
        {
            fight.GiveKeywords(unit, Keyword.Windfury | Keyword.DivineShield | Keyword.Taunt);
        }
    }
}

/// <summary>
/// Timewarped Trickster (BG34_Giant_010): "Deathrattle: Give this minion's maximum stats to another friendly minion."
/// Golden: "…, twice."
/// </summary>
/// <remarks>
/// Its Attack and maximum Health when it died go to a random living friendly minion. Golden: twice, each a fresh pick
/// (the same minion can get both).
/// </remarks>
[CardEffect("BG34_Giant_010")]
internal sealed class TimewarpedTrickster : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var (attack, health) = (Math.Max(0, self.Attack), Math.Max(0, self.MaxHealth));
        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            if (fight.RandomFriendly(self) is { } target)
            {
                fight.Buff(target, attack, health);
            }
        }
    }
}

/// <summary>
/// Timewarped Ultralisk (BG34_Treasure_994): "Also damages adjacent minions. Start of Combat: Double this minion's
/// stats." Golden: "Triple this minion's stats."
/// </summary>
/// <remarks>
/// Start of Combat resolved before the capture (the captured stats already show it); one summoned in the fight has no
/// Start of Combat. Cleave as Blade Collector: the target's neighbours take its Attack and do not hit back.
/// </remarks>
[CardEffect("BG34_Treasure_994")]
internal sealed class TimewarpedUltralisk : CombatEffect
{
    public override void OnAttackDamage(Fight fight, Unit self, Unit target)
    {
        var (left, right) = fight.Neighbours(target);
        if (left is not null) fight.DealDamage(self, left, self.Attack);
        if (right is not null) fight.DealDamage(self, right, self.Attack);
    }
}
